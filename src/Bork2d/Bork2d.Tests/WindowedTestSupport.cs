using Bork2d.Runtime;

namespace Bork2d.Tests;

/// <summary>
/// Shared helpers for the deterministic windowed-loop tests driven by <see cref="FakeSdlApi"/>.
/// </summary>
/// <remarks>
/// The synchronizing helper blocks a game callback only until the OS thread has applied a window
/// command that was queued after the test's event. The pump drains events before commands, so the
/// observed command proves the earlier event was handled, with no polling, sleep, or timing
/// assumption.
/// </remarks>
internal static class WindowedTestSupport
{
    private static readonly TimeSpan _syncTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Creates a windowed builder wired to the fake SDL seam and a fixed title.</summary>
    /// <param name="sdl">The fake SDL seam the OS thread uses.</param>
    /// <param name="game">The scripted game instance.</param>
    /// <returns>The configured builder.</returns>
    internal static Bork2dAppBuilder<GameLayer> Builder(
        FakeSdlApi sdl,
        ScriptedGame<GameLayer> game)
        => new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithTitle("Loop test")
            .WithSdlApiFactory(() => sdl)
            .WithoutAssets();

    /// <summary>Blocks until the OS thread has applied a synchronizing title change.</summary>
    /// <param name="game">The game whose <c>Setup</c> captured the engine.</param>
    /// <param name="sdl">The fake SDL seam recording the applied command.</param>
    /// <param name="title">A unique title for this synchronization point.</param>
    /// <exception cref="TimeoutException">The command was not applied within the bounded wait.</exception>
    internal static void SyncAfterCommand(
        ScriptedGame<GameLayer> game,
        FakeSdlApi sdl,
        string title)
    {
        var signal = new ManualResetEventSlim();
        var expected = $"SetWindowTitle({title})";
        sdl.CallRecorded = call =>
        {
            if (string.Equals(call, expected, StringComparison.Ordinal))
            {
                signal.Set();
            }
        };

        var engine = game.Engine
            ?? throw new InvalidOperationException("Synchronization requires the captured engine.");
        engine.Window.Title = title;

        if (!signal.Wait(_syncTimeout))
        {
            throw new TimeoutException(
                $"The OS thread did not apply '{expected}' within "
                + $"{_syncTimeout.TotalSeconds:0} s.");
        }
    }
}
