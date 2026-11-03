namespace Bork2d.Engine.Assets;

/// <summary>
/// The value type of a uniform declared by a <see cref="ShaderAsset"/>.
/// </summary>
/// <remarks>
/// <para>
/// The type determines how many consecutive float lanes the uniform occupies: <see cref="Float"/>
/// one, <see cref="Vector2"/> two, <see cref="Vector3"/> three, and <see cref="Vector4"/> and
/// <see cref="Color"/> four. The numeric values are part of the shader layout contract and are
/// frozen by <see cref="ShaderLayout.CurrentVersion"/>.
/// </para>
/// <para>
/// <see cref="Color"/> is a linear RGBA value carried exactly like a <see cref="Vector4"/>; the
/// declaration type only tells callers which setter and validation rules apply. The loader does not
/// validate a declared type, so an unknown numeric value is tolerated when the asset is frozen and
/// contributes no lanes to the layout.
/// </para>
/// </remarks>
public enum ShaderUniformType
{
    /// <summary>A single float lane.</summary>
    Float = 0,

    /// <summary>Two consecutive float lanes.</summary>
    Vector2 = 1,

    /// <summary>Three consecutive float lanes.</summary>
    Vector3 = 2,

    /// <summary>Four consecutive float lanes.</summary>
    Vector4 = 3,

    /// <summary>Four consecutive float lanes carrying linear RGBA components.</summary>
    Color = 4,
}
