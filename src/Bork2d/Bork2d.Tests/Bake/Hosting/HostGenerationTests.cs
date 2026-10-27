using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Project;
using Xunit;

namespace Bork2d.Tests.Bake.Hosting;

/// <summary>
/// The generated host contract: project and manifest contents, type-map order, determinism, the
/// Q-04 closure resolution, and every rejected type form with its <c>BK1xxx</c> code.
/// </summary>
[Collection(BakeHostFixtureCollection.Name)]
public sealed class HostGenerationTests
{
    private static readonly string[] _requiredReferences =
    [
        "Bork2d.Api",
        "Bork2d.Assets",
        "Bork2d.Bake",
        "Nerdbank.MessagePack",
        "PolyType",
        "System.IO.Hashing",
        "Tomlyn",
    ];

    private readonly TempBakeFixture _fixture;

    /// <summary>Initializes a new instance of the <see cref="HostGenerationTests"/> class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public HostGenerationTests(TempBakeFixture fixture) => _fixture = fixture;

    [Fact]
    public void GeneratedProjectHasTheRequiredShape()
    {
        string project = File.ReadAllText(_fixture.HostProjectPath);

        Assert.Contains("<AssemblyName>Bork2d.BakeHost</AssemblyName>", project, StringComparison.Ordinal);
        Assert.Contains("<Nullable>enable</Nullable>", project, StringComparison.Ordinal);
        Assert.Contains("<ImplicitUsings>enable</ImplicitUsings>", project, StringComparison.Ordinal);
        Assert.Contains(
            "<Compile Include=\"BakeManifest.g.cs\" />",
            project,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectReference", project, StringComparison.Ordinal);
        Assert.Contains("<Reference Include=\"Project.Common\">", project, StringComparison.Ordinal);

        foreach (string name in _requiredReferences)
        {
            Assert.Contains($"<Reference Include=\"{name}\">", project, StringComparison.Ordinal);
        }

        string[] hintPaths = [.. project
            .Split('\n')
            .Where(line => line.Contains("<HintPath>", StringComparison.Ordinal))
            .Select(line => line.Trim()
                .Replace("<HintPath>", string.Empty, StringComparison.Ordinal)
                .Replace("</HintPath>", string.Empty, StringComparison.Ordinal))];

        Assert.NotEmpty(hintPaths);
        Assert.All(hintPaths, path => Assert.True(Path.IsPathRooted(path), path));
        Assert.Equal(
            hintPaths.Length,
            hintPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            hintPaths.Length,
            project.Split("<Private>true</Private>").Length - 1);
    }

    [Fact]
    public void GeneratedManifestUsesConnectedTypeNamesInSourceOrder()
    {
        string manifest = File.ReadAllText(_fixture.HostManifestPath);

        Assert.Contains(
            "[assembly: global::Bork2d.Bake.Hosting.BakeManifest(",
            manifest,
            StringComparison.Ordinal);
        Assert.Contains(
            "typeof(global::Bork2d.BakeHost.BakeManifest))]",
            manifest,
            StringComparison.Ordinal);
        Assert.Contains("namespace Bork2d.BakeHost;", manifest, StringComparison.Ordinal);
        Assert.Contains(
            "internal sealed class BakeManifest : global::Bork2d.Bake.Hosting.IBakeManifest",
            manifest,
            StringComparison.Ordinal);

        int config = manifest.IndexOf(
            "\"BakeFixture.Common.GameConfig\"",
            StringComparison.Ordinal);
        int hidden = manifest.IndexOf(
            "\"BakeFixture.Common.HiddenConfig\"",
            StringComparison.Ordinal);
        int stats = manifest.IndexOf(
            "\"BakeFixture.Common.StatsRoot\"",
            StringComparison.Ordinal);
        int sprite = manifest.IndexOf(
            "\"Bork2d.Api.Assets.SpriteAsset\"",
            StringComparison.Ordinal);
        int weapon = manifest.IndexOf("\"Game.Data.Weapon\"", StringComparison.Ordinal);

        Assert.True(config >= 0 && config < hidden, "data/config must precede hidden/secret");
        Assert.True(hidden < stats, "hidden/secret must precede roots/stats");
        Assert.True(stats < sprite, "roots/stats must precede sprites/player");
        Assert.True(sprite < weapon, "sprites/player must precede weapons/sword");
    }

    [Fact]
    public void ManifestRegistersTheExternalLibraryAndInternalFriendTypes()
    {
        IReadOnlyList<BakeTypeRegistration> registrations = _fixture.Manifest.Types;

        Assert.Equal(
            new[]
            {
                new BakeTypeRegistration("BakeFixture.Common.GameConfig", BakeAssetKind.MessagePack),
                new BakeTypeRegistration("BakeFixture.Common.HiddenConfig", BakeAssetKind.MessagePack),
                new BakeTypeRegistration("BakeFixture.Common.StatsRoot", BakeAssetKind.MessagePack),
                new BakeTypeRegistration("Bork2d.Api.Assets.SpriteAsset", BakeAssetKind.MessagePack),
                new BakeTypeRegistration("Game.Data.Weapon", BakeAssetKind.MessagePack),
            },
            registrations);
    }

    [Fact]
    public void GeneratingTwiceIsDeterministicAndUnchangedWritesAreSkipped()
    {
        string outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"bork2d-bake-generate-{Guid.NewGuid():N}");

        var diagnostics = new BakeDiagnostics();
        HostGenerationResult? first = HostGenerator.Generate(
            new HostGenerationOptions(
                _fixture.DescriptorPath,
                _fixture.ProjectCommonAssemblyPath,
                outputDirectory,
                "Debug",
                force: false),
            diagnostics);

        Assert.NotNull(first);
        Assert.Empty(diagnostics.GetSorted());
        Assert.True(first.Changed);

        string project = File.ReadAllText(first.ProjectPath);
        string manifest = File.ReadAllText(first.ManifestPath);

        var secondDiagnostics = new BakeDiagnostics();
        HostGenerationResult? second = HostGenerator.Generate(
            new HostGenerationOptions(
                _fixture.DescriptorPath,
                _fixture.ProjectCommonAssemblyPath,
                outputDirectory,
                "Debug",
                force: false),
            secondDiagnostics);

        Assert.NotNull(second);
        Assert.False(second.Changed);
        Assert.Equal(project, File.ReadAllText(second.ProjectPath));
        Assert.Equal(manifest, File.ReadAllText(second.ManifestPath));

        var forcedDiagnostics = new BakeDiagnostics();
        HostGenerationResult? forced = HostGenerator.Generate(
            new HostGenerationOptions(
                _fixture.DescriptorPath,
                _fixture.ProjectCommonAssemblyPath,
                outputDirectory,
                "Debug",
                force: true),
            forcedDiagnostics);

        Assert.NotNull(forced);
        Assert.True(forced.Changed);
        Assert.Equal(project, File.ReadAllText(forced.ProjectPath));
        Assert.Equal(manifest, File.ReadAllText(forced.ManifestPath));
    }

