using Bork2d.Api.Assets;
using Bork2d.Api.Rendering;
using Bork2d.Assets.Pack;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Tests.Assets.Service;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// The acceptance path for WP1: explicit registrations instead of a generated host, a full bake to one
/// pack, and the runtime loader reading every baked value back. Also covers check-only, empty and
/// texture-only projects, invalid input leaving the previous pack untouched, and structural
/// determinism.
/// </summary>
public sealed class BakePipelineTests
{
    [Fact]
    public async Task FullBakeLoadsCustomDataSpriteTextureAndStructRoot()
    {
        using var project = TestBakeProject.Create("full");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var directory = TempBakeDirectory.Create();
        string packPath = directory.FullPath("assets.pck");

        BakeResult result = RunBake(project.Project, directory.RootPath);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.Equal(packPath, result.PackPath);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(3, result.AssetCount);
        Assert.Equal(1, result.TextureCount);
        Assert.Equal(3, result.ReferenceCount);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code.StartsWith("BK5", StringComparison.Ordinal));
        Assert.Equal(
            new[]
            {
                BakeProjectFixture.TexturePath,
                BakeProjectFixture.TexturePayloadPath,
                BakeProjectFixture.GamePath,
                BakeProjectFixture.StatsPath,
                BakeProjectFixture.SpritePath,
            },
            result.Entries.Select(entry => entry.Path));

        using var host = new AssetServiceHost(packPath);
        host.Start();

        Bork2d.BakeFixture.GameConfig config =
            await host.Service.LoadAsset<Bork2d.BakeFixture.GameConfig>(BakeProjectFixture.GamePath);
        Assert.Equal("campaign", config.Name);
        Assert.Equal(120, config.Health);
        Assert.Equal(4, config.MaxPlayers);
        Assert.Equal("present", config.Must);
        Assert.Null(config.DebugLabel);
        Assert.Equal(new Bork2d.BakeFixture.Stats(3, 7), config.Stats);
        Assert.Equal(new Color(1f, 0.5f, 0.25f, 1f), config.Tint);
        Assert.Equal(BakeProjectFixture.TexturePath, config.Texture.Path);

        TextureDescriptor texture = await host.Service.LoadAsset(config.Texture);
        Assert.Equal(BakeProjectFixture.TexturePayloadPath, texture.Payload.Path);
        Assert.Equal(2u, texture.Width);
        Assert.Equal(2u, texture.Height);
        Assert.Equal("rgba8", texture.Format);
        Assert.Equal("nearest", texture.Filter);
        Assert.Equal("repeat", texture.Wrap);
        Assert.Equal(
            BakeProjectFixture.ExpectedTexturePayload,
            await host.Service.LoadData(texture.Payload));

        SpriteAsset sprite = await host.Service.LoadAsset<SpriteAsset>(BakeProjectFixture.SpritePath);
        Assert.Equal(BakeProjectFixture.TexturePath, sprite.Texture.Path);
        Assert.Equal(16f, sprite.Scale);
        Assert.Equal(SpriteTopology.Triangles, sprite.Topology);
        Assert.Equal(new ushort[] { 0, 1, 2, 0, 2, 3 }, sprite.Indices);

        Bork2d.BakeFixture.StatsRoot stats =
            await host.Service.LoadAsset<Bork2d.BakeFixture.StatsRoot>(BakeProjectFixture.StatsPath);
        Assert.Equal(new Bork2d.BakeFixture.StatsRoot(11, 13), stats);

        var statsReference = new AssetRef<Bork2d.BakeFixture.StatsRoot>(BakeProjectFixture.StatsPath);
        Bork2d.BakeFixture.StatsRoot referenced = await host.Service.LoadAsset(statsReference);
        Assert.Equal(stats, referenced);

