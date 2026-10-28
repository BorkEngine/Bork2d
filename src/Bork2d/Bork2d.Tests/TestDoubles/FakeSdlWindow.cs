using Bork2d.Engine;
using Bork2d.Runtime.Sdl;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// An in-memory <see cref="ISdlWindow"/> created by <see cref="FakeSdlApi"/>.
/// </summary>
/// <remarks>
/// Window commands update the confirmed state immediately so tests can assert command application
/// without a pump. <see cref="SetSize"/> treats its arguments as window points and derives the
/// framebuffer size with the current scale, which mirrors the SDL contract. <see cref="Resize"/>,
/// <see cref="ChangeScale"/>, and the minimize/maximize/focus helpers let a test emit the matching
/// window event after changing the fake's state.
/// </remarks>
internal sealed class FakeSdlWindow : ISdlWindow
{
    private readonly Action<string>? _record;
    private float _scale;
    private bool _disposed;

    internal FakeSdlWindow(
        uint id,
        string title,
        SdlWindowSize size,
        float scale,
        SdlWindowFlags flags,
        Action<string>? record = null)
    {
        Id = id;
        Title = title;
        Size = size;
        _scale = scale;
        SizeInPixels = Scale(size, scale);
        Flags = flags;
        _record = record;
    }

    /// <summary>Gets the confirmed window title.</summary>
    internal string Title { get; private set; }

    /// <summary>Gets a value indicating whether the window was destroyed.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>Gets a value indicating whether the window is in desktop fullscreen.</summary>
    internal bool IsFullscreen => (Flags & SdlWindowFlags.FullscreenDesktop) != 0;

    /// <summary>Gets the number of <see cref="SetSize"/> calls.</summary>
    internal int SetSizeCallCount { get; private set; }

    /// <summary>Gets the number of <see cref="SetFullscreen"/> calls.</summary>
    internal int SetFullscreenCallCount { get; private set; }

    /// <summary>Gets or sets the message <see cref="SetFullscreen"/> fails with.</summary>
    internal string? SetFullscreenFailure { get; set; }

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
        _record?.Invoke($"SetWindowTitle({title})");
    }

    /// <inheritdoc/>
    public void SetSize(int width, int height)
    {
        ThrowIfDisposed();
        SetSizeCallCount++;
        Size = new SdlWindowSize(width, height);
        SizeInPixels = Scale(Size, _scale);
        _record?.Invoke($"SetWindowSize({width},{height})");
    }

    /// <inheritdoc/>
    public void SetFullscreen(bool fullscreen)
    {
        ThrowIfDisposed();
        SetFullscreenCallCount++;
        if (SetFullscreenFailure is not null)
        {
            throw new Bork2dException(SetFullscreenFailure);
        }

        Flags = fullscreen
            ? Flags | SdlWindowFlags.FullscreenDesktop
            : Flags & ~SdlWindowFlags.FullscreenDesktop;
        _record?.Invoke($"SetWindowFullscreen({fullscreen})");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _record?.Invoke("DestroyWindow");
    }

    /// <summary>Changes the window size in window points and re-derives the framebuffer size.</summary>
    /// <param name="width">The new width in window points.</param>
    /// <param name="height">The new height in window points.</param>
    internal void Resize(int width, int height)
    {
        Size = new SdlWindowSize(width, height);
        SizeInPixels = Scale(Size, _scale);
    }

    /// <summary>Changes the display scale and re-derives the framebuffer size.</summary>
    /// <param name="scale">The new framebuffer-pixels-per-point scale.</param>
    internal void ChangeScale(float scale)
    {
        _scale = scale;
        SizeInPixels = Scale(Size, _scale);
    }

    /// <summary>Marks the window minimized or restored in its SDL flags.</summary>
    /// <param name="minimized">Whether the window is minimized.</param>
    internal void SetMinimized(bool minimized)
    {
        Flags = minimized
            ? Flags | SdlWindowFlags.Minimized
            : Flags & ~SdlWindowFlags.Minimized;
    }

    /// <summary>Marks the window maximized in its SDL flags.</summary>
    internal void SetMaximized()
    {
        Flags |= SdlWindowFlags.Maximized;
        Flags &= ~SdlWindowFlags.Minimized;
    }

    /// <summary>Marks the window keyboard focus in its SDL flags.</summary>
    /// <param name="focused">Whether the window has focus.</param>
    internal void SetFocused(bool focused)
    {
        Flags = focused
            ? Flags | SdlWindowFlags.InputFocus
            : Flags & ~SdlWindowFlags.InputFocus;
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
