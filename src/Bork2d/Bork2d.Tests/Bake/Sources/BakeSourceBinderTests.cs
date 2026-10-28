using System.Text;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Sources;
using Bork2d.Engine.Assets;
using Bork2d.Tests.Bake.Baking;
using Xunit;

namespace Bork2d.Tests.Bake.Sources;

/// <summary>
/// Binding every discovered source through the explicit registrations, including the canonical
/// diagnostic paths, UTF-8/BOM handling, and reference sites for the fixture assets.
/// </summary>
public sealed class BakeSourceBinderTests
{
    [Fact]
    public void EveryFixtureSourceBindsInOrder()
    {
        using var project = TestBakeProject.Create("binder-order");
        WriteStandardSources(project);
        var diagnostics = new BakeDiagnostics();

        IReadOnlyList<BoundAsset> assets = Bind(project, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.Equal(
            new[] { "data/config", "roots/stats", "sprites/player" },
            assets.Select(asset => asset.AssetPath));
        Assert.Equal(
            new[]
            {
                "Bork2d.BakeFixture.GameConfig",
                "Bork2d.BakeFixture.StatsRoot",
                "Bork2d.Engine.Assets.SpriteAsset",
            },
            assets.Select(asset => asset.TypeName));
        Assert.All(assets, asset => Assert.Equal("Assets", asset.SourcePath.Split('/')[0]));
    }

    [Fact]
    public void ClassAndStructRootsAndTheirReferenceSitesBind()
    {
        using var project = TestBakeProject.Create("binder-roots");
        WriteStandardSources(project);
        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BoundAsset> assets = Bind(project, diagnostics);
        Assert.Empty(diagnostics.GetSorted());

        BoundAsset config = assets.Single(asset => asset.AssetPath == "data/config");
        var gameConfig = Assert.IsType<Bork2d.BakeFixture.GameConfig>(config.Value);
        Assert.Equal("campaign", gameConfig.Name);
        BakeReferenceSite texture = Assert.Single(config.References);
        Assert.Equal("texture", texture.MemberPath);
        Assert.Equal("art/player", texture.ReferencePath);
        Assert.False(texture.IsData);
        Assert.Equal(typeof(TextureDescriptor).FullName, texture.TargetTypeName);
        Assert.Equal(6, texture.Line);

        BoundAsset sprite = assets.Single(asset => asset.AssetPath == "sprites/player");
        var spriteAsset = Assert.IsType<SpriteAsset>(sprite.Value);
        Assert.Equal("art/player", spriteAsset.Texture.Path);
        Assert.Equal(16f, spriteAsset.Scale);
        Assert.Equal(SpriteTopology.Triangles, spriteAsset.Topology);
        Assert.Equal(new ushort[] { 0, 1, 2, 0, 2, 3 }, spriteAsset.Indices);
        Assert.Single(sprite.References);

        BoundAsset stats = assets.Single(asset => asset.AssetPath == "roots/stats");
        Assert.Equal(new Bork2d.BakeFixture.StatsRoot(11, 13), stats.Value);
    }

    [Fact]
    public void UnknownKeyUsesTheProjectRelativePathAndTheValueSpan()
    {
        using var project = TestBakeProject.Create("binder-unknown-key");
        project.WriteAsset(
            "data/bad.toml",
            """
            name = "x"
            health = 1
            must = "m"
            bogus = true

            [bork2d]
            type = "Bork2d.BakeFixture.GameConfig"
            """);
        var diagnostics = new BakeDiagnostics();

        _ = Bind(project, diagnostics);

        BakeDiagnostic diagnostic = Assert.Single(
            diagnostics.GetSorted(),
            candidate => candidate.Code == BakeDiagnosticCodes.BinderUnknownKey);
        Assert.Equal("Assets/data/bad.toml", diagnostic.FilePath);
        Assert.Equal(4, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
        Assert.Equal("bogus", diagnostic.MemberPath);
        Assert.Equal("data/bad", diagnostic.AssetPath);
    }

    [Fact]
    public void InvalidTomlIsASyntaxDiagnostic()
    {
        using var project = TestBakeProject.Create("binder-syntax");
        project.WriteAsset("data/bad.toml", "name = \n");
        var diagnostics = new BakeDiagnostics();

        _ = Bind(project, diagnostics);

        Assert.Contains(
            diagnostics.GetSorted(),
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.TomlSyntax
                && diagnostic.FilePath == "Assets/data/bad.toml"
                && diagnostic.Line is not null);
    }

    [Fact]
    public void Utf8BomIsAcceptedAndStripped()
    {
        using var project = TestBakeProject.Create("binder-bom");
        string path = project.FullPath("Assets/data/bom.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] content = Encoding.UTF8.GetBytes(
            "name = \"caf\u00e9\"\nhealth = 1\nmust = \"m\"\n\n[bork2d]\n"
                + "type = \"Bork2d.BakeFixture.GameConfig\"\n");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. content]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BoundAsset> assets = Bind(project, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        BoundAsset asset = Assert.Single(assets);
        Assert.Equal("data/bom", asset.AssetPath);
        Assert.Equal("caf\u00e9", Assert.IsType<Bork2d.BakeFixture.GameConfig>(asset.Value).Name);
    }

    [Fact]
    public void InvalidUtf8IsReported()
    {
        using var project = TestBakeProject.Create("binder-invalid-utf8");
        string path = project.FullPath("Assets/data/bad.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x6E, 0x61, 0x6D, 0x65, 0x20, 0x3D, 0x20, 0xFF, 0xFE]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BoundAsset> assets = Bind(project, diagnostics);

        Assert.Empty(assets);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SourceNotUtf8, diagnostic.Code);
        Assert.Equal("Assets/data/bad.toml", diagnostic.FilePath);
    }

    [Fact]
    public void ATextureSidecarIsNeverBoundAsData()
    {
        using var project = TestBakeProject.Create("binder-sidecar");
        project.WriteAssetBytes("art/player.png", [1, 2, 3]);
        project.WriteAsset("art/player.png.toml", "filter = \"nearest\"\n");
        project.WriteAsset(
            "data/config.toml",
            """
            name = "campaign"
            health = 1
            must = "m"
            texture = "art/player"

            [bork2d]
            type = "Bork2d.BakeFixture.GameConfig"
            """);
        var diagnostics = new BakeDiagnostics();

        IReadOnlyList<BoundAsset> assets = Bind(project, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        BoundAsset asset = Assert.Single(assets);
        Assert.Equal("data/config", asset.AssetPath);
    }

    private static void WriteStandardSources(TestBakeProject project)
    {
        project.WriteAsset(
            "data/config.toml",
            """
            name = "campaign"
            health = 100
            max_players = 4
            must = "present"
            tint = [1.0, 0.5, 0.25, 1.0]
            texture = "art/player"

            [stats]
            attack = 1
            defense = 2

            [bork2d]
            type = "Bork2d.BakeFixture.GameConfig"
            """);
        project.WriteAsset(
            "roots/stats.toml",
            """
            attack = 11
            defense = 13

            [bork2d]
            type = "Bork2d.BakeFixture.StatsRoot"
            """);
        project.WriteAsset(
            "sprites/player.toml",
            """
            texture = "art/player"
            uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]
            scale = 16.0
            pivot = [0.5, 0.5]
            topology = "triangles"
            indices = [0, 1, 2, 0, 2, 3]

            [bork2d]
            type = "Bork2d.Engine.Assets.SpriteAsset"
            """);
    }

    private static IReadOnlyList<BoundAsset> Bind(
        TestBakeProject project,
        BakeDiagnostics diagnostics)
    {
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            project.Project,
            diagnostics).DataSources;
        return BakeSourceBinder.BindAll(project.Project, sources, diagnostics);
    }
}
