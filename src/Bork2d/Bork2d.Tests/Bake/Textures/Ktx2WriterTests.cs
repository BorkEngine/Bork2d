using Bork2d.Bake.Textures;
using Bork2d.Engine.Assets;
using Xunit;

namespace Bork2d.Tests.Bake.Textures;

/// <summary>
/// The narrow KTX2 writer: identifier, header, level index, Data Format Descriptor, level ordering,
/// mip padding, and the stored lengths of every supported format.
/// </summary>
public sealed class Ktx2WriterTests
{
    private const int HeaderSize = 80;
    private const int LevelIndexEntrySize = 24;

    private static readonly ExpectedDfd[] _expectedDfds =
    [
        new ExpectedDfd(
            TextureFormat.Rgba8,
            37,
            1,
            1,
            1,
            4,
            [
                new DfdSample(0, 8, 0, 0, 255),
                new DfdSample(8, 8, 1, 0, 255),
                new DfdSample(16, 8, 2, 0, 255),
                new DfdSample(24, 8, 15, 0, 255),
            ]),
        new ExpectedDfd(
            TextureFormat.Rgb8,
            37,
            1,
            1,
            1,
            4,
            [
                new DfdSample(0, 8, 0, 0, 255),
                new DfdSample(8, 8, 1, 0, 255),
                new DfdSample(16, 8, 2, 0, 255),
                new DfdSample(24, 8, 15, 0, 255),
            ]),
        new ExpectedDfd(
            TextureFormat.Rg8,
            16,
            1,
            1,
            1,
            2,
            [
                new DfdSample(0, 8, 0, 0, 255),
                new DfdSample(8, 8, 1, 0, 255),
            ]),
        new ExpectedDfd(
            TextureFormat.R8,
            9,
            1,
            1,
            1,
            1,
            [new DfdSample(0, 8, 0, 0, 255)]),
        new ExpectedDfd(
            TextureFormat.CompRgba8,
            145,
            134,
            4,
            4,
            16,
            [new DfdSample(0, 128, 0, 0, uint.MaxValue)]),
        new ExpectedDfd(
            TextureFormat.CompRgb8,
            131,
            128,
            4,
            4,
            8,
            [new DfdSample(0, 64, 0, 0, uint.MaxValue)]),
        new ExpectedDfd(
            TextureFormat.CompRg8,
            141,
            132,
            4,
            4,
            16,
            [
                new DfdSample(0, 64, 0, 0, uint.MaxValue),
                new DfdSample(64, 64, 1, 0, uint.MaxValue),
            ]),
        new ExpectedDfd(
            TextureFormat.CompR8,
            139,
            131,
            4,
            4,
            8,
            [new DfdSample(0, 64, 0, 0, uint.MaxValue)]),
    ];

    public static TheoryData<TextureFormat> DfdFormats => new()
    {
        TextureFormat.Rgba8,
        TextureFormat.Rgb8,
        TextureFormat.Rg8,
        TextureFormat.R8,
        TextureFormat.CompRgba8,
        TextureFormat.CompRgb8,
        TextureFormat.CompRg8,
        TextureFormat.CompR8,
    };

    [Fact]
    public void WritesTheIdentifierAndNarrowHeader()
    {
        (Ktx2TestDocument document, _) = Write(TextureFormat.Rgba8, 2, 2);

        Assert.Equal(Ktx2TestDocument.Identifier, document.Bytes[..12]);
        Assert.Equal(37u, document.VkFormat);
        Assert.Equal(1u, document.TypeSize);
        Assert.Equal(2u, document.Width);
        Assert.Equal(2u, document.Height);
        Assert.Equal(0u, document.Depth);
        Assert.Equal(0u, document.LayerCount);
        Assert.Equal(1u, document.FaceCount);
        Assert.Equal(2u, document.LevelCount);
        Assert.Equal(0u, document.SupercompressionScheme);
        Assert.Equal(0u, document.KvdByteOffset);
        Assert.Equal(0u, document.KvdByteLength);
        Assert.Equal(0ul, document.SgdByteOffset);
        Assert.Equal(0ul, document.SgdByteLength);
        Assert.Equal((uint)(HeaderSize + (LevelIndexEntrySize * 2)), document.DfdByteOffset);
        Assert.Equal(document.DfdTotalSize, document.DfdByteLength);
        Assert.Equal(92u, document.DfdTotalSize);
    }

