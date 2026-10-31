using System.Numerics;
using Bork2d.Engine.Platform;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// An <see cref="IWindow"/> whose members throw <see cref="ObjectDisposedException"/>, modelling a
/// retained window reference after a windowed app shut down.
/// </summary>
internal sealed class DisposedWindow : IWindow
{
    private static readonly ObjectDisposedException _disposed = new(nameof(DisposedWindow));

    public string Title
    {
        get => throw _disposed;
        set => throw _disposed;
    }

    public Vector2 Size
    {
        get => throw _disposed;
        set => throw _disposed;
    }

    public bool Fullscreen
    {
        get => throw _disposed;
        set => throw _disposed;
    }

    public bool CursorVisible
    {
        get => throw _disposed;
        set => throw _disposed;
    }

    public bool IsMinimized => throw _disposed;
}
