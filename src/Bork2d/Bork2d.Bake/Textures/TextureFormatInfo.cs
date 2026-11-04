using Bork2d.Engine.Assets;

namespace Bork2d.Bake.Textures;

/// <summary>
/// The KTX2 and Data Format Descriptor metadata of one <see cref="TextureFormat"/>.
/// </summary>
/// <remarks>
/// The values mirror the Vulkan format table plus the narrow KTX2 profile this bake writes: one 2D
/// image, one layer, one face, no supercompression, and a complete mip chain. The DFD model, texel
/// block extent, and sample list are the fields the runtime reader compares against the descriptor.
/// </remarks>
internal sealed class TextureFormatInfo
{
    private const byte ColorModelRgbsda = 1;
    private const byte ColorModelBc1A = 128;
    private const byte ColorModelBc4 = 131;
    private const byte ColorModelBc5 = 132;
    private const byte ColorModelBc7 = 134;

    private const byte RedChannel = 0;
    private const byte GreenChannel = 1;
    private const byte BlueChannel = 2;
    private const byte AlphaChannel = 15;

    private static readonly TextureFormatInfo[] _formats =
    [
        new(
            TextureFormat.Rgba8,
            37,
            1,
            1,
            4,
            ColorModelRgbsda,
            RgbaSamples()),
        new(
            TextureFormat.Rgb8,
            37,
            1,
            1,
            4,
            ColorModelRgbsda,
            RgbaSamples()),
        new(
            TextureFormat.Rg8,
            16,
            1,
            1,
            2,
            ColorModelRgbsda,
            [UnormSample(0, RedChannel), UnormSample(8, GreenChannel)]),
        new(
            TextureFormat.R8,
            9,
            1,
            1,
            1,
            ColorModelRgbsda,
            [UnormSample(0, RedChannel)]),
        new(
            TextureFormat.CompRgba8,
            145,
            4,
            4,
            16,
            ColorModelBc7,
            [BlockSample(0, 128, RedChannel)]),
        new(
            TextureFormat.CompRgb8,
            131,
            4,
            4,
            8,
            ColorModelBc1A,
            [BlockSample(0, 64, RedChannel)]),
        new(
            TextureFormat.CompRg8,
            141,
            4,
            4,
            16,
            ColorModelBc5,
            [BlockSample(0, 64, RedChannel), BlockSample(64, 64, GreenChannel)]),
        new(
            TextureFormat.CompR8,
            139,
            4,
            4,
            8,
            ColorModelBc4,
            [BlockSample(0, 64, RedChannel)]),
    ];

    private readonly DfdSample[] _samples;

    private TextureFormatInfo(
        TextureFormat format,
        uint vkFormat,
        int blockWidth,
        int blockHeight,
        int bytesPerBlock,
        byte colorModel,
        DfdSample[] samples)
    {
        Format = format;
        VkFormat = vkFormat;
        BlockWidth = blockWidth;
        BlockHeight = blockHeight;
        BytesPerBlock = bytesPerBlock;
        ColorModel = colorModel;
        _samples = samples;
    }

    /// <summary>Gets the descriptor format this metadata describes.</summary>
    internal TextureFormat Format { get; }

    /// <summary>Gets the Vulkan format value written to the KTX2 header.</summary>
    internal uint VkFormat { get; }

    /// <summary>Gets the texel block width in pixels.</summary>
    internal int BlockWidth { get; }

    /// <summary>Gets the texel block height in pixels.</summary>
    internal int BlockHeight { get; }

    /// <summary>Gets the stored bytes per texel block (or per pixel when uncompressed).</summary>
    internal int BytesPerBlock { get; }

    /// <summary>Gets the Khronos Data Format color model.</summary>
    internal byte ColorModel { get; }

    /// <summary>Gets the ordered DFD samples.</summary>
    internal IReadOnlyList<DfdSample> Samples => _samples;

    /// <summary>Gets whether the format stores 4x4 block-compressed data.</summary>
    internal bool IsBlockCompressed => BlockWidth != 1 || BlockHeight != 1;

    /// <summary>
    /// Gets the alignment KTX2 requires for the first byte of every mip level:
    /// <c>lcm(texelBlockSize, 4)</c>.
    /// </summary>
    internal int RequiredAlignment => LeastCommonMultiple(BytesPerBlock, 4);

    /// <summary>Gets the metadata for a descriptor format.</summary>
    /// <param name="format">The descriptor format.</param>
    /// <returns>The format metadata.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined format.</exception>
    internal static TextureFormatInfo For(TextureFormat format)
    {
        foreach (TextureFormatInfo info in _formats)
        {
            if (info.Format == format)
            {
                return info;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(format),
            format,
            "The texture format has no KTX2 metadata mapping.");
    }

    /// <summary>Gets the stored byte length of one mip level.</summary>
    /// <param name="width">The level's pixel width.</param>
    /// <param name="height">The level's pixel height.</param>
    /// <returns>The stored byte length, without padding.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    internal int GetLevelByteLength(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height must be positive.");
        }

        long blocksWide = IsBlockCompressed ? (width + 3) / 4 : width;
        long blocksHigh = IsBlockCompressed ? (height + 3) / 4 : height;
        return checked((int)(blocksWide * blocksHigh * BytesPerBlock));
    }

    private static DfdSample[] RgbaSamples() =>
    [
        UnormSample(0, RedChannel),
        UnormSample(8, GreenChannel),
        UnormSample(16, BlueChannel),
        UnormSample(24, AlphaChannel),
    ];

    private static DfdSample UnormSample(ushort bitOffset, byte channel) =>
        new(bitOffset, 8, channel, 0, 255);

    private static DfdSample BlockSample(ushort bitOffset, byte bitLength, byte channel) =>
        new(bitOffset, bitLength, channel, 0, uint.MaxValue);

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
