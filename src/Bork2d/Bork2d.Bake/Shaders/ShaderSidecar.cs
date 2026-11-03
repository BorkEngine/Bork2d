using PolyType;

namespace Bork2d.Bake.Shaders;

/// <summary>
/// The settings sidecar of the built-in fragment shader processor: additional texture slot names and
/// the per-instance and engine-wide uniform declarations.
/// </summary>
/// <remarks>
/// <para>
/// The property names are the canonical camelCase keys <c>textureSlots</c>, <c>uniforms</c>, and
/// <c>globals</c>. Every member is optional: an empty sidecar declares no slots and no uniforms, and
/// the framework creates that empty sidecar when a shader source has none. A missing key stays
/// <see langword="null"/>; there is no builder-wide default to inherit.
/// </para>
/// <para>
/// <c>textureSlots</c> lists at most four unique GLSL identifiers. Their positions correspond to the
/// sprite's additional texture positions; the names are metadata only and the generated prologue
/// always defines <c>BORK2D_TEXTURE0</c> through <c>BORK2D_TEXTURE3</c>. <c>uniforms</c> declares
/// per-renderable values inside the 16-float instance block and <c>globals</c> declares engine-wide
/// values, one eight-slot block each. The processor validates, sorts, and packs them.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class ShaderSidecar
{
    /// <summary>Gets or sets the additional texture slot names, or <see langword="null"/> for none.</summary>
    public string[]? TextureSlots { get; set; }

    /// <summary>
    /// Gets or sets the per-instance uniform declarations by shader name, or <see langword="null"/>
    /// for none.
    /// </summary>
    public Dictionary<string, ShaderUniformSidecar>? Uniforms { get; set; }

    /// <summary>
    /// Gets or sets the engine-wide uniform declarations by shader name, or <see langword="null"/>
    /// for none.
    /// </summary>
    public Dictionary<string, ShaderUniformSidecar>? Globals { get; set; }
}
