using Bork2d.Api;
using PolyType;

namespace Bork2d.Runtime;

/// <summary>
/// The temporary <see cref="IAssets"/> stub: the M3 asset service is wired in a later work package.
/// </summary>
/// <remarks>
/// Every member throws <see cref="NotSupportedException"/> until the asset service lands. The
/// builder validates only the pack path shape and never touches the filesystem.
/// </remarks>
internal sealed class UnsupportedAssets : IAssets
{
    private const string Message =
        "Bork2d has no asset service wired into the runtime yet. Pack loading and asset loading are "
        + "implemented in M3.";

    public Task<T> LoadAsset<T>(string assetPath)
        where T : class, IShapeable<T>
        => throw new NotSupportedException(Message);

    public Sprite LoadSprite(string assetPath) => throw new NotSupportedException(Message);

    public Task<byte[]> LoadData(string assetPath) => throw new NotSupportedException(Message);

    public void LoadPack(string packPath) => throw new NotSupportedException(Message);

    public IReadOnlyList<AssetInfo> Browse(string? prefix = null)
        => throw new NotSupportedException(Message);

    public bool Exists(string assetPath) => throw new NotSupportedException(Message);
}
