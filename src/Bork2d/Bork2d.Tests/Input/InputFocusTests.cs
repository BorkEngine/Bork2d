using System.Numerics;
using Bork2d.Engine.Input;
using Bork2d.Runtime.Sdl;
using Xunit;

namespace Bork2d.Tests.Input;

/// <summary>
/// Focus transitions: synthesized releases and cleared transient state on loss, and a re-seeded held
/// state, re-synchronous position, and zero delta on regain, in event order.
/// </summary>
public sealed class InputFocusTests
{
    [Fact]
    public void FocusLossSynthesizesReleasesAndClearsWheelAndText()
    {
        using var harness = InputTestHarness.Start(focused: true);
        var chars = new List<char>();
        harness.Input.OnTextTyped += chars.Add;
        Assert.True(harness.Sdl.TextInputActive);

        harness.Send(
            SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.A),
            SdlEvent.MouseButton((int)MouseButton.Left, isDown: true, 10, 20),
            SdlEvent.MouseMotion(10, 20));
        harness.Freeze();

        Assert.True(harness.Input.IsKeyDown(KeyCode.A));
        Assert.True(harness.Input.IsMouseDown(MouseButton.Left));
        Assert.Equal(new Vector2(10f, 20f), harness.Input.MousePosition);

        harness.Send(
            SdlEvent.MouseWheel(0, 3, isFlipped: false),
            SdlEvent.TextInput("pending"),
            SdlEvent.Window(SdlEventType.WindowFocusLost));
        harness.Freeze();
        harness.Input.DispatchText();

