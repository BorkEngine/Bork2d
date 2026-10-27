using System.Numerics;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;

namespace Bork2d.Engine.Scene;

/// <summary>
/// An engine-owned camera: a transform plus projection and clear state.
/// </summary>
/// <remarks>
/// Cameras are scene entities with no destruction state and no global registration. They are
/// retained only while rooted; detaching keeps the instance valid for re-attachment.
/// </remarks>
internal sealed class Camera<TLayer> : Transform, ICamera<TLayer>
    where TLayer : struct, Enum
{
    private Vector4 _viewport;
    private float _size;
    private TLayer _cullingMask;
    private ClearKind _clear;
    private Color _clearColor;
    private int _zIndex;

    internal Camera(Lock structuralLock)
        : base(structuralLock)
    {
        _viewport = new Vector4(0f, 0f, 1f, 1f);
        _size = 10f;
        _cullingMask = LayerValidation.All<TLayer>();
        _clear = ClearKind.Color;
        _clearColor = Color.Black;
        _zIndex = 0;
    }

    public Vector4 Viewport
    {
        get
        {
            lock (Sync)
            {
                return _viewport;
            }
        }

        set
        {
            lock (Sync)
            {
                _viewport = value;
            }
        }
    }

    public float Size
    {
        get
        {
            lock (Sync)
            {
                return _size;
            }
        }

        set
        {
            lock (Sync)
            {
                _size = value;
            }
        }
    }

    public TLayer CullingMask
    {
        get
        {
            lock (Sync)
            {
                return _cullingMask;
            }
        }

        set
        {
            lock (Sync)
            {
                _cullingMask = value;
            }
        }
    }

    public ClearKind Clear
    {
        get
        {
            lock (Sync)
            {
                return _clear;
            }
        }

        set
        {
            lock (Sync)
            {
                _clear = value;
            }
        }
    }

    public Color ClearColor
    {
        get
        {
            lock (Sync)
            {
                return _clearColor;
            }
        }

        set
        {
            lock (Sync)
            {
                _clearColor = value;
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
