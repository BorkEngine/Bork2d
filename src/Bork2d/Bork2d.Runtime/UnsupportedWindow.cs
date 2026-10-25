using System.Numerics;
using Bork2d.Api;

namespace Bork2d.Runtime;

/// <summary>
/// The M1 <see cref="IWindow"/> stub: Bork2d has no window until M2.
/// </summary>
/// <remarks>
/// Every member throws <see cref="NotSupportedException"/>. The type exists so the engine can be
/// constructed and the builder/validation/Run surface can be exercised before window creation
/// lands.
/// </remarks>
internal sealed class UnsupportedWindow : IWindow
{
    private const string Message =
        "Bork2d has no window in M1. Window creation and platform window controls are implemented "
        + "in M2.";

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
}
