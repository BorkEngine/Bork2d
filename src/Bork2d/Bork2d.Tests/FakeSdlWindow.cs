using Bork2d.Runtime;

namespace Bork2d.Tests;

/// <summary>
/// An in-memory <see cref="ISdlWindow"/> created by <see cref="FakeSdlApi"/>.
/// </summary>
/// <remarks>
/// Window commands update the confirmed state immediately so tests can assert command application
/// without a pump. <see cref="SetSize"/> treats its arguments as window points and derives the
/// framebuffer size with the scale captured at creation, which mirrors the SDL contract.
/// </remarks>
internal sealed class FakeSdlWindow : ISdlWindow
{
    private readonly float _scale;
    private bool _disposed;

    internal FakeSdlWindow(
        uint id,
        string title,
        SdlWindowSize size,
        float scale,
        SdlWindowFlags flags)
    {
        Id = id;
        Title = title;
        Size = size;
        _scale = scale;
        SizeInPixels = Scale(size, scale);
        Flags = flags;
    }

    /// <summary>Gets the confirmed window title.</summary>
    internal string Title { get; private set; }

    /// <summary>Gets a value indicating whether the window was destroyed.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>Gets a value indicating whether the window is in desktop fullscreen.</summary>
    internal bool IsFullscreen => (Flags & SdlWindowFlags.Fullscreen) != 0;

    /// <inheritdoc/>
    public uint Id { get; }

    /// <inheritdoc/>
    public SdlWindowSize Size { get; private set; }

    /// <inheritdoc/>
    public SdlWindowSize SizeInPixels { get; private set; }

    /// <inheritdoc/>
    public SdlWindowFlags Flags { get; private set; }

    /// <inheritdoc/>
    public void SetTitle(string title)
    {
        ThrowIfDisposed();
        Title = title;
    }

    /// <inheritdoc/>
    public void SetSize(int width, int height)
    {
        ThrowIfDisposed();
        Size = new SdlWindowSize(width, height);
        SizeInPixels = Scale(Size, _scale);
    }

    /// <inheritdoc/>
    public void SetFullscreen(bool fullscreen)
    {
        ThrowIfDisposed();
        Flags = fullscreen
            ? Flags | SdlWindowFlags.FullscreenDesktop
            : Flags & ~SdlWindowFlags.FullscreenDesktop;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
    }

    private static SdlWindowSize Scale(SdlWindowSize size, float scale)
        => new(
            (int)MathF.Round(size.Width * scale),
            (int)MathF.Round(size.Height * scale));

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
