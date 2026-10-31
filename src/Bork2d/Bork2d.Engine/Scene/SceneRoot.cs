using System.Numerics;
using Bork2d.Engine.Scene;

namespace Bork2d.Engine.Scene;

/// <summary>
/// The read-only, identity-transform root of one engine scene tree.
/// </summary>
/// <remarks>
/// <para>
/// The root is the strong owner of the live scene. It has no parent and identity local values;
/// every parent/local setter throws <see cref="InvalidOperationException"/>. It stays valid
/// after <see cref="DetachAll"/> and can own children again at any time.
/// </para>
/// <para>
/// The root implements <see cref="ISceneRoot"/> and the internal <see cref="ISceneNode"/> tree
/// contract directly; it is deliberately <b>not</b> a <see cref="Transform"/>. Structural
/// operations are shared through <see cref="ISceneNode"/> and serialized by the engine-owned
/// global structural lock, the same lock transforms use.
/// </para>
/// <para>
/// The tree is unordered: the root owns its children strongly but exposes no child enumeration,
/// order, or identity. Snapshot capture reaches them only through the internal unordered
/// <see cref="ISceneNodeVisitor"/> walk (M7 WP-2).
/// </para>
/// </remarks>
internal sealed class SceneRoot : ISceneRoot, ISceneNode
{
    private readonly Lock _structuralLock;
    private List<Transform>? _children;

    internal SceneRoot(Lock structuralLock)
    {
        _structuralLock = structuralLock;
    }

    /// <summary>
    /// Gets the engine-owned global structural lock shared by every node in the scene.
    /// </summary>
    internal Lock StructuralLock => _structuralLock;

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

    ISceneNode? ISceneNode.ParentNode => null;

    public void DetachAll()
    {
        using var scope = _structuralLock.EnterScope();
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

    void ISceneNode.VisitChildrenCore(ISceneNodeVisitor visitor)
    {
        var children = _children;
        if (children is null)
        {
            return;
        }

        for (var index = 0; index < children.Count; index++)
        {
            visitor.Visit(children[index]);
        }
    }
}
