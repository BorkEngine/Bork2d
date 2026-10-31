using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Handoff;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A deterministic <see cref="IRenderer"/> used by host tests instead of Vulkan.
/// </summary>
/// <remarks>
/// <para>
/// Readiness, readiness failure, and a post-start fatal are explicit calls, so tests never sleep
/// or poll. Waits are bounded overloads; every stop path signals the join event.
/// </para>
/// <para>
/// The fake owns a real <see cref="SnapshotRing"/>, so the M7 WP-4 host tests exercise the real
/// acquire/publish/abandon/wake contract. <see cref="AutoConsume"/> (the default) takes and
/// releases each published snapshot immediately, which models a renderer that keeps up and leaves
/// existing host tests unblocked; clearing it models a renderer that holds its snapshots to the GPU
/// fence, so two stay <c>Rendering</c>, the third stays <c>Ready</c>, and the next acquire blocks.
/// </para>
/// </remarks>
internal sealed class FakeRenderer : IRenderer
{
    private const int RendererFramesInFlight = 2;

    private readonly ManualResetEventSlim _ready = new(initialState: false);
    private readonly ManualResetEventSlim _stopped = new(initialState: false);
    private readonly ManualResetEventSlim _posted = new(initialState: false);
    private readonly SnapshotRing _ring = new();
    private readonly List<SnapshotSlot> _heldSlots = [];
    private Exception? _fatalException;
    private int _status = (int)RendererStatus.NotStarted;
    private int _stopReason = (int)RendererStopReason.None;
    private bool _isReady;
    private int _stopRequested;
    private int _joined;
    private int _waitForReadyCount;
    private long _suspendedConsumptions;

    /// <summary>Gets or sets a value indicating whether <see cref="Start"/> becomes ready.</summary>
    internal bool AutoReady { get; set; }

    /// <summary>Gets or sets the exception <see cref="Start"/> throws synchronously.</summary>
    internal Exception? StartFailure { get; set; }

    /// <summary>
    /// Gets or sets a callback invoked when the host starts waiting for readiness, before the wait
    /// blocks. Tests use it to assert the host ordering and to signal readiness deterministically.
    /// </summary>
    internal Action? OnWaitForReady { get; set; }

    /// <summary>Gets the number of times the host waited for readiness.</summary>
    internal int WaitForReadyCount => Volatile.Read(ref _waitForReadyCount);

    /// <summary>Gets or sets a value indicating whether <see cref="RequestStop"/> completes.</summary>
    internal bool StopOnRequest { get; set; } = true;

    /// <summary>Gets a value indicating whether <see cref="Start"/> was called.</summary>
    internal bool Started { get; private set; }

    /// <summary>Gets a value indicating whether a stop was requested.</summary>
    internal bool StopRequested => Volatile.Read(ref _stopRequested) != 0;

    /// <inheritdoc/>
    public SnapshotRingCounters SnapshotCounters => _ring.Counters;

    /// <inheritdoc/>
    public long SuspendedConsumptions => Interlocked.Read(ref _suspendedConsumptions);

    /// <inheritdoc/>
    public SnapshotSlot? AcquireSnapshotSlot() => _ring.Acquire();

    /// <inheritdoc/>
    public void PublishSnapshot(SnapshotSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        _ring.Publish(slot);
        if (AutoConsume)
        {
            SnapshotSlot? taken = _ring.TakeLatest();
            if (taken is not null)
            {
                _ring.Release(taken);
            }

            return;
        }

        // Hold mode: model two GPU frames in flight that keep their snapshot slot until released.
        if (_heldSlots.Count < RendererFramesInFlight)
        {
            SnapshotSlot? taken = _ring.TakeLatest();
            if (taken is not null)
            {
                _heldSlots.Add(taken);
            }
        }
    }

    /// <inheritdoc/>
    public void AbandonSnapshotSlot(SnapshotSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        _ring.Abandon(slot);
    }

