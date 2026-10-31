using Bork2d.Engine.Rendering;

namespace Bork2d.Render.Handoff;

/// <summary>
/// The three-slot snapshot ring: ownership of the slots, <c>latest</c>, publication, take/release,
/// wait/wake, and handoff counters (D-003, Q1=A1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Protocol.</b> The tick thread is the only producer: <see cref="Acquire"/> takes a
/// <see cref="SnapshotSlotState.Free"/> slot into <see cref="SnapshotSlotState.Writing"/>,
/// <see cref="Publish"/> makes it <see cref="SnapshotSlotState.Ready"/> and the newest snapshot,
/// and <see cref="Abandon"/> returns an unpublishable slot to <see cref="SnapshotSlotState.Free"/>.
/// The render thread is the only consumer: <see cref="TakeLatest"/> takes the newest
/// <see cref="SnapshotSlotState.Ready"/> slot into <see cref="SnapshotSlotState.Rendering"/> and
/// <see cref="Release"/> frees it once the consuming GPU frame's fence has signaled. A newer
/// publish supersedes at most the immediately previous <see cref="SnapshotSlotState.Ready"/> slot
/// and never a <see cref="SnapshotSlotState.Rendering"/> slot, which bounds run-ahead to the ring.
/// </para>
/// <para>
/// <b>State and memory ordering.</b> Slot states and <c>latest</c> are <see cref="int"/> fields
/// moved with <see cref="Interlocked"/> and read with <see cref="Volatile"/>: publishing is a
/// release, taking is an acquire, and the consumer's <c>latest</c> clear is a compare-exchange so
/// a concurrently published newer snapshot survives. No lock is taken on the hot path and no lock
/// is held across a wait.
/// </para>
/// <para>
/// <b>Waiting.</b> <see cref="Acquire"/> blocks only when no slot is free, and
/// <see cref="WaitForSnapshotOrStop"/> blocks until a snapshot is ready or the ring stops. Both
/// waits are event-based; there is no <c>Thread.Sleep</c>, <c>SpinWait</c>, deadline, or timed
/// polling. A stop (including a renderer fatal, which the renderer maps to <see cref="Stop"/>)
/// wakes both sides; <see cref="SignalExit"/> wakes only the producer so a blocked tick returns
/// without starting another tick, while consumption and a final Exit-tick publish stay legal
/// (Q4=D1, Q9=I1).
/// </para>
/// </remarks>
internal sealed class SnapshotRing : IDisposable
{
    private const int NoLatest = -1;

    /// <summary>The fixed number of slots in the ring.</summary>
    internal const int SlotCapacity = 3;

    private readonly SnapshotSlot[] _slots;
    private readonly ManualResetEventSlim _producerWake = new(initialState: false);
    private readonly ManualResetEventSlim _consumerWake = new(initialState: false);
    private int _latest = NoLatest;
    private int _stopped;
    private int _exitSignalled;
    private int _disposed;
    private long _published;
    private long _consumed;
    private long _superseded;
    private long _backpressureWaits;
    private long _stopWakes;
    private long _exitWakes;
    private int _maxLive;

    /// <summary>Initializes a new instance of the <see cref="SnapshotRing"/> class.</summary>
    /// <remarks>Creates exactly three free slots, each with its own empty snapshot buffer.</remarks>
    internal SnapshotRing()
    {
        _slots =
        [
            new SnapshotSlot(0, new FrameSnapshot()),
            new SnapshotSlot(1, new FrameSnapshot()),
            new SnapshotSlot(2, new FrameSnapshot()),
        ];
    }

    /// <summary>Gets the slots in index order (tests and the drain path).</summary>
    internal ReadOnlySpan<SnapshotSlot> Slots => _slots;

    /// <summary>
    /// Gets the index of the newest published snapshot, or <c>-1</c> when nothing is published.
    /// </summary>
    /// <remarks>
    /// The value is <c>-1</c> or the index of a <see cref="SnapshotSlotState.Ready"/> or
    /// <see cref="SnapshotSlotState.Rendering"/> slot; it is never a <see cref="SnapshotSlotState.Writing"/>
    /// or <see cref="SnapshotSlotState.Free"/> slot. The consumer clears it when it takes that slot.
    /// </remarks>
    internal int LatestIndex => Volatile.Read(ref _latest);

    /// <summary>Gets a value indicating whether a stop was requested.</summary>
    internal bool IsStopped => Volatile.Read(ref _stopped) != 0;

