using System.ComponentModel;
using PolyType;

namespace Bork2d.Engine.Assets;

/// <summary>
/// A MessagePack custom fragment shader asset: a SPIR-V module reference plus the value-only
/// uniform and texture-slot declarations the renderer consumes.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SpirV"/> references the compiled module as raw data; the engine never compiles or
/// reads shader source. <see cref="LayoutVersion"/> identifies the declaration layout schema this
/// asset was built for; the current version is <c>3</c> (<see cref="ShaderLayout.CurrentVersion"/>).
/// <see cref="EntryPoint"/> names the fragment entry point and defaults to <c>main</c>.
/// </para>
/// <para>
/// Sprite and line renderables share one fragment interface from version <c>3</c> on. Besides the
/// sprite values, the interface carries the line-only inputs <c>vLineUv</c> (across the stroke width
/// and along the line), <c>vColor</c>, and <c>vLineFlags</c>; a sprite stage writes them as neutral
/// values, and a line stage writes neutral sprite values, so the same asset may be assigned to
/// either renderable kind.
/// </para>
/// <para>
/// <see cref="InstanceUniforms"/> declares the per-renderable uniforms inside the shader's 16-float
/// instance block. <see cref="GlobalUniforms"/> declares the engine-wide uniforms inside its 8-slot
/// global block. <see cref="TextureSlots"/> declares additional textures beyond the sprite's own
/// texture, up to four per renderable. The arrays are non-null by default so a loaded asset always
/// has a declaration list; the loader does not validate names, types, offsets, overlaps, or the
/// layout version, and freezing tolerates malformed declarations.
/// </para>
/// <para>
/// Assigning the asset to a sprite renderable freezes these declarations once, and capture reads
/// only that frozen copy. Mutating the asset after assignment — including replacing or editing
/// <see cref="InstanceUniforms"/>, <see cref="GlobalUniforms"/>, or <see cref="TextureSlots"/> —
/// does not change the captured shader metadata or any published snapshot, and re-assigning the
/// same asset keeps the first frozen values. Load or create a new asset instance to change the
/// rendered declaration set. Freezing tolerates null arrays and malformed declarations; the
/// renderer later rejects or falls back from them.
/// </para>
/// <para>
/// Load with <see cref="IAssets.LoadAsset{T}(string)"/> using the canonical shader path.
/// </para>
/// </remarks>
[GenerateShape]
public sealed partial class ShaderAsset
{
    /// <summary>
    /// Gets or sets the referenced SPIR-V module, or an absent reference for no module.
    /// </summary>
    public DataRef SpirV { get; set; }

    /// <summary>
    /// Gets or sets the shader layout schema version the declarations were built for.
    /// </summary>
    /// <remarks>
    /// The engine's current layout version is <c>3</c>. The loader does not validate this value;
    /// the renderer rejects or falls back from a version it does not implement.
    /// </remarks>
    public uint LayoutVersion { get; set; }

    /// <summary>
    /// Gets or sets the fragment entry point name. The default is <c>main</c>.
    /// </summary>
    [DefaultValue("main")]
    public string EntryPoint { get; set; } = "main";

    /// <summary>
    /// Gets or sets the per-renderable uniform declarations inside the 16-float instance block.
    /// </summary>
    /// <remarks>
    /// Each <see cref="ShaderUniform.Offset"/> is a float-lane offset in that block. The array is
    /// never null after construction, but a malformed loaded asset may contain a null array or
    /// invalid declarations; freezing tolerates both.
    /// </remarks>
    public ShaderUniform[] InstanceUniforms { get; set; } = [];

    /// <summary>
    /// Gets or sets the engine-wide uniform declarations inside the 8-slot global block.
    /// </summary>
    /// <remarks>
    /// The time input is not one of these declarations; it is captured separately from
    /// <see cref="IEngine{TLayer}.Time"/>. The array is never null after construction, but freezing
    /// tolerates a null or malformed declaration array.
    /// </remarks>
    public ShaderUniform[] GlobalUniforms { get; set; } = [];

    /// <summary>
    /// Gets or sets the additional texture-slot declarations, up to four per renderable.
    /// </summary>
    /// <remarks>
    /// The array is never null after construction, but freezing tolerates a null or malformed
    /// declaration array. Slot names are matched ordinally against
    /// <see cref="Scene.ISpriteRender{TLayer}.AdditionalTextures"/> positions.
    /// </remarks>
    public ShaderTextureSlot[] TextureSlots { get; set; } = [];
}
