using Bork2d.Bake.Cli;
using Bork2d.Bake.Incremental;
using Bork2d.Tests.Bake.Baking;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Incremental;

/// <summary>
/// The all-or-nothing incremental contract for a multi-pack project (D-M4-14): every pack output is
/// recorded, a second bake skips only when every output is current, and a missing or changed DLC pack
/// or DLC input re-bakes the whole project.
/// </summary>
[Collection(BakeProjectFixtureCollection.Name)]
public sealed class MultiPackIncrementalTests
{
    private const string DescriptorFileName = BakeProjectFixture.DescriptorFileName;

    private readonly BakeProjectFixture _fixture;

    /// <summary>Initializes a new instance of the <see cref="MultiPackIncrementalTests"/> class.</summary>
    /// <param name="fixture">The shared fixture host.</param>
    public MultiPackIncrementalTests(BakeProjectFixture fixture) => _fixture = fixture;

    [Fact]
    public void ManifestRecordsEveryOutputAndTheSecondBakeSkips()
    {
        (string project, string outputRoot) = CreateProject("multi-incremental-skip");

        Result first = Bake(project, outputRoot);

        Assert.Equal(CliApplication.SuccessExitCode, first.ExitCode);
        Assert.Contains("2 pack(s)", first.Output, StringComparison.Ordinal);
        BakeIncrementalManifest manifest = Assert.IsType<BakeIncrementalManifest>(
            BakeManifestStore.TryRead(ManifestPath(project)));
        Assert.Equal(2, manifest.Outputs.Count);
        Assert.Equal(Path.Combine(outputRoot, "assets.pck"), manifest.Outputs[0].Path);
        Assert.Equal(Path.Combine(outputRoot, "dlc1.pck"), manifest.Outputs[1].Path);
        Assert.Equal(
            BakeHash.HashFile(Path.Combine(outputRoot, "assets.pck")),
            manifest.Outputs[0].Hash);
        Assert.Equal(
            BakeHash.HashFile(Path.Combine(outputRoot, "dlc1.pck")),
            manifest.Outputs[1].Hash);
        Assert.Contains(
            manifest.Inputs,
            input => input.Path == "DLC1/data/dlc.toml");
        Assert.Equal(4, manifest.AssetCount);

        byte[] mainBytes = File.ReadAllBytes(Path.Combine(outputRoot, "assets.pck"));
        byte[] dlcBytes = File.ReadAllBytes(Path.Combine(outputRoot, "dlc1.pck"));
        byte[] manifestBytes = File.ReadAllBytes(ManifestPath(project));

        Result second = Bake(project, outputRoot);

        Assert.Equal(CliApplication.SuccessExitCode, second.ExitCode);
        Assert.Contains("up to date", second.Output, StringComparison.Ordinal);
        Assert.Contains("2 pack(s)", second.Output, StringComparison.Ordinal);
        Assert.Equal(mainBytes, File.ReadAllBytes(Path.Combine(outputRoot, "assets.pck")));
        Assert.Equal(dlcBytes, File.ReadAllBytes(Path.Combine(outputRoot, "dlc1.pck")));
        Assert.Equal(manifestBytes, File.ReadAllBytes(ManifestPath(project)));
    }

    [Fact]
    public void DeletingOnePackOutputRebakesEveryPack()
    {
        (string project, string outputRoot) = CreateProject("multi-incremental-missing-output");
        Assert.Equal(CliApplication.SuccessExitCode, Bake(project, outputRoot).ExitCode);
        byte[] mainBytes = File.ReadAllBytes(Path.Combine(outputRoot, "assets.pck"));
        byte[] dlcBytes = File.ReadAllBytes(Path.Combine(outputRoot, "dlc1.pck"));

        File.Delete(Path.Combine(outputRoot, "dlc1.pck"));

        Result result = Bake(project, outputRoot);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("baked '", result.Output, StringComparison.Ordinal);
        Assert.Equal(mainBytes, File.ReadAllBytes(Path.Combine(outputRoot, "assets.pck")));
        Assert.Equal(dlcBytes, File.ReadAllBytes(Path.Combine(outputRoot, "dlc1.pck")));
    }

    [Fact]
    public void ChangingADlcInputRebakesEveryPack()
    {
        (string project, string outputRoot) = CreateProject("multi-incremental-dlc-change");
        Assert.Equal(CliApplication.SuccessExitCode, Bake(project, outputRoot).ExitCode);
        byte[] manifestBefore = File.ReadAllBytes(ManifestPath(project));
        byte[] dlcBefore = File.ReadAllBytes(Path.Combine(outputRoot, "dlc1.pck"));

        BakeProjectFixture.WriteAssetUnder(
            project,
            "DLC1",
            "data/dlc.toml",
            """
            name = "dlc-changed"
            health = 11
            must = "overlay"
            texture = "art/player"

            [bork2d]
            type = "Bork2d.BakeFixture.GameConfig"
            """);

        Result result = Bake(project, outputRoot);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("baked '", result.Output, StringComparison.Ordinal);
        Assert.NotEqual(dlcBefore, File.ReadAllBytes(Path.Combine(outputRoot, "dlc1.pck")));
        Assert.NotEqual(manifestBefore, File.ReadAllBytes(ManifestPath(project)));
    }

