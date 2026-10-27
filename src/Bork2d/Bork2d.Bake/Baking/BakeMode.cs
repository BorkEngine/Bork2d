namespace Bork2d.Bake.Baking;

/// <summary>
/// The payload compression mode for a bake, matching the build configuration and overridable with
/// <c>--mode</c>.
/// </summary>
internal enum BakeMode
{
    /// <summary>Debug: payloads stay uncompressed, matching <c>PackCompressionMode.Debug</c>.</summary>
    Debug,

    /// <summary>Release: payloads are Zstd-compressed only when strictly smaller.</summary>
    Release,
}
