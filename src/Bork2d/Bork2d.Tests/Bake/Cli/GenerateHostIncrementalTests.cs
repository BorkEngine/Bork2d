using Bork2d.Bake.Cli;
using Bork2d.Bake.Incremental;
using Bork2d.Tests.Bake.Hosting;
using Xunit;

namespace Bork2d.Tests.Bake.Cli;

/// <summary>
/// The incremental <c>generate-host</c> contract: the state file, the unchanged-input skip,
/// <c>--force</c>, a corrupt state, an externally modified generated file, a descriptor change, and a
/// <c>Project.Common</c> change.
/// </summary>
[Collection(BakeHostFixtureCollection.Name)]
public sealed class GenerateHostIncrementalTests
{
    private readonly TempBakeFixture _fixture;

    /// <summary>Initializes a new instance of the <see cref="GenerateHostIncrementalTests"/> class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public GenerateHostIncrementalTests(TempBakeFixture fixture) => _fixture = fixture;

    [Fact]
    public void UnchangedInputsSkipAndRecordTheState()
    {
        string output = CreateOutputDirectory();
        string statePath = StatePath();

        Result first = Generate(_fixture.DescriptorPath, _fixture.ProjectCommonAssemblyPath, output);

        Assert.Equal(CliApplication.SuccessExitCode, first.ExitCode);
        Assert.Contains("generated '", first.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, first.Error);
        Assert.True(File.Exists(statePath));

        BakeHostGenerationState state = Assert.IsType<BakeHostGenerationState>(
            BakeHostGenerationStore.TryRead(statePath));
        Assert.Equal(BakeHostGenerationState.CurrentVersion, state.Version);
        Assert.Equal(BakeToolInfo.Version, state.GeneratorVersion);
        Assert.Equal(BakeHash.HashFile(_fixture.DescriptorPath), state.DescriptorHash);
        Assert.Equal(BakeHash.HashFile(_fixture.ProjectCommonAssemblyPath), state.CommonAssemblyHash);
        Assert.Equal("Debug", state.Configuration);
        Assert.Equal(Path.GetFullPath(output), state.OutputDirectory);
        Assert.Equal(
            BakeHash.HashFields(
                File.ReadAllBytes(Path.Combine(output, "BakeHost.csproj")),
                File.ReadAllBytes(Path.Combine(output, "BakeManifest.g.cs"))),
            state.GeneratedHash);

        DateTime projectWriteTime = File.GetLastWriteTimeUtc(
            Path.Combine(output, "BakeHost.csproj"));
        byte[] stateBytes = File.ReadAllBytes(statePath);

        Result second = Generate(_fixture.DescriptorPath, _fixture.ProjectCommonAssemblyPath, output);

        Assert.Equal(CliApplication.SuccessExitCode, second.ExitCode);
        Assert.Contains("up to date", second.Output, StringComparison.Ordinal);
        Assert.Equal(
            projectWriteTime,
            File.GetLastWriteTimeUtc(Path.Combine(output, "BakeHost.csproj")));
        Assert.Equal(stateBytes, File.ReadAllBytes(statePath));
    }

