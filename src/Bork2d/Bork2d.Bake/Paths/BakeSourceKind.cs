namespace Bork2d.Bake.Paths;

/// <summary>
/// The source-file kinds whose relative path maps to a canonical asset path.
/// </summary>
internal enum BakeSourceKind
{
    /// <summary>A custom-data or sprite <c>.toml</c> source.</summary>
    DataToml,

    /// <summary>A texture source image (PNG, JPG/JPEG, BMP, or TGA).</summary>
    TextureSource,

    /// <summary>A <c>&lt;source image&gt;.toml</c> texture sidecar.</summary>
    TextureSidecar,
}
