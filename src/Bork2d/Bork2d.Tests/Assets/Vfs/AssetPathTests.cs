using Bork2d.Engine.Assets.Vfs;
using Xunit;

namespace Bork2d.Tests.Assets.Vfs;

public sealed class AssetPathTests
{
    [Theory]
    [InlineData("Textures\\Player", "textures/player")]
    [InlineData("SPRITES/Idle", "sprites/idle")]
    [InlineData("data/Player\\Idle", "data/player/idle")]
    [InlineData("textures/raw/player_01", "textures/raw/player_01")]
    public void NormalizeLowercasesAndConvertsBackslashes(string input, string expected)
    {
        Assert.Equal(expected, AssetPath.Normalize(input));
    }

    [Fact]
    public void NormalizeRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => AssetPath.Normalize(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/textures/player")]
    [InlineData("textures/player/")]
    [InlineData("textures//player")]
    [InlineData("textures/./player")]
    [InlineData("textures/../player")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("textures/player.png")]
    [InlineData("textures/pla yer")]
    [InlineData("textures/pl@yer")]
    [InlineData("textures/pl\u00e9yer")]
    [InlineData("textures\\\\player")]
    public void NormalizeRejectsInvalidPaths(string input)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => AssetPath.Normalize(input));

        Assert.Contains("Invalid asset path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeAcceptsDigitsUnderscoresAndHyphens()
    {
        Assert.Equal("data/level_01-boss", AssetPath.Normalize("Data/Level_01-Boss"));
    }

    [Theory]
    [InlineData("textures/player", true)]
    [InlineData("textures/Player", false)]
    [InlineData("textures\\player", false)]
    [InlineData("textures/player.png", false)]
    [InlineData("textures/player/", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryValidateCanonicalDoesNotNormalize(string? input, bool expected)
    {
        Assert.Equal(expected, AssetPath.TryValidateCanonical(input, out _));
    }

    [Theory]
    [InlineData("textures/player", true)]
    [InlineData("textures/Player", false)]
    [InlineData("textures\\player", false)]
    public void IsCanonicalRequiresTheExactCanonicalForm(string input, bool expected)
    {
        Assert.Equal(expected, AssetPath.IsCanonical(input));
    }
}
