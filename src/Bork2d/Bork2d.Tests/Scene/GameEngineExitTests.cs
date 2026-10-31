using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Scene;

/// <summary>
/// Engine-level tests for the M7 exit wake seam (Q9=I1): the host's callback fires exactly once on
/// the active-to-requested transition, and it is never invoked after the run is marked stopped.
/// </summary>
public sealed class GameEngineExitTests
{
    [Fact]
    public void ExitInvokesTheInstalledWakeCallbackExactlyOnce()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();
        var calls = 0;
        engine.SetExitCallback(() => calls++);

        engine.Exit();
        engine.Exit();

        Assert.True(engine.ExitRequested);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ExitAfterMarkStoppedDoesNotInvokeAStaleCallback()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();
        var calls = 0;
        engine.SetExitCallback(() => calls++);
        engine.MarkStopped();

        engine.Exit();

        Assert.False(engine.ExitRequested);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ClearingTheCallbackStopsTheWake()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();
        var calls = 0;
        engine.SetExitCallback(() => calls++);
        engine.SetExitCallback(null);

        engine.Exit();

        Assert.True(engine.ExitRequested);
        Assert.Equal(0, calls);
    }
}
