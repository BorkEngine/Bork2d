using Bork2d.Api;

namespace Bork2d.Tests;

internal sealed class FakeAssets : IAssets
{
    public Task<T> LoadAsset<T>(string assetPath)
        where T : class
        => throw new NotSupportedException("Scene-graph tests do not load assets.");

    public Sprite LoadSprite(string assetPath) => Sprite.Empty;

    public Task<byte[]> LoadData(string assetPath)
        => throw new NotSupportedException("Scene-graph tests do not load data.");
}
