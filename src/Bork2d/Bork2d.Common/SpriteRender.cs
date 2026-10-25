using Bork2d.Api;

namespace Bork2d.Common;

/// <summary>
/// An engine-owned sprite renderable: a transform plus a sprite handle and draw state.
/// </summary>
/// <remarks>
/// Sprite renderables are scene entities with no destruction state and no global registration.
/// They are retained only while rooted; detaching keeps the instance valid for re-attachment.
/// </remarks>
internal sealed class SpriteRender<TLayer> : Transform, ISpriteRender<TLayer>
    where TLayer : struct, Enum
{
    private Sprite _sprite;
    private TLayer _layer;
    private Color _tint;
    private int _zIndex;

    internal SpriteRender(SceneGraph graph)
        : base(graph)
    {
        _sprite = Sprite.Empty;
        _layer = default;
        _tint = Color.White;
        _zIndex = 0;
    }

    public Sprite Sprite
    {
        get
        {
            lock (Sync)
            {
                return _sprite;
            }
        }

        set
        {
            lock (Sync)
            {
                _sprite = value;
            }
        }
    }

    public TLayer Layer
    {
        get
        {
            lock (Sync)
            {
                return _layer;
            }
        }

        set
        {
            lock (Sync)
            {
                _layer = value;
            }
        }
    }

    public Color Tint
    {
        get
        {
            lock (Sync)
            {
                return _tint;
            }
        }

        set
        {
            lock (Sync)
            {
                _tint = value;
            }
        }
    }

    public int ZIndex
    {
        get
        {
            lock (Sync)
            {
                return _zIndex;
            }
        }

        set
        {
            lock (Sync)
            {
                _zIndex = value;
            }
        }
    }
}
