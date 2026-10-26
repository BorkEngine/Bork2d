namespace Bork2d.Assets.Pack;

/// <summary>
/// Selects the payload compression policy for <see cref="PackBuilder"/>.
/// </summary>
/// <remarks>
/// The pack header is always maximum-quality Zstd in both modes. Only asset payloads differ:
/// <see cref="Debug"/> stores them uncompressed, while <see cref="Release"/> stores maximum-quality
/// Zstd only when it is strictly smaller than the raw payload.
/// </remarks>
public enum PackCompressionMode
{
    /// <summary>
    /// Store payloads uncompressed. Intended for tests and local iteration.
    /// </summary>
    Debug,

    /// <summary>
    /// Compress payloads with maximum-quality Zstd only when that is strictly smaller.
    /// </summary>
    Release,
}
