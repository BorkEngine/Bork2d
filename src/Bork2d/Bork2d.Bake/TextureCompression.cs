namespace Bork2d.Bake;

/// <summary>
/// The compression mode selected by a texture sidecar's <c>compression</c> key.
/// </summary>
/// <remarks>
/// <see cref="None"/> is the default so an existing texture never becomes lossy without an explicit
/// authoring choice. <see cref="Auto"/> selects the block format that matches the sidecar's channel
/// type: BC7 for <see cref="TextureChannelType.Rgba"/>, opaque BC1 for
/// <see cref="TextureChannelType.Rgb"/>, BC5 for <see cref="TextureChannelType.Rg"/> and BC4 for
/// <see cref="TextureChannelType.R"/>.
/// </remarks>
public enum TextureCompression
{
    /// <summary>Uncompressed linear UNORM storage.</summary>
    None = 0,

    /// <summary>The block format that matches the channel type.</summary>
    Auto = 1,
}
