using Bork2d.Api;

namespace Bork2d.Runtime;

/// <summary>
/// The M1 <see cref="IAssets"/> stub: the pack and asset system is implemented in M3.
/// </summary>
/// <remarks>
/// Every member throws <see cref="NotSupportedException"/>. The builder validates only the pack
/// path shape in M1 and never touches the filesystem.
/// </remarks>
internal sealed class UnsupportedAssets : IAssets
{
    private const string Message =
        "Bork2d has no asset or pack system in M1. Pack loading and asset services are implemented "
        + "in M3.";

    public Task<T> LoadAsset<T>(string assetPath)
        where T : class
        => throw new NotSupportedException(Message);

    public Sprite LoadSprite(string assetPath) => throw new NotSupportedException(Message);

    public Task<byte[]> LoadData(string assetPath) => throw new NotSupportedException(Message);
}
