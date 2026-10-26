using System.Text.Json;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// Serializes and parses <see cref="BakeHostGenerationState"/>.
/// </summary>
/// <remarks>
/// The reader is as narrow as the bake-manifest reader: missing, unreadable, or mistyped state is
/// treated as "no state" and forces a regeneration. The writer fixes property order and LF newlines
/// so the state is byte-stable for the same model.
/// </remarks>
internal static class BakeHostGenerationStateJson
{
    private const string VersionProperty = "version";
    private const string GeneratorVersionProperty = "generatorVersion";
    private const string DescriptorHashProperty = "descriptorHash";
    private const string CommonAssemblyHashProperty = "commonAssemblyHash";
    private const string ConfigurationProperty = "configuration";
    private const string OutputDirectoryProperty = "outputDirectory";
    private const string ProjectPathProperty = "projectPath";
    private const string ManifestPathProperty = "manifestPath";
    private const string GeneratedHashProperty = "generatedHash";

    /// <summary>Serializes the state to UTF-8 JSON bytes.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The JSON bytes with a trailing LF.</returns>
    internal static byte[] Serialize(BakeHostGenerationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

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
            writer.WriteNumber(VersionProperty, state.Version);
            writer.WriteString(GeneratorVersionProperty, state.GeneratorVersion);
            writer.WriteString(DescriptorHashProperty, state.DescriptorHash);
            writer.WriteString(CommonAssemblyHashProperty, state.CommonAssemblyHash);
            writer.WriteString(ConfigurationProperty, state.Configuration);
            writer.WriteString(OutputDirectoryProperty, state.OutputDirectory);
            writer.WriteString(ProjectPathProperty, state.ProjectPath);
            writer.WriteString(ManifestPathProperty, state.ManifestPath);
            writer.WriteString(GeneratedHashProperty, state.GeneratedHash);
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>Parses the state from UTF-8 JSON bytes.</summary>
    /// <param name="bytes">The JSON bytes.</param>
    /// <returns>The state, or <see langword="null"/> when the file is not valid state.</returns>
    internal static BakeHostGenerationState? TryDeserialize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        using JsonDocument? document = BakeJsonFile.TryParse(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        JsonElement root = document.RootElement;
        if (!TryGetInt32(root, VersionProperty, out int version)
            || !TryGetString(root, GeneratorVersionProperty, out string? generatorVersion)
            || !TryGetString(root, DescriptorHashProperty, out string? descriptorHash)
            || !TryGetString(root, CommonAssemblyHashProperty, out string? commonAssemblyHash)
            || !TryGetString(root, ConfigurationProperty, out string? configuration)
            || !TryGetString(root, OutputDirectoryProperty, out string? outputDirectory)
            || !TryGetString(root, ProjectPathProperty, out string? projectPath)
            || !TryGetString(root, ManifestPathProperty, out string? manifestPath)
            || !TryGetString(root, GeneratedHashProperty, out string? generatedHash))
        {
            return null;
        }

        return new BakeHostGenerationState(
            version,
            generatorVersion!,
            descriptorHash!,
            commonAssemblyHash!,
            configuration!,
            outputDirectory!,
            projectPath!,
            manifestPath!,
            generatedHash!);
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
