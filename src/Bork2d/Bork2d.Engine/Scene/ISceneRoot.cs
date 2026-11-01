namespace Bork2d.Engine.Scene;

/// <summary>
/// The read-only root of the strong scene graph.
/// </summary>
/// <remarks>
/// <para>
/// The root is an identity transform at the origin: <see cref="ITransform.Parent"/> is always
/// <see langword="null"/>, local position is zero, local rotation is zero, and local scale is
/// one. Assigning the parent or any local transform value throws
/// <see cref="InvalidOperationException"/> at runtime.
/// </para>
/// <para>
/// <see cref="ISceneRoot"/> adds no members. <see cref="ITransform.DetachAll"/> is inherited and
/// detaches the root's direct children; their subtrees remain intact and can be re-attached. The
/// root is an identity transform, so root detachment leaves each direct child's local values
/// unchanged while still preserving its world transform.
/// </para>
/// <para>
/// The root is the owner node of the scene tree, not a scene entity: it implements
/// <see cref="ISceneRoot"/> directly and is not an instance of any concrete transform type. Code
/// must not cast it or assume entity behavior; use <see cref="ITransform"/> members only.
/// </para>
/// <para>
/// The root is owned by the host application and reached through <c>IEngine.Root</c>. Parented
/// objects are retained strongly by their parent;
/// unparented objects are not retained by the engine. There is no <see cref="IDisposable"/> or
/// liveness flag on scene entities: detach to remove, re-attach the same object later.
/// </para>
/// </remarks>
public interface ISceneRoot : ITransform
{
}
