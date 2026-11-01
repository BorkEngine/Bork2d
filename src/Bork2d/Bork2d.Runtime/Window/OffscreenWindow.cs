using System.Numerics;
using Bork2d.Engine.Platform;

namespace Bork2d.Runtime.Window;

/// <summary>
/// The functional <see cref="IWindow"/> facade an offscreen app exposes.
/// </summary>
/// <remarks>
/// <para>
/// Offscreen mode has no OS window, but camera projection reads <see cref="Size"/>. The facade
/// reports the fixed <c>WithOffscreen(width, height)</c> target extent for the whole run, including
/// after <c>Run()</c> returns; the setter is accepted as a no-op and never resizes the target. The
/// title, fullscreen flag, and cursor flag are inert stored state, and the window is never
/// minimized.
/// </para>
/// <para>
/// No member throws and no member requires a particular thread. A small lock keeps the composite
/// <see cref="Size"/> value and the <see cref="Title"/> reference from tearing under concurrent
/// reads and writes.
/// </para>
/// </remarks>
internal sealed class OffscreenWindow : IWindow
{
    private readonly Lock _gate = new();
    private Vector2 _size;
    private string _title;
    private bool _fullscreen;
    private bool _cursorVisible = true;

    /// <summary>Initializes a new instance of the <see cref="OffscreenWindow"/> class.</summary>
    /// <param name="width">The fixed offscreen target width in pixels.</param>
    /// <param name="height">The fixed offscreen target height in pixels.</param>
    /// <param name="title">The initial inert title; <see langword="null"/> becomes empty.</param>
    internal OffscreenWindow(int width, int height, string? title)
    {
        _size = new Vector2(width, height);
        _title = title ?? string.Empty;
    }

    /// <inheritdoc/>
    public string Title
    {
        get
        {
            lock (_gate)
            {
                return _title;
            }
        }

        set
        {
            lock (_gate)
            {
                _title = value ?? string.Empty;
            }
        }
    }

    /// <inheritdoc/>
    public Vector2 Size
    {
        get
        {
            lock (_gate)
            {
                return _size;
            }
        }

        set
        {
            // The target extent is fixed for the run; the setter is accepted but resizes nothing.
        }
    }

    /// <inheritdoc/>
    public bool Fullscreen
    {
        get
        {
            lock (_gate)
            {
                return _fullscreen;
            }
        }

        set
        {
            lock (_gate)
            {
                _fullscreen = value;
            }
        }
    }

    /// <inheritdoc/>
    public bool CursorVisible
    {
        get
        {
            lock (_gate)
            {
                return _cursorVisible;
            }
        }

        set
        {
            lock (_gate)
            {
                _cursorVisible = value;
            }
        }
    }

    /// <inheritdoc/>
    public bool IsMinimized => false;
}
