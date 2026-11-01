using System.Numerics;
using Bork2d.Engine.Platform;

namespace Bork2d.Runtime.Window;

/// <summary>
/// The <see cref="IWindow"/> stub used by a headless app.
/// </summary>
/// <remarks>
/// Every member throws <see cref="NotSupportedException"/>. Only headless apps keep this stub. A
/// windowed app gets the internal proxy attached by the runtime host during
/// <c>Bork2dApp.Run()</c>; after that run the proxy's members throw
/// <see cref="ObjectDisposedException"/> instead. An offscreen app gets the functional
/// <see cref="OffscreenWindow"/> facade, which never throws.
/// </remarks>
internal sealed class UnsupportedWindow : IWindow
{
    private const string Message =
        "This app has no window. Headless mode does not create one, and a windowed window exists "
        + "only while Bork2dApp.Run() is running.";

    public string Title
    {
        get => throw new NotSupportedException(Message);
        set => throw new NotSupportedException(Message);
    }

    public Vector2 Size
    {
        get => throw new NotSupportedException(Message);
        set => throw new NotSupportedException(Message);
    }

    public bool Fullscreen
    {
        get => throw new NotSupportedException(Message);
        set => throw new NotSupportedException(Message);
    }

    public bool CursorVisible
    {
        get => throw new NotSupportedException(Message);
        set => throw new NotSupportedException(Message);
    }

    public bool IsMinimized => throw new NotSupportedException(Message);
}
