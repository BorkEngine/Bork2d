using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Silk.NET.Vulkan;
using SilkFormat = Silk.NET.Vulkan.Format;

namespace Bork2d.Render.Textures;

/// <summary>
/// The KTX2 header, Vulkan, and level-layout metadata of one <see cref="TextureFormat"/>.
/// </summary>
/// <remarks>
/// <para>
/// The runtime keeps its own copy of the narrow KTX2 profile's format table instead of referencing the
/// bake tooling: <see cref="VkFormat"/> is the header value a payload must carry, <see cref="VulkanFormat"/>
/// is the concrete format the page image uses, and the texel-block fields derive every mip level's
/// stored byte length and alignment.
/// </para>
/// <para>
/// A format with no mapping is a content failure while validating a page and a fatal
/// <see cref="Bork2dException"/> if it ever reaches the upload seam. Sampling support is queried from
/// the device's format properties before an image is created; there is no fallback format.
/// </para>
/// </remarks>
internal sealed class RenderTextureFormat
{
    private static readonly RenderTextureFormat[] _formats =
    [
        new(TextureFormat.Rgba8, 37, SilkFormat.R8G8B8A8Unorm, 1, 1, 4),
        new(TextureFormat.Rgb8, 37, SilkFormat.R8G8B8A8Unorm, 1, 1, 4),
        new(TextureFormat.Rg8, 16, SilkFormat.R8G8Unorm, 1, 1, 2),
        new(TextureFormat.R8, 9, SilkFormat.R8Unorm, 1, 1, 1),
        new(TextureFormat.CompRgba8, 145, SilkFormat.BC7UnormBlock, 4, 4, 16),
        new(TextureFormat.CompRgb8, 131, SilkFormat.BC1RgbUnormBlock, 4, 4, 8),
        new(TextureFormat.CompRg8, 141, SilkFormat.BC5UnormBlock, 4, 4, 16),
        new(TextureFormat.CompR8, 139, SilkFormat.BC4UnormBlock, 4, 4, 8),
    ];

    private RenderTextureFormat(
        TextureFormat format,
        uint vkFormat,
        SilkFormat vulkanFormat,
        int blockWidth,
        int blockHeight,
        int bytesPerBlock)
    {
        Format = format;
        VkFormat = vkFormat;
        VulkanFormat = vulkanFormat;
        BlockWidth = blockWidth;
        BlockHeight = blockHeight;
        BytesPerBlock = bytesPerBlock;
        RequiredAlignment = LeastCommonMultiple(bytesPerBlock, 4);
    }

    /// <summary>Gets the descriptor format this metadata describes.</summary>
    internal TextureFormat Format { get; }

    /// <summary>Gets the Vulkan format value the KTX2 header must carry.</summary>
    internal uint VkFormat { get; }

    /// <summary>Gets the concrete Vulkan format used by the page image.</summary>
    internal SilkFormat VulkanFormat { get; }

    /// <summary>Gets the texel block width in pixels.</summary>
    internal int BlockWidth { get; }

    /// <summary>Gets the texel block height in pixels.</summary>
    internal int BlockHeight { get; }

    /// <summary>Gets the stored bytes per texel block (or per pixel when uncompressed).</summary>
    internal int BytesPerBlock { get; }

    /// <summary>
    /// Gets the alignment KTX2 requires for a mip level's first byte:
    /// <c>lcm(texelBlockSize, 4)</c>.
    /// </summary>
    internal int RequiredAlignment { get; }

    /// <summary>Gets whether the format stores 4x4 block-compressed data.</summary>
    internal bool IsBlockCompressed => BlockWidth != 1 || BlockHeight != 1;

    /// <summary>Gets the metadata for a descriptor format, or <see langword="null"/> when unmapped.</summary>
    /// <param name="format">The descriptor format.</param>
    /// <returns>The format metadata, or <see langword="null"/> for an undefined value.</returns>
    internal static RenderTextureFormat? TryGet(TextureFormat format)
    {
        foreach (RenderTextureFormat info in _formats)
        {
            if (info.Format == format)
            {
                return info;
            }
        }

        return null;
    }

    /// <summary>Gets the metadata for a descriptor format that the upload seam accepted.</summary>
    /// <param name="format">The descriptor format.</param>
    /// <returns>The format metadata.</returns>
    /// <exception cref="Bork2dException">The value has no Vulkan mapping.</exception>
    internal static RenderTextureFormat Get(TextureFormat format)
        => TryGet(format) ?? throw new Bork2dException(
            $"Texture format {format} has no concrete Vulkan format mapping. Every texture page "
                + "must be a validated KTX2 payload whose format is one of the eight linear or BC "
                + "formats; Bork2d requires the desktop Vulkan 1.3 textureCompressionBC feature for "
                + "block-compressed formats and offers no fallback.");

    /// <summary>Gets the stored byte length of one mip level.</summary>
    /// <param name="width">The level's pixel width; positive.</param>
    /// <param name="height">The level's pixel height; positive.</param>
    /// <returns>The stored byte length, without alignment padding.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    internal int GetLevelByteLength(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        long blocksWide = IsBlockCompressed ? (width + 3) / 4 : width;
        long blocksHigh = IsBlockCompressed ? (height + 3) / 4 : height;
        return checked((int)(blocksWide * blocksHigh * BytesPerBlock));
    }

    /// <summary>
    /// Gets whether the device's optimal-tiling features include sampling for this format.
    /// </summary>
    /// <param name="properties">The properties returned by <c>vkGetPhysicalDeviceFormatProperties</c>.</param>
    /// <returns><see langword="true"/> when the format can be sampled.</returns>
    internal static bool SupportsSampling(in FormatProperties properties)
        => (properties.OptimalTilingFeatures & FormatFeatureFlags.SampledImageBit) != 0;

    /// <summary>Creates the fatal exception for a format the device cannot sample.</summary>
    /// <param name="format">The mapped format.</param>
    /// <returns>The exception naming the format and the texture-compression requirement.</returns>
    internal static Bork2dException CreateUnsupportedFormatException(RenderTextureFormat format)
        => new(
            $"The Vulkan device cannot sample texture format {format.Format} "
                + $"({format.VulkanFormat}). Bork2d requires the desktop Vulkan 1.3 "
                + "textureCompressionBC feature and sampled-image support for every baked KTX2 "
                + "format; the renderer never falls back to another format or the placeholder for a "
                + "requested texture that the device cannot sample.");

    private static int LeastCommonMultiple(int left, int right)
    {
        int a = left;
        int b = right;
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return (left / a) * right;
    }
}
