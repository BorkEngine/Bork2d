using System.Numerics;
using System.Runtime.InteropServices;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Shaders;
using Bork2d.Render.Sprites;
using Bork2d.Render.Textures;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// The render-thread-owned, allocation-free unified 2D frame planner (sprites and lines).
/// </summary>
/// <remarks>
/// <para>
/// One call to <see cref="Plan"/> resolves the whole consumed snapshot exactly once:
/// </para>
/// <list type="number">
/// <item>
/// Pre-pass every sprite and line once: skip classification, malformed sprite-field validation,
/// shader binding resolution, a sprite's declared additional textures, the sprite's camera-
/// independent instance record, and the line's resolved entry and seeded uniform block.
/// </item>
/// <item>Sort camera indices by <c>ZIndex</c>, resolve each projection, and skip invalid cameras.</item>
/// <item>
/// Per camera, build the visible candidate list (layer + conservative bounds), project each record's
/// world-space origin to that camera's framebuffer pixels, sort the candidates by
/// <c>(ZIndex, screen Y, screen X)</c>, and group the sorted records into contiguous
/// <see cref="DrawRun"/>s. A run changes only when two adjacent visible records differ in
/// <see cref="DrawKind"/> or resolved shader entry.
/// </item>
/// <item>
/// Validate the accumulated frame totals against <see cref="int.MaxValue"/>, write one 144-byte global
/// block per run, grow the instance/line buffers once, and write the precomputed sprite records and
/// the world-transformed line vertices/indices in draw order.
/// </item>
/// </list>
/// <para>
/// Draw order is <c>ZIndex</c> ascending; for equal <c>ZIndex</c> it is per-camera screen-space Y
/// ascending in framebuffer pixels (top-left origin, so a record lower on screen draws later), then
/// screen X ascending. <c>Layer</c> is only a camera culling bit and never participates in ordering,
/// and <c>ZIndex</c> is not layer-scoped.
/// </para>
/// <para>
/// A custom shader that is still loading skips the record for the frame. A custom shader that failed
/// validation, loading, module/pipeline creation, or conflicts with an engine-wide global name draws
/// with the requesting kind's default entry instead and is not skipped. Additional textures resolve
/// positionally against a sprite shader's declared slots; pending extra page skips the whole sprite,
/// a failed or absent page uses the bindless placeholder index <c>0</c>, and paths beyond the declared
/// slot count are never loaded. Lines have no main or additional textures.
/// </para>
/// <para>
/// Bounds culling is always on and conservative: a record is skipped for a camera only when its
/// non-empty world AABB does not intersect the camera's world AABB. Empty bounds mean "unknown", so
/// they never cull. A record is counted as culled once per frame when it matched the layer mask of at
/// least one planned camera and no planned camera emitted it.
/// </para>
/// <para>
/// Every per-record, per-run, and per-frame array is renderer-owned and preallocated; after warm-up
/// the plan allocates nothing. Diagnostics are throttled: invalid-camera reasons, invalid sprite
/// layers, invalid line layers, and missing line geometry log once per renderer, a non-Quad asset logs
/// once per asset path, and a shader global-type mismatch logs once per shader path. Malformed sprite
/// fields throw a fatal <see cref="Bork2dException"/> naming the field and texture reference; they are
/// never silently repaired. Frame totals accumulate in <see cref="long"/> and are validated before
/// they are narrowed, so a multi-camera frame faults instead of overflowing.
/// </para>
/// </remarks>
internal sealed class TwoDimensionalFramePlanner
{
    /// <summary>The maximum number of additional textures a shader may sample per sprite.</summary>
    internal const int MaxExtraTextures = ShaderValidation.MaxTextureSlots;

    private const int MinimumCapacity = 16;

    // Per-record bounds visibility for the current plan: not a layer candidate, a candidate that no
    // planned camera emitted, or emitted by at least one camera. The culled counters count the
    // middle state once per record.
    private const byte VisibilityNone = 0;
    private const byte VisibilityCandidate = 1;
    private const byte VisibilityDrawn = 2;

    private readonly TexturePageStore _pages;
    private readonly IShaderFrameSource _shaders;
    private readonly ILogger _logger;
    private readonly HashSet<string> _loggedNonQuadAssets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loggedGlobalTypeMismatches = new(StringComparer.Ordinal);
    private readonly TwoDimensionalFramePlan _plan = new();
    private readonly ShaderEntry?[] _defaultEntries = new ShaderEntry?[2];
    private SpriteInstance[] _spriteInstances = [];
    private int[] _spriteEntryIds = [];
    private bool[] _spriteDrawable = [];
    private byte[] _spriteVisibility = [];
    private LineGeometryData?[] _lineGeometries = [];
    private int[] _lineEntryIds = [];
    private bool[] _lineDrawable = [];
    private byte[] _lineVisibility = [];
    private Vector4[] _lineParams0 = [];
    private Vector4[] _lineParams1 = [];
    private Vector4[] _lineParams2 = [];
    private Vector4[] _lineParams3 = [];
    private TwoDimensionalSortKey[] _sortKeys = [];
    private VisibleRecord[] _visibleRecords = [];
    private DrawRun[] _runs = [];
    private int[] _cameraIndices = [];
    private CameraDrawPlan[] _cameras = [];
    private uint _loggedCameraReasons;
    private bool _loggedInvalidSpriteLayer;
    private bool _loggedInvalidLineLayer;
    private bool _loggedLineGeometry;
    private bool _loggedDepthClear;
    private bool _loggedExtraTextureOverflow;

