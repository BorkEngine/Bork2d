using System.Diagnostics;

namespace Bork2d.Render;

/// <summary>
/// The lock-free SPSC control and stop/fatal channel between the host and the render thread
/// (D-M6-19).
/// </summary>
/// <remarks>
/// <para>
/// The OS/window thread posts extent, fullscreen, and minimized changes; the render thread drains
/// them. Each property keeps only its latest value, and the render thread applies the pending
/// properties in the order of their latest post, so a change to one property is never applied
/// before a change posted earlier to another property. The producer never blocks, and there is no
/// queue capacity to overflow.
/// </para>
/// <para>
/// A stop request is an atomic flag plus a wake; the fatal slot is written once by the render
/// thread and read by the host. The render thread waits on the wake event only when it has nothing
/// to present (minimized or zero extent): no busy loop, no <see cref="Thread.Sleep"/>, and no
/// timeout-based polling in production.
/// </para>
/// </remarks>
internal sealed class RendererControlChannel : IDisposable
{
    private const int ExtentIndex = 0;
    private const int FullscreenIndex = 1;
    private const int MinimizedIndex = 2;
    private const int PropertyCount = 3;

    private readonly long[] _posted = new long[PropertyCount];
    private readonly long[] _consumed = new long[PropertyCount];
    private readonly long[] _values = new long[PropertyCount];
    private readonly ManualResetEventSlim _wake = new(initialState: false);
    private long _postSequence;
    private int _stop;
    private Exception? _fatal;
    private int _disposed;

    /// <summary>Gets a value indicating whether a graceful stop was requested.</summary>
    internal bool IsStopRequested => Volatile.Read(ref _stop) != 0;

    /// <summary>Gets the one-shot fatal error, or <see langword="null"/> when none was published.</summary>
    internal Exception? FatalException => Volatile.Read(ref _fatal);

    /// <summary>
    /// Gets the event the channel sets on every post, stop request, and fatal publication.
    /// </summary>
    /// <remarks>
    /// Exposed so the renderer can combine the control wake with the snapshot-ring wake in one
    /// event-based wait (M7 WP-3). The channel owns the event; callers may reset it and wait on its
    /// handle but never dispose it.
    /// </remarks>
    internal ManualResetEventSlim WakeEvent => _wake;

