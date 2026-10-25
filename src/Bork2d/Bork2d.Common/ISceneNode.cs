using System.Numerics;

namespace Bork2d.Common;

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
/// deliberately exposes no child enumeration or traversal helper and no entity identity: the
/// tree is unordered and snapshots are not implemented in M1.
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
}
