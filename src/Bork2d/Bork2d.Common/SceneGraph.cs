namespace Bork2d.Common;

/// <summary>
/// State shared by every node of one scene graph: the structural lock and the entity ID
/// allocator.
/// </summary>
/// <remarks>
/// The structural lock serializes attach/detach, tree traversal, and ID discovery. It is the
/// outermost lock: no code path acquires it while holding a per-object lock, so the lock order
/// is always structural lock first, per-object lock second.
/// </remarks>
internal sealed class SceneGraph
{
    private uint _nextEntityId;

    /// <summary>
    /// Gets the structural lock shared by all transforms in the graph.
    /// </summary>
    internal Lock StructuralLock { get; } = new();

    /// <summary>
    /// Allocates the next entity ID.
    /// </summary>
    /// <returns>
    /// A monotonic, never-reused ID starting at 1. ID 0 is reserved for the scene root.
    /// </returns>
    internal uint NextEntityId() => Interlocked.Increment(ref _nextEntityId);
}
