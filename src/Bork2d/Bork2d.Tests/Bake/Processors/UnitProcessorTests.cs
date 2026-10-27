using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.BakeFixture.Assets.Processors;
using Bork2d.Tests.Assets.Service;
using Bork2d.Tests.Bake.Baking;
using Xunit;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// The Unit acceptance processor through the public builder and the real bake/load path: the default
/// sidecar from the source file stem, sidecar overrides, JSON validation, and a <c>Unit</c> round trip
/// through the real asset service without a <c>RegisterAsset&lt;Unit&gt;()</c> entry.
/// </summary>
public sealed class UnitProcessorTests
{
    private const string UnitType = "Bork2d.BakeFixture.Unit";

    private const string ArcherJson = """
        {"Health": 50, "Attack": 20}
        """;

    [Fact]
    public async Task MissingSidecarDefaultsTheNameToTheFileStemAndLoadsWithoutRegistration()
    {
        using var project = CreateUnitProject("unit-default-sidecar");
        project.WriteAsset("data/archer.unit", ArcherJson);
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        BakeDiagnostic info = Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.Equal(BakeSeverity.Info, info.Severity);
        Assert.Equal("Assets/data/archer.unit.toml", info.FilePath);
        Assert.Equal("name = \"archer\"\n", File.ReadAllText(project.FullPath("Assets/data/archer.unit.toml")));

        Assert.Equal(1, result.AssetCount);
        BakedAssetEntry entry = Assert.Single(result.Entries);
        Assert.Equal("data/archer", entry.Path);
        Assert.Equal(BakedAssetEntryKind.ProcessorAsset, entry.Kind);
        Assert.Equal(UnitType, entry.TypeName);

        using var host = new AssetServiceHost(output.FullPath("assets.pck"));
        host.Start();
        Bork2d.BakeFixture.Unit unit =
            await host.Service.LoadAsset<Bork2d.BakeFixture.Unit>("data/archer");
        Assert.Equal(new Bork2d.BakeFixture.Unit("archer", 50, 20), unit);
    }

    [Fact]
    public async Task SidecarNameOverridesTheFileStem()
    {
        using var project = CreateUnitProject("unit-sidecar-name");
        project.WriteAsset("data/archer.unit", ArcherJson);
        project.WriteAsset("data/archer.unit.toml", "name = \"ranger\"\n");
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("name = \"ranger\"\n", File.ReadAllText(project.FullPath("Assets/data/archer.unit.toml")));

        using var host = new AssetServiceHost(output.FullPath("assets.pck"));
        host.Start();
        Bork2d.BakeFixture.Unit unit =
            await host.Service.LoadAsset<Bork2d.BakeFixture.Unit>("data/archer");
        Assert.Equal(new Bork2d.BakeFixture.Unit("ranger", 50, 20), unit);
    }

    [Fact]
    public async Task DefaultSidecarNameUsesTheFileStemOfADifferentSource()
    {
        using var project = CreateUnitProject("unit-file-stem");
        project.WriteAsset("data/knight.unit", """{"Health": 7, "Attack": 3}""");
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Equal("name = \"knight\"\n", File.ReadAllText(project.FullPath("Assets/data/knight.unit.toml")));

        using var host = new AssetServiceHost(output.FullPath("assets.pck"));
        host.Start();
        Bork2d.BakeFixture.Unit unit = await host.Service.LoadAsset<Bork2d.BakeFixture.Unit>("data/knight");
        Assert.Equal(new Bork2d.BakeFixture.Unit("knight", 7, 3), unit);
    }

    [Fact]
    public async Task EmptySidecarNameFallsBackToTheFileStem()
    {
        using var project = CreateUnitProject("unit-empty-name");
        project.WriteAsset("data/archer.unit", ArcherJson);
        project.WriteAsset("data/archer.unit.toml", "name = \"\"\n");
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output);

        Assert.Equal(BakeStatus.Success, result.Status);
        using var host = new AssetServiceHost(output.FullPath("assets.pck"));
        host.Start();
        Bork2d.BakeFixture.Unit unit =
            await host.Service.LoadAsset<Bork2d.BakeFixture.Unit>("data/archer");
        Assert.Equal(new Bork2d.BakeFixture.Unit("archer", 50, 20), unit);
    }

    [Theory]
    [MemberData(nameof(InvalidUnitSources))]
    public void InvalidJsonOrShapeIsBK7xxxAndProducesNoOutputs(
        string caseName,
        string json,
        string expectedCode)
    {
        Assert.False(string.IsNullOrEmpty(caseName));
        using var project = CreateUnitProject("unit-invalid");
        project.WriteAsset("data/broken.unit", json);
        project.WriteAsset("data/broken.unit.toml", "name = \"broken\"\n");
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.False(result.PackWritten);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Severity == BakeSeverity.Error);
        Assert.Equal(expectedCode, diagnostic.Code);
        Assert.StartsWith("BK7", diagnostic.Code, StringComparison.Ordinal);
        Assert.Equal("Assets/data/broken.unit", diagnostic.FilePath);
        Assert.Contains("unit", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("broken.unit", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output.FullPath("assets.pck")));
    }

    /// <summary>Gets the invalid unit sources and their expected diagnostic codes.</summary>
    public static TheoryData<string, string, string> InvalidUnitSources => new()
    {
        { "invalid JSON", "{", ProcessorDiagnosticCodes.SourceRead },
        { "array root", "[]", ProcessorDiagnosticCodes.InputInvalid },
        { "missing health", """{"Attack": 1}""", ProcessorDiagnosticCodes.InputInvalid },
        { "missing attack", """{"Health": 1}""", ProcessorDiagnosticCodes.InputInvalid },
        { "string health", """{"Health": "many", "Attack": 1}""", ProcessorDiagnosticCodes.InputInvalid },
        { "boolean attack", """{"Health": 1, "Attack": true}""", ProcessorDiagnosticCodes.InputInvalid },
        { "fractional health", """{"Health": 1.5, "Attack": 1}""", ProcessorDiagnosticCodes.InputInvalid },
    };

    private static TestBakeProject CreateUnitProject(string name)
        => TestBakeProject.Create(
            name,
            registerFixtureTypes: false,
            processors: [new UnitProcessor()]);

    private static BakeResult RunBake(TestBakeProject project, TempBakeDirectory output)
        => BakePipeline.Run(
            project.Project,
            new BakeOptions(output.RootPath, BakeMode.Debug, check: false, force: false),
            new BakeDiagnostics());
}