        Assert.True(host.Service.Exists(BakeProjectFixture.TexturePath));
        Assert.True(host.Service.Exists(BakeProjectFixture.TexturePayloadPath));
    }

    [Fact]
    public void CheckValidatesEverythingAndWritesNothing()
    {
        using var project = TestBakeProject.Create("check");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var directory = TempBakeDirectory.Create();
        string packPath = directory.FullPath("assets.pck");

        BakeResult result = RunBake(project.Project, directory.RootPath, check: true);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.False(result.PackWritten);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(3, result.AssetCount);
        Assert.Equal(1, result.TextureCount);
        Assert.Equal(3, result.ReferenceCount);
        Assert.False(File.Exists(packPath));
    }

    [Fact]
    public void EmptyProjectProducesAValidEmptyPack()
    {
        using var project = TestBakeProject.Create("empty");
        using var directory = TempBakeDirectory.Create();
        string packPath = directory.FullPath("assets.pck");

        BakeResult result = RunBake(project.Project, directory.RootPath);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, result.AssetCount);
        Assert.Equal(0, result.TextureCount);

        using PackArchive archive = PackArchive.Open(packPath);
        Assert.Empty(archive.Entries);
    }

    [Fact]
    public async Task TextureOnlyProjectProducesTheDescriptorAndPayload()
    {
        using var project = TestBakeProject.Create("texture-only");
        project.WriteTexture();
        using var directory = TempBakeDirectory.Create();
        string packPath = directory.FullPath("assets.pck");

        BakeResult result = RunBake(project.Project, directory.RootPath);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == BakeSeverity.Error);
        Assert.Equal(0, result.AssetCount);
        Assert.Equal(1, result.TextureCount);
        Assert.Equal(
            new[] { BakeProjectFixture.TexturePath, BakeProjectFixture.TexturePayloadPath },
            result.Entries.Select(entry => entry.Path));

        using var host = new AssetServiceHost(packPath);
        host.Start();
        TextureDescriptor descriptor =
            await host.Service.LoadAsset<TextureDescriptor>(BakeProjectFixture.TexturePath);
        Assert.Equal("linear", descriptor.Filter);
        Assert.Equal("clamp", descriptor.Wrap);
        Assert.Equal(
            BakeProjectFixture.ExpectedTexturePayload,
            await host.Service.LoadData(BakeProjectFixture.TexturePayloadPath));
    }

    [Theory]
    [MemberData(nameof(InvalidGameSources))]
    public void InvalidSourceLeavesThePreviousPackUnchanged(string toml, string expectedCode)
    {
        using var project = TestBakeProject.Create("invalid-source");
        project.WriteStandardAssets();
        project.WriteAsset("data/game.toml", toml);
        project.WriteTexture();
        using var directory = TempBakeDirectory.Create();
        string packPath = directory.FullPath("assets.pck");
        BakedPackAssertions.WriteValidPack(packPath);
        byte[] before = File.ReadAllBytes(packPath);

        BakeResult result = RunBake(project.Project, directory.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.False(result.PackWritten);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
        Assert.All(result.Diagnostics, diagnostic => Assert.NotNull(diagnostic.FilePath));
        Assert.Equal(before, File.ReadAllBytes(packPath));
    }

    [Fact]
    public void PackPathIsResolvedUnderTheOutputRoot()
    {
        using var project = TestBakeProject.Create("default-pack");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var directory = TempBakeDirectory.Create();

        BakeResult result = RunBake(project.Project, directory.RootPath);

        string expectedPath = directory.FullPath("assets.pck");
        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.Equal(expectedPath, result.PackPath);
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public void DataUnderTexturesBakesAsOrdinaryCustomData()
    {
        using var project = TestBakeProject.Create("data-under-textures");
        project.WriteStandardAssets();
        project.WriteTexture();
        project.WriteAsset(
            "textures/notes.toml",
            """
            attack = 3
            defense = 5

            [bork2d]
            type = "Bork2d.BakeFixture.StatsRoot"
            """);
        using var directory = TempBakeDirectory.Create();

        BakeResult result = RunBake(project.Project, directory.RootPath);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Contains(
            result.Entries,
            entry => entry.Path == "textures/notes"
                && entry.TypeName == "Bork2d.BakeFixture.StatsRoot");
        Assert.DoesNotContain(
            result.Entries,
            entry => entry.Path == "textures/notes-raw");
    }

    [Fact]
    public void DataAndTextureDescriptorPathCollisionIsBK3005AndPreventsThePack()
    {
        using var project = TestBakeProject.Create("collision-descriptor");
        project.WriteStandardAssets();
        project.WriteTexture();
        project.WriteAsset(
            "art/player.toml",
            """
            name = "art"
            health = 1
            must = "m"

            [bork2d]
            type = "Bork2d.BakeFixture.GameConfig"
            """);
        using var directory = TempBakeDirectory.Create();
        string packPath = directory.FullPath("assets.pck");

        BakeResult result = RunBake(project.Project, directory.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.False(result.PackWritten);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.AssetPathCollision
                && diagnostic.AssetPath == "art/player");
        Assert.False(File.Exists(packPath));
    }

    [Fact]
    public void DataAndRawPayloadPathCollisionIsBK3005AndPreventsThePack()
    {
        using var project = TestBakeProject.Create("collision-raw");
        project.WriteStandardAssets();
        project.WriteTexture();
        project.WriteAsset(
            "art/player-raw.toml",
            """
            attack = 2
            defense = 4

            [bork2d]
            type = "Bork2d.BakeFixture.StatsRoot"
            """);
        using var directory = TempBakeDirectory.Create();
        string packPath = directory.FullPath("assets.pck");

        BakeResult result = RunBake(project.Project, directory.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.False(result.PackWritten);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.AssetPathCollision
                && diagnostic.AssetPath == "art/player-raw");
        Assert.False(File.Exists(packPath));
    }

    [Fact]
    public void TwoBakesOfTheSameInputsAreStructurallyIdentical()
    {
        using var project = TestBakeProject.Create("deterministic");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var first = TempBakeDirectory.Create();
        using var second = TempBakeDirectory.Create();

        BakeResult firstResult = RunBake(project.Project, first.RootPath);
        BakeResult secondResult = RunBake(project.Project, second.RootPath);

        Assert.Equal(BakeStatus.Success, firstResult.Status);
        Assert.Equal(BakeStatus.Success, secondResult.Status);
        Assert.Empty(firstResult.Diagnostics);
        Assert.Empty(secondResult.Diagnostics);

        BakedPackAssertions.AssertStructurallyEqual(
            first.FullPath("assets.pck"),
            second.FullPath("assets.pck"));
    }

    [Fact]
    public void RepeatedInvalidInputProducesTheSameDiagnosticsInTheSameOrder()
    {
        using var project = TestBakeProject.Create("stable-diagnostics");
        project.WriteStandardAssets();
        project.WriteTexture();
        project.WriteAsset(
            "data/game.toml",
            """
            health = "many"
            bogus = true
            texture = "Art/Player"

            [stats]
            attack = "strong"

            [bork2d]
            type = "Bork2d.BakeFixture.GameConfig"
            """);

        BakeResult firstRun = RunBake(project.Project, project.FullPath("out-first"));
        BakeResult secondRun = RunBake(project.Project, project.FullPath("out-second"));

        Assert.Equal(BakeStatus.DiagnosticsError, firstRun.Status);
        Assert.Equal(BakeStatus.DiagnosticsError, secondRun.Status);
        string[] first = [.. firstRun.Diagnostics.Select(diagnostic => diagnostic.Format())];
        string[] second = [.. secondRun.Diagnostics.Select(diagnostic => diagnostic.Format())];
        Assert.Equal(first, second);
        Assert.True(first.Length >= 4);
    }

    /// <summary>Gets the invalid TOML cases used by the pack-preservation theory.</summary>
    public static TheoryData<string, string> InvalidGameSources => new()
    {
        { "name = \n" + TypeTable, BakeDiagnosticCodes.TomlSyntax },
        { "health = 1\nmust = \"m\"\n" + TypeTable, BakeDiagnosticCodes.BinderMissingRequired },
        { "name = \"x\"\nhealth = 1\nmust = \"m\"\nbogus = true\n" + TypeTable, BakeDiagnosticCodes.BinderUnknownKey },
        { "name = \"x\"\nhealth = \"many\"\nmust = \"m\"\n" + TypeTable, BakeDiagnosticCodes.BinderTypeMismatch },
        {
            "name = \"x\"\nhealth = 9223372036854775807\nmust = \"m\"\n" + TypeTable,
            BakeDiagnosticCodes.BinderNumericRange
        },
        {
            "name = \"x\"\nhealth = 1\nmust = \"m\"\ntexture = \"art/missing\"\n" + TypeTable,
            BakeDiagnosticCodes.ReferenceMissing
        },
        {
            "name = \"x\"\nhealth = 1\nmust = \"m\"\ntexture = \"Art/Player\"\n" + TypeTable,
            BakeDiagnosticCodes.ReferenceNotCanonical
        },
        {
            "name = \"x\"\nhealth = 1\nmust = \"m\"\ntexture = \"art/player-raw\"\n" + TypeTable,
            BakeDiagnosticCodes.ReferenceWrongKind
        },
        {
            "name = \"x\"\nhealth = 1\nmust = \"m\"\ntexture = \"data/game\"\n" + TypeTable,
            BakeDiagnosticCodes.ReferenceWrongType
        },
    };

    private const string TypeTable = "\n[bork2d]\ntype = \"Bork2d.BakeFixture.GameConfig\"\n";

    private static BakeResult RunBake(BakeProject project, string outputRoot, bool check = false)
        => BakePipeline.Run(
            project,
            new BakeOptions(Path.GetFullPath(outputRoot), BakeMode.Debug, check, force: false),
            new BakeDiagnostics());
}
