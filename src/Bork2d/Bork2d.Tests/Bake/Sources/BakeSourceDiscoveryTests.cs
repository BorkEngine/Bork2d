using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Sources;
using Bork2d.Tests.Bake.Baking;
using Xunit;

namespace Bork2d.Tests.Bake.Sources;

/// <summary>
/// The source discovery contract: every typed TOML anywhere under a pack root, its own
/// <c>[bork2d].type</c> declaration resolved against the explicit registrations, sidecar
/// discrimination, ordinal order, and missing/malformed/unregistered metadata diagnostics.
/// </summary>
public sealed class BakeSourceDiscoveryTests
{
    [Fact]
    public void TypedSourcesAreDiscoveredAnywhereAndOtherFilesAreIgnored()
    {
        using var project = TestBakeProject.Create("discovery-anywhere");
        project.WriteAsset(
            "data/a.toml",
            """
            value = 1

            [bork2d]
            type = "Bork2d.BakeFixture.StatsRoot"
            """);
        project.WriteAsset("data/readme.md", "not a source\n");
        project.WriteAsset("art/player.png.toml", "filter = \"nearest\"\n");
        project.WriteAssetBytes("art/player.png", "not an image"u8.ToArray());
        project.WriteAsset(
            "textures/ui/button.toml",
            """
            value = 2

            [bork2d]
            type = "Bork2d.Api.Assets.SpriteAsset"
            """);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            project.Project,
            diagnostics).DataSources;

        Assert.Empty(diagnostics.GetSorted());
        Assert.Equal(2, sources.Count);

