namespace Pong.Common;

/// <summary>
/// The render layer flags of the Pong scene.
/// </summary>
/// <remarks>
/// A renderable owns exactly one bit and the camera renders the combined mask, so the court
/// (including the wordmark), paddles, ball, and score can be filtered and ordered independently.
/// </remarks>
[Flags]
public enum GameLayer
{
    /// <summary>No layer; a renderable with this value is skipped.</summary>
    None = 0,

    /// <summary>The court border, center line, and wordmark layer.</summary>
    Court = 1,

    /// <summary>The paddle layer.</summary>
    Paddles = 2,

    /// <summary>The ball layer.</summary>
    Ball = 4,

    /// <summary>The score layer; the HUD draws above every gameplay element.</summary>
    Hud = 8,
}
