using Bork2d.Engine.Scene;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Scene;

/// <summary>
/// Rotation unit and normalization contract: public rotation is degrees in <c>[0, 360)</c>.
/// </summary>
public sealed class TransformRotationTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(-90f, 270f)]
    [InlineData(360f, 0f)]
    [InlineData(720f, 0f)]
    [InlineData(45.5f, 45.5f)]
    [InlineData(-1f, 359f)]
    [InlineData(450f, 90f)]
    public void LocalRotationIsNormalizedDegrees(float assigned, float expected)
    {
        var engine = TestEngineFactory.Create();
        var transform = engine.CreateTransform(engine.Root);

        transform.LocalRotation = assigned;

        Assert.Equal(expected, transform.LocalRotation);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void NonFiniteLocalRotationIsRejected(float assigned)
    {
        var engine = TestEngineFactory.Create();
        var transform = engine.CreateTransform(engine.Root);
        transform.LocalRotation = 45f;

        Assert.Throws<ArgumentOutOfRangeException>(() => { transform.LocalRotation = assigned; });
        Assert.Equal(45f, transform.LocalRotation);
    }

    [Fact]
    public void WorldRotationIsReportedAsNormalizedDegrees()
    {
        var engine = TestEngineFactory.Create();
        var transform = engine.CreateTransform(engine.Root);

        transform.LocalRotation = 350f;
        TestAssertions.Close(350f, ((Transform)transform).WorldRotation());

        transform.LocalRotation = -10f;
        TestAssertions.Close(350f, ((Transform)transform).WorldRotation());
    }

    [Fact]
    public void DegreeRotationsComposeThroughTheParentChain()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalRotation = 90f;

        var child = engine.CreateTransform(parent);
        child.LocalRotation = 45f;

        TestAssertions.Close(135f, ((Transform)child).WorldRotation());
    }

    [Fact]
    public void ReparentRefreshNormalizesTheLocalRotation()
    {
        var engine = TestEngineFactory.Create();
        var origin = engine.CreateTransform(engine.Root);
        var child = engine.CreateTransform(origin);

        var rotated = engine.CreateTransform(engine.Root);
        rotated.LocalRotation = 30f;

        child.Parent = rotated;

        Assert.Same(rotated, child.Parent);
        TestAssertions.Close(330f, child.LocalRotation);
        TestAssertions.Close(0f, ((Transform)child).WorldRotation());
    }
}
