namespace Bork2d.Api;

public interface IGame<TLayer> where TLayer : struct, Enum
{
    void Setup(IEngine<TLayer> engine);
    void Tick();
}
