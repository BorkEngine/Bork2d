using System.Numerics;
using Bork2d.Api;

namespace Bork2d.Common;

public sealed class Camera<TLayer>: Transform, IEntity, ICamera<TLayer> where TLayer: struct, Enum
{
    public uint Id { get; }
    public Vector4 Viewport { get; set; }
    public float Size { get; set; }
    public TLayer CullingMask { get; set; }
    public ClearKind Clear { get; set; }
    public Color ClearColor { get; set; }
    
    private readonly EntityList<Camera<TLayer>> _cameras;

    public Camera(EntityList<Camera<TLayer>> list)
    {
        _cameras = list;
        Id = _cameras.GetId();
        _cameras.RegisterEntity(this);
        
        // Top Left - 0, Bottom Right - 1, Default - whole screen
        Viewport = new Vector4(0f, 0f, 1f, 1.0f);
        Size = 10f;
        CullingMask = default;
        Clear = ClearKind.Color;
        ClearColor = Color.Cyan;
    }
    
    public void Dispose()
    {
        _cameras.UnregisterEntity(Id);
    }
}