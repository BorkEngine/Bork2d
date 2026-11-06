using System.Diagnostics;
using System.Numerics;
using Bork2d.Bake;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Fonts;
using Bork2d.Bake.Incremental;
using Bork2d.Bake.Processors;
using Bork2d.Bake.Textures;
using Bork2d.Engine.Assets;
using Bork2d.Tests.Assets.Service;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Fonts;

/// <summary>
/// The built-in font processor through the real builder and pack pipeline: the font, atlas descriptor,
/// and KTX2 payload paths, default-sidecar creation, the digit-and-colon selection, missing glyphs,
/// oversized selections, unreadable sources, check and incremental bakes, and the default registry
/// match.
/// </summary>
public sealed class FontProcessorTests
{
    private const string FontType = "Bork2d.Engine.Assets.FontAsset";

    private const string TextureType = "Bork2d.Engine.Assets.TextureDescriptor";

    private static readonly DateTime _pinnedTime = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EndToEndPackEmitsTheFontAtlasAndPayload()
    {
        using var project = FontTestProject.Create();
        project.CopyScoreFont();
        project.WriteDigitSidecar();

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.AssetCount);
        Assert.Equal(1, result.TextureCount);
        Assert.Equal(2, result.ReferenceCount);
        Assert.Equal(
            new[]
            {
                "fonts/score",
                "fonts/score-atlas",
                "fonts/score-atlas-ktx2",
            },
            result.Entries.Select(entry => entry.Path));

        BakedAssetEntry fontEntry = Assert.Single(
            result.Entries,
            entry => entry.Path == "fonts/score");
        Assert.Equal(BakedAssetEntryKind.ProcessorAsset, fontEntry.Kind);
        Assert.Equal(FontType, fontEntry.TypeName);
        Assert.False(fontEntry.IsRaw);

        BakedAssetEntry atlasEntry = Assert.Single(
            result.Entries,
            entry => entry.Path == "fonts/score-atlas");
        Assert.Equal(BakedAssetEntryKind.ProcessorAsset, atlasEntry.Kind);
        Assert.Equal(TextureType, atlasEntry.TypeName);
        Assert.False(atlasEntry.IsRaw);

