using Bork2d.Bake.Cli;
using Xunit;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// The <c>bake</c> CLI surface against the real generated host: exit codes, stderr/stdout routing,
/// <c>--check</c>/<c>--force</c> behavior, and the separate-process structural determinism proof
/// (§16.6).
/// </summary>
[Collection(BakeProjectFixtureCollection.Name)]
public sealed class BakeCliTests
{
    private readonly BakeProjectFixture _fixture;

    /// <summary>Initializes a new instance of the <see cref="BakeCliTests"/> class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public BakeCliTests(BakeProjectFixture fixture) => _fixture = fixture;

    [Fact]
    public void BakeWritesThePackAndReportsTheSummaryOnStdout()
    {
        using var directory = TempBakeDirectory.Create();
        string packPath = directory.FullPath("assets.pck");

        Result result = Run(
            "bake",
            "--project",
            _fixture.DescriptorPath,
            "--host",
            _fixture.HostAssemblyPath,
            "--output",
            packPath);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.True(File.Exists(packPath));
        Assert.Contains("baked '", result.Output, StringComparison.Ordinal);
        Assert.Contains("3 asset(s)", result.Output, StringComparison.Ordinal);
        Assert.Contains("1 texture(s)", result.Output, StringComparison.Ordinal);
        Assert.Contains("2 reference(s)", result.Output, StringComparison.Ordinal);
        Assert.Contains("debug mode", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckWritesNoPack()
    {
        using var directory = TempBakeDirectory.Create();
        string packPath = directory.FullPath("assets.pck");

        Result result = Run(
            "bake",
            "--project",
            _fixture.DescriptorPath,
            "--host",
            _fixture.HostAssemblyPath,
            "--output",
            packPath,
            "--check");

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.False(File.Exists(packPath));
        Assert.Contains("no pack written (--check)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondBakeIsUpToDateAndForceRebakes()
    {
        using var directory = TempBakeDirectory.Create();
        string packPath = directory.FullPath("assets.pck");

        Result first = Run(
            "bake",
            "--project",
            _fixture.DescriptorPath,
            "--host",
            _fixture.HostAssemblyPath,
            "--output",
            packPath);
        Assert.Equal(CliApplication.SuccessExitCode, first.ExitCode);
        Assert.Contains("baked '", first.Output, StringComparison.Ordinal);
        byte[] packBytes = File.ReadAllBytes(packPath);

        Result second = Run(
            "bake",
            "--project",
            _fixture.DescriptorPath,
            "--host",
            _fixture.HostAssemblyPath,
            "--output",
            packPath);
        Assert.Equal(CliApplication.SuccessExitCode, second.ExitCode);
        Assert.Contains("up to date", second.Output, StringComparison.Ordinal);
        Assert.Equal(packBytes, File.ReadAllBytes(packPath));

        Result forced = Run(
            "bake",
            "--project",
            _fixture.DescriptorPath,
            "--host",
            _fixture.HostAssemblyPath,
            "--output",
            packPath,
            "--force");
        Assert.Equal(CliApplication.SuccessExitCode, forced.ExitCode);
        Assert.Contains("baked '", forced.Output, StringComparison.Ordinal);
        Assert.Equal(packBytes, File.ReadAllBytes(packPath));
    }

    [Fact]
    public void ReleaseModeIsReportedAndAccepted()
    {
        using var directory = TempBakeDirectory.Create();

        Result result = Run(
            "bake",
            "--project",
            _fixture.DescriptorPath,
            "--host",
            _fixture.HostAssemblyPath,
            "--output",
            directory.FullPath("assets.pck"),
            "--mode",
            "release");

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.Contains("release mode", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidModeIsAUsageError()
    {
        Result result = Run(
            "bake",
            "--project",
            _fixture.DescriptorPath,
            "--host",
            _fixture.HostAssemblyPath,
            "--mode",
            "fast");

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("--mode must be 'debug' or 'release'", result.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public void MissingHostIsAConfigurationError()
    {
        Result result = Run(
            "bake",
            "--project",
            _fixture.DescriptorPath,
            "--host",
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll"));

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("error BK1014:", result.Error, StringComparison.Ordinal);
        Assert.Contains("could not be loaded", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidSourceReturnsOneAndKeepsTheExistingPack()
    {
        string project = BakeProjectFixture.CreateProject("cli-invalid");
        BakeProjectFixture.WriteAsset(project, "data/game.toml", "name = \n");
        BakeProjectFixture.WriteTexture(project);
        string packPath = Path.Combine(project, "assets.pck");
        BakedPackAssertions.WriteValidPack(packPath);
        byte[] before = File.ReadAllBytes(packPath);

        Result result = Run(
            "bake",
            "--project",
            Path.Combine(project, BakeProjectFixture.DescriptorFileName),
            "--host",
            _fixture.HostAssemblyPath,
            "--output",
            packPath);

        Assert.Equal(CliApplication.DiagnosticsExitCode, result.ExitCode);
        Assert.Contains("error BK2001:", result.Error, StringComparison.Ordinal);
        Assert.Contains("no pack was written", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(packPath));
    }

    [Fact]
    public void SeparateProcessBakesAreStructurallyIdentical()
    {
        using var first = TempBakeDirectory.Create();
        using var second = TempBakeDirectory.Create();
        string firstPack = first.FullPath("assets.pck");
        string secondPack = second.FullPath("assets.pck");
        string cliPath = typeof(CliApplication).Assembly.Location;

        TempProcessRunner.Result firstRun = RunCli(cliPath, firstPack);
        TempProcessRunner.Result secondRun = RunCli(cliPath, secondPack);

        Assert.Equal(0, firstRun.ExitCode);
        Assert.Equal(0, secondRun.ExitCode);
        Assert.Contains("baked '", firstRun.Output, StringComparison.Ordinal);
        Assert.Contains("baked '", secondRun.Output, StringComparison.Ordinal);

        BakedPackAssertions.AssertStructurallyEqual(firstPack, secondPack);
    }

    private TempProcessRunner.Result RunCli(string cliPath, string packPath)
        => TempProcessRunner.Run(
            "dotnet",
            [
                cliPath,
                "bake",
                "--project",
                _fixture.DescriptorPath,
                "--host",
                _fixture.HostAssemblyPath,
                "--output",
                packPath,
                "--mode",
                "debug",
            ],
            _fixture.RootPath);

    private static Result Run(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = CliApplication.Run(arguments, output, error);
        return new Result(exitCode, output.ToString(), error.ToString());
    }

    private sealed record Result(int ExitCode, string Output, string Error);
}
