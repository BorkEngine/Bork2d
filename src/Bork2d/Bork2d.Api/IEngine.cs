namespace Bork2d.Api;

public interface IEngine<TLayer> where TLayer : struct, Enum
{
    ITime Time { get; }
    IWindow Window { get; }
    IAssets Assets { get; }

    ITransform CreateTransform();
    ICamera<TLayer> CreateCamera();
    ISpriteRender<TLayer> CreateSpriteRender();
}
