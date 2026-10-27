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
        Assert.False(string.IsNullOrEmpty(state.SourceTypesHash));
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
    public void SourceTypeAddChangeRemoveRewritesTheGeneratedManifest()
    {
        string project = Path.Combine(
            Path.GetTempPath(),
            $"bork2d-bake-host-inc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(project, "Assets", "data"));
        string descriptorPath = Path.Combine(project, TempBakeFixture.DescriptorFileName);
        File.WriteAllText(
            descriptorPath,
            """
            schema = 1

            [[assets]]
            root = "Assets"
            pack = "assets.pck"
            """);
        string sourcePath = Path.Combine(project, "Assets", "data", "source.toml");
        File.WriteAllText(
            sourcePath,
            """
            value = 1

            [bork2d]
            type = "BakeFixture.Common.GameConfig"
            """);
        string output = CreateOutputDirectory();

        Result first = Generate(descriptorPath, _fixture.ProjectCommonAssemblyPath, output);

        Assert.Equal(CliApplication.SuccessExitCode, first.ExitCode);
        Assert.Contains("generated '", first.Output, StringComparison.Ordinal);
        string manifestPath = Path.Combine(output, "BakeManifest.g.cs");
        Assert.Contains(
            "\"BakeFixture.Common.GameConfig\"",
            File.ReadAllText(manifestPath),
            StringComparison.Ordinal);

        string otherSource = Path.Combine(project, "Assets", "weapons", "sword.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(otherSource)!);
        File.WriteAllText(
            otherSource,
            """
            name = "sword"
            damage = 1

            [bork2d]
            type = "Game.Data.Weapon"
            """);

        Result added = Generate(descriptorPath, _fixture.ProjectCommonAssemblyPath, output);

        Assert.Equal(CliApplication.SuccessExitCode, added.ExitCode);
        Assert.Contains("\"Game.Data.Weapon\"", File.ReadAllText(manifestPath), StringComparison.Ordinal);

        File.WriteAllText(
            sourcePath,
            """
            attack = 1
            defense = 2

            [bork2d]
            type = "BakeFixture.Common.StatsRoot"
            """);

        Result changed = Generate(descriptorPath, _fixture.ProjectCommonAssemblyPath, output);

        Assert.Equal(CliApplication.SuccessExitCode, changed.ExitCode);
        string changedManifest = File.ReadAllText(manifestPath);
        Assert.Contains("\"BakeFixture.Common.StatsRoot\"", changedManifest, StringComparison.Ordinal);
        Assert.DoesNotContain("\"BakeFixture.Common.GameConfig\"", changedManifest, StringComparison.Ordinal);

        File.Delete(sourcePath);

        Result removed = Generate(descriptorPath, _fixture.ProjectCommonAssemblyPath, output);

        Assert.Equal(CliApplication.SuccessExitCode, removed.ExitCode);
        string removedManifest = File.ReadAllText(manifestPath);
        Assert.DoesNotContain("\"BakeFixture.Common.StatsRoot\"", removedManifest, StringComparison.Ordinal);
        Assert.Contains("\"Game.Data.Weapon\"", removedManifest, StringComparison.Ordinal);
    }

    [Fact]
    public void MovingASourceBetweenPackRootsRegeneratesTheHost()
    {
        string project = Path.Combine(
            Path.GetTempPath(),
            $"bork2d-bake-host-packs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(project, "Assets", "data"));
        Directory.CreateDirectory(Path.Combine(project, "DLC1", "data"));
        string descriptorPath = Path.Combine(project, TempBakeFixture.DescriptorFileName);
        File.WriteAllText(
            descriptorPath,
            """
            schema = 1

            [[assets]]
            root = "Assets"
            pack = "assets.pck"

            [[assets]]
            root = "DLC1"
            pack = "dlc1.pck"
            """);
        string source = """
            value = 1

            [bork2d]
            type = "BakeFixture.Common.GameConfig"
            """;
        string sourcePath = Path.Combine(project, "Assets", "data", "source.toml");
        File.WriteAllText(sourcePath, source);
        string output = CreateOutputDirectory();

        Assert.Equal(
            CliApplication.SuccessExitCode,
            Generate(descriptorPath, _fixture.ProjectCommonAssemblyPath, output).ExitCode);
        Result unchanged = Generate(descriptorPath, _fixture.ProjectCommonAssemblyPath, output);
        Assert.Contains("up to date", unchanged.Output, StringComparison.Ordinal);
        string statePath = BakeManifestPaths.HostGenerationStatePath(project);
        string beforeHash = Assert.IsType<BakeHostGenerationState>(
            BakeHostGenerationStore.TryRead(statePath)).SourceTypesHash;

        // The asset path and type are unchanged; only the pack/root identity changes. Without pack
        // identity in the source-type hash this would incorrectly stay current (D-M4-14).
        File.Delete(sourcePath);
        string movedPath = Path.Combine(project, "DLC1", "data", "source.toml");
        File.WriteAllText(movedPath, source);

        Result moved = Generate(descriptorPath, _fixture.ProjectCommonAssemblyPath, output);

        Assert.Equal(CliApplication.SuccessExitCode, moved.ExitCode);
        BakeHostGenerationState state = Assert.IsType<BakeHostGenerationState>(
            BakeHostGenerationStore.TryRead(statePath));
        Assert.NotEqual(beforeHash, state.SourceTypesHash);
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
