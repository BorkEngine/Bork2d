using System.Numerics;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;

namespace Bork2d.Engine.Scene;

/// <summary>
/// The renderable definition and typed producer for engine line renderables.
/// </summary>
/// <remarks>
/// Creation builds a <see cref="LineRender{TLayer}"/> and attaches it under the requested parent.
/// Capture takes the line's per-object lock exactly once through
/// <see cref="LineRender{TLayer}.CaptureForSnapshot"/>, composes <c>local * parentWorld</c> outside
/// that lock, and appends the record to the reserved line buffer. A line whose geometry is not
/// drawable is captured with a null geometry and empty bounds; capture itself never throws for
/// malformed points or width.
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class LineRenderableDefinition<TLayer>
    : RenderDataProducer<LineRender<TLayer>>, IRenderableDefinition
    where TLayer : struct, Enum
{
    private readonly Lock _structuralLock;

    /// <summary>
    /// Initializes a new instance of the <see cref="LineRenderableDefinition{TLayer}"/> class.
    /// </summary>
    /// <param name="structuralLock">The engine-owned global structural lock.</param>
    internal LineRenderableDefinition(Lock structuralLock)
        : base("Line")
    {
        _structuralLock = structuralLock;
    }

    /// <inheritdoc/>
    public Type NodeType => typeof(LineRender<TLayer>);

    /// <inheritdoc/>
    public IRenderDataProducer Producer => this;

    /// <inheritdoc/>
    public Transform Create(ISceneNode parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var line = new LineRender<TLayer>(_structuralLock);
        line.AttachAsNewChild(parent);
        return line;
    }

    /// <inheritdoc/>
    protected override bool Capture(
        LineRender<TLayer> node,
        in Matrix3x2 parentWorld,
        FrameSnapshot snapshot,
        out Matrix3x2 world)
    {
        Matrix3x2 local = node.CaptureForSnapshot(out LineSnapshot line);
        world = local * parentWorld;

        // The snapshot and the live IBoundedTransform.Bounds getter share LineBounds, so culling in
        // the renderer sees exactly the expanded line the renderer draws.
        Bounds bounds = LineBounds.CreateWorldBounds(line.Geometry, world);
        snapshot.GetBuffer<LineSnapshot>(Slot).Add(
            line with { WorldMatrix = world, Bounds = bounds });
        return true;
    }
}
