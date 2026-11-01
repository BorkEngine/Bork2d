using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
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
/// cached <see cref="SpriteQuad"/>. Each drawable sprite's camera-independent
/// <see cref="SpriteInstance"/> is built once here, and its sorted index is compacted into a
/// per-layer range (one single-bit layer maps to one of 32 layer bits).
/// </item>
/// <item>Sort camera indices by <c>ZIndex</c>, resolve each projection, and skip invalid cameras.</item>
/// <item>
/// One visibility pass builds every camera's range: iterate only the layer bits set in the camera's
/// culling mask, apply the shared <see cref="SpritePlanning.IsVisible"/> rule once per sprite, and
/// collect the visible index while counting the camera's instances in the same scan.
/// </item>
/// <item>
/// Validate the accumulated frame total against <see cref="int.MaxValue"/>, grow the frame's
/// instance buffer once, and copy each precomputed record into its per-camera range.
/// </item>
/// </list>
/// <para>
/// Bounds culling is always on and conservative: a sprite is skipped for a camera only when its
/// non-empty world AABB does not intersect the camera's world AABB. Empty bounds mean "unknown"
/// (direct snapshot producers and malformed or absent data), so they never cull. A culled sprite is
/// a normal visibility skip, not a fatal error; the pre-pass classification and malformed-field
/// validation still run before culling and keep their existing order.
/// </para>
/// <para>
/// Every per-sprite array is renderer-owned and preallocated; after warm-up the plan allocates
/// nothing. Diagnostics are throttled: invalid-camera reasons and invalid layers log once per
/// renderer, while a non-Quad asset logs once per asset path. Malformed sprite fields throw a fatal
/// <see cref="Bork2dException"/> naming the field and texture reference; they are never silently
/// repaired. The frame instance total accumulates in <see cref="long"/> and is validated before it
/// is narrowed, so a multi-camera frame faults instead of overflowing.
/// </para>
/// </remarks>
internal sealed class SpriteFramePlanner
{
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
    private readonly ILogger _logger;
    private readonly HashSet<string> _loggedNonQuadAssets = new(StringComparer.Ordinal);
    private readonly SpriteFramePlan _plan = new();
    private readonly int[] _layerStarts = new int[LayerBitCount];
    private readonly int[] _layerCounts = new int[LayerBitCount];
    private int[] _spriteIndices = [];
    private SpriteInstance[] _spriteInstances = [];
    private byte[] _visibility = [];
    private int[] _visibleIndices = [];
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
    /// <exception cref="Bork2dException">
    /// A sprite field is malformed, the accumulated instance total exceeds <see cref="int.MaxValue"/>,
    /// or the instance buffer cannot hold the frame.
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
        SpritePlanning.Sort(sprites, spriteOrder);

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
                out TexturePageLookup lookup,
                out SpriteQuad quad);
            if (reason != SpriteSkipReason.None)
            {
                skipped++;
                continue;
            }

            // Build the camera-independent record once; each camera copies it into its own range.
            _spriteInstances[spriteIndex] = SpriteInstanceBuilder.Create(
                sprite,
                quad,
                (uint)lookup.Index);
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
                InstanceCount: 0);
        }

        Span<byte> visibility = _visibility.AsSpan(0, drawableCount);
        visibility.Clear();
        EnsureVisibleIndexCapacity(drawableCount);
        Span<int> visibleIndices = _visibleIndices.AsSpan();
        var visibleCount = 0;
        long frameInstanceCount = 0;
        for (var cameraIndex = 0; cameraIndex < cameraPlanCount; cameraIndex++)
        {
            CameraDrawPlan camera = _cameras[cameraIndex];
            int firstVisible = visibleCount;
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

            int count = visibleCount - firstVisible;
            _cameras[cameraIndex] = camera with { InstanceCount = count };
            frameInstanceCount += count;
            _ = ValidateInstanceCount(frameInstanceCount);
        }

        int instanceCount = ValidateInstanceCount(frameInstanceCount);
        var firstInstance = 0;
        var nonEmptyCameras = 0;
        for (var cameraIndex = 0; cameraIndex < cameraPlanCount; cameraIndex++)
        {
            CameraDrawPlan camera = _cameras[cameraIndex];
            _cameras[cameraIndex] = camera with { FirstInstance = firstInstance };
            firstInstance += camera.InstanceCount;
            if (camera.InstanceCount > 0)
            {
                nonEmptyCameras++;
            }
        }

        var culled = 0;
        for (var position = 0; position < drawableCount; position++)
        {
            if (visibility[position] == VisibilityCandidate)
            {
                culled++;
            }
        }

        if (instanceCount > 0)
        {
            instances.EnsureCapacity(instanceCount);
            WriteInstances(visibleIndices[..visibleCount], cameraPlanCount, instances);
        }

        _plan.CameraCount = cameraCount;
        _plan.CameraPlanCount = cameraPlanCount;
        _plan.DrawCalls = nonEmptyCameras;
        _plan.NonEmptyCameras = nonEmptyCameras;
        _plan.InstanceCount = instanceCount;
        _plan.SkippedSprites = skipped;
        _plan.CulledSprites = culled;
        _plan.Cameras = _cameras;
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
        Array.Resize(ref _spriteInstances, capacity);
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
