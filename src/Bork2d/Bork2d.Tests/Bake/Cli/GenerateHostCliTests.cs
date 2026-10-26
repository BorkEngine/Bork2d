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
        Assert.Contains("data -> BakeFixture.Common.GameConfig", forced.Output, StringComparison.Ordinal);
        Assert.Contains("reference Bork2d.Api:", forced.Output, StringComparison.Ordinal);
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
        string project = CreateProjectWithMapping(
            "data",
            "BakeFixture.Common.Missing");

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
    public void HelpListsBothCommands()
    {
        Result result = Run("--help");

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("generate-host --project", result.Output, StringComparison.Ordinal);
        Assert.Contains("validate --project", result.Output, StringComparison.Ordinal);
    }

    private static string CreateOutputDirectory()
        => Path.Combine(Path.GetTempPath(), $"bork2d-bake-host-out-{Guid.NewGuid():N}");

    private static string CreateProjectWithMapping(string prefix, string typeName)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"bork2d-bake-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "Assets"));
        File.WriteAllText(
            Path.Combine(directory, TempBakeFixture.DescriptorFileName),
            $"""
            schema = 1

            [assets]
            root = "Assets"

            [[types]]
            prefix = "{prefix}"
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
