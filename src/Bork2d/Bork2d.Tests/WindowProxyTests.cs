using System.Numerics;
using Bork2d.Runtime;
using Xunit;

namespace Bork2d.Tests;

/// <summary>
/// The <see cref="WindowProxy"/> validates setters before enqueueing, reads the confirmed platform
/// snapshot, and enforces the unattached and post-shutdown contracts.
/// </summary>
public sealed class WindowProxyTests
{
    [Fact]
    public void UnattachedProxyThrowsNotSupportedForEveryMember()
    {
        var proxy = new WindowProxy();

        Assert.Throws<NotSupportedException>(() => { _ = proxy.Title; });
        Assert.Throws<NotSupportedException>(() => proxy.Title = "title");
        Assert.Throws<NotSupportedException>(() => { _ = proxy.Size; });
        Assert.Throws<NotSupportedException>(() => proxy.Size = new Vector2(1f, 1f));
        Assert.Throws<NotSupportedException>(() => { _ = proxy.Fullscreen; });
        Assert.Throws<NotSupportedException>(() => proxy.Fullscreen = true);
        Assert.Throws<NotSupportedException>(() => { _ = proxy.CursorVisible; });
        Assert.Throws<NotSupportedException>(() => proxy.CursorVisible = false);
        Assert.Throws<NotSupportedException>(() => { _ = proxy.IsMinimized; });
    }

    [Fact]
    public void AttachedProxyReadsTheConfirmedInitialState()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(
            sdl,
            WindowTestSupport.Options(title: "Bork2d proxy"));
        var proxy = WindowTestSupport.Attach(platform);

        Assert.Equal("Bork2d proxy", proxy.Title);
        Assert.Equal(new Vector2(1280f, 720f), proxy.Size);
        Assert.False(proxy.Fullscreen);
        Assert.True(proxy.CursorVisible);
        Assert.False(proxy.IsMinimized);
    }

    [Fact]
    public void DetachedProxyThrowsObjectDisposedForEveryMember()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.Attach(platform);

        proxy.Detach();
        proxy.Detach();

        Assert.Throws<ObjectDisposedException>(() => { _ = proxy.Title; });
        Assert.Throws<ObjectDisposedException>(() => proxy.Title = "title");
        Assert.Throws<ObjectDisposedException>(() => { _ = proxy.Size; });
        Assert.Throws<ObjectDisposedException>(() => proxy.Size = new Vector2(1f, 1f));
        Assert.Throws<ObjectDisposedException>(() => { _ = proxy.Fullscreen; });
        Assert.Throws<ObjectDisposedException>(() => proxy.Fullscreen = true);
        Assert.Throws<ObjectDisposedException>(() => { _ = proxy.CursorVisible; });
        Assert.Throws<ObjectDisposedException>(() => proxy.CursorVisible = false);
        Assert.Throws<ObjectDisposedException>(() => { _ = proxy.IsMinimized; });
    }

    [Fact]
    public void AttachRejectsASecondPlatformAndDetachIsIdempotent()
    {
        var first = new FakeSdlApi();
        var second = new FakeSdlApi();
        using var firstPlatform = WindowTestSupport.Start(first);
        using var secondPlatform = WindowTestSupport.Start(second);
        var proxy = WindowTestSupport.Attach(firstPlatform);

        Assert.Throws<InvalidOperationException>(() => proxy.Attach(secondPlatform));

        proxy.Detach();
        proxy.Detach();
    }

    [Fact]
    public void TitleRequiresNonNullButAllowsEmptyAndWhitespace()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.Attach(platform);

        Assert.Throws<ArgumentNullException>(() => proxy.Title = null!);

        proxy.Title = string.Empty;
        WindowTestSupport.WaitUntil(platform, () => proxy.Title.Length == 0);

        proxy.Title = "   ";
        WindowTestSupport.WaitUntil(platform, () => proxy.Title == "   ");
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(-1f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(float.NaN, 1f)]
    [InlineData(1f, float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity, 1f)]
    public void SizeRejectsNonPositiveAndNonFiniteComponents(float width, float height)
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.Attach(platform);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => proxy.Size = new Vector2(width, height));
    }

    [Fact]
    public void SameValueFullscreenAndCursorRequestsAreDropped()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.Attach(platform);
        var window = Assert.Single(sdl.Windows);

        var callsBefore = sdl.Calls.Count;

        proxy.Fullscreen = false;
        proxy.CursorVisible = true;

        Assert.Equal(callsBefore, sdl.Calls.Count);
        Assert.Equal(0, window.SetFullscreenCallCount);
        Assert.True(sdl.CursorVisible);
    }

    [Fact]
    public void SizeIsConvertedFromFramebufferPixelsToWindowPoints()
    {
        var sdl = new FakeSdlApi { WindowScale = 1.5f };
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.Attach(platform);
        var window = Assert.Single(sdl.Windows);

        Assert.Equal(new Vector2(1280f, 720f), proxy.Size);
        Assert.Equal(853, window.Size.Width);
        Assert.Equal(480, window.Size.Height);

        proxy.Size = new Vector2(1920f, 1080f);
        WindowTestSupport.WaitUntil(platform, () => proxy.Size == new Vector2(1920f, 1080f));

        Assert.Equal(1280, window.Size.Width);
        Assert.Equal(720, window.Size.Height);
    }

    [Fact]
    public void SizeWhileFullscreenIsIgnored()
    {
        var sdl = new FakeSdlApi { WindowScale = 1.5f };
        using var platform = WindowTestSupport.Start(sdl, WindowTestSupport.Options(fullscreen: true));
        var proxy = WindowTestSupport.Attach(platform);
        var window = Assert.Single(sdl.Windows);
        var confirmedSize = proxy.Size;

        proxy.Size = new Vector2(800f, 600f);

        Assert.Equal(confirmedSize, proxy.Size);
        Assert.Equal(0, window.SetSizeCallCount);
        Assert.True(proxy.Fullscreen);
    }

    [Fact]
    public void SizeWhileMinimizedUpdatesTheRestoreSize()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.Attach(platform);
        var window = Assert.Single(sdl.Windows);

        window.SetMinimized(true);
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowMinimized));
        WindowTestSupport.WaitUntil(platform, () => proxy.IsMinimized);

        proxy.Size = new Vector2(640f, 360f);
        proxy.Title = "processed-before-restore";
        WindowTestSupport.WaitUntil(platform, () => proxy.Title == "processed-before-restore");

        Assert.Equal(0, window.SetSizeCallCount);
        Assert.Equal(new Vector2(1280f, 720f), proxy.Size);

        window.SetMinimized(false);
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowRestored));
        WindowTestSupport.WaitUntil(platform, () => !proxy.IsMinimized);

        Assert.Equal(1, window.SetSizeCallCount);
        Assert.Equal(new Vector2(640f, 360f), proxy.Size);
        Assert.Equal(640, window.Size.Width);
        Assert.Equal(360, window.Size.Height);
    }

    [Fact]
    public void SizeKeepsTheLastNonZeroFramebufferSizeWhileMinimized()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.Attach(platform);
        var window = Assert.Single(sdl.Windows);

        window.SetMinimized(true);
        window.Resize(0, 0);
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowMinimized));
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowSizeChanged, 0, 0));
        WindowTestSupport.WaitUntil(platform, () => proxy.IsMinimized);

        proxy.Title = "after-zero-size";
        WindowTestSupport.WaitUntil(platform, () => proxy.Title == "after-zero-size");

        Assert.Equal(new Vector2(1280f, 720f), proxy.Size);
    }
}
