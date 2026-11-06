using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;

namespace Bork2d.Engine.Scene;

/// <summary>
/// A font renderable in the scene graph: a transform, a font asset, the text to lay out, and the
/// font-specific layout state.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
/// <remarks>
/// <para>
/// Font renderables are scene entities. They do not implement <see cref="IDisposable"/> and have no
/// liveness flag: detach with <see cref="ITransform.Parent"/> = <see langword="null"/> or clear the
/// owning parent to remove one, and re-attach the same instance later. A renderable with no
/// <see cref="Font"/> or empty <see cref="Text"/> draws nothing.
/// </para>
/// <para>
/// Assigning <see cref="Font"/> freezes its atlas reference, metrics, glyphs, and kerning once, so
/// mutating the asset afterwards cannot change a cached layout, a bounds read, or a published
/// snapshot. Re-assigning the same asset keeps the first frozen values; assign a new asset instance
/// to change the rendered result.
/// </para>
/// <para>
/// Layout is local-space and cached. It is rebuilt only when <see cref="Font"/>, <see cref="Text"/>,
/// <see cref="Size"/>, <see cref="Alignment"/>, or <see cref="LineSpacing"/> changes. The local
/// origin is the first line's baseline at <c>y = 0</c>; lines advance downward and the default line
/// height is <c>(Ascent - Descent + LineGap) * Size * LineSpacing</c>. Kerning, explicit
/// <c>\n</c> (including <c>\r\n</c> and lone <c>\r</c>) line breaks, and per-line alignment are
/// applied by the engine.
/// </para>
/// <para>
/// A missing codepoint renders <c>?</c> when that glyph exists; otherwise it is skipped and one
/// warning is logged per codepoint per renderable. An invalid <see cref="Size"/> or
/// <see cref="LineSpacing"/> (not finite, or less than or equal to zero) produces an empty layout
/// and one warning, and never makes bounds or capture throw. A value that is not a defined
/// <see cref="TextAlignment"/> behaves as <see cref="TextAlignment.Left"/>.
/// </para>
/// <para>
/// <see cref="IBoundedTransform.Bounds"/> is the world-space AABB of the laid-out glyph quads, not
/// the layout box; empty text, a null font, an empty layout, or a layout with no drawable quads
/// returns <see cref="Bounds.Empty"/>.
/// </para>
/// <para>
/// The shared draw state — <see cref="IRenderable{TLayer}.Shader"/>,
/// <see cref="IRenderable{TLayer}.Color"/>, <see cref="IRenderable{TLayer}.Layer"/>,
/// <see cref="IRenderable{TLayer}.ZIndex"/>, the instance uniform overloads, and
/// <see cref="IRenderable{TLayer}.ResetUniforms"/> — is declared by
/// <see cref="IRenderable{TLayer}"/>. A font with no assigned shader uses the built-in
/// <c>shader/sdf</c>; a custom fragment shader is drawn with the font glyph instance ABI, and a
/// failed custom shader falls back to <c>shader/sdf</c>.
/// </para>
/// </remarks>
public interface IFontRender<TLayer> : IRenderable<TLayer> where TLayer : struct, Enum
{
    /// <summary>
    /// Gets or sets the loaded font asset; <see langword="null"/> draws nothing.
    /// </summary>
    /// <remarks>
    /// Load the asset with <see cref="IAssets.LoadAsset{T}(string)"/> and assign the same instance
    /// to one or more renderables. Assigning the asset freezes its atlas reference, metrics,
    /// glyphs, and kerning once under the renderable's lock; later mutation of the asset does not
    /// change a cached layout or a published snapshot.
    /// </remarks>
    FontAsset? Font { get; set; }

    /// <summary>
    /// Gets or sets the text to lay out; the default is the empty string, which draws nothing.
    /// </summary>
    /// <remarks>
    /// <c>\n</c>, <c>\r\n</c>, and a lone <c>\r</c> start a new line. A trailing line break creates a
    /// final empty line for measurement and line-height purposes. Assigning an ordinal-equal value
    /// does not rebuild the cached layout.
    /// </remarks>
    string Text { get; set; }

    /// <summary>
    /// Gets or sets the world units per em; the default is <c>1</c>.
    /// </summary>
    /// <remarks>
    /// The setter stores the value without validating or throwing. A non-finite or non-positive size
    /// produces an empty layout at capture and bounds reads, with one warning per renderable.
    /// Changing the size rebuilds the cached layout.
    /// </remarks>
    float Size { get; set; }

    /// <summary>
    /// Gets or sets the per-line horizontal alignment; the default is <see cref="TextAlignment.Left"/>.
    /// </summary>
    /// <remarks>
    /// An undefined enum value is stored as-is and behaves as <see cref="TextAlignment.Left"/>.
    /// Changing the alignment rebuilds the cached layout.
    /// </remarks>
    TextAlignment Alignment { get; set; }

    /// <summary>
    /// Gets or sets the line-height multiplier; the default is <c>1</c>.
    /// </summary>
    /// <remarks>
    /// The multiplier scales the default line height
    /// <c>(Ascent - Descent + LineGap) * Size</c>; it is not extra world units. A non-finite or
    /// non-positive value produces an empty layout at capture and bounds reads, with one warning per
    /// renderable. Changing the multiplier rebuilds the cached layout.
    /// </remarks>
    float LineSpacing { get; set; }
}
