using Bork2d.Bake.Cli;
using Bork2d.Bake.Hosting;
using Bork2d.Tests.Bake.Hosting;
using Xunit;

namespace Bork2d.Tests.Bake.Cli;

/// <summary>
/// The <c>generate-host</c> command surface: required options, configuration validation, file
/// writing, up-to-date/force behavior, and canonical diagnostics on failure.
/// </summary>
[Collection(BakeHostFixtureCollection.Name)]
public sealed class GenerateHostCliTests
{
    private readonly TempBakeFixture _fixture;

    /// <summary>Initializes a new instance of the <see cref="GenerateHostCliTests"/> class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public GenerateHostCliTests(TempBakeFixture fixture) => _fixture = fixture;

    [Fact]
    public void GeneratesTheHostAndReportsUpToDate()
    {
        string outputDirectory = CreateOutputDirectory();

        Result first = Run(
            "generate-host",
            "--project",
            _fixture.DescriptorPath,
            "--common",
            _fixture.ProjectCommonAssemblyPath,
            "--output",
            outputDirectory);

        Assert.Equal(CliApplication.SuccessExitCode, first.ExitCode);
        Assert.Contains("generated", first.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, first.Error);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "BakeHost.csproj")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "BakeManifest.g.cs")));

        Result second = Run(
            "generate-host",
            "--project",
            _fixture.DescriptorPath,
            "--common",
            _fixture.ProjectCommonAssemblyPath,
            "--output",
            outputDirectory);

        Assert.Equal(CliApplication.SuccessExitCode, second.ExitCode);
        Assert.Contains("up to date", second.Output, StringComparison.Ordinal);

        Result forced = Run(
            "generate-host",
            "--project",
            _fixture.DescriptorPath,
            "--common",
            _fixture.ProjectCommonAssemblyPath,
            "--output",
            outputDirectory,
            "--force",
            "--verbose");

        Assert.Equal(CliApplication.SuccessExitCode, forced.ExitCode);
        Assert.Contains("generated", forced.Output, StringComparison.Ordinal);
        Assert.Contains(
            "mapped type BakeFixture.Common.GameConfig",
            forced.Output,
            StringComparison.Ordinal);
        Assert.Contains("reference Bork2d.Api:", forced.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ScansEveryPackRootForDistinctTypes()
    {
        string project = Path.Combine(
            Path.GetTempPath(),
            $"bork2d-bake-host-roots-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(project, "Assets", "data"));
        Directory.CreateDirectory(Path.Combine(project, "DLC1", "data"));
        File.WriteAllText(
            Path.Combine(project, TempBakeFixture.DescriptorFileName),
            """
            schema = 1

            [[assets]]
            root = "Assets"
            pack = "assets.pck"

            [[assets]]
            root = "DLC1"
            pack = "dlc1.pck"
            """);
        File.WriteAllText(
            Path.Combine(project, "Assets", "data", "main.toml"),
            """
            name = "main"
            health = 1
            must = "m"

            [bork2d]
            type = "BakeFixture.Common.GameConfig"
            """);
        File.WriteAllText(
            Path.Combine(project, "DLC1", "data", "dlc.toml"),
            """
            attack = 1
            defense = 2

            [bork2d]
            type = "BakeFixture.Common.StatsRoot"
            """);
        string outputDirectory = CreateOutputDirectory();

        Result result = Run(
            "generate-host",
            "--project",
            Path.Combine(project, TempBakeFixture.DescriptorFileName),
            "--common",
            _fixture.ProjectCommonAssemblyPath,
            "--output",
            outputDirectory);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        string manifest = File.ReadAllText(Path.Combine(outputDirectory, "BakeManifest.g.cs"));
        Assert.Contains("\"BakeFixture.Common.GameConfig\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"BakeFixture.Common.StatsRoot\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingCommonIsAUsageError()
    {
        Result result = Run(
            "generate-host",
            "--project",
            _fixture.DescriptorPath,
            "--output",
            CreateOutputDirectory());

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Equal(string.Empty, result.Output);
        Assert.Contains("--common", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingOutputIsAUsageError()
    {
        Result result = Run(
            "generate-host",
            "--project",
            _fixture.DescriptorPath,
            "--common",
            _fixture.ProjectCommonAssemblyPath);

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("--output", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedConfigurationIsAUsageError()
    {
        Result result = Run(
            "generate-host",
            "--project",
            _fixture.DescriptorPath,
            "--common",
            _fixture.ProjectCommonAssemblyPath,
            "--output",
            CreateOutputDirectory(),
            "--configuration",
            "Fast");

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("'Debug' or 'Release'", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownOptionIsAUsageError()
    {
        Result result = Run(
            "generate-host",
            "--project",
            _fixture.DescriptorPath,
            "--common",
            _fixture.ProjectCommonAssemblyPath,
            "--output",
            CreateOutputDirectory(),
            "--json");

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("--json", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TypeResolutionFailureIsAUsageErrorWithACanonicalDiagnostic()
    {
        string project = CreateProjectWithMapping("BakeFixture.Common.Missing");

        Result result = Run(
            "generate-host",
            "--project",
            project,
            "--common",
            _fixture.ProjectCommonAssemblyPath,
            "--output",
            CreateOutputDirectory());

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("error BK1020:", result.Error, StringComparison.Ordinal);
        Assert.Contains("BakeFixture.Common.Missing", result.Error, StringComparison.Ordinal);
        Assert.Contains("failed", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceMetadataErrorFailsGenerationAtTheSourceSpan()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"bork2d-bake-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "Assets", "data"));
        File.WriteAllText(
            Path.Combine(directory, TempBakeFixture.DescriptorFileName),
            """
            schema = 1

            [[assets]]
            root = "Assets"
            pack = "assets.pck"
            """);
        File.WriteAllText(
            Path.Combine(directory, "Assets", "data", "bad.toml"),
            """
            value = 1

            [bork2d]
            type = 4
            """);

        Result result = Run(
            "generate-host",
            "--project",
            Path.Combine(directory, TempBakeFixture.DescriptorFileName),
            "--common",
            _fixture.ProjectCommonAssemblyPath,
            "--output",
            CreateOutputDirectory());

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("error BK1031:", result.Error, StringComparison.Ordinal);
        Assert.Contains("Assets/data/bad.toml(4,8)", result.Error, StringComparison.Ordinal);
        Assert.Contains("failed", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void SidecarSyntaxErrorFailsGenerationBeforeTheHostIsWritten()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"bork2d-bake-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "Assets", "art"));
        File.WriteAllText(
            Path.Combine(directory, TempBakeFixture.DescriptorFileName),
            """
            schema = 1

            [[assets]]
            root = "Assets"
            pack = "assets.pck"
            """);
        File.WriteAllText(
            Path.Combine(directory, "Assets", "art", "player.png.toml"),
            "filter = \n");
        string output = CreateOutputDirectory();

        Result result = Run(
            "generate-host",
            "--project",
            Path.Combine(directory, TempBakeFixture.DescriptorFileName),
            "--common",
            _fixture.ProjectCommonAssemblyPath,
            "--output",
            output);

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("error BK2001:", result.Error, StringComparison.Ordinal);
        Assert.Contains("Assets/art/player.png.toml", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(output, "BakeHost.csproj")));
    }

    [Fact]
    public void HelpListsBothCommands()
    {
        Result result = Run("--help");

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("generate-host --project", result.Output, StringComparison.Ordinal);
        Assert.Contains("validate --project", result.Output, StringComparison.Ordinal);
    }

    private static string CreateOutputDirectory()
        => Path.Combine(Path.GetTempPath(), $"bork2d-bake-host-out-{Guid.NewGuid():N}");

    private static string CreateProjectWithMapping(string typeName)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"bork2d-bake-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "Assets", "data"));
        File.WriteAllText(
            Path.Combine(directory, TempBakeFixture.DescriptorFileName),
            """
            schema = 1

            [[assets]]
            root = "Assets"
            pack = "assets.pck"
            """);
        File.WriteAllText(
            Path.Combine(directory, "Assets", "data", "source.toml"),
            $"""
            value = 1

            [bork2d]
            type = "{typeName}"
            """);
        return Path.Combine(directory, TempBakeFixture.DescriptorFileName);
    }

    private static Result Run(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = CliApplication.Run(arguments, output, error);
        return new Result(exitCode, output.ToString(), error.ToString());
    }

    private sealed record Result(int ExitCode, string Output, string Error);

}
