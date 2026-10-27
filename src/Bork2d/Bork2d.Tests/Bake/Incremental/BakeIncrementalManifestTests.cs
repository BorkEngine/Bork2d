using Bork2d.Bake.Baking;
using Bork2d.Bake.Incremental;
using Xunit;

namespace Bork2d.Tests.Bake.Incremental;

/// <summary>
/// The incremental manifest and skip rule: JSON round-trip, deterministic input order, every recorded
/// pack output, and each field of the skip decision (descriptor, inputs, common closure, host, tool,
/// mode, outputs, pack hashes) invalidating a recorded manifest.
/// </summary>
public sealed class BakeIncrementalManifestTests
{
    private const string PackHash = "PACKHASH";
    private const string DlcHash = "DLCHASH";
    private static string PackPath => Path.Combine("game", "out", "assets.pck");
    private static string DlcPath => Path.Combine("game", "out", "dlc1.pck");

    [Fact]
    public void JsonRoundTripsEveryRecordedField()
    {
        BakeIncrementalManifest manifest = CreateManifest();

        byte[] json = BakeManifestJson.Serialize(manifest);
        BakeIncrementalManifest? parsed = BakeManifestJson.TryDeserialize(json);

        Assert.NotNull(parsed);
        Assert.Equal(manifest.Version, parsed.Version);
        Assert.Equal(manifest.ToolVersion, parsed.ToolVersion);
        Assert.Equal(manifest.DescriptorHash, parsed.DescriptorHash);
        Assert.Equal(manifest.CommonAssemblyHash, parsed.CommonAssemblyHash);
        Assert.Equal(manifest.HostHash, parsed.HostHash);
        Assert.Equal(manifest.Mode, parsed.Mode);
        Assert.Equal(manifest.Outputs, parsed.Outputs);
        Assert.Equal(manifest.AssetCount, parsed.AssetCount);
        Assert.Equal(manifest.TextureCount, parsed.TextureCount);
        Assert.Equal(manifest.ReferenceCount, parsed.ReferenceCount);
        Assert.Equal(manifest.Inputs, parsed.Inputs);

        string text = System.Text.Encoding.UTF8.GetString(json);
        Assert.StartsWith("{", text, StringComparison.Ordinal);
        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        Assert.Equal(json, BakeManifestJson.Serialize(parsed));
    }

    [Fact]
    public void ParsingRejectsCorruptOrIncompleteJson()
    {
        Assert.Null(BakeManifestJson.TryDeserialize("not json"u8.ToArray()));
        Assert.Null(BakeManifestJson.TryDeserialize("[]"u8.ToArray()));
        Assert.Null(BakeManifestJson.TryDeserialize("{}"u8.ToArray()));
        Assert.Null(BakeManifestJson.TryDeserialize(
            """{"version":2,"toolVersion":"1.0.0"}"""u8.ToArray()));
    }

    [Fact]
    public void MissingOrCorruptManifestReadsAsNoManifest()
    {
        using var directory = TempBakeDirectory.Create();
        string manifestPath = directory.FullPath("manifest.json");

        Assert.Null(BakeManifestStore.TryRead(manifestPath));

        File.WriteAllText(manifestPath, "{ broken");
        Assert.Null(BakeManifestStore.TryRead(manifestPath));

        File.WriteAllBytes(manifestPath, [0x00, 0xFF]);
        Assert.Null(BakeManifestStore.TryRead(manifestPath));
    }

    [Fact]
    public void WritingThenReadingAManifestIsStable()
    {
        using var directory = TempBakeDirectory.Create();
        string manifestPath = directory.FullPath(Path.Combine("nested", "manifest.json"));
        var diagnostics = new Bork2d.Bake.Diagnostics.BakeDiagnostics();

        Assert.True(BakeManifestStore.TryWrite(manifestPath, CreateManifest(), diagnostics));
        Assert.Empty(diagnostics.GetSorted());

        BakeIncrementalManifest? read = BakeManifestStore.TryRead(manifestPath);
        Assert.NotNull(read);
        Assert.Equal(CreateManifest().Inputs, read.Inputs);
        Assert.Equal(CreateManifest().Outputs, read.Outputs);
    }

    [Fact]
    public void MatchingManifestSkips()
    {
        Assert.True(BakeIncrementalPlanner.Matches(
            CreateManifest(),
            CreateInputs(),
            BakeToolInfo.Version,
            CreateOutputHashes()));
    }

