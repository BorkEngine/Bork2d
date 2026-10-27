using Bork2d.Api.Assets;
using Bork2d.Assets.Pack;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Processors;
using Bork2d.Bake.Sources;
using Bork2d.Tests.Assets.Service;
using Bork2d.Tests.Bake.Baking;
using Xunit;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// Processor output behavior: any number of shaped/raw outputs, engine-side serialization, path
/// validation, duplicate detection, exception/error discarding, warnings, zero outputs, and processor
/// instance reuse.
/// </summary>
public sealed class BakeProcessorOutputTests
{
    [Fact]
    public void NoneProcessorCanEmitMultipleShapedAndRawOutputs()
    {
        using var project = TestBakeProject.Create(
            "outputs-multiple",
            processors:
            [
                new TestProcessor(
                    "test-stats",
                    "stats",
                    context =>
                    {
                        context.Outputs.AddAsset(
                            context.BaseAssetPath,
                            new Bork2d.BakeFixture.StatsRoot(11, 13));
                        context.Outputs.AddAsset(
                            context.BaseAssetPath + "-alt",
                            new Bork2d.BakeFixture.StatsRoot(1, 2));
                        context.Outputs.AddData(
                            context.BaseAssetPath + "-raw",
                            "hello"u8);
                    }),
            ]);
        project.WriteAssetBytes("data/archer.stats", [1]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.Equal(3, outputs.Count);

        BakeProcessorOutput asset = Assert.Single(outputs, output => output.Path == "data/archer");
        Assert.False(asset.IsRaw);
        Assert.Equal(typeof(Bork2d.BakeFixture.StatsRoot).FullName, asset.TypeName);
        Assert.NotNull(asset.Write);

        BakeProcessorOutput alt = Assert.Single(outputs, output => output.Path == "data/archer-alt");
        Assert.False(alt.IsRaw);

        BakeProcessorOutput raw = Assert.Single(outputs, output => output.Path == "data/archer-raw");
        Assert.True(raw.IsRaw);
        Assert.Equal("hello"u8.ToArray(), raw.RawPayload);
    }

    [Fact]
    public async Task AddAssetIsSerializedByTheEngineAndLoadableThroughAssetService()
    {
        using var project = TestBakeProject.Create(
            "outputs-load",
            processors:
            [
                new TestProcessor(
                    "test-stats",
                    "stats",
                    context =>
                    {
                        context.Outputs.AddAsset(
                            context.BaseAssetPath,
                            new Bork2d.BakeFixture.StatsRoot(21, 34));
                        context.Outputs.AddData(context.BaseAssetPath + "-raw", [9, 8, 7]);
                    }),
            ]);
        project.WriteAssetBytes("data/archer.stats", [1]);
        using var directory = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, directory.RootPath);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == BakeSeverity.Error);
        Assert.Equal(1, result.AssetCount);
        Assert.Equal(0, result.TextureCount);
        Assert.Contains(
            result.Entries,
            entry => entry.Path == "data/archer" && entry.Kind == BakedAssetEntryKind.ProcessorAsset);
        Assert.Contains(
            result.Entries,
            entry => entry.Path == "data/archer-raw" && entry.Kind == BakedAssetEntryKind.ProcessorData);

        using var host = new AssetServiceHost(directory.FullPath("assets.pck"));
        host.Start();

