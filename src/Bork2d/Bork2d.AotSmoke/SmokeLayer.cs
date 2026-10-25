namespace Bork2d.AotSmoke;

/// <summary>
/// The layer enum used by the AOT smoke app: an Int32-backed <see cref="FlagsAttribute"/> enum
/// with one named bit per layer.
/// </summary>
[Flags]
internal enum SmokeLayer
{
    None = 0,
    World = 1,
    Effects = 2,
}
