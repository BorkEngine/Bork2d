using Bork2d.Bake.Baking;
using Bork2d.Bake.Cli;
using Bork2d.Bake.Incremental;
using Bork2d.Tests.Bake.Baking;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Incremental;

/// <summary>
/// The incremental bake contract (§16.7): a second bake of unchanged inputs skips without touching
/// the pack, a changed TOML or texture re-bakes, a deleted source disappears, a corrupt manifest or
/// missing/modified pack re-bakes, <c>--force</c> re-bakes, <c>--check</c> writes neither artifact,
/// a mode change re-bakes, and a failed bake leaves both the pack and the manifest unchanged.
/// </summary>
[Collection(BakeProjectFixtureCollection.Name)]
public sealed class BakeIncrementalCliTests
{
    private const string GameToml = """
        name = "campaign"
        health = 120
        max_players = 4
        must = "present"
        tint = [1.0, 0.5, 0.25, 1.0]
        texture = "textures/player"

        [stats]
        attack = 3
        defense = 7

        [bork2d]
        type = "Bork2d.BakeFixture.GameConfig"
        """;

    private const string StatsToml = """
        attack = 11
        defense = 13

        [bork2d]
        type = "Bork2d.BakeFixture.StatsRoot"
        """;

    private const string SpriteToml = """
        texture = "textures/player"
        uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]
        scale = 16.0
        pivot = [0.5, 0.5]
        topology = "triangles"
        indices = [0, 1, 2, 0, 2, 3]

        [bork2d]
        type = "Bork2d.Api.Assets.SpriteAsset"
        """;

    private const string SidecarToml = """
        filter = "nearest"
        wrap = "repeat"
        """;

    private static readonly string[] _expectedInputs =
    [
        "Assets/data/game.toml",
        "Assets/roots/stats.toml",
        "Assets/sprites/player.toml",
        "Assets/textures/player.png",
        "Assets/textures/player.png.toml",
    ];

    private static readonly string[] _expectedEntries =
    [
        BakeProjectFixture.GamePath,
        BakeProjectFixture.StatsPath,
        BakeProjectFixture.SpritePath,
        BakeProjectFixture.TexturePath,
        BakeProjectFixture.TexturePayloadPath,
    ];

    private readonly BakeProjectFixture _fixture;

    /// <summary>Initializes a new instance of the <see cref="BakeIncrementalCliTests"/> class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public BakeIncrementalCliTests(BakeProjectFixture fixture) => _fixture = fixture;

    [Fact]
    public void FirstBakeWritesTheManifestAndTheSecondSkips()
    {
        (string project, string pack) = CreateProject("incremental-skip");

        Result first = Bake(project, pack);

        Assert.Equal(CliApplication.SuccessExitCode, first.ExitCode);
        Assert.Contains("baked '", first.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, first.Error);
        Assert.True(File.Exists(pack));

        string manifestPath = ManifestPath(project);
        Assert.True(File.Exists(manifestPath));
        BakeIncrementalManifest manifest = Assert.IsType<BakeIncrementalManifest>(
            BakeManifestStore.TryRead(manifestPath));
        Assert.Equal(BakeIncrementalManifest.CurrentVersion, manifest.Version);
        Assert.Equal(BakeToolInfo.Version, manifest.ToolVersion);
        Assert.Equal("debug", manifest.Mode);
        Assert.Equal(Path.GetFullPath(pack), manifest.OutputPath);
        Assert.Equal(BakeHash.HashFile(pack), manifest.OutputHash);
        Assert.Equal(3, manifest.AssetCount);
        Assert.Equal(1, manifest.TextureCount);
        Assert.Equal(2, manifest.ReferenceCount);
        Assert.Equal(_expectedInputs, manifest.Inputs.Select(input => input.Path));
        Assert.All(
            manifest.Inputs,
            input => Assert.Equal(
                BakeHash.HashFile(Path.Combine(
                    project,
                    input.Path.Replace('/', Path.DirectorySeparatorChar))),
                input.Hash));

        byte[] packBytes = File.ReadAllBytes(pack);
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        DateTime packWriteTime = File.GetLastWriteTimeUtc(pack);

        Result second = Bake(project, pack);

        Assert.Equal(CliApplication.SuccessExitCode, second.ExitCode);
        Assert.Contains("up to date", second.Output, StringComparison.Ordinal);
        Assert.Contains("no inputs changed", second.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, second.Error);
        Assert.Equal(packBytes, File.ReadAllBytes(pack));
        Assert.Equal(packWriteTime, File.GetLastWriteTimeUtc(pack));
        Assert.Equal(manifestBytes, File.ReadAllBytes(manifestPath));

        Assert.Equal(_expectedEntries, BakedPackAssertions.Shapes(pack).Select(shape => shape.Path));
    }

