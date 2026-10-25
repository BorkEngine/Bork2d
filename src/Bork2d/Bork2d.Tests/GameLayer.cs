namespace Bork2d.Tests;

/// <summary>
/// The layer enum used by the scene-graph tests: an Int32-backed <see cref="FlagsAttribute"/>
/// enum with one named bit per layer.
/// </summary>
[Flags]
internal enum GameLayer
{
    None = 0,
    World = 1,
    Player = 2,
    Ui = 4,
}
