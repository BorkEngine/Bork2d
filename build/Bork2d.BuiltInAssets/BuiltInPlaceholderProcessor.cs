using System.Globalization;
using System.Text;
using Bork2d.Bake;
using Bork2d.Bake.Diagnostics;
using Bork2d.Engine.Assets;
using PolyType;

namespace Bork2d.BuiltInAssets;

/// <summary>
/// The tool-only processor that bakes the built-in bindless placeholder texture into the embedded
/// pack.
/// </summary>
/// <remarks>
/// <para>
/// A <c>.checker</c> source is a small UTF-8 text file containing one integer resolution in the
/// accepted range. The processor generates a magenta/opaque-black checkerboard at that resolution and
/// calls <see cref="TextureBaker.Bake"/> with the uncompressed RGBA channel type and the full mip
/// chain, exactly like a generated game texture. It emits the <c>TextureDescriptor</c> at the source's
/// base path and its KTX2 payload at <c>&lt;base&gt;-ktx2</c>.
/// </para>
/// <para>
/// The descriptor uses <see cref="TextureFormat.Rgba8"/>, <see cref="TextureFilter.Nearest"/>, and
/// <see cref="TextureWrap.Clamp"/>. The runtime loads it synchronously at renderer startup and
/// validates it as ordinary KTX2 content; there is no generated-pixel placeholder path.
/// </para>
/// </remarks>
internal sealed class BuiltInPlaceholderProcessor : IAssetProcessor
{
    /// <summary>The smallest accepted placeholder resolution.</summary>
    internal const int MinResolution = 1;

    /// <summary>The largest accepted placeholder resolution.</summary>
    internal const int MaxResolution = 256;

    private const string PayloadSuffix = "-ktx2";

    private static readonly string[] _extensions = ["checker"];

    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <inheritdoc/>
    public string Name => "builtin-placeholder";

    /// <inheritdoc/>
    public IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    public SidecarPolicy Policy => SidecarPolicy.None;

    /// <inheritdoc/>
    public ITypeShape? SidecarShape => null;

    /// <inheritdoc/>
    public void Process(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryReadResolution(context, out int resolution))
        {
            return;
        }

        byte[] pixels = CreateCheckerboard(resolution);
        BakedTexture baked;
        try
        {
            baked = TextureBaker.Bake(
                pixels,
                resolution,
                resolution,
                TextureChannelType.Rgba,
                TextureCompression.None);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ProcessorSourceInvalid,
                $"Failed to bake the built-in placeholder at '{context.RelativePath}': "
                    + exception.Message);
            return;
        }

        var descriptor = new TextureDescriptor
        {
            Payload = context.BaseAssetPath + PayloadSuffix,
            Width = (uint)resolution,
            Height = (uint)resolution,
            Format = baked.Format,
            MipLevels = baked.MipLevels,
            Filter = TextureFilter.Nearest,
            Wrap = TextureWrap.Clamp,
        };
        context.Outputs.AddAsset(context.BaseAssetPath, descriptor);
        context.Outputs.AddData(context.BaseAssetPath + PayloadSuffix, baked.Payload.Span);
    }

    /// <summary>
    /// Creates the placeholder's magenta/opaque-black checkerboard pixels, row-major with a top-left
    /// origin. Cell <c>(x, y)</c> is magenta when <c>(x + y)</c> is even and opaque black otherwise.
    /// </summary>
    /// <param name="resolution">The square resolution in pixels; positive.</param>
    /// <returns>The straight-alpha RGBA8 pixels, exactly <c>resolution * resolution * 4</c> bytes.</returns>
    internal static byte[] CreateCheckerboard(int resolution)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resolution);

        var pixels = new byte[checked(resolution * resolution * 4)];
        for (var y = 0; y < resolution; y++)
        {
            for (var x = 0; x < resolution; x++)
            {
                bool magenta = ((x + y) & 1) == 0;
                int offset = ((y * resolution) + x) * 4;
                pixels[offset] = magenta ? (byte)255 : (byte)0;
                pixels[offset + 1] = 0;
                pixels[offset + 2] = magenta ? (byte)255 : (byte)0;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }

    private static bool TryReadResolution(AssetProcessorContext context, out int resolution)
    {
        resolution = 0;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(context.SourcePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ProcessorSourceRead,
                $"Failed to read the built-in placeholder source '{context.RelativePath}': "
                    + exception.Message);
            return false;
        }

        ReadOnlySpan<byte> content = bytes;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            content = content[3..];
        }

        string text;
        try
        {
            text = _strictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ProcessorSourceRead,
                $"The built-in placeholder source '{context.RelativePath}' is not valid UTF-8.");
            return false;
        }

        if (!int.TryParse(
                text.AsSpan().Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out resolution)
            || resolution < MinResolution
            || resolution > MaxResolution)
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ProcessorSourceInvalid,
                $"The built-in placeholder source '{context.RelativePath}' must contain one integer "
                    + $"resolution in [{MinResolution}, {MaxResolution}]; received '{text.Trim()}'.");
            return false;
        }

        return true;
    }
}
