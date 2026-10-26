using Bork2d.Api.Input;
using Bork2d.Runtime.Sdl;
using Xunit;

namespace Bork2d.Tests.Input;

/// <summary>
/// Keyboard and mouse edge accumulation: one-tick edges, tap in one interval, repeat/duplicate
/// suppression, held state, and the five supported mouse buttons.
/// </summary>
public sealed class InputEdgeTests
{
    [Fact]
    public void PressAndReleaseAcrossFreezesReportOneTickEdges()
    {
        using var harness = InputTestHarness.Start();

        harness.Send(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.A));
        harness.Freeze();
        Assert.True(harness.Input.IsKeyPressed(KeyCode.A));
        Assert.False(harness.Input.IsKeyReleased(KeyCode.A));
        Assert.True(harness.Input.IsKeyDown(KeyCode.A));

        harness.Freeze();
        Assert.False(harness.Input.IsKeyPressed(KeyCode.A));
        Assert.False(harness.Input.IsKeyReleased(KeyCode.A));
        Assert.True(harness.Input.IsKeyDown(KeyCode.A));

        harness.Send(SdlEvent.Key(SdlEventType.KeyUp, (int)KeyCode.A));
        harness.Freeze();
        Assert.False(harness.Input.IsKeyPressed(KeyCode.A));
        Assert.True(harness.Input.IsKeyReleased(KeyCode.A));
        Assert.False(harness.Input.IsKeyDown(KeyCode.A));

