namespace Bork2d.Render.Handoff;

/// <summary>
/// The handoff counters exposed to tests and logged once at stop by the M7 renderer/host
/// (D-M7-20; no public API).
/// </summary>
/// <param name="Published">Completed ticks that published a snapshot.</param>
/// <param name="Consumed">Snapshots taken by the renderer (the slot entered <c>Rendering</c>).</param>
/// <param name="Superseded">Ready slots freed by a newer publish before they were consumed.</param>
/// <param name="BackpressureWaits">Times the producer had to wait for a free slot.</param>
/// <param name="MaxLive">
/// High-water mark of <c>Ready + Rendering</c>, sampled at publish before the superseded Ready
/// slot is freed; must be at most <c>3</c>.
/// </param>
/// <param name="StopWakes">Acquire calls that returned <see langword="null"/> because of a stop.</param>
/// <param name="ExitWakes">Acquire calls that returned <see langword="null"/> because of an exit signal.</param>
internal readonly record struct SnapshotRingCounters(
    long Published,
    long Consumed,
    long Superseded,
    long BackpressureWaits,
    int MaxLive,
    long StopWakes,
    long ExitWakes);
