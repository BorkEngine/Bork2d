using Bork2d.Bake;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Processors;
using Bork2d.Bake.Sources;
using Bork2d.Tests.Bake.Baking;
using Xunit;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// The single processor contract: a sidecar-free <see cref="IAssetProcessor"/> and an
/// <see cref="AssetProcessor{TSidecar}"/> run through the same public context, output sink, and
/// diagnostic sink with the same output and diagnostic behavior.
/// </summary>
public sealed class BakeProcessorContractTests
{
    [Fact]
    public void NoneAndRequiredProcessorsShareThePublicContextAndSinkPath()
    {
        AssetProcessorContext? noneContext = null;
        AssetProcessorContext? requiredContext = null;
        var none = new TestProcessor(
            "shared-contract",
            "none",
            context =>
            {
                noneContext = context;
                context.Outputs.AddAsset(
                    context.BaseAssetPath,
                    new Bork2d.Tests.Bake.Baking.StatsRoot(7, 0));
                context.Outputs.AddData(context.BaseAssetPath + "-raw", "payload"u8);
                context.Diagnostics.Warning("BK7201", "shared contract");
            });
        var required = new TestSidecarProcessor(
            "shared-contract",
            "req",
            _ => new TestSidecar { Value = 7 },
            (context, sidecar) =>
            {
                requiredContext = context;
                context.Outputs.AddAsset(
                    context.BaseAssetPath,
                    new Bork2d.Tests.Bake.Baking.StatsRoot(sidecar.Value, 0));
                context.Outputs.AddData(context.BaseAssetPath + "-raw", "payload"u8);
                context.Diagnostics.Warning("BK7201", "shared contract");
            });

        using var project = TestBakeProject.Create(
            "processor-contract",
            processors: [none, required]);
        project.WriteAssetBytes("data/shared.none", [1]);
        project.WriteAssetBytes("data/shared.req", [2]);
        project.WriteAsset("data/shared.req.toml", "value = 7\n");

        // The registry keeps the public processors: both registrations execute the same contract.
        Assert.Same(none, project.Project.Processors[1].Processor);
        Assert.Same(required, project.Project.Processors[2].Processor);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.NotNull(noneContext);
        Assert.NotNull(requiredContext);
        Assert.IsType<BakeOutputSink>(noneContext!.Outputs);
        Assert.IsType<BakeProcessorDiagnosticSink>(noneContext.Diagnostics);
        Assert.IsType<BakeOutputSink>(requiredContext!.Outputs);
        Assert.IsType<BakeProcessorDiagnosticSink>(requiredContext.Diagnostics);

        BakeProcessorOutput[] noneOutputs = [.. outputs.Where(
            output => output.SourcePath == "Assets/data/shared.none")];
        BakeProcessorOutput[] requiredOutputs = [.. outputs.Where(
            output => output.SourcePath == "Assets/data/shared.req")];

        Assert.Equal(Describe(noneOutputs), Describe(requiredOutputs));
        Assert.Equal(noneOutputs[1].RawPayload, requiredOutputs[1].RawPayload);

        BakeDiagnostic noneDiagnostic = Assert.Single(
            diagnostics.GetSorted(),
            diagnostic => diagnostic.FilePath == "Assets/data/shared.none");
        BakeDiagnostic requiredDiagnostic = Assert.Single(
            diagnostics.GetSorted(),
            diagnostic => diagnostic.FilePath == "Assets/data/shared.req");
        Assert.Equal(noneDiagnostic.Severity, requiredDiagnostic.Severity);
        Assert.Equal(noneDiagnostic.Code, requiredDiagnostic.Code);
        Assert.Equal(noneDiagnostic.Message, requiredDiagnostic.Message);
        Assert.Equal(noneDiagnostic.AssetPath, requiredDiagnostic.AssetPath);
    }

    private static IReadOnlyList<string> Describe(IReadOnlyList<BakeProcessorOutput> outputs)
        => [.. outputs.Select(output => string.Join(
            '|',
            output.Path,
            output.IsRaw,
            output.TypeName ?? string.Empty,
            output.ProcessorName))];

    private static IReadOnlyList<BakeProcessorOutput> Run(
        TestBakeProject project,
        BakeDiagnostics diagnostics)
    {
        BakeDiscoveredSources discovered = BakeSourceDiscovery.Discover(project.Project, diagnostics);
        return BakeProcessorRunner.Run(project.Project, discovered.ProcessorSources, diagnostics);
    }
}
