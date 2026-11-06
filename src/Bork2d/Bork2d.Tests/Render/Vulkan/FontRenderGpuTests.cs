using System.Diagnostics;
using System.Numerics;
using Bork2d.Bake.Fonts;
using Bork2d.Bake.Shaders;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
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
/// Opt-in real-GPU proof for the font render path: baked MSDF glyph quads drawn through the built-in
/// <c>shader/sdf</c> default and a custom font fragment shader, per-glyph ordering and run grouping
/// against sprites, separate runs per font asset, pending/failed custom-shader and atlas-page
/// behavior, empty text, and font-only statistics — all with validation 0 errors/0 warnings.
/// </summary>
/// <remarks>
/// The tests bake the authored Pong font into a small MSDF atlas at runtime with the task-1
/// <see cref="FontBakery"/>, so no binary fixture or generated image is committed, and follow the
/// custom-shader offscreen lifecycle, producer, and capture helpers. They skip when the Vulkan
/// loader/device/validation layer is unavailable.
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class FontRenderGpuTests
{
    private const int FramebufferSize = 64;
    private const string AtlasPath = "fonts/score-atlas";
    private const string FontPath = "examples/pong/Pong.Assets/Assets/fonts/score.ttf";

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _joinTimeout = TimeSpan.FromSeconds(20);

    private static readonly Vector2[] _fullUvs =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    private static readonly Color _clearColor = new(0f, 0f, 0f, 1f);

    [GpuFact]
    public async Task GlyphRendersVisiblePixelsAndFontOnlyStats()
    {
        using var assets = new TestRendererAssets();
        FontAsset fontAsset = BakeFont(assets);
        var font = new TestFont(fontAsset, "A", size: 8f);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot => AddScene(snapshot, font);
            PublishUntil(
                renderer,
                producer,
                fill,
                stats => stats.DrawCalls == 2
                    && stats.Glyphs == font.Layout.Glyphs.Length
                    && stats.SkippedFonts == 0);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);
            ScreenRect rect = ScreenRectOf(font.Layout.Glyphs[0], Matrix3x2.Identity);
            Assert.True(
                CountNonBlack(result, rect) > 40,
                "The glyph quad should contain visible coverage pixels.");
            AssertPixelNear(result, 2, 2, 0, 0, 0, 255, 3, "the background outside the glyph");

            RenderStats stats = renderer.RenderStats;
            Assert.Equal(font.Layout.Glyphs.Length, stats.Glyphs);
            Assert.Equal(0, stats.Instances);
            Assert.Equal(2, stats.DrawCalls);
            Assert.Equal(1, stats.NonEmptyCameras);
            Assert.Equal(0, stats.SkippedFonts);
            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task PendingCustomFontShaderSkipsThenDrawsThroughTheFontPipeline()
    {
        using var assets = new TestRendererAssets();
        FontAsset fontAsset = BakeFont(assets);
        var font = new TestFont(fontAsset, "A", size: 8f);
        ShaderAsset shader = MakeShader("materials/font-red-spv");
        byte[] spirV = Compile("void main() { outColor = vec4(1.0, 0.0, 0.0, 1.0); }");
        assets.AddPendingShader("materials/font-red", shader);
        ShaderRenderData metadata = ShaderRenderData.Freeze(shader);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot => AddScene(snapshot, font, shader: metadata);
            PublishUntil(
                renderer,
                producer,
                fill,
                stats => stats.SkippedFonts == 1 && stats.Glyphs == 0);
            Assert.Equal(1, renderer.RenderStats.DrawCalls);

            assets.CompleteShader("materials/font-red", spirV);
            PublishUntil(
                renderer,
                producer,
                fill,
                stats => stats.DrawCalls == 2
                    && stats.Glyphs == font.Layout.Glyphs.Length
                    && stats.SkippedFonts == 0);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);
            ScreenRect rect = ScreenRectOf(font.Layout.Glyphs[0], Matrix3x2.Identity);
            AssertPixelNear(
                result,
                (int)((rect.MinX + rect.MaxX) * 0.5f),
                (int)((rect.MinY + rect.MaxY) * 0.5f),
                255,
                0,
                0,
                255,
                2,
                "the custom font shader's red pixel");
            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task FailedCustomFontShaderFallsBackToSdfWithOneWarning()
    {
        using var assets = new TestRendererAssets();
        FontAsset fontAsset = BakeFont(assets);
        var font = new TestFont(fontAsset, "A", size: 8f);
        ShaderAsset invalid = MakeShader(
            "materials/font-invalid-spv",
            layoutVersion: ShaderLayout.CurrentVersion - 1);
        ShaderRenderData metadata = ShaderRenderData.Freeze(invalid);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out CapturingLoggerProvider provider);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot => AddScene(snapshot, font, shader: metadata);
            PublishUntil(
                renderer,
                producer,
                fill,
                stats => stats.DrawCalls == 2
                    && stats.Glyphs == font.Layout.Glyphs.Length
                    && stats.SkippedFonts == 0);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);
            ScreenRect rect = ScreenRectOf(font.Layout.Glyphs[0], Matrix3x2.Identity);
            Assert.True(
                CountNonBlack(result, rect) > 40,
                "The SDF fallback should still draw the glyph.");
            Assert.Equal(0, renderer.RenderStats.SkippedFonts);
            Assert.Contains(
                provider.Entries,
                entry => entry.Message.Contains("materials/font-invalid-spv", StringComparison.Ordinal));
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task PerGlyphOrderingAndRunGroupingWithSpritesAndFontAssets()
    {
        using var assets = new TestRendererAssets();
        FontAsset fontAsset = BakeFont(assets);
        assets.AddSolidTexture("tex/white", 255, 255, 255);
        var font = new TestFont(fontAsset, "AA", size: 2f);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);

            // One contiguous glyph run for one font.
            Action<FrameSnapshot> glyphsOnly = snapshot => AddScene(snapshot, font);
            PublishUntil(
                renderer,
                producer,
                glyphsOnly,
                stats => stats.DrawCalls == 2 && stats.Glyphs == 2 && stats.SkippedFonts == 0);

            // A sprite whose screen position falls between the two glyphs at the same ZIndex breaks
            // the run: font glyph, sprite, font glyph, then the present draw.
            Vector2 firstTopLeft = QuadWorldTopLeft(font.Layout.Glyphs[0], Matrix3x2.Identity);
            Vector2 secondTopLeft = QuadWorldTopLeft(font.Layout.Glyphs[1], Matrix3x2.Identity);
            float spriteX = (firstTopLeft.X + secondTopLeft.X) * 0.5f;
            Action<FrameSnapshot> interleaved = snapshot =>
            {
                AddScene(snapshot, font);
                AddSprite(snapshot, spriteX, firstTopLeft.Y);
            };
            PublishUntil(
                renderer,
                producer,
                interleaved,
                stats => stats.DrawCalls == 4 && stats.Glyphs == 2);

            // Two distinct font assets never merge into one run even with the same shader entry.
            var first = new TestFont(fontAsset, "A", size: 2f);
            var second = new TestFont(CloneFont(fontAsset), "A", size: 2f);
            Action<FrameSnapshot> twoFonts = snapshot =>
            {
                AddCamera(snapshot);
                AddFont(snapshot, first, Matrix3x2.CreateTranslation(-2f, 0f), zIndex: 0);
                AddFont(snapshot, second, Matrix3x2.CreateTranslation(2f, 0f), zIndex: 1);
            };
            PublishUntil(
                renderer,
                producer,
                twoFonts,
                stats => stats.DrawCalls == 3 && stats.Glyphs == 2 && stats.SkippedFonts == 0);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task PendingAtlasSkipsThenDrawsAndEmptyTextAddsNoRun()
    {
        using var assets = new TestRendererAssets();
        FontAsset fontAsset = BakeFont(assets);
        var font = new TestFont(fontAsset, "A", size: 8f);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot => AddScene(snapshot, font);
            PublishUntil(
                renderer,
                producer,
                fill,
                stats => stats.SkippedFonts == 1 && stats.Glyphs == 0);
            Assert.Equal(1, renderer.RenderStats.DrawCalls);

            PublishUntil(
                renderer,
                producer,
                fill,
                stats => stats.DrawCalls == 2 && stats.Glyphs == 1 && stats.SkippedFonts == 0);

            // Empty text is a normal no-draw: no run, no glyph, and no skip.
            var empty = new TestFont(fontAsset, string.Empty, size: 8f);
            Action<FrameSnapshot> emptyFill = snapshot => AddScene(snapshot, empty);
            PublishUntil(
                renderer,
                producer,
                emptyFill,
                stats => stats.DrawCalls == 1 && stats.Glyphs == 0);
            Assert.Equal(0, renderer.RenderStats.SkippedFonts);
            Assert.Equal(0, renderer.RenderStats.NonEmptyCameras);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task FailedAtlasPageDrawsPlaceholderAndIsNotSkipped()
    {
        using var assets = new TestRendererAssets();
        FontAsset fontAsset = BakeFont(assets);
        assets.AddFailedDescriptor("fonts/failed-atlas", 128, 128);
        FontAsset failed = CloneFont(fontAsset);
        failed.Atlas = "fonts/failed-atlas";
        var font = new TestFont(failed, "A", size: 8f);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot => AddScene(snapshot, font);
            PublishUntil(
                renderer,
                producer,
                fill,
                stats => stats.DrawCalls == 2 && stats.Glyphs == 1 && stats.SkippedFonts == 0);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    private static FontAsset BakeFont(TestRendererAssets assets)
    {
        byte[] source = File.ReadAllBytes(Path.Combine(
            RepositoryPaths.Root,
            FontPath.Replace('/', Path.DirectorySeparatorChar)));
        var diagnostics = new FontDiagnostics();
        FontBakeResult? result = FontBakery.Bake(
            source,
            new FontBakeOptions(Resolution: 128, Range: 8, Padding: 2),
            ranges: [],
            glyphs: ["U+0041", "U+0042", "U+0030", "U+003F"],
            atlasAssetPath: AtlasPath,
            diagnostics);
        if (result is null)
        {
            throw new InvalidOperationException(
                "The test font atlas failed to bake: "
                    + string.Join(
                        Environment.NewLine,
                        diagnostics.Items.Select(item => $"{item.Code}: {item.Message}")));
        }

        assets.AddBakedTexture(
            AtlasPath,
            result.Texture,
            (uint)result.Resolution,
            (uint)result.Resolution,
            TextureFilter.Linear,
            TextureWrap.Clamp);
        return result.Font;
    }

    private static FontAsset CloneFont(FontAsset source) => new()
    {
        Atlas = source.Atlas,
        PixelsPerEm = source.PixelsPerEm,
        Ascent = source.Ascent,
        Descent = source.Descent,
        LineGap = source.LineGap,
        Glyphs = source.Glyphs,
        Kerning = source.Kerning,
        DistanceRange = source.DistanceRange,
    };

    private static void AddScene(
        FrameSnapshot snapshot,
        TestFont font,
        Matrix3x2? world = null,
        ShaderRenderData? shader = null,
        int zIndex = 0)
    {
        AddCamera(snapshot);
        AddFont(snapshot, font, world ?? Matrix3x2.Identity, zIndex, shader);
    }

    private static void AddCamera(FrameSnapshot snapshot)
        => snapshot.AddCamera(
            new CameraSnapshot(
                ZIndex: 0,
                WorldMatrix: Matrix3x2.Identity,
                Size: 10f,
                Viewport: new Vector4(0f, 0f, 1f, 1f),
                CullingMask: 1u,
                Clear: ClearKind.Color,
                ClearColor: _clearColor));

    private static void AddFont(
        FrameSnapshot snapshot,
        TestFont font,
        Matrix3x2 world,
        int zIndex = 0,
        ShaderRenderData? shader = null)
        => snapshot.AddFont(
            new FontSnapshot(
                font.Data,
                font.Layout,
                world,
                Color.White,
                Layer: 1u,
                ZIndex: zIndex)
            {
                Bounds = FontBounds.CreateWorldBounds(font.Layout, world),
                ShaderData = shader,
            });

    private static void AddSprite(FrameSnapshot snapshot, float x, float y)
    {
        var asset = new SpriteAsset
        {
            Texture = "tex/white",
            Uvs = _fullUvs,
            Scale = Vector2.One,
            Pivot = new Vector2(0.5f, 0.5f),
            Topology = SpriteTopology.Quad,
        };
        snapshot.AddSprite(
            new SpriteSnapshot(
                SpriteRenderData.Freeze(asset),
                Matrix3x2.CreateTranslation(x, y),
                Color.White,
                Layer: 1u,
                ZIndex: 0,
                FlipX: false,
                FlipY: false)
            {
                Bounds = Bounds.Empty,
            });
    }

    private static ShaderAsset MakeShader(string spirVPath, uint layoutVersion = ShaderLayout.CurrentVersion)
        => new()
        {
            SpirV = spirVPath,
            LayoutVersion = layoutVersion,
            EntryPoint = "main",
            InstanceUniforms = [],
            GlobalUniforms = [],
            TextureSlots = [],
        };

    private static byte[] Compile(string source)
    {
        string prologue = ShaderPrologue.Build([], []);
        if (!GlslCompiler.TryCompileFragment(
            ShaderPrologue.Compose(prologue, source),
            "font-shader-test.frag",
            out byte[] spirV,
            out string message))
        {
            throw new InvalidOperationException($"The test shader failed to compile: {message}");
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
            throw new TimeoutException("The font offscreen renderer did not become ready.");
        }

        if (renderer.FatalException is { } fatal)
        {
            if (IsUnavailable(fatal))
            {
                throw SkipException.ForSkip(
                    $"No suitable Vulkan 1.3 offscreen device is available: {fatal.Message}");
            }

            throw new InvalidOperationException("The font renderer faulted during startup.", fatal);
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
                    "The frame did not reach the expected state: " + Describe(renderer.RenderStats));
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
            + $"glyphs={stats.Glyphs}, pages={stats.TexturePages}/"
            + $"{stats.TexturePageCapacity}, skippedFonts={stats.SkippedFonts}, "
            + $"validation={stats.ValidationErrors}/{stats.ValidationWarnings}";

    private static void AssertPixelNear(
        FrameCaptureResult result,
        int x,
        int y,
        byte r,
        byte g,
        byte b,
        byte a,
        int tolerance,
        string because)
    {
        int offset = ((y * result.Width) + x) * 4;
        ReadOnlySpan<byte> pixels = result.Pixels.Span;
        byte actualR = pixels[offset];
        byte actualG = pixels[offset + 1];
        byte actualB = pixels[offset + 2];
        byte actualA = pixels[offset + 3];
        Assert.True(
            Math.Abs(actualR - r) <= tolerance
                && Math.Abs(actualG - g) <= tolerance
                && Math.Abs(actualB - b) <= tolerance
                && Math.Abs(actualA - a) <= tolerance,
            $"{because}: pixel ({x}, {y}) is ({actualR}, {actualG}, {actualB}, {actualA}), "
                + $"expected ({r}, {g}, {b}, {a}) +/- {tolerance}.");
    }

    private static int CountNonBlack(FrameCaptureResult result, in ScreenRect rect)
    {
        int minX = Math.Clamp((int)MathF.Ceiling(rect.MinX), 0, result.Width - 1);
        int maxX = Math.Clamp((int)MathF.Floor(rect.MaxX), 0, result.Width - 1);
        int minY = Math.Clamp((int)MathF.Ceiling(rect.MinY), 0, result.Height - 1);
        int maxY = Math.Clamp((int)MathF.Floor(rect.MaxY), 0, result.Height - 1);
        ReadOnlySpan<byte> pixels = result.Pixels.Span;
        var count = 0;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                int offset = ((y * result.Width) + x) * 4;
                if (pixels[offset] > 8 || pixels[offset + 1] > 8 || pixels[offset + 2] > 8)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static Vector2 QuadWorldTopLeft(in FontGlyphQuad quad, in Matrix3x2 world)
        => Vector2.Transform(new Vector2(quad.Min.X, quad.Min.Y + quad.Size.Y), world);

    private static ScreenRect ScreenRectOf(in FontGlyphQuad quad, in Matrix3x2 world)
    {
        Vector2 min = quad.Min;
        Vector2 max = quad.Min + quad.Size;
        Vector2 p0 = ScreenPoint(Vector2.Transform(new Vector2(min.X, max.Y), world));
        Vector2 p1 = ScreenPoint(Vector2.Transform(new Vector2(max.X, max.Y), world));
        Vector2 p2 = ScreenPoint(Vector2.Transform(new Vector2(min.X, min.Y), world));
        Vector2 p3 = ScreenPoint(Vector2.Transform(new Vector2(max.X, min.Y), world));
        return new ScreenRect(
            MathF.Min(MathF.Min(p0.X, p1.X), MathF.Min(p2.X, p3.X)),
            MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y)),
            MathF.Max(MathF.Max(p0.X, p1.X), MathF.Max(p2.X, p3.X)),
            MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y)));
    }

    private static Vector2 ScreenPoint(Vector2 world)
        => new(
            ((world.X + 5f) / 10f) * FramebufferSize,
            ((5f - world.Y) / 10f) * FramebufferSize);

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

    private readonly record struct ScreenRect(float MinX, float MinY, float MaxX, float MaxY);

    private sealed class TestFont
    {
        internal TestFont(FontAsset asset, string text, float size)
        {
            Data = FontRenderData.Freeze(asset);
            Layout = FontLayoutBuilder.Build(
                Data.Glyphs,
                Data.Kerning,
                Data.Ascent,
                Data.Descent,
                Data.LineGap,
                text,
                size,
                TextAlignment.Left,
                lineSpacing: 1f,
                missingGlyph: null);
        }

        internal FontRenderData Data { get; }

        internal FontLayoutData Layout { get; }
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
