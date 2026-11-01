using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Handoff;
using Microsoft.Extensions.Logging;

namespace Bork2d.Render;

/// <summary>
/// The internal renderer composition contract consumed by <c>Bork2d.Runtime</c> (D-M6-25).
/// </summary>
/// <remarks>
/// <para>
/// The interface is deliberately free of Vulkan, SDL, and window types: the host passes opaque
/// <see cref="nint"/> instance/surface handles, posts coalesced window-state changes, observes
/// readiness, status, counters, and a one-shot fatal error, and (M7 WP-4) drives the internal
/// snapshot handoff through <see cref="AcquireSnapshotSlot"/> /
/// <see cref="PublishSnapshot"/> / <see cref="AbandonSnapshotSlot"/> /
/// <see cref="WakeSnapshotWait"/>. Tests inject a fake implementation.
/// </para>
/// <para>
/// <see cref="Start"/> starts the renderer thread without waiting for readiness (D-M6-06). The
/// production host waits for the first tick with <see cref="WaitForReady()"/>; the bounded
/// overload exists for tests only and no production path uses a timeout. Stop is a request
/// followed by a join; no renderer thread survives <see cref="Join()"/>.
/// </para>
/// </remarks>
internal interface IRenderer
{
    /// <summary>Starts the renderer and returns without waiting for readiness.</summary>
    void Start();

    /// <summary>Gets a value indicating whether the renderer reached readiness.</summary>
    bool IsReady { get; }

    /// <summary>Waits without a timeout until readiness or a fatal error.</summary>
    void WaitForReady();

    /// <summary>Waits at most <paramref name="timeout"/> for readiness (tests only).</summary>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when ready; <see langword="false"/> on timeout.</returns>
    bool WaitForReady(TimeSpan timeout);

    /// <summary>Gets the current lifecycle state.</summary>
    RendererStatus Status { get; }

    /// <summary>Gets why the renderer stopped, or <see cref="RendererStopReason.None"/>.</summary>
    RendererStopReason StopReason { get; }

    /// <summary>Gets the latest frame/validation counters.</summary>
    RendererCounters Counters { get; }

    /// <summary>
    /// Gets the last completed frame's public stats snapshot (M8 §6.9), or
    /// <see langword="default"/> before the first completed frame.
    /// </summary>
    /// <remarks>
    /// The runtime installs this as the engine's stats source, so the tick thread reads it without
    /// touching Vulkan. The property is thread-safe, allocation-free, and never blocks on the GPU;
    /// the renderer publishes once per completed frame.
    /// </remarks>
    RenderStats RenderStats { get; }

    /// <summary>
    /// Requests the next completed frame as a <see cref="FrameCaptureResult"/> for
    /// <c>IEngine.CaptureFrameAsync</c> (M8 §6.9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The request is queued thread-safely. The render thread records the image-to-buffer copy after
    /// the last camera pass and before present/discard, and completes the task from the serving
    /// frame's fence, even when no further snapshot is published. The result carries the frame's
    /// actual extent, <see cref="TextureFormat.Rgba8"/>, and row-major top-left-origin RGBA8 pixels:
    /// one <see cref="byte"/> array per captured frame shared by every request that frame serves,
    /// while staging memory is pooled per GPU frame.
    /// </para>
    /// <para>
    /// At most eight requests may be outstanding; a request beyond the limit faults with
    /// <see cref="Bork2dException"/> instead of blocking or being dropped. Cancellation is checked
    /// first and faults the task with <see cref="OperationCanceledException"/>; a stop or fatal
    /// faults pending requests.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The captured frame's pixels, extent, and format.</returns>
    Task<FrameCaptureResult> CaptureFrameAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the one-shot fatal error observed by the host at a tick boundary.</summary>
    Exception? FatalException { get; }

    /// <summary>Posts the latest framebuffer extent; the latest value wins.</summary>
    /// <param name="extent">The confirmed framebuffer extent in pixels.</param>
    void PostWindowExtent(WindowExtent extent);

    /// <summary>Posts the latest fullscreen state; the latest value wins.</summary>
    /// <param name="fullscreen">Whether the window is fullscreen.</param>
    void PostFullscreen(bool fullscreen);

    /// <summary>Posts the latest minimized state; the latest value wins.</summary>
    /// <param name="minimized">Whether the window is minimized.</param>
    void PostMinimized(bool minimized);

    /// <summary>
    /// Acquires a free snapshot slot for the producer (Q1=A1).
    /// </summary>
    /// <remarks>
    /// Blocks only when all three slots are occupied (bounded run-ahead); the wait is event-based
    /// with no spin, sleep, or timed polling. Returns <see langword="null"/> when a stop, exit
    /// signal, or renderer fatal is observed, so the caller starts no further tick; an already
    /// acquired slot may still be published.
    /// </remarks>
    /// <returns>The slot to fill, or <see langword="null"/> on stop/exit.</returns>
    SnapshotSlot? AcquireSnapshotSlot();

    /// <summary>Publishes a filled slot as the newest snapshot; the previous <c>Ready</c> is dropped.</summary>
    /// <param name="slot">The slot returned by <see cref="AcquireSnapshotSlot"/>.</param>
    void PublishSnapshot(SnapshotSlot slot);

    /// <summary>
    /// Returns an unpublished slot after a <c>Tick</c> or capture failure; nothing is published.
    /// </summary>
    /// <param name="slot">The slot returned by <see cref="AcquireSnapshotSlot"/>.</param>
    void AbandonSnapshotSlot(SnapshotSlot slot);

    /// <summary>
    /// Wakes a producer blocked in <see cref="AcquireSnapshotSlot"/> on <c>IEngine.Exit()</c>.
    /// </summary>
    /// <remarks>
    /// Consumption continues and a producer that already owns a slot may still publish the Exit
    /// tick's snapshot (Q4=D1, Q9=I1). The host calls this from its exit path in M7 WP-4.
    /// </remarks>
    void WakeSnapshotWait();

    /// <summary>Gets a snapshot of the handoff counters for the stop log and tests.</summary>
    SnapshotRingCounters SnapshotCounters { get; }

    /// <summary>Gets the number of snapshots drained without GPU work while minimized (Q8).</summary>
    long SuspendedConsumptions { get; }

    /// <summary>Requests a graceful stop; returns immediately.</summary>
    void RequestStop();

    /// <summary>Waits for the renderer thread to join.</summary>
    void Join();

    /// <summary>Waits at most <paramref name="timeout"/> for the renderer thread to join.</summary>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when joined; <see langword="false"/> on timeout.</returns>
    bool Join(TimeSpan timeout);
}
