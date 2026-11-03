using System.Numerics;
using System.Runtime.InteropServices;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Shaders;
using Bork2d.Render.Textures;
using Bork2d.Render.Vulkan;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Sprites;

/// <summary>
/// The render-thread-owned, allocation-free sprite frame planner (M8 §6.4–§6.8, custom shaders).
/// </summary>
/// <remarks>
/// <para>
/// One call to <see cref="Plan"/> resolves the whole consumed snapshot exactly once:
/// </para>
/// <list type="number">
/// <item>Sort sprite snapshot indices by <c>(Layer, ZIndex)</c> ascending.</item>
/// <item>
/// Pre-pass every sprite once: null/invalid-layer/non-Quad/pending skips, malformed-field
/// validation, shader binding resolution, the shader's declared additional textures, and a cached
/// <see cref="SpriteQuad"/>. Each drawable sprite's camera-independent <see cref="SpriteInstance"/>
/// is built once here, and its sorted index is compacted into a per-layer range (one single-bit
/// layer maps to one of 32 layer bits).
/// </item>
/// <item>Sort camera indices by <c>ZIndex</c>, resolve each projection, and skip invalid cameras.</item>
/// <item>
/// One visibility pass builds every camera's contiguous instance range and groups the visible
/// sprites into contiguous <see cref="ShaderRun"/>s: two adjacent visible sprites with the same
/// resolved shader entry share a run, and a different entry starts a new run without reordering the
/// sprites.
/// </item>
/// <item>
/// Validate the accumulated frame total against <see cref="int.MaxValue"/>, write one 144-byte global
/// block per run, grow the instance buffer once, and copy each precomputed record into its
/// per-camera range.
/// </item>
/// </list>
/// <para>
/// A custom shader that is still loading skips the sprite for the frame and counts in
/// <see cref="SpriteFramePlan.SkippedSprites"/>, exactly like a still-loading texture page. A custom
/// shader that failed validation, loading, module/pipeline creation, or conflicts with an
/// engine-wide global name draws with the default entry instead and is not skipped. Additional
/// textures resolve positionally against the shader's declared slots; a pending extra page skips the
/// whole sprite, a failed or absent page uses the bindless placeholder index <c>0</c>, and paths
/// beyond the declared slot count are never loaded.
/// </para>
/// <para>
/// Bounds culling is always on and conservative: a sprite is skipped for a camera only when its
/// non-empty world AABB does not intersect the camera's world AABB. Empty bounds mean "unknown"
/// (direct snapshot producers and malformed or absent data), so they never cull. A culled sprite is
/// a normal visibility skip, not a fatal error; the pre-pass classification and malformed-field
/// validation still run before culling and keep their existing order.
/// </para>
/// <para>
/// Every per-sprite, per-run, and per-frame array is renderer-owned and preallocated; after warm-up
/// the plan allocates nothing. Diagnostics are throttled: invalid-camera reasons and invalid layers
/// log once per renderer, a non-Quad asset logs once per asset path, an additional-texture overflow
/// logs once per renderer, and a shader global-type mismatch logs once per shader path. Malformed
/// sprite fields throw a fatal <see cref="Bork2dException"/> naming the field and texture reference;
/// they are never silently repaired. The frame instance total accumulates in <see cref="long"/> and
/// is validated before it is narrowed, so a multi-camera frame faults instead of overflowing.
/// </para>
/// </remarks>
internal sealed class SpriteFramePlanner
{
    /// <summary>The maximum number of additional textures a shader may sample per sprite.</summary>
    internal const int MaxExtraTextures = ShaderValidation.MaxTextureSlots;

    private const int MinimumCapacity = 16;

    /// <summary>The number of single-bit layers a culling mask and flattened layer can address.</summary>
    private const int LayerBitCount = 32;

    // Per-sprite bounds visibility for the current plan: not a layer candidate, a candidate that
    // no planned camera emitted, or emitted by at least one camera. CulledSprites counts the
    // middle state once per sprite.
    private const byte VisibilityNone = 0;
    private const byte VisibilityCandidate = 1;
    private const byte VisibilityDrawn = 2;

    private readonly TexturePageStore _pages;
    private readonly IShaderFrameSource _shaders;
    private readonly ILogger _logger;
    private readonly HashSet<string> _loggedNonQuadAssets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loggedGlobalTypeMismatches = new(StringComparer.Ordinal);
    private readonly SpriteFramePlan _plan = new();
    private readonly int[] _layerStarts = new int[LayerBitCount];
    private readonly int[] _layerCounts = new int[LayerBitCount];
    private int[] _spriteIndices = [];
    private SpriteSortKey[] _spriteSortKeys = [];
    private SpriteInstance[] _spriteInstances = [];
    private int[] _spriteEntryIds = [];
    private byte[] _visibility = [];
    private int[] _visibleIndices = [];
    private ShaderRun[] _runs = [];
    private int[] _cameraIndices = [];
    private CameraDrawPlan[] _cameras = [];
    private ShaderEntry? _defaultEntry;
    private uint _loggedCameraReasons;
    private bool _loggedInvalidLayer;
    private bool _loggedDepthClear;
    private bool _loggedExtraTextureOverflow;

