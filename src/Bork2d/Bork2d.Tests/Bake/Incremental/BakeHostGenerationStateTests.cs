using Bork2d.Bake.Incremental;
using Xunit;

namespace Bork2d.Tests.Bake.Incremental;

/// <summary>
/// The host-generation state model and store: JSON round-trip, unusable state reading as absent, and
/// the recorded content hash of the generated files.
/// </summary>
public sealed class BakeHostGenerationStateTests
{
    [Fact]
    public void JsonRoundTripsEveryRecordedField()
    {
        BakeHostGenerationState state = CreateState();

        byte[] json = BakeHostGenerationStateJson.Serialize(state);
        BakeHostGenerationState? parsed = BakeHostGenerationStateJson.TryDeserialize(json);

        Assert.NotNull(parsed);
        Assert.Equal(state.Version, parsed.Version);
        Assert.Equal(state.GeneratorVersion, parsed.GeneratorVersion);
        Assert.Equal(state.DescriptorHash, parsed.DescriptorHash);
        Assert.Equal(state.CommonAssemblyHash, parsed.CommonAssemblyHash);
        Assert.Equal(state.SourceTypesHash, parsed.SourceTypesHash);
        Assert.Equal(state.Configuration, parsed.Configuration);
        Assert.Equal(state.OutputDirectory, parsed.OutputDirectory);
        Assert.Equal(state.ProjectPath, parsed.ProjectPath);
        Assert.Equal(state.ManifestPath, parsed.ManifestPath);
        Assert.Equal(state.GeneratedHash, parsed.GeneratedHash);
        Assert.Equal(json, BakeHostGenerationStateJson.Serialize(parsed));
    }

    [Fact]
    public void ParsingRejectsCorruptOrIncompleteJson()
    {
        Assert.Null(BakeHostGenerationStateJson.TryDeserialize("not json"u8.ToArray()));
        Assert.Null(BakeHostGenerationStateJson.TryDeserialize("{}"u8.ToArray()));
        Assert.Null(BakeHostGenerationStateJson.TryDeserialize(
            """{"version":1,"generatorVersion":"1.0.0","descriptorHash":"D"}"""u8.ToArray()));
    }

    [Fact]
    public void MissingOrCorruptStateReadsAsNoState()
    {
        using var directory = TempBakeDirectory.Create();
        string statePath = directory.FullPath("host-generation.json");

        Assert.Null(BakeHostGenerationStore.TryRead(statePath));

        File.WriteAllText(statePath, "not json");
        Assert.Null(BakeHostGenerationStore.TryRead(statePath));
    }

    [Fact]
    public void WritingThenReadingTheStateIsStable()
    {
        using var directory = TempBakeDirectory.Create();
        string statePath = directory.FullPath(Path.Combine("nested", "host-generation.json"));
        var diagnostics = new Bork2d.Bake.Diagnostics.BakeDiagnostics();

        Assert.True(BakeHostGenerationStore.TryWrite(statePath, CreateState(), diagnostics));
        Assert.Empty(diagnostics.GetSorted());

        BakeHostGenerationState? read = BakeHostGenerationStore.TryRead(statePath);
        Assert.NotNull(read);
        Assert.Equal(CreateState().GeneratedHash, read.GeneratedHash);
        Assert.Equal(CreateState().CommonAssemblyHash, read.CommonAssemblyHash);
    }

    private static BakeHostGenerationState CreateState()
        => new(
            BakeHostGenerationState.CurrentVersion,
            BakeToolInfo.Version,
            "DESCRIPTOR",
            "COMMON",
            "SOURCETYPES",
            "Debug",
            Path.Combine("game", "obj", "bork2d", "bakehost"),
            Path.Combine("game", "obj", "bork2d", "bakehost", "BakeHost.csproj"),
            Path.Combine("game", "obj", "bork2d", "bakehost", "BakeManifest.g.cs"),
            "GENERATED");
}
