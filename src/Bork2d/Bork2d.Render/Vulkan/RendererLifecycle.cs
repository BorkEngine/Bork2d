using System.Diagnostics;
using Bork2d.Engine;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Handoff;
using Microsoft.Extensions.Logging;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The renderer lifecycle coordinator shared by the windowed and offscreen Vulkan renderers: the
/// render thread, readiness, status, stop reason, one-shot fatal error, progress signaling, handoff
/// delegations, counters, and the stats board.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Start"/> creates the one render thread and returns immediately; the target driver's
/// startup callback marks readiness with <see cref="MarkReady"/> after it created the device, target,
/// frames, and texture pages. The coordinator then runs the target frame loop, and on exit runs the
/// driver's cleanup, records the final status and stop reason, logs, wakes readiness waiters, and
/// signals progress. <see cref="Join"/> is the quiescent point and drains the snapshot ring only
/// after the render thread is gone.
/// </para>
/// <para>
/// Stop is a request followed by a join: <see cref="RequestStop"/> stops the ring, closes pending
/// capture requests, posts the control stop, and moves a starting/ready renderer to
/// <see cref="RendererStatus.Stopping"/>. A fatal publishes the one-shot failure, wakes a blocked
/// producer, and faults pending captures; it always wins over a later requested stop.
/// </para>
/// <para>
/// The coordinator never touches Vulkan and never owns the target driver's device, target, or
/// frames; those stay target-owned. It does own the snapshot ring/consumer, the control channel, the
/// combined wait, the readiness/progress events, the public counters, and the stats board.
/// </para>
/// </remarks>
internal sealed class RendererLifecycle
{
    private const string ThreadName = "Bork2d.Render";

    private readonly ILogger _logger;
    private readonly RendererCaptureService _captures;
    private readonly ManualResetEventSlim _ready = new(initialState: false);
    private readonly ManualResetEventSlim _progress = new(initialState: false);
    private readonly RenderStatsBoard _stats = new();
    private readonly SnapshotConsumer _consumer;

    private Thread? _thread;
    private int _started;
    private int _status = (int)RendererStatus.NotStarted;
    private int _stopReason = (int)RendererStopReason.None;
    private int _readyFlag;
    private long _framesPresented;
    private long _framesDiscarded;
    private long _swapchainRecreations;
    private int _validationErrorCount;
    private int _validationWarningCount;
    private long _lastFrameNumber;
    private int _lastPresentResult;

    /// <summary>Initializes a new instance of the <see cref="RendererLifecycle"/> class.</summary>
    /// <param name="logger">The <c>Bork2d.Render</c> logger.</param>
    /// <param name="captures">The capture service the stop/fatal path closes and faults.</param>
    /// <param name="framesInFlight">The number of GPU frames in flight the consumer tracks.</param>
    internal RendererLifecycle(ILogger logger, RendererCaptureService captures, int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(captures);
        _logger = logger;
        _captures = captures;
        _consumer = new SnapshotConsumer(new SnapshotRing(), framesInFlight);
        Control = new RendererControlChannel();
        Wait = new SnapshotControlWait(Consumer.Ring, Control);
    }

    /// <summary>Gets the control channel the host posts window state and stop/fatal to.</summary>
    internal RendererControlChannel Control { get; }

    /// <summary>Gets the combined snapshot/control wait the render thread blocks on.</summary>
    internal SnapshotControlWait Wait { get; }

    /// <summary>Gets the snapshot consumer that owns the ring and per-frame slot references.</summary>
    internal SnapshotConsumer Consumer => _consumer;

    /// <summary>Gets a value indicating whether the renderer reached readiness.</summary>
    internal bool IsReady => Volatile.Read(ref _readyFlag) != 0;

    /// <summary>Gets the current lifecycle state.</summary>
    internal RendererStatus Status => (RendererStatus)Volatile.Read(ref _status);

    /// <summary>Gets why the renderer stopped, or <see cref="RendererStopReason.None"/>.</summary>
    internal RendererStopReason StopReason => (RendererStopReason)Volatile.Read(ref _stopReason);

    /// <summary>Gets the latest frame/validation counters.</summary>
    internal RendererCounters Counters => new(
        FramesPresented: Volatile.Read(ref _framesPresented),
        FramesDiscarded: Volatile.Read(ref _framesDiscarded),
        SwapchainRecreations: Volatile.Read(ref _swapchainRecreations),
        ValidationErrorCount: Volatile.Read(ref _validationErrorCount),
        ValidationWarningCount: Volatile.Read(ref _validationWarningCount),
        LastFrameNumber: Volatile.Read(ref _lastFrameNumber),
        LastPresentResult: Volatile.Read(ref _lastPresentResult));