    /// <summary>Initializes a new instance of the <see cref="TwoDimensionalFramePlanner"/> class.</summary>
    /// <param name="pages">The texture-page cache the planner resolves and prefetches through.</param>
    /// <param name="shaders">The shader catalog the planner resolves captured bindings through.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger for throttled diagnostics.</param>
    internal TwoDimensionalFramePlanner(
        TexturePageStore pages,
        IShaderFrameSource shaders,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(logger);
        _pages = pages;
        _shaders = shaders;
        _logger = logger;
    }

    /// <summary>
    /// Plans one snapshot, writes one global uniform block per draw run, and writes its sprite
    /// instances and line vertices/indices into the frame buffers.
    /// </summary>
    /// <param name="snapshot">The consumed snapshot; read-only and never mutated.</param>
    /// <param name="framebufferWidth">The target/framebuffer width in pixels.</param>
    /// <param name="framebufferHeight">The target/framebuffer height in pixels.</param>
    /// <param name="instances">The frame's sprite instance sink, after its fence has been observed.</param>
    /// <param name="lines">The frame's line vertex/index sink, after its fence has been observed.</param>
    /// <param name="globals">
    /// The frame's global uniform sink. Exactly one 144-byte block is written per draw run with the
    /// frame time in <c>time.x</c>, the declared engine-wide user values for that run's shader, and
    /// each declaration's shader default where the engine has no matching name and type.
    /// </param>
    /// <param name="sceneFormat">The linear scene color format the 2D pipelines are created for.</param>
    /// <returns>The refilled plan; valid until the next <see cref="Plan"/> call.</returns>
    /// <exception cref="Bork2dException">
    /// A sprite field is malformed, a shader entry or pipeline invariant is broken, the accumulated
    /// frame total exceeds <see cref="int.MaxValue"/>, or a frame buffer cannot hold the frame.
    /// </exception>
    internal TwoDimensionalFramePlan Plan(
        FrameSnapshot snapshot,
        int framebufferWidth,
        int framebufferHeight,
        IInstanceBuffer instances,
        ILineBufferSink lines,
        IGlobalUniformBuffer globals,
        Format sceneFormat)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(globals);

        ReadOnlySpan<SpriteSnapshot> sprites = snapshot.Sprites;
        ReadOnlySpan<LineSnapshot> lineRecords = snapshot.Lines;
        ReadOnlySpan<GlobalUniformSnapshot> globalUniforms = snapshot.GlobalUniforms;
        int spriteCount = sprites.Length;
        int lineCount = lineRecords.Length;
        int recordCount = spriteCount + lineCount;
        _defaultEntries[0] = null;
        _defaultEntries[1] = null;
        EnsureSpriteArrays(spriteCount);
        EnsureLineArrays(lineCount);
        EnsureSortKeyCapacity(recordCount);
        EnsureVisibleRecordCapacity(recordCount);

        long skippedSprites = 0;
        for (var index = 0; index < spriteCount; index++)
        {
            SpriteSkipReason reason = ResolveSprite(
                sprites[index],
                index,
                sceneFormat,
                globalUniforms,
                out SpriteInstance instance);
            if (reason != SpriteSkipReason.None)
            {
                _spriteDrawable[index] = false;
                skippedSprites++;
                continue;
            }

            _spriteInstances[index] = instance;
            _spriteDrawable[index] = true;
        }

        long skippedLines = 0;
        for (var index = 0; index < lineCount; index++)
        {
            LineSkipReason reason = ResolveLine(
                lineRecords[index],
                index,
                sceneFormat,
                globalUniforms);
            _lineDrawable[index] = reason == LineSkipReason.None;
            if (!_lineDrawable[index])
            {
                skippedLines++;
            }
        }

        _spriteVisibility.AsSpan(0, spriteCount).Clear();
        _lineVisibility.AsSpan(0, lineCount).Clear();

        ReadOnlySpan<CameraSnapshot> cameras = snapshot.Cameras;
        int cameraCount = cameras.Length;
        EnsureCameraArrays(cameraCount);
        Span<int> cameraOrder = _cameraIndices.AsSpan(0, cameraCount);
        TwoDimensionalPlanning.SortCameras(cameras, cameraOrder);

