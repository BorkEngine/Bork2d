using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Tests.Bake.Baking;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// Multi-pack processor reference scoping through the real pipeline: a DLC processor output may
/// reference the main pack, while the strict main pack cannot resolve a DLC-only path.
/// </summary>
public sealed class BakeProcessorMultiPackTests
{
    [Fact]
    public void ADlcProcessorOutputMayReferenceAMainPackTexture()
    {
        using var project = TestBakeProject.Create(
            "processor-multipack-main",
            [("assets.pck", "Assets"), ("dlc1.pck", "DLC1")],
            processors:
            [
                ReferenceProcessor(new ProcessorRefAsset
                {
                    Texture = "art/player",
                    Payload = "art/player-raw",
                }),
            ]);
        project.WriteAssetBytesUnder("Assets", "art/player.png", TextureImageFactory.RgbaPng());
        project.WriteAssetBytesUnder("DLC1", "data/link.ref", [1]);
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.Empty(result.Diagnostics);
        Assert.Contains(
            result.Packs[1].Entries,
            entry => entry.Path == "data/link" && entry.Kind == BakedAssetEntryKind.ProcessorAsset);
        Assert.Contains(
            result.Packs[0].Entries,
            entry => entry.Path == "art/player" && entry.Kind == BakedAssetEntryKind.TextureDescriptor);
    }

    [Fact]
    public void AMainProcessorOutputCannotReferenceADlcOnlyPath()
    {
        using var project = TestBakeProject.Create(
            "processor-multipack-dlc",
            [("assets.pck", "Assets"), ("dlc1.pck", "DLC1")],
            processors:
            [
                ReferenceProcessor(new ProcessorRefAsset { Texture = "art/dlc" }),
            ]);
        project.WriteAssetBytesUnder("Assets", "data/main.ref", [1]);
        project.WriteAssetBytesUnder("DLC1", "art/dlc.png", TextureImageFactory.RgbaPng());
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.False(result.PackWritten);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == BakeDiagnosticCodes.ReferenceMissing);
        Assert.Equal("art/dlc", diagnostic.AssetPath);
        Assert.Contains("assets.pck", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(output.RootPath, "assets.pck")));
        Assert.False(File.Exists(Path.Combine(output.RootPath, "dlc1.pck")));
    }

    private static TestProcessor ReferenceProcessor(ProcessorRefAsset value)
        => new(
            "test-ref",
            "ref",
            context => context.Outputs.AddAsset(context.BaseAssetPath, value));

    private static BakeResult RunBake(TestBakeProject project, string outputRoot)
        => BakePipeline.Run(
            project.Project,
            new BakeOptions(Path.GetFullPath(outputRoot), BakeMode.Debug, check: false, force: false),
            new BakeDiagnostics());
}