    /// <summary>Gets a value indicating whether a control message is pending.</summary>
    internal bool HasPendingWork
    {
        get
        {
            for (var index = 0; index < PropertyCount; index++)
            {
                if (Volatile.Read(ref _posted[index]) > Volatile.Read(ref _consumed[index]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Posts the latest framebuffer extent; the previous pending extent is superseded.</summary>
    /// <param name="extent">The confirmed framebuffer extent in pixels.</param>
    internal void PostExtent(WindowExtent extent)
    {
        Volatile.Write(ref _values[ExtentIndex], Pack(extent));
        Post(ExtentIndex);
    }

    /// <summary>Posts the latest fullscreen state; the previous pending value is superseded.</summary>
    /// <param name="fullscreen">Whether the window is fullscreen.</param>
    internal void PostFullscreen(bool fullscreen)
    {
        Volatile.Write(ref _values[FullscreenIndex], fullscreen ? 1 : 0);
        Post(FullscreenIndex);
    }

    /// <summary>Posts the latest minimized state; the previous pending value is superseded.</summary>
    /// <param name="minimized">Whether the window is minimized.</param>
    internal void PostMinimized(bool minimized)
    {
        Volatile.Write(ref _values[MinimizedIndex], minimized ? 1 : 0);
        Post(MinimizedIndex);
    }

    /// <summary>Requests a graceful stop and wakes the render thread; the producer never blocks.</summary>
    internal void RequestStop()
    {
        Volatile.Write(ref _stop, 1);
        Signal();
    }

    /// <summary>
    /// Dequeues the pending property whose latest post is oldest, so changes keep their relative
    /// order across properties while each property coalesces to its latest value.
    /// </summary>
    /// <param name="message">The next control message when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a message was returned.</returns>
    internal bool TryDequeue(out RendererControlMessage message)
    {
        var best = -1;
        var bestSequence = long.MaxValue;
        for (var index = 0; index < PropertyCount; index++)
        {
            long posted = Volatile.Read(ref _posted[index]);
            if (posted <= Volatile.Read(ref _consumed[index]) || posted >= bestSequence)
            {
                continue;
            }

            best = index;
            bestSequence = posted;
        }

        if (best < 0)
        {
            message = default;
            return false;
        }

        long value = Volatile.Read(ref _values[best]);
        Volatile.Write(ref _consumed[best], bestSequence);
        message = best switch
        {
            ExtentIndex => new RendererControlMessage(
                RendererControlMessage.Kind.Extent,
                Unpack(value),
                Flag: false),
            FullscreenIndex => new RendererControlMessage(
                RendererControlMessage.Kind.Fullscreen,
                default,
                Flag: value != 0),
            _ => new RendererControlMessage(
                RendererControlMessage.Kind.Minimized,
                default,
                Flag: value != 0),
        };
        return true;
    }

    /// <summary>
    /// Blocks the render thread until a control message is pending or a stop is requested.
    /// </summary>
    /// <returns><see langword="true"/> when work is pending; <see langword="false"/> on stop.</returns>
    internal bool WaitForWork()
    {
        while (true)
        {
            if (IsStopRequested || Volatile.Read(ref _disposed) != 0)
            {
                return false;
            }

            if (HasPendingWork)
            {
                return true;
            }

            // Reset before re-checking so a post racing with the wait cannot be lost.
            if (!TryResetWake())
            {
                return false;
            }

            if (IsStopRequested || Volatile.Read(ref _disposed) != 0)
            {
                return false;
            }

            if (HasPendingWork)
            {
                return true;
            }

            try
            {
                _wake.Wait();
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Waits at most <paramref name="timeout"/> for a control message or stop (tests only).
    /// </summary>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when work is pending; <see langword="false"/> on stop/timeout.</returns>
    internal bool WaitForWork(TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (IsStopRequested || Volatile.Read(ref _disposed) != 0)
            {
                return false;
            }

            if (HasPendingWork)
            {
                return true;
            }

            if (!TryResetWake())
            {
                return false;
            }

            if (IsStopRequested || Volatile.Read(ref _disposed) != 0)
            {
                return false;
            }

            if (HasPendingWork)
            {
                return true;
            }

            TimeSpan remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            try
            {
                if (!_wake.Wait(remaining))
                {
                    return HasPendingWork;
                }
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }

    /// <summary>Publishes the fatal error once; later publications are ignored and the host is woken.</summary>
    /// <param name="exception">The fatal error.</param>
    internal void PublishFatal(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.CompareExchange(ref _fatal, exception, null) is not null)
        {
            return;
        }

        Signal();
    }

    /// <summary>Wakes any waiter and releases the wake event. Idempotent.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            // Wake first: Signal intentionally ignores a disposed channel.
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
            // Nothing to wake if the event is already gone.
        }

        _wake.Dispose();
    }

    private bool TryResetWake()
    {
        try
        {
            _wake.Reset();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // Dispose can race with the reset between the disposed check and this call.
            return false;
        }
    }

    private void Post(int index)
    {
        long sequence = Interlocked.Increment(ref _postSequence);
        Volatile.Write(ref _posted[index], sequence);
        Signal();
    }

    private void Signal()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
            // The channel was disposed by the joined host; there is no waiter left to wake.
        }
    }

    private static long Pack(WindowExtent extent)
        => ((long)(uint)extent.Width << 32) | (uint)extent.Height;

    private static WindowExtent Unpack(long value)
        => new((int)(value >> 32), (int)(value & 0xFFFF_FFFFL));
}
