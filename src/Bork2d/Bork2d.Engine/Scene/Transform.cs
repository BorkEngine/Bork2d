using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Scene;

namespace Bork2d.Engine.Scene;

/// <summary>
/// An engine-owned scene node: a local transform plus a strong parent/child relationship.
/// </summary>
/// <remarks>
/// <para>
/// Structural changes (attach, detach) are serialized by the engine-owned global structural
/// <see cref="Lock"/>; the local matrix is serialized by the per-object <see cref="Sync"/> lock.
/// The lock order is structural lock first, per-object lock second; no code path acquires the
/// structural lock while holding a per-object lock.
/// </para>
/// <para>
/// The local affine matrix is the world source of truth: <see cref="WorldMatrix"/> composes it
/// through the parent chain, and re-parenting assigns the exact local matrix derived from the old
/// world matrix and the inverse of the new parent's world matrix. A non-uniform scale combined
/// with rotation therefore survives a re-parent, even though it introduces shear that a T/R/S
/// triple cannot represent. Detaching adopts the old world matrix directly as the new local
/// matrix, so it preserves the world transform without inverting the old parent and works even
/// when the old parent's world matrix is singular.
/// </para>
/// <para>
/// <see cref="LocalPosition"/>, <see cref="LocalRotation"/>, and <see cref="LocalScale"/> are the
/// authored scalar view cached alongside the matrix. The rotation is authored and returned in
/// degrees, normalized to <c>[0, 360)</c> on assignment; the internal matrix composes radians
/// converted at the boundary. The position setter changes only the matrix translation and keeps
/// the rest of the matrix; the rotation and scale setters recompose from the authored scalars,
/// so writing either discards shear introduced by a re-parent. A re-parent or detach refreshes the
/// scalar view from the exact local matrix, normalizing the decomposed rotation to degrees in
/// <c>[0, 360)</c>, and keeps the authored mirror axis when possible.
/// </para>
/// <para>
/// A parent holds its children strongly and there is no destruction state: detaching keeps the
/// object, its world transform, and its subtree valid, and the same instance can be attached
/// again later. The parent's child list caches each child's index so removing one child is
/// <c>O(1)</c> (see <see cref="SceneChildList"/>).
/// </para>
/// </remarks>
internal class Transform : ITransform, ISceneNode
{
    private const float DegreesPerRadian = 180f / MathF.PI;
    private const float RadiansPerDegree = MathF.PI / 180f;

    private readonly Lock _structuralLock;
    private readonly Lock _sync = new();
    private ISceneNode? _parent;
    private SceneChildList? _children;
    private Matrix3x2 _localMatrix = Matrix3x2.Identity;
    private Vector2 _localPosition = Vector2.Zero;
    private float _localRotation;
    private Vector2 _localScale = Vector2.One;

    internal Transform(Lock structuralLock)
    {
        _structuralLock = structuralLock;
    }

    /// <summary>
    /// Gets the engine-owned global structural lock shared by every node in the scene.
    /// </summary>
    internal Lock StructuralLock => _structuralLock;

    /// <summary>
    /// Gets the per-object property lock.
    /// </summary>
    internal Lock Sync => _sync;

    /// <summary>
    /// Gets or sets this node's cached position in its parent's child list.
    /// </summary>
    /// <remarks>
    /// <see cref="SceneChildList"/> maintains the value under the structural lock; <c>-1</c> means
    /// the node has no parent. Swap-back removal writes the moved child's new slot here so the
    /// removal path never has to scan the list.
    /// </remarks>
    internal int ChildIndex { get; set; } = -1;

    public ITransform? Parent
    {
        get => Volatile.Read(ref _parent) as ITransform;
        set => SetParent(value);
    }

    public Vector2 LocalPosition
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

