namespace Pong.Common;

/// <summary>
/// Identifies a side of the Pong court.
/// </summary>
public enum PongSide
{
    /// <summary>No side; used before the match starts or before there is a winner.</summary>
    None = 0,

    /// <summary>The left side, controlled by the local player.</summary>
    Left = 1,

    /// <summary>The right side, controlled by <see cref="PongAi"/>.</summary>
    Right = 2,
}
