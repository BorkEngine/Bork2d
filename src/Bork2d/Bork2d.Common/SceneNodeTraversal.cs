namespace Bork2d.Common;

/// <summary>
/// Tree traversal shared by the nodes of one scene graph.
/// </summary>
internal static class SceneNodeTraversal
{
    /// <summary>
    /// Appends every descendant of <paramref name="node"/>, excluding the node itself, to
    /// <paramref name="results"/>.
    /// </summary>
    /// <remarks>The caller must hold the graph's structural lock.</remarks>
    internal static void CollectDescendants(ISceneNode node, List<Transform> results)
    {
        var start = results.Count;
        node.CollectChildrenCore(results);
        var end = results.Count;
        for (var index = start; index < end; index++)
        {
            CollectDescendants(results[index], results);
        }
    }
}