        var framebufferSize = new Vector2(framebufferWidth, framebufferHeight);
        var cameraPlanCount = 0;
        for (var index = 0; index < cameraCount; index++)
        {
            int cameraIndex = cameraOrder[index];
            CameraSnapshot camera = cameras[cameraIndex];
            if (!TwoDimensionalCameraProjection.TryCreate(
                camera,
                framebufferSize,
                out CameraProjectionResult projection))
            {
                LogInvalidCamera(cameraIndex, projection.Reason);
                continue;
            }

            if (camera.Clear == ClearKind.Depth)
            {
                LogDepthClear();
            }

            _cameras[cameraPlanCount++] = new CameraDrawPlan(
                projection,
                projection.CreateWorldBounds(),
                camera.Clear,
                camera.ClearColor,
                camera.CullingMask,
                FirstInstance: 0,
                InstanceCount: 0,
                FirstVertex: 0,
                VertexCount: 0,
                FirstIndex: 0,
                IndexCount: 0,
                FirstRun: 0,
                RunCount: 0);
        }

        var visibleCount = 0;
        var runIndex = 0;
        var nonEmptyCameras = 0;
        var instanceCursor = 0;
        var vertexCursor = 0;
        var indexCursor = 0;
        long frameInstanceCount = 0;
        long frameLineCount = 0;
        long frameVertexCount = 0;
        long frameIndexCount = 0;
        for (var cameraIndex = 0; cameraIndex < cameraPlanCount; cameraIndex++)
        {
            CameraDrawPlan camera = _cameras[cameraIndex];
            int candidateCount = BuildCandidates(camera, sprites, lineRecords);
            Span<TwoDimensionalSortKey> candidates = _sortKeys.AsSpan(0, candidateCount);
            candidates.Sort();

            int cameraFirstInstance = instanceCursor;
            int cameraFirstVertex = vertexCursor;
            int cameraFirstIndex = indexCursor;
            int cameraFirstRun = runIndex;
            var cameraLineCount = 0;
            var runActive = false;
            var runKind = DrawKind.Sprite;
            var runEntry = -1;
            var runFirst = 0;
            var runAccumulated = 0;
            for (var position = 0; position < candidates.Length; position++)
            {
                TwoDimensionalSortKey key = candidates[position];
                int recordIndex = key.RecordIndex;
                int entryId = key.Kind == DrawKind.Sprite
                    ? _spriteEntryIds[recordIndex]
                    : _lineEntryIds[recordIndex];
                if (!runActive || key.Kind != runKind || entryId != runEntry)
                {
                    if (runActive)
                    {
                        EnsureRunCapacity(runIndex + 1);
                        _runs[runIndex++] = new DrawRun(
                            runKind,
                            runEntry,
                            runFirst,
                            runAccumulated,
                            GlobalBlockOffset: 0);
                    }

                    runActive = true;
                    runKind = key.Kind;
                    runEntry = entryId;
                    runFirst = key.Kind == DrawKind.Sprite ? instanceCursor : indexCursor;
                    runAccumulated = 0;
                }

                if (key.Kind == DrawKind.Sprite)
                {
                    _spriteVisibility[recordIndex] = VisibilityDrawn;
                    EnsureVisibleRecordCapacity(visibleCount + 1);
                    _visibleRecords[visibleCount++] = new VisibleRecord(DrawKind.Sprite, recordIndex);
                    instanceCursor++;
                    runAccumulated++;
                }
                else
                {
                    LineGeometryData geometry = _lineGeometries[recordIndex]!;
                    _lineVisibility[recordIndex] = VisibilityDrawn;
                    EnsureVisibleRecordCapacity(visibleCount + 1);
                    _visibleRecords[visibleCount++] = new VisibleRecord(DrawKind.Line, recordIndex);
                    cameraLineCount++;
                    vertexCursor += geometry.VertexCount;
                    indexCursor += geometry.IndexCount;
                    runAccumulated += geometry.IndexCount;
                }
            }

            if (runActive)
            {
                EnsureRunCapacity(runIndex + 1);
                _runs[runIndex++] = new DrawRun(
                    runKind,
                    runEntry,
                    runFirst,
                    runAccumulated,
                    GlobalBlockOffset: 0);
            }

            int cameraRunCount = runIndex - cameraFirstRun;
            int cameraInstanceCount = instanceCursor - cameraFirstInstance;
            int cameraVertexCount = vertexCursor - cameraFirstVertex;
            int cameraIndexCount = indexCursor - cameraFirstIndex;
            _cameras[cameraIndex] = camera with
            {
                FirstInstance = cameraFirstInstance,
                InstanceCount = cameraInstanceCount,
                FirstVertex = cameraFirstVertex,
                VertexCount = cameraVertexCount,
                FirstIndex = cameraFirstIndex,
                IndexCount = cameraIndexCount,
                FirstRun = cameraFirstRun,
                RunCount = cameraRunCount,
            };
            frameInstanceCount += cameraInstanceCount;
            frameLineCount += cameraLineCount;
            frameVertexCount += cameraVertexCount;
            frameIndexCount += cameraIndexCount;
            _ = ValidateInstanceCount(frameInstanceCount);
            if (cameraRunCount > 0)
            {
                nonEmptyCameras++;
            }
        }

