using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// The multi-pack bake contract: ordered packs, the main pack's strict local references, DLC -> main
/// references, DLC -> DLC rejection, duplicate canonical paths across packs, local overlay precedence,
/// output-root joining, and failure preservation across all packs.
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

        using var archive = Bork2d.Assets.Pack.PackArchive.Open(result.Packs[1].PackPath);
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
            type = "Bork2d.BakeFixture.GameConfig"
            """);
}
