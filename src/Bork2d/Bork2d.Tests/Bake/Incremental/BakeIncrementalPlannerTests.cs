using Bork2d.Bake;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Incremental;
using Bork2d.Tests.Bake.Baking;
using Bork2d.Tests.Bake.Processors;
using PolyType;
using Xunit;

namespace Bork2d.Tests.Bake.Incremental;

/// <summary>
/// The WP5 incremental planner: schema-3 manifest creation, the all-or-nothing skip, invalidation by
/// source/registration/processor/pack/output-root/mode changes, missing or externally modified
/// outputs, corrupt or missing state, <c>--force</c>, <c>--check</c>, failures, and <c>clean</c>.
/// </summary>
public sealed class BakeIncrementalPlannerTests
{
    private static readonly DateTime _pinnedTime = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    [Fact]
    public void FirstBakeWritesSchemaThreeAndSecondBakeIsUpToDate()
    {
        using var project = TestBakeProject.Create("incremental-up-to-date");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var output = TempBakeDirectory.Create();
        string packPath = output.FullPath("assets.pck");
        string manifestPath = BakeManifestPaths.ManifestPath(project.RootPath);

        BakeResult first = Run(project.Project, output.RootPath);

        Assert.Equal(BakeStatus.Success, first.Status);
        Assert.True(first.PackWritten);
        Assert.True(File.Exists(packPath));
        Assert.True(File.Exists(manifestPath));
        Assert.Contains("\"schema\": 3", File.ReadAllText(manifestPath), StringComparison.Ordinal);

        BakeIncrementalManifest manifest = ReadManifest(project.RootPath);
        string[] inputPaths = [.. manifest.Inputs.Select(input => input.Path)];
        Assert.Equal(
            new[]
            {
                "Assets/art/player.png",
                "Assets/art/player.png.toml",
                "Assets/data/game.toml",
                "Assets/roots/stats.toml",
                "Assets/sprites/player.toml",
            },
            inputPaths);
        Assert.Equal([.. inputPaths.Order(StringComparer.Ordinal)], inputPaths);
        Assert.Equal(new[] { packPath }, manifest.Outputs.Select(value => value.Path));
        Assert.Equal(first.AssetCount, manifest.AssetCount);
        Assert.Equal(first.TextureCount, manifest.TextureCount);
        Assert.Equal(first.ReferenceCount, manifest.ReferenceCount);

        // No descriptor/host state: only the manifest lives in the incremental directory.
        Assert.Equal([manifestPath], Directory.GetFiles(BakeManifestPaths.Directory(project.RootPath)));
        Assert.False(Directory.Exists(Path.Combine(project.RootPath, "obj", "bork2d", "bakehost")));
        Assert.False(File.Exists(Path.Combine(project.RootPath, "bork2d.project.toml")));

        byte[] packBytes = File.ReadAllBytes(packPath);
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        File.SetLastWriteTimeUtc(packPath, _pinnedTime);
        File.SetLastWriteTimeUtc(manifestPath, _pinnedTime);

        BakeResult second = Run(project.Project, output.RootPath);

        Assert.Equal(BakeStatus.UpToDate, second.Status);
        Assert.False(second.PackWritten);
        Assert.Equal(first.AssetCount, second.AssetCount);
        Assert.Equal(first.TextureCount, second.TextureCount);
        Assert.Equal(first.ReferenceCount, second.ReferenceCount);
        Assert.Empty(second.Entries);
        Assert.Contains(
            second.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.IncrementalSkipped
                && diagnostic.Severity == BakeSeverity.Info);

        // The skip neither rewrote a pack nor the manifest.
        Assert.Equal(packBytes, File.ReadAllBytes(packPath));
        Assert.Equal(manifestBytes, File.ReadAllBytes(manifestPath));
        Assert.Equal(_pinnedTime, File.GetLastWriteTimeUtc(packPath));
        Assert.Equal(_pinnedTime, File.GetLastWriteTimeUtc(manifestPath));
    }