        int instanceCount = ValidateInstanceCount(frameInstanceCount);
        int lineCountTotal = ValidateFrameCount(frameLineCount, "line records");
        int vertexCount = ValidateFrameCount(frameVertexCount, "line vertices");
        int indexCount = ValidateFrameCount(frameIndexCount, "line indices");

        var culledSprites = 0;
        for (var index = 0; index < spriteCount; index++)
        {
            if (_spriteDrawable[index] && _spriteVisibility[index] == VisibilityCandidate)
            {
                culledSprites++;
            }
        }

        var culledLines = 0;
        for (var index = 0; index < lineCount; index++)
        {
            if (_lineDrawable[index] && _lineVisibility[index] == VisibilityCandidate)
            {
                culledLines++;
            }
        }

        globals.EnsureCapacity(runIndex);
        for (var position = 0; position < runIndex; position++)
        {
            DrawRun run = _runs[position];
            ShaderEntry entry = _shaders.GetEntry(run.ShaderEntryId);
            GlobalUniformBlock block = CreateGlobalBlock(snapshot, globalUniforms, entry);
            globals.WriteBlock(position, in block);
            _runs[position] = run with
            {
                GlobalBlockOffset = (uint)(position * globals.BlockStride),
            };
        }

        if (instanceCount > 0)
        {
            instances.EnsureCapacity(instanceCount);
        }

        lines.EnsureCapacity(vertexCount, indexCount);
        WriteBuffers(visibleCount, lineRecords, instances, lines);

