using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// Deterministic schema-3 JSON serialization for the incremental manifest.
/// </summary>
/// <remarks>
/// <para>
/// The writer is explicit: every field is written in a fixed order with a fixed name and type, and
/// the text is indented with LF line endings and UTF-8 without a BOM. Two serializations of the same
/// manifest therefore produce the same bytes without reflection-based serialization.
/// </para>
/// <para>
/// The reader only queries the documented fields. Unknown extra properties are ignored for forward
/// compatibility, while a missing or mistyped field (including a <c>schema</c> other than
/// <see cref="BakeIncrementalManifest.CurrentVersion"/>) makes the manifest unusable: the caller then
/// runs a full bake instead of failing.
/// </para>
/// </remarks>
internal static class BakeManifestJson
{
    private const string SchemaProperty = "schema";
    private const string ToolVersionProperty = "toolVersion";
    private const string ConfigurationHashProperty = "configurationHash";
    private const string ShapeClosureHashProperty = "shapeClosureHash";
    private const string ModeProperty = "mode";
    private const string AssetCountProperty = "assetCount";
    private const string TextureCountProperty = "textureCount";
    private const string ReferenceCountProperty = "referenceCount";
    private const string InputsProperty = "inputs";
    private const string OutputsProperty = "outputs";
    private const string PathProperty = "path";
    private const string HashProperty = "hash";

    private static readonly JsonWriterOptions _writerOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Serializes a manifest to deterministic UTF-8 JSON bytes.</summary>
    /// <param name="manifest">The manifest to serialize.</param>
    /// <returns>The UTF-8 JSON bytes without a BOM.</returns>
    internal static byte[] Serialize(BakeIncrementalManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, _writerOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber(SchemaProperty, BakeIncrementalManifest.CurrentVersion);
            writer.WriteString(ToolVersionProperty, manifest.ToolVersion);
            writer.WriteString(ConfigurationHashProperty, manifest.ConfigurationHash);
            writer.WriteString(ShapeClosureHashProperty, manifest.ShapeClosureHash);
            writer.WriteString(ModeProperty, manifest.Mode);
            writer.WriteNumber(AssetCountProperty, manifest.AssetCount);
            writer.WriteNumber(TextureCountProperty, manifest.TextureCount);
            writer.WriteNumber(ReferenceCountProperty, manifest.ReferenceCount);
            WriteInputs(writer, manifest.Inputs);
            WriteOutputs(writer, manifest.Outputs);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Tries to deserialize schema-3 manifest bytes.</summary>
    /// <param name="bytes">The manifest bytes.</param>
    /// <param name="manifest">The parsed manifest, or <see langword="null"/> when unusable.</param>
    /// <returns><see langword="true"/> when every documented field has the expected type.</returns>
    internal static bool TryDeserialize(
        byte[] bytes,
        out BakeIncrementalManifest? manifest)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        manifest = null;
        using JsonDocument? document = BakeJsonFile.TryParse(bytes);
        if (document is null)
        {
            return false;
        }

        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryReadInt32(root, SchemaProperty, out int schema)
            || schema != BakeIncrementalManifest.CurrentVersion)
        {
            return false;
        }

        if (!TryReadString(root, ToolVersionProperty, out string? toolVersion)
            || !TryReadString(root, ConfigurationHashProperty, out string? configurationHash)
            || !TryReadString(root, ShapeClosureHashProperty, out string? shapeClosureHash)
            || !TryReadString(root, ModeProperty, out string? mode)
            || !TryReadInt32(root, AssetCountProperty, out int assetCount)
            || !TryReadInt32(root, TextureCountProperty, out int textureCount)
            || !TryReadInt32(root, ReferenceCountProperty, out int referenceCount))
        {
            return false;
        }

        if (!TryReadInputs(root, out IReadOnlyList<BakeInputHash>? inputs)
            || !TryReadOutputs(root, out IReadOnlyList<BakeOutputHash>? outputs))
        {
            return false;
        }

        manifest = new BakeIncrementalManifest(
            toolVersion,
            configurationHash,
            shapeClosureHash,
            mode,
            inputs,
            outputs,
            assetCount,
            textureCount,
            referenceCount);

        return true;
    }

    private static void WriteInputs(Utf8JsonWriter writer, IReadOnlyList<BakeInputHash> inputs)
    {
        writer.WriteStartArray(InputsProperty);
        foreach (BakeInputHash input in inputs)
        {
            writer.WriteStartObject();
            writer.WriteString(PathProperty, input.Path);
            writer.WriteString(HashProperty, input.Hash);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteOutputs(Utf8JsonWriter writer, IReadOnlyList<BakeOutputHash> outputs)
    {
        writer.WriteStartArray(OutputsProperty);
        foreach (BakeOutputHash output in outputs)
        {
            writer.WriteStartObject();
            writer.WriteString(PathProperty, output.Path);
            writer.WriteString(HashProperty, output.Hash);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static bool TryReadInputs(
        JsonElement root,
        [NotNullWhen(true)] out IReadOnlyList<BakeInputHash>? inputs)
    {
        inputs = null;
        if (!TryReadArray(root, InputsProperty, out JsonElement array))
        {
            return false;
        }

        var values = new List<BakeInputHash>(array.GetArrayLength());
        foreach (JsonElement element in array.EnumerateArray())
        {
            if (!TryReadPathAndHash(element, out string? path, out string? hash))
            {
                return false;
            }

            values.Add(new BakeInputHash(path, hash));
        }

        inputs = values;
        return true;
    }

    private static bool TryReadOutputs(
        JsonElement root,
        [NotNullWhen(true)] out IReadOnlyList<BakeOutputHash>? outputs)
    {
        outputs = null;
        if (!TryReadArray(root, OutputsProperty, out JsonElement array))
        {
            return false;
        }

        var values = new List<BakeOutputHash>(array.GetArrayLength());
        foreach (JsonElement element in array.EnumerateArray())
        {
            if (!TryReadPathAndHash(element, out string? path, out string? hash))
            {
                return false;
            }

            values.Add(new BakeOutputHash(path, hash));
        }

        outputs = values;
        return true;
    }

    private static bool TryReadPathAndHash(
        JsonElement element,
        [NotNullWhen(true)] out string? path,
        [NotNullWhen(true)] out string? hash)
    {
        path = null;
        hash = null;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryReadString(element, PathProperty, out path)
            || !TryReadString(element, HashProperty, out hash))
        {
            return false;
        }

        return path.Length > 0 && hash.Length > 0;
    }

    private static bool TryReadString(
        JsonElement element,
        string propertyName,
        [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return value is not null;
    }

    private static bool TryReadInt32(
        JsonElement element,
        string propertyName,
        out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value)
            && value >= 0;
    }

    private static bool TryReadArray(
        JsonElement element,
        string propertyName,
        out JsonElement array)
    {
        array = default;
        return element.TryGetProperty(propertyName, out array)
            && array.ValueKind == JsonValueKind.Array;
    }
}
