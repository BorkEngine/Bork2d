using Bork2d.Api;
using Bork2d.Assets.Pack;
using Xunit;

namespace Bork2d.Tests.Assets.Pack;

public sealed class PackValidationTests
{
    [Fact]
    public void RejectsShortFiles()
    {
        PackTestSupport.AssertInvalidPack([]);
        PackTestSupport.AssertInvalidPack(new byte[27]);
    }

    [Fact]
    public void RejectsBadMagic()
    {
        byte[] pack = PackTestSupport.BuildPack(dataStart => []);
        pack[0] = (byte)'X';

        PackTestSupport.AssertInvalidPack(pack);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(2u)]
    [InlineData(uint.MaxValue)]
    public void RejectsUnknownVersions(uint version)
    {
        byte[] pack = PackTestSupport.PatchUInt32(
            PackTestSupport.BuildPack(dataStart => []),
            8,
            version);

        PackTestSupport.AssertInvalidPack(pack);
    }

    [Fact]
    public void RejectsFileTruncatedInsideTheHeader()
    {
        byte[] pack = PackTestSupport.BuildPack(dataStart => []);

        PackTestSupport.AssertInvalidPack(PackTestSupport.Truncate(pack, PackTestSupport.PrefixLength));
    }

    [Fact]
    public void RejectsZeroHeaderCompressedLength()
    {
        byte[] pack = PackTestSupport.PatchUInt32(
            PackTestSupport.BuildPack(dataStart => []),
            12,
            0);

        PackTestSupport.AssertInvalidPack(pack);
    }

    [Fact]
    public void RejectsOverlongHeaderCompressedLength()
    {
        byte[] pack = PackTestSupport.BuildPack(dataStart => []);
        uint overlong = (uint)(pack.Length - PackTestSupport.PrefixLength + 1);

        PackTestSupport.AssertInvalidPack(PackTestSupport.PatchUInt32(pack, 12, overlong));
    }

    [Fact]
    public void RejectsZeroHeaderUncompressedLength()
    {
        byte[] pack = PackTestSupport.PatchUInt32(
            PackTestSupport.BuildPack(dataStart => []),
            16,
            0);

        PackTestSupport.AssertInvalidPack(pack);
    }

    [Fact]
    public void RejectsHeaderDecompressionFailure()
    {
        byte[] pack = PackTestSupport.BuildPack(dataStart => []);
        int compressedLength = (int)PackTestSupport.HeaderCompressedLength(pack);
        byte[] garbage = Enumerable.Repeat((byte)0xFF, compressedLength).ToArray();

        PackTestSupport.AssertInvalidPack(PackTestSupport.PatchBytes(pack, PackTestSupport.PrefixLength, garbage));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void RejectsHeaderUncompressedLengthMismatch(int delta)
    {
        byte[] pack = PackTestSupport.BuildPack(dataStart => []);
        uint actual = PackTestSupport.HeaderUncompressedLength(pack);
        uint patched = delta > 0 ? actual + 1 : actual - 1;

        PackTestSupport.AssertInvalidPack(PackTestSupport.PatchUInt32(pack, 16, patched));
    }

    [Fact]
    public void RejectsHeaderChecksumMismatch()
    {
        byte[] pack = PackTestSupport.BuildPack(dataStart => []);
        ulong checksum = PackTestSupport.HeaderChecksum(pack);

        PackTestSupport.AssertInvalidPack(PackTestSupport.PatchUInt64(pack, 20, checksum ^ 1));
    }

    [Fact]
    public void RejectsInvalidHeaderPayloads()
    {
        // A MessagePack integer is not a PackHeader.
        PackTestSupport.AssertInvalidPack(PackTestSupport.BuildFromHeader([0x2A]));

        // MessagePack nil deserializes to a null header.
        PackTestSupport.AssertInvalidPack(PackTestSupport.BuildFromHeader([0xC0]));

        // A zero-length header is rejected as an empty header.
        PackTestSupport.AssertInvalidPack(PackTestSupport.BuildFromHeader([]));
    }

    [Fact]
    public void RejectsNullEntries()
    {
        PackTestSupport.AssertInvalidPack(PackTestSupport.BuildFromHeader([0x81, 0x00, 0xC0]));
    }

    [Fact]
    public void RejectsNullEntry()
    {
        PackTestSupport.AssertInvalidPack(PackTestSupport.BuildFromHeader([0x81, 0x00, 0x91, 0xC0]));
    }

    [Fact]
    public void RejectsNullPath()
    {
        PackTestSupport.AssertInvalidPack(
            PackTestSupport.BuildFromHeader([0x81, 0x00, 0x91, 0x81, 0x00, 0xC0]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Textures/Player")]
    [InlineData("textures/player.png")]
    [InlineData("../escape")]
    [InlineData("a/../b")]
    [InlineData("a/./b")]
    [InlineData("/absolute")]
    [InlineData("trailing/")]
    [InlineData("double//slash")]
    [InlineData("with space")]
    [InlineData("with:colon")]
    [InlineData("back\\slash")]
    public void RejectsNonCanonicalPaths(string path)
    {
        byte[] pack = PackTestSupport.BuildPack(
            dataStart => [PackTestSupport.Entry(path, isRaw: true, offset: dataStart)]);

        PackTestSupport.AssertInvalidPack(pack);
    }

    [Fact]
    public void RejectsDuplicatePaths()
    {
        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry("data/duplicate", offset: dataStart),
                PackTestSupport.Entry("data/duplicate", offset: dataStart),
            ]);

        PackTestSupport.AssertInvalidPack(pack);
    }

    [Fact]
    public void RejectsOffsetBeforeTheDataStart()
    {
        byte[] pack = PackTestSupport.BuildPack(
            dataStart => [PackTestSupport.Entry("data/early", offset: 0)]);

        PackTestSupport.AssertInvalidPack(pack);
    }

    [Fact]
    public void RejectsOffsetPlusLengthOverflow()
    {
        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry(
                    "data/overflow",
                    offset: ulong.MaxValue,
                    length: 2,
                    size: 2),
            ]);

        PackTestSupport.AssertInvalidPack(pack);
    }