    [Fact]
    public void WritesTheLevelIndexBaseFirstAndDataSmallestLevelFirst()
    {
        // A 4x2 RGBA8 chain is 4x2, 2x1, 1x1: 32, 8, and 4 stored bytes.
        (Ktx2TestDocument document, byte[][] levels) = Write(TextureFormat.Rgba8, 4, 2);

        Assert.Equal(3u, document.LevelCount);
        Assert.Equal(32u, document.Levels[0].ByteLength);
        Assert.Equal(8u, document.Levels[1].ByteLength);
        Assert.Equal(4u, document.Levels[2].ByteLength);
        Assert.All(
            document.Levels,
            entry => Assert.Equal(entry.ByteLength, entry.UncompressedByteLength));

        // KTX2 orders the mip level array smallest first while the level index stays base first.
        Assert.True(document.Levels[0].ByteOffset > document.Levels[1].ByteOffset);
        Assert.True(document.Levels[1].ByteOffset > document.Levels[2].ByteOffset);
        Assert.Equal(288ul, (ulong)document.Bytes.Length);
        Assert.Equal(256ul, document.Levels[0].ByteOffset);
        Assert.Equal(248ul, document.Levels[1].ByteOffset);
        Assert.Equal(244ul, document.Levels[2].ByteOffset);
        Assert.All(document.Levels, entry => Assert.Equal(0ul, entry.ByteOffset % 4));

        for (int level = 0; level < levels.Length; level++)
        {
            Assert.Equal(levels[level], document.GetLevelBytes(level));
            Assert.True(
                document.Levels[level].ByteOffset >= (ulong)HeaderSize,
                "A level must follow the header.");
        }
    }

    [Fact]
    public void AlignsEveryLevelStartToTheTexelBlockSize()
    {
        // BC7 uses 16-byte blocks, so the level data starts on a 16-byte boundary after the DFD.
        (Ktx2TestDocument document, byte[][] levels) = Write(TextureFormat.CompRgba8, 4, 4);

        Assert.Equal(3u, document.LevelCount);
        Assert.Equal(256, document.Bytes.Length);
        Assert.Equal(240ul, document.Levels[0].ByteOffset);
        Assert.Equal(224ul, document.Levels[1].ByteOffset);
        Assert.Equal(208ul, document.Levels[2].ByteOffset);
        Assert.All(document.Levels, entry => Assert.Equal(0ul, entry.ByteOffset % 16));
        Assert.All(document.Levels, entry => Assert.Equal(16ul, entry.ByteLength));
        Assert.Equal(levels[0], document.GetLevelBytes(0));

        // The padding inserted after the DFD and between levels is zero.
        for (int level = 2; level >= 1; level--)
        {
            ulong end = document.Levels[level].ByteOffset + document.Levels[level].ByteLength;
            for (ulong offset = end; offset < document.Levels[level - 1].ByteOffset; offset++)
            {
                Assert.Equal(0, document.Bytes[(int)offset]);
            }
        }
    }

