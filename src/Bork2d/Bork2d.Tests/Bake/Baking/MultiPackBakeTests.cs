using Bork2d.Bake.Baking;
using Bork2d.Bake.Cli;
using Bork2d.Bake.Diagnostics;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// The multi-pack bake contract (D-M4-14): ordered <c>[[assets]]</c> packs, the main pack's strict
/// local references, DLC -> main references, DLC -> DLC rejection, duplicate canonical paths across
/// packs, local overlay precedence, output-root joining, and failure preservation across all packs.
/// </summary>
[Collection(BakeProjectFixtureCollection.Name)]
public sealed class MultiPackBakeTests
{
    private const string DescriptorFileName = BakeProjectFixture.DescriptorFileName;

    private readonly BakeProjectFixture _fixture;

    /// <summary>Initializes a new instance of the <see cref="MultiPackBakeTests"/> class.</summary>
    /// <param name="fixture">The shared fixture host.</param>
    public MultiPackBakeTests(BakeProjectFixture fixture) => _fixture = fixture;

    [Fact]
    public void BakesMainAndDlcPacksAndAllowsADlcReferenceToTheMainTexture()
    {
        string project = BakeProjectFixture.CreatePackProject(
            "multi-basic",
            ("Assets", "assets.pck"),
            ("DLC1", "dlc1.pck"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");

        using var output = TempBakeDirectory.Create();
        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Packs.Count);
        Assert.True(result.Packs[0].IsMain);
        Assert.False(result.Packs[1].IsMain);
        Assert.Equal(Path.Combine(output.RootPath, "assets.pck"), result.Packs[0].PackPath);
        Assert.Equal(Path.Combine(output.RootPath, "dlc1.pck"), result.Packs[1].PackPath);
        Assert.Equal(
            new[] { "art/player", "art/player-raw", "data/game", "roots/stats", "sprites/player" },
            result.Packs[0].Entries.Select(entry => entry.Path));
        Assert.Equal(
            new[] { BakeProjectFixture.DlcGamePath },
            result.Packs[1].Entries.Select(entry => entry.Path));
        Assert.True(File.Exists(result.Packs[0].PackPath));
        Assert.True(File.Exists(result.Packs[1].PackPath));

        using var archive = Bork2d.Assets.Pack.PackArchive.Open(result.Packs[1].PackPath);
        Assert.Equal(
            BakeProjectFixture.DlcGamePath,
            Assert.Single(archive.Entries).Path);
    }

    [Fact]
    public void MainPackReferenceToADlcOnlyPathIsRejected()
    {
        string project = BakeProjectFixture.CreatePackProject(
            "multi-main-strict",
            ("Assets", "assets.pck"),
            ("DLC1", "dlc1.pck"));
        // The main pack's sprite references a texture that exists only in the DLC pack.
        BakeProjectFixture.WriteAssetUnder(project, "Assets", "roots/stats.toml", StatsToml);
        BakeProjectFixture.WriteAssetUnder(project, "Assets", "sprites/player.toml", SpriteToml);
        BakeProjectFixture.WriteAssetBytesUnder(project, "DLC1", "art/player.png", TextureImageFactory.RgbaPng());
        WriteDlcConfig(project, texture: "art/player");

        using var output = TempBakeDirectory.Create();
        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.False(result.PackWritten);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == BakeDiagnosticCodes.ReferenceMissing);
        Assert.Equal("texture", diagnostic.MemberPath);
        Assert.Contains("'assets.pck'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("does not produce", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(output.RootPath, "assets.pck")));
        Assert.False(File.Exists(Path.Combine(output.RootPath, "dlc1.pck")));
    }

    [Fact]
    public void DlcReferenceToAnotherDlcIsRejectedAndNamesTheDlcPack()
    {
        string project = BakeProjectFixture.CreatePackProject(
            "multi-dlc-to-dlc",
            ("Assets", "assets.pck"),
            ("DLC1", "dlc1.pck"),
            ("DLC2", "dlc2.pck"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/dlc2");
        BakeProjectFixture.WriteAssetBytesUnder(
            project,
            "DLC2",
            "art/dlc2.png",
            TextureImageFactory.RgbaPng());

        using var output = TempBakeDirectory.Create();
        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == BakeDiagnosticCodes.ReferenceMissing);
        Assert.Contains("DLC pack 'dlc1.pck'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("main pack 'assets.pck'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("art/dlc2", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(output.RootPath, "assets.pck")));
        Assert.False(File.Exists(Path.Combine(output.RootPath, "dlc1.pck")));
        Assert.False(File.Exists(Path.Combine(output.RootPath, "dlc2.pck")));
    }

    [Fact]
    public void DlcReferenceToAPathThatAlsoExistsInMainResolvesToMainEvenWhenAnotherDlcHasIt()
    {
        string project = BakeProjectFixture.CreatePackProject(
            "multi-dlc-main-wins",
            ("Assets", "assets.pck"),
            ("DLC1", "dlc1.pck"),
            ("DLC2", "dlc2.pck"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");
        // DLC2 declares the same canonical texture path; DLC1 must still resolve it through main.
        BakeProjectFixture.WriteAssetBytesUnder(
            project,
            "DLC2",
            "art/player.png",
            TextureImageFactory.GrayscalePng());

        using var output = TempBakeDirectory.Create();
        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(3, result.Packs.Count);
        Assert.Equal(
            new[] { BakeProjectFixture.DlcGamePath },
            result.Packs[1].Entries.Select(entry => entry.Path));
        Assert.Equal(
            new[] { "art/player", "art/player-raw" },
            result.Packs[2].Entries.Select(entry => entry.Path));
    }

    [Fact]
    public void DuplicateCanonicalPathsAcrossPacksAreAllowed()
    {
        string project = BakeProjectFixture.CreatePackProject(
            "multi-duplicate-across",
            ("Assets", "assets.pck"),
            ("DLC1", "dlc1.pck"));
        WriteMainAssets(project);
        // The DLC overlays data/game with its own asset at the same canonical path.
        BakeProjectFixture.WriteAssetUnder(
            project,
            "DLC1",
            "data/game.toml",
            GameToml("art/player"));
        WriteDlcConfig(project, texture: "art/player");

        using var output = TempBakeDirectory.Create();
        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.Empty(result.Diagnostics);
        Assert.Contains(
            result.Packs[0].Entries,
            entry => entry.Path == BakeProjectFixture.GamePath);
        Assert.Contains(
            result.Packs[1].Entries,
            entry => entry.Path == BakeProjectFixture.GamePath);
        Assert.Contains(
            result.Packs[1].Entries,
            entry => entry.Path == BakeProjectFixture.DlcGamePath);
    }

    [Fact]
    public void DuplicateWithinOnePackIsStillAnErrorAndPreventsEveryPackWrite()
    {
        string project = BakeProjectFixture.CreatePackProject(
            "multi-duplicate-within",
            ("Assets", "assets.pck"),
            ("DLC1", "dlc1.pck"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");
        // The DLC declares both a texture and custom data at art/player: a within-pack collision.
        BakeProjectFixture.WriteAssetBytesUnder(
            project,
            "DLC1",
            "art/player.png",
            TextureImageFactory.RgbaPng());
        BakeProjectFixture.WriteAssetUnder(
            project,
            "DLC1",
            "art/player.toml",
            """
            attack = 1
            defense = 2

            [bork2d]
            type = "Bork2d.BakeFixture.StatsRoot"
            """);

        using var output = TempBakeDirectory.Create();
        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == BakeDiagnosticCodes.AssetPathCollision);
        Assert.Equal("art/player", diagnostic.AssetPath);
        Assert.Contains("pack 'dlc1.pck'", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(output.RootPath, "assets.pck")));
        Assert.False(File.Exists(Path.Combine(output.RootPath, "dlc1.pck")));
    }

    [Fact]
    public void LocalDlcAssetWinsReferenceValidationOverAMainPackPath()
    {
        string project = BakeProjectFixture.CreatePackProject(
            "multi-local-wins",
            ("Assets", "assets.pck"),
            ("DLC1", "dlc1.pck"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");
        // The DLC's local art/player is custom data, not a TextureDescriptor. Local resolution wins,
        // so the DLC's texture reference must fail against the local (wrong) type even though main
        // has a valid texture descriptor at the same canonical path.
        BakeProjectFixture.WriteAssetUnder(
            project,
            "DLC1",
            "art/player.toml",
            """
            attack = 3
            defense = 4

            [bork2d]
            type = "Bork2d.BakeFixture.StatsRoot"
            """);

        using var output = TempBakeDirectory.Create();
        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == BakeDiagnosticCodes.ReferenceWrongType);
        Assert.Contains("Bork2d.Api.Assets.TextureDescriptor", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(output.RootPath, "assets.pck")));
    }

    [Fact]
    public void OutputRootJoinsPackPathsAndPreservesSubdirectories()
    {
        string project = BakeProjectFixture.CreatePackProject(
            "multi-output-layout",
            ("Assets", "packs/assets.pck"),
            ("DLC1", "dlc/dlc1.pck"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");

        using var output = TempBakeDirectory.Create();
        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Equal(
            Path.Combine(output.RootPath, "packs", "assets.pck"),
            result.Packs[0].PackPath);
        Assert.Equal(
            Path.Combine(output.RootPath, "dlc", "dlc1.pck"),
            result.Packs[1].PackPath);
        Assert.True(File.Exists(result.Packs[0].PackPath));
        Assert.True(File.Exists(result.Packs[1].PackPath));
    }

    [Fact]
    public void DescriptorPackPathIsUsedWhenNoOutputRootIsGiven()
    {
        string project = BakeProjectFixture.CreatePackProject(
            "multi-default-root",
            ("Assets", "packs/assets.pck"),
            ("DLC1", "dlc/dlc1.pck"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");

        var options = new BakeOptions(
            Path.Combine(project, DescriptorFileName),
            _fixture.HostAssemblyPath,
            outputRoot: null,
            BakeMode.Debug,
            check: false,
            force: false);
        BakeResult result = BakePipeline.Run(options, new BakeDiagnostics());

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Equal(Path.Combine(project, "packs", "assets.pck"), result.Packs[0].PackPath);
        Assert.Equal(Path.Combine(project, "dlc", "dlc1.pck"), result.Packs[1].PackPath);
        Assert.True(File.Exists(result.Packs[0].PackPath));
        Assert.True(File.Exists(result.Packs[1].PackPath));
    }

    [Fact]
    public void CheckValidatesEveryPackAndWritesNothing()
    {
        string project = BakeProjectFixture.CreatePackProject(
            "multi-check",
            ("Assets", "assets.pck"),
            ("DLC1", "dlc1.pck"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");

        using var output = TempBakeDirectory.Create();
        BakeResult result = RunBake(project, output.RootPath, check: true);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.False(result.PackWritten);
        Assert.Equal(2, result.Packs.Count);
        Assert.All(result.Packs, pack => Assert.False(pack.PackWritten));
        Assert.False(File.Exists(Path.Combine(output.RootPath, "assets.pck")));
        Assert.False(File.Exists(Path.Combine(output.RootPath, "dlc1.pck")));
    }

    [Fact]
    public void FailedDlcLeavesBothPreviousPacksUnchanged()
    {
        string project = BakeProjectFixture.CreatePackProject(
            "multi-failure-preserves",
            ("Assets", "assets.pck"),
            ("DLC1", "dlc1.pck"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");

        using var output = TempBakeDirectory.Create();
        BakeResult first = RunBake(project, output.RootPath);
        Assert.Equal(BakeStatus.Success, first.Status);
        byte[] mainBefore = File.ReadAllBytes(first.Packs[0].PackPath);
        byte[] dlcBefore = File.ReadAllBytes(first.Packs[1].PackPath);

        BakeProjectFixture.WriteAssetUnder(
            project,
            "DLC1",
            "data/dlc.toml",
            "name = \n");

        BakeResult failed = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, failed.Status);
        Assert.False(failed.PackWritten);
        Assert.Equal(mainBefore, File.ReadAllBytes(Path.Combine(output.RootPath, "assets.pck")));
        Assert.Equal(dlcBefore, File.ReadAllBytes(Path.Combine(output.RootPath, "dlc1.pck")));
    }

    [Fact]
    public void TwoBakesOfTheSameMultiPackInputsAreStructurallyIdentical()
    {
        string project = BakeProjectFixture.CreatePackProject(
            "multi-deterministic",
            ("Assets", "assets.pck"),
            ("DLC1", "dlc1.pck"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");

        using var first = TempBakeDirectory.Create();
        using var second = TempBakeDirectory.Create();

        BakeResult firstResult = RunBake(project, first.RootPath);
        BakeResult secondResult = RunBake(project, second.RootPath);

        Assert.Equal(BakeStatus.Success, firstResult.Status);
        Assert.Equal(BakeStatus.Success, secondResult.Status);
        BakedPackAssertions.AssertStructurallyEqual(
            Path.Combine(first.RootPath, "assets.pck"),
            Path.Combine(second.RootPath, "assets.pck"));
        BakedPackAssertions.AssertStructurallyEqual(
            Path.Combine(first.RootPath, "dlc1.pck"),
            Path.Combine(second.RootPath, "dlc1.pck"));
    }

    [Fact]
    public void CliBakesEveryPackAndReportsThePackCountAndPaths()
    {
        string project = BakeProjectFixture.CreatePackProject(
            "multi-cli",
            ("Assets", "assets.pck"),
            ("DLC1", "dlc1.pck"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");

        using var output = TempBakeDirectory.Create();
        (int exitCode, string stdout, string stderr) = RunCli(
            "bake",
            "--project",
            Path.Combine(project, DescriptorFileName),
            "--host",
            _fixture.HostAssemblyPath,
            "--output-root",
            output.RootPath);

        Assert.Equal(CliApplication.SuccessExitCode, exitCode);
        Assert.Equal(string.Empty, stderr);
        Assert.Contains("2 pack(s)", stdout, StringComparison.Ordinal);
        Assert.Contains(
            $"'{Path.Combine(output.RootPath, "assets.pck")}'",
            stdout,
            StringComparison.Ordinal);
        Assert.Contains(
            $"'{Path.Combine(output.RootPath, "dlc1.pck")}'",
            stdout,
            StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(output.RootPath, "assets.pck")));
        Assert.True(File.Exists(Path.Combine(output.RootPath, "dlc1.pck")));
    }

    [Fact]
    public void CliValidateChecksEveryPackRoot()
    {
        string project = BakeProjectFixture.CreatePackProject(
            "multi-validate",
            ("Assets", "assets.pck"),
            ("DLC1", "dlc1.pck"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");

        (int exitCode, string stdout, string stderr) = RunCli(
            "validate",
            "--project",
            Path.Combine(project, DescriptorFileName));

        Assert.Equal(CliApplication.SuccessExitCode, exitCode);
        Assert.Equal(string.Empty, stderr);
        Assert.Contains("2 pack(s)", stdout, StringComparison.Ordinal);
        Assert.Contains("4 custom-data source(s)", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void CliRejectsTheRemovedOutputOptionAndPointsAtOutputRoot()
    {
        (int exitCode, _, string stderr) = RunCli(
            "bake",
            "--project",
            "unused.toml",
            "--host",
            "unused.dll",
            "--output",
            "assets.pck");

        Assert.Equal(CliApplication.UsageExitCode, exitCode);
        Assert.Contains("no longer accepts --output", stderr, StringComparison.Ordinal);
        Assert.Contains("--output-root", stderr, StringComparison.Ordinal);
        Assert.Contains("[[assets]]", stderr, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output, string Error) RunCli(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = CliApplication.Run(arguments, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private BakeResult RunBake(string project, string outputRoot, bool check = false)
    {
        var options = new BakeOptions(
            Path.Combine(project, DescriptorFileName),
            _fixture.HostAssemblyPath,
            outputRoot,
            BakeMode.Debug,
            check,
            force: false);
        return BakePipeline.Run(options, new BakeDiagnostics());
    }

    private static void WriteMainAssets(string project)
    {
        BakeProjectFixture.WriteAssetUnder(project, "Assets", "data/game.toml", GameToml("art/player"));
        BakeProjectFixture.WriteAssetUnder(project, "Assets", "roots/stats.toml", StatsToml);
        BakeProjectFixture.WriteAssetUnder(project, "Assets", "sprites/player.toml", SpriteToml);
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

    private static void WriteDlcConfig(string project, string texture)
        => BakeProjectFixture.WriteAssetUnder(
            project,
            "DLC1",
            "data/dlc.toml",
            $"""
            name = "dlc"
            health = 10
            must = "overlay"
            texture = "{texture}"

            [bork2d]
            type = "Bork2d.BakeFixture.GameConfig"
            """);

    private static string GameToml(string texture)
        => $"""
        name = "campaign"
        health = 120
        max_players = 4
        must = "present"
        tint = [1.0, 0.5, 0.25, 1.0]
        texture = "{texture}"

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
        texture = "art/player"
        uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]
        scale = 16.0
        pivot = [0.5, 0.5]
        topology = "triangles"
        indices = [0, 1, 2, 0, 2, 3]

        [bork2d]
        type = "Bork2d.Api.Assets.SpriteAsset"
        """;
}
