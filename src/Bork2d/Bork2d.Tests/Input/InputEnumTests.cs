using Bork2d.Engine.Input;
using Bork2d.Runtime.Sdl;
using Xunit;
using Scancode = Silk.NET.SDL.Scancode;
using Sdl = Silk.NET.SDL.Sdl;

namespace Bork2d.Tests.Input;

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
    public void KeyCodeUsesReadableWordNames()
    {
        var expected = new (int Value, string Name)[]
        {
            (47, nameof(KeyCode.LeftBracket)),
            (48, nameof(KeyCode.RightBracket)),
            (57, nameof(KeyCode.CapsLock)),
            (70, nameof(KeyCode.PrintScreen)),
            (71, nameof(KeyCode.ScrollLock)),
            (75, nameof(KeyCode.PageUp)),
            (78, nameof(KeyCode.PageDown)),
            (83, nameof(KeyCode.NumLockClear)),
            (128, nameof(KeyCode.VolumeUp)),
            (129, nameof(KeyCode.VolumeDown)),
            (134, nameof(KeyCode.KeypadEqualsAs400)),
            (153, nameof(KeyCode.AltErase)),
            (154, nameof(KeyCode.SysReq)),
            (162, nameof(KeyCode.ClearAgain)),
            (178, nameof(KeyCode.ThousandsSeparator)),
            (179, nameof(KeyCode.DecimalSeparator)),
            (180, nameof(KeyCode.CurrencyUnit)),
            (181, nameof(KeyCode.CurrencySubunit)),
            (182, nameof(KeyCode.KeypadLeftParen)),
            (183, nameof(KeyCode.KeypadRightParen)),
            (184, nameof(KeyCode.KeypadLeftBrace)),
            (185, nameof(KeyCode.KeypadRightBrace)),
            (200, nameof(KeyCode.KeypadDblAmpersand)),
            (202, nameof(KeyCode.KeypadDblVerticalBar)),
            (208, nameof(KeyCode.KeypadMemStore)),
            (209, nameof(KeyCode.KeypadMemRecall)),
            (210, nameof(KeyCode.KeypadMemClear)),
            (211, nameof(KeyCode.KeypadMemAdd)),
            (212, nameof(KeyCode.KeypadMemSubtract)),
            (213, nameof(KeyCode.KeypadMemMultiply)),
            (214, nameof(KeyCode.KeypadMemDivide)),
            (215, nameof(KeyCode.KeypadPlusMinus)),
            (217, nameof(KeyCode.KeypadClearEntry)),
            (224, nameof(KeyCode.LeftCtrl)),
            (225, nameof(KeyCode.LeftShift)),
            (226, nameof(KeyCode.LeftAlt)),
            (227, nameof(KeyCode.LeftGui)),
            (228, nameof(KeyCode.RightCtrl)),
            (229, nameof(KeyCode.RightShift)),
            (230, nameof(KeyCode.RightAlt)),
            (231, nameof(KeyCode.RightGui)),
            (258, nameof(KeyCode.AudioNext)),
            (259, nameof(KeyCode.AudioPrev)),
            (260, nameof(KeyCode.AudioStop)),
            (261, nameof(KeyCode.AudioPlay)),
            (262, nameof(KeyCode.AudioMute)),
            (263, nameof(KeyCode.MediaSelect)),
            (275, nameof(KeyCode.BrightnessDown)),
            (276, nameof(KeyCode.BrightnessUp)),
            (277, nameof(KeyCode.DisplaySwitch)),
            (278, nameof(KeyCode.KbdIllumToggle)),
            (279, nameof(KeyCode.KbdIllumDown)),
            (280, nameof(KeyCode.KbdIllumUp)),
            (285, nameof(KeyCode.AudioRewind)),
            (286, nameof(KeyCode.AudioFastForward)),
            (287, nameof(KeyCode.SoftLeft)),
            (288, nameof(KeyCode.SoftRight)),
            (290, nameof(KeyCode.EndCall)),
        };

        foreach (var (value, name) in expected)
        {
            Assert.Equal(name, Enum.GetName(typeof(KeyCode), value));
        }
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