    /// <summary>Initializes a new instance of the <see cref="SpriteFramePlanner"/> class.</summary>
    /// <param name="pages">The texture-page cache the planner resolves and prefetches through.</param>
    /// <param name="shaders">The shader catalog the planner resolves captured bindings through.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger for throttled diagnostics.</param>
    internal SpriteFramePlanner(
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
    /// Plans one snapshot, writes one global uniform block per shader run, and writes its instance
    /// records into the frame buffer.
    /// </summary>
    /// <param name="snapshot">The consumed snapshot; read-only and never mutated.</param>
    /// <param name="framebufferWidth">The target/framebuffer width in pixels.</param>
    /// <param name="framebufferHeight">The target/framebuffer height in pixels.</param>
    /// <param name="instances">The frame's instance sink, after its fence has been observed.</param>
    /// <param name="globals">
    /// The frame's global uniform sink. Exactly one 144-byte block is written per shader run with the
    /// frame time in <c>time.x</c>, the declared engine-wide user values for that run's shader, and
    /// each declaration's shader default where the engine has no matching name and type.
    /// </param>
    /// <param name="targetFormat">The target's sRGB output color format for this frame.</param>
    /// <returns>The refilled plan; valid until the next <see cref="Plan"/> call.</returns>
    /// <exception cref="Bork2dException">
    /// A sprite field is malformed, a shader entry or pipeline invariant is broken, the accumulated
    /// instance total exceeds <see cref="int.MaxValue"/>, or the instance buffer cannot hold the
    /// frame.
    /// </exception>
    internal SpriteFramePlan Plan(
        FrameSnapshot snapshot,
        int framebufferWidth,
        int framebufferHeight,
        IInstanceBuffer instances,
        IGlobalUniformBuffer globals,
        Format targetFormat)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(globals);

        ReadOnlySpan<SpriteSnapshot> sprites = snapshot.Sprites;
        ReadOnlySpan<GlobalUniformSnapshot> globalUniforms = snapshot.GlobalUniforms;
        int spriteCount = sprites.Length;
        _defaultEntry = null;
        EnsureSpriteArrays(spriteCount);
        Span<int> spriteOrder = _spriteIndices.AsSpan(0, spriteCount);
        SpritePlanning.Sort(
            sprites,
            spriteOrder,
            _spriteSortKeys.AsSpan(0, spriteCount));

        Span<int> layerStarts = _layerStarts;
        Span<int> layerCounts = _layerCounts;
        layerCounts.Clear();

        long skipped = 0;
        var drawableCount = 0;
        for (var index = 0; index < spriteCount; index++)
        {
            int spriteIndex = spriteOrder[index];
            SpriteSnapshot sprite = sprites[spriteIndex];
            SpriteSkipReason reason = ResolveSprite(
                sprite,
                spriteIndex,
                targetFormat,
                globalUniforms,
                out SpriteInstance instance);
            if (reason != SpriteSkipReason.None)
            {
                skipped++;
                continue;
            }

            _spriteInstances[spriteIndex] = instance;
            int layerBit = BitOperations.TrailingZeroCount(sprite.Layer);
            if (layerCounts[layerBit] == 0)
            {
                layerStarts[layerBit] = drawableCount;
            }

            layerCounts[layerBit]++;
            spriteOrder[drawableCount++] = spriteIndex;
        }

        ReadOnlySpan<CameraSnapshot> cameras = snapshot.Cameras;
        int cameraCount = cameras.Length;
        EnsureCameraArrays(cameraCount);
        Span<int> cameraOrder = _cameraIndices.AsSpan(0, cameraCount);
        SpritePlanning.SortCameras(cameras, cameraOrder);

        var framebufferSize = new Vector2(framebufferWidth, framebufferHeight);
        var cameraPlanCount = 0;
        for (var index = 0; index < cameraCount; index++)
        {
            int cameraIndex = cameraOrder[index];
            CameraSnapshot camera = cameras[cameraIndex];
            if (!SpriteCameraProjection.TryCreate(camera, framebufferSize, out CameraProjectionResult projection))
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
                FirstRun: 0,
                RunCount: 0);
        }

