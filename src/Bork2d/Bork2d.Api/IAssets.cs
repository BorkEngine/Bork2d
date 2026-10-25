namespace Bork2d.Api;

public interface IAssets
{
    Task<T> LoadAsset<T>(string assetPath) where T : class;
    Sprite LoadSprite(string assetPath);
    Task<byte[]> LoadData(string assetPath);
}