    [Fact]
    public void ForceRebakesEveryPack()
    {
        (string project, string outputRoot) = CreateProject("multi-incremental-force");
        Assert.Equal(CliApplication.SuccessExitCode, Bake(project, outputRoot).ExitCode);
        Assert.Contains(
            "up to date",
            Bake(project, outputRoot).Output,
            StringComparison.Ordinal);
        byte[] mainBytes = File.ReadAllBytes(Path.Combine(outputRoot, "assets.pck"));
        byte[] dlcBytes = File.ReadAllBytes(Path.Combine(outputRoot, "dlc1.pck"));

        Result forced = Bake(project, outputRoot, "--force");

        Assert.Equal(CliApplication.SuccessExitCode, forced.ExitCode);
        Assert.Contains("baked '", forced.Output, StringComparison.Ordinal);
        Assert.Equal(mainBytes, File.ReadAllBytes(Path.Combine(outputRoot, "assets.pck")));
        Assert.Equal(dlcBytes, File.ReadAllBytes(Path.Combine(outputRoot, "dlc1.pck")));
    }

    [Fact]
    public void FailedDlcBakeLeavesEveryPackAndTheManifestUnchanged()
    {
        (string project, string outputRoot) = CreateProject("multi-incremental-failure");
        Assert.Equal(CliApplication.SuccessExitCode, Bake(project, outputRoot).ExitCode);
        byte[] mainBefore = File.ReadAllBytes(Path.Combine(outputRoot, "assets.pck"));
        byte[] dlcBefore = File.ReadAllBytes(Path.Combine(outputRoot, "dlc1.pck"));
        byte[] manifestBefore = File.ReadAllBytes(ManifestPath(project));

        BakeProjectFixture.WriteAssetUnder(project, "DLC1", "data/dlc.toml", "name = \n");

        Result result = Bake(project, outputRoot);

        Assert.Equal(CliApplication.DiagnosticsExitCode, result.ExitCode);
        Assert.Contains("no pack was written", result.Output, StringComparison.Ordinal);
        Assert.Equal(mainBefore, File.ReadAllBytes(Path.Combine(outputRoot, "assets.pck")));
        Assert.Equal(dlcBefore, File.ReadAllBytes(Path.Combine(outputRoot, "dlc1.pck")));
        Assert.Equal(manifestBefore, File.ReadAllBytes(ManifestPath(project)));
    }

    private (string Project, string OutputRoot) CreateProject(string name)
    {
        string project = BakeProjectFixture.CreatePackProject(
            name,
            ("Assets", "assets.pck"),
            ("DLC1", "dlc1.pck"));
        WriteMainAssets(project);
        BakeProjectFixture.WriteAssetUnder(
            project,
            "DLC1",
            "data/dlc.toml",
            """
            name = "dlc"
            health = 10
            must = "overlay"
            texture = "art/player"

            [bork2d]
            type = "Bork2d.BakeFixture.GameConfig"
            """);
        return (project, Path.Combine(project, "out"));
    }

    private static void WriteMainAssets(string project)
    {
        BakeProjectFixture.WriteAssetUnder(
            project,
            "Assets",
            "data/game.toml",
            """
            name = "campaign"
            health = 120
            max_players = 4
            must = "present"
            tint = [1.0, 0.5, 0.25, 1.0]
            texture = "art/player"

            [stats]
            attack = 3
            defense = 7

            [bork2d]
            type = "Bork2d.BakeFixture.GameConfig"
            """);
        BakeProjectFixture.WriteAssetUnder(
            project,
            "Assets",
            "roots/stats.toml",
            """
            attack = 11
            defense = 13

            [bork2d]
            type = "Bork2d.BakeFixture.StatsRoot"
            """);
        BakeProjectFixture.WriteAssetUnder(
            project,
            "Assets",
            "sprites/player.toml",
            """
            texture = "art/player"
            uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]
            scale = 16.0
            pivot = [0.5, 0.5]
            topology = "triangles"
            indices = [0, 1, 2, 0, 2, 3]

            [bork2d]
            type = "Bork2d.Api.Assets.SpriteAsset"
            """);
        BakeProjectFixture.WriteAssetUnder(
            project,
            "Assets",
            "art/player.png.toml",
            """
            filter = "nearest"
            wrap = "repeat"
            """);
        BakeProjectFixture.WriteAssetBytesUnder(
            project,
            "Assets",
            "art/player.png",
            TextureImageFactory.RgbaPng());
    }

    private static string ManifestPath(string project)
        => Path.Combine(project, "obj", "bork2d", "bake", "manifest.json");

    private Result Bake(string project, string outputRoot, params string[] extra)
        => Run(
        [
            "bake",
            "--project",
            Path.Combine(project, DescriptorFileName),
            "--host",
            _fixture.HostAssemblyPath,
            "--output-root",
            outputRoot,
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
