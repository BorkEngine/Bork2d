using Bork2d.Engine.Input;
using Bork2d.Runtime.Input;
using Bork2d.Runtime.Sdl;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Input;

/// <summary>
/// The <see cref="InputProxy"/> lifecycle: unattached, attached, detached/post-shutdown, and
/// thread-safe text subscription.
/// </summary>
public sealed class InputProxyTests
{
    [Fact]
    public void UnattachedProxyThrowsNotSupportedForEveryMember()
    {
        var proxy = new InputProxy();
        Action<char> handler = _ => { };

        Assert.Throws<NotSupportedException>(() => proxy.OnTextTyped += handler);
        Assert.Throws<NotSupportedException>(() => proxy.OnTextTyped -= handler);
        Assert.Throws<NotSupportedException>(() => proxy.IsKeyPressed(KeyCode.A));
        Assert.Throws<NotSupportedException>(() => proxy.IsKeyReleased(KeyCode.A));
        Assert.Throws<NotSupportedException>(() => proxy.IsKeyDown(KeyCode.A));
        Assert.Throws<NotSupportedException>(() => proxy.MousePosition);
        Assert.Throws<NotSupportedException>(() => proxy.MouseDelta);
        Assert.Throws<NotSupportedException>(() => proxy.MouseWheelDelta);
        Assert.Throws<NotSupportedException>(() => proxy.IsMousePressed(MouseButton.Left));
        Assert.Throws<NotSupportedException>(() => proxy.IsMouseReleased(MouseButton.Left));
        Assert.Throws<NotSupportedException>(() => proxy.IsMouseDown(MouseButton.Left));
        Assert.Throws<NotSupportedException>(proxy.Freeze);
        Assert.Throws<NotSupportedException>(proxy.DispatchText);
    }

    [Fact]
    public void AttachRejectsASecondPlatformAndAttachAfterDetachThrowsObjectDisposed()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.AttachInput(platform);

        Assert.Throws<InvalidOperationException>(() => proxy.Attach(platform));

        proxy.Detach();
        proxy.Detach();

        Assert.Throws<ObjectDisposedException>(() => proxy.Attach(platform));
    }

    [Fact]
    public void DetachedProxyThrowsObjectDisposedForEveryMember()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.AttachInput(platform);
        Action<char> handler = _ => { };

        proxy.Detach();

        Assert.Throws<ObjectDisposedException>(() => proxy.OnTextTyped += handler);
        Assert.Throws<ObjectDisposedException>(() => proxy.OnTextTyped -= handler);
        Assert.Throws<ObjectDisposedException>(() => proxy.IsKeyPressed(KeyCode.A));
        Assert.Throws<ObjectDisposedException>(() => proxy.IsKeyReleased(KeyCode.A));
        Assert.Throws<ObjectDisposedException>(() => proxy.IsKeyDown(KeyCode.A));
        Assert.Throws<ObjectDisposedException>(() => proxy.MousePosition);
        Assert.Throws<ObjectDisposedException>(() => proxy.MouseDelta);
        Assert.Throws<ObjectDisposedException>(() => proxy.MouseWheelDelta);
        Assert.Throws<ObjectDisposedException>(() => proxy.IsMousePressed(MouseButton.Left));
        Assert.Throws<ObjectDisposedException>(() => proxy.IsMouseReleased(MouseButton.Left));
        Assert.Throws<ObjectDisposedException>(() => proxy.IsMouseDown(MouseButton.Left));
        Assert.Throws<ObjectDisposedException>(proxy.Freeze);
        Assert.Throws<ObjectDisposedException>(proxy.DispatchText);
    }

    [Fact]
    public void AttachedProxyReturnsAnEmptySnapshotBeforeTheFirstFreeze()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.AttachInput(platform);

        Assert.False(proxy.IsKeyDown(KeyCode.A));
        Assert.Equal(System.Numerics.Vector2.Zero, proxy.MousePosition);
        Assert.Equal(System.Numerics.Vector2.Zero, proxy.MouseDelta);
        Assert.Equal(0f, proxy.MouseWheelDelta);
    }

    [Fact]
    public void TextSubscriptionIsThreadSafe()
    {
        var sdl = new FakeSdlApi { StartFocused = true };
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.AttachInput(platform);
        var calls = 0;
        Action<char> handler = _ => Interlocked.Increment(ref calls);

        var subscribe = new Thread(() => proxy.OnTextTyped += handler);
        subscribe.Start();
        Assert.True(subscribe.Join(TimeSpan.FromSeconds(10)), "The subscribing thread did not finish.");

        var version = platform.InputVersion;
        sdl.Enqueue(SdlEvent.TextInput("a"));
        WindowTestSupport.WaitForInput(platform, version);
        proxy.Freeze();
        proxy.DispatchText();
        Assert.Equal(1, Volatile.Read(ref calls));

        var unsubscribe = new Thread(() => proxy.OnTextTyped -= handler);
        unsubscribe.Start();
        Assert.True(
            unsubscribe.Join(TimeSpan.FromSeconds(10)),
            "The unsubscribing thread did not finish.");

        version = platform.InputVersion;
        sdl.Enqueue(SdlEvent.TextInput("b"));
        WindowTestSupport.WaitForInput(platform, version);
        proxy.Freeze();
        proxy.DispatchText();
        Assert.Equal(1, Volatile.Read(ref calls));
    }
}
