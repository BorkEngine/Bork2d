using System.Text.Json;
using Bork2d.Bake;
using Bork2d.Engine.Assets;
using PolyType;

namespace Pong.Assets.Processors;

/// <summary>
/// The circle acceptance processor: it parses the processor-owned JSON source, renders a white
/// straight-alpha antialiased circle on a transparent background, and emits a
/// <see cref="TextureDescriptor"/> plus its raw RGBA8 payload.
/// </summary>
/// <remarks>
/// <para>
/// The source is a small JSON object with integer members <c>resolution</c> (the square texture
/// edge in pixels) and <c>size</c> (the circle diameter in pixels). The valid ranges are
/// <c>1 &lt;= resolution &lt;= 4096</c> and <c>1 &lt;= size &lt;= resolution</c>.
/// </para>
/// <para>
/// The JSON is owned by this processor, not by the TOML binder: the processor declares
/// <see cref="SidecarPolicy.None"/> and parses the source itself. An unreadable source or a source
/// that is not valid JSON is <c>BK7001</c>; a valid JSON document with missing, non-integer, or
/// out-of-range values is <c>BK7002</c>. A failed source emits no outputs.
/// </para>
/// <para>
/// Coverage is supersampled on a 4×4 grid per pixel and quantized to straight alpha, so the center
/// is fully opaque, the outside is fully transparent, and the edge is antialiased. The descriptor
/// is emitted at <c>BaseAssetPath</c> and the RGBA8 bytes at <c>BaseAssetPath + "-raw"</c>.
/// </para>
/// </remarks>
internal sealed class CircleProcessor : IAssetProcessor
{
    private const string SourceCode = "BK7001";
    private const string ValueCode = "BK7002";
    private const string RawSuffix = "-raw";
    private const int MaximumResolution = 4096;
    private const int SupersampleFactor = 4;
    private const byte OpaqueWhite = 255;

    private static readonly string[] _extensions = ["circle"];

    /// <inheritdoc/>
    public string Name => "circle";

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

        byte[] source;
        try
        {
            source = File.ReadAllBytes(context.SourcePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            context.Diagnostics.Error(
                SourceCode,
                $"Failed to read the circle source '{context.RelativePath}': {exception.Message}");
            return;
        }

        if (!TryReadValues(source, context, out int resolution, out int size))
        {
            return;
        }

        byte[] payload = RenderCircle(resolution, size);
        string payloadPath = context.BaseAssetPath + RawSuffix;
        var descriptor = new TextureDescriptor
        {
            Payload = payloadPath,
            Width = (uint)resolution,
            Height = (uint)resolution,
            Format = TextureFormat.Rgba8,
            Filter = TextureFilter.Linear,
            Wrap = TextureWrap.Clamp,
        };

        context.Outputs.AddAsset(context.BaseAssetPath, descriptor);
        context.Outputs.AddData(payloadPath, payload);
    }

    private static bool TryReadValues(
        byte[] source,
        AssetProcessorContext context,
        out int resolution,
        out int size)
    {
        resolution = 0;
        size = 0;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(source.AsMemory());
        }
        catch (JsonException exception)
        {
            context.Diagnostics.Error(
                SourceCode,
                $"The circle source '{context.RelativePath}' is not valid JSON: {exception.Message}");
            return false;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryReadInteger(root, "resolution", out resolution)
                || !TryReadInteger(root, "size", out size))
            {
                context.Diagnostics.Error(
                    ValueCode,
                    $"The circle source '{context.RelativePath}' must be a JSON object with integer "
                        + "'resolution' and 'size' members.");
                return false;
            }

            if (resolution < 1 || resolution > MaximumResolution || size < 1 || size > resolution)
            {
                context.Diagnostics.Error(
                    ValueCode,
                    $"The circle source '{context.RelativePath}' is out of range: got "
                        + $"resolution={resolution}, size={size}; require 1 <= resolution <= "
                        + $"{MaximumResolution} and 1 <= size <= resolution.");
                return false;
            }
        }

        return true;
    }

    private static bool TryReadInteger(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    private static byte[] RenderCircle(int resolution, int size)
    {
        var payload = new byte[checked(resolution * resolution * 4)];
        double radius = size / 2.0;
        double center = resolution / 2.0;
        int samplesPerPixel = SupersampleFactor * SupersampleFactor;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int covered = 0;
                for (int sampleY = 0; sampleY < SupersampleFactor; sampleY++)
                {
                    double dy = (y + ((sampleY + 0.5) / SupersampleFactor)) - center;
                    for (int sampleX = 0; sampleX < SupersampleFactor; sampleX++)
                    {
                        double dx = (x + ((sampleX + 0.5) / SupersampleFactor)) - center;
                        if ((dx * dx) + (dy * dy) <= radius * radius)
                        {
                            covered++;
                        }
                    }
                }

                int offset = ((y * resolution) + x) * 4;
                payload[offset] = OpaqueWhite;
                payload[offset + 1] = OpaqueWhite;
                payload[offset + 2] = OpaqueWhite;
                payload[offset + 3] = (byte)((covered * OpaqueWhite) / samplesPerPixel);
            }
        }

        return payload;
    }
}