    [Fact]
    public void ConfigurationBecomesTheGeneratedProjectDefault()
    {
        string outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"bork2d-bake-release-{Guid.NewGuid():N}");
        var diagnostics = new BakeDiagnostics();

        HostGenerationResult? result = HostGenerator.Generate(
            new HostGenerationOptions(
                _fixture.DescriptorPath,
                _fixture.ProjectCommonAssemblyPath,
                outputDirectory,
                "Release",
                force: true),
            diagnostics);

        Assert.NotNull(result);
        Assert.Contains(
            "<Configuration Condition=\"'$(Configuration)' == ''\">Release</Configuration>",
            File.ReadAllText(result.ProjectPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void MissingCommonAssemblyIsAHostReferenceDiagnostic()
    {
        (HostGenerationResult? result, IReadOnlyList<BakeDiagnostic> diagnostics) = Generate("BakeFixture.Common.GameConfig",
            commonAssemblyPath: Path.Combine(Path.GetTempPath(), "missing-common.dll"));

        Assert.Null(result);
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.HostReferenceMissing);
    }

    [Theory]
    [InlineData("BakeFixture.Common.Missing", BakeDiagnosticCodes.TypeNotFound)]
    [InlineData("Game.Data.DupType", BakeDiagnosticCodes.TypeAmbiguous)]
    [InlineData("BakeFixture.Common.GameConfig, Project.Common", BakeDiagnosticCodes.TypeUnsupportedForm)]
    [InlineData("BakeFixture.Common.Outer+Inner", BakeDiagnosticCodes.TypeUnsupportedForm)]
    [InlineData("BakeFixture.Common.GenericRoot`1", BakeDiagnosticCodes.TypeUnsupportedForm)]
    [InlineData("BakeFixture.Common.AbstractRoot", BakeDiagnosticCodes.TypeUnsupportedForm)]
    [InlineData("BakeFixture.Common.StaticRoot", BakeDiagnosticCodes.TypeUnsupportedForm)]
    [InlineData("BakeFixture.Common.Mood", BakeDiagnosticCodes.TypeUnsupportedForm)]
    [InlineData("BakeFixture.Common.NoShape", BakeDiagnosticCodes.TypeNotShapeable)]
    [InlineData("Game.Data.HiddenWeapon", BakeDiagnosticCodes.TypeNotVisible)]
    public void UnsupportedTypeNamesAreRejected(string typeName, string expectedCode)
    {
        (HostGenerationResult? result, IReadOnlyList<BakeDiagnostic> diagnostics) = Generate(typeName);

        Assert.Null(result);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == expectedCode);
    }

