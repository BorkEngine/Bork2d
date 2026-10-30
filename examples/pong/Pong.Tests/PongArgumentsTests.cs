using Pong.Desktop;
using Xunit;

namespace Pong.Tests;

/// <summary>
/// Strict command-line parsing for the Pong desktop host: the windowed/headless/offscreen mode
/// selection, the mode-specific tick limit, and the duplicate/unknown/malformed diagnostics.
/// </summary>
public sealed class PongArgumentsTests
{
    [Fact]
    public void NoArgumentsSelectsTheInteractiveWindowedMode()
    {
        Assert.True(PongArguments.TryParse([], out PongArguments arguments, out string error));

        Assert.Empty(error);
        Assert.False(arguments.Headless);
        Assert.False(arguments.Windowed);
        Assert.False(arguments.Offscreen);
        Assert.Null(arguments.TickLimit);
    }

    [Fact]
    public void WindowedSelectsTheInteractiveWindowedMode()
    {
        Assert.True(PongArguments.TryParse(["--windowed"], out PongArguments arguments, out string error));

        Assert.Empty(error);
        Assert.False(arguments.Headless);
        Assert.True(arguments.Windowed);
        Assert.False(arguments.Offscreen);
        Assert.Null(arguments.TickLimit);
    }

    [Fact]
    public void WindowedAcceptsATickLimit()
    {
        Assert.True(
            PongArguments.TryParse(["--windowed", "--ticks", "120"], out PongArguments arguments, out string error));

        Assert.Empty(error);
        Assert.True(arguments.Windowed);
        Assert.False(arguments.Headless);
        Assert.False(arguments.Offscreen);
        Assert.Equal(120, arguments.TickLimit);
    }

    [Fact]
    public void HeadlessHasNoTickLimit()
    {
        Assert.True(PongArguments.TryParse(["--headless"], out PongArguments arguments, out string error));

        Assert.Empty(error);
        Assert.True(arguments.Headless);
        Assert.False(arguments.Windowed);
        Assert.False(arguments.Offscreen);
        Assert.Null(arguments.TickLimit);
    }

    [Fact]
    public void OffscreenAcceptsARequiredTickLimit()
    {
        Assert.True(
            PongArguments.TryParse(["--offscreen", "--ticks", "7"], out PongArguments arguments, out string error));

        Assert.Empty(error);
        Assert.True(arguments.Offscreen);
        Assert.False(arguments.Headless);
        Assert.False(arguments.Windowed);
        Assert.Equal(7, arguments.TickLimit);
    }

    [Fact]
    public void OffscreenRequiresATickLimit()
    {
        Assert.False(PongArguments.TryParse(["--offscreen"], out _, out string error));

        Assert.Contains("--offscreen", error, StringComparison.Ordinal);
        Assert.Contains("--ticks", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TicksCannotBeCombinedWithHeadless()
    {
        Assert.False(
            PongArguments.TryParse(["--headless", "--ticks", "1"], out _, out string error));

        Assert.Contains("--ticks", error, StringComparison.Ordinal);
        Assert.Contains("--headless", error, StringComparison.Ordinal);
    }

    [Fact]
    public void HeadlessAndWindowedAreMutuallyExclusive()
    {
        Assert.False(
            PongArguments.TryParse(["--headless", "--windowed"], out _, out string error));

        Assert.Contains("--headless", error, StringComparison.Ordinal);
        Assert.Contains("--windowed", error, StringComparison.Ordinal);
    }

    [Fact]
    public void HeadlessAndOffscreenAreMutuallyExclusive()
    {
        Assert.False(
            PongArguments.TryParse(["--headless", "--offscreen", "--ticks", "1"], out _, out string error));

        Assert.Contains("--headless", error, StringComparison.Ordinal);
        Assert.Contains("--offscreen", error, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowedAndOffscreenAreMutuallyExclusive()
    {
        Assert.False(
            PongArguments.TryParse(["--windowed", "--offscreen", "--ticks", "1"], out _, out string error));

        Assert.Contains("--windowed", error, StringComparison.Ordinal);
        Assert.Contains("--offscreen", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TicksWithoutWindowedOrOffscreenIsRejected()
    {
        Assert.False(PongArguments.TryParse(["--ticks", "1"], out _, out string error));

        Assert.Contains("--ticks", error, StringComparison.Ordinal);
        Assert.Contains("--windowed", error, StringComparison.Ordinal);
        Assert.Contains("--offscreen", error, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateHeadlessIsRejected()
    {
        Assert.False(
            PongArguments.TryParse(["--headless", "--headless"], out _, out string error));

        Assert.Contains("only once", error, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateWindowedIsRejected()
    {
        Assert.False(
            PongArguments.TryParse(["--windowed", "--windowed"], out _, out string error));

        Assert.Contains("only once", error, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateOffscreenIsRejected()
    {
        Assert.False(
            PongArguments.TryParse(["--offscreen", "--offscreen", "--ticks", "1"], out _, out string error));

        Assert.Contains("only once", error, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateTicksIsRejected()
    {
        Assert.False(
            PongArguments.TryParse(
                ["--windowed", "--ticks", "1", "--ticks", "2"],
                out _,
                out string error));

        Assert.Contains("only once", error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownArgumentIsRejected()
    {
        Assert.False(
            PongArguments.TryParse(["--fullscreen"], out _, out string error));

        Assert.Contains("--fullscreen", error, StringComparison.Ordinal);
        Assert.Contains("unknown", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TicksWithoutAValueIsRejected()
    {
        Assert.False(
            PongArguments.TryParse(["--windowed", "--ticks"], out _, out string error));

        Assert.Contains("--ticks", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    public void MalformedTickCountIsRejected(string tickCount)
    {
        Assert.False(
            PongArguments.TryParse(
                ["--windowed", "--ticks", tickCount],
                out _,
                out string error));

        Assert.Contains("--ticks", error, StringComparison.Ordinal);
    }
}
