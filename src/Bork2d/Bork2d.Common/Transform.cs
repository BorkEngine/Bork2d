using System.Numerics;
using Bork2d.Api;

namespace Bork2d.Common;

/// <summary>
/// An engine-owned scene node: a local transform plus a strong parent/child relationship.
/// </summary>
/// <remarks>
/// <para>
/// Structural changes (attach, detach, traversal) are serialized by the graph's structural
/// <see cref="SceneGraph.StructuralLock"/>; local transform values are serialized by the
/// per-object <see cref="Sync"/> lock. The lock order is structural lock first, per-object lock
/// second; no code path acquires the structural lock while holding a per-object lock.
/// </para>
/// <para>
/// A parent holds its children strongly. There is no destruction state: detaching keeps the
/// object and its local values valid, and the same instance can be attached again later.
/// </para>
/// <para>
/// <see cref="WorldMatrix"/> composes the full local-to-world affine matrix. The scalar helpers
/// (<see cref="WorldPosition"/>, <see cref="WorldRotation"/>, <see cref="WorldScale"/>) exist for
/// world-preserving re-parenting and tests: position comes from the composed matrix, while
/// rotation and scale accumulate local values (a T/R/S triple cannot represent the shear that a
/// non-uniform scale combined with rotation introduces).
/// </para>
/// </remarks>
internal class Transform : ITransform
{
    private readonly Lock _sync = new();
    private Transform? _parent;
    private List<Transform>? _children;
    private Vector2 _localPosition = Vector2.Zero;
    private float _localRotation;
    private Vector2 _localScale = Vector2.One;

    internal Transform(SceneGraph graph)
        : this(graph, graph.NextEntityId())
    {
    }

    internal Transform(SceneGraph graph, uint id)
    {
        Graph = graph;
        Id = id;
    }

    /// <summary>
    /// Gets the entity ID: monotonic and never reused. The scene root has ID 0.
    /// </summary>
    internal uint Id { get; }

    /// <summary>
    /// Gets the scene graph this node belongs to.
    /// </summary>
    internal SceneGraph Graph { get; }

    /// <summary>
    /// Gets the per-object property lock.
    /// </summary>
    internal Lock Sync => _sync;

    public virtual ITransform? Parent
    {
        get => Volatile.Read(ref _parent);
        set => SetParent(value);
    }

    public virtual Vector2 LocalPosition
    {
        get
        {
            lock (_sync)
            {
                return _localPosition;
            }
        }

        set
        {
            lock (_sync)
            {
                _localPosition = value;
            }
        }
    }

    public virtual float LocalRotation
    {
        get
        {
            lock (_sync)
            {
                return _localRotation;
            }
        }

        set
        {
            lock (_sync)
            {
                _localRotation = value;
            }
        }
    }

    public virtual Vector2 LocalScale
    {
        get
        {
            lock (_sync)
            {
                return _localScale;
            }
        }

        set
        {
            lock (_sync)
            {
                _localScale = value;
            }
        }
    }

    /// <summary>
    /// Gets the local-to-world matrix, composed through the parent chain.
    /// </summary>
    internal Matrix3x2 WorldMatrix()
    {
        using var scope = Graph.StructuralLock.EnterScope();
        return WorldMatrixCore();
    }

    /// <summary>
    /// Gets the evaluated world position in world units.
    /// </summary>
    internal Vector2 WorldPosition()
    {
        using var scope = Graph.StructuralLock.EnterScope();
        return WorldMatrixCore().Translation;
    }

    /// <summary>
    /// Gets the accumulated world rotation in radians.
    /// </summary>
    internal float WorldRotation()
    {
        using var scope = Graph.StructuralLock.EnterScope();
        return WorldRotationCore();
    }

    /// <summary>
    /// Gets the accumulated world scale.
    /// </summary>
    internal Vector2 WorldScale()
    {
        using var scope = Graph.StructuralLock.EnterScope();
        return WorldScaleCore();
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
            var child = children[index];
            Volatile.Write(ref child._parent, null);
        }