    [Fact]
    public void AmbiguousTypeNamesEveryCandidateAssembly()
    {
        (HostGenerationResult? result, IReadOnlyList<BakeDiagnostic> diagnostics) = Generate("Game.Data.DupType");

        Assert.Null(result);
        BakeDiagnostic diagnostic = Assert.Single(
            diagnostics,
            candidate => candidate.Code == BakeDiagnosticCodes.TypeAmbiguous);
        Assert.Contains("Project.Common", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Game.Data", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingMappedTypeDiagnosticPointsAtTheTypeValue()
    {
        (HostGenerationResult? result, IReadOnlyList<BakeDiagnostic> diagnostics) = Generate("BakeFixture.Common.Missing");

        Assert.Null(result);
        BakeDiagnostic diagnostic = Assert.Single(
            diagnostics,
            candidate => candidate.Code == BakeDiagnosticCodes.TypeNotFound);
        Assert.Equal(4, diagnostic.Line);
        Assert.Equal(8, diagnostic.Column);
    }

    [Theory]
    [InlineData("BakeFixture.Common.HiddenConfig")]
    [InlineData("Game.Data.Weapon")]
    [InlineData("Bork2d.Api.Assets.SpriteAsset")]
    public void SupportedTypesGenerate(string typeName)
    {
        (HostGenerationResult? result, IReadOnlyList<BakeDiagnostic> diagnostics) = Generate(typeName);

        Assert.NotNull(result);
        Assert.Empty(diagnostics);
        Assert.Contains(
            result.Types,
            mapping => mapping.TypeName == typeName
                && mapping.CSharpName.StartsWith("global::", StringComparison.Ordinal));
    }

    private (HostGenerationResult? Result, IReadOnlyList<BakeDiagnostic> Diagnostics) Generate(
        string typeName,
        string? commonAssemblyPath = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"bork2d-bake-gen-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "Assets", "data"));
        string descriptorPath = Path.Combine(directory, TempBakeFixture.DescriptorFileName);
        File.WriteAllText(
            descriptorPath,
            """
            schema = 1

            [assets]
            root = "Assets"
            """);
        File.WriteAllText(
            Path.Combine(directory, "Assets", "data", "source.toml"),
            $"""
            value = 1

            [bork2d]
            type = "{typeName}"
            """);

        var diagnostics = new BakeDiagnostics();
        HostGenerationResult? result = HostGenerator.Generate(
            new HostGenerationOptions(
                descriptorPath,
                commonAssemblyPath ?? _fixture.ProjectCommonAssemblyPath,
                Path.Combine(directory, "host"),
                "Debug",
                force: true),
            diagnostics);

        return (result, diagnostics.GetSorted());
    }
}
