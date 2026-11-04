using Bork2d.Bake;
using Bork2d.Engine.Assets;
using Bork2d.Render.Textures;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Render.Textures;

/// <summary>
/// Deterministic tests for the runtime KTX2 reader: every mapped format parses with the bake's payload
/// (which the runtime never produces itself), and every narrow-profile header, DFD, level-index, and
/// level-length violation is a content failure rather than a crash or a partial parse.
/// </summary>
public sealed class Ktx2ReaderTests
{
    private const int HeaderSize = 80;
    private const int LevelIndexEntrySize = 24;
    private const int DfdOffsetField = 48;
    private const int DfdLengthField = 52;

    private static readonly TextureFormat[] _formats =
    [
        TextureFormat.Rgba8,
        TextureFormat.Rgb8,
        TextureFormat.Rg8,
        TextureFormat.R8,
        TextureFormat.CompRgba8,
        TextureFormat.CompRgb8,
        TextureFormat.CompRg8,
        TextureFormat.CompR8,
    ];

    [Theory]
    [MemberData(nameof(Formats))]
    public void ParsesEveryMappedFormat(TextureFormat format)
    {
        BakedTexture baked = Ktx2TextureFactory.BakeSolid(format, 5, 5, 200, 100, 50);
        TextureDescriptor descriptor = Descriptor(baked, format, width: 5, height: 5);

        bool parsed = Ktx2Reader.TryParse(
            baked.Payload.Span,
            descriptor,
            out Ktx2Texture texture,
            out TexturePageFailure failure);

        Assert.True(parsed, failure.ToString());
        Assert.Equal(TexturePageFailure.None, failure);
        Assert.Equal(format, texture.Format);
        Assert.Equal(5u, texture.Width);
        Assert.Equal(5u, texture.Height);
        Assert.Equal(3, texture.LevelCount);

        RenderTextureFormat info = RenderTextureFormat.Get(format);
        for (var level = 0; level < texture.LevelCount; level++)
        {
            Ktx2Level levelData = texture.Levels[level];
            int levelWidth = Ktx2Reader.LevelExtent(texture.Width, level);
            int levelHeight = Ktx2Reader.LevelExtent(texture.Height, level);
            Assert.Equal(
                (ulong)info.GetLevelByteLength(levelWidth, levelHeight),
                levelData.ByteLength);
            Assert.Equal(0UL, levelData.ByteOffset % (ulong)info.RequiredAlignment);
        }
    }

