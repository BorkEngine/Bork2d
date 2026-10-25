using Bork2d.Runtime;
using Xunit;

namespace Bork2d.Tests;

/// <summary>
/// Text handoff: UTF-16 order, surrogate pairs, dispatch on the tick thread before <c>Tick</c>,
/// focus-loss clearing, and handler exception propagation.
/// </summary>
public sealed class InputTextTests
{
    [Fact]
    public void TextIsDispatchedInOrderAndASurrogatePairIsTwoChars()
    {
        using var harness = InputTestHarness.Start();
        var chars = new List<char>();
        harness.Input.OnTextTyped += chars.Add;

        harness.Send(
            SdlEvent.TextInput("a"),
            SdlEvent.TextInput("\U0001F600"),
            SdlEvent.TextInput("z"));
        harness.Freeze();
        harness.Input.DispatchText();

        Assert.Equal(new[] { 'a', '\uD83D', '\uDE00', 'z' }, chars);

        harness.Input.DispatchText();
        Assert.Equal(4, chars.Count);
    }

    [Fact]
    public void TextIsDispatchedBeforeTick()
    {
        using var harness = InputTestHarness.Start();
        var order = new List<string>();
        harness.Input.OnTextTyped += character => order.Add($"text:{character}");

        harness.Send(SdlEvent.TextInput("ab"));
        harness.Freeze();
        harness.Input.DispatchText();
        order.Add("tick");

        Assert.Equal(new[] { "text:a", "text:b", "tick" }, order);
    }

    [Fact]
    public void TextIsNotDispatchedBeforeTheFreeze()
    {
        using var harness = InputTestHarness.Start();
        var chars = new List<char>();
        harness.Input.OnTextTyped += chars.Add;

        harness.Send(SdlEvent.TextInput("x"));
        harness.Input.DispatchText();
        Assert.Empty(chars);

        harness.Freeze();
        harness.Input.DispatchText();
        Assert.Equal(new[] { 'x' }, chars);
    }

    [Fact]
    public void FocusLossClearsPendingText()
    {
        using var harness = InputTestHarness.Start(focused: true);
        var chars = new List<char>();
        harness.Input.OnTextTyped += chars.Add;

        harness.Send(SdlEvent.TextInput("lost"), SdlEvent.Window(SdlEventType.WindowFocusLost));
        harness.Freeze();
        harness.Input.DispatchText();

        Assert.Empty(chars);
        Assert.False(harness.Sdl.TextInputActive);
    }

    [Fact]
    public void TextHandlerExceptionPropagatesAndStopsFurtherDispatch()
    {
        using var harness = InputTestHarness.Start();
        var calls = 0;
        harness.Input.OnTextTyped += _ =>
        {
            calls++;
            throw new InvalidOperationException("text handler failed");
        };

        harness.Send(SdlEvent.TextInput("abc"));
        harness.Freeze();

        var thrown = Assert.Throws<InvalidOperationException>(harness.Input.DispatchText);
        Assert.Equal("text handler failed", thrown.Message);
        Assert.Equal(1, calls);

        harness.Input.DispatchText();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void RemovingAHandlerStopsFurtherText()
    {
        using var harness = InputTestHarness.Start();
        var calls = 0;
        Action<char> handler = _ => calls++;
        harness.Input.OnTextTyped += handler;

        harness.Send(SdlEvent.TextInput("a"));
        harness.Freeze();
        harness.Input.DispatchText();
        Assert.Equal(1, calls);

        harness.Input.OnTextTyped -= handler;
        harness.Send(SdlEvent.TextInput("b"));
        harness.Freeze();
        harness.Input.DispatchText();
        Assert.Equal(1, calls);
    }
}
