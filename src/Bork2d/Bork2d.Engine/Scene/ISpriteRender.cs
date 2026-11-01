using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;

namespace Bork2d.Engine.Scene;

/// <summary>
/// A sprite renderable in the scene graph: a transform plus a sprite asset and draw state.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
/// <remarks>
/// <para>
/// Sprite renderables are scene entities. They do not implement <see cref="IDisposable"/> and
/// have no liveness flag: detach with <see cref="ITransform.Parent"/> = <see langword="null"/> or
/// clear the owning parent to remove one, and re-attach the same instance later. Hide a sprite
/// without removing it by assigning <see langword="null"/>.
/// </para>
/// <para>
/// <see cref="IRenderable.Bounds"/> is the world-space AABB of the drawn quad, derived from the
/// frozen render data, the pivot, the flips, and the world matrix with the same formula the vertex
/// shader uses. It returns <see cref="Bounds.Empty"/> for a null or malformed sprite and never
/// throws for those cases.
/// </para>
/// </remarks>
public interface ISpriteRender<TLayer> : IRenderable where TLayer : struct, Enum
{
    /// <summary>
    /// Gets or sets the loaded sprite asset; <see langword="null"/> draws nothing.
    /// </summary>
    /// <remarks>
    /// Load the asset with <see cref="IAssets.LoadAsset{T}(string)"/> and assign the same instance
    /// to one or more renderables. The loader does not validate the asset's texture reference; a
    /// missing texture simply makes the sprite non-drawable.
    /// </remarks>
    SpriteAsset? Sprite { get; set; }

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

    /// <summary>
    /// Gets or sets a value indicating whether the sprite is mirrored horizontally.
    /// </summary>
    /// <remarks>
    /// The flag mirrors the quad around the sprite's pivot when it is drawn, without changing the
    /// transform or its world matrix. The default is <see langword="false"/>. M7 stores the value
    /// and captures it into the frame snapshot; the M8 renderer applies it, so it has no visible
    /// effect yet.
    /// </remarks>
    bool FlipX { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sprite is mirrored vertically.
    /// </summary>
    /// <remarks>
    /// The flag mirrors the quad around the sprite's pivot when it is drawn, without changing the
    /// transform or its world matrix. The default is <see langword="false"/>. M7 stores the value
    /// and captures it into the frame snapshot; the M8 renderer applies it, so it has no visible
    /// effect yet.
    /// </remarks>
    bool FlipY { get; set; }
}