    [Fact]
    public void FirstBakeRecordsTheSidecarItCreatesAndTheSecondBakeIsUpToDate()
    {
        var processor = new TestSidecarProcessor(
            "sidecar",
            "dat",
            _ => new TestSidecar { Value = 7 },
            (context, _) => context.Outputs.AddData(context.BaseAssetPath, new byte[] { 1, 2, 3 }));

        using var project = TestBakeProject.Create(
            "incremental-created-sidecar",
            registerFixtureTypes: false,
            processors: [processor]);
        project.WriteAsset("data/thing.dat", "processor source");
        using var output = TempBakeDirectory.Create();
        string packPath = output.FullPath("assets.pck");
        string manifestPath = BakeManifestPaths.ManifestPath(project.RootPath);

        BakeResult first = Run(project.Project, output.RootPath);

        Assert.Equal(BakeStatus.Success, first.Status);
        Assert.Equal(1, processor.CreateDefaultSidecarCount);
        Assert.True(File.Exists(project.FullPath("Assets/data/thing.dat.toml")));
        Assert.Contains(
            ReadManifest(project.RootPath).Inputs,
            input => input.Path == "Assets/data/thing.dat.toml");

        byte[] packBytes = File.ReadAllBytes(packPath);
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        File.SetLastWriteTimeUtc(packPath, _pinnedTime);
        File.SetLastWriteTimeUtc(manifestPath, _pinnedTime);

        BakeResult second = Run(project.Project, output.RootPath);

        Assert.Equal(BakeStatus.UpToDate, second.Status);
        Assert.Equal(1, processor.CreateDefaultSidecarCount);
        Assert.Equal(packBytes, File.ReadAllBytes(packPath));
        Assert.Equal(manifestBytes, File.ReadAllBytes(manifestPath));
        Assert.Equal(_pinnedTime, File.GetLastWriteTimeUtc(packPath));
        Assert.Equal(_pinnedTime, File.GetLastWriteTimeUtc(manifestPath));
    }

    [Fact]
    public void ChangedSourceRebakesAndRewritesTheManifest()
    {
        using var project = TestBakeProject.Create("incremental-source");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var output = TempBakeDirectory.Create();
        string packPath = output.FullPath("assets.pck");
        string manifestPath = BakeManifestPaths.ManifestPath(project.RootPath);

        BakeResult first = Run(project.Project, output.RootPath);
        Assert.Equal(BakeStatus.Success, first.Status);
        byte[] packBytes = File.ReadAllBytes(packPath);
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);

        project.WriteAsset(
            "data/game.toml",
            BakeProjectFixture.GameToml.Replace("health = 120", "health = 121", StringComparison.Ordinal));

        BakeResult second = Run(project.Project, output.RootPath);