        Span<byte> visibility = _visibility.AsSpan(0, drawableCount);
        visibility.Clear();
        EnsureVisibleIndexCapacity(drawableCount);
        Span<int> visibleIndices = _visibleIndices.AsSpan();
        var visibleCount = 0;
        var runCount = 0;
        var nonEmptyCameras = 0;
        long frameInstanceCount = 0;
        for (var cameraIndex = 0; cameraIndex < cameraPlanCount; cameraIndex++)
        {
            CameraDrawPlan camera = _cameras[cameraIndex];
            int firstVisible = visibleCount;
            int firstRun = runCount;
            int currentRun = -1;
            int currentEntry = -1;
            var runFirst = 0;
            uint mask = camera.CullingMask;
            while (mask != 0)
            {
                int layerBit = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;
                int position = layerStarts[layerBit];
                int layerEnd = position + layerCounts[layerBit];
                for (; position < layerEnd; position++)
                {
                    int spriteIndex = spriteOrder[position];
                    if (!SpritePlanning.IsVisible(
                        camera.CullingMask,
                        camera.WorldBounds,
                        sprites[spriteIndex]))
                    {
                        if (visibility[position] == VisibilityNone)
                        {
                            visibility[position] = VisibilityCandidate;
                        }

                        continue;
                    }

                    visibility[position] = VisibilityDrawn;
                    int entryId = _spriteEntryIds[spriteIndex];
                    if (entryId != currentEntry)
                    {
                        if (currentEntry >= 0)
                        {
                            _runs[currentRun] = new ShaderRun(
                                runFirst,
                                visibleCount - runFirst,
                                currentEntry,
                                GlobalBlockOffset: 0);
                        }

                        currentEntry = entryId;
                        runFirst = visibleCount;
                        EnsureRunCapacity(runCount + 1);
                        _runs[runCount] = new ShaderRun(
                            runFirst,
                            InstanceCount: 0,
                            entryId,
                            GlobalBlockOffset: 0);
                        currentRun = runCount++;
                    }

                    if (visibleCount == visibleIndices.Length)
                    {
                        // The shared index scratch cannot address more records than an int; fail
                        // through the validated long path instead of overflowing the counter.
                        if (visibleCount == int.MaxValue)
                        {
                            throw CreateInstanceCountException((long)int.MaxValue + 1);
                        }

                        EnsureVisibleIndexCapacity(visibleCount + 1);
                        visibleIndices = _visibleIndices.AsSpan();
                    }

                    visibleIndices[visibleCount++] = spriteIndex;
                }
            }

            if (currentEntry >= 0)
            {
                _runs[currentRun] = new ShaderRun(
                    runFirst,
                    visibleCount - runFirst,
                    currentEntry,
                    GlobalBlockOffset: 0);
            }

            int count = visibleCount - firstVisible;
            int cameraRunCount = runCount - firstRun;
            _cameras[cameraIndex] = camera with
            {
                InstanceCount = count,
                FirstRun = firstRun,
                RunCount = cameraRunCount,
            };
            frameInstanceCount += count;
            _ = ValidateInstanceCount(frameInstanceCount);
            if (cameraRunCount > 0)
            {
                nonEmptyCameras++;
            }
        }

        int instanceCount = ValidateInstanceCount(frameInstanceCount);
        var firstInstance = 0;
        for (var cameraIndex = 0; cameraIndex < cameraPlanCount; cameraIndex++)
        {
            CameraDrawPlan camera = _cameras[cameraIndex];
            _cameras[cameraIndex] = camera with { FirstInstance = firstInstance };
            firstInstance += camera.InstanceCount;
        }

        var culled = 0;
        for (var position = 0; position < drawableCount; position++)
        {
            if (visibility[position] == VisibilityCandidate)
            {
                culled++;
            }
        }

        globals.EnsureCapacity(runCount);
        for (var runIndex = 0; runIndex < runCount; runIndex++)
        {
            ShaderRun run = _runs[runIndex];
            ShaderEntry entry = _shaders.GetEntry(run.ShaderEntryId);
            GlobalUniformBlock block = CreateGlobalBlock(snapshot, globalUniforms, entry);
            globals.WriteBlock(runIndex, in block);
            _runs[runIndex] = run with
            {
                GlobalBlockOffset = (uint)(runIndex * globals.BlockStride),
            };
        }

        if (instanceCount > 0)
        {
            instances.EnsureCapacity(instanceCount);
            WriteInstances(visibleIndices[..visibleCount], cameraPlanCount, instances);
        }

        _plan.CameraCount = cameraCount;
        _plan.CameraPlanCount = cameraPlanCount;
        _plan.DrawCalls = runCount;
        _plan.NonEmptyCameras = nonEmptyCameras;
        _plan.InstanceCount = instanceCount;
        _plan.RunCount = runCount;
        _plan.SkippedSprites = skipped;
        _plan.CulledSprites = culled;
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

