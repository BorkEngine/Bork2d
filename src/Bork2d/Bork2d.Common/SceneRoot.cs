using System.Numerics;
using Bork2d.Api;

namespace Bork2d.Common;

/// <summary>
/// The read-only, identity-transform root of one engine scene graph.
/// </summary>
/// <remarks>
/// <para>
/// The root is the strong owner of the live scene. It has ID 0, no parent, and identity local
/// values; every parent/local setter throws <see cref="InvalidOperationException"/>. It stays
/// valid after <see cref="DetachAll"/> and can own children again at any time.
/// </para>
/// <para>
/// The root implements <see cref="ISceneRoot"/> and the internal <see cref="ISceneNode"/> tree
/// contract directly; it is deliberately <b>not</b> a <see cref="Transform"/>. Structural
/// operations are shared through <see cref="ISceneNode"/> and serialized by the same
/// <see cref="SceneGraph.StructuralLock"/> as transforms.
/// </para>
/// </remarks>
internal sealed class SceneRoot : ISceneRoot, ISceneNode
{
    private List<Transform>? _children;

    internal SceneRoot(SceneGraph graph)
    {
        Graph = graph;
    }

    /// <summary>
    /// Gets the entity ID of the scene root. ID 0 is reserved and never allocated to a transform.
    /// </summary>
    internal uint Id => 0;

    /// <summary>
    /// Gets the scene graph this root belongs to.
    /// </summary>
    internal SceneGraph Graph { get; }

    public ITransform? Parent
    {
        get => null;
        set => throw new InvalidOperationException("The scene root cannot be parented.");
    }

    public Vector2 LocalPosition
    {
        get => Vector2.Zero;
        set => throw new InvalidOperationException(
            "The scene root is an identity transform; its local position cannot be set.");
    }

    public float LocalRotation
    {
        get => 0f;
        set => throw new InvalidOperationException(
            "The scene root is an identity transform; its local rotation cannot be set.");
    }

    public Vector2 LocalScale
    {
        get => Vector2.One;
        set => throw new InvalidOperationException(
            "The scene root is an identity transform; its local scale cannot be set.");
    }

    SceneGraph ISceneNode.Graph => Graph;

    uint ISceneNode.Id => Id;

    ISceneNode? ISceneNode.ParentNode => null;

    /// <summary>
    /// Snapshots every rooted transform, excluding the root itself, in entity-ID order.
    /// </summary>
    /// <returns>The rooted transforms sorted by entity ID.</returns>
    internal IReadOnlyList<Transform> GetRootedTransformsInIdOrder()
    {
        using var scope = Graph.StructuralLock.EnterScope();
        var transforms = new List<Transform>();
        SceneNodeTraversal.CollectDescendants(this, transforms);
        transforms.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        return transforms;
    }

    public void DetachAll()
    {
        using var scope = Graph.StructuralLock.EnterScope();
        var children = _children;
        if (children is null || children.Count == 0)
        {
            return;
        }

        for (var index = 0; index < children.Count; index++)
        {
            children[index].ClearParentPointer();
        }

        children.Clear();
    }

    Matrix3x2 ISceneNode.LocalMatrixCore() => Matrix3x2.Identity;

    Matrix3x2 ISceneNode.WorldMatrixCore() => Matrix3x2.Identity;

    void ISceneNode.AddChildCore(Transform child) => (_children ??= new List<Transform>()).Add(child);

    void ISceneNode.RemoveChildCore(Transform child)
    {
        var children = _children;
        if (children is null)
        {
            return;
        }

        var index = children.IndexOf(child);
        if (index < 0)
        {
            return;
        }

        var last = children.Count - 1;
        if (index != last)
        {
            children[index] = children[last];
        }

        children.RemoveAt(last);
    }

    void ISceneNode.CollectChildrenCore(List<Transform> results)
    {
        var children = _children;
        if (children is null || children.Count == 0)
        {
            return;
        }

        results.AddRange(children);
    }
}
