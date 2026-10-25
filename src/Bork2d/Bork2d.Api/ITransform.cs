using System.Numerics;

namespace Bork2d.Api;

/// <summary>
/// A node in the scene hierarchy: a local transform plus a strong parent/child relationship.
/// </summary>
/// <remarks>
/// <para>
/// Any transform can own children, not only the scene root. Structural attach/detach is
/// serialized by the scene graph; individual property access is serialized per object. No member
/// requires the process main thread.
/// </para>
/// <para>
/// A parent retains its children strongly. Setting <see cref="Parent"/> to another transform
/// preserves the world transform, and setting it to <see langword="null"/> detaches the node
/// without destroying it: the same object can be re-attached later with its state intact.
/// Self-parenting, cycles, and parenting the scene root are rejected at runtime.
/// </para>
/// </remarks>
public interface ITransform
{
    /// <summary>
    /// Gets or sets the parent of this transform.
    /// </summary>
    /// <value>
    /// The parent transform, or <see langword="null"/> when unparented. Assigning
    /// <see langword="null"/> detaches the node from the scene; it stays valid and can be
    /// re-attached.
    /// </value>
    /// <exception cref="InvalidOperationException">
    /// The transform is the scene root, or the assignment would create a cycle.
    /// </exception>
    ITransform? Parent { get; set; }

    /// <summary>
    /// Gets or sets the position relative to <see cref="Parent"/>, in world units.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The transform is the scene root (read-only identity transform).
    /// </exception>
    Vector2 LocalPosition { get; set; }

    /// <summary>
    /// Gets or sets the rotation relative to <see cref="Parent"/>, in degrees, positive
    /// counter-clockwise around +Z.
    /// </summary>
    /// <value>
    /// A degree value normalized to <c>[0, 360)</c>. The setter normalizes finite values
    /// (<c>-90</c> becomes <c>270</c>, <c>360</c> and <c>720</c> become <c>0</c>) and the getter
    /// always returns the normalized value.
    /// </value>
    /// <exception cref="InvalidOperationException">
    /// The transform is the scene root (read-only identity transform).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The assigned value is <see cref="float.NaN"/> or infinite.
    /// </exception>
    float LocalRotation { get; set; }

    /// <summary>
    /// Gets or sets the scale relative to <see cref="Parent"/>. Negative values mirror.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The transform is the scene root (read-only identity transform).
    /// </exception>
    Vector2 LocalScale { get; set; }

    /// <summary>
    /// Detaches all direct children of this transform.
    /// </summary>
    /// <remarks>
    /// Grandchildren stay attached to their parents. Detached children keep their state and can
    /// be re-parented later. Can be called on any transform, not only the scene root.
    /// </remarks>
    void DetachAll();
}