    [Fact]
    public void ChangingOneTomlRebakes()
    {
        (string project, string pack) = CreateProject("incremental-toml");
        Assert.Equal(CliApplication.SuccessExitCode, Bake(project, pack).ExitCode);
        byte[] before = File.ReadAllBytes(pack);
        byte[] manifestBefore = File.ReadAllBytes(ManifestPath(project));

        BakeProjectFixture.WriteAsset(
            project,
            "data/game.toml",
            GameToml.Replace("health = 120", "health = 121", StringComparison.Ordinal));

        Result result = Bake(project, pack);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("baked '", result.Output, StringComparison.Ordinal);
        Assert.NotEqual(before, File.ReadAllBytes(pack));
        Assert.NotEqual(manifestBefore, File.ReadAllBytes(ManifestPath(project)));
    }

    [Fact]
    public void ChangingOneTextureRebakes()
    {
        (string project, string pack) = CreateProject("incremental-texture");
        Assert.Equal(CliApplication.SuccessExitCode, Bake(project, pack).ExitCode);
        Assert.Equal(
            BakeProjectFixture.ExpectedTexturePayload,
            ReadRawPayload(pack, BakeProjectFixture.TexturePayloadPath));

        BakeProjectFixture.WriteAssetBytes(
            project,
            "textures/player.png",
            TextureImageFactory.GrayscalePng());

        Result result = Bake(project, pack);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("baked '", result.Output, StringComparison.Ordinal);
        Assert.Equal(
            new byte[]
            {
                0, 0, 0, 255,
                85, 85, 85, 255,
                170, 170, 170, 255,
                255, 255, 255, 255,
            },
            ReadRawPayload(pack, BakeProjectFixture.TexturePayloadPath));
    }

    [Fact]
    public void DeletingASourceRebakesAndDropsTheAsset()
    {
        (string project, string pack) = CreateProject("incremental-delete");
        BakeProjectFixture.WriteAsset(
            project,
            "roots/extra.toml",
            """
            attack = 2
            defense = 4

            [bork2d]
            type = "Bork2d.BakeFixture.StatsRoot"
            """);
        Assert.Equal(CliApplication.SuccessExitCode, Bake(project, pack).ExitCode);
        Assert.Contains(
            BakeProjectFixture.StatsPath,
            BakedPackAssertions.Shapes(pack).Select(shape => shape.Path));

        File.Delete(Path.Combine(project, "Assets", "roots", "stats.toml"));

        Result result = Bake(project, pack);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("baked '", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            BakeProjectFixture.StatsPath,
            BakedPackAssertions.Shapes(pack).Select(shape => shape.Path));
        Assert.DoesNotContain(
            BakeProjectFixture.StatsPath,
            Assert.IsType<BakeIncrementalManifest>(
                BakeManifestStore.TryRead(ManifestPath(project))).Inputs.Select(input => input.Path));
    }

    [Fact]
    public void CorruptManifestForcesAFullBake()
    {
        (string project, string pack) = CreateProject("incremental-corrupt-manifest");
        Assert.Equal(CliApplication.SuccessExitCode, Bake(project, pack).ExitCode);
        string manifestPath = ManifestPath(project);
        File.WriteAllText(manifestPath, "{ this is not a manifest");

        Result result = Bake(project, pack);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("baked '", result.Output, StringComparison.Ordinal);
        Assert.NotNull(BakeManifestStore.TryRead(manifestPath));
    }

