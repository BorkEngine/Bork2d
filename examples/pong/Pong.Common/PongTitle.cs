namespace Pong.Common;

/// <summary>
/// Formats the window title that carries the score, the match state, and the input prompt alongside
/// the score drawn on the court.
/// </summary>
/// <remarks>
/// The title still carries the match state and the input prompt in windowed mode, so its text is
/// part of the game contract. The forms are stable and ASCII-only: an ASCII hyphen separates the
/// score from the state, and the score is surrounded by spaces. Call this only when the title should
/// be compared with the previous value; never assign the title every frame.
/// </remarks>
public static class PongTitle
{
    /// <summary>Formats the current simulation state as the window title.</summary>
    /// <param name="state">The simulation state to read.</param>
    /// <returns>The title for the current score and state.</returns>
    public static string Format(SimulationState state)
    {
        string score = $"{state.LeftScore} : {state.RightScore}";

        // Pause wins over the serve state: a paused serve would otherwise look frozen with no hint
        // about how to continue.
        if (state.IsPaused)
        {
            return $"Pong - {score} - Paused - Space to resume";
        }

        if (state.IsServing)
        {
            return $"Pong - {score} - Serving...";
        }

        if (state.Winner == PongSide.Left)
        {
            return $"Pong - {score} - Left wins - R to restart, Esc to quit";
        }

        if (state.Winner == PongSide.Right)
        {
            return $"Pong - {score} - Right wins - R to restart, Esc to quit";
        }

        return $"Pong - {score} - W/S move, Space pause, R reset, Esc quit";
    }
}
