using Bork2d.Api.Assets;
using PolyType;

namespace Bork2d.Tests.TestDoubles;

internal sealed class FakeAssets : IAssets
{
    public Task<T> LoadAsset<T>(string assetPath)
        where T : class, IShapeable<T>
        => throw new NotSupportedException("Scene-graph tests do not load assets.");

    public Task<T> LoadAsset<T>(AssetRef<T> reference)
        where T : class, IShapeable<T>
        => throw new NotSupportedException("Scene-graph tests do not load assets.");

    public Task<byte[]> LoadData(string assetPath)
        => throw new NotSupportedException("Scene-graph tests do not load data.");

    public Task<byte[]> LoadData(DataRef reference)
        => throw new NotSupportedException("Scene-graph tests do not load data.");

    public void LoadPack(string packPath)
        => throw new NotSupportedException("Scene-graph tests do not mount packs.");

    public IReadOnlyList<AssetInfo> Browse(string? prefix = null)
        => throw new NotSupportedException("Scene-graph tests do not browse assets.");

    public bool Exists(string assetPath)
        => throw new NotSupportedException("Scene-graph tests do not query assets.");
}
