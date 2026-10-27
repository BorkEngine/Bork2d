using Bork2d.Api.Assets;
using Bork2d.Bake.Builder;

namespace Bork2d.BakeFixture.Assets.Processors;

/// <summary>
/// The PPM acceptance processor: parses a simple binary P6 image, converts RGB to straight-alpha RGBA8,
/// and emits a <see cref="TextureDescriptor"/> plus the raw payload.
/// </summary>
/// <remarks>
/// <para>
/// The source format is deliberately small: the <c>P6</c> magic, header comments (<c>#</c> to the end
/// of the line) and whitespace, positive width and height, a maximum color value of exactly 255,
/// exactly one whitespace byte after that value, and exactly <c>width * height * 3</c> payload bytes
/// with no trailing bytes. No 16-bit samples, ASCII variants, or resampling are supported. Invalid
/// input is <c>BK7002</c>; an unreadable source is <c>BK7001</c>; a failed source produces no outputs.
/// </para>
/// <para>
/// The descriptor is added as a typed instance through <see cref="IAssetOutputSink.AddAsset{T}"/>, so
/// the engine serializes it and the processor never touches MessagePack. A present sidecar key
/// overrides the configured default, an absent key keeps it, and a missing sidecar is created with
/// both configured values.
/// </para>
/// </remarks>
internal sealed class PpmProcessor : AssetProcessor<PpmSidecar>
{
    private const string RawSuffix = "-raw";
    private const string FormatRgba8 = "rgba8";
    private const int MaximumColorValue = 255;
    private const byte AlphaOpaque = 255;

    private static readonly string[] _extensions = ["ppm"];

    private readonly TextureFilter _filter;
    private readonly TextureWrap _wrap;

    /// <summary>
    /// Initializes a new instance of the <see cref="PpmProcessor"/> class.
    /// </summary>
    /// <param name="filter">The sampler filter used by a default sidecar or an absent <c>filter</c> key.</param>
    /// <param name="wrap">The sampler wrap mode used by a default sidecar or an absent <c>wrap</c> key.</param>
    internal PpmProcessor(TextureFilter filter, TextureWrap wrap)
    {
        _filter = filter;
        _wrap = wrap;
    }

    /// <inheritdoc/>
    public override string Name => "ppm";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    protected override PpmSidecar CreateDefaultSidecar(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new PpmSidecar
        {
            Filter = _filter,
            Wrap = _wrap,
        };
    }

    /// <inheritdoc/>
    protected override void Process(AssetProcessorContext context, PpmSidecar sidecar)
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
                ProcessorDiagnosticCodes.SourceRead,
                $"Failed to read the PPM source '{context.RelativePath}': {exception.Message}");
            return;
        }

        if (!TryParseP6(source, out int width, out int height, out byte[] payload, out string? reason))
        {
            context.Diagnostics.Error(
                ProcessorDiagnosticCodes.InputInvalid,
                $"The PPM source '{context.RelativePath}' is not a supported binary P6 image: {reason}");
            return;
        }

        TextureFilter filter = sidecar.Filter ?? _filter;
        TextureWrap wrap = sidecar.Wrap ?? _wrap;
        string payloadPath = context.BaseAssetPath + RawSuffix;
        var descriptor = new TextureDescriptor
        {
            Payload = payloadPath,
            Width = (uint)width,
            Height = (uint)height,
            Format = FormatRgba8,
            Filter = filter == TextureFilter.Nearest ? "nearest" : "linear",
            Wrap = wrap == TextureWrap.Repeat ? "repeat" : "clamp",
        };

        context.Outputs.AddAsset(context.BaseAssetPath, descriptor);
        context.Outputs.AddData(payloadPath, payload);
    }

    private static bool TryParseP6(
        byte[] source,
        out int width,
        out int height,
        out byte[] payload,
        out string? reason)
    {
        width = 0;
        height = 0;
        payload = [];
        reason = null;

        if (source.Length < 2 || source[0] != (byte)'P' || source[1] != (byte)'6')
        {
            reason = "the magic number must be 'P6'.";
            return false;
        }

        int index = 2;
        if (!TrySkipSeparators(source, ref index))
        {
            reason = "the magic number must be followed by whitespace.";
            return false;
        }

        if (!TryReadPositiveInt(source, ref index, out width))
        {
            reason = "the width must be a positive integer.";
            return false;
        }

        if (!TrySkipSeparators(source, ref index))
        {
            reason = "the width must be followed by whitespace.";
            return false;
        }

        if (!TryReadPositiveInt(source, ref index, out height))
        {
            reason = "the height must be a positive integer.";
            return false;
        }

        if (!TrySkipSeparators(source, ref index))
        {
            reason = "the height must be followed by whitespace.";
            return false;
        }

        if (!TryReadPositiveInt(source, ref index, out int maximum) || maximum != MaximumColorValue)
        {
            reason = "the maximum color value must be exactly 255.";
            return false;
        }

        if (index >= source.Length || !IsWhitespace(source[index]))
        {
            reason = "the maximum color value must be followed by exactly one whitespace byte.";
            return false;
        }

        index++;
        long expectedPayloadLength = (long)width * height * 3;
        long actualPayloadLength = source.Length - index;
        if (actualPayloadLength != expectedPayloadLength)
        {
            reason = $"the payload must contain exactly {expectedPayloadLength} RGB bytes for a "
                + $"{width}x{height} image, but {actualPayloadLength} bytes remain.";
            return false;
        }

        int pixelCount = width * height;
        payload = new byte[pixelCount * 4];
        for (int pixel = 0, sourceIndex = index, targetIndex = 0;
            pixel < pixelCount;
            pixel++, sourceIndex += 3, targetIndex += 4)
        {
            payload[targetIndex] = source[sourceIndex];
            payload[targetIndex + 1] = source[sourceIndex + 1];
            payload[targetIndex + 2] = source[sourceIndex + 2];
            payload[targetIndex + 3] = AlphaOpaque;
        }

        return true;
    }

    private static bool TryReadPositiveInt(byte[] source, ref int index, out int value)
    {
        value = 0;
        int start = index;
        while (index < source.Length && source[index] is >= (byte)'0' and <= (byte)'9')
        {
            int digit = source[index] - '0';
            if (value > (int.MaxValue - digit) / 10)
            {
                return false;
            }

            value = (value * 10) + digit;
            index++;
        }

        return index > start && value > 0;
    }

    private static bool TrySkipSeparators(byte[] source, ref int index)
    {
        bool skipped = false;
        while (index < source.Length)
        {
            byte current = source[index];
            if (IsWhitespace(current))
            {
                index++;
                skipped = true;
                continue;
            }

            if (current == (byte)'#')
            {
                skipped = true;
                while (index < source.Length && source[index] != (byte)'\n')
                {
                    index++;
                }

                if (index < source.Length)
                {
                    index++;
                }

                continue;
            }

            break;
        }

        return skipped;
    }

    private static bool IsWhitespace(byte value)
        => value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\v' or (byte)'\f';
}
