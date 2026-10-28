using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Processors;
using Bork2d.Bake.Sources;
using Bork2d.Engine.Assets;
using Xunit;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// The baked asset index and the M4 §9 reference rules: canonical, present, kind, and target type,
/// with the source span, member path, and reference path carried into every diagnostic.
/// </summary>
public sealed class ReferenceValidationTests
{
    private static readonly string _gameConfigType = typeof(Bork2d.Tests.Bake.Baking.GameConfig).FullName!;
    private static readonly string _statsRootType = typeof(Bork2d.Tests.Bake.Baking.StatsRoot).FullName!;
    private static readonly string _textureType = typeof(TextureDescriptor).FullName!;
    private static readonly string _spriteType = typeof(SpriteAsset).FullName!;

    [Fact]
    public void IndexIsSortedAndRecordsTheMessagePackTypeAndRawKind()
    {
        var diagnostics = new BakeDiagnostics();

        BakedAssetIndex index = BakedAssetIndex.Build(
            [
                Custom("sprites/player", _spriteType),
                Custom("data/game", _gameConfigType),
            ],
            [
                ProcessorAsset("textures/player", _textureType),
                ProcessorData("textures/player-raw"),
            ],
            diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.Equal(
            new[]
            {
                "data/game",
                "sprites/player",
                "textures/player",
                "textures/player-raw",
            },
            index.Entries.Select(entry => entry.Path));

        Assert.Equal(BakedAssetEntryKind.CustomData, index.Entries[0].Kind);
        Assert.Equal(_gameConfigType, index.Entries[0].TypeName);
        Assert.False(index.Entries[0].IsRaw);

        Assert.Equal(BakedAssetEntryKind.ProcessorAsset, index.Entries[2].Kind);
        Assert.Equal(_textureType, index.Entries[2].TypeName);
        Assert.False(index.Entries[2].IsRaw);

        Assert.Equal(BakedAssetEntryKind.ProcessorData, index.Entries[3].Kind);
        Assert.Null(index.Entries[3].TypeName);
        Assert.True(index.Entries[3].IsRaw);
    }

    [Fact]
    public void ValidReferencesAcrossKindsTypesAndAbsentReferencesPass()
    {
        var diagnostics = new BakeDiagnostics();
        var assets = new[]
        {
            Custom(
                "data/game",
                _gameConfigType,
                Reference("texture", "textures/player", target: _textureType),
                Reference("payload", "textures/player-raw", isData: true),
                Reference("stats", "roots/stats", target: _statsRootType)),
            Custom("data/absent", _gameConfigType),
            Custom("roots/stats", _statsRootType),
        };

        BakedAssetIndex index = BakedAssetIndex.Build(
            assets,
            [
                ProcessorAsset("textures/player", _textureType),
                ProcessorData("textures/player-raw"),
            ],
            diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.True(BakeReferenceValidator.Validate(index, diagnostics));
        Assert.Empty(diagnostics.GetSorted());
    }

    [Fact]
    public void NonCanonicalReferenceIsBK3001WithTheSourceSpan()
    {
        BakeDiagnostic diagnostic = ValidateSingleError(
            Reference("texture", "Textures/Player", line: 5, column: 11, target: _textureType),
            BakeDiagnosticCodes.ReferenceNotCanonical);

        Assert.Equal("Assets/data/game.toml", diagnostic.FilePath);
        Assert.Equal(5, diagnostic.Line);
        Assert.Equal(11, diagnostic.Column);
        Assert.Equal("texture", diagnostic.MemberPath);
        Assert.Equal("Textures/Player", diagnostic.AssetPath);
        Assert.Contains("non-canonical", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingTargetIsBK3002()
    {
        BakeDiagnostic diagnostic = ValidateSingleError(
            Reference("texture", "textures/missing", target: _textureType),
            BakeDiagnosticCodes.ReferenceMissing);

        Assert.Equal("textures/missing", diagnostic.AssetPath);
        Assert.Contains("does not produce", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetReferenceToARawPayloadIsBK3003()
    {
        BakeDiagnostic diagnostic = ValidateSingleError(
            Reference("texture", "textures/player-raw", target: _textureType),
            BakeDiagnosticCodes.ReferenceWrongKind);

        Assert.Contains("a MessagePack asset", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("raw payload", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DataReferenceToAMessagePackAssetIsBK3003()
    {
        BakeDiagnostic diagnostic = ValidateSingleError(
            Reference("payload", "textures/player", isData: true),
            BakeDiagnosticCodes.ReferenceWrongKind);

        Assert.Contains("a raw payload", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetReferenceToTheWrongBakedTypeIsBK3004()
    {
        var diagnostics = new BakeDiagnostics();
        var assets = new[]
        {
            Custom(
                "data/game",
                _gameConfigType,
                Reference("texture", "roots/stats", target: _textureType)),
            Custom("roots/stats", _statsRootType),
        };

        BakedAssetIndex index = BakedAssetIndex.Build(
            assets,
            [],
            diagnostics);
        Assert.Empty(diagnostics.GetSorted());

        Assert.False(BakeReferenceValidator.Validate(index, diagnostics));
        BakeDiagnostic diagnostic = Assert.Single(
            diagnostics.GetSorted(),
            candidate => candidate.Code == BakeDiagnosticCodes.ReferenceWrongType);
        Assert.Equal("texture", diagnostic.MemberPath);
        Assert.Equal("roots/stats", diagnostic.AssetPath);
        Assert.Contains(_textureType, diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(_statsRootType, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateCustomDataPathIsBK3005AndKeepsTheFirstEntry()
    {
        var diagnostics = new BakeDiagnostics();

        BakedAssetIndex index = BakedAssetIndex.Build(
            [
                Custom("data/dup", _gameConfigType),
                Custom("data/dup", _spriteType),
            ],
            [],
            diagnostics);

        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.AssetPathCollision, diagnostic.Code);
        Assert.Equal("data/dup", diagnostic.AssetPath);
        Assert.Single(index.Entries);
        Assert.Equal(_gameConfigType, index.Entries[0].TypeName);
    }

    [Fact]
    public void AMessagePackPathCollidingWithARawPayloadIsBK3005()
    {
        var diagnostics = new BakeDiagnostics();

        _ = BakedAssetIndex.Build(
            [Custom("textures/player-raw", _gameConfigType)],
            [ProcessorData("textures/player-raw")],
            diagnostics);

        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.AssetPathCollision, diagnostic.Code);
        Assert.Equal("textures/player-raw", diagnostic.AssetPath);
        Assert.Contains("raw payload", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("MessagePack asset", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedAndCollectionMemberPathsKeepTheirSpans()
    {
        var diagnostics = new BakeDiagnostics();
        var assets = new[]
        {
            Custom(
                "data/game",
                _gameConfigType,
                Reference("items[2].icon", "textures/missing", line: 9, column: 4, target: _textureType)),
        };

        BakedAssetIndex index = BakedAssetIndex.Build(
            assets,
            [ProcessorAsset("textures/player", _textureType)],
            diagnostics);
        Assert.Empty(diagnostics.GetSorted());

        Assert.False(BakeReferenceValidator.Validate(index, diagnostics));
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ReferenceMissing, diagnostic.Code);
        Assert.Equal("items[2].icon", diagnostic.MemberPath);
        Assert.Equal(9, diagnostic.Line);
        Assert.Equal(4, diagnostic.Column);
    }

    private static BakeDiagnostic ValidateSingleError(
        BakeReferenceSite site,
        string expectedCode)
    {
        var diagnostics = new BakeDiagnostics();
        var assets = new[]
        {
            Custom("data/game", _gameConfigType, site),
            Custom("roots/stats", _statsRootType),
        };

        BakedAssetIndex index = BakedAssetIndex.Build(
            assets,
            [
                ProcessorAsset("textures/player", _textureType),
                ProcessorData("textures/player-raw"),
            ],
            diagnostics);
        Assert.Empty(diagnostics.GetSorted());

        Assert.False(BakeReferenceValidator.Validate(index, diagnostics));
        return Assert.Single(diagnostics.GetSorted(), diagnostic => diagnostic.Code == expectedCode);
    }

    private static BakeReferenceSite Reference(
        string memberPath,
        string referencePath,
        bool isData = false,
        int? line = 1,
        int? column = 1,
        string? target = null)
        => new(
            memberPath,
            referencePath,
            isData,
            line,
            column,
            isData ? null : target);

    private static BoundAsset Custom(
        string assetPath,
        string typeName,
        params BakeReferenceSite[] references)
        => new(
            assetPath,
            typeName,
            new object(),
            $"Assets/{assetPath}.toml",
            $"{assetPath}.toml",
            references);

    private static BakeProcessorOutput ProcessorAsset(string path, string typeName)
        => BakeProcessorOutput.ForAsset(
            path,
            typeName,
            static (_, _) => { },
            [],
            $"Assets/{path}.png",
            "texture");

    private static BakeProcessorOutput ProcessorData(string path)
        => BakeProcessorOutput.ForData(
            path,
            new byte[16],
            $"Assets/{path}.png",
            "texture");
}