    [Fact]
    public void MissingPackForcesAFullBake()
    {
        (string project, string pack) = CreateProject("incremental-missing-pack");
        Assert.Equal(CliApplication.SuccessExitCode, Bake(project, pack).ExitCode);
        byte[] before = File.ReadAllBytes(pack);
        File.Delete(pack);

        Result result = Bake(project, pack);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("baked '", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(pack));
    }

    [Fact]
    public void ExternallyModifiedPackForcesAFullBake()
    {
        (string project, string pack) = CreateProject("incremental-modified-pack");
        Assert.Equal(CliApplication.SuccessExitCode, Bake(project, pack).ExitCode);
        byte[] before = File.ReadAllBytes(pack);
        File.WriteAllBytes(pack, [0x00, 0x01, 0x02, 0x03]);

        Result result = Bake(project, pack);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("baked '", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(pack));
        Assert.Equal(
            BakeHash.HashFile(pack),
            Assert.IsType<BakeIncrementalManifest>(
                BakeManifestStore.TryRead(ManifestPath(project))).OutputHash);
    }

    [Fact]
    public void ForceBypassesTheSkip()
    {
        (string project, string pack) = CreateProject("incremental-force");
        Assert.Equal(CliApplication.SuccessExitCode, Bake(project, pack).ExitCode);
        Assert.Contains(
            "up to date",
            Bake(project, pack).Output,
            StringComparison.Ordinal);
        byte[] before = File.ReadAllBytes(pack);

        Result forced = Bake(project, pack, "--force");

        Assert.Equal(CliApplication.SuccessExitCode, forced.ExitCode);
        Assert.Contains("baked '", forced.Output, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(pack));
    }

    [Fact]
    public void FailedBakeLeavesThePackAndManifestUnchanged()
    {
        (string project, string pack) = CreateProject("incremental-failed");
        Assert.Equal(CliApplication.SuccessExitCode, Bake(project, pack).ExitCode);
        byte[] packBefore = File.ReadAllBytes(pack);
        byte[] manifestBefore = File.ReadAllBytes(ManifestPath(project));

        BakeProjectFixture.WriteAsset(project, "data/game.toml", "name = \n");

        Result result = Bake(project, pack);

        Assert.Equal(CliApplication.DiagnosticsExitCode, result.ExitCode);
        Assert.Contains("error BK2001:", result.Error, StringComparison.Ordinal);
        Assert.Contains("no pack was written", result.Output, StringComparison.Ordinal);
        Assert.Equal(packBefore, File.ReadAllBytes(pack));
        Assert.Equal(manifestBefore, File.ReadAllBytes(ManifestPath(project)));
    }