        Assert.Equal(BakeStatus.Success, second.Status);
        Assert.True(second.PackWritten);
        Assert.NotEqual(packBytes, File.ReadAllBytes(packPath));
        Assert.NotEqual(manifestBytes, File.ReadAllBytes(manifestPath));
    }

    [Fact]
    public void ChangedRegistrationInvalidatesTheManifest()
    {
        using var project = TestBakeProject.Create("incremental-registration");
        project.WriteAsset("sprites/player.toml", BakeProjectFixture.SpriteToml);
        project.WriteAsset("art/player.png.toml", BakeProjectFixture.TextureSidecarToml);
        project.WriteTexture();
        using var output = TempBakeDirectory.Create();
        IReadOnlyList<(string Pack, string Root)> packs = [("assets.pck", "Assets")];

        BakeResult first = Run(project.Project, output.RootPath);
        Assert.Equal(BakeStatus.Success, first.Status);

        // The same sources under a builder that registers fewer data types must rebake: the
        // configuration hash includes the registered type set.
        BakeProject unregistered = BakeProjectFixture.CreateProject(
            project.RootPath,
            packs,
            registerFixtureTypes: false);

        BakeResult second = Run(unregistered, output.RootPath);

        Assert.Equal(BakeStatus.Success, second.Status);
        Assert.True(second.PackWritten);
        Assert.Equal(
            BakeConfigurationHash.Compute(unregistered),
            ReadManifest(project.RootPath).ConfigurationHash);

        Assert.Equal(BakeStatus.UpToDate, Run(unregistered, output.RootPath).Status);
    }

    [Fact]
    public void ChangedProcessorRegistrationInvalidatesTheManifest()
    {
        using var project = TestBakeProject.Create("incremental-processor");
        project.WriteAsset("sprites/player.toml", BakeProjectFixture.SpriteToml);
        project.WriteAsset("art/player.png.toml", BakeProjectFixture.TextureSidecarToml);
        project.WriteTexture();
        using var output = TempBakeDirectory.Create();
        IReadOnlyList<(string Pack, string Root)> packs = [("assets.pck", "Assets")];

        BakeResult first = Run(project.Project, output.RootPath);
        Assert.Equal(BakeStatus.Success, first.Status);

        BakeProject withProcessor = BakeProjectFixture.CreateProject(
            project.RootPath,
            packs,
            processors: [new TestProcessor("custom", "raw")]);

        BakeResult second = Run(withProcessor, output.RootPath);

        Assert.Equal(BakeStatus.Success, second.Status);
        Assert.True(second.PackWritten);
        Assert.Equal(
            BakeConfigurationHash.Compute(withProcessor),
            ReadManifest(project.RootPath).ConfigurationHash);
    }

    [Fact]
    public void ChangedProcessorExtensionOrPolicyChangesTheConfigurationHash()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        IReadOnlyList<(string Pack, string Root)> packs = [("assets.pck", "Assets")];

        BakeProject baseline = BakeProjectFixture.CreateProject(directory.RootPath, packs);
        BakeProject none = BakeProjectFixture.CreateProject(
            directory.RootPath,
            packs,
            processors: [new TestProcessor("custom", "dat")]);
        BakeProject noneUpper = BakeProjectFixture.CreateProject(
            directory.RootPath,
            packs,
            processors: [new TestProcessor("custom", ".DAT")]);
        BakeProject otherExtension = BakeProjectFixture.CreateProject(
            directory.RootPath,
            packs,
            processors: [new TestProcessor("custom", "datx")]);
        BakeProject required = BakeProjectFixture.CreateProject(
            directory.RootPath,
            packs,
            processors:
            [
                new TestProcessor(
                    "custom",
                    "dat",
                    policy: SidecarPolicy.Required,
                    sidecarShape: Shape<TestSidecar>()),
            ]);

        // Extension casing is normalized, so the same registration hashes the same.
        Assert.Equal(
            BakeConfigurationHash.Compute(none),
            BakeConfigurationHash.Compute(noneUpper));

        // A changed processor set, extension, or sidecar policy all invalidate the bake.
        Assert.NotEqual(
            BakeConfigurationHash.Compute(baseline),
            BakeConfigurationHash.Compute(none));
        Assert.NotEqual(
            BakeConfigurationHash.Compute(none),
            BakeConfigurationHash.Compute(otherExtension));
        Assert.NotEqual(
            BakeConfigurationHash.Compute(none),
            BakeConfigurationHash.Compute(required));
    }

    [Fact]
    public void ChangedPackPathForcesAFullBake()
    {
        using var project = TestBakeProject.Create("incremental-pack-path");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var output = TempBakeDirectory.Create();

        Assert.Equal(BakeStatus.Success, Run(project.Project, output.RootPath).Status);

        BakeProject otherPath = BakeProjectFixture.CreateProject(
            project.RootPath,
            [("content.pck", "Assets")]);
        BakeResult second = Run(otherPath, output.RootPath);

        Assert.Equal(BakeStatus.Success, second.Status);
        Assert.True(second.PackWritten);
        Assert.True(File.Exists(output.FullPath("content.pck")));
        Assert.Equal(
            new[] { output.FullPath("content.pck") },
            ReadManifest(project.RootPath).Outputs.Select(value => value.Path));
    }

    [Fact]
    public void ChangedPackRootForcesAFullBake()
    {
        using var project = TestBakeProject.Create("incremental-pack-root");
        project.WriteStandardAssets();
        project.WriteTexture();
        Directory.CreateDirectory(project.FullPath("DLC1"));
        using var output = TempBakeDirectory.Create();

        Assert.Equal(BakeStatus.Success, Run(project.Project, output.RootPath).Status);

        BakeProject otherRoot = BakeProjectFixture.CreateProject(
            project.RootPath,
            [("assets.pck", "DLC1")]);
        BakeResult second = Run(otherRoot, output.RootPath);

        Assert.Equal(BakeStatus.Success, second.Status);
        Assert.True(second.PackWritten);
        BakeIncrementalManifest manifest = ReadManifest(project.RootPath);
        Assert.Empty(manifest.Inputs);
        Assert.Equal(
            BakeConfigurationHash.Compute(otherRoot),
            manifest.ConfigurationHash);
    }

    [Fact]
    public void ChangedOutputRootForcesAFullBake()
    {
        using var project = TestBakeProject.Create("incremental-output-root");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var first = TempBakeDirectory.Create();
        using var second = TempBakeDirectory.Create();

        Assert.Equal(BakeStatus.Success, Run(project.Project, first.RootPath).Status);
        BakeResult rebaked = Run(project.Project, second.RootPath);

        Assert.Equal(BakeStatus.Success, rebaked.Status);
        Assert.True(rebaked.PackWritten);
        Assert.True(File.Exists(second.FullPath("assets.pck")));
        Assert.Equal(
            new[] { second.FullPath("assets.pck") },
            ReadManifest(project.RootPath).Outputs.Select(value => value.Path));
    }

    [Fact]
    public void ChangedModeForcesAFullBake()
    {
        using var project = TestBakeProject.Create("incremental-mode");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var output = TempBakeDirectory.Create();

        Assert.Equal(BakeStatus.Success, Run(project.Project, output.RootPath).Status);
        BakeResult release = Run(project.Project, output.RootPath, BakeMode.Release);

        Assert.Equal(BakeStatus.Success, release.Status);
        Assert.True(release.PackWritten);
        Assert.Equal("release", ReadManifest(project.RootPath).Mode);
    }

    [Fact]
    public void MissingOutputForcesAFullBake()
    {
        using var project = TestBakeProject.Create("incremental-missing-output");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var output = TempBakeDirectory.Create();
        string packPath = output.FullPath("assets.pck");

        Assert.Equal(BakeStatus.Success, Run(project.Project, output.RootPath).Status);
        File.Delete(packPath);

        BakeResult rebaked = Run(project.Project, output.RootPath);

        Assert.Equal(BakeStatus.Success, rebaked.Status);
        Assert.True(rebaked.PackWritten);
        Assert.True(File.Exists(packPath));
    }

    [Fact]
    public void ExternallyModifiedOutputForcesAFullBake()
    {
        using var project = TestBakeProject.Create("incremental-modified-output");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var output = TempBakeDirectory.Create();
        string packPath = output.FullPath("assets.pck");

        Assert.Equal(BakeStatus.Success, Run(project.Project, output.RootPath).Status);
        byte[] original = File.ReadAllBytes(packPath);
        File.WriteAllBytes(packPath, [0, 1, 2, 3]);

        BakeResult rebaked = Run(project.Project, output.RootPath);

        Assert.Equal(BakeStatus.Success, rebaked.Status);
        Assert.True(rebaked.PackWritten);
        Assert.Equal(original, File.ReadAllBytes(packPath));
    }

    [Fact]
    public void MissingManifestForcesAFullBake()
    {
        using var project = TestBakeProject.Create("incremental-missing-manifest");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var output = TempBakeDirectory.Create();
        string manifestPath = BakeManifestPaths.ManifestPath(project.RootPath);

        Assert.Equal(BakeStatus.Success, Run(project.Project, output.RootPath).Status);
        File.Delete(manifestPath);

        BakeResult rebaked = Run(project.Project, output.RootPath);

        Assert.Equal(BakeStatus.Success, rebaked.Status);
        Assert.True(rebaked.PackWritten);
        Assert.True(File.Exists(manifestPath));
    }

    [Fact]
    public void CorruptManifestForcesAFullBake()
    {
        using var project = TestBakeProject.Create("incremental-corrupt-manifest");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var output = TempBakeDirectory.Create();
        string manifestPath = BakeManifestPaths.ManifestPath(project.RootPath);

        Assert.Equal(BakeStatus.Success, Run(project.Project, output.RootPath).Status);
        File.WriteAllText(manifestPath, "{ corrupted");

        BakeResult rebaked = Run(project.Project, output.RootPath);

        Assert.Equal(BakeStatus.Success, rebaked.Status);
        Assert.True(rebaked.PackWritten);
        Assert.Contains("\"schema\": 3", File.ReadAllText(manifestPath), StringComparison.Ordinal);
    }

    [Fact]
    public void ForceBypassesTheSkip()
    {
        using var project = TestBakeProject.Create("incremental-force");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var output = TempBakeDirectory.Create();

        Assert.Equal(BakeStatus.Success, Run(project.Project, output.RootPath).Status);
        Assert.Equal(BakeStatus.UpToDate, Run(project.Project, output.RootPath).Status);

        BakeResult forced = Run(project.Project, output.RootPath, force: true);

        Assert.Equal(BakeStatus.Success, forced.Status);
        Assert.True(forced.PackWritten);
        Assert.Equal(BakeStatus.UpToDate, Run(project.Project, output.RootPath).Status);
    }

    [Fact]
    public void CheckNeverSkipsAndNeverWritesTheManifest()
    {
        using var project = TestBakeProject.Create("incremental-check");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var output = TempBakeDirectory.Create();
        string packPath = output.FullPath("assets.pck");
        string manifestPath = BakeManifestPaths.ManifestPath(project.RootPath);

        BakeResult checkedBeforeBake = Run(project.Project, output.RootPath, check: true);
        Assert.Equal(BakeStatus.Success, checkedBeforeBake.Status);
        Assert.False(checkedBeforeBake.PackWritten);
        Assert.False(File.Exists(manifestPath));

        Assert.Equal(BakeStatus.Success, Run(project.Project, output.RootPath).Status);
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        File.SetLastWriteTimeUtc(packPath, _pinnedTime);
        File.SetLastWriteTimeUtc(manifestPath, _pinnedTime);

        BakeResult checkedAfterBake = Run(project.Project, output.RootPath, check: true);

        Assert.Equal(BakeStatus.Success, checkedAfterBake.Status);
        Assert.False(checkedAfterBake.PackWritten);
        Assert.Equal(manifestBytes, File.ReadAllBytes(manifestPath));
        Assert.Equal(_pinnedTime, File.GetLastWriteTimeUtc(packPath));
        Assert.Equal(_pinnedTime, File.GetLastWriteTimeUtc(manifestPath));
    }

    [Fact]
    public void FailedBakeLeavesPacksAndManifestUnchanged()
    {
        using var project = TestBakeProject.Create("incremental-failure");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var output = TempBakeDirectory.Create();
        string packPath = output.FullPath("assets.pck");
        string manifestPath = BakeManifestPaths.ManifestPath(project.RootPath);

        Assert.Equal(BakeStatus.Success, Run(project.Project, output.RootPath).Status);
        byte[] packBytes = File.ReadAllBytes(packPath);
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);

        project.WriteAsset("data/game.toml", "name = \n");

        BakeResult failed = Run(project.Project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, failed.Status);
        Assert.False(failed.PackWritten);
        Assert.Contains(
            failed.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.TomlSyntax);
        Assert.Equal(packBytes, File.ReadAllBytes(packPath));
        Assert.Equal(manifestBytes, File.ReadAllBytes(manifestPath));
    }

    [Fact]
    public void CleanRemovesPacksAndTheIncrementalState()
    {
        using var project = TestBakeProject.Create("incremental-clean");
        project.WriteStandardAssets();
        project.WriteTexture();
        using var output = TempBakeDirectory.Create();
        string packPath = output.FullPath("assets.pck");
        BakeApplication application = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);

        int bakeExitCode = application.Run(
            ["bake", "--project-root", project.RootPath, "--output-root", output.RootPath],
            new StringWriter(),
            new StringWriter(),
            "test-assets");
        Assert.Equal(0, bakeExitCode);
        Assert.True(File.Exists(packPath));
        Assert.True(Directory.Exists(BakeManifestPaths.Directory(project.RootPath)));

        var cleanOutput = new StringWriter();
        int cleanExitCode = application.Run(
            ["clean", "--project-root", project.RootPath, "--output-root", output.RootPath],
            cleanOutput,
            new StringWriter(),
            "test-assets");

        Assert.Equal(0, cleanExitCode);
        Assert.Contains("incremental", cleanOutput.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(packPath));
        Assert.False(Directory.Exists(BakeManifestPaths.Directory(project.RootPath)));
    }

    [Fact]
    public void MultiPackManifestKeepsOutputsInConfiguredPackOrder()
    {
        using var project = TestBakeProject.Create(
            "incremental-multi-pack",
            [("assets.pck", "Assets"), ("dlc1.pck", "DLC1")]);
        project.WriteAsset("roots/stats.toml", BakeProjectFixture.StatsToml);
        project.WriteAssetUnder("DLC1", "roots/stats.toml", BakeProjectFixture.StatsToml);
        using var output = TempBakeDirectory.Create();

        BakeResult first = Run(project.Project, output.RootPath);

        Assert.Equal(BakeStatus.Success, first.Status);
        Assert.True(first.PackWritten);
        Assert.Equal(
            new[]
            {
                output.FullPath("assets.pck"),
                output.FullPath("dlc1.pck"),
            },
            ReadManifest(project.RootPath).Outputs.Select(value => value.Path));
        Assert.Equal(
            new[]
            {
                "Assets/roots/stats.toml",
                "DLC1/roots/stats.toml",
            },
            ReadManifest(project.RootPath).Inputs.Select(value => value.Path));
        Assert.Equal(BakeStatus.UpToDate, Run(project.Project, output.RootPath).Status);
    }

    private static BakeResult Run(
        BakeProject project,
        string outputRoot,
        BakeMode mode = BakeMode.Debug,
        bool check = false,
        bool force = false)
    {
        var diagnostics = new BakeDiagnostics();
        return BakeIncrementalPlanner.Run(
            project,
            new BakeOptions(outputRoot, mode, check, force),
            diagnostics);
    }

    private static BakeIncrementalManifest ReadManifest(string projectRoot)
        => Assert.IsType<BakeIncrementalManifest>(
            BakeManifestStore.TryRead(BakeManifestPaths.ManifestPath(projectRoot)));

    private static ITypeShape Shape<T>()
        where T : IShapeable<T>
        => T.GetTypeShape();
}
