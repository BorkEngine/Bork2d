using System.Numerics;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Microsoft.Extensions.Logging;

namespace Bork2d.Engine.Scene;

/// <summary>
/// The renderable definition and typed producer for engine font renderables.
/// </summary>
/// <remarks>
/// Creation builds a <see cref="FontRender{TLayer}"/> and attaches it under the requested parent.
/// Capture takes the font's per-object lock exactly once through
/// <see cref="FontRender{TLayer}.CaptureForSnapshot"/>, composes <c>local * parentWorld</c> outside
/// that lock, and appends the record to the reserved font buffer. The snapshot bound is the
/// world-space AABB of the laid-out glyph quads; an empty or invalid layout is captured with empty
/// bounds and never throws.
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class FontRenderableDefinition<TLayer>
    : RenderDataProducer<FontRender<TLayer>>, IRenderableDefinition
    where TLayer : struct, Enum
{
    private readonly Lock _structuralLock;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FontRenderableDefinition{TLayer}"/> class.
    /// </summary>
    /// <param name="structuralLock">The engine-owned global structural lock.</param>
    /// <param name="logger">The engine logger used for per-renderable font diagnostics.</param>
    internal FontRenderableDefinition(Lock structuralLock, ILogger logger)
        : base("Font")
    {
        ArgumentNullException.ThrowIfNull(logger);
        _structuralLock = structuralLock;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Type NodeType => typeof(FontRender<TLayer>);

    /// <inheritdoc/>
    public IRenderDataProducer Producer => this;

    /// <inheritdoc/>
    public Transform Create(ISceneNode parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var font = new FontRender<TLayer>(_structuralLock, _logger);
        font.AttachAsNewChild(parent);
        return font;
    }

    /// <inheritdoc/>
    protected override bool Capture(
        FontRender<TLayer> node,
        in Matrix3x2 parentWorld,
        FrameSnapshot snapshot,
        out Matrix3x2 world)
    {
        Matrix3x2 local = node.CaptureForSnapshot(out FontSnapshot font);
        world = local * parentWorld;

        // The snapshot and the live IBoundedTransform.Bounds getter share FontBounds, so culling in
        // the renderer sees exactly the laid-out glyph quads.
        Bounds bounds = FontBounds.CreateWorldBounds(font.Layout, world);
        snapshot.GetBuffer<FontSnapshot>(Slot).Add(
            font with { WorldMatrix = world, Bounds = bounds });
        return true;
    }
}
