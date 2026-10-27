using System.Numerics;
using Bork2d.Engine.Input;
using Bork2d.Runtime.Sdl;
using Xunit;

namespace Bork2d.Tests.Input;

/// <summary>
/// Mouse position/delta in framebuffer pixels, initial emptiness, scale conversion, and wheel
/// accumulation with natural-scrolling normalization.
/// </summary>
public sealed class InputMouseWheelTests
{
    [Fact]
    public void ReadsBeforeTheFirstFreezeReturnAnEmptySnapshot()
    {
        using var harness = InputTestHarness.Start();

        Assert.Equal(Vector2.Zero, harness.Input.MousePosition);
        Assert.Equal(Vector2.Zero, harness.Input.MouseDelta);
        Assert.Equal(0f, harness.Input.MouseWheelDelta);

        foreach (var key in Enum.GetValues<KeyCode>())
        {
            Assert.False(harness.Input.IsKeyDown(key));
            Assert.False(harness.Input.IsKeyPressed(key));
            Assert.False(harness.Input.IsKeyReleased(key));
        }

        foreach (var button in Enum.GetValues<MouseButton>())
        {
            Assert.False(harness.Input.IsMouseDown(button));
            Assert.False(harness.Input.IsMousePressed(button));
            Assert.False(harness.Input.IsMouseReleased(button));
        }

        var chars = new List<char>();
        harness.Input.OnTextTyped += chars.Add;
        harness.Input.DispatchText();
        Assert.Empty(chars);
    }

    [Fact]
    public void FirstFreezeReportsZeroDeltaEvenAfterMotion()
    {
        using var harness = InputTestHarness.Start();

        harness.Send(SdlEvent.MouseMotion(10, 20));
        harness.Freeze();

        Assert.Equal(new Vector2(10f, 20f), harness.Input.MousePosition);
        Assert.Equal(Vector2.Zero, harness.Input.MouseDelta);
    }

    [Fact]
    public void DeltaIsThePositionDifferenceBetweenFreezes()
    {
        using var harness = InputTestHarness.Start();
        harness.Freeze();

        harness.Send(SdlEvent.MouseMotion(10, 20));
        harness.Freeze();
        Assert.Equal(new Vector2(10f, 20f), harness.Input.MouseDelta);

        harness.Send(SdlEvent.MouseMotion(30, 50));
        harness.Freeze();
        Assert.Equal(new Vector2(20f, 30f), harness.Input.MouseDelta);

        harness.Freeze();
        Assert.Equal(Vector2.Zero, harness.Input.MouseDelta);
    }

    [Fact]
    public void MousePositionIsConvertedFromWindowPointsToFramebufferPixels()
    {
        using var harness = InputTestHarness.Start(scale: 1.5f);

        var scale = harness.Platform.State.Scale;
        harness.Send(SdlEvent.MouseMotion(100, 50));
        harness.Freeze();

        var first = new Vector2(100f * scale, 50f * scale);
        Assert.Equal(first, harness.Input.MousePosition);
        Assert.Equal(Vector2.Zero, harness.Input.MouseDelta);

        harness.Send(SdlEvent.MouseMotion(102, 51));
        harness.Freeze();
        var second = new Vector2(102f * scale, 51f * scale);
        Assert.Equal(second - first, harness.Input.MouseDelta);
    }

    [Fact]
    public void WheelAccumulatesAndResetsEveryFreeze()
    {
        using var harness = InputTestHarness.Start();
        harness.Freeze();

        harness.Send(
            SdlEvent.MouseWheel(0, 2, isFlipped: false),
            SdlEvent.MouseWheel(0, 3, isFlipped: false),
            SdlEvent.MouseWheel(0, -1, isFlipped: false));
        harness.Freeze();

        Assert.Equal(4f, harness.Input.MouseWheelDelta);

        harness.Freeze();
        Assert.Equal(0f, harness.Input.MouseWheelDelta);
    }

    [Fact]
    public void WheelRespectsTheFlippedDirection()
    {
        using var harness = InputTestHarness.Start();
        harness.Freeze();

        harness.Send(SdlEvent.MouseWheel(0, 2, isFlipped: true));
        harness.Freeze();
        Assert.Equal(-2f, harness.Input.MouseWheelDelta);

        harness.Send(SdlEvent.MouseWheel(0, 2, isFlipped: false));
        harness.Freeze();
        Assert.Equal(2f, harness.Input.MouseWheelDelta);
    }

    [Fact]
    public void WheelIsClearedOnFocusLoss()
    {
        using var harness = InputTestHarness.Start(focused: true);

        harness.Send(SdlEvent.MouseWheel(0, 5, isFlipped: false));
        harness.Send(SdlEvent.Window(SdlEventType.WindowFocusLost));
        harness.Freeze();

        Assert.Equal(0f, harness.Input.MouseWheelDelta);
    }
}
