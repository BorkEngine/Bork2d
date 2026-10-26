using Bork2d.Bake.Paths;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Bake.Paths;

public sealed class BakeSourcePathTests
{
    [Theory]
    [InlineData("data/foo.toml", "data/foo")]
    [InlineData("sprites/player/idle.toml", "sprites/player/idle")]
    [InlineData("data/Foo.TOML", "data/foo")]
    [InlineData("data\\sub\\foo.toml", "data/sub/foo")]
    public void DataTomlSourcesStripTheSuffix(string sourcePath, string expected)
    {
        Assert.True(
            BakeSourcePath.TryToAssetPath(sourcePath, BakeSourceKind.DataToml, out string assetPath, out string? reason),
            reason);

        Assert.Equal(expected, assetPath);
    }

    [Theory]
    [InlineData("textures/player.png", "textures/player")]
    [InlineData("textures/UI/Button.PNG", "textures/ui/button")]
    [InlineData("textures/ui/button.jpg", "textures/ui/button")]
    [InlineData("textures/ui/button.jpeg", "textures/ui/button")]
    [InlineData("textures/ui/button.bmp", "textures/ui/button")]
    [InlineData("textures/ui/button.tga", "textures/ui/button")]
    public void TextureSourcesStripTheImageExtension(string sourcePath, string expected)
    {
        Assert.True(
            BakeSourcePath.TryToAssetPath(
                sourcePath,
                BakeSourceKind.TextureSource,
                out string assetPath,
                out string? reason),
            reason);

        Assert.Equal(expected, assetPath);
    }

    [Theory]
    [InlineData("textures/player.png.toml", "textures/player")]
    [InlineData("textures/UI/Button.JPG.TOML", "textures/ui/button")]
    [InlineData("textures/ui/button.bmp.toml", "textures/ui/button")]
    public void TextureSidecarsStripBothSuffixes(string sourcePath, string expected)
    {
        Assert.True(
            BakeSourcePath.TryToAssetPath(
                sourcePath,
                BakeSourceKind.TextureSidecar,
                out string assetPath,
                out string? reason),
            reason);

        Assert.Equal(expected, assetPath);
    }

    [Theory]
    [InlineData("", (int)BakeSourceKind.DataToml)]
    [InlineData("   ", (int)BakeSourceKind.DataToml)]
    [InlineData("/data/foo.toml", (int)BakeSourceKind.DataToml)]
    [InlineData("C:\\data\\foo.toml", (int)BakeSourceKind.DataToml)]
    [InlineData("../data/foo.toml", (int)BakeSourceKind.DataToml)]
    [InlineData("data/../foo.toml", (int)BakeSourceKind.DataToml)]
    [InlineData("data//foo.toml", (int)BakeSourceKind.DataToml)]
    [InlineData("data/foo.txt", (int)BakeSourceKind.DataToml)]
    [InlineData("data/foo bar.toml", (int)BakeSourceKind.DataToml)]
    [InlineData("data/.toml", (int)BakeSourceKind.DataToml)]
    [InlineData("textures/player", (int)BakeSourceKind.TextureSource)]
    [InlineData("textures/player.toml", (int)BakeSourceKind.TextureSource)]
    [InlineData("textures/player.png", (int)BakeSourceKind.TextureSidecar)]
    [InlineData("textures/player.txt.toml", (int)BakeSourceKind.TextureSidecar)]
    public void UnsafeOrUnsupportedSourcePathsAreRejected(
        string sourcePath,
        int kindValue)
    {
        var kind = (BakeSourceKind)kindValue;

        Assert.False(
            BakeSourcePath.TryToAssetPath(sourcePath, kind, out string assetPath, out string? reason));

        Assert.Equal(string.Empty, assetPath);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void TryMakeRelativeReturnsASlashSeparatedPath()
    {
        using var directory = TempBakeDirectory.Create();
        string assetsRoot = directory.CreateDirectory("Assets");
        string sourcePath = directory.WriteFile("Assets/data/foo.toml", "x = 1");
        string outsidePath = directory.WriteFile("elsewhere/bar.toml", "x = 1");

        Assert.True(
            BakeSourcePath.TryMakeRelative(assetsRoot, sourcePath, out string relative, out string? reason),
            reason);
        Assert.Equal("data/foo.toml", relative);

        Assert.False(
            BakeSourcePath.TryMakeRelative(assetsRoot, outsidePath, out _, out string? outsideReason));
        Assert.False(string.IsNullOrWhiteSpace(outsideReason));
    }

    [Fact]
    public void TryResolveUnderRootResolvesAndContains()
    {
        using var directory = TempBakeDirectory.Create();
        string assetsRoot = directory.CreateDirectory("Assets");

        Assert.True(
            BakeSourcePath.TryResolveUnderRoot(
                assetsRoot,
                "data/sub/foo",
                out string fullPath,
                out string? reason),
            reason);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(assetsRoot, "data", "sub", "foo")),
            fullPath);