        Bork2d.BakeFixture.StatsRoot value =
            await host.Service.LoadAsset<Bork2d.BakeFixture.StatsRoot>("data/archer");
        Assert.Equal(new Bork2d.BakeFixture.StatsRoot(21, 34), value);
        Assert.Equal(new byte[] { 9, 8, 7 }, await host.Service.LoadData("data/archer-raw"));
    }

    [Fact]
    public void InvalidOutputPathIsBK7004AndDiscardsTheOutput()
    {
        using var project = TestBakeProject.Create(
            "outputs-invalid-path",
            processors:
            [
                new TestProcessor(
                    "test-stats",
                    "stats",
                    context => context.Outputs.AddAsset(
                        "other/path",
                        new Bork2d.BakeFixture.StatsRoot(1, 2))),
            ]);
        project.WriteAssetBytes("data/archer.stats", [1]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Empty(outputs);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorOutputPathInvalid, diagnostic.Code);
        Assert.Contains("test-stats", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("data/archer-")]
    [InlineData("data/archer-RAW")]
    [InlineData("data/archer-bad/suffix")]
    public void NonBaseDerivedOrNonCanonicalPathsAreBK7004(string outputPath)
    {
        using var project = TestBakeProject.Create(
            "outputs-bad-suffix",
            processors:
            [
                new TestProcessor(
                    "test-stats",
                    "stats",
                    context => context.Outputs.AddData(outputPath, [1])),
            ]);
        project.WriteAssetBytes("data/archer.stats", [1]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Empty(outputs);
        Assert.Contains(
            diagnostics.GetSorted(),
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorOutputPathInvalid);
    }

    [Fact]
    public void DuplicateOutputFromOneSourceIsBK7005()
    {
        using var project = TestBakeProject.Create(
            "outputs-duplicate",
            processors:
            [
                new TestProcessor(
                    "test-stats",
                    "stats",
                    context =>
                    {
                        context.Outputs.AddAsset(
                            context.BaseAssetPath,
                            new Bork2d.BakeFixture.StatsRoot(1, 2));
                        context.Outputs.AddAsset(
                            context.BaseAssetPath,
                            new Bork2d.BakeFixture.StatsRoot(3, 4));
                    }),
            ]);
        project.WriteAssetBytes("data/archer.stats", [1]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Empty(outputs);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorOutputDuplicate, diagnostic.Code);
        Assert.Equal("data/archer", diagnostic.AssetPath);
    }

    [Fact]
    public void ProcessorExceptionIsBK7003AndOtherSourcesStillRun()
    {
        int processed = 0;
        using var project = TestBakeProject.Create(
            "outputs-exception",
            processors:
            [
                new TestProcessor(
                    "test-stats",
                    "stats",
                    context =>
                    {
                        processed++;
                        if (context.RelativePath.Contains("bad", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("boom");
                        }

                        context.Outputs.AddAsset(
                            context.BaseAssetPath,
                            new Bork2d.BakeFixture.StatsRoot(1, 2));
                    }),
            ]);
        project.WriteAssetBytes("data/archer.stats", [1]);
        project.WriteAssetBytes("data/bad.stats", [2]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        BakeDiagnostic diagnostic = Assert.Single(
            diagnostics.GetSorted(),
            candidate => candidate.Code == BakeDiagnosticCodes.ProcessorFailed);
        Assert.Contains("test-stats", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("data/bad.stats", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(2, processed);
        BakeProcessorOutput output = Assert.Single(outputs);
        Assert.Equal("data/archer", output.Path);
    }

    [Fact]
    public void ProcessorReportedErrorDiscardsItsOutputs()
    {
        using var project = TestBakeProject.Create(
            "outputs-error",
            processors:
            [
                new TestProcessor(
                    "test-stats",
                    "stats",
                    context =>
                    {
                        context.Outputs.AddAsset(
                            context.BaseAssetPath,
                            new Bork2d.BakeFixture.StatsRoot(1, 2));
                        context.Diagnostics.Error("BK7101", "the source is invalid");
                    }),
            ]);
        project.WriteAssetBytes("data/archer.stats", [1]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Empty(outputs);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal("BK7101", diagnostic.Code);
        Assert.Equal(BakeSeverity.Error, diagnostic.Severity);
        Assert.StartsWith("test-stats: ", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("Assets/data/archer.stats", diagnostic.FilePath);
    }

    [Fact]
    public void ProcessorWarningKeepsItsOutputs()
    {
        using var project = TestBakeProject.Create(
            "outputs-warning",
            processors:
            [
                new TestProcessor(
                    "test-stats",
                    "stats",
                    context =>
                    {
                        context.Outputs.AddAsset(
                            context.BaseAssetPath,
                            new Bork2d.BakeFixture.StatsRoot(1, 2));
                        context.Diagnostics.Warning("BK7102", "just a note");
                    }),
            ]);
        project.WriteAssetBytes("data/archer.stats", [1]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        BakeProcessorOutput output = Assert.Single(outputs);
        Assert.Equal("data/archer", output.Path);
        BakeDiagnostic warning = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeSeverity.Warning, warning.Severity);
        Assert.Equal("BK7102", warning.Code);
    }

    [Fact]
    public void AProcessorMayEmitZeroOutputs()
    {
        using var project = TestBakeProject.Create(
            "outputs-zero",
            processors: [new TestProcessor("test-stats", "stats")]);
        project.WriteAssetBytes("data/archer.stats", [1]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Empty(outputs);
        Assert.Empty(diagnostics.GetSorted());
    }

    [Fact]
    public void AProcessorInstanceIsReusedAcrossSources()
    {
        var processor = new TestProcessor(
            "test-stats",
            "stats",
            context => context.Outputs.AddAsset(
                context.BaseAssetPath,
                new Bork2d.BakeFixture.StatsRoot(1, 2)));
        using var project = TestBakeProject.Create("outputs-reuse", processors: [processor]);
        project.WriteAssetBytes("data/archer.stats", [1]);
        project.WriteAssetBytes("data/knight.stats", [2]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Equal(2, processor.ProcessCount);
        Assert.Equal(2, outputs.Count);
    }

    [Fact]
    public void RequiredProcessorProducesOutputsFromItsBoundSidecar()
    {
        var processor = new TestSidecarProcessor(
            "test-unit",
            "unit",
            _ => new TestSidecar { Value = 7 },
            (context, sidecar) => context.Outputs.AddAsset(
                context.BaseAssetPath,
                new Bork2d.BakeFixture.StatsRoot(sidecar.Value, sidecar.Value + 1)));
        using var project = TestBakeProject.Create("outputs-sidecar", processors: [processor]);
        project.WriteAssetBytes("data/archer.unit", [1]);
        project.WriteAsset("data/archer.unit.toml", "value = 12\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        BakeProcessorOutput output = Assert.Single(outputs);
        Assert.Equal("data/archer", output.Path);
        Assert.Equal(typeof(Bork2d.BakeFixture.StatsRoot).FullName, output.TypeName);
    }

    [Fact]
    public void ACustomPngProcessorRegisteredAfterDefaultsOverridesTheBuiltInOne()
    {
        var processor = new TestSidecarProcessor(
            "test-png",
            "png",
            _ => new TestSidecar { Value = 5 },
            (context, sidecar) => context.Outputs.AddAsset(
                context.BaseAssetPath,
                new Bork2d.BakeFixture.StatsRoot(sidecar.Value, 0)));
        using var directory = TempBakeDirectory.Create();
        string sourceDirectory = directory.CreateDirectory("Assets/art");
        File.WriteAllBytes(Path.Combine(sourceDirectory, "player.png"), [1, 2, 3]);
        using var output = TempBakeDirectory.Create();

        var diagnostics = new BakeDiagnostics();
        BakeProject? project = BakeProjectFixture.CreateBuilder(
                directory.RootPath,
                [("assets.pck", "Assets")],
                registerFixtureTypes: false,
                processors: [processor])
            .CreateProject(diagnostics);
        Assert.NotNull(project);
        BakeDiagnostic overrideInfo = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorExtensionOverride, overrideInfo.Code);
        Assert.Equal(BakeSeverity.Info, overrideInfo.Severity);
        Assert.Contains("texture", overrideInfo.Message, StringComparison.Ordinal);
        Assert.Contains("test-png", overrideInfo.Message, StringComparison.Ordinal);

        BakeResult result = BakePipeline.Run(
            project,
            new BakeOptions(output.RootPath, BakeMode.Debug, check: false, force: false),
            new BakeDiagnostics());

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Equal(1, result.AssetCount);
        Assert.Equal(0, result.TextureCount);
        Assert.Contains(
            result.Entries,
            entry => entry.Path == "art/player" && entry.Kind == BakedAssetEntryKind.ProcessorAsset);
        Assert.DoesNotContain(
            result.Entries,
            entry => entry.TypeName == typeof(TextureDescriptor).FullName);
    }

    private static IReadOnlyList<BakeProcessorOutput> Run(
        TestBakeProject project,
        BakeDiagnostics diagnostics)
    {
        BakeDiscoveredSources discovered = BakeSourceDiscovery.Discover(project.Project, diagnostics);
        return BakeProcessorRunner.Run(project.Project, discovered.ProcessorSources, diagnostics);
    }

    private static BakeResult RunBake(TestBakeProject project, string outputRoot)
        => BakePipeline.Run(
            project.Project,
            new BakeOptions(Path.GetFullPath(outputRoot), BakeMode.Debug, check: false, force: false),
            new BakeDiagnostics());
}