        children.Clear();
    }

    /// <summary>
    /// Appends every rooted descendant, excluding this node, to <paramref name="results"/>.
    /// </summary>
    /// <remarks>The caller must hold the graph's structural lock.</remarks>
    internal void CollectDescendants(List<Transform> results)
    {
        var children = _children;
        if (children is null)
        {
            return;
        }

        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            results.Add(child);
            child.CollectDescendants(results);
        }
    }

    /// <summary>
    /// Attaches a newly created node to <paramref name="parent"/> without changing its local
    /// values.
    /// </summary>
    /// <remarks>
    /// Used only by the engine during creation: a brand-new node has no observable world state
    /// to preserve. Later parent changes go through <see cref="Parent"/>, which preserves the
    /// world transform.
    /// </remarks>
    internal void AttachAsNewChild(Transform parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var graph = Graph;
        if (!ReferenceEquals(parent.Graph, graph))
        {
            throw new Bork2dException("Cannot attach to a node that belongs to a different scene graph.");
        }

        using var scope = graph.StructuralLock.EnterScope();
        if (ReferenceEquals(_parent, parent))
        {
            return;
        }

        if (_parent is not null)
        {
            throw new InvalidOperationException(
                "The transform is already attached; assign Parent to move it.");
        }

        Volatile.Write(ref _parent, parent);
        parent.AddChildCore(this);
    }

    private void SetParent(ITransform? parent)
    {
        if (parent is not null && parent is not Transform)
        {
            throw new Bork2dException(
                $"A parent must be an engine-owned transform; got '{parent.GetType().FullName}'.");
        }

        var newParent = (Transform?)parent;
        var graph = Graph;
        using var scope = graph.StructuralLock.EnterScope();

        if (ReferenceEquals(_parent, newParent))
        {
            return;
        }

        if (newParent is not null)
        {
            if (!ReferenceEquals(newParent.Graph, graph))
            {
                throw new Bork2dException(
                    "A transform cannot be parented to a node from a different scene graph.");
            }

            if (ReferenceEquals(newParent, this))
            {
                throw new InvalidOperationException("A transform cannot be its own parent.");
            }

            for (var ancestor = newParent; ancestor is not null; ancestor = ancestor._parent)
            {
                if (ReferenceEquals(ancestor, this))
                {
                    throw new InvalidOperationException(
                        "The parent assignment would create a cycle in the scene graph.");
                }
            }
        }

        var localPosition = Vector2.Zero;
        var localRotation = 0f;
        var localScale = Vector2.One;

        if (newParent is not null)
        {
            var worldPosition = WorldMatrixCore().Translation;
            var worldRotation = WorldRotationCore();
            var worldScale = WorldScaleCore();
            var parentWorld = newParent.WorldMatrixCore();
            if (!Matrix3x2.Invert(parentWorld, out var parentInverse))
            {
                throw new Bork2dException(
                    "Cannot preserve the world transform against a parent with a singular world transform.");
            }

            var parentScale = newParent.WorldScaleCore();
            if (parentScale.X == 0f || parentScale.Y == 0f)
            {
                throw new Bork2dException(
                    "Cannot preserve the world transform against a parent with a zero world scale.");
            }

            localPosition = Vector2.Transform(worldPosition, parentInverse);
            localRotation = worldRotation - newParent.WorldRotationCore();
            localScale = worldScale / parentScale;
        }

        DetachFromCurrentParentCore();
        Volatile.Write(ref _parent, newParent);

        if (newParent is not null)
        {
            newParent.AddChildCore(this);
            lock (_sync)
            {
                _localPosition = localPosition;
                _localRotation = localRotation;
                _localScale = localScale;
            }
        }
    }

    private void DetachFromCurrentParentCore()
    {
        var parent = _parent;
        if (parent is null)
        {
            return;
        }

        parent.RemoveChildCore(this);
        Volatile.Write(ref _parent, null);
    }

    private void AddChildCore(Transform child)
    {
        (_children ??= new List<Transform>()).Add(child);
    }

    private void RemoveChildCore(Transform child)
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

    private Matrix3x2 WorldMatrixCore()
    {
        var local = LocalMatrixCore();
        var parent = _parent;
        return parent is null ? local : local * parent.WorldMatrixCore();
    }

    private Matrix3x2 LocalMatrixCore()
    {
        lock (_sync)
        {
            return Matrix3x2.CreateScale(_localScale)
                * Matrix3x2.CreateRotation(_localRotation)
                * Matrix3x2.CreateTranslation(_localPosition);
        }
    }

    private float WorldRotationCore()
    {
        var rotation = 0f;
        for (var node = this; node is not null; node = node._parent)
        {
            lock (node._sync)
            {
                rotation += node._localRotation;
            }
        }

        return rotation;
    }

    private Vector2 WorldScaleCore()
    {
        var scale = Vector2.One;
        for (var node = this; node is not null; node = node._parent)
        {
            lock (node._sync)
            {
                scale *= node._localScale;
            }
        }

        return scale;
    }
}