        BakedAssetEntry payloadEntry = Assert.Single(
            result.Entries,
            entry => entry.Path == "fonts/score-atlas-ktx2");
        Assert.Equal(BakedAssetEntryKind.ProcessorData, payloadEntry.Kind);
        Assert.Null(payloadEntry.TypeName);
        Assert.True(payloadEntry.IsRaw);
    }

    [Fact]
    public async Task BakedFontCarriesTheDocumentedMetricsAndGlyphs()
    {
        using var project = FontTestProject.Create();
        project.CopyScoreFont();
        project.WriteDigitSidecar();

        BakeResult result = project.Bake();
        Assert.Equal(BakeStatus.Success, result.Status);

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        FontAsset font = await host.Service.LoadAsset<FontAsset>("fonts/score");

        Assert.Equal("fonts/score-atlas", font.Atlas.Path);
        Assert.True(font.PixelsPerEm >= 8);
        Assert.Equal(1.02f, font.Ascent, 3);
        Assert.Equal(-0.3f, font.Descent, 3);
        Assert.Equal(0f, font.LineGap);
        Assert.Equal(8f, font.DistanceRange);

        Assert.Equal(11, font.Glyphs.Length);
        Assert.Equal(
            new[] { 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A },
            font.Glyphs.Select(glyph => glyph.Codepoint));
        Assert.Empty(font.Kerning);
        foreach (FontGlyph glyph in font.Glyphs)
        {
            Assert.Equal(0.6f, glyph.Advance, 3);
            Assert.Equal(glyph.PlaneBounds.X, glyph.Bearing.X);
            Assert.Equal(glyph.PlaneBounds.Y, glyph.Bearing.Y);
            Assert.True(float.IsFinite(glyph.PlaneBounds.X));
            Assert.True(float.IsFinite(glyph.PlaneBounds.Y));
            Assert.True(float.IsFinite(glyph.PlaneBounds.Z));
            Assert.True(float.IsFinite(glyph.PlaneBounds.W));
            Assert.True(glyph.PlaneBounds.Z > glyph.PlaneBounds.X);
            Assert.True(glyph.PlaneBounds.W > glyph.PlaneBounds.Y);
            Assert.True(glyph.AtlasBounds.X >= 0f && glyph.AtlasBounds.X <= 1f);
            Assert.True(glyph.AtlasBounds.Y >= 0f && glyph.AtlasBounds.Y <= 1f);
            Assert.True(glyph.AtlasBounds.Z >= 0f && glyph.AtlasBounds.Z <= 1f);
            Assert.True(glyph.AtlasBounds.W >= 0f && glyph.AtlasBounds.W <= 1f);
            Assert.True(glyph.AtlasBounds.Z > glyph.AtlasBounds.X);
            Assert.True(glyph.AtlasBounds.W > glyph.AtlasBounds.Y);
        }
    }

    [Fact]
    public async Task BakedAtlasIsAFixedSquareRgb8TextureWithFullMips()
    {
        using var project = FontTestProject.Create();
        project.CopyScoreFont();
        project.WriteDigitSidecar(resolution: 128);

        BakeResult result = project.Bake();
        Assert.Equal(BakeStatus.Success, result.Status);

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("fonts/score-atlas");
        Assert.Equal("fonts/score-atlas-ktx2", descriptor.Payload.Path);
        Assert.Equal(128u, descriptor.Width);
        Assert.Equal(128u, descriptor.Height);
        Assert.Equal(TextureFormat.Rgb8, descriptor.Format);
        Assert.Equal((uint)TextureMipChain.LevelCount(128, 128), descriptor.MipLevels);
        Assert.Equal(TextureFilter.Linear, descriptor.Filter);
        Assert.Equal(TextureWrap.Clamp, descriptor.Wrap);

        byte[] payload = await host.Service.LoadData("fonts/score-atlas-ktx2");
        Assert.Equal(Ktx2TestDocument.Identifier, payload[..12]);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(payload);
        Assert.Equal(37u, document.VkFormat);
        Assert.Equal(descriptor.Width, document.Width);
        Assert.Equal(descriptor.Height, document.Height);
        Assert.Equal(descriptor.MipLevels, document.LevelCount);
        for (int level = 0; level < document.LevelCount; level++)
        {
            int width = TextureMipChain.LevelWidth(128, level);
            int height = TextureMipChain.LevelHeight(128, level);
            byte[] levelBytes = document.GetLevelBytes(level);
            Assert.Equal(width * height * 4, levelBytes.Length);
            for (int alpha = 3; alpha < levelBytes.Length; alpha += 4)
            {
                Assert.Equal(255, levelBytes[alpha]);
            }
        }
    }

    [Fact]
    public void MissingSidecarIsCreatedWithTheExplicitDefaults()
    {
        using var project = FontTestProject.Create();
        project.CopyScoreFont();

        var stopwatch = Stopwatch.StartNew();
        BakeResult result = project.Bake();
        stopwatch.Stop();

        Assert.True(result.Status == BakeStatus.Success, Describe(result));
        Assert.True(result.PackWritten);
        BakeDiagnostic info = Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.Equal(BakeSeverity.Info, info.Severity);
        Assert.Equal(FontTestProject.DisplayPath(FontTestProject.SidecarRelativePath), info.FilePath);

        string sidecarPath = project.FullPath(FontTestProject.SidecarRelativePath);
        Assert.True(File.Exists(sidecarPath));
        Assert.Equal(
            "resolution = 2048\nrange = 8\npadding = 2\n"
                + "ranges = [\"U+0020-U+007E\"]\nglyphs = []\n",
            File.ReadAllText(sidecarPath));
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMinutes(5),
            $"The default 2048 ASCII bake took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task DefaultSidecarBakesPrintableAsciiAt2048()
    {
        using var project = FontTestProject.Create();
        project.CopyScoreFont();

        BakeResult result = project.Bake();
        Assert.True(result.Status == BakeStatus.Success, Describe(result));

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        FontAsset font = await host.Service.LoadAsset<FontAsset>("fonts/score");
        Assert.True(font.PixelsPerEm >= 8);
        Assert.Equal(95, font.Glyphs.Length);
        Assert.Equal(0x20, font.Glyphs[0].Codepoint);
        Assert.Equal(0x7E, font.Glyphs[^1].Codepoint);
        Assert.True(font.Glyphs[0].AtlasBounds == Vector4.Zero);

        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("fonts/score-atlas");
        Assert.Equal(2048u, descriptor.Width);
        Assert.Equal(2048u, descriptor.Height);
    }

    [Fact]
    public async Task SelectedCodepointsAreCombinedAndDeduplicated()
    {
        using var project = FontTestProject.Create();
        project.CopyScoreFont();
        project.WriteText(
            FontTestProject.SidecarRelativePath,
            FontTestProject.Sidecar(
                128,
                8,
                2,
                ["U+0030-U+0031", "U+0031-U+0032"],
                ["U+0032", "U+003A"]));

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        FontAsset font = await host.Service.LoadAsset<FontAsset>("fonts/score");
        Assert.Equal(
            new[] { 0x30, 0x31, 0x32, 0x3A },
            font.Glyphs.Select(glyph => glyph.Codepoint));
    }

    [Fact]
    public async Task MissingSelectedCodepointIsBK9303AndTheBakeSucceeds()
    {
        using var project = FontTestProject.Create();
        project.CopyScoreFont();
        project.WriteText(
            FontTestProject.SidecarRelativePath,
            FontTestProject.Sidecar(
                128,
                8,
                2,
                ["U+0030-U+0039"],
                ["U+1F600"]));

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        BakeDiagnostic info = Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == FontDiagnosticCodes.GlyphMissing);
        Assert.Equal(BakeSeverity.Info, info.Severity);
        Assert.Contains("U+1F600", info.Message, StringComparison.Ordinal);

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        FontAsset font = await host.Service.LoadAsset<FontAsset>("fonts/score");
        Assert.Equal(10, font.Glyphs.Length);
        Assert.DoesNotContain(font.Glyphs, glyph => glyph.Codepoint == 0x1F600);
    }

    [Fact]
    public void OversizedSelectionIsBK9305WithNoOutputs()
    {
        using var project = FontTestProject.Create();
        project.CopyScoreFont();
        project.WriteText(
            FontTestProject.SidecarRelativePath,
            FontTestProject.Sidecar(8, 1, 0, ["U+0020-U+007E"], []));

        BakeResult result = project.Bake();

        AssertFailedBake(project, result, FontDiagnosticCodes.AtlasDoesNotFit);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == FontDiagnosticCodes.AtlasDoesNotFit);
        Assert.Contains("8x8", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("glyphs", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidSelectionIsBK9302WithNoOutputs()
    {
        using var project = FontTestProject.Create();
        project.CopyScoreFont();
        project.WriteText(
            FontTestProject.SidecarRelativePath,
            FontTestProject.Sidecar(128, 8, 2, ["U+ZZ"], []));

        BakeResult result = project.Bake();

        AssertFailedBake(project, result, FontDiagnosticCodes.SelectionInvalid);
    }

    [Fact]
    public void UnreadableFontIsBK9301WithNoOutputs()
    {
        using var project = FontTestProject.Create();
        project.WriteBytes("fonts/broken.ttf", [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);
        project.WriteText(
            "fonts/broken.ttf.toml",
            FontTestProject.Sidecar(128, 8, 2, ["U+0030-U+0039"], []));

        BakeResult result = project.Bake();

        AssertFailedBake(project, result, FontDiagnosticCodes.SourceUnreadable);
    }

    [Fact]
    public async Task WhitespaceOnlySelectionBakesAnEmptyAtlasAtTheDeterministicEm()
    {
        using var project = FontTestProject.Create();
        project.CopyScoreFont();
        project.WriteText(
            FontTestProject.SidecarRelativePath,
            FontTestProject.Sidecar(64, 8, 2, ["U+0020"], []));

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        FontAsset font = await host.Service.LoadAsset<FontAsset>("fonts/score");
        Assert.Equal(4u, font.PixelsPerEm);
        FontGlyph glyph = Assert.Single(font.Glyphs);
        Assert.Equal(0x20, glyph.Codepoint);
        Assert.True(glyph.Advance > 0f);
        Assert.Equal(Vector4.Zero, glyph.PlaneBounds);
        Assert.Equal(Vector4.Zero, glyph.AtlasBounds);
        Assert.Equal(Vector2.Zero, glyph.Bearing);
    }

    [Fact]
    public void CheckBakeWritesNoPackAndNoSidecar()
    {
        using var project = FontTestProject.Create();
        project.CopyScoreFont();
        project.WriteDigitSidecar(resolution: 128);

        BakeResult result = project.Bake(check: true);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.False(result.PackWritten);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.False(File.Exists(project.PackPath));
        Assert.Equal(
            new[]
            {
                "fonts/score",
                "fonts/score-atlas",
                "fonts/score-atlas-ktx2",
            },
            result.Entries.Select(entry => entry.Path));
    }

    [Fact]
    public void IncrementalBakeIsUpToDateAndRebakesAfterInputChanges()
    {
        using var project = FontTestProject.Create();
        project.CopyScoreFont();
        project.WriteDigitSidecar(resolution: 128);
        string manifestPath = BakeManifestPaths.ManifestPath(project.RootPath);

        BakeResult first = project.BakeIncremental();

        Assert.Equal(BakeStatus.Success, first.Status);
        BakeIncrementalManifest? manifest = BakeManifestStore.TryRead(manifestPath);
        Assert.NotNull(manifest);
        Assert.Contains(
            manifest!.Inputs,
            input => input.Path == FontTestProject.DisplayPath(FontTestProject.FontRelativePath));
        Assert.Contains(
            manifest.Inputs,
            input => input.Path == FontTestProject.DisplayPath(FontTestProject.SidecarRelativePath));
        Assert.Equal(BakeConfigurationHash.Compute(project.Project), manifest.ConfigurationHash);
        Assert.Equal("font-1/stbtruetype-1.26.13/atlas-1", FontToolInfo.ConfigurationMarker);

        byte[] packBytes = File.ReadAllBytes(project.PackPath);
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        File.SetLastWriteTimeUtc(project.PackPath, _pinnedTime);
        File.SetLastWriteTimeUtc(manifestPath, _pinnedTime);

        BakeResult second = project.BakeIncremental();

        Assert.Equal(BakeStatus.UpToDate, second.Status);
        Assert.False(second.PackWritten);
        Assert.Contains(
            second.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.IncrementalSkipped);
        Assert.Equal(packBytes, File.ReadAllBytes(project.PackPath));
        Assert.Equal(manifestBytes, File.ReadAllBytes(manifestPath));
        Assert.Equal(_pinnedTime, File.GetLastWriteTimeUtc(project.PackPath));
        Assert.Equal(_pinnedTime, File.GetLastWriteTimeUtc(manifestPath));

        project.WriteDigitSidecar(resolution: 64, range: 4);
        BakeResult sidecarChange = project.BakeIncremental();
        Assert.Equal(BakeStatus.Success, sidecarChange.Status);
        Assert.True(sidecarChange.PackWritten);

        byte[] fontBytes = File.ReadAllBytes(project.FullPath(FontTestProject.FontRelativePath));
        byte[] changedFont = [.. fontBytes, 0x00, 0x00, 0x00, 0x00];
        project.WriteBytes(FontTestProject.FontRelativePath, changedFont);
        BakeResult fontChange = project.BakeIncremental();
        Assert.Equal(BakeStatus.Success, fontChange.Status);
        Assert.True(fontChange.PackWritten);
    }

    [Fact]
    public void DefaultRegistryMatchesTtfAndOtfCaseInsensitivelyWithARequiredSidecar()
    {
        using var project = FontTestProject.Create();

        Assert.True(
            project.Project.ProcessorRegistry.TryMatch(
                "fonts/Score.TTF",
                out BakeProcessorMatch match));
        Assert.Equal("font", match.Processor.Name);
        Assert.Equal(["ttf", "otf"], match.Processor.Extensions);
        Assert.Equal(SidecarPolicy.Required, match.Processor.Policy);
        Assert.True(project.Project.ProcessorRegistry.TryMatch("fonts/score.otf", out _));
    }

    private static string Describe(BakeResult result)
        => string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(diagnostic => diagnostic.Format()));

    private static void AssertFailedBake(
        FontTestProject project,
        BakeResult result,
        string expectedCode)
    {
        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.False(result.PackWritten);
        Assert.False(File.Exists(project.PackPath));
        Assert.Empty(result.Entries);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == expectedCode
                && diagnostic.Severity == BakeSeverity.Error);
    }
}
