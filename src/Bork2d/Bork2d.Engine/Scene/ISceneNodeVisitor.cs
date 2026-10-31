namespace Bork2d.Engine.Scene;

/// <summary>
/// The internal unordered visitor used to walk one scene tree without exposing child enumeration
/// publicly (M7 snapshot capture).
/// </summary>
/// <remarks>
/// <para>
/// The caller must already hold the engine-owned structural lock and must not mutate the tree
/// while the walk is in progress. The walk visits every node reachable from the starting node's
/// children in no specified order; there is no entity identity, depth, or ordering guarantee.
/// </para>
/// <para>
/// Implementations of <see cref="Visit"/> may recurse with
/// <see cref="ISceneNode.VisitChildrenCore"/>; the capture pass does exactly that to evaluate the
/// parent hierarchy. The interface is never part of the public API.
/// </para>
/// </remarks>
internal interface ISceneNodeVisitor
{
    /// <summary>
    /// Visits one attached node. The caller holds the structural lock.
    /// </summary>
    /// <param name="node">The visited transform; never the scene root itself.</param>
    void Visit(Transform node);
}
