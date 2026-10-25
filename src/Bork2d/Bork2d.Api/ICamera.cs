using System.Numerics;

namespace Bork2d.Api;

public interface ICamera<TLayer>: ITransform, IDisposable where TLayer : struct, Enum
{
    Vector4 Viewport { get; set; }
    float Size { get; set; }
    TLayer CullingMask { get; set; }
    ClearKind Clear { get; set; }
    Color ClearColor { get; set; }
}