using Bork2d.Assets.Pack;
using Bork2d.Bake.Baking;
using Xunit;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// The <c>--mode</c> contract: <c>debug</c>/<c>release</c> parse case-insensitively (default debug)
/// and map to the v1 writer's compression policy.
/// </summary>
public sealed class BakeModeTests
{
    [Fact]
    public void BakeModeMapsToThePackCompressionPolicy()
    {
        Assert.Equal(PackCompressionMode.Debug, BakeMode.Debug.ToCompressionMode());
        Assert.Equal(PackCompressionMode.Release, BakeMode.Release.ToCompressionMode());
    }

    [Theory]
    [InlineData("debug", "Debug")]
    [InlineData("release", "Release")]
    [InlineData("Debug", "Debug")]
    [InlineData("RELEASE", "Release")]
    public void ModeValuesParse(string value, string expected)
    {
        Assert.True(BakeModeExtensions.TryParse(value, out BakeMode mode));
        Assert.Equal(expected, mode.ToString());
    }

    [Theory]
    [InlineData("fast")]
    [InlineData("")]
    [InlineData(null)]
    public void InvalidModeValuesAreRejected(string? value)
    {
        Assert.False(BakeModeExtensions.TryParse(value, out BakeMode mode));
        Assert.Equal(BakeMode.Debug, mode);
    }
}