    /// <summary>Gets the last completed frame's public stats snapshot.</summary>
    internal RenderStats RenderStats => _stats.Current;

    /// <summary>Gets the one-shot fatal error observed by the host at a tick boundary.</summary>
    internal Exception? FatalException => Control.FatalException;

    /// <summary>Gets a snapshot of the handoff counters for the stop log and tests.</summary>
    internal SnapshotRingCounters SnapshotCounters => Consumer.Counters;

    /// <summary>Gets the number of snapshots drained without GPU work while minimized.</summary>
    internal long SuspendedConsumptions => Consumer.SuspendedConsumptions;

    /// <summary>Gets a value indicating whether a stop was requested or the ring stopped.</summary>
    internal bool IsStopping => Control.IsStopRequested || Consumer.IsStopped;

    /// <summary>Gets the cumulative fatal validation error count.</summary>
    internal int ValidationErrorCount => Volatile.Read(ref _validationErrorCount);

    /// <summary>Gets the cumulative validation warning count.</summary>
    internal int ValidationWarningCount => Volatile.Read(ref _validationWarningCount);

    /// <summary>
    /// Starts the one render thread and returns without waiting for readiness.
    /// </summary>
    /// <param name="startup">
    /// Target startup; returns <see langword="false"/> when a stop was requested before readiness, so
    /// the frame loop is skipped.
    /// </param>
    /// <param name="frames">The target frame loop.</param>
    /// <param name="cleanup">Target teardown, run once when the thread exits.</param>
    /// <param name="publishFatal">Target fatal wrapping and logging, run before the final state.</param>
    /// <param name="logStopped">Target stop log, run after cleanup.</param>
    /// <exception cref="InvalidOperationException">The renderer was already started.</exception>
    internal void Start(
        Func<bool> startup,
        Action frames,
        Action cleanup,
        Action<Exception> publishFatal,
        Action logStopped)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(cleanup);
        ArgumentNullException.ThrowIfNull(publishFatal);
        ArgumentNullException.ThrowIfNull(logStopped);

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The Vulkan renderer was already started.");
        }

        SetStatus(RendererStatus.Starting);
        var thread = new Thread(
            () => ThreadMain(startup, frames, cleanup, publishFatal, logStopped))
        {
            IsBackground = true,
            Name = ThreadName,
        };
        _thread = thread;
        thread.Start();
    }

    /// <summary>Marks the renderer ready and wakes readiness waiters; called once by target startup.</summary>
    internal void MarkReady()
    {
        Volatile.Write(ref _readyFlag, 1);
        SetStatus(RendererStatus.Ready);
        _ready.Set();
    }

    /// <summary>Waits without a timeout until readiness or a fatal error.</summary>
    internal void WaitForReady()
    {
        if (IsReady)
        {
            return;
        }

        _ready.Wait();
    }

    /// <summary>Waits at most <paramref name="timeout"/> for readiness (tests only).</summary>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when ready; <see langword="false"/> on timeout.</returns>
    internal bool WaitForReady(TimeSpan timeout) => IsReady || _ready.Wait(timeout);

    /// <summary>Acquires a free snapshot slot for the producer.</summary>
    /// <returns>The slot to fill, or <see langword="null"/> on stop/exit.</returns>
    internal SnapshotSlot? AcquireSnapshotSlot() => Consumer.Ring.Acquire();

    /// <summary>Publishes a filled slot as the newest snapshot.</summary>
    /// <param name="slot">The slot returned by <see cref="AcquireSnapshotSlot"/>.</param>
    internal void PublishSnapshot(SnapshotSlot slot) => Consumer.Ring.Publish(slot);

    /// <summary>Returns an unpublished slot after a tick or capture failure.</summary>
    /// <param name="slot">The slot returned by <see cref="AcquireSnapshotSlot"/>.</param>
    internal void AbandonSnapshotSlot(SnapshotSlot slot) => Consumer.Ring.Abandon(slot);

    /// <summary>Wakes a producer blocked in <see cref="AcquireSnapshotSlot"/> on exit.</summary>
    internal void WakeSnapshotWait() => Consumer.Ring.SignalExit();

    /// <summary>Requests a graceful stop; returns immediately.</summary>
    internal void RequestStop()
    {
        Volatile.Write(ref _readyFlag, 0);
        Consumer.Ring.Stop();
        _captures.CloseForStop();
        Control.RequestStop();
        if (Volatile.Read(ref _stopReason) != (int)RendererStopReason.Fatal)
        {
            SetStopReason(RendererStopReason.Requested);
            RendererStatus status = Status;
            if (status is RendererStatus.Starting or RendererStatus.Ready)
            {
                SetStatus(RendererStatus.Stopping);
            }
        }

        _ready.Set();
        SignalProgress();
    }

    /// <summary>Waits for the renderer thread to join, then drains the snapshot ring.</summary>
    internal void Join()
    {
        _thread?.Join();
        DrainSnapshotsAfterJoin();
    }

    /// <summary>Waits at most <paramref name="timeout"/> for the renderer thread to join.</summary>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when joined; <see langword="false"/> on timeout.</returns>
    internal bool Join(TimeSpan timeout)
    {
        Thread? thread = _thread;
        if (thread is null)
        {
            DrainSnapshotsAfterJoin();
            return true;
        }

        if (!thread.Join(timeout))
        {
            return false;
        }

        DrainSnapshotsAfterJoin();
        return true;
    }

    /// <summary>
    /// Waits at most <paramref name="timeout"/> for a renderer predicate (GPU tests only;
    /// event-based, no polling or sleeps).
    /// </summary>
    /// <param name="predicate">The condition to observe.</param>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when the predicate held; <see langword="false"/> on timeout.</returns>
    internal bool WaitForProgress(Func<bool> predicate, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (predicate())
            {
                return true;
            }

            _progress.Reset();
            if (predicate())
            {
                return true;
            }

            TimeSpan remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            if (!_progress.Wait(remaining))
            {
                return predicate();
            }
        }
    }

    /// <summary>
    /// Waits at most <paramref name="timeout"/> for the counters to satisfy a predicate
    /// (GPU tests only; event-based, no polling or sleeps).
    /// </summary>
    /// <param name="predicate">The counter condition to observe.</param>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when the predicate held; <see langword="false"/> on timeout.</returns>
    internal bool WaitForProgress(Func<RendererCounters, bool> predicate, TimeSpan timeout)
        => WaitForProgress(() => predicate(Counters), timeout);

    /// <summary>Updates the cumulative validation counters from the instance seam.</summary>
    /// <param name="instance">The instance to observe, or <see langword="null"/> when none exists.</param>
    internal void UpdateValidationCounters(IVulkanInstance? instance)
    {
        if (instance is not { } value)
        {
            return;
        }

        Volatile.Write(ref _validationErrorCount, value.ValidationErrorCount);
        Volatile.Write(ref _validationWarningCount, value.ValidationWarningCount);
    }

    /// <summary>
    /// Refreshes the validation counters and turns a validation fatal into a renderer fatal.
    /// </summary>
    /// <param name="instance">The instance to observe, or <see langword="null"/> when none exists.</param>
    /// <exception cref="Bork2dException">Validation reported a fatal error.</exception>
    internal void ObserveValidation(IVulkanInstance? instance)
    {
        UpdateValidationCounters(instance);
        if (instance?.FatalException is { } fatal)
        {
            throw fatal as Bork2dException
                ?? new Bork2dException("Vulkan validation reported a fatal error.", fatal);
        }
    }

    /// <summary>Publishes the completed frame's stats snapshot.</summary>
    /// <param name="sequence">The snapshot sequence the frame consumed.</param>
    /// <param name="width">The target/framebuffer width in pixels.</param>
    /// <param name="height">The target/framebuffer height in pixels.</param>
    /// <param name="stats">The planned frame's aggregate render-pass statistics.</param>
    /// <param name="texturePages">The resident pages, including the placeholder.</param>
    /// <param name="texturePageCapacity">The current bindless page capacity, including the placeholder.</param>
    internal void PublishStats(
        long sequence,
        int width,
        int height,
        RenderPassStats stats,
        int texturePages,
        int texturePageCapacity)
    {
        RenderStats renderStats = FrameRenderStats.Create(
            sequence,
            width,
            height,
            stats,
            texturePages,
            texturePageCapacity,
            Volatile.Read(ref _validationErrorCount),
            Volatile.Read(ref _validationWarningCount));
        _stats.Publish(in renderStats);
    }

    /// <summary>Records one windowed frame presented.</summary>
    internal void IncrementFramesPresented() => Interlocked.Increment(ref _framesPresented);

    /// <summary>Records one offscreen frame rendered and discarded.</summary>
    internal void IncrementFramesDiscarded() => Interlocked.Increment(ref _framesDiscarded);

    /// <summary>Records one swapchain recreation.</summary>
    internal void IncrementSwapchainRecreations() => Interlocked.Increment(ref _swapchainRecreations);

    /// <summary>Records that one frame began.</summary>
    internal void IncrementLastFrameNumber() => Interlocked.Increment(ref _lastFrameNumber);

    /// <summary>Records the last present result as its numeric Vulkan result value.</summary>
    /// <param name="result">The numeric VkResult value.</param>
    internal void SetLastPresentResult(int result) => Volatile.Write(ref _lastPresentResult, result);

    /// <summary>Sets the lifecycle status.</summary>
    /// <param name="status">The new status.</param>
    private void SetStatus(RendererStatus status) => Volatile.Write(ref _status, (int)status);

    /// <summary>Sets the stop reason, keeping a fatal or earlier requested stop.</summary>
    /// <param name="reason">The observed stop reason.</param>
    private void SetStopReason(RendererStopReason reason)
    {
        while (true)
        {
            var current = (RendererStopReason)Volatile.Read(ref _stopReason);
            if (current == RendererStopReason.Fatal
                || (current == RendererStopReason.Requested
                    && reason != RendererStopReason.Fatal))
            {
                return;
            }

            if (Interlocked.CompareExchange(
                ref _stopReason,
                (int)reason,
                (int)current) == (int)current)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Publishes a fatal failure once: stops the ring, faults pending captures, publishes the error,
    /// and moves the lifecycle to faulted.
    /// </summary>
    /// <param name="fatal">The wrapped renderer fatal.</param>
    internal void PublishFatal(Exception fatal)
    {
        ArgumentNullException.ThrowIfNull(fatal);

        // A renderer fatal wakes a blocked producer, stops further acquisition, and faults pending
        // capture requests (M8 §6.12).
        Consumer.Ring.Stop();
        _captures.Close(fatal);
        Control.PublishFatal(fatal);
        SetStopReason(RendererStopReason.Fatal);
        SetStatus(RendererStatus.Faulted);
    }

    /// <summary>
    /// Releases every snapshot slot after the device became idle, before a swapchain recreation or
    /// shutdown; best-effort and logged.
    /// </summary>
    /// <param name="operation">The operation releasing the slots, for diagnostics.</param>
    internal void ReleaseHeldSnapshots(string operation)
    {
        try
        {
            Consumer.ReleaseAll();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Vulkan snapshot-slot release failed during {Operation}.",
                operation);
        }
    }

    /// <summary>Signals progress waiters after a completed frame or a state change.</summary>
    internal void SignalProgress() => _progress.Set();

    /// <summary>
    /// Frees every non-<c>Writing</c> slot once the render thread has joined.
    /// </summary>
    /// <remarks>
    /// Join is the quiescent point (Q9=I1): a fatal mid-tick may publish after the renderer's
    /// cleanup, so the ring cannot be fully drained until the render thread is gone. A slot still
    /// <c>Writing</c> belongs to a producer and is never touched.
    /// </remarks>
    private void DrainSnapshotsAfterJoin()
    {
        try
        {
            Consumer.Ring.DrainAfterRenderThreadJoined();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan snapshot-ring drain after join failed.");
        }
    }

    private void ThreadMain(
        Func<bool> startup,
        Action frames,
        Action cleanup,
        Action<Exception> publishFatal,
        Action logStopped)
    {
        var faulted = false;
        try
        {
            if (startup())
            {
                frames();
            }

            SetStopReason(RendererStopReason.Requested);
        }
        catch (Exception exception)
        {
            faulted = true;
            publishFatal(exception);
        }
        finally
        {
            cleanup();
            Volatile.Write(ref _readyFlag, 0);
            if (faulted)
            {
                SetStopReason(RendererStopReason.Fatal);
                SetStatus(RendererStatus.Faulted);
            }
            else
            {
                SetStatus(RendererStatus.Stopped);
            }

            logStopped();
            _ready.Set();
            SignalProgress();
        }
    }
}
