namespace Pong.Common;

/// <summary>
/// Formats the window title that carries the score, the match state, and the input prompt until the
/// renderer (and a later UI milestone) can draw them.
/// </summary>
/// <remarks>
/// The title is the only player-visible surface in windowed mode, so its text is part of the game
/// contract. The forms are stable and ASCII-only: an ASCII hyphen separates the score from the
/// state, and the score is surrounded by spaces. Call this only when the title should be compared
/// with the previous value; never assign the title every frame.
/// </remarks>
public static class PongTitle
{
    /// <summary>Formats the current simulation state as the window title.</summary>
    /// <param name="simulation">The simulation to read; not stored or modified.</param>
    /// <returns>The title for the current score and state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="simulation"/> is <see langword="null"/>.</exception>
    public static string Format(PongSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        string score = $"{simulation.LeftScore} : {simulation.RightScore}";

        // Pause wins over the serve state: a paused serve would otherwise look frozen with no hint
        // about how to continue.
        if (simulation.IsPaused)
        {
            return $"Pong - {score} - Paused - Space to resume";
        }

        if (simulation.IsServing)
        {
            return $"Pong - {score} - Serving...";
        }

        if (simulation.Winner == PongSide.Left)
        {
            return $"Pong - {score} - Left wins - R to restart, Esc to quit";
        }

        if (simulation.Winner == PongSide.Right)
        {
            return $"Pong - {score} - Right wins - R to restart, Esc to quit";
        }

        return $"Pong - {score} - W/S move, Space pause, R reset, Esc quit";
    }
}
