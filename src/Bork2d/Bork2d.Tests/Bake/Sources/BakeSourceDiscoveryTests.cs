using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Project;
using Bork2d.Bake.Sources;
using Xunit;

namespace Bork2d.Tests.Bake.Sources;

/// <summary>
/// The source discovery contract (D-M4-13): every typed TOML anywhere under the asset root, its own
/// <c>[bork2d].type</c> declaration, sidecar discrimination, ordinal order, and missing/malformed
/// metadata diagnostics.
/// </summary>
public sealed class BakeSourceDiscoveryTests
{
    [Fact]
    public void TypedSourcesAreDiscoveredAnywhereAndOtherFilesAreIgnored()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(directory);
        directory.WriteFile(
            "Assets/data/a.toml",
            """
            value = 1

            [bork2d]
            type = "BakeFixture.Common.GameConfig"
            """);
        directory.WriteFile("Assets/data/readme.md", "not a source\n");
        directory.WriteFile("Assets/art/player.png.toml", "filter = \"nearest\"\n");
        directory.WriteFile("Assets/art/player.png", "not an image");
        directory.WriteFile(
            "Assets/textures/ui/button.toml",
            """
            value = 2

            [bork2d]
            type = "Bork2d.Api.Assets.SpriteAsset"
            """);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.Equal(2, sources.Count);

        BakeSource data = Assert.Single(sources, source => source.AssetPath == "data/a");
        Assert.Equal("data/a.toml", data.RelativePath);
        Assert.Equal("BakeFixture.Common.GameConfig", data.TypeName);
        Assert.Equal(4, data.TypeLine);
        Assert.Equal(8, data.TypeColumn);

        BakeSource underTextures = Assert.Single(
            sources,
            source => source.AssetPath == "textures/ui/button");
        Assert.Equal("Bork2d.Api.Assets.SpriteAsset", underTextures.TypeName);
    }

    [Fact]
    public void SourcesAreSortedOrdinalByCanonicalAssetPath()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(directory);
        WriteTyped(directory, "Assets/data/zeta.toml", "Type.Zeta");
        WriteTyped(directory, "Assets/data/alpha.toml", "Type.Alpha");
        WriteTyped(directory, "Assets/art/beta.toml", "Type.Beta");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.Equal(
            new[] { "art/beta", "data/alpha", "data/zeta" },
            sources.Select(source => source.AssetPath));
    }

    [Fact]
    public void TypeLessSidecarShapedTomlIsLeftToTheTexturePipeline()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(directory);
        directory.WriteFile("Assets/art/player.png.toml", "filter = \"nearest\"\n");
        directory.WriteFile("Assets/art/player.png", "not an image");
        WriteTyped(directory, "Assets/data/a.toml", "Type.A");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        BakeSource source = Assert.Single(sources);
        Assert.Equal("data/a", source.AssetPath);
    }

    [Fact]
    public void TypeLessNonSidecarTomlIsAMissingTypeError()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(directory);
        directory.WriteFile("Assets/data/notes.toml", "value = 1\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(sources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SourceTypeMissing, diagnostic.Code);
        Assert.Contains("data/notes.toml", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingBork2dTableIsAMissingTypeError()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(directory);
        directory.WriteFile("Assets/art/ground.toml", "value = 1\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(sources);
        Assert.Equal(
            BakeDiagnosticCodes.SourceTypeMissing,
            Assert.Single(diagnostics.GetSorted()).Code);
    }

    [Fact]
    public void TypedTomlWithAnExtensionInItsNameIsAnInvalidAssetPath()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(directory);
        directory.WriteFile(
            "Assets/art/player.png.toml",
            """
            [bork2d]
            type = "Type.Player"
            """);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(sources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SourceInvalidAssetPath, diagnostic.Code);
        Assert.Equal(2, diagnostic.Line);
        Assert.Equal(8, diagnostic.Column);
    }

    [Fact]
    public void MalformedBork2dTableIsAnErrorWithTheValueSpan()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(directory);
        directory.WriteFile(
            "Assets/data/bad.toml",
            """
            value = 1

            [bork2d]
            type = 4
            """);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(sources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SourceTypeInvalid, diagnostic.Code);
        Assert.Equal(4, diagnostic.Line);
        Assert.Equal(8, diagnostic.Column);
    }

    [Fact]
    public void UnknownBork2dKeyIsAnError()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(directory);
        directory.WriteFile(
            "Assets/data/bad.toml",
            """
            [bork2d]
            type = "Type.A"
            kind = "MessagePack"
            """);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(sources);
        Assert.Equal(
            BakeDiagnosticCodes.SourceTypeInvalid,
            Assert.Single(diagnostics.GetSorted()).Code);
    }

    [Fact]
    public void MissingBork2dTypeIsAnError()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(directory);
        directory.WriteFile("Assets/data/bad.toml", "[bork2d]\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(sources);
        Assert.Equal(
            BakeDiagnosticCodes.SourceTypeMissing,
            Assert.Single(diagnostics.GetSorted()).Code);
    }

    [Fact]
    public void TomlSyntaxErrorInADataSourceIsReported()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(directory);
        directory.WriteFile("Assets/data/bad.toml", "value =\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(sources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.TomlSyntax, diagnostic.Code);
        Assert.Equal("Assets/data/bad.toml", diagnostic.FilePath);
    }

    [Fact]
    public void DuplicateTypeKeyIsASyntaxError()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(directory);
        directory.WriteFile(
            "Assets/data/bad.toml",
            """
            [bork2d]
            type = "Type.A"
            type = "Type.B"
            """);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(sources);
        Assert.Equal(
            BakeDiagnosticCodes.TomlSyntax,
            Assert.Single(diagnostics.GetSorted()).Code);
    }

    [Fact]
    public void NonTableBork2dRootValueIsAnError()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(directory);
        directory.WriteFile("Assets/data/bad.toml", "bork2d = \"Type.A\"\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(sources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SourceTypeInvalid, diagnostic.Code);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
    }

    [Fact]
    public void TheDescriptorInsideTheAssetRootIsNotADataSource()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        string descriptorPath = directory.WriteFile(
            "Assets/bork2d.project.toml",
            """
            schema = 1

            [[assets]]
            root = "."
            pack = "assets.pck"
            """);
        var parseDiagnostics = new BakeDiagnostics();
        ProjectDescriptor descriptor = Assert.IsType<ProjectDescriptor>(
            ProjectDescriptorParser.Parse(descriptorPath, parseDiagnostics));
        Assert.Empty(parseDiagnostics.GetSorted());

        WriteTyped(directory, "Assets/data/a.toml", "Type.A");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.Single(sources);
    }

    private static void WriteTyped(TempBakeDirectory directory, string path, string typeName)
        => directory.WriteFile(
            path,
            $"value = 1\n\n[bork2d]\ntype = \"{typeName}\"\n");

    private static ProjectDescriptor Parse(TempBakeDirectory directory)
    {
        directory.CreateDirectory("Assets");
        string descriptorPath = directory.WriteFile(
            "bork2d.project.toml",
            """
            schema = 1

            [[assets]]
            root = "Assets"
            pack = "assets.pck"
            """);

        var diagnostics = new BakeDiagnostics();
        ProjectDescriptor? descriptor = ProjectDescriptorParser.Parse(descriptorPath, diagnostics);
        Assert.NotNull(descriptor);
        Assert.Empty(diagnostics.GetSorted());
        return descriptor;
    }
}