    /// <inheritdoc/>
    public void WakeSnapshotWait() => _ring.SignalExit();

    /// <summary>Gets the number of snapshots the hold mode currently keeps <c>Rendering</c>.</summary>
    internal int HeldSnapshotCount => _heldSlots.Count;

    /// <summary>Releases the snapshots the hold mode kept <c>Rendering</c> (a simulated fence).</summary>
    internal void ReleaseHeldSnapshots()
    {
        foreach (SnapshotSlot slot in _heldSlots)
        {
            if (slot.State == SnapshotSlotState.Rendering)
            {
                _ring.Release(slot);
            }
        }

        _heldSlots.Clear();
    }

    /// <summary>
    /// Drains the newest <c>Ready</c> snapshot without a GPU frame, modelling the suspended
    /// (minimized) path for host tests.
    /// </summary>
    /// <returns><see langword="true"/> when a snapshot was dropped.</returns>
    internal bool DrainSuspendedSnapshot()
    {
        if (!_ring.DiscardLatest())
        {
            return false;
        }

        Interlocked.Increment(ref _suspendedConsumptions);
        return true;
    }

    /// <summary>Gets a value indicating whether the renderer was joined.</summary>
    internal bool Joined => Volatile.Read(ref _joined) != 0;

    /// <summary>Gets the number of extent posts.</summary>
    internal int ExtentPostCount { get; private set; }

    /// <summary>Gets the number of fullscreen posts.</summary>
    internal int FullscreenPostCount { get; private set; }

    /// <summary>Gets the number of minimized posts.</summary>
    internal int MinimizedPostCount { get; private set; }

    /// <summary>Gets the latest posted extent.</summary>
    internal WindowExtent? LastExtent { get; private set; }

    /// <summary>Gets the latest posted fullscreen value.</summary>
    internal bool? LastFullscreen { get; private set; }

    /// <summary>Gets the latest posted minimized value.</summary>
    internal bool? LastMinimized { get; private set; }

