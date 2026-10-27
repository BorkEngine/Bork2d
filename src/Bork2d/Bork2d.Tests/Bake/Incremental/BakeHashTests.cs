using System.Text;
using Bork2d.Bake.Incremental;
using Xunit;

namespace Bork2d.Tests.Bake.Incremental;

public sealed class BakeHashTests
{
    [Fact]
    public void HashIsStableUppercaseHex()
    {
        string first = BakeHash.HashText("bork2d");
        string second = BakeHash.HashText("bork2d");

        Assert.Equal(first, second);
        Assert.Equal("4F13B25208C1D986E03ADFEE1D5CF3DB", first);
        Assert.Equal(32, first.Length);
        Assert.All(first, character => Assert.True(char.IsAsciiHexDigitUpper(character)));
    }

    [Fact]
    public void EmptyInputHasAStableVector()
    {
        Assert.Equal("99AA06D3014798D86001C324468D497F", BakeHash.Hash([]));
    }

    [Fact]
    public void HashTextMatchesHashOfTheUtf8Bytes()
    {
        string path = "data/foo";

        Assert.Equal(
            "B4D9073DC1ED025CA80DA618AC8A5AC1",
            BakeHash.HashText(path));
        Assert.Equal(
            BakeHash.Hash(Encoding.UTF8.GetBytes(path)),
            BakeHash.HashText(path));
    }

    [Fact]
    public void FieldFramingIsUnambiguous()
    {
        string splitOne = BakeHash.HashTextFields("ab", "c");
        string splitTwo = BakeHash.HashTextFields("a", "bc");

        Assert.NotEqual(splitOne, splitTwo);
        Assert.Equal(32, splitOne.Length);
        Assert.Equal(32, splitTwo.Length);
    }

    [Fact]
    public void FieldOrderIsPreserved()
    {
        Assert.NotEqual(
            BakeHash.HashTextFields("a", "b"),
            BakeHash.HashTextFields("b", "a"));
    }

    [Fact]
    public void HashFileMatchesTheSameBytes()
    {
        using var directory = TempBakeDirectory.Create();
        byte[] bytes = [0x42, 0x4F, 0x52, 0x4B, 0x32, 0x44];
        string path = directory.FullPath("input.bin");
        File.WriteAllBytes(path, bytes);

        Assert.Equal(BakeHash.Hash(bytes), BakeHash.HashFile(path));
    }
}
