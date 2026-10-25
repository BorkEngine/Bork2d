using System.Numerics;
using Bork2d.Api;

namespace Bork2d.Common;

/// <summary>
/// The read-only, identity-transform root of one engine scene graph.
/// </summary>
/// <remarks>
/// The root is the strong owner of the live scene. It has ID 0, no parent, and identity local
/// values; every parent/local setter throws <see cref="InvalidOperationException"/>. It stays
/// valid after <see cref="Transform.DetachAll"/> and can own children again at any time.
/// </remarks>
internal sealed class SceneRoot : Transform, ISceneRoot
{
    internal SceneRoot(SceneGraph graph)
        : base(graph, id: 0)
    {
    }

    public override ITransform? Parent
    {
        get => null;
        set => throw new InvalidOperationException("The scene root cannot be parented.");
    }

    public override Vector2 LocalPosition
    {
        get => Vector2.Zero;
        set => throw new InvalidOperationException(
            "The scene root is an identity transform; its local position cannot be set.");
    }

    public override float LocalRotation
    {
        get => 0f;
        set => throw new InvalidOperationException(
            "The scene root is an identity transform; its local rotation cannot be set.");
    }

    public override Vector2 LocalScale
    {
        get => Vector2.One;
        set => throw new InvalidOperationException(
            "The scene root is an identity transform; its local scale cannot be set.");
    }

    /// <summary>
    /// Snapshots every rooted transform, excluding the root itself, in entity-ID order.
    /// </summary>
    /// <returns>The rooted transforms sorted by entity ID.</returns>
    internal IReadOnlyList<Transform> GetRootedTransformsInIdOrder()
    {
        using var scope = Graph.StructuralLock.EnterScope();
        var transforms = new List<Transform>();
        CollectDescendants(transforms);
        transforms.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        return transforms;
    }
}
