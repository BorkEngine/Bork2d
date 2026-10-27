using Bork2d.Api.Assets;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Processors;
using Bork2d.Bake.Sources;
using Bork2d.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// The generic baked index and reference validator for processor output: cross-source collisions are
/// <c>BK3005</c> and processor <c>AssetRef</c>/<c>DataRef</c> sites get the same canonical, present,
/// kind, and target-type checks as data references, with DLC/main scoping and no source spans.
/// </summary>
public sealed class BakeProcessorIndexTests
{
    private static readonly string _projectDirectory = Path.Combine(
        Path.GetTempPath(),
        "bork2d-processor-index-tests");

    private static readonly string _gameConfigType = typeof(Bork2d.BakeFixture.GameConfig).FullName!;
    private static readonly string _statsRootType = typeof(Bork2d.BakeFixture.StatsRoot).FullName!;

    [Fact]
    public void ProcessorAssetCollidingWithCustomDataIsBK3005()
    {
        var diagnostics = new BakeDiagnostics();

        BakedAssetIndex index = BakedAssetIndex.Build(
            [Custom("data/proc", _gameConfigType)],
            [ProcessorAsset("data/proc", _statsRootType)],
            [],
            _projectDirectory,
            diagnostics);

        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.AssetPathCollision, diagnostic.Code);
        Assert.Equal("data/proc", diagnostic.AssetPath);
        Assert.Single(index.Entries);
    }

    [Fact]
    public void ProcessorAssetCollidingWithATextureDescriptorIsBK3005()
    {
        var diagnostics = new BakeDiagnostics();

        _ = BakedAssetIndex.Build(
            [],
            [ProcessorAsset("art/player", _statsRootType)],
            [Texture("art/player")],
            _projectDirectory,
            diagnostics);

        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.AssetPathCollision, diagnostic.Code);
        Assert.Equal("art/player", diagnostic.AssetPath);
    }

    [Fact]
    public void ProcessorDataCollidingWithATexturePayloadIsBK3005()
    {
        var diagnostics = new BakeDiagnostics();

        BakedAssetIndex index = BakedAssetIndex.Build(
            [],
            [ProcessorData("art/player-raw")],
            [Texture("art/player")],
            _projectDirectory,
            diagnostics);

        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.AssetPathCollision, diagnostic.Code);
        Assert.Contains("ProcessorData", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("TexturePayload", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(
            index.Entries,
            entry => entry.Path == "art/player-raw"
                && entry.Kind == BakedAssetEntryKind.ProcessorData);
    }

    [Fact]
    public void ValidProcessorReferencesResolveToTextureAndRawEntries()
    {
        var diagnostics = new BakeDiagnostics();
        BakedAssetIndex index = ProcessorReferenceIndex(
            new ProcessorRefAsset
            {
                Texture = "art/player",
                Payload = "art/player-raw",
            },
            out BakeProcessorOutput output,
            diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.Equal(2, output.References.Count);
        Assert.True(BakeReferenceValidator.Validate(index, diagnostics));
        Assert.Empty(diagnostics.GetSorted());
    }

    [Fact]
    public void ProcessorReferenceToANonCanonicalPathIsBK3001WithoutASpan()
    {
        BakeDiagnostic diagnostic = SingleProcessorReferenceError(
            new ProcessorRefAsset { Texture = "Art/Player" },
            BakeDiagnosticCodes.ReferenceNotCanonical);

        Assert.Equal("Assets/data/proc.ref", diagnostic.FilePath);
        Assert.Null(diagnostic.Line);
        Assert.Null(diagnostic.Column);
        Assert.Equal("Texture", diagnostic.MemberPath);
        Assert.Contains("non-canonical", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessorAssetReferenceToARawPayloadIsBK3003()
    {
        BakeDiagnostic diagnostic = SingleProcessorReferenceError(
            new ProcessorRefAsset { Texture = "art/player-raw" },
            BakeDiagnosticCodes.ReferenceWrongKind);

        Assert.Equal("Texture", diagnostic.MemberPath);
        Assert.Contains("raw payload", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessorDataReferenceToAMessagePackAssetIsBK3003()
    {
        BakeDiagnostic diagnostic = SingleProcessorReferenceError(
            new ProcessorRefAsset { Payload = "roots/stats" },
            BakeDiagnosticCodes.ReferenceWrongKind);

        Assert.Equal("Payload", diagnostic.MemberPath);
    }

    [Fact]
    public void ProcessorAssetReferenceToTheWrongTypeIsBK3004()
    {
        BakeDiagnostic diagnostic = SingleProcessorReferenceError(
            new ProcessorRefAsset { Texture = "roots/stats" },
            BakeDiagnosticCodes.ReferenceWrongType);

        Assert.Equal("Texture", diagnostic.MemberPath);
        Assert.Contains(typeof(TextureDescriptor).FullName!, diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(_statsRootType, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessorReferenceToAMissingPathIsBK3002()
    {
        BakeDiagnostic diagnostic = SingleProcessorReferenceError(
            new ProcessorRefAsset { Texture = "art/missing" },
            BakeDiagnosticCodes.ReferenceMissing);

        Assert.Equal("art/missing", diagnostic.AssetPath);
        Assert.Contains("does not produce", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADlcProcessorOutputMayReferenceTheMainPack()
    {
        var diagnostics = new BakeDiagnostics();
        BakedAssetIndex mainIndex = BakedAssetIndex.Build(
            [],
            [],
            [Texture("art/player")],
            _projectDirectory,
            diagnostics);

        BakedAssetIndex dlcIndex = ProcessorReferenceOnlyIndex(
            new ProcessorRefAsset
            {
                Texture = "art/player",
                Payload = "art/player-raw",
            },
            diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.True(BakeReferenceValidator.Validate(
            dlcIndex,
            mainIndex,
            "dlc1.pck",
            diagnostics,
            "assets.pck"));
        Assert.Empty(diagnostics.GetSorted());
    }

    [Fact]
    public void AMainProcessorOutputCannotReferenceADlcOnlyPath()
    {
        var diagnostics = new BakeDiagnostics();
        BakedAssetIndex mainIndex = ProcessorReferenceOnlyIndex(
            new ProcessorRefAsset { Texture = "art/dlc-only" },
            diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.False(BakeReferenceValidator.Validate(
            mainIndex,
            externalIndex: null,
            "assets.pck",
            diagnostics));

        BakeDiagnostic diagnostic = Assert.Single(
            diagnostics.GetSorted(),
            candidate => candidate.Code == BakeDiagnosticCodes.ReferenceMissing);
        Assert.Equal("art/dlc-only", diagnostic.AssetPath);
        Assert.Contains("does not produce", diagnostic.Message, StringComparison.Ordinal);
    }

    private static BakeDiagnostic SingleProcessorReferenceError(
        ProcessorRefAsset value,
        string expectedCode)
    {
        var diagnostics = new BakeDiagnostics();
        BakedAssetIndex index = ProcessorReferenceIndex(value, out _, diagnostics);
        Assert.Empty(diagnostics.GetSorted());

        Assert.False(BakeReferenceValidator.Validate(index, diagnostics));
        return Assert.Single(
            diagnostics.GetSorted(),
            diagnostic => diagnostic.Code == expectedCode);
    }

    private static BakedAssetIndex ProcessorReferenceIndex(
        ProcessorRefAsset value,
        out BakeProcessorOutput output,
        BakeDiagnostics diagnostics)
    {
        output = ProcessorReferenceOutput(value, diagnostics);

        return BakedAssetIndex.Build(
            [Custom("roots/stats", _statsRootType)],
            [output],
            [Texture("art/player")],
            _projectDirectory,
            diagnostics);
    }

    private static BakedAssetIndex ProcessorReferenceOnlyIndex(
        ProcessorRefAsset value,
        BakeDiagnostics diagnostics)
    {
        BakeProcessorOutput output = ProcessorReferenceOutput(value, diagnostics);
        return BakedAssetIndex.Build(
            [],
            [output],
            [],
            _projectDirectory,
            diagnostics);
    }

    private static BakeProcessorOutput ProcessorReferenceOutput(
        ProcessorRefAsset value,
        BakeDiagnostics diagnostics)
    {
        var sink = new BakeOutputSink(
            "data/proc",
            "Assets/data/proc.ref",
            "test-processor",
            diagnostics);
        sink.AddAsset("data/proc", value);
        Assert.Empty(diagnostics.GetSorted());
        return Assert.Single(sink.Outputs);
    }

    private static BakeProcessorOutput ProcessorAsset(string path, string typeName)
        => BakeProcessorOutput.ForAsset(
            path,
            typeName,
            static (_, _) => { },
            [],
            $"Assets/{path}.stats",
            "test-processor");

    private static BakeProcessorOutput ProcessorData(string path)
        => BakeProcessorOutput.ForData(
            path,
            [1, 2, 3],
            $"Assets/{path}.stats",
            "test-processor");

    private static BoundAsset Custom(string assetPath, string typeName)
        => new(
            assetPath,
            typeName,
            new object(),
            $"Assets/{assetPath}.toml",
            $"{assetPath}.toml",
            []);

    private static BakedTexture Texture(string assetPath)
    {
        string payloadPath = assetPath + "-raw";
        var descriptor = new TextureDescriptor
        {
            Payload = payloadPath,
            Width = 2,
            Height = 2,
            Format = "rgba8",
            Filter = "linear",
            Wrap = "clamp",
        };

        return new BakedTexture(
            assetPath,
            descriptor,
            payloadPath,
            new byte[16],
            Path.Combine(_projectDirectory, "Assets", "art", Path.GetFileName(assetPath) + ".png"),
            TextureImportSettings.Defaults(BakeTextureFilter.Linear, BakeTextureWrap.Clamp));
    }
}