    [Fact]
    public void EveryChangedInputInvalidatesTheSkip()
    {
        BakeIncrementalInputs current = CreateInputs();
        IReadOnlyList<BakeOutputHash> outputHashes = CreateOutputHashes();

        Assert.False(BakeIncrementalPlanner.Matches(
            CreateManifest(version: BakeIncrementalManifest.CurrentVersion + 1),
            current,
            BakeToolInfo.Version,
            outputHashes));
        Assert.False(BakeIncrementalPlanner.Matches(
            CreateManifest(toolVersion: "0.0.1"),
            current,
            BakeToolInfo.Version,
            outputHashes));
        Assert.False(BakeIncrementalPlanner.Matches(
            CreateManifest(descriptorHash: "OTHER"),
            current,
            BakeToolInfo.Version,
            outputHashes));
        Assert.False(BakeIncrementalPlanner.Matches(
            CreateManifest(commonAssemblyHash: "OTHER"),
            current,
            BakeToolInfo.Version,
            outputHashes));
        Assert.False(BakeIncrementalPlanner.Matches(
            CreateManifest(hostHash: "OTHER"),
            current,
            BakeToolInfo.Version,
            outputHashes));
        Assert.False(BakeIncrementalPlanner.Matches(
            CreateManifest(mode: "release"),
            current,
            BakeToolInfo.Version,
            outputHashes));
        Assert.False(BakeIncrementalPlanner.Matches(
            CreateManifest(outputs:
            [
                new BakeOutputHash(Path.Combine("game", "out", "other.pck"), PackHash),
            ]),
            current,
            BakeToolInfo.Version,
            outputHashes));
        Assert.False(BakeIncrementalPlanner.Matches(
            CreateManifest(outputs:
            [
                new BakeOutputHash(PackPath, "OTHER"),
                new BakeOutputHash(DlcPath, DlcHash),
            ]),
            current,
            BakeToolInfo.Version,
            outputHashes));
        Assert.False(BakeIncrementalPlanner.Matches(
            CreateManifest(outputs: [new BakeOutputHash(PackPath, PackHash)]),
            current,
            BakeToolInfo.Version,
            outputHashes));
        Assert.False(BakeIncrementalPlanner.Matches(
            CreateManifest(inputs:
            [
                new BakeInputHash("assets/a.toml", "A"),
                new BakeInputHash("assets/c.toml", "C"),
            ]),
            current,
            BakeToolInfo.Version,
            outputHashes));
        Assert.False(BakeIncrementalPlanner.Matches(
            CreateManifest(inputs:
            [
                new BakeInputHash("assets/a.toml", "A"),
                new BakeInputHash("assets/b.toml", "C"),
            ]),
            current,
            BakeToolInfo.Version,
            outputHashes));
        Assert.False(BakeIncrementalPlanner.Matches(
            CreateManifest(inputs:
            [
                new BakeInputHash("assets/a.toml", "A"),
                new BakeInputHash("assets/b.toml", "B"),
                new BakeInputHash("assets/c.toml", "C"),
            ]),
            current,
            BakeToolInfo.Version,
            outputHashes));
        Assert.False(BakeIncrementalPlanner.Matches(
            CreateManifest(),
            current,
            BakeToolInfo.Version,
            [new BakeOutputHash(PackPath, "OTHER"), new BakeOutputHash(DlcPath, DlcHash)]));
    }

    [Fact]
    public void InputsAreHeldInOrdinalOrderAndOutputsKeepTheirOrder()
    {
        var current = new BakeIncrementalInputs(
            "DESC",
            [new BakeInputHash("assets/b.toml", "B"), new BakeInputHash("assets/a.toml", "A")],
            "COMMON",
            "HOST",
            "debug",
            [PackPath, DlcPath]);

        Assert.Equal(
            new[] { "assets/b.toml", "assets/a.toml" },
            current.Inputs.Select(input => input.Path));
        Assert.Equal(new[] { PackPath, DlcPath }, current.Outputs);
        Assert.Equal("debug", BakeIncrementalInputs.ModeName(BakeMode.Debug));
        Assert.Equal("release", BakeIncrementalInputs.ModeName(BakeMode.Release));
    }

    private static BakeIncrementalManifest CreateManifest(
        int? version = null,
        string? toolVersion = null,
        string? descriptorHash = null,
        IEnumerable<BakeInputHash>? inputs = null,
        string? commonAssemblyHash = null,
        string? hostHash = null,
        string? mode = null,
        IEnumerable<BakeOutputHash>? outputs = null)
        => new(
            version ?? BakeIncrementalManifest.CurrentVersion,
            toolVersion ?? BakeToolInfo.Version,
            descriptorHash ?? "DESC",
            inputs ??
            [
                new BakeInputHash("assets/a.toml", "A"),
                new BakeInputHash("assets/b.toml", "B"),
            ],
            commonAssemblyHash ?? "COMMON",
            hostHash ?? "HOST",
            mode ?? "debug",
            outputs ?? CreateOutputHashes(),
            3,
            1,
            2);

    private static BakeIncrementalInputs CreateInputs()
        => new(
            "DESC",
            [
                new BakeInputHash("assets/a.toml", "A"),
                new BakeInputHash("assets/b.toml", "B"),
            ],
            "COMMON",
            "HOST",
            "debug",
            [PackPath, DlcPath]);

    private static BakeOutputHash[] CreateOutputHashes()
        =>
        [
            new BakeOutputHash(PackPath, PackHash),
            new BakeOutputHash(DlcPath, DlcHash),
        ];
}
