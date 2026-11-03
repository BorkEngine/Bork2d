using System.Numerics;

namespace Bork2d.Engine.Assets;

/// <summary>
/// One uniform declared by a <see cref="ShaderAsset"/>: its name, type, float-lane offset, and
/// default value.
/// </summary>
/// <param name="Name">
/// The shader-declared uniform name. Names are matched ordinally and are the keys accepted by
/// <see cref="Scene.ISpriteRender{TLayer}.SetUniform(string, float)"/> and
/// <see cref="IEngine{TLayer}.SetGlobalUniform(string, float)"/>.
/// </param>
/// <param name="Type">
/// The value type, which determines how many consecutive float lanes the uniform occupies.
/// </param>
/// <param name="Offset">
/// The zero-based float-lane offset inside the shader's 16-float instance block or eight-`Vector4`
/// global block. An offset outside the block is tolerated by the engine-side metadata and contributes
/// no lanes until the renderer rejects or falls back from it.
/// </param>
/// <param name="Default">
/// The default value used when no override is stored. Components beyond the width of
/// <paramref name="Type"/> are ignored.
/// </param>
/// <remarks>
/// The value is a declaration, not a uniform value: the renderer reads it from the frozen
/// <see cref="ShaderRenderData"/> and the per-renderable or engine-wide override supplies the
/// captured value. Equality is the value equality of the positional record.
/// </remarks>
public readonly record struct ShaderUniform(
    string Name,
    ShaderUniformType Type,
    int Offset,
    Vector4 Default);
