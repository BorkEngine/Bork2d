using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Textures;
using Bork2d.Render.Vulkan;
using Microsoft.Extensions.Logging;

namespace Bork2d.Render.Sprites;

/// <summary>
/// The render-thread-owned, allocation-free sprite frame planner (M8 §6.4–§6.8).
/// </summary>
/// <remarks>
/// <para>
/// One call to <see cref="Plan"/> resolves the whole consumed snapshot exactly once:
/// </para>
/// <list type="number">
/// <item>Sort sprite snapshot indices by <c>(Layer, ZIndex)</c> ascending.</item>
/// <item>
/// Pre-pass every sprite once: null/invalid-layer/non-Quad/pending skips, one
/// <see cref="TexturePageStore.EnsurePage(string?)"/> per sprite, malformed-field validation, and a
/// cached <see cref="SpriteQuad"/>.
/// </item>
/// <item>Sort camera indices by <c>ZIndex</c>, resolve each projection, and skip invalid cameras.</item>
/// <item>
/// Filter each valid camera by its culling mask, assign contiguous per-camera instance ranges, grow
/// the frame's instance buffer once, and write every <see cref="SpriteInstance"/> directly into its
/// mapping.
/// </item>
/// </list>
/// <para>
/// Every per-sprite array is renderer-owned and preallocated; after warm-up the plan allocates
/// nothing. Diagnostics are throttled: invalid-camera reasons and invalid layers log once per
/// renderer, while a non-Quad asset logs once per asset path. Malformed sprite fields throw a fatal
/// <see cref="Bork2d.Engine.Bork2dException"/> naming the field and texture reference; they are
/// never silently repaired.
/// </para>
/// </remarks>
internal sealed class SpriteFramePlanner
{
    private const int MinimumCapacity = 16;

    private readonly TexturePageStore _pages;
    private readonly ILogger _logger;
    private readonly HashSet<string> _loggedNonQuadAssets = new(StringComparer.Ordinal);
    private readonly SpriteFramePlan _plan = new();
    private int[] _spriteIndices = [];
    private SpriteSkipReason[] _skipReasons = [];
    private TexturePageLookup[] _lookups = [];
    private SpriteQuad[] _quads = [];
    private int[] _cameraIndices = [];
    private CameraDrawPlan[] _cameras = [];
    private uint _loggedCameraReasons;
    private bool _loggedInvalidLayer;
    private bool _loggedDepthClear;

    /// <summary>Initializes a new instance of the <see cref="SpriteFramePlanner"/> class.</summary>
    /// <param name="pages">The texture-page cache the planner resolves and prefetches through.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger for throttled diagnostics.</param>
    internal SpriteFramePlanner(TexturePageStore pages, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(logger);
        _pages = pages;
        _logger = logger;
    }

    /// <summary>
    /// Plans one snapshot and writes its instance records into the frame buffer.
    /// </summary>
    /// <param name="snapshot">The consumed snapshot; read-only and never mutated.</param>
    /// <param name="framebufferWidth">The target/framebuffer width in pixels.</param>
    /// <param name="framebufferHeight">The target/framebuffer height in pixels.</param>
    /// <param name="instances">The frame's instance sink, after its fence has been observed.</param>
    /// <returns>The refilled plan; valid until the next <see cref="Plan"/> call.</returns>
    /// <exception cref="Bork2d.Engine.Bork2dException">
    /// A sprite field is malformed or the instance buffer cannot hold the frame.
    /// </exception>
    internal SpriteFramePlan Plan(
        FrameSnapshot snapshot,
        int framebufferWidth,
        int framebufferHeight,
        IInstanceBuffer instances)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(instances);

        ReadOnlySpan<SpriteSnapshot> sprites = snapshot.Sprites;
        int spriteCount = sprites.Length;
        EnsureSpriteArrays(spriteCount);
        Span<int> spriteOrder = _spriteIndices.AsSpan(0, spriteCount);
        Span<SpriteSkipReason> skips = _skipReasons.AsSpan(0, spriteCount);
        Span<TexturePageLookup> lookups = _lookups.AsSpan(0, spriteCount);
        Span<SpriteQuad> quads = _quads.AsSpan(0, spriteCount);
        SpritePlanning.Sort(sprites, spriteOrder);

