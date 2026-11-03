using Bork2d.Bake.Toml;
using PolyType;

namespace Bork2d.Bake.Shaders;

/// <summary>
/// One uniform declaration in a shader sidecar: the declared type and its optional default value.
/// </summary>
/// <remarks>
/// <para>
/// The canonical keys are <c>type</c> and <c>default</c>. <c>type</c> accepts exactly
/// <c>float</c>, <c>vec2</c>, <c>vec3</c>, <c>vec4</c>, and <c>color</c>, case-sensitively. The
/// default is absent for a zero value, or a scalar number for <c>float</c>, or an array of numbers
/// whose length matches the declared component count.
/// </para>
/// <para>
/// The processor owns the semantic validation; this type only carries the bound values. The
/// <see cref="TomlNumberOrArray"/> leaf exists because the TOML binder supports no union of a scalar
/// and an array as one member.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class ShaderUniformSidecar
{
    /// <summary>
    /// Gets or sets the declared uniform type name (<c>float</c>, <c>vec2</c>, <c>vec3</c>,
    /// <c>vec4</c>, or <c>color</c>), or <see langword="null"/> when the key is absent.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the declared default value, or <see langword="null"/> for a zero value.
    /// </summary>
    public TomlNumberOrArray? Default { get; set; }
}
