using System.Diagnostics;
using System.Numerics;
using Bork2d.Bake.Shaders;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Vulkan;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Sdk;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Opt-in real-GPU proof for the unified 2D line path: the built-in line shader, open/closed lines,
/// butt caps, miter and bevel corners, AA coverage, world transforms, custom line shaders using
/// <c>vLineUv</c>/<c>vColor</c>/instance uniforms/<c>BORK2D_TIME</c>, custom-shader fallback,
/// sprite/line ordering by ZIndex and screen Y, per-camera rotated ordering, and the line statistics
/// with validation 0 errors/0 warnings.
/// </summary>
/// <remarks>
/// The tests drive the real offscreen renderer through the snapshot handoff exactly like the host.
/// They build line snapshots directly with <see cref="FrameSnapshot.AddLine"/> and
/// <see cref="LineGeometryBuilder.Build"/>. They skip when the Vulkan loader/device/validation layer
/// is unavailable (D-M6-05, D-M6-20).
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class LineRenderGpuTests
{
    private const int FramebufferSize = 64;
    private const float CameraSize = 8f;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _joinTimeout = TimeSpan.FromSeconds(20);
    private static readonly Color _clearColor = new(0f, 0f, 0f, 1f);
    private static readonly Color _red = new(1f, 0f, 0f, 1f);
    private static readonly Color _green = new(0f, 1f, 0f, 1f);

    [GpuFact]
    public async Task OpenLineDrawsItsWidthAndButtCapsWithTheBuiltInLineShader()
    {
        using var assets = new TestRendererAssets();
        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                AddCamera(snapshot);
                AddLine(
                    snapshot,
                    points: [new Vector2(-4f, 0f), new Vector2(4f, 0f)],
                    width: 1f,
                    color: _red);
            };
            PublishUntil(renderer, producer, fill, stats => stats.Lines == 1 && stats.DrawCalls == 2);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);

            // The line spans screen x 16..48 and y 30..34; the ends are butt caps with no extension.
            AssertPixel(result, 32, 32, 255, 0, 0, 255, "the line center");
            AssertPixel(result, 32, 30, 255, 0, 0, 255, "the top width edge");
            AssertPixel(result, 32, 33, 255, 0, 0, 255, "the bottom width edge");
            AssertOpaqueBlackPixel(result, 32, 29, "just above the line");
            AssertOpaqueBlackPixel(result, 32, 34, "just below the line");
            AssertPixel(result, 16, 32, 255, 0, 0, 255, "the left butt cap");
            AssertPixel(result, 47, 32, 255, 0, 0, 255, "the right butt cap");
            AssertOpaqueBlackPixel(result, 15, 32, "just left of the butt cap");
            AssertOpaqueBlackPixel(result, 48, 32, "just right of the butt cap");

            RenderStats stats = renderer.RenderStats;
            Assert.Equal(1, stats.Lines);
            Assert.Equal(0, stats.SkippedLines);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task ClosedLineDrawsItsLoopAndMiteredCorners()
    {
        using var assets = new TestRendererAssets();
        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                AddCamera(snapshot);
                AddLine(
                    snapshot,
                    points:
                    [
                        new Vector2(-3f, -3f),
                        new Vector2(3f, -3f),
                        new Vector2(3f, 3f),
                        new Vector2(-3f, 3f),
                    ],
                    width: 2f,
                    closed: true,
                    color: _red);
            };
            PublishUntil(renderer, producer, fill, stats => stats.Lines == 1 && stats.DrawCalls == 2);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);

            // The loop's edges cover screen x/y 20..44; the interior stays black.
            AssertOpaqueBlackPixel(result, 32, 32, "the unfilled closed-line interior");
            AssertPixel(result, 32, 20, 255, 0, 0, 255, "the top loop edge");
            AssertPixel(result, 32, 44, 255, 0, 0, 255, "the bottom loop edge");
            AssertPixel(result, 20, 32, 255, 0, 0, 255, "the left loop edge");
            AssertPixel(result, 44, 32, 255, 0, 0, 255, "the right loop edge");
            AssertPixel(result, 20, 20, 255, 0, 0, 255, "the top-left mitered corner");
            AssertOpaqueBlackPixel(result, 15, 15, "beyond the top-left miter tip");
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task AMiteredCornerFillsTheOuterJointWhereABeveledSharpJointDoesNot()
    {
        using var assets = new TestRendererAssets();
        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                AddCamera(snapshot);
                AddLine(
                    snapshot,
                    points: [new Vector2(-6f, 0f), new Vector2(-2f, 0f), new Vector2(-2f, 4f)],
                    width: 2f,
                    color: _red);
                AddLine(
                    snapshot,
                    points: [new Vector2(1f, 0f), new Vector2(5f, 0f), new Vector2(1.241f, 1.368f)],
                    width: 2f,
                    zIndex: 1,
                    color: _red);
            };
            PublishUntil(renderer, producer, fill, stats => stats.Lines == 2 && stats.DrawCalls == 2);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);

            // The 90-degree joint at world (-2, 0) has a miter that reaches the outer corner; a bevel
            // would stop at the segment ends and leave this pixel black.
            AssertPixel(result, 26, 35, 255, 0, 0, 255, "inside the 90-degree miter");
            // The ~160-degree joint at world (5, 0) exceeds the miter limit: the bevel keeps its
            // coverage near the joint and leaves the would-be miter tip black.
            AssertPixel(result, 52, 32, 255, 0, 0, 255, "inside the bevel triangle");
            AssertOpaqueBlackPixel(result, 62, 35, "the beveled corner beyond the bevel");
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task AntiAliasingSoftensTheEdgeCoverage()
    {
        using var assets = new TestRendererAssets();
        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);

            Action<FrameSnapshot> fill = snapshot =>
            {
                AddCamera(snapshot);
                AddLine(
                    snapshot,
                    points: [new Vector2(-4f, 0f), new Vector2(4f, 0f)],
                    width: 5f,
                    color: _red);
            };
            PublishUntil(renderer, producer, fill, stats => stats.Lines == 1);

            FrameCaptureResult aliased = await CaptureAsync(renderer, producer, fill);

            Action<FrameSnapshot> antialiasedFill = snapshot =>
            {
                AddCamera(snapshot);
                AddLine(
                    snapshot,
                    points: [new Vector2(-4f, 0f), new Vector2(4f, 0f)],
                    width: 5f,
                    antiAlias: true,
                    color: _red);
            };
            PublishUntil(renderer, producer, antialiasedFill, stats => stats.Lines == 1);
            FrameCaptureResult antialiased = await CaptureAsync(renderer, producer, antialiasedFill);

            // Width 5 covers screen y 22..42. The aliased edge row is fully saturated; the AA edge row
            // is a partial coverage ramp and is therefore dimmer than both the aliased edge and the
            // fully covered center.
            (byte centerR, _, _, _) = ReadPixel(antialiased, 32, 32);
            (byte aliasedEdgeR, _, _, _) = ReadPixel(aliased, 32, 22);
            (byte aaEdgeR, _, _, _) = ReadPixel(antialiased, 32, 22);
            Assert.InRange(aliasedEdgeR, 250, 255);
            Assert.InRange(centerR, 250, 255);
            Assert.InRange(aaEdgeR, 100, 240);
            Assert.True(
                aaEdgeR < aliasedEdgeR,
                $"AA edge {aaEdgeR} must be softer than the aliased edge {aliasedEdgeR}.");
            AssertOpaqueBlackPixel(antialiased, 32, 21, "just above the AA line");
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task CustomLineShaderReadsLineUvColorInstanceUniformAndFrameTime()
    {
        using var assets = new TestRendererAssets();
        ShaderUniform[] instance =
        [
            new ShaderUniform("uParam", ShaderUniformType.Vector4, 0, Vector4.Zero),
        ];
        ShaderAsset shader = MakeShader("materials/line-attributes-spv", instance: instance);
        byte[] spirV = Compile(
            "void main() { outColor = vec4(vLineUv.x, vColor.g, uParam.x, BORK2D_TIME); }",
            instance: instance);
        assets.AddShader("materials/line-attributes", shader, spirV);
        ShaderRenderData metadata = ShaderRenderData.Freeze(shader);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                snapshot.Time = 0.5;
                AddCamera(snapshot);
                AddLine(
                    snapshot,
                    points: [new Vector2(-4f, 0f), new Vector2(4f, 0f)],
                    width: 5f,
                    color: new Color(1f, 0.5f, 0.25f, 1f),
                    shader: metadata,
                    uniform0: new Vector4(0.25f, 0f, 0f, 0f));
            };
            PublishUntil(renderer, producer, fill, stats => stats.Lines == 1 && stats.DrawCalls == 2);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);

            // Width 5 covers screen y 22..42, so pixel row 32's center (32.5) has
            // vLineUv.x = (32.5 - 22) / 20 = 0.525. The shader outputs
            // (0.525, vColor.g = 0.5, uParam.x = 0.25, time = 0.5) and blends with alpha 0.5 over
            // opaque black, so the linear RGB is half the output and the alpha is 0.75.
            AssertPixel(
                result,
                32,
                32,
                LinearToSrgbByte(0.525f * 0.5f),
                LinearToSrgbByte(0.5f * 0.5f),
                LinearToSrgbByte(0.25f * 0.5f),
                191,
                "the custom line shader output",
                tolerance: 4);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task FailedCustomLineShaderFallsBackToTheBuiltInLineShader()
    {
        using var assets = new TestRendererAssets();
        ShaderAsset shader = MakeShader(
            "materials/line-invalid-spv",
            layoutVersion: ShaderLayout.CurrentVersion - 1);
        byte[] spirV = Compile("void main() { outColor = vec4(0.0, 0.0, 1.0, 1.0); }");
        assets.AddShader("materials/line-invalid", shader, spirV);
        ShaderRenderData metadata = ShaderRenderData.Freeze(shader);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out CapturingLoggerProvider provider);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                AddCamera(snapshot);
                AddLine(
                    snapshot,
                    points: [new Vector2(-4f, 0f), new Vector2(4f, 0f)],
                    width: 5f,
                    color: _red,
                    shader: metadata);
            };
            PublishUntil(renderer, producer, fill, stats => stats.Lines == 1);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);

            // The custom shader would paint blue; the line default draws the line color instead.
            AssertPixel(result, 32, 32, 255, 0, 0, 255, "the built-in line fallback");
            RenderStats stats = renderer.RenderStats;
            Assert.Equal(0, stats.SkippedLines);
            Assert.Equal(1, stats.Lines);
            Assert.Contains(
                provider.Entries,
                entry => entry.Message.Contains("materials/line-invalid", StringComparison.Ordinal));
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task SpriteAndLineOrderByZIndex()
    {
        using var assets = new TestRendererAssets();
        assets.AddSolidTexture("tex/red", 255, 0, 0);
        SpriteAsset sprite = MakeSprite("tex/red");

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);

            Action<FrameSnapshot> lineOverSprite = snapshot =>
            {
                AddCamera(snapshot);
                AddSprite(snapshot, sprite, layer: 1u, zIndex: 0);
                AddLine(
                    snapshot,
                    points: [new Vector2(-4f, 0f), new Vector2(4f, 0f)],
                    zIndex: 1,
                    width: 5f,
                    color: _green);
            };
            PublishUntil(
                renderer,
                producer,
                lineOverSprite,
                stats => stats.Instances == 1 && stats.Lines == 1 && stats.DrawCalls == 3);
            FrameCaptureResult first = await CaptureAsync(renderer, producer, lineOverSprite);
            AssertPixel(first, 32, 32, 0, 255, 0, 255, "the line over the sprite");

            Action<FrameSnapshot> spriteOverLine = snapshot =>
            {
                AddCamera(snapshot);
                AddLine(
                    snapshot,
                    points: [new Vector2(-4f, 0f), new Vector2(4f, 0f)],
                    zIndex: 0,
                    width: 5f,
                    color: _green);
                AddSprite(snapshot, sprite, layer: 1u, zIndex: 1);
            };
            PublishUntil(
                renderer,
                producer,
                spriteOverLine,
                stats => stats.Instances == 1 && stats.Lines == 1 && stats.DrawCalls == 3);
            FrameCaptureResult second = await CaptureAsync(renderer, producer, spriteOverLine);
            AssertPixel(second, 32, 32, 255, 0, 0, 255, "the sprite over the line");
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task SpriteAndLineOrderByScreenYWithinEqualZIndex()
    {
        using var assets = new TestRendererAssets();
        assets.AddSolidTexture("tex/red", 255, 0, 0);
        SpriteAsset sprite = MakeSprite("tex/red");

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);

            // The line is lower on screen (world y = 0 vs the sprite at world y = 1), so at equal
            // ZIndex it draws later and wins the overlap at the screen center.
            Action<FrameSnapshot> lineLower = snapshot =>
            {
                AddCamera(snapshot);
                AddSprite(snapshot, sprite, layer: 1u, zIndex: 0, y: 1f);
                AddLine(
                    snapshot,
                    points: [new Vector2(-4f, 0f), new Vector2(4f, 0f)],
                    zIndex: 0,
                    width: 4f,
                    color: _green);
            };
            PublishUntil(
                renderer,
                producer,
                lineLower,
                stats => stats.Instances == 1 && stats.Lines == 1 && stats.DrawCalls == 3);
            FrameCaptureResult first = await CaptureAsync(renderer, producer, lineLower);
            AssertPixel(first, 32, 32, 0, 255, 0, 255, "the lower line over the upper sprite");

            // The sprite is now lower (world y = -1), so it draws after the line and wins.
            Action<FrameSnapshot> spriteLower = snapshot =>
            {
                AddCamera(snapshot);
                AddSprite(snapshot, sprite, layer: 1u, zIndex: 0, y: -1f);
                AddLine(
                    snapshot,
                    points: [new Vector2(-4f, 0f), new Vector2(4f, 0f)],
                    zIndex: 0,
                    width: 4f,
                    color: _green);
            };
            PublishUntil(
                renderer,
                producer,
                spriteLower,
                stats => stats.Instances == 1 && stats.Lines == 1 && stats.DrawCalls == 3);
            FrameCaptureResult second = await CaptureAsync(renderer, producer, spriteLower);
            AssertPixel(second, 32, 32, 255, 0, 0, 255, "the lower sprite over the upper line");
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task RotatedCameraReversesTheScreenYOrderAcrossCameras()
    {
        using var assets = new TestRendererAssets();
        assets.AddSolidTexture("tex/red", 255, 0, 0);
        SpriteAsset sprite = MakeSprite("tex/red");

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                snapshot.AddCamera(
                    new CameraSnapshot(
                        ZIndex: 0,
                        WorldMatrix: Matrix3x2.Identity,
                        Size: CameraSize,
                        Viewport: new Vector4(0f, 0f, 0.5f, 1f),
                        CullingMask: 1u,
                        Clear: ClearKind.Color,
                        ClearColor: _clearColor));
                snapshot.AddCamera(
                    new CameraSnapshot(
                        ZIndex: 1,
                        WorldMatrix: Matrix3x2.CreateRotation(MathF.PI),
                        Size: CameraSize,
                        Viewport: new Vector4(0.5f, 0f, 0.5f, 1f),
                        CullingMask: 1u,
                        Clear: ClearKind.Color,
                        ClearColor: _clearColor));
                AddSprite(snapshot, sprite, layer: 1u, zIndex: 0);
                AddLine(
                    snapshot,
                    points: [new Vector2(-4f, 0f), new Vector2(4f, 0f)],
                    zIndex: 0,
                    width: 4f,
                    color: _green,
                    world: Matrix3x2.CreateTranslation(0f, -1f));
            };
            PublishUntil(
                renderer,
                producer,
                fill,
                stats => stats.Instances == 2 && stats.Lines == 2 && stats.DrawCalls == 5);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);

            // The unrotated camera sees the line lower on screen, so the line draws after the sprite
            // and covers the overlap. The 180-degree camera sees it higher, so the sprite draws after
            // the line and covers the overlap.
            AssertPixel(result, 16, 32, 0, 255, 0, 255, "the unrotated camera line order");
            AssertPixel(result, 48, 32, 255, 0, 0, 255, "the rotated camera sprite order");
            RenderStats stats = renderer.RenderStats;
            Assert.Equal(2, stats.NonEmptyCameras);
            Assert.Equal(2, stats.Lines);
            Assert.Equal(0, stats.SkippedLines);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public void LineStatsCountDrawnRecordsAndReportSkipsAndValidationCleanly()
    {
        using var assets = new TestRendererAssets();
        assets.AddSolidTexture("tex/red", 255, 0, 0);
        SpriteAsset sprite = MakeSprite("tex/red");
        ShaderAsset pendingShader = MakeShader("materials/line-stats-pending-spv");
        assets.AddPendingShader("materials/line-stats-pending", pendingShader);
        ShaderRenderData pendingMetadata = ShaderRenderData.Freeze(pendingShader);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out CapturingLoggerProvider provider);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                AddCamera(snapshot);
                AddSprite(snapshot, sprite, layer: 1u, zIndex: 0);
                AddSprite(snapshot, sprite, layer: 1u, zIndex: 1);
                AddLine(
                    snapshot,
                    points: [new Vector2(-4f, 0f), new Vector2(4f, 0f)],
                    zIndex: 2,
                    width: 2f,
                    color: _red);
                AddLine(
                    snapshot,
                    points: [new Vector2(-4f, 2f), new Vector2(4f, 2f)],
                    zIndex: 3,
                    width: 2f,
                    color: _green);
                AddNullLine(snapshot, zIndex: 4);
                AddLine(
                    snapshot,
                    points: [new Vector2(-4f, -2f), new Vector2(4f, -2f)],
                    zIndex: 5,
                    width: 2f,
                    color: _red,
                    shader: pendingMetadata);
            };
            PublishUntil(
                renderer,
                producer,
                fill,
                stats => stats.Instances == 2
                    && stats.Lines == 2
                    && stats.SkippedLines == 2
                    && stats.DrawCalls == 3);

            RenderStats stats = renderer.RenderStats;
            Assert.Equal(2, stats.Instances);
            Assert.Equal(2, stats.Lines);
            Assert.Equal(2, stats.SkippedLines);
            Assert.Equal(1, stats.Cameras);
            Assert.Equal(1, stats.NonEmptyCameras);
            Assert.Equal(3, stats.DrawCalls);
            Assert.Equal(0, stats.SkippedSprites);
            Assert.Equal(0, stats.ValidationErrors);
            Assert.Equal(0, stats.ValidationWarnings);
            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            Assert.Null(renderer.FatalException);
            Assert.Contains(
                provider.Entries,
                entry => entry.Message.Contains("no drawable geometry", StringComparison.Ordinal));
            Assert.DoesNotContain(
                provider.Entries,
                entry => entry.Message.Contains(
                    "materials/line-stats-pending",
                    StringComparison.Ordinal));
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    private static void AddCamera(
        FrameSnapshot snapshot,
        int zIndex = 0,
        uint mask = 1u,
        ClearKind clear = ClearKind.Color,
        Vector4? viewport = null,
        Matrix3x2? world = null)
        => snapshot.AddCamera(
            new CameraSnapshot(
                zIndex,
                world ?? Matrix3x2.Identity,
                CameraSize,
                viewport ?? new Vector4(0f, 0f, 1f, 1f),
                mask,
                clear,
                _clearColor));

    private static void AddSprite(
        FrameSnapshot snapshot,
        SpriteAsset asset,
        uint layer,
        int zIndex = 0,
        float x = 0f,
        float y = 0f)
        => snapshot.AddSprite(
            new SpriteSnapshot(
                SpriteRenderData.Freeze(asset),
                Matrix3x2.CreateTranslation(x, y),
                Color.White,
                layer,
                zIndex,
                FlipX: false,
                FlipY: false));

    private static void AddLine(
        FrameSnapshot snapshot,
        Vector2[] points,
        uint layer = 1u,
        int zIndex = 0,
        float width = 1f,
        bool closed = false,
        bool antiAlias = false,
        Matrix3x2? world = null,
        Color? color = null,
        ShaderRenderData? shader = null,
        Vector4? uniform0 = null)
    {
        Matrix3x2 transform = world ?? Matrix3x2.Identity;
        LineGeometryData geometry = Assert.IsType<LineGeometryData>(
            LineGeometryBuilder.Build(points, closed, width));
        snapshot.AddLine(
            new LineSnapshot(
                geometry,
                transform,
                color ?? _red,
                layer,
                zIndex,
                antiAlias)
            {
                Bounds = LineBounds.CreateWorldBounds(geometry, transform),
                ShaderData = shader,
                Uniform0 = uniform0 ?? Vector4.Zero,
            });
    }

    private static void AddNullLine(FrameSnapshot snapshot, int zIndex)
        => snapshot.AddLine(
            new LineSnapshot(
                Geometry: null,
                WorldMatrix: Matrix3x2.Identity,
                Color: _red,
                Layer: 1u,
                ZIndex: zIndex,
                AntiAlias: false));

    private static SpriteAsset MakeSprite(string? texture) => new()
    {
        Texture = texture,
        Uvs =
        [
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        ],
        Scale = new Vector2(10f),
        Pivot = new Vector2(0.5f, 0.5f),
        Topology = SpriteTopology.Quad,
    };

    private static ShaderAsset MakeShader(
        string spirVPath,
        ShaderUniform[]? instance = null,
        ShaderUniform[]? globals = null,
        uint layoutVersion = ShaderLayout.CurrentVersion)
        => new()
        {
            SpirV = spirVPath,
            LayoutVersion = layoutVersion,
            EntryPoint = "main",
            InstanceUniforms = instance ?? [],
            GlobalUniforms = globals ?? [],
            TextureSlots = [],
        };

    private static byte[] Compile(
        string source,
        ShaderUniform[]? instance = null,
        ShaderUniform[]? globals = null)
    {
        string prologue = ShaderPrologue.Build(
            instance ?? [],
            globals ?? []);
        if (!GlslCompiler.TryCompileFragment(
            ShaderPrologue.Compose(prologue, source),
            "line-shader-test.frag",
            out byte[] spirV,
            out string message))
        {
            throw new InvalidOperationException($"The test line shader failed to compile: {message}");
        }

        return spirV;
    }

    private static ILoggerFactory CreateLoggerFactory(out CapturingLoggerProvider provider)
    {
        var created = new CapturingLoggerProvider();
        provider = created;
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
            builder.AddProvider(created);
        });
    }

    private static VulkanOffscreenRenderer StartOffscreen(
        TestRendererAssets assets,
        ILoggerFactory loggerFactory)
    {
        var startInfo = new RendererStartInfo
        {
            Mode = RendererMode.Offscreen,
            InstanceHandle = 0,
            Width = FramebufferSize,
            Height = FramebufferSize,
            ValidationRequested = true,
            LoggerFactory = loggerFactory,
            Assets = assets,
        };

        var renderer = Assert.IsType<VulkanOffscreenRenderer>(
            new VulkanRendererFactory().Create(startInfo));
        renderer.Start();
        if (!renderer.WaitForReady(_timeout))
        {
            throw new TimeoutException("The line-render offscreen renderer did not become ready.");
        }

        if (renderer.FatalException is { } fatal)
        {
            if (IsUnavailable(fatal))
            {
                throw SkipException.ForSkip(
                    $"No suitable Vulkan 1.3 offscreen device is available: {fatal.Message}");
            }

            throw new InvalidOperationException(
                "The line-render offscreen renderer faulted during startup.",
                fatal);
        }

        if (!renderer.ValidationEnabled)
        {
            throw SkipException.ForSkip(
                $"{VulkanNames.ValidationLayer} is not available; validation evidence cannot be "
                    + "gathered on this machine.");
        }

        return renderer;
    }

    private static void PublishUntil(
        VulkanOffscreenRenderer renderer,
        Producer producer,
        Action<FrameSnapshot> fill,
        Func<RenderStats, bool> until)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!until(renderer.RenderStats))
        {
            if (renderer.FatalException is { } fatal)
            {
                throw new InvalidOperationException("The renderer faulted while publishing.", fatal);
            }

            if (stopwatch.Elapsed >= _timeout)
            {
                throw new TimeoutException(
                    "The line frame did not reach the expected state: "
                        + Describe(renderer.RenderStats));
            }

            PublishFrame(renderer, producer, fill);
        }
    }

    private static void PublishFrame(
        VulkanOffscreenRenderer renderer,
        Producer producer,
        Action<FrameSnapshot> fill)
    {
        long sequence = renderer.RenderStats.FrameSequence;
        producer.Publish(fill);
        if (!renderer.WaitForProgress(
            () => renderer.RenderStats.FrameSequence > sequence,
            _timeout))
        {
            throw new TimeoutException(
                $"The frame was not published within {_timeout.TotalSeconds:0} s. "
                    + Describe(renderer.RenderStats));
        }
    }

    private static async Task<FrameCaptureResult> CaptureAsync(
        VulkanOffscreenRenderer renderer,
        Producer producer,
        Action<FrameSnapshot> fill)
    {
        Task<FrameCaptureResult> capture = renderer.CaptureFrameAsync();
        PublishFrame(renderer, producer, fill);
        return await capture.WaitAsync(_timeout);
    }

    private static void StopAndAssertClean(VulkanOffscreenRenderer renderer)
    {
        renderer.RequestStop();
        Assert.True(renderer.Join(_joinTimeout), "The renderer did not join.");
        Assert.Equal(RendererStatus.Stopped, renderer.Status);
        Assert.Equal(RendererStopReason.Requested, renderer.StopReason);
        Assert.Null(renderer.FatalException);
        Assert.Equal(0, renderer.Counters.ValidationErrorCount);
        Assert.Equal(0, renderer.Counters.ValidationWarningCount);
    }

    private static void Finish(VulkanOffscreenRenderer? renderer)
    {
        if (renderer is not null)
        {
            renderer.RequestStop();
            _ = renderer.Join(_joinTimeout);
        }
    }

    private static string Describe(RenderStats stats)
        => $"frame={stats.FrameSequence}, cameras={stats.Cameras}, "
            + $"nonEmpty={stats.NonEmptyCameras}, draws={stats.DrawCalls}, "
            + $"instances={stats.Instances}, lines={stats.Lines}, "
            + $"skippedSprites={stats.SkippedSprites}, skippedLines={stats.SkippedLines}, "
            + $"validation={stats.ValidationErrors}/{stats.ValidationWarnings}";

    private static void AssertPixel(
        FrameCaptureResult result,
        int x,
        int y,
        byte r,
        byte g,
        byte b,
        byte a,
        string because,
        int tolerance = 2)
    {
        (byte actualR, byte actualG, byte actualB, byte actualA) = ReadPixel(result, x, y);
        Assert.True(
            Math.Abs(actualR - r) <= tolerance
                && Math.Abs(actualG - g) <= tolerance
                && Math.Abs(actualB - b) <= tolerance
                && Math.Abs(actualA - a) <= tolerance,
            $"{because}: pixel ({x}, {y}) is ({actualR}, {actualG}, {actualB}, {actualA}), "
                + $"expected ({r}, {g}, {b}, {a}) +/- {tolerance}.");
    }

    private static void AssertOpaqueBlackPixel(
        FrameCaptureResult result,
        int x,
        int y,
        string because)
    {
        (byte r, byte g, byte b, byte a) = ReadPixel(result, x, y);
        Assert.True(
            r == 0 && g == 0 && b == 0 && a == 255,
            $"{because}: pixel ({x}, {y}) is ({r}, {g}, {b}, {a}), expected opaque black.");
    }

    private static (byte R, byte G, byte B, byte A) ReadPixel(
        FrameCaptureResult result,
        int x,
        int y)
    {
        int offset = ((y * result.Width) + x) * 4;
        ReadOnlySpan<byte> pixels = result.Pixels.Span;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    private static byte LinearToSrgbByte(float linear)
    {
        float encoded = linear <= 0.0031308f
            ? 12.92f * linear
            : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;
        return (byte)Math.Clamp(MathF.Round(encoded * 255f), 0f, 255f);
    }

    private static bool IsUnavailable(Exception exception)
    {
        string message = exception.Message;
        return message.Contains("could not be loaded", StringComparison.Ordinal)
            || message.Contains("No Vulkan physical devices", StringComparison.Ordinal)
            || message.Contains("No suitable Vulkan 1.3 device", StringComparison.Ordinal)
            || message.Contains("below the required 1.3.0", StringComparison.Ordinal)
            || message.Contains("requires Vulkan 1.3.0", StringComparison.Ordinal)
            || message.Contains("vkCreateInstance failed", StringComparison.Ordinal)
            || message.Contains("textureCompressionBC", StringComparison.Ordinal)
            || message.Contains(
                "maxPerStageDescriptorUpdateAfterBindSampledImages=0",
                StringComparison.Ordinal)
            || message.Contains("No UNORM offscreen color format", StringComparison.Ordinal);
    }

    private sealed class Producer
    {
        private readonly VulkanOffscreenRenderer _renderer;
        private long _published;

        internal Producer(VulkanOffscreenRenderer renderer) => _renderer = renderer;

        internal void Publish(Action<FrameSnapshot> fill)
        {
            var slot = _renderer.AcquireSnapshotSlot()
                ?? throw new InvalidOperationException(
                    "The renderer stopped consuming; the test producer cannot publish.");
            slot.Snapshot.Reset();
            slot.Snapshot.Sequence = ++_published;
            slot.Snapshot.Time = _published * 0.016;
            slot.Snapshot.DeltaTime = 0.016;
            fill(slot.Snapshot);
            _renderer.PublishSnapshot(slot);
        }
    }
}
