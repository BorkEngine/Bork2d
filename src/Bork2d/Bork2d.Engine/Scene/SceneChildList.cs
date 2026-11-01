namespace Bork2d.Engine.Scene;

/// <summary>
/// The shared child collection of one scene node.
/// </summary>
/// <remarks>
/// <para>
/// The list caches each child's position in <see cref="Transform.ChildIndex"/> so removal is
/// <c>O(1)</c>: the removed slot is filled with the last child and that child's cached index is
/// updated. <see cref="Transform"/> and <see cref="SceneRoot"/> share this one add/remove/detach
/// implementation instead of duplicating the swap-back logic.
/// </para>
/// <para>
/// Every member requires the engine-owned structural lock. The list itself is never exposed; the
/// owning node reads it through <see cref="Count"/> and <see cref="Get"/> while snapshot capture
/// walks the tree with its own stack.
/// </para>
/// </remarks>
internal sealed class SceneChildList
{
    private readonly List<Transform> _children = [];

    /// <summary>
    /// Gets the number of direct children.
    /// </summary>
    internal int Count => _children.Count;

    /// <summary>
    /// Gets the child at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The slot; the caller guarantees it is below <see cref="Count"/>.</param>
    internal Transform Get(int index) => _children[index];

    /// <summary>
    /// Appends <paramref name="child"/> and caches its index.
    /// </summary>
    /// <param name="child">The child to append; it must already point at this owner.</param>
    internal void Add(Transform child)
    {
        child.ChildIndex = _children.Count;
        _children.Add(child);
    }

    /// <summary>
    /// Removes <paramref name="child"/> with swap-back removal and fixes the moved child's index.
    /// </summary>
    /// <remarks>
    /// The normal path trusts <see cref="Transform.ChildIndex"/>, so it never scans the list. The
    /// <see cref="List{T}.IndexOf"/> fallback runs only when the cached index does not point back
    /// at <paramref name="child"/>, which means that invariant was already broken; it keeps a bug
    /// elsewhere from corrupting this list.
    /// </remarks>
    /// <param name="child">The child to remove; a foreign or already removed child is ignored.</param>
    internal void Remove(Transform child)
    {
        var index = child.ChildIndex;
        if ((uint)index >= (uint)_children.Count || !ReferenceEquals(_children[index], child))
        {
            index = _children.IndexOf(child);
            if (index < 0)
            {
                return;
            }
        }

        var last = _children.Count - 1;
        if (index != last)
        {
            var moved = _children[last];
            _children[index] = moved;
            moved.ChildIndex = index;
        }

        _children.RemoveAt(last);
        child.ChildIndex = -1;
    }

    /// <summary>
    /// Detaches every direct child while preserving its world transform, then clears the list.
    /// </summary>
    /// <remarks>
    /// Each direct child adopts its current world matrix as its new local matrix, so clearing the
    /// parent link never moves it in world space. Only the direct links are cut, so grandchildren
    /// stay attached to those children.
    /// </remarks>
    internal void DetachAll()
    {
        for (var index = 0; index < _children.Count; index++)
        {
            var child = _children[index];
            child.DetachToWorld(child.WorldMatrixCore());
        }

        _children.Clear();
    }
}
