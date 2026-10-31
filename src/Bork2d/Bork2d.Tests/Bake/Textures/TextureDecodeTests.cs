using Bork2d.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Textures;

/// <summary>
/// The texture decode matrix through the single shared reader: RGBA/RGB/gray/palette PNG, BMP/TGA/JPEG
/// normalization, explicit 16-bit rejection, truncation probing, dimension and payload validation,
/// and the decoder seam used to exercise those checks without a real codec.
/// </summary>
public sealed class TextureDecodeTests
{
    private static readonly byte[] _rgbaPayload =
    [
        255, 0, 0, 255,
        0, 255, 0, 128,
        0, 0, 255, 64,
        255, 255, 255, 0,
    ];

    private static readonly byte[] _checkerboardPayload =
    [
        255, 0, 0, 255,
        0, 255, 0, 255,
        0, 0, 255, 255,
        255, 255, 255, 255,
    ];

    [Fact]
    public void RgbaPngDecodesToExactStraightAlphaPayload()
    {
        Decoded result = Decode(TextureImageFactory.RgbaPng());

        Assert.True(result.Success);
        Assert.Equal(_rgbaPayload, result.Payload);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
    }

    [Fact]
    public void RgbPngSuppliesOpaqueAlpha()
    {
        Decoded result = Decode(TextureImageFactory.RgbPng());

        Assert.True(result.Success);
        Assert.Equal(
            new byte[]
            {
                10, 20, 30, 255,
                40, 50, 60, 255,
                70, 80, 90, 255,
                100, 110, 120, 255,
            },
            result.Payload);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
    }

    [Fact]
    public void GrayscalePngIsExpandedToRgba()
    {
        Decoded result = Decode(TextureImageFactory.GrayscalePng());

        Assert.True(result.Success);
        Assert.Equal(
            new byte[]
            {
                0, 0, 0, 255,
                85, 85, 85, 255,
                170, 170, 170, 255,
                255, 255, 255, 255,
            },
            result.Payload);
    }

    [Fact]
    public void PalettePngIsExpandedToRgba()
    {
        Decoded result = Decode(TextureImageFactory.PalettePng());

        Assert.True(result.Success);
        Assert.Equal(_rgbaPayload, result.Payload);
    }

