using Bork2d.Api;

namespace Bork2d.Common;

public class SpriteRender<TLayer>: Transform, IEntity, ISpriteRender<TLayer> where TLayer: struct, Enum
{
    public uint Id { get; }
    public Sprite Sprite { get; set; }
    public TLayer Layer { get; set; }
    public Color Tint { get; set; }
    
    private readonly EntityList<SpriteRender<TLayer>> _sprites;

    public SpriteRender(EntityList<SpriteRender<TLayer>> list)
    {
        _sprites = list;
        Id = _sprites.GetId();
        _sprites.RegisterEntity(this);

        Sprite = Sprite.Empty;
        Layer = default;
        Tint = Color.White;
    }
    
    public void Dispose()
    {
        _sprites.UnregisterEntity(Id);
    }
}