    [Theory]
    [InlineData(TextureFormat.Rgba8)]
    [InlineData(TextureFormat.Rgb8)]
    [InlineData(TextureFormat.Rg8)]
    [InlineData(TextureFormat.R8)]
    [InlineData(TextureFormat.CompRgba8)]
    [InlineData(TextureFormat.CompRgb8)]
    [InlineData(TextureFormat.CompRg8)]
    [InlineData(TextureFormat.CompR8)]
    public void WritesAlignedLevelOffsetsAndLengthsForEveryFormat(TextureFormat format)
    {
        const int width = 5;
        const int height = 3;
        (Ktx2TestDocument document, byte[][] levels) = Write(format, width, height);

        TextureFormatInfo info = TextureFormatInfo.For(format);
        int expectedLevels = TextureMipChain.LevelCount(width, height);
        Assert.Equal((uint)expectedLevels, document.LevelCount);
        Assert.Equal(expectedLevels, document.Levels.Count);

        for (int level = 0; level < expectedLevels; level++)
        {
            int levelWidth = TextureMipChain.LevelWidth(width, level);
            int levelHeight = TextureMipChain.LevelHeight(height, level);
            Assert.Equal(
                (ulong)info.GetLevelByteLength(levelWidth, levelHeight),
                document.Levels[level].ByteLength);
            Assert.Equal(
                0ul,
                document.Levels[level].ByteOffset % (ulong)info.RequiredAlignment);
            Assert.Equal(levels[level], document.GetLevelBytes(level));
        }

        // The level index is base first, while the file stores the smallest level first.
        for (int level = 0; level < expectedLevels - 1; level++)
        {
            Assert.True(document.Levels[level].ByteOffset > document.Levels[level + 1].ByteOffset);
        }

        Assert.Equal(
            document.Levels[0].ByteOffset + document.Levels[0].ByteLength,
            (ulong)document.Bytes.Length);
    }

    [Fact]
    public void PadsOddUncompressedLevelsToAFourByteBoundary()
    {
        // R8 has a one-byte texel block, so a 3x3 -> 1x1 chain needs padding after the 1x1 level.
        (Ktx2TestDocument document, byte[][] levels) = Write(TextureFormat.R8, 3, 3);

        Assert.Equal(2u, document.LevelCount);
        Assert.Equal(9ul, document.Levels[0].ByteLength);
        Assert.Equal(1ul, document.Levels[1].ByteLength);
        Assert.Equal(172ul, document.Levels[1].ByteOffset);
        Assert.Equal(176ul, document.Levels[0].ByteOffset);
        Assert.Equal(185, document.Bytes.Length);
        Assert.All(document.Levels, entry => Assert.Equal(0ul, entry.ByteOffset % 4));
        Assert.Equal(levels[0], document.GetLevelBytes(0));
    }

