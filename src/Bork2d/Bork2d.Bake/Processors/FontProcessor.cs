using Bork2d.Bake.Fonts;
using Bork2d.Engine.Assets;

namespace Bork2d.Bake.Processors;

/// <summary>
/// The built-in font processor: reads one TTF/OTF source, rasterizes the selected codepoints into a
/// linear MSDF atlas, and emits a <see cref="FontAsset"/>, a <see cref="TextureDescriptor"/>, and the
/// KTX2 payload.
/// </summary>
/// <remarks>
/// <para>
/// A font source under any canonical asset path is baked, so <c>fonts/score.ttf</c> emits the
/// <c>fonts/score</c> font, the <c>fonts/score-atlas</c> texture descriptor, and the raw
/// <c>fonts/score-atlas-ktx2</c> payload. The atlas is exactly <c>resolution × resolution</c>
/// pixels in linear <see cref="TextureFormat.Rgb8"/> with a full mip chain, linear filtering, and
/// clamp wrapping; the font asset references it and carries the derived em size, the font metrics,
/// the sorted glyphs, the kerning pairs, and the distance range.
/// </para>
/// <para>
/// The required sidecar carries the resolution, range, padding, ranges, and glyphs settings; a
/// missing sidecar is created with the five explicit defaults. The processor reports the font
/// <c>BK93xx</c> codes and emits no outputs for a failed source. It is synchronous, runs on the bake
/// thread, and keeps no per-source state.
/// </para>
/// </remarks>
internal sealed class FontProcessor : AssetProcessor<FontSidecar>
{
    private const string AtlasSuffix = "-atlas";

    private const string PayloadSuffix = "-atlas-ktx2";

    private static readonly string[] _extensions = ["ttf", "otf"];

    /// <inheritdoc/>
    public override string Name => "font";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    protected override FontSidecar CreateDefaultSidecar(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new FontSidecar
        {
            Resolution = FontBakeOptions.DefaultResolution,
            Range = FontBakeOptions.DefaultRange,
            Padding = FontBakeOptions.DefaultPadding,
            Ranges = [FontBakeOptions.DefaultRangeSelection],
            Glyphs = [],
        };
    }

    /// <inheritdoc/>
    protected override void Process(AssetProcessorContext context, FontSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sidecar);

        byte[] source;
        try
        {
            source = File.ReadAllBytes(context.SourcePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            context.Diagnostics.Error(
                FontDiagnosticCodes.SourceUnreadable,
                $"Failed to read the font source '{context.RelativePath}': {exception.Message}");
            return;
        }

        var options = new FontBakeOptions(
            sidecar.Resolution ?? FontBakeOptions.DefaultResolution,
            sidecar.Range ?? FontBakeOptions.DefaultRange,
            sidecar.Padding ?? FontBakeOptions.DefaultPadding);
        string[] ranges = sidecar.Ranges ?? [FontBakeOptions.DefaultRangeSelection];
        string[] glyphs = sidecar.Glyphs ?? [];

        var diagnostics = new FontDiagnostics();
        FontBakeResult? result;
        try
        {
            result = FontBakery.Bake(
                source,
                options,
                ranges,
                glyphs,
                context.BaseAssetPath + AtlasSuffix,
                diagnostics);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The baker classifies authored and outline failures itself; an exception it did not
            // classify is a font-content failure, never a generic processor failure or an unreadable
            // source (the source bytes were read successfully before the bake).
            context.Diagnostics.Error(
                FontDiagnosticCodes.FieldInvalid,
                $"The font source '{context.RelativePath}' could not be baked: {exception.Message}");
            return;
        }

        Forward(context, diagnostics);
        if (result is null || diagnostics.HasErrors)
        {
            return;
        }

        string atlasPath = context.BaseAssetPath + AtlasSuffix;
        string payloadPath = context.BaseAssetPath + PayloadSuffix;
        var descriptor = new TextureDescriptor
        {
            Payload = payloadPath,
            Width = (uint)result.Resolution,
            Height = (uint)result.Resolution,
            Format = result.Texture.Format,
            MipLevels = result.Texture.MipLevels,
            Filter = TextureFilter.Linear,
            Wrap = TextureWrap.Clamp,
        };

        context.Outputs.AddAsset(context.BaseAssetPath, result.Font);
        context.Outputs.AddAsset(atlasPath, descriptor);
        context.Outputs.AddData(payloadPath, result.Texture.Payload.Span);
    }

    private static void Forward(AssetProcessorContext context, FontDiagnostics diagnostics)
    {
        foreach (FontDiagnostic diagnostic in diagnostics.Items)
        {
            if (diagnostic.Severity == FontDiagnosticSeverity.Error)
            {
                context.Diagnostics.Error(diagnostic.Code, diagnostic.Message);
            }
            else
            {
                context.Diagnostics.Info(diagnostic.Code, diagnostic.Message);
            }
        }
    }
}
