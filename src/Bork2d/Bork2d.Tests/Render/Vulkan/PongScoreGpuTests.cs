using System.Diagnostics;
using System.Numerics;
using Bork2d.Bake.Fonts;
using Bork2d.Bake.Shaders;
using Bork2d.Bake.Svg;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Vulkan;
using Bork2d.Tests.Bake.Svg;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Sdk;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Opt-in real-GPU integration proof for the Pong HUD frame: a court line, the wordmark drawn through
/// the built-in <c>shader/sdf</c>, two default-shader paddles, the pulse ball, and the score, match
/// state, and control prompt drawn with one baked HUD font at the HUD z-index render together in the
/// unified 2D pass. The test asserts the run count, that glyphs stay out of
/// <see cref="RenderStats.Instances"/>, that no font is skipped, and that the captured pixels contain
/// visible score, state, and prompt text.
/// </summary>
/// <remarks>
/// <para>
/// The test bakes the authored Pong wordmark and HUD font at the authored sidecar resolution and
/// compiles the authored court and pulse shaders, so no binary fixture or generated image is
/// committed. It reuses the shared offscreen lifecycle, raw-snapshot producer, and capture helpers
/// from the other GPU suites instead of duplicating a full font suite.
/// </para>
/// <para>
/// The frame expects six draw calls: one sprite/<c>shader/sdf</c> wordmark run at `ZIndex` 1, one
/// court line run at `ZIndex` 5, one default-sprite run for both paddles at `ZIndex` 10, one pulse
/// ball run at `ZIndex` 20, one font/<c>shader/sdf</c> run for the two scores, the state, and the
/// prompt at `ZIndex` 30, and the fullscreen present draw. The four sprites are the only instance
/// records; the score, state, and prompt glyphs are counted separately in
/// <see cref="RenderStats.Glyphs"/>, and all four texts share the font asset, so they stay one
/// contiguous font run.
/// </para>
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class PongScoreGpuTests
{
    private const int FramebufferWidth = 640;
    private const int FramebufferHeight = 360;
    private const float CameraSize = 9f;

    private const int CourtLayer = 1;
    private const int PaddleLayer = 2;
    private const int BallLayer = 4;
    private const int HudLayer = 8;

    private const int WordmarkZIndex = 1;
    private const int CourtZIndex = 5;
    private const int PaddleZIndex = 10;
    private const int BallZIndex = 20;
    private const int HudZIndex = 30;

    private const float CourtHalfWidth = 16f;
    private const float CourtHalfHeight = 9f;
    private const float CourtLineWidth = 0.08f;
    private const float PaddleInset = 1f;
    private const float WordmarkScale = 5.5f;
    private const float ScoreSize = 1.5f;
    private const float ScoreBaselineY = 7.2f;
    private const float ScoreGap = 0.4f;
    private const float StateSize = 1.0f;
    private const float StateBaselineY = 6.0f;
    private const float PromptSize = 0.65f;
    private const float PromptBaselineY = -8.0f;

    private const int AtlasResolution = 384;
    private const string PrintableAsciiRange = "U+0020-U+007E";
    private const string ScoreFontPath = "examples/pong/Pong.Assets/Assets/fonts/score.ttf";
    private const string WordmarkPath = "examples/pong/Pong.Assets/Assets/art/wordmark.svg";
    private const string CourtShaderPath = "examples/pong/Pong.Assets/Assets/shader/court.frag";
    private const string PulseShaderPath = "examples/pong/Pong.Assets/Assets/shader/pulse.frag";
    private const string ScoreAtlasPath = "fonts/score-atlas";

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _joinTimeout = TimeSpan.FromSeconds(20);

    private static readonly Vector2[] _fullUvs =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    private static readonly Color _clearColor = new(0.04f, 0.04f, 0.06f, 1f);
    private static readonly Color _courtColor = new(0.7f, 0.82f, 1f, 1f);
    private static readonly Color _promptColor = new(0.7f, 0.82f, 1f, 0.65f);
    private static readonly Color _wordmarkColor = new(0.65f, 0.78f, 1f, 0.15f);
    private static readonly Color _leftTint = new(1f, 0.1f, 0.1f, 1f);
    private static readonly Color _rightTint = new(0.1f, 0.3f, 1f, 1f);

    [GpuFact]
    public async Task PongHudFrameDrawsEveryKindAndVisibleHudText()
    {
        using var assets = new TestRendererAssets();
        FontAsset font = BakeHudFont(assets);
        SvgSdfBakeResult wordmarkBake = BakeWordmark(assets);
        ShaderRenderData sdf = LoadShader(assets, "shader/sdf");
        ShaderRenderData court = CompileShader(
            assets,
            "shader/court",
            CourtShaderPath,
            [new ShaderUniform("dash", ShaderUniformType.Float, 0, Vector4.Zero)]);
        ShaderRenderData pulse = CompileShader(
            assets,
            "shader/pulse",
            PulseShaderPath,
            [new ShaderUniform("lastHit", ShaderUniformType.Float, 0, Vector4.Zero)]);
        SpriteAsset paddle = MakeSprite("tex/paddle", new Vector2(0.25f, 1.5f));
        SpriteAsset ball = MakeSprite("tex/ball", new Vector2(0.75f, 0.75f));
        SpriteAsset wordmark = MakeSprite("sdf/wordmark", wordmarkBake.SpriteScale * WordmarkScale);
        assets.AddSolidTexture("tex/paddle", 255, 255, 255);
        assets.AddSolidTexture("tex/ball", 255, 255, 255);

        var leftScore = new HudText(
            font,
            "0",
            TextAlignment.Right,
            new Vector2(-ScoreGap, ScoreBaselineY),
            _leftTint,
            ScoreSize);
        var rightScore = new HudText(
            font,
            "0",
            TextAlignment.Left,
            new Vector2(ScoreGap, ScoreBaselineY),
            _rightTint,
            ScoreSize);
        var stateText = new HudText(
            font,
            "Paused",
            TextAlignment.Center,
            new Vector2(0f, StateBaselineY),
            _courtColor,
            StateSize);
        var promptText = new HudText(
            font,
            "Space to resume",
            TextAlignment.Center,
            new Vector2(0f, PromptBaselineY),
            _promptColor,
            PromptSize);
        int expectedGlyphs = leftScore.Layout.Glyphs.Length
                             + rightScore.Layout.Glyphs.Length
                             + stateText.Layout.Glyphs.Length
                             + promptText.Layout.Glyphs.Length;
        Assert.True(
            expectedGlyphs > 2,
            "The frame should draw the state and prompt glyphs, not only the two score glyphs.");

        using ILoggerFactory loggerFactory = CreateLoggerFactory();
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                AddCamera(snapshot);
                AddSprite(
                    snapshot,
                    wordmark,
                    CourtLayer,
                    WordmarkZIndex,
                    color: _wordmarkColor,
                    shader: sdf);
                AddCourtLines(snapshot, court);
                AddSprite(
                    snapshot,
                    paddle,
                    PaddleLayer,
                    PaddleZIndex,
                    x: -CourtHalfWidth + PaddleInset,
                    color: _leftTint);
                AddSprite(
                    snapshot,
                    paddle,
                    PaddleLayer,
                    PaddleZIndex,
                    x: CourtHalfWidth - PaddleInset,
                    color: _rightTint);
                AddSprite(
                    snapshot,
                    ball,
                    BallLayer,
                    BallZIndex,
                    color: Color.White,
                    shader: pulse,
                    uniform0: pulse.DefaultInstanceUniformBlock[0]);
                AddFont(snapshot, leftScore);
                AddFont(snapshot, rightScore);
                AddFont(snapshot, stateText);
                AddFont(snapshot, promptText);
            };

            PublishUntil(
                renderer,
                producer,
                fill,
                stats => stats.DrawCalls == 6
                    && stats.Instances == 4
                    && stats.Glyphs == expectedGlyphs
                    && stats.Lines == 2
                    && stats.SkippedSprites == 0
                    && stats.SkippedFonts == 0
                    && stats.SkippedLines == 0);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);
            Assert.True(
                CountTintPixels(result, new ScreenRect(285, 6, 316, 42), redDominant: true) > 20,
                "The left score should draw visible red glyph pixels near the top center.");
            Assert.True(
                CountTintPixels(result, new ScreenRect(324, 6, 355, 42), redDominant: false) > 20,
                "The right score should draw visible blue glyph pixels near the top center.");
            Assert.True(
                CountHudTextPixels(result, new ScreenRect(280, 43, 360, 63)) > 40,
                "The paused state should draw visible HUD text below the score.");
            Assert.True(
                CountHudTextPixels(result, new ScreenRect(255, 326, 385, 346)) > 40,
                "The control prompt should draw visible HUD text above the bottom court line.");

            RenderStats stats = renderer.RenderStats;
            Assert.Equal(6, stats.DrawCalls);
            Assert.Equal(4, stats.Instances);
            Assert.Equal(expectedGlyphs, stats.Glyphs);
            Assert.Equal(2, stats.Lines);
            Assert.Equal(1, stats.Cameras);
            Assert.Equal(1, stats.NonEmptyCameras);
            Assert.Equal(0, stats.SkippedSprites);
            Assert.Equal(0, stats.SkippedFonts);
            Assert.Equal(0, stats.SkippedLines);
            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    private static FontAsset BakeHudFont(TestRendererAssets assets)
    {
        byte[] source = File.ReadAllBytes(Path.Combine(
            RepositoryPaths.Root,
            ScoreFontPath.Replace('/', Path.DirectorySeparatorChar)));
        var diagnostics = new FontDiagnostics();
        FontBakeResult? result = FontBakery.Bake(
            source,
            new FontBakeOptions(Resolution: AtlasResolution, Range: 8, Padding: 2),
            ranges: [PrintableAsciiRange],
            glyphs: [],
            atlasAssetPath: ScoreAtlasPath,
            diagnostics);
        if (result is null)
        {
            throw new InvalidOperationException(
                "The Pong HUD font failed to bake: "
                    + string.Join(
                        Environment.NewLine,
                        diagnostics.Items.Select(item => $"{item.Code}: {item.Message}")));
        }

        assets.AddBakedTexture(
            ScoreAtlasPath,
            result.Texture,
            (uint)result.Resolution,
            (uint)result.Resolution,
            TextureFilter.Linear,
            TextureWrap.Clamp);
        return result.Font;
    }

    private static SvgSdfBakeResult BakeWordmark(TestRendererAssets assets)
    {
        string markup = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            WordmarkPath.Replace('/', Path.DirectorySeparatorChar)));
        SvgArtwork artwork = SvgTestSupport.Parse(markup);
        SvgSdfBakeResult result = SvgSdfTestSupport.Bake(
            artwork,
            SvgSdfTestSupport.Options(
                resolution: 128,
                range: 8,
                padding: 2,
                compression: SdfCompression.None));
        assets.AddBakedTexture(
            "sdf/wordmark",
            result.Texture,
            (uint)result.TextureWidth,
            (uint)result.TextureHeight,
            TextureFilter.Linear,
            TextureWrap.Clamp);
        return result;
    }

    private static ShaderRenderData LoadShader(TestRendererAssets assets, string path)
        => ShaderRenderData.Freeze(assets.LoadAsset<ShaderAsset>(path).GetAwaiter().GetResult());

    private static ShaderRenderData CompileShader(
        TestRendererAssets assets,
        string assetPath,
        string sourceRelativePath,
        ShaderUniform[] uniforms)
    {
        string source = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            sourceRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prologue = ShaderPrologue.Build(uniforms, []);
        if (!GlslCompiler.TryCompileFragment(
            ShaderPrologue.Compose(prologue, source),
            Path.GetFileName(sourceRelativePath),
            out byte[] spirV,
            out string message))
        {
            throw new InvalidOperationException(
                $"The Pong shader '{sourceRelativePath}' failed to compile: {message}");
        }

        var asset = new ShaderAsset
        {
            SpirV = assetPath + "-spv",
            LayoutVersion = ShaderLayout.CurrentVersion,
            EntryPoint = "main",
            InstanceUniforms = uniforms,
            GlobalUniforms = [],
            TextureSlots = [],
        };
        assets.AddShader(assetPath, asset, spirV);
        return ShaderRenderData.Freeze(asset);
    }

    private static SpriteAsset MakeSprite(string texture, Vector2 scale) => new()
    {
        Texture = texture,
        Uvs = _fullUvs,
        Scale = scale,
        Pivot = new Vector2(0.5f, 0.5f),
        Topology = SpriteTopology.Quad,
    };

    private static void AddCamera(FrameSnapshot snapshot)
        => snapshot.AddCamera(
            new CameraSnapshot(
                ZIndex: 0,
                WorldMatrix: Matrix3x2.Identity,
                Size: CameraSize,
                Viewport: new Vector4(0f, 0f, 1f, 1f),
                CullingMask: CourtLayer | PaddleLayer | BallLayer | HudLayer,
                Clear: ClearKind.Color,
                ClearColor: _clearColor));

    private static void AddCourtLines(FrameSnapshot snapshot, ShaderRenderData court)
    {
        float inset = CourtLineWidth / 2f;
        AddLine(
            snapshot,
            points:
            [
                new Vector2(-CourtHalfWidth + inset, -CourtHalfHeight + inset),
                new Vector2(CourtHalfWidth - inset, -CourtHalfHeight + inset),
                new Vector2(CourtHalfWidth - inset, CourtHalfHeight - inset),
                new Vector2(-CourtHalfWidth + inset, CourtHalfHeight - inset),
            ],
            closed: true,
            shader: court,
            uniform0: court.DefaultInstanceUniformBlock[0]);
        AddLine(
            snapshot,
            points:
            [
                new Vector2(0f, -CourtHalfHeight + inset),
                new Vector2(0f, CourtHalfHeight - inset),
            ],
            closed: false,
            shader: court,
            uniform0: new Vector4(1f, 0f, 0f, 0f));
    }

    private static void AddLine(
        FrameSnapshot snapshot,
        Vector2[] points,
        bool closed,
        ShaderRenderData shader,
        Vector4 uniform0)
    {
        LineGeometryData geometry = Assert.IsType<LineGeometryData>(
            LineGeometryBuilder.Build(points, closed, CourtLineWidth));
        snapshot.AddLine(
            new LineSnapshot(
                geometry,
                Matrix3x2.Identity,
                _courtColor,
                CourtLayer,
                CourtZIndex,
                AntiAlias: true)
            {
                Bounds = LineBounds.CreateWorldBounds(geometry, Matrix3x2.Identity),
                ShaderData = shader,
                Uniform0 = uniform0,
            });
    }

    private static void AddSprite(
        FrameSnapshot snapshot,
        SpriteAsset asset,
        int layer,
        int zIndex,
        float x = 0f,
        float y = 0f,
        Color? color = null,
        ShaderRenderData? shader = null,
        Vector4? uniform0 = null)
        => snapshot.AddSprite(
            new SpriteSnapshot(
                SpriteRenderData.Freeze(asset),
                Matrix3x2.CreateTranslation(x, y),
                color ?? Color.White,
                (uint)layer,
                zIndex,
                FlipX: false,
                FlipY: false)
            {
                ShaderData = shader,
                Uniform0 = uniform0 ?? Vector4.Zero,
            });

    private static void AddFont(FrameSnapshot snapshot, HudText text)
    {
        Matrix3x2 world = Matrix3x2.CreateTranslation(text.Position);
        snapshot.AddFont(
            new FontSnapshot(
                text.Data,
                text.Layout,
                world,
                text.Color,
                HudLayer,
                HudZIndex)
            {
                Bounds = FontBounds.CreateWorldBounds(text.Layout, world),
            });
    }

    private static int CountTintPixels(
        FrameCaptureResult result,
        in ScreenRect rect,
        bool redDominant)
    {
        int count = 0;
        ReadOnlySpan<byte> pixels = result.Pixels.Span;
        for (var y = rect.MinY; y <= rect.MaxY; y++)
        {
            if (y < 0 || y >= result.Height)
            {
                continue;
            }

            for (var x = rect.MinX; x <= rect.MaxX; x++)
            {
                if (x < 0 || x >= result.Width)
                {
                    continue;
                }

                int offset = ((y * result.Width) + x) * 4;
                byte r = pixels[offset];
                byte g = pixels[offset + 1];
                byte b = pixels[offset + 2];
                bool hit = redDominant
                    ? r > 100 && r > g + 25 && r > b + 25
                    : b > 100 && b > r + 25 && b > g + 10;
                if (hit)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountHudTextPixels(FrameCaptureResult result, in ScreenRect rect)
    {
        // The centered HUD lines share x = 0 with the dashed court center line, so its screen
        // columns are skipped; the count then reflects the bright HUD glyph ink only.
        const int centerLineMinX = 316;
        const int centerLineMaxX = 324;

        int count = 0;
        ReadOnlySpan<byte> pixels = result.Pixels.Span;
        for (var y = rect.MinY; y <= rect.MaxY; y++)
        {
            if (y < 0 || y >= result.Height)
            {
                continue;
            }

            for (var x = rect.MinX; x <= rect.MaxX; x++)
            {
                if (x < 0 || x >= result.Width
                    || (x >= centerLineMinX && x <= centerLineMaxX))
                {
                    continue;
                }

                int offset = ((y * result.Width) + x) * 4;
                byte r = pixels[offset];
                byte g = pixels[offset + 1];
                byte b = pixels[offset + 2];

                // The state uses the court color and the prompt the same color at reduced alpha; both
                // are bright, blue-leaning pixels against the dark clear color and the dim wordmark.
                bool hit = b > 90 && g > 70 && b > r + 15;
                if (hit)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static ILoggerFactory CreateLoggerFactory()
        => LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));

    private static VulkanOffscreenRenderer StartOffscreen(
        TestRendererAssets assets,
        ILoggerFactory loggerFactory)
    {
        var startInfo = new RendererStartInfo
        {
            Mode = RendererMode.Offscreen,
            InstanceHandle = 0,
            Width = FramebufferWidth,
            Height = FramebufferHeight,
            ValidationRequested = true,
            LoggerFactory = loggerFactory,
            Assets = assets,
        };

        var renderer = Assert.IsType<VulkanOffscreenRenderer>(
            new VulkanRendererFactory().Create(startInfo));
        renderer.Start();
        if (!renderer.WaitForReady(_timeout))
        {
            throw new TimeoutException("The Pong score offscreen renderer did not become ready.");
        }

        if (renderer.FatalException is { } fatal)
        {
            if (IsUnavailable(fatal))
            {
                throw SkipException.ForSkip(
                    $"No suitable Vulkan 1.3 offscreen device is available: {fatal.Message}");
            }

            throw new InvalidOperationException("The Pong score renderer faulted during startup.", fatal);
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
            + $"instances={stats.Instances}, glyphs={stats.Glyphs}, lines={stats.Lines}, "
            + $"skipped=({stats.SkippedSprites},{stats.SkippedFonts},{stats.SkippedLines}), "
            + $"validation={stats.ValidationErrors}/{stats.ValidationWarnings}";

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

    private readonly record struct ScreenRect(int MinX, int MinY, int MaxX, int MaxY);

    private sealed class HudText
    {
        internal HudText(
            FontAsset asset,
            string text,
            TextAlignment alignment,
            Vector2 position,
            Color color,
            float size)
        {
            Data = FontRenderData.Freeze(asset);
            Alignment = alignment;
            Position = position;
            Color = color;
            Layout = FontLayoutBuilder.Build(
                Data.Glyphs,
                Data.Kerning,
                Data.Ascent,
                Data.Descent,
                Data.LineGap,
                text,
                size,
                alignment,
                lineSpacing: 1f,
                missingGlyph: null);
        }

        internal FontRenderData Data { get; }

        internal FontLayoutData Layout { get; }

        internal TextAlignment Alignment { get; }

        internal Vector2 Position { get; }

        internal Color Color { get; }
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
