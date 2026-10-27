using Bork2d.Assets.Pack;
using Xunit;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// Structural comparisons for two packs produced from identical inputs: the entry table
/// (path/kind/compression/length/size/offset) and the decompressed payload bytes, never a whole-pack
/// hash (D-M4-07).
/// </summary>
internal static class BakedPackAssertions
{
    internal sealed record EntryShape(
        string Path,
        bool IsRaw,
        bool IsCompressed,
        uint Length,
        uint Size,
        ulong Offset);

    internal static void AssertStructurallyEqual(string expectedPackPath, string actualPackPath)
    {
        IReadOnlyList<EntryShape> expectedShapes = Shapes(expectedPackPath);
        IReadOnlyList<EntryShape> actualShapes = Shapes(actualPackPath);
        Assert.Equal(expectedShapes, actualShapes);

        IReadOnlyList<byte[]> expectedPayloads = Payloads(expectedPackPath);
        IReadOnlyList<byte[]> actualPayloads = Payloads(actualPackPath);
        Assert.Equal(expectedPayloads.Count, actualPayloads.Count);
        for (int i = 0; i < expectedPayloads.Count; i++)
        {
            Assert.Equal(expectedPayloads[i], actualPayloads[i]);
        }
    }

    internal static IReadOnlyList<EntryShape> Shapes(string packPath)
    {
        using PackArchive archive = PackArchive.Open(packPath);
        return [.. archive.Entries.Select(entry => new EntryShape(
            entry.Path,
            entry.IsRaw,
            entry.IsCompressed,
            entry.Length,
            entry.Size,
            entry.Offset))];
    }

    internal static IReadOnlyList<byte[]> Payloads(string packPath)
    {
        using PackArchive archive = PackArchive.Open(packPath);
        return [.. archive.Entries.Select(archive.ReadDecompressedBytes)];
    }

    internal static void WriteValidPack(string packPath)
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddRaw("data/previous", [1, 2, 3, 4]);
        builder.Write(packPath);
    }
}
