using System.Numerics;
using System.Runtime.CompilerServices;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;

namespace Bork2d.Engine.Scene;

/// <summary>
/// An engine-owned sprite renderable: a transform plus a sprite asset and draw state.
/// </summary>
/// <remarks>
/// <para>
/// Sprite renderables are scene entities with no destruction state and no global registration.
/// They are retained only while rooted; detaching keeps the instance valid for re-attachment.
/// </para>
/// <para>
/// Assigning <see cref="Sprite"/> keeps the live asset for the public property and freezes its
/// render fields once under <see cref="Transform.Sync"/>. Capture reads only the frozen copy, so
/// mutating the asset after assignment cannot change a published snapshot or a planned frame.
/// </para>
/// </remarks>
internal sealed class SpriteRender<TLayer> : Transform, ISpriteRender<TLayer>
    where TLayer : struct, Enum
{
    private SpriteAsset? _sprite;
    private SpriteRenderData? _renderData;
    private TLayer _layer;
    private Color _tint;
    private int _zIndex;
    private bool _flipX;
    private bool _flipY;

    internal SpriteRender(Lock structuralLock)
        : base(structuralLock)
    {
        _sprite = null;
        _renderData = null;
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
            SpriteRenderData? renderData = SpriteRenderData.FreezeOrNull(value);
            lock (Sync)
            {
                _sprite = value;
                _renderData = renderData;
            }
        }
    }

    /// <summary>
    /// Copies this sprite's capture state under one per-object lock acquisition.
    /// </summary>
    /// <remarks>
    /// The caller must hold the structural lock. Capture uses this instead of the public property
    /// getters, which would acquire <see cref="Transform.Sync"/> once per field and could observe a
    /// concurrent writer between fields. The frozen render data is read in the same critical
    /// section. The snapshot's <see cref="SpriteSnapshot.WorldMatrix"/> is the local matrix as a
    /// placeholder; the capture pass replaces it with the composed world matrix after this method
    /// returns and the lock is released.
    /// </remarks>
    /// <param name="snapshot">
    /// The sprite values with the local matrix in <see cref="SpriteSnapshot.WorldMatrix"/>.
    /// </param>
    /// <returns>The sprite's local-to-parent matrix.</returns>
    internal Matrix3x2 CaptureForSnapshot(out SpriteSnapshot snapshot)
    {
        lock (Sync)
        {
            var local = LocalMatrixUnderLock;
            snapshot = new SpriteSnapshot(
                _renderData,
                local,
                _tint,
                ToMask(_layer),
                _zIndex,
                _flipX,
                _flipY);
            return local;
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

    private static uint ToMask(TLayer layer) => (uint)Unsafe.As<TLayer, int>(ref layer);
}