    [Fact]
    public void ForceRegenerates()
    {
        string output = CreateOutputDirectory();
        Assert.Equal(
            CliApplication.SuccessExitCode,
            Generate(_fixture.DescriptorPath, _fixture.ProjectCommonAssemblyPath, output).ExitCode);

        Result forced = Generate(
            _fixture.DescriptorPath,
            _fixture.ProjectCommonAssemblyPath,
            output,
            "--force");

        Assert.Equal(CliApplication.SuccessExitCode, forced.ExitCode);
        Assert.Contains("generated '", forced.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptStateRegenerates()
    {
        string output = CreateOutputDirectory();
        Assert.Equal(
            CliApplication.SuccessExitCode,
            Generate(_fixture.DescriptorPath, _fixture.ProjectCommonAssemblyPath, output).ExitCode);
        string statePath = StatePath();
        File.WriteAllText(statePath, "{ not state");

        Result result = Generate(
            _fixture.DescriptorPath,
            _fixture.ProjectCommonAssemblyPath,
            output);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.DoesNotContain("not state", File.ReadAllText(statePath), StringComparison.Ordinal);
        Assert.NotNull(BakeHostGenerationStore.TryRead(statePath));
        Assert.True(File.Exists(Path.Combine(output, "BakeHost.csproj")));
        Assert.True(File.Exists(Path.Combine(output, "BakeManifest.g.cs")));
    }

    [Fact]
    public void ExternallyModifiedGeneratedFileIsRewritten()
    {
        string output = CreateOutputDirectory();
        Assert.Equal(
            CliApplication.SuccessExitCode,
            Generate(_fixture.DescriptorPath, _fixture.ProjectCommonAssemblyPath, output).ExitCode);
        string manifestPath = Path.Combine(output, "BakeManifest.g.cs");
        File.AppendAllText(manifestPath, "// externally modified\n");

        Result result = Generate(
            _fixture.DescriptorPath,
            _fixture.ProjectCommonAssemblyPath,
            output);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Contains("generated '", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "externally modified",
            File.ReadAllText(manifestPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DescriptorChangeRewritesTheGeneratedManifest()
    {
        string project = Path.Combine(
            Path.GetTempPath(),
            $"bork2d-bake-host-inc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        string descriptorPath = Path.Combine(project, TempBakeFixture.DescriptorFileName);
        File.WriteAllText(descriptorPath, TempBakeFixture.DescriptorText);
        string output = CreateOutputDirectory();

        Result first = Generate(descriptorPath, _fixture.ProjectCommonAssemblyPath, output);

        Assert.Equal(CliApplication.SuccessExitCode, first.ExitCode);
        Assert.Contains("generated '", first.Output, StringComparison.Ordinal);
        string manifestPath = Path.Combine(output, "BakeManifest.g.cs");
        Assert.DoesNotContain("\"more\"", File.ReadAllText(manifestPath), StringComparison.Ordinal);

        File.WriteAllText(
            descriptorPath,
            TempBakeFixture.DescriptorText
                + "\n[[types]]\nprefix = \"more\"\ntype = \"Game.Data.Weapon\"\n");

        Result changed = Generate(descriptorPath, _fixture.ProjectCommonAssemblyPath, output);

        Assert.Equal(CliApplication.SuccessExitCode, changed.ExitCode);
        Assert.Contains("generated '", changed.Output, StringComparison.Ordinal);
        Assert.Contains("\"more\"", File.ReadAllText(manifestPath), StringComparison.Ordinal);
    }

    [Fact]
    public void CommonAssemblyChangeInvalidatesTheState()
    {
        string output = CreateOutputDirectory();
        Assert.Equal(
            CliApplication.SuccessExitCode,
            Generate(_fixture.DescriptorPath, _fixture.ProjectCommonAssemblyPath, output).ExitCode);
        string statePath = StatePath();
        string commonPath = _fixture.ProjectCommonAssemblyPath;
        string previousHash = Assert.IsType<BakeHostGenerationState>(
            BakeHostGenerationStore.TryRead(statePath)).CommonAssemblyHash;

        TempProcessRunner.Result build = TempProcessRunner.Run(
            "dotnet",
            [
                "build",
                Path.Combine(_fixture.RootPath, "Project.Common", "Project.Common.csproj"),
                "-c",
                "Debug",
                "-p:Version=2.0.0",
                "-nologo",
            ],
            _fixture.RootPath);
        Assert.True(build.ExitCode == 0, build.Output);

        string rebuiltHash = BakeHash.HashFile(commonPath);
        Assert.NotEqual(previousHash, rebuiltHash);

        Result result = Generate(_fixture.DescriptorPath, commonPath, output);

        Assert.Equal(CliApplication.SuccessExitCode, result.ExitCode);
        Assert.Equal(
            rebuiltHash,
            Assert.IsType<BakeHostGenerationState>(
                BakeHostGenerationStore.TryRead(StatePath())).CommonAssemblyHash);
    }

    private string StatePath()
        => BakeManifestPaths.HostGenerationStatePath(_fixture.RootPath);

    private static string CreateOutputDirectory()
        => Path.Combine(Path.GetTempPath(), $"bork2d-bake-host-inc-out-{Guid.NewGuid():N}");

    private static Result Generate(
        string projectPath,
        string commonPath,
        string output,
        params string[] extra)
        => Run(
        [
            "generate-host",
            "--project",
            projectPath,
            "--common",
            commonPath,
            "--output",
            output,
            .. extra,
        ]);

    private static Result Run(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = CliApplication.Run(arguments, output, error);
        return new Result(exitCode, output.ToString(), error.ToString());
    }

    private sealed record Result(int ExitCode, string Output, string Error);
}
