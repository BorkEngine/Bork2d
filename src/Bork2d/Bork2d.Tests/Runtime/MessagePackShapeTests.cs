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
    public void SerializedPayloadIsANonEmptyMessagePackValue()
    {
        byte[] payload = _serializer.Serialize(
            new SmokeShapeData { Name = "payload", Value = 7, Scale = 2f });

        Assert.NotEmpty(payload);
        Assert.NotEqual(0, payload[0] & 0x80);
    }
}
