namespace Pong.Common;

/// <summary>
/// Formats the in-scene HUD lines that carry the match state and the input prompt.
/// </summary>
/// <remarks>
/// <para>
/// The window title is the host's static <c>Pong</c> title and never reflects the match. Every piece
/// of text that used to be title-only is drawn on the court with <c>FontRender</c>s; the two scores
/// are drawn separately by the right- and left-aligned score renderables, so neither line repeats
/// them.
/// </para>
/// <para>
/// Every result is a fixed ASCII literal, so a frame that formats the same state allocates nothing,
/// and the renderable's ordinal-equal text check keeps a steady frame from rebuilding the cached
/// layout.
/// </para>
/// </remarks>
public static class PongHud
{
    /// <summary>Formats the centered match-state line for the current simulation state.</summary>
    /// <param name="state">The simulation state to read.</param>
    /// <returns>The state word, or an empty string while the match is being played.</returns>
    public static string State(SimulationState state)
    {
        // Pause wins over the serve state: a paused serve would otherwise look frozen with no hint
        // about how to continue.
        if (state.IsPaused)
        {
            return "Paused";
        }

        if (state.IsServing)
        {
            return "Serving...";
        }

        if (state.Winner == PongSide.Left)
        {
            return "Left wins";
        }

        if (state.Winner == PongSide.Right)
        {
            return "Right wins";
        }

        return string.Empty;
    }

    /// <summary>Formats the centered control prompt for the current simulation state.</summary>
    /// <param name="state">The simulation state to read.</param>
    /// <returns>The prompt, or an empty string while the ball is being served.</returns>
    public static string Prompt(SimulationState state)
    {
        // The prompt follows the same precedence as the state line so a paused serve still explains
        // how to continue.
        if (state.IsPaused)
        {
            return "Space to resume";
        }

        if (state.IsServing)
        {
            return string.Empty;
        }

        if (state.Winner == PongSide.Left || state.Winner == PongSide.Right)
        {
            return "R to restart, Esc to quit";
        }

        return "W/S move, Space pause, R reset, Esc quit";
    }
}
