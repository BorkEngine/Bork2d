using Bork2d.Bake.Cli;
using Bork2d.Tests.Bake.Hosting;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Cli;

/// <summary>
/// The <c>validate --host</c> surface: it loads the host, checks the host manifest against the
/// descriptor, binds every mapped source, reports source diagnostics with exit code 1, and never
/// writes a pack.
/// </summary>
[Collection(BakeHostFixtureCollection.Name)]
public sealed class ValidateHostCliTests
{
    private readonly TempBakeFixture _fixture;

    /// <summary>Initializes a new instance of the <see cref="ValidateHostCliTests"/> class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public ValidateHostCliTests(TempBakeFixture fixture) => _fixture = fixture;

    [Fact]
    public void ValidSourcesBindWithExitZeroAndNoPack()
    {
        string project = _fixture.CreateStandardProject("valid");

        string texturePath = Path.Combine(project, "Assets", "textures", "player.png");
        Directory.CreateDirectory(Path.GetDirectoryName(texturePath)!);
        File.WriteAllBytes(texturePath, TextureImageFactory.RgbaPng());

        Result result = Run(
            "validate",
            "--project",
            Path.Combine(project, TempBakeFixture.DescriptorFileName),
            "--host",
            _fixture.HostAssemblyPath,
            "--verbose");

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.Contains("5 asset(s) bound", result.Output, StringComparison.Ordinal);
        Assert.Contains("2 reference site(s)", result.Output, StringComparison.Ordinal);
        Assert.Contains("data/config -> BakeFixture.Common.GameConfig", result.Output, StringComparison.Ordinal);
        Assert.Contains("sprites/player -> Bork2d.Api.Assets.SpriteAsset", result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(project, "assets.pck")));
    }

    [Fact]
    public void SourceErrorsUseExitOneAndTheCanonicalLine()
    {
        string project = _fixture.CreateStandardProject("source-errors");
        File.WriteAllText(
            Path.Combine(project, "Assets", "data", "config.toml"),
            """
            name = "campaign"
            health = "many"
            bogus = true

            [bork2d]
            type = "BakeFixture.Common.GameConfig"
            """);

        Result result = Run(
            "validate",
            "--project",
            Path.Combine(project, TempBakeFixture.DescriptorFileName),
            "--host",
            _fixture.HostAssemblyPath);

        Assert.Equal(CliApplication.DiagnosticsExitCode, result.ExitCode);
        Assert.Contains(
            "Assets/data/config.toml(2,10): error BK2004:",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains("error BK2003:", result.Error, StringComparison.Ordinal);
        Assert.Contains("error BK2002:", result.Error, StringComparison.Ordinal);
        Assert.Contains("source error(s)", result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(project, "assets.pck")));
    }

    [Fact]
    public void MissingHostIsAHostDiagnosticWithExitTwo()
    {
        string project = _fixture.CreateProjectCopy("missing-host");

        Result result = Run(
            "validate",
            "--project",
            Path.Combine(project, TempBakeFixture.DescriptorFileName),
            "--host",
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll"));

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("error BK1014:", result.Error, StringComparison.Ordinal);
        Assert.Contains("could not be loaded", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleHostIsReportedBeforeBinding()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"bork2d-bake-stale-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "Assets"));
        File.WriteAllText(
            Path.Combine(directory, TempBakeFixture.DescriptorFileName),
            """
            schema = 1

            [assets]
            root = "Assets"
            """);

        Result result = Run(
            "validate",
            "--project",
            Path.Combine(directory, TempBakeFixture.DescriptorFileName),
            "--host",
            _fixture.HostAssemblyPath);

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("error BK1018:", result.Error, StringComparison.Ordinal);
        Assert.Contains("regenerate the host", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void QuietSuppressesTheSummaryButNotDiagnostics()
    {
        string project = _fixture.CreateStandardProject("quiet");
        File.WriteAllText(
            Path.Combine(project, "Assets", "data", "config.toml"),
            """
            name = "campaign"
            health = "many"

            [bork2d]
            type = "BakeFixture.Common.GameConfig"
            """);

        Result invalid = Run(
            "validate",
            "--project",
            Path.Combine(project, TempBakeFixture.DescriptorFileName),
            "--host",
            _fixture.HostAssemblyPath,
            "--quiet");

        Assert.Equal(CliApplication.DiagnosticsExitCode, invalid.ExitCode);
        Assert.Equal(string.Empty, invalid.Output);
        Assert.Contains("error BK2004:", invalid.Error, StringComparison.Ordinal);

        string validProject = _fixture.CreateStandardProject("quiet-valid");
        Bork2d.Tests.Bake.Baking.BakeProjectFixture.WriteTexture(validProject);

        Result valid = Run(
            "validate",
            "--project",
            Path.Combine(validProject, TempBakeFixture.DescriptorFileName),
            "--host",
            _fixture.HostAssemblyPath,
            "--quiet");

        Assert.Equal(CliApplication.SuccessExitCode, valid.ExitCode);
        Assert.Equal(string.Empty, valid.Output);
        Assert.Equal(string.Empty, valid.Error);
    }

    [Fact]
    public void WithoutAHostTheDescriptorTexturesAndTomlSyntaxAreValidated()
    {
        string project = _fixture.CreateProjectCopy(
            "descriptor-only",
            ("data/config.toml", "this is not valid toml"));

        Result result = Run(
            "validate",
            "--project",
            Path.Combine(project, TempBakeFixture.DescriptorFileName));

        Assert.Equal(CliApplication.DiagnosticsExitCode, result.ExitCode);
        Assert.Contains("error BK2001:", result.Error, StringComparison.Ordinal);
        Assert.Contains("Assets/data/config.toml(1,", result.Error, StringComparison.Ordinal);
        Assert.Contains("source error(s)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAHostMissingSourceTypeIsReported()
    {
        string project = _fixture.CreateProjectCopy(
            "descriptor-missing-type",
            ("data/notes.toml", "value = 1\n"));

        Result result = Run(
            "validate",
            "--project",
            Path.Combine(project, TempBakeFixture.DescriptorFileName));

        Assert.Equal(CliApplication.DiagnosticsExitCode, result.ExitCode);
        Assert.Contains("error BK1030:", result.Error, StringComparison.Ordinal);
        Assert.Contains("Assets/data/notes.toml", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAHostAbsentReferencesAndValidSyntaxPass()
    {
        string project = _fixture.CreateProjectCopy(
            "descriptor-syntax-ok",
            ("data/config.toml", """
                name = "campaign"
                health = 1
                must = "m"

                [bork2d]
                type = "BakeFixture.Common.GameConfig"
                """));

        Result result = Run(
            "validate",
            "--project",
            Path.Combine(project, TempBakeFixture.DescriptorFileName));

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("is valid", result.Output, StringComparison.Ordinal);
        Assert.Contains("1 custom-data source(s)", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("bound", result.Output, StringComparison.Ordinal);
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
