using System.Text;
using Bork2d.Api.Assets;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Sources;
using Bork2d.Tests.Bake.Hosting;
using Xunit;

namespace Bork2d.Tests.Bake.Sources;

/// <summary>
/// Binding every discovered source through the generated host, including the canonical diagnostic
/// paths, UTF-8/BOM handling, and reference sites for the fixture assets.
/// </summary>
[Collection(BakeHostFixtureCollection.Name)]
public sealed class BakeSourceBinderTests
{
    private readonly TempBakeFixture _fixture;

    /// <summary>Initializes a new instance of the <see cref="BakeSourceBinderTests"/> class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public BakeSourceBinderTests(TempBakeFixture fixture) => _fixture = fixture;

    [Fact]
    public void EveryFixtureSourceBindsInOrder()
    {
        var diagnostics = new BakeDiagnostics();

        IReadOnlyList<BoundAsset> assets = BakeSourceBinder.BindAll(
            _fixture.DescriptorDescriptor,
            _fixture.Manifest,
            diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.Equal(
            new[] { "data/config", "hidden/secret", "roots/stats", "sprites/player", "weapons/sword" },
            assets.Select(asset => asset.AssetPath));
        Assert.Equal(
            new[]
            {
                "BakeFixture.Common.GameConfig",
                "BakeFixture.Common.HiddenConfig",
                "BakeFixture.Common.StatsRoot",
                "Bork2d.Api.Assets.SpriteAsset",
                "Game.Data.Weapon",
            },
            assets.Select(asset => asset.TypeName));
        Assert.All(assets, asset => Assert.Equal("Assets", asset.SourcePath.Split('/')[0]));
        Assert.All(assets, asset => Assert.True(File.Exists(Path.Combine(_fixture.RootPath, "Assets", asset.RelativeSourcePath.Replace('/', Path.DirectorySeparatorChar)))));
    }

    [Fact]
    public void ClassAndStructRootsAndTheirReferenceSitesBind()
    {
        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BoundAsset> assets = BakeSourceBinder.BindAll(
            _fixture.DescriptorDescriptor,
            _fixture.Manifest,
            diagnostics);
        Assert.Empty(diagnostics.GetSorted());

        BoundAsset config = assets.Single(asset => asset.AssetPath == "data/config");
        var gameConfig = config.Value;
        Assert.Equal("BakeFixture.Common.GameConfig", gameConfig.GetType().FullName);
        BakeReferenceSite texture = Assert.Single(config.References);
        Assert.Equal("texture", texture.MemberPath);
        Assert.Equal("textures/player", texture.ReferencePath);
        Assert.False(texture.IsData);
        Assert.Equal(6, texture.Line);

        BoundAsset sprite = assets.Single(asset => asset.AssetPath == "sprites/player");
        var spriteAsset = Assert.IsType<SpriteAsset>(sprite.Value);
        Assert.Equal("textures/player", spriteAsset.Texture.Path);
        Assert.Equal(16f, spriteAsset.Scale);
        Assert.Equal(SpriteTopology.Triangles, spriteAsset.Topology);
        Assert.Equal(new ushort[] { 0, 1, 2, 0, 2, 3 }, spriteAsset.Indices);
        Assert.Single(sprite.References);

        BoundAsset stats = assets.Single(asset => asset.AssetPath == "roots/stats");
        Assert.Contains("Attack = 7", stats.Value.ToString(), StringComparison.Ordinal);
        Assert.Contains("Defense = 9", stats.Value.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownKeyUsesTheProjectRelativePathAndTheValueSpan()
    {
        string project = _fixture.CreateProjectCopy(
            "unknown-key",
            ("data/bad.toml", "name = \"x\"\nhealth = 1\nmust = \"m\"\nbogus = true\n"));
        var diagnostics = new BakeDiagnostics();

        _ = BakeSourceBinder.BindAll(Parse(project), _fixture.Manifest, diagnostics);

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
        string project = _fixture.CreateProjectCopy(
            "syntax",
            ("data/bad.toml", "name = \n"));
        var diagnostics = new BakeDiagnostics();

        _ = BakeSourceBinder.BindAll(Parse(project), _fixture.Manifest, diagnostics);

        Assert.Contains(
            diagnostics.GetSorted(),
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.TomlSyntax
                && diagnostic.FilePath == "Assets/data/bad.toml"
                && diagnostic.Line is not null);
    }

    [Fact]
    public void Utf8BomIsAcceptedAndStripped()
    {
        string project = _fixture.CreateProjectCopy("bom");
        string path = Path.Combine(project, "Assets", "data", "bom.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] content = Encoding.UTF8.GetBytes(
            "name = \"caf\u00e9\"\nhealth = 1\nmust = \"m\"\n");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. content]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BoundAsset> assets = BakeSourceBinder.BindAll(
            Parse(project),
            _fixture.Manifest,
            diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        BoundAsset asset = Assert.Single(assets);
        Assert.Equal("data/bom", asset.AssetPath);
        Assert.Equal("caf\u00e9", ((dynamic)asset.Value).Name);
    }

    [Fact]
    public void InvalidUtf8IsReported()
    {
        string project = _fixture.CreateProjectCopy("invalid-utf8");
        string path = Path.Combine(project, "Assets", "data", "bad.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x6E, 0x61, 0x6D, 0x65, 0x20, 0x3D, 0x20, 0xFF, 0xFE]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BoundAsset> assets = BakeSourceBinder.BindAll(
            Parse(project),
            _fixture.Manifest,
            diagnostics);

        Assert.Empty(assets);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SourceNotUtf8, diagnostic.Code);
        Assert.Equal("Assets/data/bad.toml", diagnostic.FilePath);
    }

    [Fact]
    public void ATextureSidecarIsNeverBound()
    {
        string project = _fixture.CreateProjectCopy(
            "sidecar",
            ("textures/player.png.toml", "filter = \"nearest\"\n"),
            ("data/config.toml", """
                name = "campaign"
                health = 1
                must = "m"
                texture = "textures/player"
                """));
        var diagnostics = new BakeDiagnostics();

        IReadOnlyList<BoundAsset> assets = BakeSourceBinder.BindAll(
            Parse(project),
            _fixture.Manifest,
            diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        BoundAsset asset = Assert.Single(assets);
        Assert.Equal("data/config", asset.AssetPath);
    }

    private static Bork2d.Bake.Project.ProjectDescriptor Parse(string project)
    {
        var diagnostics = new BakeDiagnostics();
        Bork2d.Bake.Project.ProjectDescriptor? descriptor =
            Bork2d.Bake.Project.ProjectDescriptorParser.Parse(
                Path.Combine(project, TempBakeFixture.DescriptorFileName),
                diagnostics);
        Assert.NotNull(descriptor);
        Assert.Empty(diagnostics.GetSorted());
        return descriptor;
    }
}