    private void WriteInstances(
        ReadOnlySpan<int> visibleIndices,
        int cameraPlanCount,
        IInstanceBuffer instances)
    {
        for (var cameraIndex = 0; cameraIndex < cameraPlanCount; cameraIndex++)
        {
            CameraDrawPlan camera = _cameras[cameraIndex];
            if (camera.InstanceCount == 0)
            {
                continue;
            }

            int end = camera.FirstInstance + camera.InstanceCount;
            for (var instanceIndex = camera.FirstInstance; instanceIndex < end; instanceIndex++)
            {
                int spriteIndex = visibleIndices[instanceIndex];
                instances.WriteInstance(instanceIndex, in _spriteInstances[spriteIndex]);
            }
        }
    }

    private SpriteSkipReason ResolveSprite(
        in SpriteSnapshot sprite,
        int spriteIndex,
        Format targetFormat,
        ReadOnlySpan<GlobalUniformSnapshot> globalUniforms,
        out SpriteInstance instance)
    {
        instance = default;
        SpriteSkipReason reason = SpritePlanning.Classify(sprite, texturePending: false);
        switch (reason)
        {
            case SpriteSkipReason.InvalidLayer:
                LogInvalidLayer(sprite.Layer);
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
            entry = _defaultEntry ??= ResolveDefaultEntry(targetFormat);
        }
        else
        {
            ShaderBinding binding = _shaders.Resolve(sprite.ShaderData, targetFormat);
            if (binding.State == ShaderBindingState.Pending)
            {
                return SpriteSkipReason.Pending;
            }

            entry = binding.State == ShaderBindingState.Failed
                ? _defaultEntry ??= ResolveDefaultEntry(targetFormat)
                : _shaders.GetEntry(binding.EntryId);
        }

        entry = ApplyGlobalCompatibility(entry, globalUniforms, targetFormat);

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

    private ShaderEntry ResolveDefaultEntry(Format targetFormat)
    {
        ShaderBinding binding = _shaders.Resolve(null, targetFormat);
        return _shaders.GetEntry(binding.EntryId);
    }

    private ShaderEntry ApplyGlobalCompatibility(
        ShaderEntry entry,
        ReadOnlySpan<GlobalUniformSnapshot> globalUniforms,
        Format targetFormat)
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

        ShaderEntry defaultEntry = _defaultEntry ??= ResolveDefaultEntry(targetFormat);
        if (!IsGlobalCompatible(defaultEntry, globalUniforms))
        {
            LogGlobalTypeMismatch(defaultEntry);
        }

        return defaultEntry;
    }

    private SpriteSnapshot SeedDefaultUniforms(in SpriteSnapshot sprite, ShaderEntry entry)
    {
        if (!entry.IsDefault)
        {
            return sprite;
        }

        // Task 1 leaves the four lanes zero for a sprite with no custom shader. A game pack may
        // shadow shader/sprite with declarations, so seed the default block with no per-sprite
        // overrides instead of assuming the default declares nothing.
        ReadOnlySpan<Vector4> block = entry.Metadata.DefaultInstanceUniformBlock;
        return sprite with
        {
            Uniform0 = block[0],
            Uniform1 = block[1],
            Uniform2 = block[2],
            Uniform3 = block[3],
        };
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

    private void LogInvalidLayer(uint layer)
    {
        if (_loggedInvalidLayer)
        {
            return;
        }

        _loggedInvalidLayer = true;
        _logger.LogWarning(
            "Sprite layer {Layer} is not a single non-zero layer bit; the sprite is skipped. This "
                + "diagnostic is logged once per renderer and the frame counter keeps the total.",
            layer);
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
                + "value. Its sprites draw with the default shader and the engine value is never "
                + "reinterpreted. This diagnostic is logged once per shader path.",
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
        if (_spriteIndices.Length >= count)
        {
            return;
        }

        int capacity = Math.Max(count, Math.Max(MinimumCapacity, _spriteIndices.Length * 2));
        Array.Resize(ref _spriteIndices, capacity);
        Array.Resize(ref _spriteSortKeys, capacity);
        Array.Resize(ref _spriteInstances, capacity);
        Array.Resize(ref _spriteEntryIds, capacity);
        Array.Resize(ref _visibility, capacity);
    }

    private void EnsureVisibleIndexCapacity(int required)
    {
        if (_visibleIndices.Length >= required)
        {
            return;
        }

        int capacity = Math.Max(required, Math.Max(MinimumCapacity, _visibleIndices.Length * 2));
        Array.Resize(ref _visibleIndices, capacity);
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
}
