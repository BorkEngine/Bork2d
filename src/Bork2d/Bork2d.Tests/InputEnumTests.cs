using Bork2d.Api;
using Bork2d.Runtime;
using Xunit;
using Scancode = Silk.NET.SDL.Scancode;
using Sdl = Silk.NET.SDL.Sdl;

namespace Bork2d.Tests;

/// <summary>
/// The public input enums mirror the SDL physical scancode and button sets exactly, so an accidental
/// binding or generator change cannot silently mis-map a physical key.
/// </summary>
public sealed class InputEnumTests
{
    [Fact]
    public void KeyCodeHasOneMemberPerNamedSdlScancodeAndNoOtherValue()
    {
        var scancodes = Enum.GetValues<Scancode>()
            .Where(scancode => scancode != Scancode.NumScancodes)
            .Select(scancode => (int)scancode)
            .OrderBy(value => value)
            .ToArray();
        var keys = Enum.GetValues<KeyCode>()
            .Select(key => (int)key)
            .OrderBy(value => value)
            .ToArray();

        Assert.Equal(247, scancodes.Length);
        Assert.Equal(scancodes, keys);
        Assert.DoesNotContain((int)Scancode.NumScancodes, keys);
        Assert.Equal(
            Enum.GetNames<KeyCode>().Length,
            Enum.GetNames<KeyCode>().Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void KeyCodeUsesTheDocumentedNamesAndSdlValues()
    {
        Assert.Equal((int)Scancode.ScancodeUnknown, (int)KeyCode.Unknown);
        Assert.Equal(0, (int)KeyCode.Unknown);
        Assert.Equal((int)Scancode.ScancodeLeft, (int)KeyCode.Left);
        Assert.Equal(80, (int)KeyCode.Left);
        Assert.Equal(100, (int)KeyCode.NonUsBackslash);
        Assert.Equal(98, (int)KeyCode.Keypad0);
        Assert.Equal(30, (int)KeyCode.Digit1);
        Assert.Equal(39, (int)KeyCode.Digit0);
        Assert.Equal(50, (int)KeyCode.NonUsHash);
    }

    [Fact]
    public void MouseButtonMapsTheFiveSdlButtons()
    {
        Assert.Equal(Sdl.ButtonLeft, (int)MouseButton.Left);
        Assert.Equal(Sdl.ButtonMiddle, (int)MouseButton.Middle);
        Assert.Equal(Sdl.ButtonRight, (int)MouseButton.Right);
        Assert.Equal(Sdl.ButtonX1, (int)MouseButton.Back);
        Assert.Equal(Sdl.ButtonX2, (int)MouseButton.Forward);

        Assert.Equal(5, Enum.GetValues<MouseButton>().Length);
        Assert.Equal(
            new[] { "Back", "Forward", "Left", "Middle", "Right" },
            Enum.GetNames<MouseButton>().OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryKeyCodeValueIsAccumulatedFromItsScancode()
    {
        using var harness = InputTestHarness.Start();
        var keys = Enum.GetValues<KeyCode>()
            .Where(key => key != KeyCode.Unknown)
            .ToArray();

        harness.Send(keys.Select(key => SdlEvent.Key(SdlEventType.KeyDown, (int)key)).ToArray());
        harness.Freeze();

        foreach (var key in keys)
        {
            Assert.True(harness.Input.IsKeyDown(key), $"KeyCode {key} was not accepted.");
        }

        Assert.False(harness.Input.IsKeyDown(KeyCode.Unknown));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(130)]
    [InlineData(222)]
    [InlineData(511)]
    [InlineData(512)]
    public void UndefinedScancodeEventsAreIgnored(int scancode)
    {
        using var harness = InputTestHarness.Start();

        harness.Send(SdlEvent.Key(SdlEventType.KeyDown, scancode));
        harness.Freeze();

        Assert.False(harness.Input.IsKeyDown((KeyCode)scancode));
        Assert.False(harness.Input.IsKeyPressed((KeyCode)scancode));
    }

    [Fact]
    public void UndefinedKeyAndButtonQueriesReturnFalse()
    {
        using var harness = InputTestHarness.Start();
        harness.Freeze();

        foreach (var key in new[]
                 {
                     (KeyCode)(-1),
                     (KeyCode)3,
                     (KeyCode)511,
                     (KeyCode)512,
                     (KeyCode)int.MaxValue,
                 })
        {
            Assert.False(harness.Input.IsKeyDown(key));
            Assert.False(harness.Input.IsKeyPressed(key));
            Assert.False(harness.Input.IsKeyReleased(key));
        }

        foreach (var button in new[]
                 {
                     (MouseButton)0,
                     (MouseButton)6,
                     (MouseButton)(-1),
                     (MouseButton)int.MaxValue,
                 })
        {
            Assert.False(harness.Input.IsMouseDown(button));
            Assert.False(harness.Input.IsMousePressed(button));
            Assert.False(harness.Input.IsMouseReleased(button));
        }
    }
}