    /// <summary>Gets a value indicating whether an exit signal was raised.</summary>
    internal bool IsExitSignalled => Volatile.Read(ref _exitSignalled) != 0;

    /// <summary>
    /// Gets the event the ring sets when a snapshot becomes <see cref="SnapshotSlotState.Ready"/>,
    /// a slot is superseded or released, or the ring stops.
    /// </summary>
    /// <remarks>
    /// Exposed so the renderer can combine the ring wake with the control-channel wake in one
    /// event-based wait (M7 WP-3). The ring owns the event; callers may reset it and wait on its
    /// handle but never dispose it.
    /// </remarks>
    internal ManualResetEventSlim ConsumerWake => _consumerWake;

    /// <summary>Gets a value indicating whether a <see cref="SnapshotSlotState.Ready"/> snapshot exists.</summary>
    internal bool HasReadySnapshot
    {
        get
        {
            var index = Volatile.Read(ref _latest);
            return index != NoLatest && _slots[index].State == SnapshotSlotState.Ready;
        }
    }

    /// <summary>Gets a snapshot of the handoff counters.</summary>
    internal SnapshotRingCounters Counters => new(
        Interlocked.Read(ref _published),
        Interlocked.Read(ref _consumed),
        Interlocked.Read(ref _superseded),
        Interlocked.Read(ref _backpressureWaits),
        Volatile.Read(ref _maxLive),
        Interlocked.Read(ref _stopWakes),
        Interlocked.Read(ref _exitWakes));

    /// <summary>
    /// Acquires a free slot for the tick thread, blocking while all three slots are occupied.
    /// </summary>
    /// <remarks>
    /// The wait is event-based backpressure, not an artificial delay: the call returns a slot as
    /// soon as one is free (or is freed by a fence release or a supersede) and blocks only when
    /// <c>Ready + Rendering == 3</c>. It returns <see langword="null"/> without acquiring when a
    /// stop or exit signal is observed, and counts that reason.
    /// </remarks>
    /// <returns>The slot in <see cref="SnapshotSlotState.Writing"/>, or <see langword="null"/> on stop/exit.</returns>
    internal SnapshotSlot? Acquire()
    {
        var waited = false;
        while (true)
        {
            if (IsStopped)
            {
                Interlocked.Increment(ref _stopWakes);
                return null;
            }

            if (IsExitSignalled)
            {
                Interlocked.Increment(ref _exitWakes);
                return null;
            }

            if (TryAcquireFree(out var slot))
            {
                return slot;
            }

            if (!waited)
            {
                waited = true;
                Interlocked.Increment(ref _backpressureWaits);
            }

            // Reset before re-checking so a release racing with the wait cannot be lost.
            _producerWake.Reset();

            if (IsStopped)
            {
                Interlocked.Increment(ref _stopWakes);
                return null;
            }

            if (IsExitSignalled)
            {
                Interlocked.Increment(ref _exitWakes);
                return null;
            }

            if (TryAcquireFree(out slot))
            {
                return slot;
            }

            try
            {
                _producerWake.Wait();
            }
            catch (ObjectDisposedException)
            {
                Interlocked.Increment(ref _stopWakes);
                return null;
            }
        }
    }

    /// <summary>
    /// Publishes a <see cref="SnapshotSlotState.Writing"/> slot as the newest
    /// <see cref="SnapshotSlotState.Ready"/> snapshot.
    /// </summary>
    /// <remarks>
    /// Publication is a release: the slot's snapshot contents are visible to a consumer that takes
    /// the slot afterwards. The previous <c>latest</c> slot is freed only when it is still
    /// <see cref="SnapshotSlotState.Ready"/>; a slot the renderer already took
    /// (<see cref="SnapshotSlotState.Rendering"/>) is never freed by the producer. Publishing stays
    /// legal after <see cref="Stop"/> and <see cref="SignalExit"/> so an Exit tick's snapshot is not
    /// lost (Q4=D1); the shutdown drain frees whatever remains.
    /// </remarks>
    /// <param name="slot">The slot the caller acquired and filled.</param>
    /// <exception cref="InvalidOperationException">The slot is not in <see cref="SnapshotSlotState.Writing"/>.</exception>
    internal void Publish(SnapshotSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        if (!slot.TryTransition(SnapshotSlotState.Writing, SnapshotSlotState.Ready))
        {
            throw new InvalidOperationException(
                $"Only a Writing slot can be published; slot {slot.Index} is {slot.State}.");
        }

        Interlocked.Increment(ref _published);

        // The new slot is Ready before the previous Ready is freed, so this is the transient
        // high-water moment (for example 1 Rendering + the old Ready + the new Ready). Sample
        // Ready + Rendering here, before the supersede below can hide the peak.
        UpdateMaxLive();

        var previous = Interlocked.Exchange(ref _latest, slot.Index);
        if (previous != NoLatest && previous != slot.Index)
        {
            var superseded = _slots[previous];
            if (superseded.TryTransition(SnapshotSlotState.Ready, SnapshotSlotState.Free))
            {
                Interlocked.Increment(ref _superseded);

                // A superseded Ready frees a slot without a fence; a waiting producer may proceed.
                _producerWake.Set();
            }
        }

        _consumerWake.Set();
    }

