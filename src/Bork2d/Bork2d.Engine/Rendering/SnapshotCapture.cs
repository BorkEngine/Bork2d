using System.Numerics;
using Bork2d.Engine.Scene;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// Fills one <see cref="FrameSnapshot"/> by walking the rooted scene once with an iterative,
/// parent-first traversal in no specified order (M7 WP-2 capture pass).
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
/// <b>Traversal.</b> The pass uses one explicit reusable stack of nodes paired with their evaluated
/// parent world matrix instead of recursion, so a deep hierarchy cannot exhaust the call stack. A
/// node's world matrix is composed exactly once as <c>local * parentWorld</c>, where
/// <c>parentWorld</c> is the matrix of the node that owns it and the root's direct children use the
/// identity. The stack arrays grow to the observed high-water mark and allocate nothing after
/// warm-up; every used reference is cleared after the pass, so a capture never retains a detached
/// node.
/// </para>
/// <para>
/// <b>Locking.</b> The whole traversal holds the existing engine structural lock. Each node's
/// local matrix and render state are copied under one acquisition of its per-object lock, and the
/// world matrix is composed outside that lock. This keeps the established
/// structural-then-per-object order (D-024) and never re-enters the structural lock. No lock is
/// held across a wait and no new lock ordering is added.
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
internal sealed class SnapshotCapture<TLayer>
    where TLayer : struct, Enum
{
    private readonly Lock _structuralLock;
    private readonly SceneRoot _root;
    private Transform?[] _nodeStack = Array.Empty<Transform?>();
    private Matrix3x2[] _worldStack = Array.Empty<Matrix3x2>();
    private int _stackCount;

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

        try
        {
            using var scope = _structuralLock.EnterScope();
            PushChildren(_root, Matrix3x2.Identity);
            while (_stackCount > 0)
            {
                var index = _stackCount - 1;
                _stackCount = index;
                Transform node = _nodeStack[index]!;
                _nodeStack[index] = null;
                Matrix3x2 parentWorld = _worldStack[index];
                Matrix3x2 world = CaptureNode(node, snapshot, parentWorld);
                PushChildren(node, world);
            }
        }
        finally
        {
            ClearStacks();
        }
    }

    /// <summary>
    /// Copies one node under its single per-object lock acquisition and returns its world matrix.
    /// </summary>
    /// <remarks>
    /// The world matrix is composed after the capture method releases the per-object lock.
    /// </remarks>
    private static Matrix3x2 CaptureNode(
        Transform node,
        FrameSnapshot snapshot,
        in Matrix3x2 parentWorld)
    {
        switch (node)
        {
            case Camera<TLayer> camera:
            {
                Matrix3x2 local = camera.CaptureForSnapshot(out CameraSnapshot cameraSnapshot);
                Matrix3x2 world = local * parentWorld;
                snapshot.AddCamera(cameraSnapshot with { WorldMatrix = world });
                return world;
            }

            case SpriteRender<TLayer> sprite:
            {
                Matrix3x2 local = sprite.CaptureForSnapshot(out SpriteSnapshot spriteSnapshot);
                Matrix3x2 world = local * parentWorld;
                snapshot.AddSprite(spriteSnapshot with { WorldMatrix = world });
                return world;
            }

            default:
                return node.CaptureLocalMatrix() * parentWorld;
        }
    }

    /// <summary>
    /// Pushes every direct child of <paramref name="owner"/> with its parent world matrix.
    /// </summary>
    /// <remarks>
    /// The caller holds the structural lock, so the child count and indices are stable for the
    /// duration of the loop.
    /// </remarks>
    private void PushChildren(ISceneNode owner, in Matrix3x2 parentWorld)
    {
        int childCount = owner.ChildCountCore;
        for (var index = 0; index < childCount; index++)
        {
            EnsureStackCapacity(_stackCount + 1);
            _nodeStack[_stackCount] = owner.GetChildCore(index);
            _worldStack[_stackCount] = parentWorld;
            _stackCount++;
        }
    }

    private void EnsureStackCapacity(int required)
    {
        if (required <= _nodeStack.Length)
        {
            return;
        }

        var capacity = _nodeStack.Length == 0 ? 16 : _nodeStack.Length * 2;
        while (capacity < required)
        {
            capacity *= 2;
        }

        Array.Resize(ref _nodeStack, capacity);
        Array.Resize(ref _worldStack, capacity);
    }

    private void ClearStacks()
    {
        Array.Clear(_nodeStack, 0, _stackCount);
        _stackCount = 0;
    }
}
