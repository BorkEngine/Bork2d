using System.Text;
using Bork2d.Bake.Incremental;
using Xunit;

namespace Bork2d.Tests.Bake.Incremental;

/// <summary>
/// The schema-3 manifest model and its deterministic JSON/store contract: round-trip, sorted inputs,
/// stable bytes, forward-compatible unknown properties, and unusable missing/mistyped state.
/// </summary>
public sealed class BakeManifestTests
{
    private const string ToolVersion = "1.0.0";

    private static readonly string _hashA = new('A', 32);
    private static readonly string _hashB = new('B', 32);

    [Fact]
    public void SerializeRoundTripsEveryFieldAndSortsInputs()
    {
        BakeIncrementalManifest manifest = CreateManifest(
        [
            new BakeInputHash("z/input.toml", _hashB),
            new BakeInputHash("a/input.toml", _hashA),
        ]);

        byte[] bytes = BakeManifestJson.Serialize(manifest);

        Assert.True(BakeManifestJson.TryDeserialize(bytes, out BakeIncrementalManifest? parsed));
        Assert.NotNull(parsed);
        Assert.Equal(BakeIncrementalManifest.CurrentVersion, parsed.Version);
        Assert.Equal(ToolVersion, parsed.ToolVersion);
        Assert.Equal(_hashA, parsed.ConfigurationHash);
        Assert.Equal(_hashB, parsed.ShapeClosureHash);
        Assert.Equal("debug", parsed.Mode);
        Assert.Equal(2, parsed.AssetCount);
        Assert.Equal(1, parsed.TextureCount);
        Assert.Equal(3, parsed.ReferenceCount);
        Assert.Equal(
            new[] { "a/input.toml", "z/input.toml" },
            parsed.Inputs.Select(input => input.Path));
        Assert.Equal(new[] { @"/tmp/assets.pck" }, parsed.Outputs.Select(output => output.Path));
    }

    [Fact]
    public void SerializeIsDeterministicLfAndDeclaresSchemaThree()
    {
        BakeIncrementalManifest manifest = CreateManifest([new BakeInputHash("a/input.toml", _hashA)]);

        byte[] first = BakeManifestJson.Serialize(manifest);
        byte[] second = BakeManifestJson.Serialize(manifest);

        Assert.Equal(first, second);
        string text = Encoding.UTF8.GetString(first);
        Assert.Contains("\"schema\": 3", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\"inputs\"", text, StringComparison.Ordinal);
        Assert.Contains("\"outputs\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownExtraPropertiesAreIgnoredForForwardCompatibility()
    {
        byte[] bytes = BakeManifestJson.Serialize(CreateManifest([]));
        string text = Encoding.UTF8.GetString(bytes);
        string withExtra = text.Replace(
            "{\n",
            "{\n  \"future\": { \"value\": 42 },\n",
            StringComparison.Ordinal);

        Assert.True(
            BakeManifestJson.TryDeserialize(
                Encoding.UTF8.GetBytes(withExtra),
                out BakeIncrementalManifest? parsed));
        Assert.NotNull(parsed);
        Assert.Equal(ToolVersion, parsed.ToolVersion);
    }

    [Fact]
    public void MissingMistypedAndWrongSchemaStateMakesTheManifestUnusable()
    {
        byte[] valid = BakeManifestJson.Serialize(new BakeIncrementalManifest(
            ToolVersion,
            _hashA,
            _hashB,
            "debug",
            inputs: [],
            outputs: [],
            assetCount: 2,
            textureCount: 1,
            referenceCount: 3));
        string text = Encoding.UTF8.GetString(valid);

        byte[][] unusable =
        [
            Encoding.UTF8.GetBytes("{"),
            Encoding.UTF8.GetBytes("[]"),
            Encoding.UTF8.GetBytes(text.Replace("\"schema\": 3", "\"schema\": 2", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(text.Replace("\"toolVersion\"", "\"tool_version\"", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(text.Replace("\"assetCount\": 2", "\"assetCount\": \"2\"", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(text.Replace("\"mode\": \"debug\"", "\"mode\": 7", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(text.Replace("\"inputs\": []", "\"inputs\": {}", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(
                text.Replace(
                    "\"outputs\": []",
                    "\"outputs\": { \"path\": \"x\", \"hash\": \"y\" }",
                    StringComparison.Ordinal)),
        ];

        foreach (byte[] bytes in unusable)
        {
            Assert.False(BakeManifestJson.TryDeserialize(bytes, out _));
        }
    }

    [Fact]
    public void StoreReadsWhatItWritesAndTreatsCorruptStateAsUnusable()
    {
        using var directory = TempBakeDirectory.Create();
        string path = directory.FullPath("nested/manifest.json");

        Assert.Null(BakeManifestStore.TryRead(path));
        Assert.True(BakeManifestStore.TryWrite(path, CreateManifest([])));

        BakeIncrementalManifest? written = BakeManifestStore.TryRead(path);
        Assert.NotNull(written);
        Assert.Equal(ToolVersion, written.ToolVersion);

        File.WriteAllText(path, "{ not json");
        Assert.Null(BakeManifestStore.TryRead(path));
    }

    private static BakeIncrementalManifest CreateManifest(IReadOnlyList<BakeInputHash> inputs)
        => new(
            ToolVersion,
            _hashA,
            _hashB,
            "debug",
            inputs,
            [new BakeOutputHash(@"/tmp/assets.pck", _hashB)],
            assetCount: 2,
            textureCount: 1,
            referenceCount: 3);
}