    /// <summary>
    /// Returns an unpublished <see cref="SnapshotSlotState.Writing"/> slot to
    /// <see cref="SnapshotSlotState.Free"/> after a tick or capture failure.
    /// </summary>
    /// <param name="slot">The slot the caller acquired.</param>
    /// <exception cref="InvalidOperationException">The slot is not in <see cref="SnapshotSlotState.Writing"/>.</exception>
    internal void Abandon(SnapshotSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        if (!slot.TryTransition(SnapshotSlotState.Writing, SnapshotSlotState.Free))
        {
            throw new InvalidOperationException(
                $"Only a Writing slot can be abandoned; slot {slot.Index} is {slot.State}.");
        }

        _producerWake.Set();
    }

    /// <summary>
    /// Takes the newest <see cref="SnapshotSlotState.Ready"/> snapshot into
    /// <see cref="SnapshotSlotState.Rendering"/> for the render thread.
    /// </summary>
    /// <remarks>
    /// The take is an acquire. <c>latest</c> is cleared with a compare-exchange only when it still
    /// points at the taken slot, so a newer snapshot published concurrently stays latest.
    /// </remarks>
    /// <returns>The taken slot, or <see langword="null"/> when no new snapshot is ready.</returns>
    internal SnapshotSlot? TakeLatest()
    {
        var index = Volatile.Read(ref _latest);
        if (index == NoLatest)
        {
            return null;
        }

        var slot = _slots[index];
        if (!slot.TryTransition(SnapshotSlotState.Ready, SnapshotSlotState.Rendering))
        {
            return null;
        }

        Interlocked.CompareExchange(ref _latest, NoLatest, index);
        Interlocked.Increment(ref _consumed);
        return slot;
    }

    /// <summary>
    /// Frees the newest <see cref="SnapshotSlotState.Ready"/> snapshot without taking it into
    /// <see cref="SnapshotSlotState.Rendering"/>.
    /// </summary>
    /// <remarks>
    /// The suspended consumer uses this while the window is minimized or the extent is zero (Q8=H1):
    /// the dropped snapshot never becomes a GPU frame, no fence participates, and the slot returns
    /// to <see cref="SnapshotSlotState.Free"/> immediately. It is not counted as consumed, which
    /// counts only slots taken for a submitted frame; the renderer counts suspended consumptions.
    /// </remarks>
    /// <returns><see langword="true"/> when a Ready snapshot was freed.</returns>
    internal bool DiscardLatest()
    {
        var index = Volatile.Read(ref _latest);
        if (index == NoLatest)
        {
            return false;
        }

        var slot = _slots[index];
        if (!slot.TryTransition(SnapshotSlotState.Ready, SnapshotSlotState.Free))
        {
            return false;
        }

        Interlocked.CompareExchange(ref _latest, NoLatest, index);
        _producerWake.Set();
        return true;
    }

    /// <summary>
    /// Frees a <see cref="SnapshotSlotState.Rendering"/> slot once its GPU frame fence has signaled.
    /// </summary>
    /// <remarks>This is the only producer wake for a rendering slot; it never frees a slot the producer owns.</remarks>
    /// <param name="slot">The slot the consuming frame referenced.</param>
    /// <exception cref="InvalidOperationException">The slot is not in <see cref="SnapshotSlotState.Rendering"/>.</exception>
    internal void Release(SnapshotSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        if (!slot.TryTransition(SnapshotSlotState.Rendering, SnapshotSlotState.Free))
        {
            throw new InvalidOperationException(
                $"Only a Rendering slot can be released; slot {slot.Index} is {slot.State}.");
        }

        _producerWake.Set();
    }

