namespace Bork2d.Render.Handoff;

/// <summary>
/// The render thread's combined event-based wait for a snapshot, a control message, a stop, or a
/// ring stop (M7 WP-3).
/// </summary>
/// <remarks>
/// <para>
/// The renderer must not poll: it blocks until the ring publishes a snapshot, the host posts a
/// window-state change, a stop is requested, or a renderer fatal stops the ring. The two wake
/// events (the ring's consumer wake and the control channel's wake) are borrowed, reset before the
/// wait, and re-checked after every wake so a signal racing with the reset is not lost.
/// </para>
/// <para>
/// The predicate order is deliberate: stop, then snapshot, then control. A snapshot published while
/// the renderer is also processing a control message stays <c>Ready</c> until the renderer takes it
/// in the next loop iteration, so exactly one GPU frame is recorded per consumed snapshot.
/// </para>
/// <para>
/// There is no timeout, deadline, <c>Thread.Sleep</c>, spin, or timed polling; the only blocking
/// call is <see cref="WaitHandle.WaitAny(WaitHandle[])"/> on the two wake handles. The wait type
/// owns no event: the ring and the control channel keep ownership and are never disposed while a
/// wait can be in flight.
/// </para>
/// </remarks>
internal sealed class SnapshotControlWait
{
    private readonly SnapshotRing _ring;
    private readonly RendererControlChannel _control;
    private readonly WaitHandle[] _handles;

    /// <summary>Initializes a new instance of the <see cref="SnapshotControlWait"/> class.</summary>
    /// <param name="ring">The renderer's snapshot ring.</param>
    /// <param name="control">The renderer's control channel.</param>
    internal SnapshotControlWait(SnapshotRing ring, RendererControlChannel control)
    {
        ArgumentNullException.ThrowIfNull(ring);
        ArgumentNullException.ThrowIfNull(control);
        _ring = ring;
        _control = control;
        _handles = [ring.ConsumerWake.WaitHandle, control.WakeEvent.WaitHandle];
    }

    /// <summary>
    /// Blocks until a snapshot is ready, a control message is pending, or a stop is observed.
    /// </summary>
    /// <returns>Why the wait returned.</returns>
    internal SnapshotWaitOutcome Wait()
    {
        while (true)
        {
            if (IsStopped)
            {
                return SnapshotWaitOutcome.Stopped;
            }

            if (_ring.HasReadySnapshot)
            {
                return SnapshotWaitOutcome.Snapshot;
            }

            if (_control.HasPendingWork)
            {
                return SnapshotWaitOutcome.Control;
            }

            // Reset before the final predicate check so a publish or post racing with the wait
            // cannot be lost; both producers signal after publishing the predicate.
            _ring.ConsumerWake.Reset();
            _control.WakeEvent.Reset();

            if (IsStopped)
            {
                return SnapshotWaitOutcome.Stopped;
            }

            if (_ring.HasReadySnapshot)
            {
                return SnapshotWaitOutcome.Snapshot;
            }

            if (_control.HasPendingWork)
            {
                return SnapshotWaitOutcome.Control;
            }

            try
            {
                WaitHandle.WaitAny(_handles);
            }
            catch (ObjectDisposedException)
            {
                // The renderer was shut down while waiting; treat it as a stop.
                return SnapshotWaitOutcome.Stopped;
            }
        }
    }

    private bool IsStopped
        => _control.IsStopRequested
            || _control.FatalException is not null
            || _ring.IsStopped;
}