        Assert.True(harness.Input.IsKeyReleased(KeyCode.A));
        Assert.False(harness.Input.IsKeyDown(KeyCode.A));
        Assert.True(harness.Input.IsMouseReleased(MouseButton.Left));
        Assert.False(harness.Input.IsMouseDown(MouseButton.Left));
        Assert.Equal(0f, harness.Input.MouseWheelDelta);
        Assert.Equal(new Vector2(10f, 20f), harness.Input.MousePosition);
        Assert.Equal(Vector2.Zero, harness.Input.MouseDelta);
        Assert.Empty(chars);
        Assert.False(harness.Sdl.TextInputActive);
    }

    [Fact]
    public void FocusRegainReseedsDownWithoutPressEdgesAndResyncsPosition()
    {
        using var harness = InputTestHarness.Start(focused: true);
        harness.Send(SdlEvent.Window(SdlEventType.WindowFocusLost));
        harness.Freeze();

        harness.Sdl.SetKey((int)KeyCode.A, isDown: true);
        harness.Sdl.SetKey((int)KeyCode.LeftShift, isDown: true);
        harness.Sdl.SetKey(511, isDown: true);
        harness.Sdl.MouseState = new SdlMouseState(
            100,
            50,
            buttons: (1u << ((int)MouseButton.Left - 1)) | (1u << ((int)MouseButton.Right - 1)));

        harness.Send(SdlEvent.Window(SdlEventType.WindowFocusGained));
        harness.Freeze();

        Assert.True(harness.Input.IsKeyDown(KeyCode.A));
        Assert.False(harness.Input.IsKeyPressed(KeyCode.A));
        Assert.True(harness.Input.IsKeyDown(KeyCode.LeftShift));
        Assert.False(harness.Input.IsKeyPressed(KeyCode.LeftShift));
        Assert.False(harness.Input.IsKeyDown((KeyCode)511));

        Assert.True(harness.Input.IsMouseDown(MouseButton.Left));
        Assert.True(harness.Input.IsMouseDown(MouseButton.Right));
        Assert.False(harness.Input.IsMousePressed(MouseButton.Left));
        Assert.False(harness.Input.IsMousePressed(MouseButton.Right));
        Assert.Equal(new Vector2(100f, 50f), harness.Input.MousePosition);
        Assert.Equal(Vector2.Zero, harness.Input.MouseDelta);
        Assert.True(harness.Sdl.TextInputActive);
    }

    [Fact]
    public void LossAndRegainBetweenFreezesKeepReleaseEdgeAndReseededHeldState()
    {
        using var harness = InputTestHarness.Start(focused: true);

        harness.Send(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.A));
        harness.Freeze();
        Assert.True(harness.Input.IsKeyDown(KeyCode.A));

        harness.Sdl.SetKey((int)KeyCode.A, isDown: true);
        harness.Send(
            SdlEvent.Window(SdlEventType.WindowFocusLost),
            SdlEvent.Window(SdlEventType.WindowFocusGained));
        harness.Freeze();

        Assert.True(harness.Input.IsKeyReleased(KeyCode.A));
        Assert.True(harness.Input.IsKeyDown(KeyCode.A));
        Assert.False(harness.Input.IsKeyPressed(KeyCode.A));
        Assert.Equal(Vector2.Zero, harness.Input.MouseDelta);
        Assert.True(harness.Sdl.TextInputActive);
    }

    [Fact]
    public void FocusTransitionsReportZeroDeltaAndIgnoreUnfocusedInput()
    {
        using var harness = InputTestHarness.Start(focused: true);

        harness.Send(SdlEvent.MouseMotion(10, 20));
        harness.Freeze();
        Assert.Equal(new Vector2(10f, 20f), harness.Input.MousePosition);
        Assert.Equal(Vector2.Zero, harness.Input.MouseDelta);

        harness.Send(SdlEvent.MouseMotion(30, 40));
        harness.Freeze();
        Assert.Equal(new Vector2(20f, 20f), harness.Input.MouseDelta);

        harness.Send(SdlEvent.Window(SdlEventType.WindowFocusLost));
        harness.Freeze();
        Assert.Equal(Vector2.Zero, harness.Input.MouseDelta);
        Assert.Equal(new Vector2(30f, 40f), harness.Input.MousePosition);

        harness.Send(SdlEvent.MouseMotion(100, 100), SdlEvent.MouseWheel(0, 4, isFlipped: false));
        harness.Freeze();
        Assert.Equal(Vector2.Zero, harness.Input.MouseDelta);
        Assert.Equal(0f, harness.Input.MouseWheelDelta);
        Assert.Equal(new Vector2(30f, 40f), harness.Input.MousePosition);

        harness.Sdl.MouseState = new SdlMouseState(70, 80, buttons: 0);
        harness.Send(SdlEvent.Window(SdlEventType.WindowFocusGained));
        harness.Freeze();
        Assert.Equal(Vector2.Zero, harness.Input.MouseDelta);
        Assert.Equal(new Vector2(70f, 80f), harness.Input.MousePosition);

        harness.Send(SdlEvent.MouseMotion(75, 85));
        harness.Freeze();
        Assert.Equal(new Vector2(5f, 5f), harness.Input.MouseDelta);
    }

    [Fact]
    public void TextInputFollowsStartupFocusAndFocusTransitions()
    {
        using var focused = InputTestHarness.Start(focused: true);
        Assert.True(focused.Sdl.TextInputActive);

        using var unfocused = InputTestHarness.Start(focused: false);
        Assert.False(unfocused.Sdl.TextInputActive);

        unfocused.Send(SdlEvent.Window(SdlEventType.WindowFocusGained));
        Assert.True(unfocused.Sdl.TextInputActive);

        unfocused.Send(SdlEvent.Window(SdlEventType.WindowFocusLost));
        Assert.False(unfocused.Sdl.TextInputActive);
    }

    [Fact]
    public void ShutdownStopsTextInput()
    {
        using var harness = InputTestHarness.Start(focused: true);
        Assert.True(harness.Sdl.TextInputActive);

        harness.Platform.Shutdown();

        Assert.False(harness.Sdl.TextInputActive);
    }
}
