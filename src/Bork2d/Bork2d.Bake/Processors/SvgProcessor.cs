using System.Numerics;
using System.Text;
using Bork2d.Bake.Svg;
using Bork2d.Engine.Assets;

namespace Bork2d.Bake.Processors;

/// <summary>
/// The built-in SVG processor: parses one shape-only SVG document, bakes its geometry into a linear
/// multi-channel signed distance field texture, and emits a <see cref="SpriteAsset"/> plus a
/// <see cref="TextureDescriptor"/> and its KTX2 payload.
/// </summary>
/// <remarks>
/// <para>
/// An SVG source under any canonical asset path is baked, so <c>art/wordmark.svg</c> emits the
/// <c>art/wordmark</c> sprite, the <c>art/wordmark-sdf</c> texture descriptor, and the raw
/// <c>art/wordmark-sdf-ktx2</c> payload. The sprite references the descriptor with full UVs, a center
/// pivot, and the bake result's scale, which makes the visible artboard exactly one world unit high
/// while the field margin extends outside it. The sprite is an ordinary asset: the processor does not
/// bake a shader choice into it, so a game can assign an SDF fragment shader to the sprite or leave
/// the default sprite shader in place.
/// </para>
/// <para>
/// The source is read as strict UTF-8 and an optional byte-order mark is stripped. Parse and bake
/// failures are reported through the SVG <c>BK92xx</c> codes, with their source location appended to
/// the message, and emit no outputs. The required sidecar carries the resolution, range, padding, and
/// compression settings; a missing sidecar is created with the explicit defaults. The processor is
/// synchronous, runs on the bake thread, and keeps no per-source state.
/// </para>
/// </remarks>
internal sealed class SvgProcessor : AssetProcessor<SvgSidecar>
{
    private const string TextureSuffix = "-sdf";

    private const string PayloadSuffix = "-sdf-ktx2";

    private static readonly string[] _extensions = ["svg"];

    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <inheritdoc/>
    public override string Name => "svg";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    protected override SvgSidecar CreateDefaultSidecar(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new SvgSidecar
        {
            Resolution = SvgSdfBakeOptions.DefaultResolution,
            Range = SvgSdfBakeOptions.DefaultRange,
            Padding = SvgSdfBakeOptions.DefaultPadding,
            Compression = SdfCompression.None,
        };
    }

    /// <inheritdoc/>
    protected override void Process(AssetProcessorContext context, SvgSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sidecar);

        if (!TryReadSource(context, out string source))
        {
            return;
        }

        var parseDiagnostics = new SvgDiagnostics();
        SvgArtwork? artwork = SvgParser.TryParse(
            source,
            context.RelativePath,
            parseDiagnostics);
        Forward(context, parseDiagnostics);
        if (artwork is null || parseDiagnostics.HasErrors)
        {
            return;
        }

        var options = new SvgSdfBakeOptions(
            sidecar.Resolution ?? SvgSdfBakeOptions.DefaultResolution,
            sidecar.Range ?? SvgSdfBakeOptions.DefaultRange,
            sidecar.Padding ?? SvgSdfBakeOptions.DefaultPadding,
            sidecar.Compression ?? SdfCompression.None);

        var bakeDiagnostics = new SvgDiagnostics();
        SvgSdfBakeResult? result;
        try
        {
            // The baker validates authored values and reports diagnostics, but a content or encoder
            // failure it did not classify must stay an SVG diagnostic instead of a generic processor
            // failure, so it is caught here and reported as BK9206.
            result = SvgSdfBaker.Bake(artwork, options, bakeDiagnostics);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            context.Diagnostics.Error(
                SvgDiagnosticCodes.SdfBakeFailed,
                $"The SVG source '{context.RelativePath}' could not be baked: {exception.Message}");
            return;
        }

        Forward(context, bakeDiagnostics);
        if (result is null || bakeDiagnostics.HasErrors)
        {
            return;
        }

        string texturePath = context.BaseAssetPath + TextureSuffix;
        string payloadPath = context.BaseAssetPath + PayloadSuffix;
        var sprite = new SpriteAsset
        {
            Texture = texturePath,
            Uvs =
            [
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            ],
            Scale = result.SpriteScale,
            Pivot = new Vector2(0.5f, 0.5f),
            Topology = SpriteTopology.Quad,
            Indices = null,
        };

        var descriptor = new TextureDescriptor
        {
            Payload = payloadPath,
            Width = (uint)result.TextureWidth,
            Height = (uint)result.TextureHeight,
            Format = result.Texture.Format,
            MipLevels = result.Texture.MipLevels,
            Filter = TextureFilter.Linear,
            Wrap = TextureWrap.Clamp,
        };

        context.Outputs.AddAsset(context.BaseAssetPath, sprite);
        context.Outputs.AddAsset(texturePath, descriptor);
        context.Outputs.AddData(payloadPath, result.Texture.Payload.Span);
    }

    private static bool TryReadSource(AssetProcessorContext context, out string source)
    {
        source = string.Empty;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(context.SourcePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            context.Diagnostics.Error(
                SvgDiagnosticCodes.SourceXmlInvalid,
                $"Failed to read the SVG source '{context.RelativePath}': {exception.Message}");
            return false;
        }

        ReadOnlySpan<byte> content = bytes;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            content = content[3..];
        }

        try
        {
            source = _strictUtf8.GetString(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            context.Diagnostics.Error(
                SvgDiagnosticCodes.SourceXmlInvalid,
                $"The SVG source '{context.RelativePath}' is not valid UTF-8.");
            return false;
        }
    }

    private static void Forward(AssetProcessorContext context, SvgDiagnostics diagnostics)
    {
        foreach (SvgDiagnostic diagnostic in diagnostics.Items)
        {
            string message = FormatMessage(diagnostic);
            if (diagnostic.Severity == SvgDiagnosticSeverity.Error)
            {
                context.Diagnostics.Error(diagnostic.Code, message);
            }
            else
            {
                context.Diagnostics.Info(diagnostic.Code, message);
            }
        }
    }

    private static string FormatMessage(SvgDiagnostic diagnostic)
    {
        if (diagnostic.Line is int line && diagnostic.Column is int column)
        {
            return diagnostic.Message + $" (line {line}, column {column})";
        }

        if (diagnostic.Line is int lineOnly)
        {
            return diagnostic.Message + $" (line {lineOnly})";
        }

        if (diagnostic.Column is int columnOnly)
        {
            return diagnostic.Message + $" (column {columnOnly})";
        }

        return diagnostic.Message;
    }
}
