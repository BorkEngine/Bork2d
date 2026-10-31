using System.Numerics;
using Bork2d.Engine.Input;
using Bork2d.Runtime.Sdl;
using Bork2d.Runtime.Window;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Input;

/// <summary>
/// Freeze guarantees: stable reads within a tick, events deferred to the next freeze, first-tick
/// press edges for keys held before it, steady-state allocation, and teardown reads.
/// </summary>
public sealed class InputFreezeTests
{
    [Fact]
    public void ReadsAreStableWithinOneTickAndEventsWaitForTheNextFreeze()
    {
        using var harness = InputTestHarness.Start();

        harness.Send(
            SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.A),
            SdlEvent.MouseMotion(10, 20));
        harness.Freeze();

        Assert.True(harness.Input.IsKeyDown(KeyCode.A));
        Assert.True(harness.Input.IsKeyPressed(KeyCode.A));

        harness.Send(
            SdlEvent.Key(SdlEventType.KeyUp, (int)KeyCode.A),
            SdlEvent.MouseMotion(99, 99));

        Assert.True(harness.Input.IsKeyDown(KeyCode.A));
        Assert.True(harness.Input.IsKeyPressed(KeyCode.A));
        Assert.False(harness.Input.IsKeyReleased(KeyCode.A));
        Assert.Equal(new Vector2(10f, 20f), harness.Input.MousePosition);
        Assert.Equal(Vector2.Zero, harness.Input.MouseDelta);

        harness.Freeze();
        Assert.False(harness.Input.IsKeyDown(KeyCode.A));
        Assert.True(harness.Input.IsKeyReleased(KeyCode.A));
        Assert.Equal(new Vector2(99f, 99f), harness.Input.MousePosition);
        Assert.Equal(new Vector2(89f, 79f), harness.Input.MouseDelta);
    }

    [Fact]
    public void EventsDuringATickLandInTheNextFreeze()
    {
        using var harness = InputTestHarness.Start();
        harness.Freeze();
        Assert.False(harness.Input.IsKeyDown(KeyCode.W));

        harness.Send(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.W));
        Assert.False(harness.Input.IsKeyDown(KeyCode.W));
        Assert.False(harness.Input.IsKeyPressed(KeyCode.W));

        harness.Freeze();
        Assert.True(harness.Input.IsKeyDown(KeyCode.W));
        Assert.True(harness.Input.IsKeyPressed(KeyCode.W));
    }

    [Fact]
    public void KeysHeldBeforeTheFirstFreezeProduceFirstTickPressEdges()
    {
        using var harness = InputTestHarness.Start();

        // This simulates a key held while Setup runs: the OS thread has already applied the event but
        // no freeze has happened yet.
        harness.Send(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.Space));
        harness.Freeze();

        Assert.True(harness.Input.IsKeyPressed(KeyCode.Space));
        Assert.True(harness.Input.IsKeyDown(KeyCode.Space));
        Assert.Equal(Vector2.Zero, harness.Input.MouseDelta);
    }

    [Fact]
    public void FreezeWithNoTextAllocatesNothing()
    {
        using var harness = InputTestHarness.Start();
        harness.Send(
            SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.A),
            SdlEvent.MouseMotion(12, 34));
        harness.Freeze();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            harness.Freeze();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ReadsDuringTeardownReturnTheMostRecentSnapshotUntilDetached()
    {
        using var harness = InputTestHarness.Start();
        harness.Send(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.A));
        harness.Freeze();
        Assert.True(harness.Input.IsKeyDown(KeyCode.A));

        harness.Platform.Shutdown();

        Assert.True(harness.Input.IsKeyDown(KeyCode.A));

        harness.Input.Detach();
        Assert.Throws<ObjectDisposedException>(() => harness.Input.IsKeyDown(KeyCode.A));
    }

    [Fact]
    public void CloseEventStillRequestsExitAndIsNotConsumedByInput()
    {
        using var harness = InputTestHarness.Start();
        harness.Send(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.A));

        harness.Sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowClose));
        WindowTestSupport.WaitForExitRequest(harness.Platform);

        Assert.True(harness.Platform.ExitRequested);
        Assert.Equal(WindowStopReason.Close, harness.Platform.StopReason);

        harness.Freeze();
        Assert.True(harness.Input.IsKeyDown(KeyCode.A));
    }

    [Fact]
    public void InputEventsDoNotRequestExit()
    {
        using var harness = InputTestHarness.Start();

        harness.Send(
            SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.A),
            SdlEvent.Key(SdlEventType.KeyUp, (int)KeyCode.A),
            SdlEvent.MouseButton((int)MouseButton.Left, isDown: true, 1, 2),
            SdlEvent.MouseButton((int)MouseButton.Left, isDown: false, 1, 2),
            SdlEvent.MouseMotion(3, 4),
            SdlEvent.MouseWheel(0, 1, isFlipped: false),
            SdlEvent.TextInput("a"));
        harness.Freeze();

        Assert.False(harness.Platform.ExitRequested);
    }
}