        harness.Freeze();
        Assert.False(harness.Input.IsKeyPressed(KeyCode.A));
        Assert.False(harness.Input.IsKeyReleased(KeyCode.A));
        Assert.False(harness.Input.IsKeyDown(KeyCode.A));
    }

    [Fact]
    public void TapInsideOneIntervalReportsPressedAndReleasedWithDownFalse()
    {
        using var harness = InputTestHarness.Start();

        harness.Send(
            SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.Space),
            SdlEvent.Key(SdlEventType.KeyUp, (int)KeyCode.Space));
        harness.Freeze();

        Assert.True(harness.Input.IsKeyPressed(KeyCode.Space));
        Assert.True(harness.Input.IsKeyReleased(KeyCode.Space));
        Assert.False(harness.Input.IsKeyDown(KeyCode.Space));

        harness.Freeze();
        Assert.False(harness.Input.IsKeyPressed(KeyCode.Space));
        Assert.False(harness.Input.IsKeyReleased(KeyCode.Space));
    }

    [Fact]
    public void MultipleCyclesInsideOneIntervalKeepOneTickEdgesAndFinalDownState()
    {
        using var harness = InputTestHarness.Start();

        harness.Send(
            SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.B),
            SdlEvent.Key(SdlEventType.KeyUp, (int)KeyCode.B),
            SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.B));
        harness.Freeze();

        Assert.True(harness.Input.IsKeyPressed(KeyCode.B));
        Assert.True(harness.Input.IsKeyReleased(KeyCode.B));
        Assert.True(harness.Input.IsKeyDown(KeyCode.B));
    }

    [Fact]
    public void RepeatKeyDownCreatesNoPressEdge()
    {
        using var harness = InputTestHarness.Start();

        harness.Send(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.C));
        harness.Freeze();
        harness.Send(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.C, isRepeat: true));
        harness.Freeze();

        Assert.False(harness.Input.IsKeyPressed(KeyCode.C));
        Assert.True(harness.Input.IsKeyDown(KeyCode.C));

        harness.Send(SdlEvent.Key(SdlEventType.KeyUp, (int)KeyCode.C));
        harness.Freeze();
        harness.Send(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.C, isRepeat: true));
        harness.Freeze();

        Assert.False(harness.Input.IsKeyPressed(KeyCode.C));
        Assert.False(harness.Input.IsKeyDown(KeyCode.C));
        Assert.False(harness.Input.IsKeyReleased(KeyCode.C));
    }

    [Fact]
    public void DuplicateDownAndUpTransitionsCreateNoEdge()
    {
        using var harness = InputTestHarness.Start();

        harness.Send(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.D));
        harness.Freeze();
        harness.Send(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.D));
        harness.Freeze();

        Assert.False(harness.Input.IsKeyPressed(KeyCode.D));
        Assert.True(harness.Input.IsKeyDown(KeyCode.D));

        harness.Send(SdlEvent.Key(SdlEventType.KeyUp, (int)KeyCode.D));
        harness.Freeze();
        harness.Send(SdlEvent.Key(SdlEventType.KeyUp, (int)KeyCode.D));
        harness.Freeze();

        Assert.False(harness.Input.IsKeyReleased(KeyCode.D));
        Assert.False(harness.Input.IsKeyDown(KeyCode.D));
    }

    [Fact]
    public void HeldKeyStaysDownWithoutRepeatedEdges()
    {
        using var harness = InputTestHarness.Start();

        harness.Send(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.LeftShift));
        harness.Freeze();
        Assert.True(harness.Input.IsKeyDown(KeyCode.LeftShift));

        for (var tick = 0; tick < 3; tick++)
        {
            harness.Freeze();
            Assert.True(harness.Input.IsKeyDown(KeyCode.LeftShift));
            Assert.False(harness.Input.IsKeyPressed(KeyCode.LeftShift));
            Assert.False(harness.Input.IsKeyReleased(KeyCode.LeftShift));
        }
    }

    [Fact]
    public void FiveMouseButtonsFollowTheSameEdgeRules()
    {
        using var harness = InputTestHarness.Start();
        harness.Freeze();

        foreach (var button in Enum.GetValues<MouseButton>())
        {
            harness.Send(SdlEvent.MouseButton((int)button, isDown: true, 0, 0));
            harness.Freeze();

            Assert.True(harness.Input.IsMousePressed(button), $"{button} did not report a press.");
            Assert.False(harness.Input.IsMouseReleased(button));
            Assert.True(harness.Input.IsMouseDown(button));

            foreach (var other in Enum.GetValues<MouseButton>())
            {
                if (other != button)
                {
                    Assert.False(harness.Input.IsMouseDown(other), $"{other} was set by {button}.");
                }
            }

            harness.Send(SdlEvent.MouseButton((int)button, isDown: false, 0, 0));
            harness.Freeze();

            Assert.False(harness.Input.IsMousePressed(button));
            Assert.True(harness.Input.IsMouseReleased(button));
            Assert.False(harness.Input.IsMouseDown(button));

            harness.Freeze();
            Assert.False(harness.Input.IsMouseReleased(button));
        }
    }

    [Fact]
    public void MouseButtonTapInsideOneIntervalReportsBothEdges()
    {
        using var harness = InputTestHarness.Start();

        harness.Send(
            SdlEvent.MouseButton((int)MouseButton.Back, isDown: true, 10, 20),
            SdlEvent.MouseButton((int)MouseButton.Back, isDown: false, 10, 20));
        harness.Freeze();

        Assert.True(harness.Input.IsMousePressed(MouseButton.Back));
        Assert.True(harness.Input.IsMouseReleased(MouseButton.Back));
        Assert.False(harness.Input.IsMouseDown(MouseButton.Back));
    }

    [Fact]
    public void UnsupportedMouseButtonIndicesAreIgnored()
    {
        using var harness = InputTestHarness.Start();

        harness.Send(
            SdlEvent.MouseButton(0, isDown: true, 0, 0),
            SdlEvent.MouseButton(6, isDown: true, 0, 0),
            SdlEvent.MouseButton(7, isDown: true, 0, 0));
        harness.Freeze();

        foreach (var button in Enum.GetValues<MouseButton>())
        {
            Assert.False(harness.Input.IsMouseDown(button));
            Assert.False(harness.Input.IsMousePressed(button));
        }
    }
}