    [Fact]
    public void RejectsOffsetPlusLengthPastTheEndOfTheFile()
    {
        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry(
                    "data/past-end",
                    offset: dataStart,
                    length: 4,
                    size: 4),
            ]);

        PackTestSupport.AssertInvalidPack(pack);
    }

    [Fact]
    public void RejectsTruncatedPayloadData()
    {
        byte[] payload = [1, 2, 3, 4];
        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry(
                    "data/raw",
                    offset: dataStart,
                    length: (uint)payload.Length,
                    size: (uint)payload.Length),
            ],
            payload);

        PackTestSupport.AssertInvalidPack(PackTestSupport.Truncate(pack, pack.Length - 1));
    }

    [Fact]
    public void RejectsOverlappingPayloadRanges()
    {
        byte[] payload = [1, 2, 3, 4];
        byte[] second = [5, 6, 7, 8];
        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry(
                    "data/first",
                    offset: dataStart,
                    length: (uint)payload.Length,
                    size: (uint)payload.Length),
                PackTestSupport.Entry(
                    "data/second",
                    offset: dataStart,
                    length: (uint)second.Length,
                    size: (uint)second.Length),
            ],
            payload,
            second);

        PackTestSupport.AssertInvalidPack(pack);
    }

    [Fact]
    public void RejectsUncompressedLengthNotEqualSize()
    {
        byte[] payload = [1, 2, 3, 4];
        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry(
                    "data/raw",
                    offset: dataStart,
                    length: (uint)payload.Length,
                    size: (uint)payload.Length + 1),
            ],
            payload);

        PackTestSupport.AssertInvalidPack(pack);
    }

    [Fact]
    public void AcceptsZeroLengthEntries()
    {
        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry("data/empty-a", offset: dataStart),
                PackTestSupport.Entry("data/empty-b", offset: dataStart),
            ]);

        using PackArchive archive = PackArchive.Open(new MemoryStream(pack));
        Assert.Equal(2, archive.Entries.Count);
        Assert.Empty(archive.ReadDecompressedBytes(archive.Entries[0]));
        Assert.Empty(archive.ReadStoredBytes(archive.Entries[1]));
    }

    [Fact]
    public void OpenFileReportsTheResolvedPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bork2d-invalid-{Guid.NewGuid():N}.pck");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);

            Bork2dException exception = Assert.Throws<Bork2dException>(() => PackArchive.Open(path));

            Assert.Contains(Path.GetFullPath(path), exception.Message, StringComparison.Ordinal);
            Assert.Contains("short", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
