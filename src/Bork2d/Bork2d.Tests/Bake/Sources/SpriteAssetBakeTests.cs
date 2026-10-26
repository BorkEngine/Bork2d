using System.Numerics;
using Bork2d.Api.Assets;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Toml;
using Bork2d.Tests.Assets.Service;
using Bork2d.Tests.Assets.Vfs;
using Bork2d.Tests.Bake.Hosting;
using Tomlyn.Model;
using Xunit;

namespace Bork2d.Tests.Bake.Sources;

/// <summary>
/// D-M4-06/Q-14: <c>SpriteAsset</c> is ordinary custom data. It binds through the generated host,
/// carries its texture reference, and round-trips through <see cref="PackBuilder"/> and the runtime
/// loader; no UV/pivot/scale/topology validation exists in M4.
/// </summary>
[Collection(BakeHostFixtureCollection.Name)]
public sealed class SpriteAssetBakeTests
{
    private const string SpriteType = "Bork2d.Api.Assets.SpriteAsset";
    private const string SpritePath = "sprites/player";
    private const string TexturePath = "textures/player";
    private const string TexturePayloadPath = "textures/raw/player";

    private readonly TempBakeFixture _fixture;

    /// <summary>Initializes a new instance of the <see cref="SpriteAssetBakeTests"/> class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public SpriteAssetBakeTests(TempBakeFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SpriteTomlWithAllPublicFieldsBindsAndRoundTripsThroughAPack()
    {
        const string toml = """
            texture = "textures/player"
            uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]
            scale = 16.0
            pivot = [0.25, 0.75]
            topology = "triangles"
            indices = [0, 1, 2, 0, 2, 3]
            """;

        var diagnostics = new BakeDiagnostics();
        BakeBindContext context = BindContext(toml, diagnostics);
        object? value = _fixture.Manifest.Bind(SpriteType, context);
        Assert.Empty(diagnostics.GetSorted());

        var sprite = Assert.IsType<SpriteAsset>(value);
        Assert.Equal(TexturePath, sprite.Texture.Path);
        Assert.NotNull(sprite.Uvs);
        Assert.Equal(
            new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            },
            sprite.Uvs);
        Assert.Equal(16f, sprite.Scale);
        Assert.Equal(new Vector2(0.25f, 0.75f), sprite.Pivot);
        Assert.Equal(SpriteTopology.Triangles, sprite.Topology);
        Assert.Equal(new ushort[] { 0, 1, 2, 0, 2, 3 }, sprite.Indices);

        BakeReferenceSite site = Assert.Single(context.References);
        Assert.Equal("texture", site.MemberPath);
        Assert.Equal(TexturePath, site.ReferencePath);
        Assert.False(site.IsData);
        Assert.Equal(1, site.Line);
        Assert.Equal(11, site.Column);

        using var directory = TempPackDirectory.CreateUnderTemp();
        var descriptor = new TextureDescriptor
        {
            Payload = TexturePayloadPath,
            Width = 2,
            Height = 2,
            Format = "rgba8",
            Filter = "linear",
            Wrap = "clamp",
        };

        string packPath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddMessagePack(TexturePath, descriptor);
                builder.AddRaw(TexturePayloadPath, new byte[16]);
                _fixture.Manifest.AddToPack(builder, SpriteType, SpritePath, sprite);
            });

        using var host = new AssetServiceHost(packPath);
        host.Start();
        SpriteAsset loaded = await host.Service.LoadAsset<SpriteAsset>(SpritePath);

        Assert.Equal(TexturePath, loaded.Texture.Path);
        Assert.Equal(sprite.Uvs, loaded.Uvs);
        Assert.Equal(sprite.Scale, loaded.Scale);
        Assert.Equal(sprite.Pivot, loaded.Pivot);
        Assert.Equal(SpriteTopology.Triangles, loaded.Topology);
        Assert.Equal(sprite.Indices, loaded.Indices);
    }

    [Fact]
    public void InvalidSpriteFieldValuesStillBindBecauseFieldValidationIsM5()
    {
        const string toml = """
            texture = "textures/player"
            uvs = [[0.0, 0.0]]
            scale = -1.0
            pivot = [2.0, -2.0]
            topology = "triangles"
            """;

        var diagnostics = new BakeDiagnostics();
        BakeBindContext context = BindContext(toml, diagnostics);
        object? value = _fixture.Manifest.Bind(SpriteType, context);

        Assert.Empty(diagnostics.GetSorted());
        var sprite = Assert.IsType<SpriteAsset>(value);
        Assert.Single(sprite.Uvs!);
        Assert.Equal(-1f, sprite.Scale);
        Assert.Equal(new Vector2(2f, -2f), sprite.Pivot);
        Assert.Null(sprite.Indices);
        Assert.Single(context.References);
    }

    private static BakeBindContext BindContext(string toml, BakeDiagnostics diagnostics)
    {
        Assert.True(
            TomlDocument.TryParse(
                toml,
                "Assets/sprites/player.toml",
                diagnostics,
                out TomlTable root,
                out TomlSyntaxIndex syntax));
        return new BakeBindContext(
            root,
            syntax,
            diagnostics,
            "Assets/sprites/player.toml",
            SpritePath);
    }
}
