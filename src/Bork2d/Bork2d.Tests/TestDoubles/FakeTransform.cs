using System.Numerics;
using Bork2d.Api.Scene;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A caller-defined <see cref="ITransform"/> that is not engine-owned. The scene graph must
/// reject it as a parent.
/// </summary>
internal sealed class FakeTransform : ITransform
{
    public ITransform? Parent { get; set; }

    public Vector2 LocalPosition { get; set; }

    public float LocalRotation { get; set; }

    public Vector2 LocalScale { get; set; } = Vector2.One;

    public void DetachAll()
    {
    }
}
