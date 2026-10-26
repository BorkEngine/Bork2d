using System.Numerics;
using Bork2d.Api.Platform;

namespace Bork2d.Runtime.Window;

/// <summary>
/// The <see cref="IWindow"/> stub used when the app has no window.
/// </summary>
/// <remarks>
/// Every member throws <see cref="NotSupportedException"/>. Headless and offscreen apps keep this
/// stub. A windowed app gets the internal proxy attached by the runtime host during
/// <c>Bork2dApp.Run()</c>; after that run the proxy's members throw
/// <see cref="ObjectDisposedException"/> instead.
/// </remarks>
internal sealed class UnsupportedWindow : IWindow
{
    private const string Message =
        "This app has no window. Headless and offscreen modes do not create one, and a windowed "
        + "window exists only while Bork2dApp.Run() is running.";

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
