using System.Numerics;
using System.Runtime.CompilerServices;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Platform;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;

namespace Bork2d.Engine.Scene;

/// <summary>
/// An engine-owned camera: a transform plus projection and clear state.
/// </summary>
/// <remarks>
/// <para>
/// Cameras are scene entities with no destruction state and no global registration. They are
/// retained only while rooted; detaching keeps the instance valid for re-attachment.
/// </para>
/// <para>
/// The screen/world helpers read the engine window's framebuffer size and the same pure
/// <see cref="CameraProjection"/> math the renderer uses (M8 §6.3/§6.9). They hold no lock while
/// reading the window and never require a particular thread.
/// </para>
/// </remarks>
internal sealed class Camera<TLayer> : Transform, ICamera<TLayer>
    where TLayer : struct, Enum
{
    private readonly IWindow _window;
    private Vector4 _viewport;
    private float _size;
    private TLayer _cullingMask;
    private ClearKind _clear;
    private Color _clearColor;
    private int _zIndex;

    internal Camera(Lock structuralLock, IWindow window)
        : base(structuralLock)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
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

    /// <summary>
    /// Copies this camera's capture state under one per-object lock acquisition.
    /// </summary>
    /// <remarks>
    /// The caller must hold the structural lock. Capture uses this instead of the public property
    /// getters, which would acquire <see cref="Transform.Sync"/> once per field and could observe a
    /// concurrent writer between fields. The snapshot's
    /// <see cref="CameraSnapshot.WorldMatrix"/> is the local matrix as a placeholder; the capture
    /// pass replaces it with the composed world matrix after this method returns and the lock is
    /// released.
    /// </remarks>
    /// <param name="snapshot">
    /// The camera values with the local matrix in <see cref="CameraSnapshot.WorldMatrix"/>.
    /// </param>
    /// <returns>The camera's local-to-parent matrix.</returns>
    internal Matrix3x2 CaptureForSnapshot(out CameraSnapshot snapshot)
    {
        lock (Sync)
        {
            var local = LocalMatrixUnderLock;
            snapshot = new CameraSnapshot(
                _zIndex,
                local,
                _size,
                _viewport,
                ToMask(_cullingMask),
                _clear,
                _clearColor);
            return local;
        }
    }

    /// <inheritdoc/>
    public Bounds Bounds
    {
        get
        {
            CameraProjectionResult projection;
            try
            {
                projection = CreateProjection();
            }
            catch (InvalidOperationException)
            {
                // Invalid cameras and apps without a usable framebuffer (headless, disposed) have
                // no visible world rectangle. ObjectDisposedException derives from
                // InvalidOperationException, so this covers both documented helper failures and
                // lets unrelated exceptions propagate.
                return Bounds.Empty;
            }

            return projection.CreateWorldBounds();
        }
    }

    /// <inheritdoc/>
    public Vector2 WorldSize => CreateProjection().WorldSize;

    /// <inheritdoc/>
    public Vector2 WorldToScreen(Vector2 world)
    {
        if (!float.IsFinite(world.X) || !float.IsFinite(world.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(world),
                world,
                "World coordinates must be finite.");
        }

        return CameraProjection.WorldToFramebuffer(CreateProjection(), world);
    }

    /// <inheritdoc/>
    public Vector2 ScreenToWorld(Vector2 screen)
    {
        if (!float.IsFinite(screen.X) || !float.IsFinite(screen.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(screen),
                screen,
                "Framebuffer-pixel coordinates must be finite.");
        }

        return CameraProjection.FramebufferToWorld(CreateProjection(), screen);
    }

    private CameraProjectionResult CreateProjection()
    {
        float size;
        Vector4 viewport;
        ClearKind clear;
        Color clearColor;
        lock (Sync)
        {
            size = _size;
            viewport = _viewport;
            clear = _clear;
            clearColor = _clearColor;
        }

        Matrix3x2 world = WorldMatrix();
        Vector2 framebuffer;
        try
        {
            framebuffer = _window.Size;
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidOperationException(
                "Camera screen/world helpers require a framebuffer. This app is headless, so "
                    + "IWindow.Size is not available.",
                exception);
        }

        if (!CameraProjection.TryCreate(
            framebuffer,
            world,
            size,
            viewport,
            clear,
            clearColor,
            out CameraProjectionResult projection))
        {
            throw new InvalidOperationException(
                "The camera cannot be projected because "
                    + CameraProjection.Describe(projection.Reason)
                    + ".");
        }

        return projection;
    }

    private static uint ToMask(TLayer layer) => (uint)Unsafe.As<TLayer, int>(ref layer);
}
