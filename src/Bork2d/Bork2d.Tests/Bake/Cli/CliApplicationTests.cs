using Bork2d.Bake.Cli;
using Xunit;

namespace Bork2d.Tests.Bake.Cli;

public sealed class CliApplicationTests
{
    [Fact]
    public void HelpWritesUsageToStdoutAndReturnsZero()
    {
        var result = Run("--help");

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("usage: bork2d-bake", result.Output, StringComparison.Ordinal);
        Assert.Contains("validate --project", result.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void VersionWritesTheToolVersionToStdout()
    {
        var result = Run("--version");

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.StartsWith("bork2d-bake ", result.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void NoArgumentsIsAUsageError()
    {
        var result = Run();

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Equal(string.Empty, result.Output);
        Assert.Contains("a command is required", result.Error, StringComparison.Ordinal);
        Assert.Contains("usage: bork2d-bake", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCommandIsAUsageError()
    {
        var result = Run("update");

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("unknown command 'update'", result.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public void ValidateRequiresProject()
    {
        var result = Run("validate");

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("--project", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRejectsUnknownOptions()
    {
        var result = Run("validate", "--json");

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("--json", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRejectsVerboseAndQuietTogether()
    {
        using var directory = TempBakeDirectory.Create();
        string descriptorPath = WriteValidProject(directory);

        var result = Run(
            "validate",
            "--project",
            descriptorPath,
            "--verbose",
            "--quiet");

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("mutually exclusive", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidDescriptorReturnsZeroWithTheSummaryOnStdout()
    {
        using var directory = TempBakeDirectory.Create();
        string descriptorPath = WriteValidProject(directory);

        var result = Run("validate", "--project", descriptorPath);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("is valid", result.Output, StringComparison.Ordinal);
        Assert.Contains(directory.RootPath, result.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void RelativeProjectPathResolvesAgainstTheProcessCurrentDirectory()
    {
        using var directory = TempBakeDirectory.CreateUnderCurrentDirectory();
        string descriptorPath = WriteValidProject(directory);
        string relativePath = Path.GetRelativePath(Environment.CurrentDirectory, descriptorPath);
        Assert.False(Path.IsPathRooted(relativePath));

        var result = Run("validate", "--project", relativePath);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("is valid", result.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public void VerboseListsTheDeclaredSourceTypes()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        string descriptorPath = directory.WriteFile(
            "bork2d.project.toml",
            """
            schema = 1

            [assets]
            root = "Assets"
            """);
        directory.WriteFile(
            "Assets/data/game.toml",
            """
            value = 1

            [bork2d]
            type = "MyGame.Data.GameConfig"
            """);
        directory.WriteFile(
            "Assets/sprites/player.toml",
            """
            value = 1

            [bork2d]
            type = "Bork2d.Api.Assets.SpriteAsset"
            """);

        var result = Run("validate", "--project", descriptorPath, "--verbose");

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("data/game -> MyGame.Data.GameConfig", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "sprites/player -> Bork2d.Api.Assets.SpriteAsset",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidDescriptorReturnsUsageErrorAndRoutesDiagnosticsToStderr()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        string descriptorPath = directory.WriteFile(
            "bork2d.project.toml",
            """
            schema = 1

            [assets]
            root = "Assets"
            unexpected = true
            """);

        var result = Run("validate", "--project", descriptorPath);

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("error BK1004:", result.Error, StringComparison.Ordinal);
        Assert.Contains("unexpected", result.Error, StringComparison.Ordinal);
        Assert.Contains("(5,1)", result.Error, StringComparison.Ordinal);
        Assert.Contains("is invalid", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void QuietSuppressesTheInvalidSummaryButNotTheDiagnostic()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        string descriptorPath = directory.WriteFile(
            "bork2d.project.toml",
            "schema = 2\n\n[assets]\nroot = \"Assets\"\n");

        var result = Run("validate", "--project", descriptorPath, "--quiet");

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Equal(string.Empty, result.Output);
        Assert.Contains("error BK1003:", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingDescriptorFileIsAUsageError()
    {
        using var directory = TempBakeDirectory.Create();

        var result = Run(
            "validate",
            "--project",
            directory.FullPath("missing.toml"));

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("error BK1001:", result.Error, StringComparison.Ordinal);
        Assert.Contains("is invalid", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void DescriptorSyntaxErrorIsRenderedCanonically()
    {
        using var directory = TempBakeDirectory.Create();
        string descriptorPath = directory.WriteFile(
            "bork2d.project.toml",
            "schema =");

        var result = Run("validate", "--project", descriptorPath);

        Assert.Equal(CliApplication.UsageExitCode, result.ExitCode);
        Assert.Contains("bork2d.project.toml(1,", result.Error, StringComparison.Ordinal);
        Assert.Contains("error BK2001:", result.Error, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output, string Error) Run(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = CliApplication.Run(arguments, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string WriteValidProject(TempBakeDirectory directory)
    {
        directory.CreateDirectory("Assets");
        return directory.WriteFile(
            "bork2d.project.toml",
            """
            schema = 1

            [assets]
            root = "Assets"
            """);
    }
}