        BakeSource data = Assert.Single(sources, source => source.AssetPath == "data/a");
        Assert.Equal("data/a.toml", data.RelativePath);
        Assert.Equal("Bork2d.BakeFixture.StatsRoot", data.TypeName);
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
        using var project = TestBakeProject.Create("discovery-sorted");
        WriteTyped(project, "data/zeta.toml");
        WriteTyped(project, "data/alpha.toml");
        WriteTyped(project, "art/beta.toml");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            project.Project,
            diagnostics).DataSources;

        Assert.Empty(diagnostics.GetSorted());
        Assert.Equal(
            new[] { "art/beta", "data/alpha", "data/zeta" },
            sources.Select(source => source.AssetPath));
    }

    [Fact]
    public void TypeLessSidecarShapedTomlIsLeftToTheTexturePipeline()
    {
        using var project = TestBakeProject.Create("discovery-sidecar");
        project.WriteAsset("art/player.png.toml", "filter = \"nearest\"\n");
        project.WriteAssetBytes("art/player.png", "not an image"u8.ToArray());
        WriteTyped(project, "data/a.toml");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            project.Project,
            diagnostics).DataSources;

        Assert.Empty(diagnostics.GetSorted());
        BakeSource source = Assert.Single(sources);
        Assert.Equal("data/a", source.AssetPath);
    }

    [Fact]
    public void TypeLessNonSidecarTomlIsAMissingTypeError()
    {
        using var project = TestBakeProject.Create("discovery-missing-type");
        project.WriteAsset("data/notes.toml", "value = 1\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            project.Project,
            diagnostics).DataSources;

        Assert.Empty(sources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SourceTypeMissing, diagnostic.Code);
        Assert.Contains("data/notes.toml", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedTomlWithAnExtensionInItsNameIsAnInvalidAssetPath()
    {
        using var project = TestBakeProject.Create("discovery-invalid-path");
        project.WriteAsset(
            "art/player.png.toml",
            """
            [bork2d]
            type = "Bork2d.BakeFixture.StatsRoot"
            """);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            project.Project,
            diagnostics).DataSources;

        Assert.Empty(sources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SourceInvalidAssetPath, diagnostic.Code);
        Assert.Equal(2, diagnostic.Line);
        Assert.Equal(8, diagnostic.Column);
    }

    [Fact]
    public void MalformedBork2dTableIsAnErrorWithTheValueSpan()
    {
        using var project = TestBakeProject.Create("discovery-malformed");
        project.WriteAsset(
            "data/bad.toml",
            """
            value = 1

            [bork2d]
            type = 4
            """);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            project.Project,
            diagnostics).DataSources;

        Assert.Empty(sources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SourceTypeInvalid, diagnostic.Code);
        Assert.Equal(4, diagnostic.Line);
        Assert.Equal(8, diagnostic.Column);
    }

    [Fact]
    public void UnknownBork2dKeyIsAnError()
    {
        using var project = TestBakeProject.Create("discovery-unknown-key");
        project.WriteAsset(
            "data/bad.toml",
            """
            [bork2d]
            type = "Bork2d.BakeFixture.StatsRoot"
            kind = "MessagePack"
            """);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            project.Project,
            diagnostics).DataSources;

        Assert.Empty(sources);
        Assert.Equal(
            BakeDiagnosticCodes.SourceTypeInvalid,
            Assert.Single(diagnostics.GetSorted()).Code);
    }

    [Fact]
    public void MissingBork2dTypeIsAnError()
    {
        using var project = TestBakeProject.Create("discovery-missing-bork2d-type");
        project.WriteAsset("data/bad.toml", "[bork2d]\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            project.Project,
            diagnostics).DataSources;

        Assert.Empty(sources);
        Assert.Equal(
            BakeDiagnosticCodes.SourceTypeMissing,
            Assert.Single(diagnostics.GetSorted()).Code);
    }

    [Fact]
    public void TomlSyntaxErrorInADataSourceIsReported()
    {
        using var project = TestBakeProject.Create("discovery-syntax");
        project.WriteAsset("data/bad.toml", "value =\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            project.Project,
            diagnostics).DataSources;

        Assert.Empty(sources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.TomlSyntax, diagnostic.Code);
        Assert.Equal("Assets/data/bad.toml", diagnostic.FilePath);
    }

    [Fact]
    public void DuplicateTypeKeyIsASyntaxError()
    {
        using var project = TestBakeProject.Create("discovery-duplicate-key");
        project.WriteAsset(
            "data/bad.toml",
            """
            [bork2d]
            type = "Bork2d.BakeFixture.StatsRoot"
            type = "Bork2d.BakeFixture.StatsRoot"
            """);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            project.Project,
            diagnostics).DataSources;

        Assert.Empty(sources);
        Assert.Equal(
            BakeDiagnosticCodes.TomlSyntax,
            Assert.Single(diagnostics.GetSorted()).Code);
    }

    [Fact]
    public void NonTableBork2dRootValueIsAnError()
    {
        using var project = TestBakeProject.Create("discovery-non-table");
        project.WriteAsset("data/bad.toml", "bork2d = \"Bork2d.BakeFixture.StatsRoot\"\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            project.Project,
            diagnostics).DataSources;

        Assert.Empty(sources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SourceTypeInvalid, diagnostic.Code);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
    }

    [Fact]
    public void UnregisteredBork2dTypeIsBK1011WithTheValueSpanAndHint()
    {
        using var project = TestBakeProject.Create("discovery-unregistered");
        project.WriteAsset(
            "data/missing.toml",
            """
            value = 1

            [bork2d]
            type = "Game.Data.Missing"
            """);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            project.Project,
            diagnostics).DataSources;

        Assert.Empty(sources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.AssetTypeNotRegistered, diagnostic.Code);
        Assert.Equal(4, diagnostic.Line);
        Assert.Equal(8, diagnostic.Column);
        Assert.Contains("Game.Data.Missing", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("RegisterAsset<Game.Data.Missing>()", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultAssetsAreRegisteredWithoutGameTypes()
    {
        using var project = TestBakeProject.Create(
            "discovery-default-assets",
            registerFixtureTypes: false);
        project.WriteAsset(
            "sprites/player.toml",
            """
            [bork2d]
            type = "Bork2d.Api.Assets.SpriteAsset"
            """);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            project.Project,
            diagnostics).DataSources;

        Assert.Empty(diagnostics.GetSorted());
        Assert.Single(sources);
    }

    private static void WriteTyped(TestBakeProject project, string path)
        => project.WriteAsset(
            path,
            "value = 1\n\n[bork2d]\ntype = \"Bork2d.BakeFixture.StatsRoot\"\n");
}
