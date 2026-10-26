using Bork2d.Api.Assets;
using Bork2d.Api.Rendering;
using Bork2d.Api.Scene;

namespace Bork2d.Common.Scene;

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

    internal SpriteRender(Lock structuralLock)
        : base(structuralLock)
    {
        _sprite = null;
        _layer = default;
        _tint = Color.White;
        _zIndex = 0;
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
}