        Assert.False(
            BakeSourcePath.TryResolveUnderRoot(assetsRoot, "../escape", out _, out string? escapeReason));
        Assert.False(string.IsNullOrWhiteSpace(escapeReason));

        Assert.False(
            BakeSourcePath.TryResolveUnderRoot(
                assetsRoot,
                "sub/../inside",
                out _,
                out string? parentReason));
        Assert.Contains("..", parentReason, StringComparison.Ordinal);

        Assert.False(
            BakeSourcePath.TryResolveUnderRoot(assetsRoot, string.Empty, out _, out _));
    }

    [Fact]
    public void IsUnderRootUsesASeparatorBoundary()
    {
        using var directory = TempBakeDirectory.Create();
        string assetsRoot = directory.CreateDirectory("Assets");

        Assert.True(BakeSourcePath.IsUnderRoot(assetsRoot, assetsRoot));
        Assert.True(
            BakeSourcePath.IsUnderRoot(assetsRoot, Path.Combine(assetsRoot, "data", "foo.toml")));
        Assert.False(BakeSourcePath.IsUnderRoot(assetsRoot, assetsRoot + "-evil"));
        Assert.False(
            BakeSourcePath.IsUnderRoot(assetsRoot, Path.Combine(assetsRoot, "..", "Assets-evil")));
    }

    [Fact]
    public void EnumerateFilesSortedReturnsOrdinalSortedFiles()
    {
        using var directory = TempBakeDirectory.Create();
        string root = directory.CreateDirectory("Assets");
        string a = directory.WriteFile("Assets/a.txt", "a");
        string b = directory.WriteFile("Assets/b.txt", "b");
        string z = directory.WriteFile("Assets/z.txt", "z");
        string c = directory.WriteFile("Assets/sub/c.txt", "c");
        string d = directory.WriteFile("Assets/sub/deeper/d.txt", "d");

        IReadOnlyList<string> files = BakeSourcePath.EnumerateFilesSorted(root);

        string[] expected = new[] { a, b, c, d, z };
        Array.Sort(expected, StringComparer.Ordinal);
        Assert.Equal(expected, files);
        Assert.False(BakeSourcePath.IsReparsePoint(a));
    }

    [SymlinkFact]
    public void EnumerationSkipsReparsePointsAndIsReparsePointDetectsThem()
    {
        using var directory = TempBakeDirectory.Create();
        string root = directory.CreateDirectory("Assets");
        string a = directory.WriteFile("Assets/a.txt", "a");
        directory.WriteFile("Assets/sub/c.txt", "c");

        string fileLink = Path.Combine(root, "link.txt");
        string directoryLink = Path.Combine(root, "link-dir");
        File.CreateSymbolicLink(fileLink, a);
        Directory.CreateSymbolicLink(directoryLink, Path.Combine(root, "sub"));

        Assert.True(BakeSourcePath.IsReparsePoint(fileLink));
        Assert.True(BakeSourcePath.IsReparsePoint(directoryLink));
        Assert.False(BakeSourcePath.IsReparsePoint(a));

        IReadOnlyList<string> files = BakeSourcePath.EnumerateFilesSorted(root);

        Assert.DoesNotContain(files, path => path.Contains("link", StringComparison.Ordinal));
        Assert.Equal(2, files.Count);
    }
}