    /// <summary>Gets or sets the counters reported to the host.</summary>
    internal RendererCounters Counters { get; set; } = RendererCounters.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether publishing auto-consumes the snapshot (default
    /// <see langword="true"/>).
    /// </summary>
    /// <remarks>
    /// With auto-consume on, each publish is taken and released immediately, so the producer never
    /// blocks. With it off, the fake models a renderer that holds its snapshots to the GPU fence:
    /// up to two published snapshots stay <c>Rendering</c>, a third stays <c>Ready</c>, and the
    /// next acquire blocks until a stop or <see cref="ReleaseHeldSnapshots"/> (WP-4 backpressure
    /// tests).
    /// </remarks>
    internal bool AutoConsume { get; set; } = true;

    /// <summary>Gets the ring the fake exposes to tests.</summary>
    internal SnapshotRing SnapshotRing => _ring;

    /// <inheritdoc/>
    public bool IsReady => _isReady;

    /// <inheritdoc/>
    public RendererStatus Status => (RendererStatus)Volatile.Read(ref _status);

    /// <inheritdoc/>
    public RendererStopReason StopReason => (RendererStopReason)Volatile.Read(ref _stopReason);

    /// <inheritdoc/>
    RendererCounters IRenderer.Counters => Counters;

    /// <inheritdoc/>
    public Exception? FatalException => Volatile.Read(ref _fatalException);

    /// <inheritdoc/>
    public void Start()
    {
        if (Started)
        {
            throw new InvalidOperationException("The fake renderer was already started.");
        }

        Started = true;
        SetStatus(RendererStatus.Starting);
        if (StartFailure is not null)
        {
            _ring.Stop();
            Volatile.Write(ref _fatalException, StartFailure);
            SetStatus(RendererStatus.Faulted);
            Volatile.Write(ref _stopReason, (int)RendererStopReason.Fatal);
            _ready.Set();
            CompleteStop();
            throw StartFailure;
        }

        if (AutoReady)
        {
            SetReady();
        }
    }

    /// <summary>Signals readiness.</summary>
    internal void SetReady()
    {
        _isReady = true;
        SetStatus(RendererStatus.Ready);
        _ready.Set();
    }

    /// <summary>Signals a readiness failure and completes the renderer.</summary>
    /// <param name="exception">The failure the host observes.</param>
    internal void FailReadiness(Exception exception) => PublishFatal(exception);

    /// <summary>Publishes a post-start fatal error and completes the renderer.</summary>
    /// <param name="exception">The failure the host observes.</param>
    internal void PublishFatal(Exception exception)
    {
        _ring.Stop();
        Volatile.Write(ref _fatalException, exception);
        _isReady = false;
        SetStatus(RendererStatus.Faulted);
        Volatile.Write(ref _stopReason, (int)RendererStopReason.Fatal);
        _ready.Set();
        CompleteStop();
    }

    /// <summary>Completes the fake stop once the test's in-flight work is done.</summary>
    internal void CompleteStop()
    {
        if (Status != RendererStatus.Faulted)
        {
            SetStatus(RendererStatus.Stopped);
        }

        _stopped.Set();
    }

    /// <inheritdoc/>
    public void WaitForReady()
    {
        Interlocked.Increment(ref _waitForReadyCount);
        OnWaitForReady?.Invoke();
        _ready.Wait();
    }

    /// <inheritdoc/>
    public bool WaitForReady(TimeSpan timeout)
    {
        Interlocked.Increment(ref _waitForReadyCount);
        OnWaitForReady?.Invoke();
        return _ready.Wait(timeout);
    }

    /// <inheritdoc/>
    public void PostWindowExtent(WindowExtent extent)
    {
        ExtentPostCount++;
        LastExtent = extent;
        _posted.Set();
    }

    /// <inheritdoc/>
    public void PostFullscreen(bool fullscreen)
    {
        FullscreenPostCount++;
        LastFullscreen = fullscreen;
        _posted.Set();
    }

    /// <inheritdoc/>
    public void PostMinimized(bool minimized)
    {
        MinimizedPostCount++;
        LastMinimized = minimized;
        _posted.Set();
    }

    /// <summary>
    /// Waits at most <paramref name="timeout"/> for a control post to satisfy a predicate
    /// (tests only; event-based, no polling or sleeps).
    /// </summary>
    /// <param name="predicate">The post condition to observe.</param>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when the predicate held; <see langword="false"/> on timeout.</returns>
    internal bool WaitForPosts(Func<bool> predicate, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (predicate())
            {
                return true;
            }

            _posted.Reset();
            if (predicate())
            {
                return true;
            }

            TimeSpan remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            if (!_posted.Wait(remaining))
            {
                return predicate();
            }
        }
    }

    /// <inheritdoc/>
    public void RequestStop()
    {
        _ring.Stop();
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
        {
            return;
        }

        if (Status != RendererStatus.Faulted)
        {
            SetStatus(RendererStatus.Stopping);
            Volatile.Write(ref _stopReason, (int)RendererStopReason.Requested);
        }

        if (StopOnRequest)
        {
            CompleteStop();
        }
    }

    /// <inheritdoc/>
    public void Join()
    {
        if (Started)
        {
            _stopped.Wait();
        }

        DrainSnapshotsAfterJoin();
        Volatile.Write(ref _joined, 1);
    }

    /// <inheritdoc/>
    public bool Join(TimeSpan timeout)
    {
        if (Started && !_stopped.Wait(timeout))
        {
            return false;
        }

        DrainSnapshotsAfterJoin();
        Volatile.Write(ref _joined, 1);
        return true;
    }

    private void DrainSnapshotsAfterJoin()
    {
        ReleaseHeldSnapshots();
        _ring.DrainAfterRenderThreadJoined();
    }

    private void SetStatus(RendererStatus status) => Volatile.Write(ref _status, (int)status);
}