    public float LocalRotation
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
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The local rotation must be a finite number of degrees.");
            }

            var normalized = NormalizeDegrees(value);
            lock (_sync)
            {
                _localRotation = normalized;
                _localMatrix = Compose(_localScale, normalized * RadiansPerDegree, _localPosition);
            }
        }
    }

    public Vector2 LocalScale
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
                _localMatrix = Compose(value, _localRotation * RadiansPerDegree, _localPosition);
            }
        }
    }

    ISceneNode? ISceneNode.ParentNode => Volatile.Read(ref _parent);

    /// <summary>
    /// Gets the local-to-world matrix, composed through the parent chain.
    /// </summary>
    internal Matrix3x2 WorldMatrix()
    {
        using var scope = _structuralLock.EnterScope();
        return WorldMatrixCore();
    }

    /// <summary>
    /// Gets the evaluated world position in world units.
    /// </summary>
    internal Vector2 WorldPosition()
    {
        using var scope = _structuralLock.EnterScope();
        return WorldMatrixCore().Translation;
    }

    /// <summary>
    /// Gets the world rotation in degrees, normalized to <c>[0, 360)</c> and decomposed from the
    /// composed world matrix.
    /// </summary>
    internal float WorldRotation()
    {
        using var scope = _structuralLock.EnterScope();
        Decompose(WorldMatrixCore(), out _, out var rotation);
        return NormalizeDegrees(rotation * DegreesPerRadian);
    }

    /// <summary>
    /// Gets the world scale, decomposed from the composed world matrix.
    /// </summary>
    internal Vector2 WorldScale()
    {
        using var scope = _structuralLock.EnterScope();
        Decompose(WorldMatrixCore(), out var scale, out _);
        return scale;
    }

    public void DetachAll()
    {
        using var scope = _structuralLock.EnterScope();
        _children?.DetachAll();
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
    internal void AttachAsNewChild(ISceneNode parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        if (!SharesStructuralLock(parent))
        {
            throw new Bork2dException(
                "Cannot attach to a node that belongs to a different engine scene.");
        }

        using var scope = _structuralLock.EnterScope();
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

    /// <summary>
    /// Clears this node's parent link and adopts <paramref name="world"/> as its local matrix.
    /// </summary>
    /// <remarks>
    /// The caller must hold the structural lock and must already have removed the node from its
    /// parent's child list, or be clearing that list in one pass. The world matrix is passed in
    /// before the parent link is cleared, so detaching never inverts the old parent and works when
    /// that parent's world matrix is singular.
    /// </remarks>
    /// <param name="world">The node's world matrix immediately before detachment.</param>
    internal void DetachToWorld(in Matrix3x2 world)
    {
        Volatile.Write(ref _parent, null);
        ChildIndex = -1;

        lock (_sync)
        {
            _localMatrix = world;
            RefreshAuthoredScalars(world);
        }
    }

    Matrix3x2 ISceneNode.LocalMatrixCore() => LocalMatrixCore();

    Matrix3x2 ISceneNode.WorldMatrixCore() => WorldMatrixCore();

    void ISceneNode.AddChildCore(Transform child) => AddChildCore(child);

    void ISceneNode.RemoveChildCore(Transform child) => RemoveChildCore(child);

    void ISceneNode.VisitChildrenCore(ISceneNodeVisitor visitor) => VisitChildrenCore(visitor);

    private void SetParent(ITransform? parent)
    {
        ISceneNode? newParent;
        if (parent is null)
        {
            newParent = null;
        }
        else if (parent is ISceneNode node)
        {
            newParent = node;
        }
        else
        {
            throw new Bork2dException(
                $"A parent must be an engine-owned scene node; got '{parent.GetType().FullName}'.");
        }

        using var scope = _structuralLock.EnterScope();

        if (ReferenceEquals(_parent, newParent))
        {
            return;
        }

        if (newParent is not null)
        {
            if (!SharesStructuralLock(newParent))
            {
                throw new Bork2dException(
                    "A transform cannot be parented to a node from a different engine scene.");
            }

            if (ReferenceEquals(newParent, this))
            {
                throw new InvalidOperationException("A transform cannot be its own parent.");
            }

            for (var ancestor = newParent; ancestor is not null; ancestor = ancestor.ParentNode)
            {
                if (ReferenceEquals(ancestor, this))
                {
                    throw new InvalidOperationException(
                        "The parent assignment would create a cycle in the scene tree.");
                }
            }
        }

        if (newParent is null)
        {
            DetachFromCurrentParentPreservingWorld();
            return;
        }

        var world = WorldMatrixCore();
        var parentWorld = newParent.WorldMatrixCore();
        if (!Matrix3x2.Invert(parentWorld, out var parentInverse))
        {
            throw new Bork2dException(
                "Cannot preserve the world transform against a parent with a singular world transform.");
        }

        var local = world * parentInverse;

        DetachFromCurrentParentCore();
        Volatile.Write(ref _parent, newParent);
        newParent.AddChildCore(this);
        lock (_sync)
        {
            _localMatrix = local;
            RefreshAuthoredScalars(local);
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

    private void DetachFromCurrentParentPreservingWorld()
    {
        var parent = _parent;
        if (parent is null)
        {
            return;
        }

        var world = WorldMatrixCore();
        parent.RemoveChildCore(this);
        DetachToWorld(world);
    }

    private void AddChildCore(Transform child)
    {
        (_children ??= new SceneChildList()).Add(child);
    }

    private void VisitChildrenCore(ISceneNodeVisitor visitor)
    {
        _children?.Visit(visitor);
    }

    private void RemoveChildCore(Transform child)
    {
        _children?.Remove(child);
    }

    private static Lock StructuralLockOf(ISceneNode node) => node switch
    {
        Transform transform => transform._structuralLock,
        SceneRoot root => root.StructuralLock,
        _ => throw new Bork2dException(
            $"A parent must be an engine-owned scene node; got '{node.GetType().FullName}'."),
    };

    private bool SharesStructuralLock(ISceneNode node)
        => StructuralLockOf(node) == _structuralLock;

    /// <summary>
    /// Gets the local-to-world matrix without acquiring the structural lock.
    /// </summary>
    /// <remarks>
    /// The caller must hold the structural lock. Used by the shared child list while it detaches
    /// direct children in one pass.
    /// </remarks>
    internal Matrix3x2 WorldMatrixCore()
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
    /// The caller must hold <see cref="_sync"/>. The decomposed rotation is converted to degrees
    /// and normalized to <c>[0, 360)</c>. The determinant convention puts a reflection on the Y
    /// scale; if the object was authored with a negative X scale, the reflection is moved back to
    /// X (with the matching half-turn) so the authored mirror axis survives a re-parent or detach.
    /// </remarks>
    private void RefreshAuthoredScalars(in Matrix3x2 local)
    {
        Decompose(local, out var scale, out var rotation);
        var rotationDegrees = NormalizeDegrees(rotation * DegreesPerRadian);

        if (scale.Y < 0f && _localScale.X < 0f)
        {
            scale = new Vector2(-scale.X, -scale.Y);
            rotationDegrees = NormalizeDegrees(rotationDegrees - 180f);
        }

        _localPosition = local.Translation;
        _localRotation = rotationDegrees;
        _localScale = scale;
    }

    private static float NormalizeDegrees(float degrees)
    {
        var normalized = degrees % 360f;
        if (normalized < 0f)
        {
            normalized += 360f;
        }

        // Collapse both signed zeros so getters return canonical 0 for full turns.
        return normalized == 0f ? 0f : normalized;
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

    private static Matrix3x2 Compose(Vector2 scale, float rotationRadians, Vector2 translation)
    {
        return Matrix3x2.CreateScale(scale)
            * Matrix3x2.CreateRotation(rotationRadians)
            * Matrix3x2.CreateTranslation(translation);
    }
}
