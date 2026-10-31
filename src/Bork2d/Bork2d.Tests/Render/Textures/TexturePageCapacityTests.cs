using Bork2d.Engine;
using Bork2d.Render.Textures;
using Xunit;

namespace Bork2d.Tests.Render.Textures;

/// <summary>
/// Deterministic tests for the pure bindless page-capacity policy (M8 §6.5): 256 entries, doubling
/// growth, the effective device limit, and the explicit failure at the limit.
/// </summary>
public sealed class TexturePageCapacityTests
{
    [Fact]
    public void InitialCapacityIs256WithOnePlaceholderAnd255RealPages()
    {
        Assert.Equal(256, TexturePageCapacity.Initial);
        Assert.Equal(255, TexturePageCapacity.InitialRealPages);
        Assert.Equal(0, TexturePageCapacity.PlaceholderIndex);
        Assert.Equal(TexturePageCapacity.Initial, TexturePageCapacity.InitialRealPages + 1);
    }

    [Theory]
    [InlineData(1024u, 512u, 512)]
    [InlineData(512u, 1024u, 512)]
    [InlineData(256u, 256u, 256)]
    [InlineData(1024u, 4096u, 1024)]
    public void EffectiveLimitUsesTheSmallerUpdateAfterBindLimit(
        uint perStage,
        uint perSet,
        int expected)
    {
        Assert.Equal(expected, TexturePageCapacity.EffectiveLimit(perStage, perSet));
    }

    [Fact]
    public void EffectiveLimitSaturatesAnUnboundedDeviceLimit()
    {
        Assert.Equal(
            TexturePageCapacity.DocumentedMaximum,
            TexturePageCapacity.EffectiveLimit(uint.MaxValue, uint.MaxValue));
        Assert.Equal(
            TexturePageCapacity.DocumentedMaximum,
            TexturePageCapacity.EffectiveLimit(TexturePageCapacity.DocumentedMaximum + 1u, uint.MaxValue));
    }

    [Fact]
    public void EffectiveLimitKeepsARealLimitBelowTheDocumentedCap()
    {
        Assert.Equal(4096, TexturePageCapacity.EffectiveLimit(4096, uint.MaxValue));
    }

    [Fact]
    public void EnsureSupportsInitialCapacityAcceptsTheInitialCapacity()
    {
        TexturePageCapacity.EnsureSupportsInitialCapacity(TexturePageCapacity.Initial);
        TexturePageCapacity.EnsureSupportsInitialCapacity(TexturePageCapacity.DocumentedMaximum);
    }

    [Fact]
    public void EnsureSupportsInitialCapacityRejectsALowLimitNamingIt()
    {
        var exception = Assert.Throws<Bork2dException>(
            () => TexturePageCapacity.EnsureSupportsInitialCapacity(255));

        Assert.Contains("256", exception.Message, StringComparison.Ordinal);
        Assert.Contains("255", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "maxPerStageDescriptorUpdateAfterBindSampledImages",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TryGrowDoublesTheCapacity()
    {
        Assert.True(TexturePageCapacity.TryGrow(256, 4096, out int grown));
        Assert.Equal(512, grown);

        Assert.True(TexturePageCapacity.TryGrow(512, 4096, out int again));
        Assert.Equal(1024, again);
    }

    [Fact]
    public void TryGrowStopsExactlyAtTheLimit()
    {
        Assert.True(TexturePageCapacity.TryGrow(256, 300, out int grown));
        Assert.Equal(300, grown);
    }

    [Fact]
    public void TryGrowFailsAtTheLimit()
    {
        Assert.False(TexturePageCapacity.TryGrow(256, 256, out int grown));
        Assert.Equal(256, grown);

        Assert.False(
            TexturePageCapacity.TryGrow(TexturePageCapacity.DocumentedMaximum, TexturePageCapacity.DocumentedMaximum, out _));
    }

    [Fact]
    public void TryGrowDoesNotOverflowNearTheDocumentedCap()
    {
        Assert.True(TexturePageCapacity.TryGrow(1 << 19, 1 << 20, out int grown));
        Assert.Equal(TexturePageCapacity.DocumentedMaximum, grown);
    }

    [Fact]
    public void LimitExceptionNamesTheCapacityTheLimitAndTheRequestedEntry()
    {
        Bork2dException exception = TexturePageCapacity.CreateLimitException(
            capacity: 256,
            limit: 256,
            requested: 256);

        Assert.Contains("256", exception.Message, StringComparison.Ordinal);
        Assert.Contains("limit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("silently", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