    [Fact]
    public void CheckWritesNeitherPackNorManifestAndNeverSkips()
    {
        (string project, string pack) = CreateProject("incremental-check");
        string manifestPath = ManifestPath(project);

        Result fresh = Bake(project, pack, "--check");

        Assert.Equal(CliApplication.SuccessExitCode, fresh.ExitCode);
        Assert.Contains("no pack written (--check)", fresh.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(pack));
        Assert.False(File.Exists(manifestPath));

        Assert.Equal(CliApplication.SuccessExitCode, Bake(project, pack).ExitCode);
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        byte[] packBytes = File.ReadAllBytes(pack);

        Result afterBake = Bake(project, pack, "--check");

        Assert.Equal(CliApplication.SuccessExitCode, afterBake.ExitCode);
        Assert.Contains("no pack written (--check)", afterBake.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("up to date", afterBake.Output, StringComparison.Ordinal);
        Assert.Equal(packBytes, File.ReadAllBytes(pack));
        Assert.Equal(manifestBytes, File.ReadAllBytes(manifestPath));
    }

    [Fact]
    public void ModeChangeForcesAFullBake()
    {
        (string project, string pack) = CreateProject("incremental-mode");
        Assert.Equal(CliApplication.SuccessExitCode, Bake(project, pack).ExitCode);
        Assert.Equal(
            "debug",
            Assert.IsType<BakeIncrementalManifest>(
                BakeManifestStore.TryRead(ManifestPath(project))).Mode);

        Result release = Bake(project, pack, "--mode", "release");

        Assert.Equal(CliApplication.SuccessExitCode, release.ExitCode);
        Assert.Contains("baked '", release.Output, StringComparison.Ordinal);
        Assert.Contains("release mode", release.Output, StringComparison.Ordinal);
        Assert.Equal(
            "release",
            Assert.IsType<BakeIncrementalManifest>(
                BakeManifestStore.TryRead(ManifestPath(project))).Mode);

        Result debug = Bake(project, pack, "--mode", "debug");

        Assert.Equal(CliApplication.SuccessExitCode, debug.ExitCode);
        Assert.Contains("baked '", debug.Output, StringComparison.Ordinal);
        Assert.Equal(
            "debug",
            Assert.IsType<BakeIncrementalManifest>(
                BakeManifestStore.TryRead(ManifestPath(project))).Mode);
    }

    [Fact]
    public void TextureAndSidecarUnderArbitraryPathsAreTrackedInputs()
    {
        string project = BakeProjectFixture.CreateProject("incremental-art");
        BakeProjectFixture.WriteAssetBytes(
            project,
            "art/player.png",
            TextureImageFactory.RgbaPng());
        BakeProjectFixture.WriteAsset(project, "art/player.png.toml", SidecarToml);
        string pack = Path.Combine(project, "out", "assets.pck");

        Result result = Run(
            "bake",
            "--project",
            Path.Combine(project, BakeProjectFixture.DescriptorFileName),
            "--host",
            _fixture.EmptyHostAssemblyPath,
            "--output",
            pack);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        BakeIncrementalManifest manifest = Assert.IsType<BakeIncrementalManifest>(
            BakeManifestStore.TryRead(ManifestPath(project)));
        Assert.Contains(
            manifest.Inputs,
            input => input.Path == "Assets/art/player.png");
        Assert.Contains(
            manifest.Inputs,
            input => input.Path == "Assets/art/player.png.toml");
        Assert.Equal(
            new[] { "art/player", "art/player-raw" },
            BakedPackAssertions.Shapes(pack).Select(shape => shape.Path));
    }

    private static string ManifestPath(string project)
        => Path.Combine(project, "obj", "bork2d", "bake", "manifest.json");

    private static byte[] ReadRawPayload(string packPath, string assetPath)
        => BakedPackAssertions
            .Payloads(packPath)
            .Zip(BakedPackAssertions.Shapes(packPath))
            .Single(pair => pair.Second.Path == assetPath)
            .First;

    private static (string Project, string PackPath) CreateProject(string name)
    {
        string project = BakeProjectFixture.CreateProject(name);
        BakeProjectFixture.WriteAsset(project, "data/game.toml", GameToml);
        BakeProjectFixture.WriteAsset(project, "roots/stats.toml", StatsToml);
        BakeProjectFixture.WriteAsset(project, "sprites/player.toml", SpriteToml);
        BakeProjectFixture.WriteAsset(project, "textures/player.png.toml", SidecarToml);
        BakeProjectFixture.WriteTexture(project);
        return (project, Path.Combine(project, "out", "assets.pck"));
    }

    private Result Bake(string project, string packPath, params string[] extra)
        => Run(
        [
            "bake",
            "--project",
            Path.Combine(project, BakeProjectFixture.DescriptorFileName),
            "--host",
            _fixture.HostAssemblyPath,
            "--output",
            packPath,
            .. extra,
        ]);

    private static Result Run(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = CliApplication.Run(arguments, output, error);
        return new Result(exitCode, output.ToString(), error.ToString());
    }

    private sealed record Result(int ExitCode, string Output, string Error);
}
