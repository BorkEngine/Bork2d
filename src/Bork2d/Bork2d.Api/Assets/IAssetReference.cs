namespace Bork2d.Api.Assets;

/// <summary>
/// Identifies a typed asset or raw-data reference for pack-tooling traversal.
/// </summary>
/// <remarks>
/// This contract is internal to the engine. It lets the pack builder find references in an asset
/// object graph without reflection or asset-type-specific code.
/// </remarks>
internal interface IAssetReference
{
    /// <summary>
    /// Gets the canonical path, or <see langword="null"/> when the reference is absent.
    /// </summary>
    string? Path { get; }

    /// <summary>
    /// Gets a value indicating whether the reference targets raw data rather than a MessagePack
    /// asset.
    /// </summary>
    bool IsData { get; }
}
