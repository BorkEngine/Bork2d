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
            BakeSourcePath.TryToAssetPath(sourcePath, "toml", out string assetPath, out string? reason),
            reason);

        Assert.Equal(expected, assetPath);
    }

    [Theory]
    [InlineData("art/player.png", "png", "art/player")]
    [InlineData("textures/UI/Button.PNG", "png", "textures/ui/button")]
    [InlineData("photos/Photo.JpEg", "jpeg", "photos/photo")]
    [InlineData("art/archive.tar.gz", "tar.gz", "art/archive")]
    [InlineData("art/Archive.TAR.GZ", "tar.gz", "art/archive")]
    public void ProcessorSourcesStripTheMatchedExtension(
        string sourcePath,
        string matchedExtension,
        string expected)
    {
        Assert.True(
            BakeSourcePath.TryToAssetPath(
                sourcePath,
                matchedExtension,
                out string assetPath,
                out string? reason),
            reason);

        Assert.Equal(expected, assetPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/data/foo.toml")]
    [InlineData("C:\\data\\foo.toml")]
    [InlineData("../data/foo.toml")]
    [InlineData("data/../foo.toml")]
    [InlineData("data//foo.toml")]
    [InlineData("data/foo.txt")]
    [InlineData("data/foo bar.toml")]
    [InlineData("data/.toml")]
    public void UnsafeOrUnsupportedSourcePathsAreRejected(string sourcePath)
    {
        Assert.False(
            BakeSourcePath.TryToAssetPath(sourcePath, "toml", out string assetPath, out string? reason));

        Assert.Equal(string.Empty, assetPath);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Theory]
    [InlineData("data/foo.toml", "")]
    [InlineData("data/foo.toml", "   ")]
    [InlineData("data/foo.png", "toml")]
    [InlineData("art/archive.gz", "tar.gz")]
    public void AnEmptyOrUnmatchedExtensionIsRejected(string sourcePath, string matchedExtension)
    {
        Assert.False(
            BakeSourcePath.TryToAssetPath(
                sourcePath,
                matchedExtension,
                out string assetPath,
                out string? reason));

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
