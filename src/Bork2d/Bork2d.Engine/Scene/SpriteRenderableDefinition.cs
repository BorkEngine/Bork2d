using System.Numerics;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;

namespace Bork2d.Engine.Scene;

/// <summary>
/// The renderable definition and typed producer for engine sprite renderables.
/// </summary>
/// <remarks>
/// Creation builds a <see cref="SpriteRender{TLayer}"/> and attaches it under the requested parent.
/// Capture takes the sprite's per-object lock exactly once through
/// <see cref="SpriteRender{TLayer}.CaptureForSnapshot"/>, composes <c>local * parentWorld</c> outside
/// that lock, and appends the record to the reserved sprite buffer.
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class SpriteRenderableDefinition<TLayer>
    : RenderDataProducer<SpriteRender<TLayer>>, IRenderableDefinition
    where TLayer : struct, Enum
{
    private readonly Lock _structuralLock;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteRenderableDefinition{TLayer}"/> class.
    /// </summary>
    /// <param name="structuralLock">The engine-owned global structural lock.</param>
    internal SpriteRenderableDefinition(Lock structuralLock)
        : base("Sprite")
    {
        _structuralLock = structuralLock;
    }

    /// <inheritdoc/>
    public Type NodeType => typeof(SpriteRender<TLayer>);

    /// <inheritdoc/>
    public IRenderDataProducer Producer => this;

    /// <inheritdoc/>
    public Transform Create(ISceneNode parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var sprite = new SpriteRender<TLayer>(_structuralLock);
        sprite.AttachAsNewChild(parent);
        return sprite;
    }

    /// <inheritdoc/>
    protected override bool Capture(
        SpriteRender<TLayer> node,
        in Matrix3x2 parentWorld,
        FrameSnapshot snapshot,
        out Matrix3x2 world)
    {
        Matrix3x2 local = node.CaptureForSnapshot(out SpriteSnapshot sprite);
        world = local * parentWorld;

        // The snapshot and the live ISpriteRender.Bounds getter share SpriteBounds, so culling in
        // the renderer sees exactly the quad the shader draws.
        Bounds bounds = SpriteBounds.CreateWorldBounds(
            sprite.SpriteData,
            world,
            sprite.FlipX,
            sprite.FlipY);
        snapshot.GetBuffer<SpriteSnapshot>(Slot).Add(
            sprite with { WorldMatrix = world, Bounds = bounds });
        return true;
    }
}
