using System.Numerics;

namespace Bork2d.Engine.Scene;

/// <summary>
/// The internal structural contract shared by every node that can own children in one scene
/// tree: <see cref="Transform"/> and <see cref="SceneRoot"/>.
/// </summary>
/// <remarks>
/// <para>
/// The interface exists so the scene root can participate in the tree without inheriting
/// <see cref="Transform"/>: <see cref="SceneRoot"/> implements <c>ISceneRoot</c> and this
/// contract directly. It is never part of the public API.
/// </para>
/// <para>
/// The <c>*Core</c> members run under the engine-owned structural lock; callers must already
/// hold it. The lock order is structural lock first, per-object lock second. The interface
/// deliberately exposes no child enumeration, no child list, and no entity identity: the tree is
/// unordered, and the only walk is the internal unordered visitor used by snapshot capture
/// (M7 WP-2).
/// </para>
/// </remarks>
internal interface ISceneNode
{
    /// <summary>
    /// Gets the parent node, or <see langword="null"/> when the node is unparented or is the root.
    /// </summary>
    ISceneNode? ParentNode { get; }

    /// <summary>
    /// Gets the local-to-parent matrix.
    /// </summary>
    Matrix3x2 LocalMatrixCore();

    /// <summary>
    /// Gets the local-to-world matrix, composed through the parent chain.
    /// </summary>
    Matrix3x2 WorldMatrixCore();

    /// <summary>
    /// Appends <paramref name="child"/> to this node's child list.
    /// </summary>
    void AddChildCore(Transform child);

    /// <summary>
    /// Removes <paramref name="child"/> from this node's child list with swap-back removal.
    /// </summary>
    void RemoveChildCore(Transform child);

    /// <summary>
    /// Visits this node's direct children in no specified order (M7 snapshot capture).
    /// </summary>
    /// <remarks>
    /// The caller must hold the structural lock, must not mutate the tree from the visitor, and
    /// must not assume any ordering. The visitor recurses by calling this method on the visited
    /// node, so the whole rooted tree is reachable without exposing the child list.
    /// </remarks>
    /// <param name="visitor">The unordered visitor.</param>
    void VisitChildrenCore(ISceneNodeVisitor visitor);
}
