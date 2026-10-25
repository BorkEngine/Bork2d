using System.Numerics;
using Bork2d.Api;

namespace Bork2d.Common;

/// <summary>
/// An engine-owned scene node: a local transform plus a strong parent/child relationship.
/// </summary>
/// <remarks>
/// <para>
/// Structural changes (attach, detach, traversal) are serialized by the graph's structural
/// <see cref="SceneGraph.StructuralLock"/>; the local matrix is serialized by the per-object
/// <see cref="Sync"/> lock. The lock order is structural lock first, per-object lock second; no
/// code path acquires the structural lock while holding a per-object lock.
/// </para>
/// <para>
/// The local affine matrix is the world source of truth: <see cref="WorldMatrix"/> composes it
/// through the parent chain, and re-parenting assigns the exact local matrix derived from the old
/// world matrix and the inverse of the new parent's world matrix. A non-uniform scale combined
/// with rotation therefore survives a re-parent, even though it introduces shear that a T/R/S
/// triple cannot represent.
/// </para>
/// <para>
/// <see cref="LocalPosition"/>, <see cref="LocalRotation"/>, and <see cref="LocalScale"/> are the
/// authored scalar view cached alongside the matrix. Their getters return the authored value
/// (including negative scale axes and angles outside (-π, π]); their setters update the authored
/// value and rebuild the local matrix. The position setter changes only the matrix translation
/// and keeps the rest of the matrix; the rotation and scale setters recompose from the authored
/// scalars, so writing either discards shear introduced by a re-parent. A re-parent refreshes the
/// scalar view from the exact local matrix and keeps the authored mirror axis when possible;
/// that refresh is the only place where an authored angle outside (-π, π] normalizes.
/// </para>
/// <para>
/// A parent holds its children strongly and there is no destruction state: detaching keeps the
/// object and its local matrix valid, and the same instance can be attached again later.
/// </para>
/// </remarks>
internal class Transform : ITransform
{
    private readonly Lock _sync = new();
    private Transform? _parent;
    private List<Transform>? _children;
    private Matrix3x2 _localMatrix = Matrix3x2.Identity;
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
                _localMatrix.M31 = value.X;
                _localMatrix.M32 = value.Y;
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
                _localMatrix = Compose(_localScale, value, _localPosition);
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
                _localMatrix = Compose(value, _localRotation, _localPosition);
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
    /// Gets the world rotation in radians, decomposed from the composed world matrix.
    /// </summary>
    internal float WorldRotation()
    {
        using var scope = Graph.StructuralLock.EnterScope();
        Decompose(WorldMatrixCore(), out _, out var rotation);
        return rotation;
    }

    /// <summary>
    /// Gets the world scale, decomposed from the composed world matrix.
    /// </summary>
    internal Vector2 WorldScale()
    {
        using var scope = Graph.StructuralLock.EnterScope();
        Decompose(WorldMatrixCore(), out var scale, out _);
        return scale;
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
    /// matrix.
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

        var local = Matrix3x2.Identity;
        if (newParent is not null)
        {
            var world = WorldMatrixCore();
            var parentWorld = newParent.WorldMatrixCore();
            if (!Matrix3x2.Invert(parentWorld, out var parentInverse))
            {
                throw new Bork2dException(
                    "Cannot preserve the world transform against a parent with a singular world transform.");
            }

            local = world * parentInverse;
        }

        DetachFromCurrentParentCore();
        Volatile.Write(ref _parent, newParent);

        if (newParent is not null)
        {
            newParent.AddChildCore(this);
            lock (_sync)
            {
                _localMatrix = local;
                RefreshAuthoredScalars(local);
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
            return _localMatrix;
        }
    }

    /// <summary>
    /// Refreshes the authored scalar cache from an exact local matrix.
    /// </summary>
    /// <remarks>
    /// The caller must hold <see cref="_sync"/>. The determinant convention puts a reflection on
    /// the Y scale; if the object was authored with a negative X scale, the reflection is moved
    /// back to X (with the matching half-turn) so the authored mirror axis survives a re-parent.
    /// </remarks>
    private void RefreshAuthoredScalars(in Matrix3x2 local)
    {
        Decompose(local, out var scale, out var rotation);

        if (scale.Y < 0f && _localScale.X < 0f)
        {
            scale = new Vector2(-scale.X, -scale.Y);
            rotation = NormalizeRotation(rotation - MathF.PI);
        }

        _localPosition = local.Translation;
        _localRotation = rotation;
        _localScale = scale;
    }

    private static float NormalizeRotation(float rotation)
    {
        const float twoPi = MathF.PI * 2f;
        var normalized = rotation % twoPi;
        if (normalized <= -MathF.PI)
        {
            normalized += twoPi;
        }
        else if (normalized > MathF.PI)
        {
            normalized -= twoPi;
        }

        return normalized;
    }

    private static void Decompose(in Matrix3x2 matrix, out Vector2 scale, out float rotation)
    {
        var xAxis = new Vector2(matrix.M11, matrix.M12);
        var yAxis = new Vector2(matrix.M21, matrix.M22);
        var scaleX = xAxis.Length();
        var scaleY = yAxis.Length();
        if ((matrix.M11 * matrix.M22) - (matrix.M12 * matrix.M21) < 0f)
        {
            scaleY = -scaleY;
        }

        scale = new Vector2(scaleX, scaleY);
        rotation = MathF.Atan2(matrix.M12, matrix.M11);
    }

    private static Matrix3x2 Compose(Vector2 scale, float rotation, Vector2 translation)
    {
        return Matrix3x2.CreateScale(scale)
            * Matrix3x2.CreateRotation(rotation)
            * Matrix3x2.CreateTranslation(translation);
    }
}
