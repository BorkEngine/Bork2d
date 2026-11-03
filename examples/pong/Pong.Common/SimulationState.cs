namespace Pong.Common;

public readonly record struct SimulationState(
    int LeftScore,
    int RightScore,
    bool IsPaused,
    bool IsServing,
    PongSide Winner);
