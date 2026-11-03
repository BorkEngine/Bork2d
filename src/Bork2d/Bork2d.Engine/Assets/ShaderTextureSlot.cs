namespace Bork2d.Engine.Assets;

/// <summary>
/// One additional texture slot declared by a <see cref="ShaderAsset"/>.
/// </summary>
/// <param name="Name">
/// The shader-declared slot name. Names are matched ordinally by the renderer; the engine captures
/// the slot declarations as metadata and does not bind textures to them.
/// </param>
/// <remarks>
/// The sprite's own texture is separate from these slots. A custom shader may declare up to four
/// additional slots; a renderable supplies matching references through
/// <see cref="Scene.ISpriteRender{TLayer}.AdditionalTextures"/>, and the renderer warns about and
/// ignores extra references.
/// </remarks>
public readonly record struct ShaderTextureSlot(string Name);