    [Fact]
    public void BmpDecodesTopLeftOrdered()
    {
        Decoded result = Decode(TextureImageFactory.Bmp24());

        Assert.True(result.Success);
        Assert.Equal(_checkerboardPayload, result.Payload);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TgaDecodesTopLeftOrderedForBothOriginFlags(bool topLeftOrigin)
    {
        Decoded result = Decode(TextureImageFactory.Tga24(topLeftOrigin));

        Assert.True(result.Success);
        Assert.Equal(_checkerboardPayload, result.Payload);
    }

    [Fact]
    public void RleTgaIsLeftToTheDecoderAndStillDecodes()
    {
        Decoded result = Decode(TextureImageFactory.RleTga());

        Assert.True(result.Success);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(
            new byte[]
            {
                255, 0, 0, 255,
                255, 0, 0, 255,
                255, 0, 0, 255,
                255, 0, 0, 255,
            },
            result.Payload);
    }

    [Fact]
    public void JpegDecodesThroughTheRealDecoderWithOpaqueAlpha()
    {
        Decoded result = Decode(TextureImageFactory.TinyJpeg());

        Assert.True(result.Success);
        Assert.Equal(
            new byte[]
            {
                220, 30, 30, 255,
                220, 30, 30, 255,
                220, 30, 30, 255,
                220, 30, 30, 255,
            },
            result.Payload);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
    }

    [Fact]
    public void UppercaseAndMixedCaseExtensionsDecode()
    {
        foreach (byte[] source in new[]
        {
            TextureImageFactory.RgbaPng(),
            TextureImageFactory.TinyJpeg(),
            TextureImageFactory.Bmp24(),
            TextureImageFactory.Tga24(topLeftOrigin: true),
        })
        {
            Decoded result = Decode(source);
            Assert.True(result.Success);
        }
    }

    [Fact]
    public void CorruptSourceFailsWithADecodeDiagnostic()
    {
        Decoded result = Decode([1, 2, 3, 4]);

        Assert.False(result.Success);
        Assert.Empty(result.Payload);
        (string code, string message) = Assert.Single(result.Errors);
        Assert.Equal(TextureDiagnosticCodes.TextureDecode, code);
        Assert.Contains("source.bin", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatedPngFailsWithoutAPartialPayload()
    {
        byte[] complete = TextureImageFactory.RgbaPng();

        Decoded result = Decode(complete[..(complete.Length / 2)]);

        Assert.False(result.Success);
        Assert.Empty(result.Payload);
        Assert.Equal(TextureDiagnosticCodes.TextureDecode, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void TruncatedBmpPixelDataIsRejectedBeforeDecoding()
    {
        byte[] complete = TextureImageFactory.Bmp24();

        Decoded result = Decode(complete[..^4]);

        Assert.False(result.Success);
        (string code, string message) = Assert.Single(result.Errors);
        Assert.Equal(TextureDiagnosticCodes.TextureTruncated, code);
        Assert.Contains("BMP", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatedTgaPixelDataIsRejectedBeforeDecoding()
    {
        byte[] complete = TextureImageFactory.Tga24(topLeftOrigin: true);

        Decoded result = Decode(complete[..^6]);

        Assert.False(result.Success);
        (string code, string message) = Assert.Single(result.Errors);
        Assert.Equal(TextureDiagnosticCodes.TextureTruncated, code);
        Assert.Contains("TGA", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatedJpegIsRejectedBeforeDecoding()
    {
        byte[] complete = TextureImageFactory.TinyJpeg();

        Decoded result = Decode(complete[..(complete.Length / 2)]);

        Assert.False(result.Success);
        (string code, string message) = Assert.Single(result.Errors);
        Assert.Equal(TextureDiagnosticCodes.TextureTruncated, code);
        Assert.Contains("JPEG", message, StringComparison.Ordinal);
    }

    [Fact]
    public void JpegWithTrailingBytesAfterEndOfImageStillDecodes()
    {
        byte[] complete = TextureImageFactory.TinyJpeg();
        byte[] trailing = new byte[256];

        Decoded result = Decode([.. complete, .. trailing]);

        Assert.True(result.Success);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(
            new byte[]
            {
                220, 30, 30, 255,
                220, 30, 30, 255,
                220, 30, 30, 255,
                220, 30, 30, 255,
            },
            result.Payload);
    }

    [Fact]
    public void JpegWithTrailingDataLargerThanOneScanChunkStillDecodes()
    {
        byte[] complete = TextureImageFactory.TinyJpeg();
        byte[] trailing = new byte[40000];

        Decoded result = Decode([.. complete, .. trailing]);

        Assert.True(result.Success);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(16, result.Payload.Length);
    }

    [Fact]
    public void UnsupportedContentFailsWithADecodeDiagnostic()
    {
        Decoded result = Decode("this is definitely not an image"u8.ToArray());

        Assert.False(result.Success);
        Assert.Equal(TextureDiagnosticCodes.TextureDecode, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void SixteenBitPngIsRejectedBeforeDecoding()
    {
        Decoded result = Decode(
            TextureImageFactory.HeaderOnlyPng(1, 1, bitDepth: 16, colorType: 2));

        Assert.False(result.Success);
        Assert.Equal(TextureDiagnosticCodes.TextureBitDepth, Assert.Single(result.Errors).Code);
    }

    [Theory]
    [InlineData(0u, 1u)]
    [InlineData(1u, 0u)]
    [InlineData(100000u, 100000u)]
    public void UnsupportedPngHeaderDimensionsAreRejected(uint width, uint height)
    {
        Decoded result = Decode(TextureImageFactory.HeaderOnlyPng(width, height));

        Assert.False(result.Success);
        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void UnsupportedBmpHeaderDimensionsAreRejected()
    {
        Decoded result = Decode(TextureImageFactory.HeaderOnlyBmp(100000, 100000));

        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void ZeroBmpHeaderDimensionIsRejected()
    {
        Decoded result = Decode(TextureImageFactory.HeaderOnlyBmp(0, 2));

        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void BitmapCoreHeaderBmpDecodesWithTheStbDecoder()
    {
        Decoded result = Decode(TextureImageFactory.CoreBmp());

        Assert.True(result.Success);
        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, result.Payload);
    }

    [Fact]
    public void BitmapCoreHeaderReadsTheHeightFromTheCoreHeader()
    {
        // 65535x1 is a valid core-header BMP. Reading the height from offset 22 (the planes field)
        // yields 0x00180001, which the probe would reject as oversized before this 196 KB image
        // could be decoded.
        Decoded result = Decode(TextureImageFactory.CoreBmp(65535, 1));

        Assert.True(result.Success);
        Assert.Equal(65535, result.Width);
        Assert.Equal(1, result.Height);
        Assert.Equal(65535 * 4, result.Payload.Length);
    }

    [Fact]
    public void ZeroBitmapCoreHeaderHeightIsRejected()
    {
        Decoded result = Decode(TextureImageFactory.HeaderOnlyBmpCore(1, 0));

        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void OversizedTgaHeaderDimensionsAreRejected()
    {
        Decoded result = Decode(TextureImageFactory.HeaderOnlyTga(65535, 65535));

        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void ZeroTgaHeaderDimensionIsRejected()
    {
        Decoded result = Decode(TextureImageFactory.HeaderOnlyTga(0, 2));

        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void DecoderSeamZeroDimensionsAreRejected()
    {
        Decoded result = Decode([9, 9, 9], FixedImage(0, 0, []));

        Assert.False(result.Success);
        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void DecoderSeamOversizedDimensionsAreRejectedBeforeAllocation()
    {
        Decoded result = Decode([9, 9, 9], FixedImage(100000, 100000, []));

        Assert.False(result.Success);
        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void DecoderSeamPayloadLengthMismatchIsRejected()
    {
        Decoded result = Decode([9, 9, 9], FixedImage(2, 2, [1, 2, 3]));

        Assert.False(result.Success);
        Assert.Equal(TextureDiagnosticCodes.TexturePayloadSize, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void DecoderSeamOutOfMemoryIsACleanDiagnostic()
    {
        Decoded result = Decode([9, 9, 9], new StubTextureDecoder(_ => throw new OutOfMemoryException()));

        Assert.False(result.Success);
        Assert.Equal(TextureDiagnosticCodes.TextureOutOfMemory, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void DecoderSeamSuppliesPixelsForAJpegSource()
    {
        Decoded result = Decode([9, 9, 9], FixedImage(1, 1, [1, 2, 3, 4]));

        Assert.True(result.Success);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, result.Payload);
        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
    }

    private static Decoded Decode(byte[] source, ITextureDecoder? decoder = null)
    {
        using var directory = TempBakeDirectory.Create();
        string path = directory.WriteFile("source.bin", "placeholder");
        File.WriteAllBytes(path, source);

        var errors = new List<(string Code, string Message)>();
        bool success = decoder is null
            ? TextureSourceReader.TryDecode(
                path,
                Path.GetFileName(path),
                (code, message) => errors.Add((code, message)),
                out byte[] payload,
                out int width,
                out int height)
            : TextureSourceReader.TryDecode(
                path,
                Path.GetFileName(path),
                decoder,
                (code, message) => errors.Add((code, message)),
                out payload,
                out width,
                out height);

        return new Decoded(success, payload, width, height, errors);
    }

    private static ITextureDecoder FixedImage(int width, int height, byte[] pixels)
        => new StubTextureDecoder(_ => new DecodedImage(width, height, pixels));

    private sealed record Decoded(
        bool Success,
        byte[] Payload,
        int Width,
        int Height,
        IReadOnlyList<(string Code, string Message)> Errors);

    private sealed class StubTextureDecoder(Func<Stream, DecodedImage> decode) : ITextureDecoder
    {
        DecodedImage ITextureDecoder.Decode(Stream stream) => decode(stream);
    }
}
