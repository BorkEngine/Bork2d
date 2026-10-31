namespace Bork2d.Render.Handoff;

/// <summary>
/// Why the combined snapshot/control wait returned (M7 WP-3).
/// </summary>
internal enum SnapshotWaitOutcome
{
    /// <summary>A <c>Ready</c> snapshot exists and can be taken.</summary>
    Snapshot,

    /// <summary>A control message is pending; the render loop drains it before deciding a frame.</summary>
    Control,

    /// <summary>A stop was requested or the ring stopped; the render loop exits.</summary>
    Stopped,
}
