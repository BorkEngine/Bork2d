namespace Bork2d.Api;

/// <summary>
/// A sprite renderable in the scene graph: a transform plus a sprite handle and draw state.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
/// <remarks>
/// Sprite renderables are scene entities. They do not implement <see cref="IDisposable"/> and
/// have no liveness flag: detach with <see cref="ITransform.Parent"/> = <see langword="null"/> or
/// clear the owning parent to remove one, and re-attach the same instance later. Hide a sprite
/// without removing it by assigning <see cref="Sprite.Empty"/>.
/// </remarks>
public interface ISpriteRender<TLayer> : ITransform where TLayer : struct, Enum
{
    /// <summary>
    /// Gets or sets the opaque sprite handle; <see cref="Sprite.Empty"/> draws nothing.
    /// </summary>
    Sprite Sprite { get; set; }

    /// <summary>
    /// Gets or sets the single layer bit used for camera filtering. <c>0</c> and combined masks
    /// are invalid and skipped by the renderer.
    /// </summary>
    TLayer Layer { get; set; }

    /// <summary>
    /// Gets or sets the linear RGBA tint multiplied with the texture;
    /// <see cref="Color.White"/> is neutral.
    /// </summary>
    Color Tint { get; set; }

    /// <summary>
    /// Gets or sets the sort order within a layer; higher draws later.
    /// </summary>
    int ZIndex { get; set; }
}
