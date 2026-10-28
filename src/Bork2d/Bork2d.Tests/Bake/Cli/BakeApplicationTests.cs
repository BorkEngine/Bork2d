using Bork2d.Bake;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Incremental;
using Bork2d.Tests.Bake.Baking;
using Xunit;

namespace Bork2d.Tests.Bake.Cli;

/// <summary>
/// The assets command surface against <see cref="BakeApplication"/>: exit codes, stderr/stdout
/// routing, <c>--check</c>/<c>--force</c> behavior, <c>clean</c>, <c>copy-packs</c>, and
/// configuration diagnostics.
/// </summary>
public sealed class BakeApplicationTests
{
    private const string ProgramName = "test-assets";

    [Fact]
    public void HelpWritesUsageToStdoutAndReturnsZero()
    {
        using var project = TestBakeProject.Create("cli-help");
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);

        Result result = Run(app, "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"usage: {ProgramName}", result.Output, StringComparison.Ordinal);
        Assert.Contains("validate", result.Output, StringComparison.Ordinal);
        Assert.Contains("copy-packs", result.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public async Task VersionWritesTheToolVersionThroughThePublicEntryPoint()
    {
        using var project = TestBakeProject.Create("cli-version");
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);

        int exitCode = await app.RunAsync(["--version"]);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void NoArgumentsIsAUsageError()
    {
        using var project = TestBakeProject.Create("cli-no-args");
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);

        Result result = Run(app);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Output);
        Assert.Contains("a command is required", result.Error, StringComparison.Ordinal);
        Assert.Contains("usage:", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCommandIsAUsageError()
    {
        using var project = TestBakeProject.Create("cli-unknown");
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);

        Result result = Run(app, "update");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("unknown command 'update'", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRejectsUnknownOptions()
    {
        using var project = TestBakeProject.Create("cli-unknown-option");
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);

        Result result = Run(app, "validate", "--json");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--json", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRejectsVerboseAndQuietTogether()
    {
        using var project = TestBakeProject.Create("cli-verbose-quiet");
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);

        Result result = Run(app, "validate", "--verbose", "--quiet");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("mutually exclusive", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidProjectReturnsZeroWithTheSummaryOnStdout()
    {
        using var project = TestBakeProject.Create("cli-valid");
        project.WriteStandardAssets();
        project.WriteTexture();
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);

        Result result = Run(app, "validate", "--project-root", project.RootPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("valid", result.Output, StringComparison.Ordinal);
        Assert.Contains("3 asset(s)", result.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void MissingProjectRootIsAConfigurationError()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"bork2d-missing-{Guid.NewGuid():N}");
        BakeApplication app = new BakeApplicationBuilder()
            .WithProjectRoot(missing)
            .WithPack("assets.pck", "Assets")
            .UseDefaultAssets()
            .Build();

        Result result = Run(app, "validate");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("error BK1002:", result.Error, StringComparison.Ordinal);
        Assert.Contains("configuration is invalid", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicatePackPathIsAConfigurationError()
    {
        using var project = TestBakeProject.Create("cli-duplicate-pack");
        BakeApplication app = new BakeApplicationBuilder()
            .WithProjectRoot(project.RootPath)
            .WithPack("assets.pck", "Assets")
            .WithPack("assets.pck", "Assets")
            .UseDefaultAssets()
            .Build();

        Result result = Run(app, "validate");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("error BK1008:", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void BakeWritesThePackAndReportsTheSummaryOnStdout()
    {
        using var project = TestBakeProject.Create("cli-bake");
        project.WriteStandardAssets();
        project.WriteTexture();
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);
        using var output = TempBakeDirectory.Create();
        string packPath = output.FullPath("assets.pck");

        Result result = Run(
            app,
            "bake",
            "--project-root",
            project.RootPath,
            "--output-root",
            output.RootPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.True(File.Exists(packPath));
        Assert.Contains("baked '", result.Output, StringComparison.Ordinal);
        Assert.Contains("3 asset(s)", result.Output, StringComparison.Ordinal);
        Assert.Contains("1 texture(s)", result.Output, StringComparison.Ordinal);
        Assert.Contains("3 reference(s)", result.Output, StringComparison.Ordinal);
        Assert.Contains("debug mode", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondBakeWithUnchangedInputsIsUpToDate()
    {
        using var project = TestBakeProject.Create("cli-up-to-date");
        project.WriteStandardAssets();
        project.WriteTexture();
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);
        using var output = TempBakeDirectory.Create();
        string packPath = output.FullPath("assets.pck");
        string manifestPath = BakeManifestPaths.ManifestPath(project.RootPath);

        Result first = Run(
            app,
            "bake",
            "--project-root",
            project.RootPath,
            "--output-root",
            output.RootPath);
        Assert.Equal(0, first.ExitCode);
        Assert.True(File.Exists(manifestPath));
        Assert.Contains("\"schema\": 3", File.ReadAllText(manifestPath), StringComparison.Ordinal);
        byte[] packBytes = File.ReadAllBytes(packPath);

        Result second = Run(
            app,
            "bake",
            "--project-root",
            project.RootPath,
            "--output-root",
            output.RootPath);

        Assert.Equal(0, second.ExitCode);
        Assert.Contains("up to date:", second.Output, StringComparison.Ordinal);
        Assert.Contains("no inputs changed.", second.Output, StringComparison.Ordinal);
        Assert.Contains("info BK5005:", second.Error, StringComparison.Ordinal);
        Assert.Equal(packBytes, File.ReadAllBytes(packPath));
    }

    [Fact]
    public void CheckWritesNoPack()
    {
        using var project = TestBakeProject.Create("cli-check");
        project.WriteStandardAssets();
        project.WriteTexture();
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);
        using var output = TempBakeDirectory.Create();
        string packPath = output.FullPath("assets.pck");

        Result result = Run(
            app,
            "bake",
            "--project-root",
            project.RootPath,
            "--output-root",
            output.RootPath,
            "--check");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.False(File.Exists(packPath));
        Assert.Contains("no pack written", result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(BakeManifestPaths.ManifestPath(project.RootPath)));
    }

    [Fact]
    public void ForceIsAcceptedAndStillBakes()
    {
        using var project = TestBakeProject.Create("cli-force");
        project.WriteStandardAssets();
        project.WriteTexture();
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);
        using var output = TempBakeDirectory.Create();

        Result first = Run(
            app,
            "bake",
            "--project-root",
            project.RootPath,
            "--output-root",
            output.RootPath);
        Result forced = Run(
            app,
            "bake",
            "--project-root",
            project.RootPath,
            "--output-root",
            output.RootPath,
            "--force");

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, forced.ExitCode);
        Assert.Contains("baked '", first.Output, StringComparison.Ordinal);
        Assert.Contains("baked '", forced.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseModeIsReportedAndAccepted()
    {
        using var project = TestBakeProject.Create("cli-release");
        project.WriteStandardAssets();
        project.WriteTexture();
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);
        using var output = TempBakeDirectory.Create();

        Result result = Run(
            app,
            "bake",
            "--project-root",
            project.RootPath,
            "--output-root",
            output.RootPath,
            "--mode",
            "release");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.Contains("release mode", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidModeIsAUsageError()
    {
        using var project = TestBakeProject.Create("cli-bad-mode");
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);

        Result result = Run(app, "bake", "--mode", "fast");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--mode must be 'debug' or 'release'", result.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public void InvalidSourceReturnsOneAndKeepsTheExistingPack()
    {
        using var project = TestBakeProject.Create("cli-invalid");
        project.WriteAsset("data/game.toml", "name = \n");
        project.WriteTexture();
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);
        using var output = TempBakeDirectory.Create();
        string packPath = output.FullPath("assets.pck");
        BakedPackAssertions.WriteValidPack(packPath);
        byte[] before = File.ReadAllBytes(packPath);

        Result result = Run(
            app,
            "bake",
            "--project-root",
            project.RootPath,
            "--output-root",
            output.RootPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error BK2001:", result.Error, StringComparison.Ordinal);
        Assert.Contains("no pack was written", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(packPath));
    }

    [Fact]
    public void CleanDeletesEveryConfiguredPack()
    {
        using var project = TestBakeProject.Create("cli-clean");
        project.WriteStandardAssets();
        project.WriteTexture();
        BakeApplication app = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")]);
        using var output = TempBakeDirectory.Create();

        Result bake = Run(
            app,
            "bake",
            "--project-root",
            project.RootPath,
            "--output-root",
            output.RootPath);
        Assert.Equal(0, bake.ExitCode);

        Result clean = Run(
            app,
            "clean",
            "--project-root",
            project.RootPath,
            "--output-root",
            output.RootPath);

        Assert.Equal(0, clean.ExitCode);
        Assert.Contains("cleaned 1 pack(s)", clean.Output, StringComparison.Ordinal);
        Assert.Contains("incremental", clean.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(output.FullPath("assets.pck")));
        Assert.False(Directory.Exists(BakeManifestPaths.Directory(project.RootPath)));
    }

    [Fact]
    public void CopyPacksCopiesTheConfiguredPackFromTheExecutableDirectory()
    {
        string packName = $"copy-{Guid.NewGuid():N}.pck";
        using var source = TempBakeDirectory.Create();
        Directory.CreateDirectory(source.FullPath("Assets"));
        BakeApplication app = BakeProjectFixture.CreateApplication(
            source.RootPath,
            [(packName, "Assets")]);
        using var destination = TempBakeDirectory.Create();
        string executableDirectory = AppContext.BaseDirectory;
        string sourcePack = Path.Combine(executableDirectory, packName);
        byte[] bytes = [1, 2, 3, 4];
        File.WriteAllBytes(sourcePack, bytes);

        try
        {
            Result result = Run(
                app,
                "copy-packs",
                "--output-root",
                destination.RootPath,
                "--project-root",
                source.RootPath);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(bytes, File.ReadAllBytes(destination.FullPath(packName)));
        }
        finally
        {
            File.Delete(sourcePack);
        }
    }

    [Fact]
    public void CopyPacksReportsAMissingPackAsBK5004()
    {
        string packName = $"missing-{Guid.NewGuid():N}.pck";
        using var source = TempBakeDirectory.Create();
        Directory.CreateDirectory(source.FullPath("Assets"));
        BakeApplication app = BakeProjectFixture.CreateApplication(
            source.RootPath,
            [(packName, "Assets")]);
        using var destination = TempBakeDirectory.Create();

        Result result = Run(
            app,
            "copy-packs",
            "--output-root",
            destination.RootPath,
            "--project-root",
            source.RootPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error BK5004:", result.Error, StringComparison.Ordinal);
        Assert.Contains(packName, result.Error, StringComparison.Ordinal);
    }

    private static Result Run(BakeApplication app, params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = app.Run(arguments, output, error, ProgramName);
        return new Result(exitCode, output.ToString(), error.ToString());
    }

    private sealed record Result(int ExitCode, string Output, string Error);
}
