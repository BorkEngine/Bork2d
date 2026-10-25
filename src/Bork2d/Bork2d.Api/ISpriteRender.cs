namespace Bork2d.Api;

public interface ISpriteRender<TLayer>: ITransform, IDisposable where TLayer : struct, Enum
{
    Sprite Sprite { get; set; }
    TLayer Layer { get; set; }
    Color Tint { get; set; }
}