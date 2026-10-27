using System.Text.Json;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// Serializes and parses <see cref="BakeIncrementalManifest"/> (the internal bake manifest JSON).
/// </summary>
/// <remarks>
/// The writer is explicit (no reflection) and fixes the property order, indentation, and LF newlines,
/// so a manifest is byte-stable for the same model. The reader returns <see langword="null"/> for any
/// structural mismatch; the caller treats that exactly like a missing manifest (a full bake).
/// Version 2 records every pack output, so a version-1 manifest is a structural mismatch.
/// </remarks>
internal static class BakeManifestJson
{
    private const string VersionProperty = "version";
    private const string ToolVersionProperty = "toolVersion";
    private const string DescriptorHashProperty = "descriptorHash";
    private const string CommonAssemblyHashProperty = "commonAssemblyHash";
    private const string HostHashProperty = "hostHash";
    private const string ModeProperty = "mode";
    private const string OutputsProperty = "outputs";
    private const string AssetCountProperty = "assetCount";
    private const string TextureCountProperty = "textureCount";
    private const string ReferenceCountProperty = "referenceCount";
    private const string InputsProperty = "inputs";
    private const string PathProperty = "path";
    private const string HashProperty = "hash";

    /// <summary>Serializes a manifest to UTF-8 JSON bytes.</summary>
    /// <param name="manifest">The manifest.</param>
    /// <returns>The JSON bytes with a trailing LF.</returns>
    internal static byte[] Serialize(BakeIncrementalManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions
            {
                Indented = true,
                NewLine = "\n",
            }))
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionProperty, manifest.Version);
            writer.WriteString(ToolVersionProperty, manifest.ToolVersion);
            writer.WriteString(DescriptorHashProperty, manifest.DescriptorHash);
            writer.WriteString(CommonAssemblyHashProperty, manifest.CommonAssemblyHash);
            writer.WriteString(HostHashProperty, manifest.HostHash);
            writer.WriteString(ModeProperty, manifest.Mode);
            writer.WriteNumber(AssetCountProperty, manifest.AssetCount);
            writer.WriteNumber(TextureCountProperty, manifest.TextureCount);
            writer.WriteNumber(ReferenceCountProperty, manifest.ReferenceCount);

            writer.WriteStartArray(OutputsProperty);
            foreach (BakeOutputHash output in manifest.Outputs)
            {
                writer.WriteStartObject();
                writer.WriteString(PathProperty, output.Path);
                writer.WriteString(HashProperty, output.Hash);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray(InputsProperty);
            foreach (BakeInputHash input in manifest.Inputs)
            {
                writer.WriteStartObject();
                writer.WriteString(PathProperty, input.Path);
                writer.WriteString(HashProperty, input.Hash);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>Parses a manifest from UTF-8 JSON bytes.</summary>
    /// <param name="bytes">The JSON bytes.</param>
    /// <returns>The manifest, or <see langword="null"/> when the file is not a valid manifest.</returns>
    internal static BakeIncrementalManifest? TryDeserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        using JsonDocument? document = BakeJsonFile.TryParse(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        JsonElement root = document.RootElement;
        if (!TryGetInt32(root, VersionProperty, out int version)
            || !TryGetString(root, ToolVersionProperty, out string? toolVersion)
            || !TryGetString(root, DescriptorHashProperty, out string? descriptorHash)
            || !TryGetString(root, CommonAssemblyHashProperty, out string? commonAssemblyHash)
            || !TryGetString(root, HostHashProperty, out string? hostHash)
            || !TryGetString(root, ModeProperty, out string? mode)
            || !TryGetInt32(root, AssetCountProperty, out int assetCount)
            || !TryGetInt32(root, TextureCountProperty, out int textureCount)
            || !TryGetInt32(root, ReferenceCountProperty, out int referenceCount)
            || !TryGetOutputHashes(root, out List<BakeOutputHash>? outputs)
            || !TryGetInputHashes(root, out List<BakeInputHash>? inputs))
        {
            return null;
        }

        return new BakeIncrementalManifest(
            version,
            toolVersion!,
            descriptorHash!,
            inputs!,
            commonAssemblyHash!,
            hostHash!,
            mode!,
            outputs!,
            assetCount,
            textureCount,
            referenceCount);
    }

    private static bool TryGetInputHashes(JsonElement element, out List<BakeInputHash>? hashes)
    {
        hashes = null;
        if (!TryGetHashEntries(element, InputsProperty, out List<(string Path, string Hash)>? entries))
        {
            return false;
        }

        hashes = [.. entries!.Select(entry => new BakeInputHash(entry.Path, entry.Hash))];
        return true;
    }

    private static bool TryGetOutputHashes(JsonElement element, out List<BakeOutputHash>? hashes)
    {
        hashes = null;
        if (!TryGetHashEntries(element, OutputsProperty, out List<(string Path, string Hash)>? entries))
        {
            return false;
        }

        hashes = [.. entries!.Select(entry => new BakeOutputHash(entry.Path, entry.Hash))];
        return true;
    }

    private static bool TryGetHashEntries(
        JsonElement element,
        string property,
        out List<(string Path, string Hash)>? entries)
    {
        entries = null;
        if (!element.TryGetProperty(property, out JsonElement arrayElement)
            || arrayElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var result = new List<(string Path, string Hash)>(arrayElement.GetArrayLength());
        foreach (JsonElement item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !TryGetString(item, PathProperty, out string? path)
                || !TryGetString(item, HashProperty, out string? hash))
            {
                return false;
            }

            result.Add((path!, hash!));
        }

        entries = result;
        return true;
    }

    private static bool TryGetString(JsonElement element, string property, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(property, out JsonElement propertyElement)
            || propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = propertyElement.GetString();
        return !string.IsNullOrEmpty(value);
    }

    private static bool TryGetInt32(JsonElement element, string property, out int value)
    {
        value = 0;
        return element.TryGetProperty(property, out JsonElement propertyElement)
            && propertyElement.ValueKind == JsonValueKind.Number
            && propertyElement.TryGetInt32(out value)
            && value >= 0;
    }
}
