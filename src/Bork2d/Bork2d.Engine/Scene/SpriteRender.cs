using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;

namespace Bork2d.Engine.Scene;

/// <summary>
/// An engine-owned sprite renderable: a transform plus a sprite asset and draw state.
/// </summary>
/// <remarks>
/// Sprite renderables are scene entities with no destruction state and no global registration.
/// They are retained only while rooted; detaching keeps the instance valid for re-attachment.
/// </remarks>
internal sealed class SpriteRender<TLayer> : Transform, ISpriteRender<TLayer>
    where TLayer : struct, Enum
{
    private SpriteAsset? _sprite;
    private TLayer _layer;
    private Color _tint;
    private int _zIndex;
    private bool _flipX;
    private bool _flipY;

    internal SpriteRender(Lock structuralLock)
        : base(structuralLock)
    {
        _sprite = null;
        _layer = default;
        _tint = Color.White;
        _zIndex = 0;
        _flipX = false;
        _flipY = false;
    }

    public SpriteAsset? Sprite
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

    public bool FlipX
    {
        get
        {
            lock (Sync)
            {
                return _flipX;
            }
        }

        set
        {
            lock (Sync)
            {
                _flipX = value;
            }
        }
    }

    public bool FlipY
    {
        get
        {
            lock (Sync)
            {
                return _flipY;
            }
        }

        set
        {
            lock (Sync)
            {
                _flipY = value;
            }
        }
    }
}