    [Theory]
    [MemberData(nameof(DfdFormats))]
    public void WritesTheDataFormatDescriptorForEveryFormat(TextureFormat format)
    {
        ExpectedDfd expected = _expectedDfds.Single(candidate => candidate.Format == format);
        (Ktx2TestDocument document, _) = Write(format, 4, 4);

        Assert.Equal(expected.VkFormat, document.VkFormat);
        Assert.Equal(1u, document.TypeSize);
        Assert.Equal(2u, document.DfdVersionNumber);
        Assert.Equal(1, document.DfdPrimaries);
        Assert.Equal(1, document.DfdTransfer);
        Assert.Equal(0, document.DfdFlags);
        Assert.Equal(expected.ColorModel, document.DfdColorModel);
        Assert.Equal(expected.BlockWidth, document.DfdTexelBlockWidth);
        Assert.Equal(expected.BlockHeight, document.DfdTexelBlockHeight);
        Assert.Equal(expected.BytesPerBlock, document.DfdBytesPlane0);
        Assert.Equal(0, document.DfdBytesPlane1);
        Assert.Equal((uint)(4 + 24 + (16 * expected.Samples.Length)), document.DfdTotalSize);
        Assert.Equal((uint)(24 + (16 * expected.Samples.Length)), document.DfdDescriptorBlockSize);
        Assert.Equal(expected.Samples, document.Samples.ToArray());
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 2)]
    [InlineData(4, 2, 3)]
    [InlineData(5, 3, 3)]
    [InlineData(8, 1, 4)]
    [InlineData(64, 32, 7)]
    public void WritesEveryLevelOfTheFullChainDownToOneByOne(
        int width,
        int height,
        int expectedLevels)
    {
        (Ktx2TestDocument document, _) = Write(TextureFormat.Rgba8, width, height);

        Assert.Equal((uint)expectedLevels, document.LevelCount);
        Assert.Equal(expectedLevels, document.Levels.Count);
        TextureFormatInfo info = TextureFormatInfo.For(TextureFormat.Rgba8);
        Assert.Equal(
            (ulong)info.GetLevelByteLength(1, 1),
            document.Levels[^1].ByteLength);
        Assert.Equal((uint)width, document.Width);
        Assert.Equal((uint)height, document.Height);
    }

    [Theory]
    [InlineData(TextureFormat.CompRgba8, 16)]
    [InlineData(TextureFormat.CompRgb8, 8)]
    [InlineData(TextureFormat.CompRg8, 16)]
    [InlineData(TextureFormat.CompR8, 8)]
    public void UsesTheBlockExtentForCompressedLevelLengths(TextureFormat format, int blockBytes)
    {
        (Ktx2TestDocument document, _) = Write(format, 5, 5);

        Assert.Equal(3u, document.LevelCount);
        Assert.Equal((ulong)(blockBytes * 4), document.Levels[0].ByteLength);
        Assert.Equal((ulong)blockBytes, document.Levels[1].ByteLength);
        Assert.Equal((ulong)blockBytes, document.Levels[2].ByteLength);
        Assert.All(document.Levels, entry => Assert.Equal(entry.ByteLength, entry.UncompressedByteLength));
    }

    [Fact]
    public void RejectsAMipChainThatIsNotComplete()
    {
        TextureFormatInfo info = TextureFormatInfo.For(TextureFormat.Rgba8);
        byte[][] levels = BuildLevels(info, 4, 4);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Ktx2Writer.Write(info, 4, 4, levels[..^1]));
        Assert.Contains("mip levels", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsALevelWithTheWrongStoredLength()
    {
        TextureFormatInfo info = TextureFormatInfo.For(TextureFormat.Rgba8);
        byte[][] levels = BuildLevels(info, 4, 4);
        levels[1] = new byte[levels[1].Length + 1];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Ktx2Writer.Write(info, 4, 4, levels));
        Assert.Contains("expected", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAMissingLevel()
    {
        TextureFormatInfo info = TextureFormatInfo.For(TextureFormat.Rgba8);
        byte[][] levels = BuildLevels(info, 4, 4);
        levels[0] = null!;

        Assert.Throws<ArgumentNullException>(() => Ktx2Writer.Write(info, 4, 4, levels));
    }

    [Fact]
    public void RejectsNonPositiveDimensions()
    {
        TextureFormatInfo info = TextureFormatInfo.For(TextureFormat.Rgba8);

        Assert.Throws<ArgumentOutOfRangeException>(() => Ktx2Writer.Write(info, 0, 4, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ktx2Writer.Write(info, 4, 0, []));
    }

    private static (Ktx2TestDocument Document, byte[][] Levels) Write(
        TextureFormat format,
        int width,
        int height)
    {
        TextureFormatInfo info = TextureFormatInfo.For(format);
        byte[][] levels = BuildLevels(info, width, height);
        byte[] payload = Ktx2Writer.Write(info, width, height, levels);
        return (Ktx2TestDocument.Parse(payload), levels);
    }

    private static byte[][] BuildLevels(TextureFormatInfo info, int width, int height)
    {
        int levelCount = TextureMipChain.LevelCount(width, height);
        var levels = new byte[levelCount][];
        for (int level = 0; level < levelCount; level++)
        {
            int levelWidth = TextureMipChain.LevelWidth(width, level);
            int levelHeight = TextureMipChain.LevelHeight(height, level);
            int length = info.GetLevelByteLength(levelWidth, levelHeight);
            levels[level] = new byte[length];
            for (int index = 0; index < length; index++)
            {
                levels[level][index] = (byte)((level * 37) + index + 1);
            }
        }

        return levels;
    }

    /// <summary>The expected DFD values of one format.</summary>
    internal sealed record ExpectedDfd(
        TextureFormat Format,
        uint VkFormat,
        byte ColorModel,
        int BlockWidth,
        int BlockHeight,
        int BytesPerBlock,
        DfdSample[] Samples);
}
