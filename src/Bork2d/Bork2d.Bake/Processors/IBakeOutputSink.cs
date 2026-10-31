using PolyType;

namespace Bork2d.Bake.Processors;

/// <summary>
/// The internal sink a processor uses to emit its outputs for one source.
/// </summary>
/// <remarks>
/// <see cref="AddAsset{T}"/> takes a typed instance and the engine serializes it with the compiled
/// shape. <see cref="AddData"/> is for opaque bytes only; a processor never serializes MessagePack
/// or calls the pack writer directly.
/// </remarks>
internal interface IBakeOutputSink
{
    /// <summary>Emits one shaped asset at a base-path-derived canonical path.</summary>
    /// <typeparam name="T">The shapeable asset type.</typeparam>
    /// <param name="assetPath">The canonical output path.</param>
    /// <param name="value">The typed asset instance.</param>
    void AddAsset<T>(string assetPath, T value)
        where T : IShapeable<T>;

    /// <summary>Emits one raw payload at a base-path-derived canonical path.</summary>
    /// <param name="assetPath">The canonical output path.</param>
    /// <param name="payload">The opaque payload bytes.</param>
    void AddData(string assetPath, ReadOnlySpan<byte> payload);
}
