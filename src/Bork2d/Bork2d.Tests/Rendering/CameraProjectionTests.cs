using System.Numerics;
using Bork2d.Engine.Rendering;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Deterministic tests for the shared camera projection/rounding helper (M8 §6.3/§8.1).
/// </summary>
public sealed class CameraProjectionTests
{
    private static readonly Vector2 _hd = new(1280f, 720f);
    private static readonly Vector4 _fullViewport = new(0f, 0f, 1f, 1f);

    [Fact]
    public void EmptyRoundedRectIsInvalid()
    {
        CameraProjectionResult projection = Invalid(
            new Vector2(1000f, 1000f),
            Matrix3x2.Identity,
            10f,
            new Vector4(0f, 0f, 0.0004f, 1f),
            ClearKind.Color,
            Color.Black);

        Assert.Equal(CameraInvalidReason.EmptyRect, projection.Reason);
    }

    [Theory]
    [InlineData(0f, 720f)]
    [InlineData(1280f, 0f)]
    [InlineData(-1f, 720f)]
    [InlineData(float.NaN, 720f)]
    [InlineData(1280f, float.PositiveInfinity)]
    public void InvalidFramebufferIsRejected(float width, float height)
    {
        CameraProjectionResult projection = Invalid(
            new Vector2(width, height),
            Matrix3x2.Identity,
            10f,
            _fullViewport,
            ClearKind.Color,
            Color.Black);

        Assert.Equal(CameraInvalidReason.Framebuffer, projection.Reason);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidSizeIsRejected(float size)
    {
        CameraProjectionResult projection = Invalid(
            _hd,
            Matrix3x2.Identity,
            size,
            _fullViewport,
            ClearKind.Color,
            Color.Black);

        Assert.Equal(CameraInvalidReason.Size, projection.Reason);
    }

    [Fact]
    public void NonFiniteViewportIsRejected()
    {
        CameraProjectionResult projection = Invalid(
            _hd,
            Matrix3x2.Identity,
            10f,
            new Vector4(float.NaN, 0f, 1f, 1f),
            ClearKind.Color,
            Color.Black);

        Assert.Equal(CameraInvalidReason.Viewport, projection.Reason);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(-0.5f, 1f)]
    [InlineData(1f, -0.5f)]
    public void NonPositiveViewportExtentIsRejected(float width, float height)
    {
        CameraProjectionResult projection = Invalid(
            _hd,
            Matrix3x2.Identity,
            10f,
            new Vector4(0f, 0f, width, height),
            ClearKind.Color,
            Color.Black);

        Assert.Equal(CameraInvalidReason.Viewport, projection.Reason);
    }

    [Fact]
    public void UndefinedClearKindIsRejected()
    {
        CameraProjectionResult projection = Invalid(
            _hd,
            Matrix3x2.Identity,
            10f,
            _fullViewport,
            (ClearKind)99,
            Color.Black);

        Assert.Equal(CameraInvalidReason.ClearKind, projection.Reason);
    }

    [Fact]
    public void NonFiniteClearColorIsRejectedOnlyForColorClears()
    {
        CameraProjectionResult color = Invalid(
            _hd,
            Matrix3x2.Identity,
            10f,
            _fullViewport,
            ClearKind.Color,
            new Color(float.NaN, 0f, 0f, 1f));
        Assert.Equal(CameraInvalidReason.ClearColor, color.Reason);

        CameraProjectionResult none = Create(
            _hd,
            Matrix3x2.Identity,
            10f,
            _fullViewport,
            ClearKind.None,
            new Color(float.NaN, 0f, 0f, 1f));
        Assert.True(none.IsValid);

        CameraProjectionResult depth = Create(
            _hd,
            Matrix3x2.Identity,
            10f,
            _fullViewport,
            ClearKind.Depth,
            new Color(float.PositiveInfinity, 0f, 0f, 1f));
        Assert.True(depth.IsValid);
    }

    [Fact]
    public void SingularCameraMatrixIsRejected()
    {
        CameraProjectionResult projection = Invalid(
            _hd,
            Matrix3x2.CreateScale(0f, 1f),
            10f,
            _fullViewport,
            ClearKind.Color,
            Color.Black);

        Assert.Equal(CameraInvalidReason.SingularMatrix, projection.Reason);
    }

    [Theory]
    [InlineData(2f, 1f)]
    [InlineData(1f, 0.5f)]
    [InlineData(1.001f, 1f)]
    public void NonUnitCameraScaleIsRejected(float scaleX, float scaleY)
    {
        CameraProjectionResult projection = Invalid(
            _hd,
            Matrix3x2.CreateScale(scaleX, scaleY),
            10f,
            _fullViewport,
            ClearKind.Color,
            Color.Black);

        Assert.Equal(CameraInvalidReason.NonUnitScale, projection.Reason);
    }

    [Fact]
    public void UnitScaleWithinTheEpsilonIsAccepted()
    {
        CameraProjectionResult projection = Create(
            _hd,
            Matrix3x2.CreateScale(1.00001f, 1.00001f),
            10f,
            _fullViewport,
            ClearKind.Color,
            Color.Black);

        Assert.True(projection.IsValid);
    }

    [Fact]
    public void RoundsViewportHalfUp()
    {
        CameraProjectionResult below = Create(
            new Vector2(1000f, 1000f),
            Matrix3x2.Identity,
            10f,
            new Vector4(0.0004f, 0.0004f, 1f, 1f),
            ClearKind.Color,
            Color.Black);
        Assert.Equal(0, below.Left);
        Assert.Equal(0, below.Top);

        CameraProjectionResult atHalf = Create(
            new Vector2(1000f, 1000f),
            Matrix3x2.Identity,
            10f,
            new Vector4(0.0005f, 0.0005f, 1f, 1f),
            ClearKind.Color,
            Color.Black);
        Assert.Equal(1, atHalf.Left);
        Assert.Equal(1, atHalf.Top);

        CameraProjectionResult above = Create(
            new Vector2(1000f, 1000f),
            Matrix3x2.Identity,
            10f,
            new Vector4(0.0006f, 0.0006f, 1f, 1f),
            ClearKind.Color,
            Color.Black);
        Assert.Equal(1, above.Left);
        Assert.Equal(1, above.Top);
    }

    [Fact]
    public void ClampsTheViewportToTheFramebuffer()
    {
        CameraProjectionResult projection = Create(
            new Vector2(1000f, 1000f),
            Matrix3x2.Identity,
            10f,
            new Vector4(-0.5f, -0.5f, 2f, 2f),
            ClearKind.Color,
            Color.Black);

        Assert.Equal(0, projection.Left);
        Assert.Equal(0, projection.Top);
        Assert.Equal(1000, projection.Width);
        Assert.Equal(1000, projection.Height);
    }

    [Fact]
    public void SplitScreenViewportsMeetAtTheSeam()
    {
        CameraProjectionResult left = Create(
            new Vector2(1000f, 500f),
            Matrix3x2.Identity,
            10f,
            new Vector4(0f, 0f, 0.5f, 1f),
            ClearKind.Color,
            Color.Black);
        CameraProjectionResult right = Create(
            new Vector2(1000f, 500f),
            Matrix3x2.Identity,
            10f,
            new Vector4(0.5f, 0f, 0.5f, 1f),
            ClearKind.Color,
            Color.Black);

        Assert.Equal(0, left.Left);
        Assert.Equal(500, left.Width);
        Assert.Equal(500, right.Left);
        Assert.Equal(500, right.Width);
        Assert.Equal(left.Left + left.Width, right.Left);
        Assert.Equal(1f, left.Aspect);
        Assert.Equal(1f, right.Aspect);
    }

    [Fact]
    public void SizeIsTheHalfVisibleHeight()
    {
        CameraProjectionResult projection = Create(
            _hd,
            Matrix3x2.Identity,
            5f,
            _fullViewport,
            ClearKind.Color,
            Color.Black);

        Assert.Equal(10f, projection.WorldSize.Y, 4);
        Assert.Equal(2f * 5f * (1280f / 720f), projection.WorldSize.X, 4);
        Assert.Equal(16f / 9f, projection.Aspect, 4);
    }

    [Fact]
    public void UsesTheRoundedPixelAspect()
    {
        CameraProjectionResult projection = Create(
            _hd,
            Matrix3x2.Identity,
            10f,
            new Vector4(0f, 0f, 0.5f, 1f),
            ClearKind.Color,
            Color.Black);

        Assert.Equal(640, projection.Width);
        Assert.Equal(720, projection.Height);
        Assert.Equal(640f / 720f, projection.Aspect, 4);
        Assert.Equal(2f * 10f * (640f / 720f), projection.WorldSize.X, 4);
    }

    [Fact]
    public void ProjectionMatchesTheDocumentedNdcFormulaForTranslation()
    {
        var cameraWorld = Matrix3x2.CreateTranslation(10f, 20f);
        CameraProjectionResult projection = Create(
            _hd,
            cameraWorld,
            10f,
            _fullViewport,
            ClearKind.Color,
            Color.Black);

        Vector4 ndc = Vector4.Transform(
            new Vector4(13f, 25f, 0f, 1f),
            projection.ViewProjection);

        float aspect = 1280f / 720f;
        Assert.Equal(3f / (10f * aspect), ndc.X, 4);
        Assert.Equal(-5f / 10f, ndc.Y, 4);
        Assert.Equal(0f, ndc.Z, 4);
        Assert.Equal(1f, ndc.W, 4);
    }

    [Fact]
    public void ProjectionMatchesTheDocumentedNdcFormulaForRotation()
    {
        var cameraWorld = Matrix3x2.CreateRotation(MathF.PI / 2f);
        CameraProjectionResult projection = Create(
            _hd,
            cameraWorld,
            10f,
            _fullViewport,
            ClearKind.Color,
            Color.Black);

        // A point +X of the camera moves to view-space -Y (behind the camera's right).
        Vector4 ndc = Vector4.Transform(
            new Vector4(10f, 0f, 0f, 1f),
            projection.ViewProjection);

        Assert.Equal(0f, ndc.X, 4);
        Assert.Equal(1f, ndc.Y, 4);
    }

    [Fact]
    public void ScreenWorldRoundTripIncludesViewportOffsets()
    {
        CameraProjectionResult projection = Create(
            _hd,
            Matrix3x2.CreateTranslation(4f, -3f),
            10f,
            new Vector4(0.25f, 0.25f, 0.5f, 0.5f),
            ClearKind.Color,
            Color.Black);

        var world = new Vector2(7f, 2f);
        Vector2 screen = CameraProjection.WorldToFramebuffer(projection, world);
        Vector2 roundTrip = CameraProjection.FramebufferToWorld(projection, screen);

        Assert.Equal(world.X, roundTrip.X, 3);
        Assert.Equal(world.Y, roundTrip.Y, 3);
        Assert.InRange(screen.X, projection.Left, projection.Left + projection.Width);
        Assert.InRange(screen.Y, projection.Top, projection.Top + projection.Height);
    }

    [Fact]
    public void PointsOutsideTheViewportExtrapolate()
    {
        CameraProjectionResult projection = Create(
            _hd,
            Matrix3x2.Identity,
            10f,
            _fullViewport,
            ClearKind.Color,
            Color.Black);

        Vector2 screen = CameraProjection.WorldToFramebuffer(
            projection,
            new Vector2(1000f, -1000f));
        Assert.True(screen.X > projection.Left + projection.Width);
        Assert.True(screen.Y > projection.Top + projection.Height);

        Vector2 roundTrip = CameraProjection.FramebufferToWorld(projection, screen);
        Assert.Equal(1000f, roundTrip.X, 2);
        Assert.Equal(-1000f, roundTrip.Y, 2);
    }

    [Fact]
    public void ValidCamerasNeverProduceNan()
    {
        CameraProjectionResult projection = Create(
            _hd,
            Matrix3x2.CreateTranslation(1f, 2f),
            3f,
            new Vector4(0.1f, 0.2f, 0.3f, 0.4f),
            ClearKind.Color,
            Color.Black);

        Assert.True(projection.IsValid);
        Assert.True(float.IsFinite(projection.WorldSize.X));
        Assert.True(float.IsFinite(projection.WorldSize.Y));
        Assert.True(float.IsFinite(projection.ViewProjection.M11));
    }

    private static CameraProjectionResult Create(
        Vector2 framebuffer,
        Matrix3x2 cameraWorld,
        float size,
        Vector4 viewport,
        ClearKind clear,
        Color clearColor)
    {
        bool valid = CameraProjection.TryCreate(
            framebuffer,
            cameraWorld,
            size,
            viewport,
            clear,
            clearColor,
            out CameraProjectionResult projection);
        Assert.True(valid);
        Assert.Equal(CameraInvalidReason.None, projection.Reason);
        Assert.True(projection.IsValid);
        return projection;
    }

    private static CameraProjectionResult Invalid(
        Vector2 framebuffer,
        Matrix3x2 cameraWorld,
        float size,
        Vector4 viewport,
        ClearKind clear,
        Color clearColor)
    {
        bool valid = CameraProjection.TryCreate(
            framebuffer,
            cameraWorld,
            size,
            viewport,
            clear,
            clearColor,
            out CameraProjectionResult projection);
        Assert.False(valid);
        Assert.False(projection.IsValid);
        return projection;
    }
}
