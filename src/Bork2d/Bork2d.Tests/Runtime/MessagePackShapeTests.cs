using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Nerdbank.MessagePack;
using PolyType;
using Xunit;

namespace Bork2d.Tests.Runtime;

public sealed class MessagePackShapeTests
{
    private static readonly MessagePackSerializer _serializer = new();

    [Fact]
    public void GeneratedShapeRoundTripsTheSmokeDocument()
    {
        var original = new SmokeShapeData
        {
            Name = "generated-shape",
            Value = 42,
            Scale = 1.5f,
            Tint = new Color(1f, 0.5f, 0.25f, 1f),
        };

        byte[] payload = _serializer.Serialize(original);
        var roundTripped = _serializer.Deserialize<SmokeShapeData>(payload);

        Assert.NotNull(roundTripped);
        Assert.NotSame(original, roundTripped);
        Assert.Equal(original.Name, roundTripped.Name);
        Assert.Equal(original.Value, roundTripped.Value);
        Assert.Equal(original.Scale, roundTripped.Scale);
        Assert.Equal(original.Tint, roundTripped.Tint);
    }

    [Fact]
    public void SmokeTypeCarriesTheSourceGeneratedShape()
    {
        var shapeable = typeof(SmokeShapeData).GetInterfaces()
            .Where(candidate => candidate.IsGenericType)
            .Select(candidate => candidate.GetGenericTypeDefinition())
            .ToArray();

        Assert.Contains(typeof(IShapeable<>), shapeable);
    }

    [Fact]
    public void ColorMemberUsesTheMarshalerSurrogate()
    {
        byte[] payload = _serializer.Serialize(
            new SmokeShapeData
            {
                Name = "surrogate",
                Tint = new Color(1f, 0.5f, 0.25f, 1f),
            });

        ReadOnlySpan<byte> fourFloatArray = [0x94, 0xCA];
        Assert.True(
            payload.AsSpan().IndexOf(fourFloatArray) >= 0,
            "A Color member must serialize as a float[4] surrogate array.");
    }

    [Fact]
    public void ShaderAssetRoundTripsItsDeclarations()
    {
        var original = new ShaderAsset
        {
            SpirV = "shaders/custom",
            LayoutVersion = 1,
            EntryPoint = "fragment_main",
            InstanceUniforms =
            [
                new ShaderUniform(
                    "tint",
                    ShaderUniformType.Color,
                    0,
                    new Vector4(1f, 0.5f, 0.25f, 1f)),
            ],
            GlobalUniforms =
            [
                new ShaderUniform("time", ShaderUniformType.Float, 0, Vector4.Zero),
            ],
            TextureSlots =
            [
                new ShaderTextureSlot("noise"),
            ],
        };

        byte[] payload = _serializer.Serialize(original);
        ShaderAsset? roundTripped = _serializer.Deserialize<ShaderAsset>(payload);

        Assert.NotNull(roundTripped);
        Assert.Equal("shaders/custom", roundTripped.SpirV.Path);
        Assert.Equal(1u, roundTripped.LayoutVersion);
        Assert.Equal("fragment_main", roundTripped.EntryPoint);
        Assert.Equal(original.InstanceUniforms, roundTripped.InstanceUniforms);
        Assert.Equal(original.GlobalUniforms, roundTripped.GlobalUniforms);
        Assert.Equal(original.TextureSlots, roundTripped.TextureSlots);
    }

    [Fact]
    public void FontAssetRoundTripsItsMetricsGlyphsAndKerning()
    {
        var original = new FontAsset
        {
            Atlas = "fonts/score-atlas",
            PixelsPerEm = 42,
            Ascent = 1.02f,
            Descent = -0.3f,
            LineGap = 0f,
            DistanceRange = 8f,
            Glyphs =
            [
                new FontGlyph(
                    0x30,
                    0.6f,
                    new Vector2(0.1f, -0.05f),
                    new Vector4(0.1f, -0.05f, 0.5f, 0.7f),
                    new Vector4(0f, 0f, 0.1f, 0.2f)),
                new FontGlyph(
                    0x3A,
                    0.6f,
                    new Vector2(0.2f, 0f),
                    new Vector4(0.2f, 0f, 0.3f, 0.1f),
                    new Vector4(0.1f, 0.2f, 0.2f, 0.3f)),
            ],
            Kerning =
            [
                new FontKerning(0x41, 0x56, -0.05f),
                new FontKerning(0x56, 0x41, 0.025f),
            ],
        };

        byte[] payload = _serializer.Serialize(original);
        FontAsset? roundTripped = _serializer.Deserialize<FontAsset>(payload);

        Assert.NotNull(roundTripped);
        Assert.Equal("fonts/score-atlas", roundTripped.Atlas.Path);
        Assert.Equal(42u, roundTripped.PixelsPerEm);
        Assert.Equal(1.02f, roundTripped.Ascent);
        Assert.Equal(-0.3f, roundTripped.Descent);
        Assert.Equal(0f, roundTripped.LineGap);
        Assert.Equal(8f, roundTripped.DistanceRange);
        Assert.Equal(original.Glyphs, roundTripped.Glyphs);
        Assert.Equal(original.Kerning, roundTripped.Kerning);
    }

    [Fact]
    public void SerializedPayloadIsANonEmptyMessagePackValue()
    {
        byte[] payload = _serializer.Serialize(
            new SmokeShapeData { Name = "payload", Value = 7, Scale = 2f });

        Assert.NotEmpty(payload);
        Assert.NotEqual(0, payload[0] & 0x80);
    }
}
