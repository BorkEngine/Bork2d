using System.Numerics;
using Bork2d.Api;

namespace Bork2d.Runtime;

/// <summary>
/// The <see cref="IWindow"/> stub used when the app has no window.
/// </summary>
/// <remarks>
/// Every member throws <see cref="NotSupportedException"/>. Headless and offscreen apps keep this
/// stub in every mode; the real windowed window is attached by the runtime host in M2 WP4.
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