        long skipped = 0;
        for (var index = 0; index < spriteCount; index++)
        {
            int spriteIndex = spriteOrder[index];
            SpriteSkipReason reason = ResolveSprite(
                sprites[spriteIndex],
                out TexturePageLookup lookup,
                out SpriteQuad quad);
            skips[spriteIndex] = reason;
            lookups[spriteIndex] = lookup;
            quads[spriteIndex] = quad;
            if (reason != SpriteSkipReason.None)
            {
                skipped++;
            }
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
                camera.Clear,
                camera.ClearColor,
                camera.CullingMask,
                FirstInstance: 0,
                InstanceCount: 0);
        }

        var instanceCount = 0;
        var nonEmptyCameras = 0;
        for (var cameraIndex = 0; cameraIndex < cameraPlanCount; cameraIndex++)
        {
            CameraDrawPlan camera = _cameras[cameraIndex];
            var count = 0;
            for (var index = 0; index < spriteCount; index++)
            {
                int spriteIndex = spriteOrder[index];
                if (skips[spriteIndex] != SpriteSkipReason.None)
                {
                    continue;
                }

                if (!SpritePlanning.LayerMatches(camera.CullingMask, sprites[spriteIndex].Layer))
                {
                    continue;
                }

                count++;
            }

            _cameras[cameraIndex] = camera with
            {
                FirstInstance = instanceCount,
                InstanceCount = count,
            };
            instanceCount += count;
            if (count > 0)
            {
                nonEmptyCameras++;
            }
        }

        if (instanceCount > 0)
        {
            instances.EnsureCapacity(instanceCount);
            WriteInstances(
                sprites,
                spriteOrder,
                skips,
                lookups,
                quads,
                cameraPlanCount,
                instances);
        }

        _plan.CameraCount = cameraCount;
        _plan.CameraPlanCount = cameraPlanCount;
        _plan.DrawCalls = nonEmptyCameras;
        _plan.NonEmptyCameras = nonEmptyCameras;
        _plan.InstanceCount = instanceCount;
        _plan.SkippedSprites = skipped;
        _plan.Cameras = _cameras;
        return _plan;
    }

    private void WriteInstances(
        ReadOnlySpan<SpriteSnapshot> sprites,
        ReadOnlySpan<int> spriteOrder,
        ReadOnlySpan<SpriteSkipReason> skips,
        ReadOnlySpan<TexturePageLookup> lookups,
        ReadOnlySpan<SpriteQuad> quads,
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

            var cursor = camera.FirstInstance;
            for (var index = 0; index < spriteOrder.Length; index++)
            {
                int spriteIndex = spriteOrder[index];
                if (skips[spriteIndex] != SpriteSkipReason.None)
                {
                    continue;
                }

                SpriteSnapshot sprite = sprites[spriteIndex];
                if (!SpritePlanning.LayerMatches(camera.CullingMask, sprite.Layer))
                {
                    continue;
                }

                SpriteInstance instance = SpriteInstanceBuilder.Create(
                    sprite,
                    quads[spriteIndex],
                    (uint)lookups[spriteIndex].Index);
                instances.WriteInstance(cursor, instance);
                cursor++;
            }
        }
    }

    private SpriteSkipReason ResolveSprite(
        in SpriteSnapshot sprite,
        out TexturePageLookup lookup,
        out SpriteQuad quad)
    {
        lookup = default;
        quad = default;
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
        lookup = _pages.EnsurePage(data.Texture.Path);
        bool pending = lookup.Kind == TexturePageLookupKind.Pending;

        // Validate the frozen fields before the pending skip: the spec's malformed-field rule is
        // unconditional, so a malformed sprite is fatal even while its page is still loading. A
        // pending lookup is skipped below, so the resolved geometry is simply not drawn yet.
        quad = SpriteQuad.Resolve(data, sprite.FlipX, sprite.FlipY);
        return pending ? SpriteSkipReason.Pending : SpriteSkipReason.None;
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
        Array.Resize(ref _skipReasons, capacity);
        Array.Resize(ref _lookups, capacity);
        Array.Resize(ref _quads, capacity);
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
