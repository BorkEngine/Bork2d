using System.Runtime.CompilerServices;
using Bork2d.Engine.Scene;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// Fills one <see cref="FrameSnapshot"/> by walking the rooted scene once in no specified order
/// (M7 WP-2 capture pass).
/// </summary>
/// <remarks>
/// <para>
/// <b>Capture point contract.</b> The host calls this on the tick thread after
/// <c>IGame.Tick</c> returns, with every game worker joined, after the first tick, and before
/// <c>Teardown</c> starts. The engine does not itself know about the loop: the quiescent point is
/// the host's responsibility, so a node detached during <c>Tick</c> is absent from that tick's
/// snapshot and a node created and attached during <c>Tick</c> is present.
/// </para>
/// <para>
/// <b>Locking.</b> The whole traversal holds the existing engine structural lock and reads each
/// object's properties under its per-object lock, in the established structural-then-per-object
/// order (D-024). The world matrix is evaluated through the internal
/// <see cref="ISceneNode.WorldMatrixCore"/> contract, which already assumes the structural lock is
/// held; capture never calls <see cref="Transform.WorldMatrix"/> and so never re-enters the
/// structural lock. No lock is held across a wait and no new lock ordering is added.
/// </para>
/// <para>
/// <b>What is captured.</b> Every reachable camera and sprite renderable is copied into
/// value-only <see cref="CameraSnapshot"/>/<see cref="SpriteSnapshot"/> entries; other transforms
/// are traversed but not captured. World matrices include the parent hierarchy, and
/// <typeparamref name="TLayer"/> is flattened to a raw <see cref="uint"/> mask (bit 31 survives;
/// invalid <c>0</c>/multi-bit values are preserved, Q3=C2). A sprite carries the immutable
/// <see cref="Assets.SpriteRenderData"/> frozen when its asset was assigned, never the live
/// <see cref="Assets.SpriteAsset"/>. The capture object is reused per engine, so a warm capture
/// allocates nothing.
/// </para>
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class SnapshotCapture<TLayer> : ISceneNodeVisitor
    where TLayer : struct, Enum
{
    private readonly Lock _structuralLock;
    private readonly SceneRoot _root;
    private FrameSnapshot? _snapshot;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotCapture{TLayer}"/> class.
    /// </summary>
    /// <param name="structuralLock">The engine-owned structural lock shared by the scene nodes.</param>
    /// <param name="root">The scene root owned by the same engine.</param>
    internal SnapshotCapture(Lock structuralLock, SceneRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _structuralLock = structuralLock;
        _root = root;
    }

    /// <summary>
    /// Resets and fills <paramref name="snapshot"/> from the current rooted scene and stamps the
    /// tick's sequence and time values.
    /// </summary>
    /// <param name="snapshot">The slot-owned snapshot the caller owns in the <c>Writing</c> state.</param>
    /// <param name="sequence">The monotonic snapshot sequence; the host starts at <c>1</c>.</param>
    /// <param name="time">The tick's <c>ITime.Time</c> value in seconds.</param>
    /// <param name="deltaTime">The tick's <c>ITime.DeltaTime</c> value in seconds.</param>
    internal void Capture(FrameSnapshot snapshot, long sequence, double time, double deltaTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        snapshot.Reset();
        snapshot.Sequence = sequence;
        snapshot.Time = time;
        snapshot.DeltaTime = deltaTime;

        _snapshot = snapshot;
        try
        {
            using var scope = _structuralLock.EnterScope();
            ((ISceneNode)_root).VisitChildrenCore(this);
        }
        finally
        {
            _snapshot = null;
        }
    }

    void ISceneNodeVisitor.Visit(Transform node)
    {
        var snapshot = _snapshot
            ?? throw new InvalidOperationException("The scene visitor must run inside Capture.");

        switch (node)
        {
            case Camera<TLayer> camera:
                snapshot.AddCamera(CaptureCamera(camera));
                break;
            case SpriteRender<TLayer> sprite:
                snapshot.AddSprite(CaptureSprite(sprite));
                break;
            default:
                break;
        }

        ((ISceneNode)node).VisitChildrenCore(this);
    }

    private static CameraSnapshot CaptureCamera(Camera<TLayer> camera)
    {
        var cullingMask = camera.CullingMask;
        return new CameraSnapshot(
            camera.ZIndex,
            ((ISceneNode)camera).WorldMatrixCore(),
            camera.Size,
            camera.Viewport,
            ToMask(cullingMask),
            camera.Clear,
            camera.ClearColor);
    }

    private static SpriteSnapshot CaptureSprite(SpriteRender<TLayer> sprite)
    {
        var layer = sprite.Layer;
        return new SpriteSnapshot(
            sprite.RenderData,
            ((ISceneNode)sprite).WorldMatrixCore(),
            sprite.Tint,
            ToMask(layer),
            sprite.ZIndex,
            sprite.FlipX,
            sprite.FlipY);
    }

    private static uint ToMask(TLayer layer) => (uint)Unsafe.As<TLayer, int>(ref layer);
}
