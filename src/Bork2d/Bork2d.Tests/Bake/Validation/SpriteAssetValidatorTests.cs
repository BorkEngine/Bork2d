using System.Numerics;
using Bork2d.Bake;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Engine.Assets;
using Xunit;

namespace Bork2d.Tests.Bake.Validation;

/// <summary>
/// The built-in <see cref="SpriteAssetValidator"/>: it reports the malformed-field conditions the
/// renderer treats as fatal, accepts an absent texture and UV spans outside <c>[0,1]</c>, and never
/// throws for malformed data.
/// </summary>
public sealed class SpriteAssetValidatorTests
{
    private const string SpritePath = "sprites/player";

    [Fact]
    public void ValidPongLikeSpriteWithoutATexturePasses()
    {
        RecordingDiagnostics diagnostics = Validate(CreateValidSprite());

        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void ValidIndexedSpritePasses()
    {
        SpriteAsset sprite = CreateValidSprite();
        sprite.Topology = SpriteTopology.Triangles;
        sprite.Indices = [0, 1, 2, 0, 2, 3];

        RecordingDiagnostics diagnostics = Validate(sprite);

        Assert.Empty(diagnostics.Items);
    }

    [Fact]
    public void UvSpansOutsideNormalizedRangeAreAccepted()
    {
        SpriteAsset sprite = CreateValidSprite();
        sprite.Uvs =
        [
            new Vector2(-1f, -1f),
            new Vector2(2f, -1f),
            new Vector2(2f, 2f),
            new Vector2(-1f, 2f),
        ];

        RecordingDiagnostics diagnostics = Validate(sprite);

        Assert.Empty(diagnostics.Items);
    }

    [Theory]
    [InlineData("uvs-null", BakeDiagnosticCodes.SpriteUvsInvalid, "Uvs")]
    [InlineData("uvs-short", BakeDiagnosticCodes.SpriteUvsInvalid, "Uvs")]
    [InlineData("uvs-long", BakeDiagnosticCodes.SpriteUvsInvalid, "Uvs")]
    [InlineData("uvs-non-finite", BakeDiagnosticCodes.SpriteUvsInvalid, "Uvs")]
    [InlineData("scale-zero", BakeDiagnosticCodes.SpriteScaleInvalid, "Scale")]
    [InlineData("scale-negative", BakeDiagnosticCodes.SpriteScaleInvalid, "Scale")]
    [InlineData("scale-non-finite", BakeDiagnosticCodes.SpriteScaleInvalid, "Scale")]
    [InlineData("pivot-non-finite", BakeDiagnosticCodes.SpritePivotInvalid, "Pivot")]
    [InlineData("pivot-below-range", BakeDiagnosticCodes.SpritePivotInvalid, "Pivot")]
    [InlineData("pivot-above-range", BakeDiagnosticCodes.SpritePivotInvalid, "Pivot")]
    [InlineData("topology-unknown", BakeDiagnosticCodes.SpriteTopologyInvalid, "Topology")]
    [InlineData("quad-with-indices", BakeDiagnosticCodes.SpriteTopologyInvalid, "Indices")]
    [InlineData("indexed-without-indices", BakeDiagnosticCodes.SpriteTopologyInvalid, "Indices")]
    [InlineData("indexed-with-empty-indices", BakeDiagnosticCodes.SpriteTopologyInvalid, "Indices")]
    [InlineData("indexed-index-out-of-range", BakeDiagnosticCodes.SpriteTopologyInvalid, "Indices")]
    public void MalformedFieldReportsTheExpectedCodeAndPath(
        string caseName,
        string expectedCode,
        string expectedField)
    {
        RecordingDiagnostics diagnostics = Validate(CreateMalformedSprite(caseName));

        RecordedDiagnostic diagnostic = Assert.Single(diagnostics.Items);
        Assert.Equal(BakeSeverity.Error, diagnostic.Severity);
        Assert.Equal(expectedCode, diagnostic.Code);
        Assert.Contains($"'{SpritePath}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains($"'{expectedField}'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneAssetCanReportEveryMalformedField()
    {
        var sprite = new SpriteAsset
        {
            Uvs = null,
            Scale = new Vector2(0f, 1f),
            Pivot = new Vector2(2f, 0.5f),
            Topology = (SpriteTopology)99,
        };

        RecordingDiagnostics diagnostics = Validate(sprite);

        Assert.Equal(
            new[]
            {
                BakeDiagnosticCodes.SpriteUvsInvalid,
                BakeDiagnosticCodes.SpriteScaleInvalid,
                BakeDiagnosticCodes.SpritePivotInvalid,
                BakeDiagnosticCodes.SpriteTopologyInvalid,
            },
            diagnostics.Items.Select(diagnostic => diagnostic.Code));
    }

    private static SpriteAsset CreateValidSprite() => new()
    {
        Uvs =
        [
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        ],
        Scale = new Vector2(1f, 1f),
        Pivot = new Vector2(0.5f, 0.5f),
        Topology = SpriteTopology.Quad,
    };

    private static SpriteAsset CreateMalformedSprite(string caseName)
    {
        SpriteAsset sprite = CreateValidSprite();
        switch (caseName)
        {
            case "uvs-null":
                sprite.Uvs = null;
                break;
            case "uvs-short":
                sprite.Uvs = [new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f)];
                break;
            case "uvs-long":
                sprite.Uvs =
                [
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 0f),
                ];
                break;
            case "uvs-non-finite":
                sprite.Uvs =
                [
                    new Vector2(0f, 0f),
                    new Vector2(float.NaN, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f),
                ];
                break;
            case "scale-zero":
                sprite.Scale = new Vector2(0f, 1f);
                break;
            case "scale-negative":
                sprite.Scale = new Vector2(1f, -1f);
                break;
            case "scale-non-finite":
                sprite.Scale = new Vector2(float.PositiveInfinity, 1f);
                break;
            case "pivot-non-finite":
                sprite.Pivot = new Vector2(float.NaN, 0.5f);
                break;
            case "pivot-below-range":
                sprite.Pivot = new Vector2(-0.01f, 0.5f);
                break;
            case "pivot-above-range":
                sprite.Pivot = new Vector2(0.5f, 1.01f);
                break;
            case "topology-unknown":
                sprite.Topology = (SpriteTopology)99;
                break;
            case "quad-with-indices":
                sprite.Indices = [0, 1, 2];
                break;
            case "indexed-without-indices":
                sprite.Topology = SpriteTopology.Triangles;
                break;
            case "indexed-with-empty-indices":
                sprite.Topology = SpriteTopology.TriangleStrip;
                sprite.Indices = [];
                break;
            case "indexed-index-out-of-range":
                sprite.Topology = SpriteTopology.Triangles;
                sprite.Indices = [0, 1, 2, 4];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "Unknown case.");
        }

        return sprite;
    }

    private static RecordingDiagnostics Validate(SpriteAsset sprite)
    {
        var diagnostics = new RecordingDiagnostics();
        new SpriteAssetValidator().Validate(SpritePath, sprite, diagnostics);
        return diagnostics;
    }

    private readonly record struct RecordedDiagnostic(
        BakeSeverity Severity,
        string Code,
        string Message);

    private sealed class RecordingDiagnostics : IAssetProcessorDiagnostics
    {
        internal List<RecordedDiagnostic> Items { get; } = [];

        public void Error(string code, string message)
            => Items.Add(new RecordedDiagnostic(BakeSeverity.Error, code, message));

        public void Warning(string code, string message)
            => Items.Add(new RecordedDiagnostic(BakeSeverity.Warning, code, message));

        public void Info(string code, string message)
            => Items.Add(new RecordedDiagnostic(BakeSeverity.Info, code, message));
    }
}