    [Fact]
    public void BaseLevelStartsWithTheLargestStoredLength()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);

        Assert.True(
            Ktx2Reader.TryParse(
                baked.Payload.Span,
                descriptor,
                out Ktx2Texture texture,
                out _),
            "The baked payload did not parse.");

        Assert.True(texture.Levels[0].ByteLength > texture.Levels[1].ByteLength);
        Assert.True(texture.Levels[0].ByteOffset > texture.Levels[1].ByteOffset);
    }

    [Fact]
    public void RejectsAnUnsupportedDescriptorFormat()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        descriptor.Format = (TextureFormat)99;

        AssertFailure(baked.Payload.ToArray(), descriptor, TexturePageFailure.UnsupportedFormat);
    }

    [Fact]
    public void RejectsAWrongVkFormat()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        byte[] payload = baked.Payload.ToArray();
        WriteUInt32(payload, 12, 999);

        AssertFailure(payload, descriptor, TexturePageFailure.FormatMismatch);
    }

    [Fact]
    public void RejectsWrongBaseDimensions()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        descriptor.Width = 5;

        AssertFailure(baked.Payload.ToArray(), descriptor, TexturePageFailure.DimensionMismatch);
    }

    [Fact]
    public void RejectsAWrongHeaderLevelCount()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        byte[] payload = baked.Payload.ToArray();
        WriteUInt32(payload, 40, 1);

        AssertFailure(payload, descriptor, TexturePageFailure.MipMismatch);
    }

    [Fact]
    public void RejectsADescriptorMipCountThatDoesNotMatchThePayload()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        descriptor.MipLevels = 1;

        AssertFailure(baked.Payload.ToArray(), descriptor, TexturePageFailure.MipMismatch);
    }

    [Fact]
    public void RejectsAChainThatIsNotComplete()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        byte[] payload = baked.Payload.ToArray();
        WriteUInt32(payload, 40, 1);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        descriptor.MipLevels = 1;

        AssertFailure(payload, descriptor, TexturePageFailure.MipMismatch);
    }

    [Fact]
    public void RejectsNonKtx2Bytes()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        byte[] payload = baked.Payload.ToArray();
        payload[0] = 0;

        AssertFailure(payload, descriptor, TexturePageFailure.MalformedKtx2);
    }

    [Fact]
    public void RejectsATruncatedHeader()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);

        AssertFailure(
            baked.Payload.Span[..(HeaderSize - 1)].ToArray(),
            descriptor,
            TexturePageFailure.MalformedKtx2);
    }

    [Fact]
    public void RejectsATruncatedLevel()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        byte[] payload = baked.Payload.ToArray()[..^1];

        AssertFailure(payload, descriptor, TexturePageFailure.MalformedKtx2);
    }

    [Fact]
    public void RejectsAWrongTypeSize()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        byte[] payload = baked.Payload.ToArray();
        WriteUInt32(payload, 16, 2);

        AssertFailure(payload, descriptor, TexturePageFailure.MalformedKtx2);
    }

    [Theory]
    [InlineData(28, 1u)]
    [InlineData(32, 1u)]
    [InlineData(36, 2u)]
    [InlineData(44, 1u)]
    public void RejectsANonzeroProfileField(int fieldOffset, uint value)
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        byte[] payload = baked.Payload.ToArray();
        WriteUInt32(payload, fieldOffset, value);

        AssertFailure(payload, descriptor, TexturePageFailure.MalformedKtx2);
    }

    [Fact]
    public void RejectsKeyValueData()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        byte[] payload = baked.Payload.ToArray();
        WriteUInt32(payload, 56, 100);
        WriteUInt32(payload, 60, 1);

        AssertFailure(payload, descriptor, TexturePageFailure.MalformedKtx2);
    }

    [Fact]
    public void RejectsSupercompressionGlobalData()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        byte[] payload = baked.Payload.ToArray();
        WriteUInt64(payload, 64, 100);
        WriteUInt64(payload, 72, 16);

        AssertFailure(payload, descriptor, TexturePageFailure.MalformedKtx2);
    }

    [Fact]
    public void RejectsDfdDataOutsideThePayload()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        byte[] payload = baked.Payload.ToArray();
        WriteUInt32(payload, DfdLengthField, (uint)payload.Length);

        AssertFailure(payload, descriptor, TexturePageFailure.MalformedKtx2);
    }

    [Fact]
    public void RejectsDfdTotalSizeMismatch()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        byte[] payload = baked.Payload.ToArray();
        uint dfdOffset = ReadUInt32(payload, DfdOffsetField);
        WriteUInt32(payload, (int)dfdOffset, ReadUInt32(payload, (int)dfdOffset) + 1);

        AssertFailure(payload, descriptor, TexturePageFailure.MalformedKtx2);
    }

    [Fact]
    public void RejectsALevelOutsideThePayload()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        byte[] payload = baked.Payload.ToArray();
        ulong levelOffset = (ulong)payload.Length;
        WriteUInt64(payload, HeaderSize, levelOffset);

        AssertFailure(payload, descriptor, TexturePageFailure.MalformedKtx2);
    }

    [Fact]
    public void RejectsAMisalignedLevelOffset()
    {
        BakedTexture baked = Bake(TextureFormat.CompRgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.CompRgba8, 4, 4);
        byte[] payload = baked.Payload.ToArray();
        ulong levelOffset = ReadUInt64(payload, HeaderSize);
        WriteUInt64(payload, HeaderSize, levelOffset + 1);

        AssertFailure(payload, descriptor, TexturePageFailure.MalformedKtx2);
    }

    [Fact]
    public void RejectsAWrongLevelLength()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        byte[] payload = baked.Payload.ToArray();
        ulong levelLength = ReadUInt64(payload, HeaderSize + 8);
        WriteUInt64(payload, HeaderSize + 8, levelLength + 4);

        AssertFailure(payload, descriptor, TexturePageFailure.MalformedKtx2);
    }

    [Fact]
    public void RejectsAMismatchedUncompressedLength()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);
        byte[] payload = baked.Payload.ToArray();
        ulong levelLength = ReadUInt64(payload, HeaderSize + 8);
        WriteUInt64(payload, HeaderSize + 16, levelLength + 1);

        AssertFailure(payload, descriptor, TexturePageFailure.MalformedKtx2);
    }

    [Fact]
    public void RejectsAMissingPayload()
    {
        BakedTexture baked = Bake(TextureFormat.Rgba8);
        TextureDescriptor descriptor = Descriptor(baked, TextureFormat.Rgba8, 4, 4);

        AssertFailure([], descriptor, TexturePageFailure.MalformedKtx2);
    }

    /// <summary>Gets every mapped format as a theory row.</summary>
    /// <returns>The formats.</returns>
    public static TheoryData<TextureFormat> Formats()
    {
        var data = new TheoryData<TextureFormat>();
        foreach (TextureFormat format in _formats)
        {
            data.Add(format);
        }

        return data;
    }

    private static TextureDescriptor Descriptor(
        BakedTexture baked,
        TextureFormat format,
        uint width,
        uint height)
    {
        Assert.Equal(format, baked.Format);
        return Ktx2TextureFactory.Descriptor("data/test-ktx2", baked, width, height);
    }

    private static BakedTexture Bake(TextureFormat format)
        => Ktx2TextureFactory.BakeSolid(format, 4, 4, 200, 100, 50);

    private static void AssertFailure(
        byte[] payload,
        TextureDescriptor descriptor,
        TexturePageFailure expected)
    {
        bool parsed = Ktx2Reader.TryParse(
            payload,
            descriptor,
            out _,
            out TexturePageFailure failure);

        Assert.False(parsed);
        Assert.Equal(expected, failure);
    }

    private static uint ReadUInt32(byte[] payload, int offset)
        => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));

    private static ulong ReadUInt64(byte[] payload, int offset)
        => System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(offset));

    private static void WriteUInt32(byte[] payload, int offset, uint value)
        => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan(offset),
            value);

    private static void WriteUInt64(byte[] payload, int offset, ulong value)
        => System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(
            payload.AsSpan(offset),
            value);
}
