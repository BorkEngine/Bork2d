using Bork2d.Assets.Pack;

namespace Bork2d.Bake.Baking;

/// <summary>
/// Maps the CLI/configuration <see cref="BakeMode"/> to the v1 writer's compression policy.
/// </summary>
internal static class BakeModeExtensions
{
    /// <summary>Maps a bake mode to its pack compression mode.</summary>
    /// <param name="mode">The bake mode.</param>
    /// <returns>The pack compression mode.</returns>
    internal static PackCompressionMode ToCompressionMode(this BakeMode mode)
        => mode switch
        {
            BakeMode.Release => PackCompressionMode.Release,
            _ => PackCompressionMode.Debug,
        };

    /// <summary>Maps a bake mode to its documented lowercase name.</summary>
    /// <param name="mode">The bake mode.</param>
    /// <returns><c>debug</c> or <c>release</c>.</returns>
    internal static string ToName(this BakeMode mode)
        => mode == BakeMode.Release ? "release" : "debug";

    /// <summary>Parses a <c>--mode</c> value.</summary>
    /// <param name="value">The option value.</param>
    /// <param name="mode">The parsed mode.</param>
    /// <returns><see langword="true"/> for <c>debug</c>/<c>release</c>, case-insensitively.</returns>
    internal static bool TryParse(string? value, out BakeMode mode)
    {
        if (string.Equals(value, "debug", StringComparison.OrdinalIgnoreCase))
        {
            mode = BakeMode.Debug;
            return true;
        }

        if (string.Equals(value, "release", StringComparison.OrdinalIgnoreCase))
        {
            mode = BakeMode.Release;
            return true;
        }

        mode = BakeMode.Debug;
        return false;
    }
}
