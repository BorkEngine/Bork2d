namespace Pong.Common;

/// <summary>
/// The render layer flags of the Pong scene.
/// </summary>
/// <remarks>
/// A renderable owns exactly one bit and the camera renders the combined mask, so the court,
/// paddles, and ball can be filtered and ordered independently.
/// </remarks>
[Flags]
public enum GameLayer
{
    /// <summary>No layer; a renderable with this value is skipped.</summary>
    None = 0,

    /// <summary>The court layer (reserved for the future renderer).</summary>
    Court = 1,

    /// <summary>The paddle layer.</summary>
    Paddles = 2,

    /// <summary>The ball layer.</summary>
    Ball = 4,
}
