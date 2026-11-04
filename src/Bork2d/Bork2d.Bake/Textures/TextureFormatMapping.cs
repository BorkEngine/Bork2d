using Bork2d.Engine.Assets;

namespace Bork2d.Bake.Textures;

/// <summary>
/// Maps the sidecar's semantic channel type and compression mode to a concrete texture format.
/// </summary>
/// <remarks>
/// The mapping is total for every defined enum value. An undefined value is a public-API guard in
/// <see cref="TextureBaker"/> and an argument exception here.
/// </remarks>
internal static class TextureFormatMapping
{
    /// <summary>Maps a channel type and compression mode to the concrete format.</summary>
    /// <param name="channelType">The sidecar channel type.</param>
    /// <param name="compression">The sidecar compression mode.</param>
    /// <returns>The concrete format.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is not a defined enum member.</exception>
    internal static TextureFormat Map(TextureChannelType channelType, TextureCompression compression)
        => (channelType, compression) switch
        {
            (TextureChannelType.Rgba, TextureCompression.None) => TextureFormat.Rgba8,
            (TextureChannelType.Rgb, TextureCompression.None) => TextureFormat.Rgb8,
            (TextureChannelType.Rg, TextureCompression.None) => TextureFormat.Rg8,
            (TextureChannelType.R, TextureCompression.None) => TextureFormat.R8,
            (TextureChannelType.Rgba, TextureCompression.Auto) => TextureFormat.CompRgba8,
            (TextureChannelType.Rgb, TextureCompression.Auto) => TextureFormat.CompRgb8,
            (TextureChannelType.Rg, TextureCompression.Auto) => TextureFormat.CompRg8,
            (TextureChannelType.R, TextureCompression.Auto) => TextureFormat.CompR8,
            _ => throw new ArgumentOutOfRangeException(
                nameof(channelType),
                channelType,
                "The channel type and compression mode do not map to a texture format."),
        };

    /// <summary>Gets whether both values are defined enum members.</summary>
    /// <param name="channelType">The sidecar channel type.</param>
    /// <param name="compression">The sidecar compression mode.</param>
    /// <returns><see langword="true"/> when both values are defined.</returns>
    internal static bool IsDefined(TextureChannelType channelType, TextureCompression compression)
        => Enum.IsDefined(channelType) && Enum.IsDefined(compression);
}
