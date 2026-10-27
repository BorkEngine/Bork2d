namespace Bork2d.Bake.Builder.Processors;

/// <summary>
/// The sink a processor uses to emit its outputs for one source.
/// </summary>
/// <remarks>
/// <see cref="AddAsset{T}"/> takes a typed instance; the engine serializes it with the compiled shape
/// through the pack writer. A processor never serializes MessagePack, never sees MessagePack bytes, and
/// never calls the pack writer. <see cref="AddData"/> is only for opaque raw payloads.
/// </remarks>
public interface IAssetOutputSink
{
    /// <summary>Emits one shaped asset at a base-path-derived canonical path.</summary>
    /// <typeparam name="T">The shapeable asset type.</typeparam>
    /// <param name="assetPath">The canonical output path.</param>
    /// <param name="value">The typed asset instance.</param>
    void AddAsset<T>(string assetPath, T value)
        where T : PolyType.IShapeable<T>;

    /// <summary>Emits one raw payload at a base-path-derived canonical path.</summary>
    /// <param name="assetPath">The canonical output path.</param>
    /// <param name="payload">The opaque payload bytes.</param>
    void AddData(string assetPath, ReadOnlySpan<byte> payload);
}
