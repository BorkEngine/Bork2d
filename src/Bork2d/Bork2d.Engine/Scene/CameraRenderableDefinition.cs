using System.Numerics;
using Bork2d.Engine.Platform;
using Bork2d.Engine.Rendering;

namespace Bork2d.Engine.Scene;

/// <summary>
/// The renderable definition and typed producer for engine cameras.
/// </summary>
/// <remarks>
/// Creation builds a <see cref="Camera{TLayer}"/> and attaches it under the requested parent.
/// Capture takes the camera's per-object lock exactly once through
/// <see cref="Camera{TLayer}.CaptureForSnapshot"/>, composes <c>local * parentWorld</c> outside that
/// lock, and appends the record to the reserved camera buffer.
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class CameraRenderableDefinition<TLayer>
    : RenderDataProducer<Camera<TLayer>>, IRenderableDefinition
    where TLayer : struct, Enum
{
    private readonly Lock _structuralLock;
    private readonly IWindow _window;

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraRenderableDefinition{TLayer}"/> class.
    /// </summary>
    /// <param name="structuralLock">The engine-owned global structural lock.</param>
    /// <param name="window">The window the camera's screen/world helpers project against.</param>
    internal CameraRenderableDefinition(Lock structuralLock, IWindow window)
        : base("Camera")
    {
        ArgumentNullException.ThrowIfNull(window);
        _structuralLock = structuralLock;
        _window = window;
    }

    /// <inheritdoc/>
    public Type NodeType => typeof(Camera<TLayer>);

    /// <inheritdoc/>
    public IRenderDataProducer Producer => this;

    /// <inheritdoc/>
    public Transform Create(ISceneNode parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var camera = new Camera<TLayer>(_structuralLock, _window);
        camera.AttachAsNewChild(parent);
        return camera;
    }

    /// <inheritdoc/>
    protected override bool Capture(
        Camera<TLayer> node,
        in Matrix3x2 parentWorld,
        FrameSnapshot snapshot,
        out Matrix3x2 world)
    {
        Matrix3x2 local = node.CaptureForSnapshot(out CameraSnapshot camera);
        world = local * parentWorld;
        snapshot.GetBuffer<CameraSnapshot>(Slot).Add(camera with { WorldMatrix = world });
        return true;
    }
}
