using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Engine.Assets.Pack;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// The multi-pack bake contract: ordered packs, the main pack's strict local references, DLC -> main
/// references, DLC -> DLC rejection, duplicate canonical paths across packs, local overlay precedence,
/// output-root joining, failure preservation across all packs, and the staged transactional publish
/// that releases each pack builder as soon as its pack is staged.
/// </summary>
public sealed class MultiPackBakeTests
{
    [Fact]
    public void BakesMainAndDlcPacksAndAllowsADlcReferenceToTheMainTexture()
    {
        using var project = CreateMultiPack("multi-basic");
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

        using var archive = Bork2d.Engine.Assets.Pack.PackArchive.Open(result.Packs[1].PackPath);
        Assert.Equal(
            BakeProjectFixture.DlcGamePath,
            Assert.Single(archive.Entries).Path);
    }

    [Fact]
    public void MainPackReferenceToADlcOnlyPathIsRejected()
    {
        using var project = CreateMultiPack("multi-main-strict");
        // The main pack's sprite references a texture that exists only in the DLC pack.
        project.WriteAssetUnder("Assets", "roots/stats.toml", BakeProjectFixture.StatsToml);
        project.WriteAssetUnder("Assets", "sprites/player.toml", BakeProjectFixture.SpriteToml);
        project.WriteAssetBytesUnder("DLC1", "art/player.png", TextureImageFactory.RgbaPng());
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
        using var project = CreateMultiPack(
            "multi-dlc-to-dlc",
            ("assets.pck", "Assets"),
            ("dlc1.pck", "DLC1"),
            ("dlc2.pck", "DLC2"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/dlc2");
        project.WriteAssetBytesUnder("DLC2", "art/dlc2.png", TextureImageFactory.RgbaPng());

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
        using var project = CreateMultiPack(
            "multi-dlc-main-wins",
            ("assets.pck", "Assets"),
            ("dlc1.pck", "DLC1"),
            ("dlc2.pck", "DLC2"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");
        // DLC2 declares the same canonical texture path; DLC1 must still resolve it through main.
        project.WriteAssetBytesUnder("DLC2", "art/player.png", TextureImageFactory.GrayscalePng());

        using var output = TempBakeDirectory.Create();
        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == BakeSeverity.Error);
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
        using var project = CreateMultiPack("multi-duplicate-across");
        WriteMainAssets(project);
        // The DLC overlays data/game with its own asset at the same canonical path.
        project.WriteAssetUnder("DLC1", "data/game.toml", BakeProjectFixture.GameToml);
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
        using var project = CreateMultiPack("multi-duplicate-within");
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");
        // The DLC declares both a texture and custom data at art/player: a within-pack collision.
        project.WriteAssetBytesUnder("DLC1", "art/player.png", TextureImageFactory.RgbaPng());
        project.WriteAssetUnder(
            "DLC1",
            "art/player.toml",
            """
            attack = 1
            defense = 2

            [bork2d]
            type = "Bork2d.Tests.Bake.Baking.StatsRoot"
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
        using var project = CreateMultiPack("multi-local-wins");
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");
        // The DLC's local art/player is custom data, not a TextureDescriptor. Local resolution wins,
        // so the DLC's texture reference must fail against the local (wrong) type even though main
        // has a valid texture descriptor at the same canonical path.
        project.WriteAssetUnder(
            "DLC1",
            "art/player.toml",
            """
            attack = 3
            defense = 4

            [bork2d]
            type = "Bork2d.Tests.Bake.Baking.StatsRoot"
            """);

        using var output = TempBakeDirectory.Create();
        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == BakeDiagnosticCodes.ReferenceWrongType);
        Assert.Contains("Bork2d.Engine.Assets.TextureDescriptor", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(output.RootPath, "assets.pck")));
    }

    [Fact]
    public void OutputRootJoinsPackPathsAndPreservesSubdirectories()
    {
        using var project = CreateMultiPack(
            "multi-output-layout",
            ("packs/assets.pck", "Assets"),
            ("dlc/dlc1.pck", "DLC1"));
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
    public void CheckValidatesEveryPackAndWritesNothing()
    {
        using var project = CreateMultiPack("multi-check");
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
        Assert.Empty(Directory.GetFiles(output.RootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void SuccessfulMultiPackBakePublishesEveryTargetAndLeavesNoStagingOrBackupFiles()
    {
        using var project = CreateMultiPack("multi-no-leftovers");
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");

        using var output = TempBakeDirectory.Create();
        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);

        string[] files = [.. Directory
            .GetFiles(output.RootPath, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.Ordinal)];
        Assert.Equal(
            new[]
            {
                Path.Combine(output.RootPath, "assets.pck"),
                Path.Combine(output.RootPath, "dlc1.pck"),
            },
            files);
        Assert.All(
            files,
            file => Assert.False(
                file.EndsWith(".tmp", StringComparison.Ordinal)
                    || file.EndsWith(".bak", StringComparison.Ordinal)));
    }

    [Fact]
    public void StagingFailureLeavesThePreviousPackUnchangedAndWritesNoTarget()
    {
        using var project = CreateMultiPack(
            "multi-staging-failure",
            ("assets.pck", "Assets"),
            ("dlc/dlc1.pck", "DLC1"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");

        using var output = TempBakeDirectory.Create();
        string mainPath = Path.Combine(output.RootPath, "assets.pck");
        BakedPackAssertions.WriteValidPack(mainPath);
        byte[] before = File.ReadAllBytes(mainPath);

        // The second pack's output directory is occupied by a file, so creating the staging directory
        // fails after the main pack has already been staged.
        string blockedDirectory = Path.Combine(output.RootPath, "dlc");
        File.WriteAllText(blockedDirectory, "not a directory");

        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.False(result.PackWritten);
        Assert.Equal(BakeFailureStage.Pack, result.FailureStage);
        Assert.Equal(Path.Combine(output.RootPath, "dlc", "dlc1.pck"), result.PackPath);
        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.PackWriteFailed, diagnostic.Code);
        Assert.Equal(before, File.ReadAllBytes(mainPath));
        Assert.False(File.Exists(Path.Combine(output.RootPath, "dlc", "dlc1.pck")));
        Assert.Equal("not a directory", File.ReadAllText(blockedDirectory));
        Assert.Empty(Directory.GetFiles(output.RootPath, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(output.RootPath, "*.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public void CommitFailureRestoresTheFirstTargetAndLeavesLaterTargetsUntouched()
    {
        using var project = CreateMultiPack(
            "multi-commit-failure",
            ("assets.pck", "Assets"),
            ("dlc1.pck", "DLC1"),
            ("dlc2.pck", "DLC2"));
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");

        using var output = TempBakeDirectory.Create();
        BakeResult first = RunBake(project, output.RootPath);
        Assert.Equal(BakeStatus.Success, first.Status);

        string mainPath = Path.Combine(output.RootPath, "assets.pck");
        string blockedPath = Path.Combine(output.RootPath, "dlc1.pck");
        string laterPath = Path.Combine(output.RootPath, "dlc2.pck");
        byte[] mainBefore = File.ReadAllBytes(mainPath);
        byte[] laterBefore = File.ReadAllBytes(laterPath);

        // Change the main pack so its new bytes would differ, then make the second target a directory
        // so the commit fails after the main target has already been replaced.
        project.WriteAsset(
            "data/game.toml",
            BakeProjectFixture.GameToml.Replace(
                "health = 120",
                "health = 121",
                StringComparison.Ordinal));
        File.Delete(blockedPath);
        Directory.CreateDirectory(blockedPath);

        BakeResult failed = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, failed.Status);
        Assert.False(failed.PackWritten);
        Assert.Equal(BakeFailureStage.Pack, failed.FailureStage);
        Assert.Equal(blockedPath, failed.PackPath);
        BakeDiagnostic diagnostic = Assert.Single(failed.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.PackWriteFailed, diagnostic.Code);
        Assert.Equal(blockedPath, diagnostic.FilePath);
        Assert.Equal(mainBefore, File.ReadAllBytes(mainPath));
        Assert.Equal(laterBefore, File.ReadAllBytes(laterPath));
        Assert.True(Directory.Exists(blockedPath));
        Assert.Empty(Directory.GetFiles(output.RootPath, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(output.RootPath, "*.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public void StagingReleasesEveryPackBuilderAndKeepsOnlyTheStagedCount()
    {
        using var project = CreateMultiPack("multi-staging-memory");
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");

        using var output = TempBakeDirectory.Create();
        var probe = new RecordingStagingProbe(output.RootPath);

        BakeResult result = BakePipeline.Run(
            project.Project,
            new BakeOptions(Path.GetFullPath(output.RootPath), BakeMode.Debug, check: false, force: false),
            new BakeDiagnostics(),
            probe);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Equal(new[] { 0, 1 }, probe.PackIndexes);
        Assert.Equal(new[] { 1, 2 }, probe.StagedPackCounts);
        Assert.Equal(new[] { 1, 2 }, probe.StagingFileCounts);
        Assert.Equal(2, probe.RetainedBuilders.Count);
        Assert.All(probe.RetainedBuilders, builder => Assert.Null(builder));
    }

    [Fact]
    public void FailedDlcLeavesBothPreviousPacksUnchanged()
    {
        using var project = CreateMultiPack("multi-failure-preserves");
        WriteMainAssets(project);
        WriteDlcConfig(project, texture: "art/player");

        using var output = TempBakeDirectory.Create();
        BakeResult first = RunBake(project, output.RootPath);
        Assert.Equal(BakeStatus.Success, first.Status);
        byte[] mainBefore = File.ReadAllBytes(first.Packs[0].PackPath);
        byte[] dlcBefore = File.ReadAllBytes(first.Packs[1].PackPath);

        project.WriteAssetUnder("DLC1", "data/dlc.toml", "name = \n");

        BakeResult failed = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, failed.Status);
        Assert.False(failed.PackWritten);
        Assert.Equal(mainBefore, File.ReadAllBytes(Path.Combine(output.RootPath, "assets.pck")));
        Assert.Equal(dlcBefore, File.ReadAllBytes(Path.Combine(output.RootPath, "dlc1.pck")));
    }

    [Fact]
    public void TwoBakesOfTheSameMultiPackInputsAreStructurallyIdentical()
    {
        using var project = CreateMultiPack("multi-deterministic");
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

    private static TestBakeProject CreateMultiPack(string name, params (string Pack, string Root)[] packs)
        => TestBakeProject.Create(
            name,
            packs.Length == 0
                ? [("assets.pck", "Assets"), ("dlc1.pck", "DLC1")]
                : packs);

    private static BakeResult RunBake(TestBakeProject project, string outputRoot, bool check = false)
        => BakePipeline.Run(
            project.Project,
            new BakeOptions(Path.GetFullPath(outputRoot), BakeMode.Debug, check, force: false),
            new BakeDiagnostics());

    private static void WriteMainAssets(TestBakeProject project)
    {
        project.WriteAssetUnder("Assets", "data/game.toml", BakeProjectFixture.GameToml);
        project.WriteAssetUnder("Assets", "roots/stats.toml", BakeProjectFixture.StatsToml);
        project.WriteAssetUnder("Assets", "sprites/player.toml", BakeProjectFixture.SpriteToml);
        project.WriteAssetUnder("Assets", "art/player.png.toml", BakeProjectFixture.TextureSidecarToml);
        project.WriteAssetBytesUnder("Assets", "art/player.png", TextureImageFactory.RgbaPng());
    }

    private static void WriteDlcConfig(TestBakeProject project, string texture)
        => project.WriteAssetUnder(
            "DLC1",
            "data/dlc.toml",
            $"""
            name = "dlc"
            health = 10
            must = "overlay"
            texture = "{texture}"

            [bork2d]
            type = "Bork2d.Tests.Bake.Baking.GameConfig"
            """);

    /// <summary>
    /// Records the pipeline's post-staging state: the staged count, the staging files actually present
    /// on disk, and the builder reference the pipeline still retains.
    /// </summary>
    private sealed class RecordingStagingProbe : IPackStagingProbe
    {
        private readonly string _outputRoot;

        internal RecordingStagingProbe(string outputRoot) => _outputRoot = outputRoot;

        internal List<int> PackIndexes { get; } = [];

        internal List<int> StagedPackCounts { get; } = [];

        internal List<int> StagingFileCounts { get; } = [];

        internal List<PackBuilder?> RetainedBuilders { get; } = [];

        public void OnPackStaged(int packIndex, int stagedPackCount, PackBuilder? retainedBuilder)
        {
            PackIndexes.Add(packIndex);
            StagedPackCounts.Add(stagedPackCount);
            StagingFileCounts.Add(
                Directory.GetFiles(_outputRoot, "*.tmp", SearchOption.AllDirectories).Length);
            RetainedBuilders.Add(retainedBuilder);
        }
    }
}
