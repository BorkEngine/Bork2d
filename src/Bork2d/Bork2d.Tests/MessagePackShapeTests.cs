using Bork2d.Common;
using PolyType;
using Xunit;

namespace Bork2d.Tests;

public sealed class MessagePackShapeTests
{
    [Fact]
    public void GeneratedShapeRoundTripsTheSmokeDocument()
    {
        var original = new MessagePackSmokeData
        {
            Name = "generated-shape",
            Value = 42,
            Scale = 1.5f,
        };

        var roundTripped = MessagePackSmokeSerializer.RoundTrip(original);

        Assert.NotSame(original, roundTripped);
        Assert.Equal(original.Name, roundTripped.Name);
        Assert.Equal(original.Value, roundTripped.Value);
        Assert.Equal(original.Scale, roundTripped.Scale);
    }

    [Fact]
    public void SmokeTypeCarriesTheSourceGeneratedShape()
    {
        var shapeable = typeof(MessagePackSmokeData).GetInterfaces()
            .Where(candidate => candidate.IsGenericType)
            .Select(candidate => candidate.GetGenericTypeDefinition())
            .ToArray();

        Assert.Contains(typeof(IShapeable<>), shapeable);
    }

    [Fact]
    public void SerializedPayloadIsANonEmptyMessagePackValue()
    {
        var payload = MessagePackSmokeSerializer.Serialize(
            new MessagePackSmokeData { Name = "payload", Value = 7, Scale = 2f });

        Assert.NotEmpty(payload);
        Assert.NotEqual(0, payload[0] & 0x80);
    }
}
