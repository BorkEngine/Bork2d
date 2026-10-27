using Bork2d.Bake;
using Bork2d.BakeFixture.Assets.Processors;
using Bork2d.Engine.Assets;
using Bork2d.Tests.Assets.Service;
using Bork2d.Tests.Bake.Baking;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// The fixture acceptance processors through the public assets application (<c>BakeApplication.Run</c>):
/// a real <c>bake</c> command writes one pack and the runtime service loads both the PPM descriptor and
/// payload and the Unit custom-data value.
/// </summary>
public sealed class FixtureProcessorsApplicationTests
{
    [Fact]
    public async Task BakeAndLoadBothAcceptanceProcessorsThroughTheAssetsApplication()
    {
        IAssetProcessor[] processors =
        [
            new PpmProcessor(TextureFilter.Nearest, TextureWrap.Repeat),
            new UnitProcessor(),
        ];

        using var project = TestBakeProject.Create(
            "fixture-processors-application",
            registerFixtureTypes: false,
            processors: processors);
        project.WriteAssetBytes(
            "art/checker.ppm",
            [
                .. "P6\n2 2\n255\n"u8.ToArray(),
                255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255,
            ]);
        project.WriteAsset("art/checker.ppm.toml", "filter = \"nearest\"\nwrap = \"repeat\"\n");
        project.WriteAsset("data/archer.unit", """{"Health": 50, "Attack": 20}""");
        project.WriteAsset("data/archer.unit.toml", "name = \"archer\"\n");
        using var output = TempBakeDirectory.Create();

        BakeApplication application = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")],
            registerFixtureTypes: false,
            processors: processors);

        var standardOutput = new StringWriter();
        var standardError = new StringWriter();
        int exitCode = application.Run(
            ["bake", "--project-root", project.RootPath, "--output-root", output.RootPath],
            standardOutput,
            standardError,
            "fixture-assets");

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, standardError.ToString());
        Assert.Contains("baked '", standardOutput.ToString(), StringComparison.Ordinal);

        using var host = new AssetServiceHost(output.FullPath("assets.pck"));
        host.Start();

        TextureDescriptor ppm = await host.Service.LoadAsset<TextureDescriptor>("art/checker");
        Assert.Equal("art/checker-raw", ppm.Payload.Path);
        Assert.Equal(2u, ppm.Width);
        Assert.Equal(2u, ppm.Height);
        Assert.Equal(TextureFormat.Rgba8, ppm.Format);
        Assert.Equal(TextureFilter.Nearest, ppm.Filter);
        Assert.Equal(TextureWrap.Repeat, ppm.Wrap);
        Assert.Equal(
            new byte[]
            {
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 255, 255,
            },
            await host.Service.LoadData("art/checker-raw"));

        Bork2d.BakeFixture.Unit unit =
            await host.Service.LoadAsset<Bork2d.BakeFixture.Unit>("data/archer");
        Assert.Equal(new Bork2d.BakeFixture.Unit("archer", 50, 20), unit);
    }

    [Fact]
    public void FixtureAssetsExecutableBakesEightMainAndNineMergedEntries()
    {
        string executable = RepositoryPaths.FixtureAssetsExecutable;
        Assert.True(
            File.Exists(executable),
            $"The fixture assets executable was not built: '{executable}'.");
        using var output = TempBakeDirectory.Create();

        TempProcessRunner.Result result = TempProcessRunner.Run(
            "dotnet",
            [
                executable,
                "bake",
                "--project-root",
                RepositoryPaths.FixtureAssetsProjectDirectory,
                "--output-root",
                output.RootPath,
                "--mode",
                "debug",
            ],
            RepositoryPaths.FixtureAssetsProjectDirectory,
            timeoutMilliseconds: 600_000);

        Assert.True(result.ExitCode == 0, result.Output);

        string[] mainEntries = [.. BakedPackAssertions
            .Shapes(output.FullPath("assets.pck"))
            .Select(shape => shape.Path)];
        string[] dlcEntries = [.. BakedPackAssertions
            .Shapes(output.FullPath("dlc1.pck"))
            .Select(shape => shape.Path)];

        Assert.Equal(8, mainEntries.Length);
        Assert.Single(dlcEntries);
        Assert.Equal(9, mainEntries.Union(dlcEntries, StringComparer.Ordinal).Count());
        Assert.Contains("art/checker", mainEntries);
        Assert.Contains("art/checker-raw", mainEntries);
        Assert.Contains("data/archer", mainEntries);
        Assert.Equal(new[] { "data/dlc" }, dlcEntries);
    }
}
