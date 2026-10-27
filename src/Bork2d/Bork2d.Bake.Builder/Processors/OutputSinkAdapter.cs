using Bork2d.Bake.Processors;
using PolyType;

namespace Bork2d.Bake.Builder.Processors;

/// <summary>
/// Adapts the public output sink onto the internal processor sink.
/// </summary>
internal sealed class OutputSinkAdapter : IAssetOutputSink
{
    private readonly IBakeOutputSink _sink;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputSinkAdapter"/> class.
    /// </summary>
    /// <param name="sink">The internal sink.</param>
    internal OutputSinkAdapter(IBakeOutputSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    /// <inheritdoc/>
    public void AddAsset<T>(string assetPath, T value)
        where T : IShapeable<T>
        => _sink.AddAsset(assetPath, value);

    /// <inheritdoc/>
    public void AddData(string assetPath, ReadOnlySpan<byte> payload)
        => _sink.AddData(assetPath, payload);
}