    /// <summary>
    /// Blocks the render thread until a snapshot is ready or the ring stops.
    /// </summary>
    /// <remarks>
    /// The wait is event-based; the predicate is re-checked after the event is reset so a publish
    /// racing with the wait cannot be lost. The call does not consume the snapshot.
    /// </remarks>
    /// <returns><see langword="true"/> when a Ready snapshot exists; <see langword="false"/> on stop.</returns>
    internal bool WaitForSnapshotOrStop()
    {
        while (true)
        {
            if (IsStopped)
            {
                return false;
            }

            if (HasReadySnapshot)
            {
                return true;
            }

            // Reset before re-checking so a publish racing with the wait cannot be lost.
            _consumerWake.Reset();

            if (IsStopped)
            {
                return false;
            }

            if (HasReadySnapshot)
            {
                return true;
            }

            try
            {
                _consumerWake.Wait();
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Stops the ring: wakes a blocked producer and a blocked consumer and makes future
    /// <see cref="Acquire"/> calls return <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// A stop never force-frees a slot: a producer that still owns a <see cref="SnapshotSlotState.Writing"/>
    /// slot abandons or publishes it, and the shutdown drain frees the published/rendering slots
    /// after the render thread has joined. A renderer fatal maps to this method (Q9=I1).
    /// </remarks>
    internal void Stop()
    {
        Interlocked.Exchange(ref _stopped, 1);
        Signal(_producerWake);
        Signal(_consumerWake);
    }

    /// <summary>
    /// Raises the exit signal: wakes a blocked <see cref="Acquire"/> and makes future acquisitions
    /// return <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// An exit request is producer-side only (Q4=D1/Q9=I1): consumption continues, and a producer
    /// that already holds a <see cref="SnapshotSlotState.Writing"/> slot may still publish the Exit
    /// tick's snapshot. It does not stop the ring, so the renderer keeps consuming until the host
    /// stops it.
    /// </remarks>
    internal void SignalExit()
    {
        Interlocked.Exchange(ref _exitSignalled, 1);
        Signal(_producerWake);
    }

    /// <summary>
    /// Frees every remaining non-<see cref="SnapshotSlotState.Writing"/> slot and clears
    /// <c>latest</c>.
    /// </summary>
    /// <remarks>
    /// <b>Quiescence required:</b> the render thread must have joined and the producer must not
    /// own a slot when this runs (the WP-3 shutdown path). A slot left in
    /// <see cref="SnapshotSlotState.Writing"/> is not touched; the method is for the final cleanup
    /// after every consumer frame has been retired.
    /// </remarks>
    internal void DrainAfterRenderThreadJoined()
    {
        for (var index = 0; index < _slots.Length; index++)
        {
            var slot = _slots[index];
            switch (slot.State)
            {
                case SnapshotSlotState.Ready:
                    slot.TryTransition(SnapshotSlotState.Ready, SnapshotSlotState.Free);
                    break;
                case SnapshotSlotState.Rendering:
                    slot.TryTransition(SnapshotSlotState.Rendering, SnapshotSlotState.Free);
                    break;
                default:
                    break;
            }
        }

        Interlocked.Exchange(ref _latest, NoLatest);
        _producerWake.Set();
    }

    /// <summary>Stops the ring and releases its wait events. Idempotent.</summary>
    /// <remarks>
    /// Dispose is only for the host after every producer/consumer thread has joined; no wait may
    /// be in flight. It never frees a slot by itself.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Stop();

        try
        {
            _producerWake.Dispose();
            _consumerWake.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // The events were already released by a racing stop path.
        }
    }

    private bool TryAcquireFree(out SnapshotSlot? slot)
    {
        for (var index = 0; index < _slots.Length; index++)
        {
            var candidate = _slots[index];
            if (candidate.TryTransition(SnapshotSlotState.Free, SnapshotSlotState.Writing))
            {
                slot = candidate;
                return true;
            }
        }

        slot = null;
        return false;
    }

    private void UpdateMaxLive()
    {
        var live = 0;
        for (var index = 0; index < _slots.Length; index++)
        {
            var state = _slots[index].State;
            if (state is SnapshotSlotState.Ready or SnapshotSlotState.Rendering)
            {
                live++;
            }
        }

        var observed = Volatile.Read(ref _maxLive);
        while (live > observed)
        {
            var previous = Interlocked.CompareExchange(ref _maxLive, live, observed);
            if (previous == observed)
            {
                return;
            }

            observed = previous;
        }
    }

    private static void Signal(ManualResetEventSlim wake)
    {
        try
        {
            wake.Set();
        }
        catch (ObjectDisposedException)
        {
            // The ring was disposed by the joined host; there is no waiter left to wake.
        }
    }
}
