using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;
using Microsoft.Extensions.Logging;

namespace Bork2d.Tests.TestSupport;

/// <summary>
/// Synthetic font assets with known metrics, glyphs, and kerning for deterministic layout tests.
/// No bake or GPU is involved.
/// </summary>
internal static class FontTestSupport
{
    internal const string AtlasPath = "fonts/test-atlas";
    internal const uint PixelsPerEm = 32;
    internal const float Ascent = 0.8f;
    internal const float Descent = -0.2f;
    internal const float LineGap = 0.05f;
    internal const float DistanceRange = 8f;
    internal const float AAdvance = 0.6f;
    internal const float BAdvance = 0.65f;
    internal const float VAdvance = 0.62f;
    internal const float QuestionAdvance = 0.5f;
    internal const float SpaceAdvance = 0.3f;
    internal const float KernAV = -0.1f;
    internal const float LineHeight = Ascent - Descent + LineGap;

    internal static readonly Vector4 APlane = new(0.05f, 0f, 0.55f, 0.6f);
    internal static readonly Vector4 AAtlas = new(0f, 0f, 0.25f, 0.5f);
    internal static readonly Vector4 BPlane = new(0.02f, -0.05f, 0.58f, 0.55f);
    internal static readonly Vector4 BAtlas = new(0.25f, 0f, 0.5f, 0.5f);
    internal static readonly Vector4 VPlane = new(0.03f, 0f, 0.6f, 0.6f);
    internal static readonly Vector4 VAtlas = new(0.5f, 0f, 0.75f, 0.5f);
    internal static readonly Vector4 QuestionPlane = new(0.1f, -0.1f, 0.45f, 0.5f);
    internal static readonly Vector4 QuestionAtlas = new(0.75f, 0f, 1f, 0.5f);

    internal static FontAsset CreateFont(
        bool includeQuestionMark = true,
        float aAdvance = AAdvance,
        float ascent = Ascent)
    {
        var glyphs = new List<FontGlyph>
        {
            new(' ', SpaceAdvance, Vector2.Zero, Vector4.Zero, Vector4.Zero),
        };
        if (includeQuestionMark)
        {
            glyphs.Add(Glyph('?', QuestionAdvance, QuestionPlane, QuestionAtlas));
        }

        glyphs.Add(Glyph('A', aAdvance, APlane, AAtlas));
        glyphs.Add(Glyph('B', BAdvance, BPlane, BAtlas));
        glyphs.Add(Glyph('V', VAdvance, VPlane, VAtlas));

        return new FontAsset
        {
            Atlas = AtlasPath,
            PixelsPerEm = PixelsPerEm,
            Ascent = ascent,
            Descent = Descent,
            LineGap = LineGap,
            DistanceRange = DistanceRange,
            Glyphs = [.. glyphs],
            Kerning =
            [
                new FontKerning('A', 'V', KernAV),
            ],
        };
    }

    internal static ShaderAsset CreateFloatShader() => new()
    {
        SpirV = "shader/font-test-spv",
        LayoutVersion = 3,
        InstanceUniforms =
        [
            new ShaderUniform(
                "uAlpha",
                ShaderUniformType.Float,
                0,
                new Vector4(0.75f, 0f, 0f, 0f)),
        ],
    };

    internal static GameEngine<GameLayer> CreateEngine(ILoggerFactory? loggerFactory = null)
        => new(new FakeTime(), new FakeWindow(), new FakeAssets(), loggerFactory: loggerFactory);

    internal static ILoggerFactory CreateLoggerFactory(CapturingLoggerProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(provider);
        });
    }

    internal static FrameSnapshot Capture<TLayer>(GameEngine<TLayer> engine)
        where TLayer : struct, Enum
    {
        var snapshot = new FrameSnapshot();
        engine.CaptureSnapshot(snapshot, sequence: 1);
        return snapshot;
    }

    private static FontGlyph Glyph(
        int codepoint,
        float advance,
        Vector4 plane,
        Vector4 atlas)
        => new(codepoint, advance, new Vector2(plane.X, plane.Y), plane, atlas);
}