        _plan.CameraCount = cameraCount;
        _plan.CameraPlanCount = cameraPlanCount;
        _plan.DrawCalls = runIndex;
        _plan.NonEmptyCameras = nonEmptyCameras;
        _plan.InstanceCount = instanceCount;
        _plan.LineCount = lineCountTotal;
        _plan.LineVertexCount = vertexCount;
        _plan.LineIndexCount = indexCount;
        _plan.RunCount = runIndex;
        _plan.SkippedSprites = skippedSprites;
        _plan.SkippedLines = skippedLines;
        _plan.CulledSprites = culledSprites;
        _plan.CulledLines = culledLines;
        _plan.Cameras = _cameras;
        _plan.Runs = _runs;
        return _plan;
    }

    /// <summary>
    /// Validates a frame's accumulated instance total before it is narrowed to the buffer's index
    /// type.
    /// </summary>
    /// <remarks>
    /// The total is accumulated in <see cref="long"/> because a multi-camera frame can exceed the
    /// <see cref="int"/> range even though each camera's count fits. Callers falsify this path with
    /// the count directly instead of trying to plan billions of instances.
    /// </remarks>
    /// <param name="instanceCount">The frame instance total accumulated in <see cref="long"/>.</param>
    /// <returns>The same total as an <see cref="int"/>, after the bound check.</returns>
    /// <exception cref="Bork2dException">
    /// The total exceeds <see cref="int.MaxValue"/> records, which no instance buffer can address.
    /// </exception>
    internal static int ValidateInstanceCount(long instanceCount)
    {
        if (instanceCount > int.MaxValue)
        {
            throw CreateInstanceCountException(instanceCount);
        }

        return (int)instanceCount;
    }

    private static Bork2dException CreateInstanceCountException(long instanceCount)
        => new(
            $"The frame requires {instanceCount} sprite instances, which exceeds the "
                + $"{int.MaxValue}-record maximum the instance buffer can address. No instance "
                + "is truncated.");

    private static int ValidateFrameCount(long count, string description)
    {
        if (count > int.MaxValue)
        {
            throw new Bork2dException(
                $"The frame requires {count} {description}, which exceeds the {int.MaxValue}-record "
                    + "maximum a frame buffer can address. No record is truncated.");
        }

        return (int)count;
    }

    private static GlobalUniformBlock CreateGlobalBlock(
        FrameSnapshot snapshot,
        ReadOnlySpan<GlobalUniformSnapshot> globalUniforms,
        ShaderEntry entry)
    {
        var block = new GlobalUniformBlock
        {
            Time = new Vector4((float)snapshot.Time, 0f, 0f, 0f),
        };
        Span<Vector4> lanes = MemoryMarshal.CreateSpan(
            ref block.Global0,
            GlobalUniformBlock.GlobalSlotCount);
        foreach (ShaderUniform declaration in entry.Metadata.GlobalUniforms)
        {
            Vector4 value = declaration.Default;
            for (var index = 0; index < globalUniforms.Length; index++)
            {
                if (!string.Equals(
                    globalUniforms[index].Name,
                    declaration.Name,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                // A type mismatch is never reinterpreted: the shader declaration's own default
                // applies, and the planner has already logged/fallen back for the custom entry.
                if (globalUniforms[index].Type == declaration.Type)
                {
                    value = globalUniforms[index].Value;
                }

                break;
            }

            ShaderLayout.WriteUniform(lanes, in declaration, value);
        }

        return block;
    }

    private static bool IsGlobalCompatible(
        ShaderEntry entry,
        ReadOnlySpan<GlobalUniformSnapshot> globalUniforms)
    {
        foreach (ShaderUniform declaration in entry.Metadata.GlobalUniforms)
        {
            for (var index = 0; index < globalUniforms.Length; index++)
            {
                if (string.Equals(
                    globalUniforms[index].Name,
                    declaration.Name,
                    StringComparison.Ordinal)
                    && globalUniforms[index].Type != declaration.Type)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static AssetRef<TextureDescriptor> GetExtraTextureReference(
        in SpriteSnapshot sprite,
        int slot)
        => slot switch
        {
            0 => sprite.ExtraTexture0,
            1 => sprite.ExtraTexture1,
            2 => sprite.ExtraTexture2,
            _ => sprite.ExtraTexture3,
        };

    private static void SetComponent(ref Vector4 value, int component, float componentValue)
    {
        switch (component)
        {
            case 0:
                value.X = componentValue;
                break;

            case 1:
                value.Y = componentValue;
                break;

            case 2:
                value.Z = componentValue;
                break;

            default:
                value.W = componentValue;
                break;
        }
    }

    private int BuildCandidates(
        in CameraDrawPlan camera,
        ReadOnlySpan<SpriteSnapshot> sprites,
        ReadOnlySpan<LineSnapshot> lineRecords)
    {
        Span<TwoDimensionalSortKey> keys = _sortKeys;
        var count = 0;
        for (var index = 0; index < sprites.Length; index++)
        {
            if (!_spriteDrawable[index])
            {
                continue;
            }

            SpriteSnapshot sprite = sprites[index];
            if (!TwoDimensionalPlanning.LayerMatches(camera.CullingMask, sprite.Layer))
            {
                continue;
            }

            if (!TwoDimensionalPlanning.BoundsIntersect(camera.WorldBounds, sprite.Bounds))
            {
                MarkCandidate(_spriteVisibility, index);
                continue;
            }

            Vector2 worldOrigin = new(sprite.WorldMatrix.M31, sprite.WorldMatrix.M32);
            Vector2 screen = CameraProjection.WorldToFramebuffer(camera.Projection, worldOrigin);
            keys[count++] = new TwoDimensionalSortKey(
                sprite.ZIndex,
                screen.Y,
                screen.X,
                DrawKind.Sprite,
                index);
        }

        for (var index = 0; index < lineRecords.Length; index++)
        {
            if (!_lineDrawable[index])
            {
                continue;
            }

            LineSnapshot line = lineRecords[index];
            if (!TwoDimensionalPlanning.LayerMatches(camera.CullingMask, line.Layer))
            {
                continue;
            }

            if (!TwoDimensionalPlanning.BoundsIntersect(camera.WorldBounds, line.Bounds))
            {
                MarkCandidate(_lineVisibility, index);
                continue;
            }

            Vector2 worldOrigin = new(line.WorldMatrix.M31, line.WorldMatrix.M32);
            Vector2 screen = CameraProjection.WorldToFramebuffer(camera.Projection, worldOrigin);
            keys[count++] = new TwoDimensionalSortKey(
                line.ZIndex,
                screen.Y,
                screen.X,
                DrawKind.Line,
                index);
        }

        return count;
    }

    private void WriteBuffers(
        int visibleCount,
        ReadOnlySpan<LineSnapshot> lineRecords,
        IInstanceBuffer instances,
        ILineBufferSink lines)
    {
        var instanceIndex = 0;
        var vertexIndex = 0;
        var indexIndex = 0;
        for (var position = 0; position < visibleCount; position++)
        {
            VisibleRecord record = _visibleRecords[position];
            if (record.Kind == DrawKind.Sprite)
            {
                instances.WriteInstance(instanceIndex++, in _spriteInstances[record.Index]);
                continue;
            }

            WriteLineGeometry(
                record.Index,
                lineRecords[record.Index],
                lines,
                ref vertexIndex,
                ref indexIndex);
        }
    }

    private void WriteLineGeometry(
        int lineIndex,
        in LineSnapshot line,
        ILineBufferSink lines,
        ref int vertexIndex,
        ref int indexIndex)
    {
        LineGeometryData geometry = _lineGeometries[lineIndex]!;
        ReadOnlySpan<Vector2> positions = geometry.Vertices.Span;
        ReadOnlySpan<Vector2> lineUvs = geometry.LineUvs.Span;
        ReadOnlySpan<uint> indices = geometry.Indices.Span;

        Matrix3x2 world = line.WorldMatrix;
        Vector4 color = line.Color;
        var misc = new Vector4(line.AntiAlias ? 1f : 0f, 0f, 0f, 0f);
        var firstVertex = (uint)vertexIndex;
        for (var index = 0; index < positions.Length; index++)
        {
            var vertex = new LineVertex
            {
                Position = Vector2.Transform(positions[index], world),
                LineUv = lineUvs[index],
                Color = color,
                Misc = misc,
                Params0 = _lineParams0[lineIndex],
                Params1 = _lineParams1[lineIndex],
                Params2 = _lineParams2[lineIndex],
                Params3 = _lineParams3[lineIndex],
            };
            lines.WriteVertex(vertexIndex + index, in vertex);
        }

        for (var index = 0; index < indices.Length; index++)
        {
            lines.WriteIndex(indexIndex + index, indices[index] + firstVertex);
        }

        vertexIndex += positions.Length;
        indexIndex += indices.Length;
    }

    private SpriteSkipReason ResolveSprite(
        in SpriteSnapshot sprite,
        int spriteIndex,
        Format sceneFormat,
        ReadOnlySpan<GlobalUniformSnapshot> globalUniforms,
        out SpriteInstance instance)
    {
        instance = default;
        SpriteSkipReason reason = TwoDimensionalPlanning.Classify(sprite, texturePending: false);
        switch (reason)
        {
            case SpriteSkipReason.InvalidLayer:
                LogInvalidSpriteLayer(sprite.Layer);
                return reason;

            case SpriteSkipReason.NonQuad:
                LogNonQuad(sprite.SpriteData!);
                return reason;

            case SpriteSkipReason.Null:
                return reason;

            default:
                break;
        }

        SpriteRenderData data = sprite.SpriteData!;

        // Validate the frozen geometry before any pending skip: the spec's malformed-field rule is
        // unconditional, so a malformed sprite is fatal even while a page or shader is loading.
        SpriteQuad quad = SpriteQuad.Resolve(data, sprite.FlipX, sprite.FlipY);

        ShaderEntry entry;
        if (sprite.ShaderData is null)
        {
            entry = GetDefaultEntry(ShaderPipelineKind.Sprite, sceneFormat);
        }
        else
        {
            ShaderBinding binding = _shaders.Resolve(
                sprite.ShaderData,
                ShaderPipelineKind.Sprite,
                sceneFormat);
            if (binding.State == ShaderBindingState.Pending)
            {
                return SpriteSkipReason.Pending;
            }

            entry = binding.State == ShaderBindingState.Failed
                ? GetDefaultEntry(ShaderPipelineKind.Sprite, sceneFormat)
                : _shaders.GetEntry(binding.EntryId);
        }

        entry = ApplyGlobalCompatibility(
            entry,
            globalUniforms,
            ShaderPipelineKind.Sprite,
            sceneFormat);

        TexturePageLookup lookup = _pages.EnsurePage(data.Texture.Path);
        if (lookup.Kind == TexturePageLookupKind.Pending)
        {
            return SpriteSkipReason.Pending;
        }

        if (!TryResolveExtraTextures(sprite, entry, out Vector4 extraTextureIndices))
        {
            return SpriteSkipReason.Pending;
        }

        SpriteSnapshot effective = SeedDefaultUniforms(sprite, entry);
        instance = SpriteInstanceBuilder.Create(
            effective,
            quad,
            (uint)lookup.Index,
            extraTextureIndices);
        _spriteEntryIds[spriteIndex] = entry.Id;
        return SpriteSkipReason.None;
    }

    private LineSkipReason ResolveLine(
        in LineSnapshot line,
        int lineIndex,
        Format sceneFormat,
        ReadOnlySpan<GlobalUniformSnapshot> globalUniforms)
    {
        _lineGeometries[lineIndex] = null;
        _lineEntryIds[lineIndex] = -1;
        LineSkipReason reason = TwoDimensionalPlanning.ClassifyLine(line, shaderPending: false);
        switch (reason)
        {
            case LineSkipReason.NullGeometry:
                LogLineGeometry();
                return reason;

            case LineSkipReason.InvalidLayer:
                LogInvalidLineLayer(line.Layer);
                return reason;

            default:
                break;
        }

        ShaderEntry entry;
        if (line.ShaderData is null)
        {
            entry = GetDefaultEntry(ShaderPipelineKind.Line, sceneFormat);
        }
        else
        {
            ShaderBinding binding = _shaders.Resolve(
                line.ShaderData,
                ShaderPipelineKind.Line,
                sceneFormat);
            if (binding.State == ShaderBindingState.Pending)
            {
                return LineSkipReason.Pending;
            }

            entry = binding.State == ShaderBindingState.Failed
                ? GetDefaultEntry(ShaderPipelineKind.Line, sceneFormat)
                : _shaders.GetEntry(binding.EntryId);
        }

        entry = ApplyGlobalCompatibility(
            entry,
            globalUniforms,
            ShaderPipelineKind.Line,
            sceneFormat);

        _lineGeometries[lineIndex] = line.Geometry;
        _lineEntryIds[lineIndex] = entry.Id;
        SeedLineUniforms(line, entry, lineIndex);
        return LineSkipReason.None;
    }

    private ShaderEntry GetDefaultEntry(ShaderPipelineKind kind, Format sceneFormat)
    {
        ShaderEntry? cached = _defaultEntries[(int)kind];
        if (cached is not null)
        {
            return cached;
        }

        ShaderBinding binding = _shaders.Resolve(null, kind, sceneFormat);
        ShaderEntry entry = _shaders.GetEntry(binding.EntryId);
        _defaultEntries[(int)kind] = entry;
        return entry;
    }

    private ShaderEntry ApplyGlobalCompatibility(
        ShaderEntry entry,
        ReadOnlySpan<GlobalUniformSnapshot> globalUniforms,
        ShaderPipelineKind kind,
        Format sceneFormat)
    {
        if (IsGlobalCompatible(entry, globalUniforms))
        {
            return entry;
        }

        LogGlobalTypeMismatch(entry);
        if (entry.IsDefault)
        {
            // The default shader has no fallback; its declared defaults apply to the mismatched
            // name and the engine value is never reinterpreted.
            return entry;
        }

        ShaderEntry defaultEntry = GetDefaultEntry(kind, sceneFormat);
        if (!IsGlobalCompatible(defaultEntry, globalUniforms))
        {
            LogGlobalTypeMismatch(defaultEntry);
        }

        return defaultEntry;
    }

    private static SpriteSnapshot SeedDefaultUniforms(in SpriteSnapshot sprite, ShaderEntry entry)
    {
        if (!entry.IsDefault)
        {
            return sprite;
        }

        // A game pack may shadow shader/sprite with declarations, so seed the default block with no
        // per-sprite overrides instead of assuming the default declares nothing.
        ReadOnlySpan<Vector4> block = entry.Metadata.DefaultInstanceUniformBlock;
        return sprite with
        {
            Uniform0 = block[0],
            Uniform1 = block[1],
            Uniform2 = block[2],
            Uniform3 = block[3],
        };
    }

    private void SeedLineUniforms(in LineSnapshot line, ShaderEntry entry, int lineIndex)
    {
        if (entry.IsDefault)
        {
            // A game pack may shadow shader/line with declarations, so seed the line's block from the
            // default entry's declared defaults instead of assuming it declares nothing.
            ReadOnlySpan<Vector4> block = entry.Metadata.DefaultInstanceUniformBlock;
            _lineParams0[lineIndex] = block[0];
            _lineParams1[lineIndex] = block[1];
            _lineParams2[lineIndex] = block[2];
            _lineParams3[lineIndex] = block[3];
            return;
        }

        _lineParams0[lineIndex] = line.Uniform0;
        _lineParams1[lineIndex] = line.Uniform1;
        _lineParams2[lineIndex] = line.Uniform2;
        _lineParams3[lineIndex] = line.Uniform3;
    }

    private bool TryResolveExtraTextures(
        in SpriteSnapshot sprite,
        ShaderEntry entry,
        out Vector4 indices)
    {
        indices = default;
        if (sprite.AdditionalTextureCount > MaxExtraTextures)
        {
            LogExtraTextureOverflow(sprite.AdditionalTextureCount);
        }

        int declared = Math.Min(entry.Metadata.TextureSlots.Length, MaxExtraTextures);
        int available = Math.Min(
            Math.Max(sprite.AdditionalTextureCount, 0),
            MaxExtraTextures);
        int count = Math.Min(declared, available);
        for (var slot = 0; slot < count; slot++)
        {
            AssetRef<TextureDescriptor> reference = GetExtraTextureReference(sprite, slot);
            TexturePageLookup lookup = _pages.EnsurePage(
                reference.HasValue ? reference.Path : null);
            if (lookup.Kind == TexturePageLookupKind.Pending)
            {
                return false;
            }

            if (lookup.Kind == TexturePageLookupKind.Ready)
            {
                SetComponent(ref indices, slot, lookup.Index);
            }
        }

        return true;
    }

    private static void MarkCandidate(byte[] visibility, int index)
    {
        if (visibility[index] == VisibilityNone)
        {
            visibility[index] = VisibilityCandidate;
        }
    }

    private void LogInvalidSpriteLayer(uint layer)
    {
        if (_loggedInvalidSpriteLayer)
        {
            return;
        }

        _loggedInvalidSpriteLayer = true;
        _logger.LogWarning(
            "Sprite layer {Layer} is not a single non-zero layer bit; the sprite is skipped. This "
                + "diagnostic is logged once per renderer and the frame counter keeps the total.",
            layer);
    }

    private void LogInvalidLineLayer(uint layer)
    {
        if (_loggedInvalidLineLayer)
        {
            return;
        }

        _loggedInvalidLineLayer = true;
        _logger.LogWarning(
            "Line layer {Layer} is not a single non-zero layer bit; the line is skipped. This "
                + "diagnostic is logged once per renderer and the frame counter keeps the total.",
            layer);
    }

    private void LogLineGeometry()
    {
        if (_loggedLineGeometry)
        {
            return;
        }

        _loggedLineGeometry = true;
        _logger.LogWarning(
            "A line has no drawable geometry (its geometry is null or invalid) and is skipped. The "
                + "engine validates line input at capture time. This diagnostic is logged once per "
                + "renderer.");
    }

    private void LogNonQuad(SpriteRenderData data)
    {
        string path = data.Texture.Path ?? "<none>";
        if (!_loggedNonQuadAssets.Add(path))
        {
            return;
        }

        _logger.LogWarning(
            "Sprite asset '{Texture}' uses topology {Topology}; M8 draws Quad only, so the asset "
                + "is skipped (never approximated as a quad). This diagnostic is logged once per "
                + "asset path.",
            path,
            data.Topology);
    }

    private void LogGlobalTypeMismatch(ShaderEntry entry)
    {
        if (!_loggedGlobalTypeMismatches.Add(entry.Path))
        {
            return;
        }

        _logger.LogWarning(
            "Shader '{Shader}' declares a global uniform whose type differs from the engine-wide "
                + "value. Its renderables draw with the kind's default shader and the engine value "
                + "is never reinterpreted. This diagnostic is logged once per shader path.",
            entry.Path);
    }

    private void LogExtraTextureOverflow(int count)
    {
        if (_loggedExtraTextureOverflow)
        {
            return;
        }

        _loggedExtraTextureOverflow = true;
        _logger.LogWarning(
            "A sprite has {Count} additional texture references; at most {Max} are supported, so "
                + "the rest are ignored. This diagnostic is logged once per renderer.",
            count,
            MaxExtraTextures);
    }

    private void LogInvalidCamera(int cameraIndex, CameraInvalidReason reason)
    {
        uint bit = 1u << (int)reason;
        if ((_loggedCameraReasons & bit) != 0)
        {
            return;
        }

        _loggedCameraReasons |= bit;
        _logger.LogWarning(
            "Camera {CameraIndex} was skipped, including its clear: {Reason}. This diagnostic is "
                + "logged once per renderer and reason.",
            cameraIndex,
            CameraProjection.Describe(reason));
    }

    private void LogDepthClear()
    {
        if (_loggedDepthClear)
        {
            return;
        }

        _loggedDepthClear = true;
        _logger.LogInformation(
            "A camera requested ClearKind.Depth; M8 has no depth attachment, so the camera behaves "
                + "like ClearKind.None. This diagnostic is logged once per renderer.");
    }

    private void EnsureSpriteArrays(int count)
    {
        if (_spriteInstances.Length >= count)
        {
            return;
        }

        int capacity = Math.Max(count, Math.Max(MinimumCapacity, _spriteInstances.Length * 2));
        Array.Resize(ref _spriteInstances, capacity);
        Array.Resize(ref _spriteEntryIds, capacity);
        Array.Resize(ref _spriteDrawable, capacity);
        Array.Resize(ref _spriteVisibility, capacity);
    }

    private void EnsureLineArrays(int count)
    {
        if (_lineGeometries.Length >= count)
        {
            return;
        }

        int capacity = Math.Max(count, Math.Max(MinimumCapacity, _lineGeometries.Length * 2));
        Array.Resize(ref _lineGeometries, capacity);
        Array.Resize(ref _lineEntryIds, capacity);
        Array.Resize(ref _lineDrawable, capacity);
        Array.Resize(ref _lineVisibility, capacity);
        Array.Resize(ref _lineParams0, capacity);
        Array.Resize(ref _lineParams1, capacity);
        Array.Resize(ref _lineParams2, capacity);
        Array.Resize(ref _lineParams3, capacity);
    }

    private void EnsureSortKeyCapacity(int count)
    {
        if (_sortKeys.Length >= count)
        {
            return;
        }

        int capacity = Math.Max(count, Math.Max(MinimumCapacity, _sortKeys.Length * 2));
        Array.Resize(ref _sortKeys, capacity);
    }

    private void EnsureVisibleRecordCapacity(int required)
    {
        if (_visibleRecords.Length >= required)
        {
            return;
        }

        int capacity = Math.Max(required, Math.Max(MinimumCapacity, _visibleRecords.Length * 2));
        Array.Resize(ref _visibleRecords, capacity);
    }

    private void EnsureRunCapacity(int required)
    {
        if (_runs.Length >= required)
        {
            return;
        }

        int capacity = Math.Max(required, Math.Max(MinimumCapacity, _runs.Length * 2));
        Array.Resize(ref _runs, capacity);
    }

    private void EnsureCameraArrays(int count)
    {
        if (_cameras.Length >= count)
        {
            return;
        }

        int capacity = Math.Max(count, Math.Max(MinimumCapacity, _cameras.Length * 2));
        Array.Resize(ref _cameraIndices, capacity);
        Array.Resize(ref _cameras, capacity);
    }

    /// <summary>One camera-visible record: its kind and its index in that kind's captured list.</summary>
    /// <param name="Kind">The renderable kind.</param>
    /// <param name="Index">The index into the captured sprite or line list.</param>
    private readonly record struct VisibleRecord(DrawKind Kind, int Index);
}
