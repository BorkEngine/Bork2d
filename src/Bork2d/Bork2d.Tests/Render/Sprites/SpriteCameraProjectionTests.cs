using System.Numerics;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Sprites;

/// <summary>
/// Proves the renderer consumes the same pure camera projection helper as the public camera
/// helpers (M8 §6.3/§6.9).
/// </summary>
public sealed class SpriteCameraProjectionTests
{
    [Fact]
    public void RendererCameraProjectionMatchesTheSharedHelper()
    {
        var camera = new CameraSnapshot(
            ZIndex: 0,
            WorldMatrix: Matrix3x2.CreateTranslation(4f, -2f),
            Size: 5f,
            Viewport: new Vector4(0.25f, 0f, 0.5f, 1f),
            CullingMask: 1,
            Clear: ClearKind.Color,
            ClearColor: Color.Black);
        var framebuffer = new Vector2(800f, 600f);

        bool valid = SpriteCameraProjection.TryCreate(camera, framebuffer, out CameraProjectionResult projection);

        Assert.True(valid);
        bool expected = CameraProjection.TryCreate(
            framebuffer,
            camera.WorldMatrix,
            camera.Size,
            camera.Viewport,
            camera.Clear,
            camera.ClearColor,
            out CameraProjectionResult shared);
        Assert.True(expected);
        Assert.Equal(shared.Left, projection.Left);
        Assert.Equal(shared.Top, projection.Top);
        Assert.Equal(shared.Width, projection.Width);
        Assert.Equal(shared.Height, projection.Height);
        Assert.Equal(shared.Aspect, projection.Aspect);
        Assert.Equal(shared.WorldSize, projection.WorldSize);
        Assert.Equal(shared.ViewProjection, projection.ViewProjection);
    }

    [Fact]
    public void RendererCameraProjectionRejectsAnInvalidCamera()
    {
        var camera = new CameraSnapshot(
            ZIndex: 0,
            WorldMatrix: Matrix3x2.Identity,
            Size: 0f,
            Viewport: new Vector4(0f, 0f, 1f, 1f),
            CullingMask: 1,
            Clear: ClearKind.None,
            ClearColor: Color.Black);

        bool valid = SpriteCameraProjection.TryCreate(
            camera,
            new Vector2(800f, 600f),
            out CameraProjectionResult projection);

        Assert.False(valid);
        Assert.Equal(CameraInvalidReason.Size, projection.Reason);
    }
}
