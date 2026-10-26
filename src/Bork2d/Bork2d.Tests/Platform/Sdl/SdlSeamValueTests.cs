using Bork2d.Runtime.Sdl;
using Xunit;

namespace Bork2d.Tests.Platform.Sdl;

/// <summary>
/// The seam's value types and event factories: the data later work packages read must be complete
/// and unambiguous.
/// </summary>
public sealed class SdlSeamValueTests
{
    [Fact]
    public void VersionFormatsAsMajorMinorPatch()
    {
        Assert.Equal("2.30.8", new SdlVersion(2, 30, 8).ToString());
        Assert.Equal("0.0.0", default(SdlVersion).ToString());
    }

    [Theory]
    [InlineData(0, 10, true)]
    [InlineData(10, 0, true)]
    [InlineData(-1, 5, true)]
    [InlineData(5, -1, true)]
    [InlineData(1, 1, false)]
    public void WindowSizeIsEmptyWhenAComponentIsNotPositive(int width, int height, bool isEmpty)
    {
        var size = new SdlWindowSize(width, height);

        Assert.Equal(isEmpty, size.IsEmpty);
    }

    [Fact]
    public void WindowSizeFormatsAsWidthByHeight()
    {
        Assert.Equal("1280x720", new SdlWindowSize(1280, 720).ToString());
    }

    [Fact]
    public void DefaultEventIsUnknown()
    {
        Assert.Equal(SdlEventType.Unknown, default(SdlEvent).Type);
    }

    [Fact]
    public void KeyEventCarriesScancodeAndRepeat()
    {
        var pressed = SdlEvent.Key(SdlEventType.KeyDown, 4, isRepeat: true);

        Assert.Equal(SdlEventType.KeyDown, pressed.Type);
        Assert.Equal(4, pressed.Scancode);
        Assert.True(pressed.IsRepeat);
    }

    [Fact]
    public void WindowEventCarriesTheNewSize()
    {
        var resized = SdlEvent.Window(SdlEventType.WindowResized, 800, 600);

        Assert.Equal(SdlEventType.WindowResized, resized.Type);
        Assert.Equal(800, resized.Width);
        Assert.Equal(600, resized.Height);
    }

    [Fact]
    public void MouseEventsCarryPositionAndButton()
    {
        var button = SdlEvent.MouseButton(buttonIndex: 3, isDown: true, x: 12, y: 34);
        var motion = SdlEvent.MouseMotion(x: 12, y: 34);
        var wheel = SdlEvent.MouseWheel(1, -2, isFlipped: true);

        Assert.Equal(SdlEventType.MouseButtonDown, button.Type);
        Assert.Equal(3, button.ButtonIndex);
        Assert.Equal(12, button.MouseX);
        Assert.Equal(34, button.MouseY);
        Assert.Equal(SdlEventType.MouseMotion, motion.Type);
        Assert.Equal(12, motion.MouseX);
        Assert.Equal(SdlEventType.MouseWheel, wheel.Type);
        Assert.Equal(1, wheel.WheelX);
        Assert.Equal(-2, wheel.WheelY);
        Assert.True(wheel.IsWheelFlipped);
    }

    [Fact]
    public void TextAndUserEventsCarryTheirPayload()
    {
        var text = SdlEvent.TextInput("hello");
        var user = SdlEvent.User(0x8001);

        Assert.Equal(SdlEventType.TextInput, text.Type);
        Assert.Equal("hello", text.Text);
        Assert.Equal(SdlEventType.UserEvent, user.Type);
        Assert.Equal(0x8001, user.UserCode);
    }
}
