using Bork2d.Bake.Diagnostics;

namespace Bork2d.Bake.Processors;

/// <summary>
/// The immutable extension-to-processor registry built from the builder's processor registrations.
/// </summary>
/// <remarks>
/// <para>
/// Extensions are normalized to lowercase without a leading dot and trimmed of surrounding
/// whitespace, so <c>"png"</c> and <c>".PNG"</c> are the same extension. A later registration
/// overrides an earlier one for the same extension and reports an informational
/// <see cref="BakeDiagnosticCodes.ProcessorExtensionOverride"/> diagnostic naming both processors.
/// </para>
/// <para>
/// Matching is case-insensitive and uses the longest registered compound suffix, so a
/// <c>tar.gz</c> registration wins over <c>gz</c>. The registry is built by
/// <see cref="Baking.BakeProjectFactory"/> before the immutable project exists and never changes
/// afterwards. Registration order is preserved for diagnostics and WP5 hashing.
/// </para>
/// </remarks>
internal sealed class BakeProcessorRegistry
{
    private const string ReservedExtension = "toml";

    private readonly Dictionary<string, IBakeAssetProcessor> _byExtension;
    private readonly string[] _extensions;
    private readonly string[] _extensionsByMatchLength;
    private readonly IBakeAssetProcessor[] _processors;

    private BakeProcessorRegistry(
        Dictionary<string, IBakeAssetProcessor> byExtension,
        IBakeAssetProcessor[] processors)
    {
        _byExtension = byExtension;
        _processors = processors;
        _extensions = [.. byExtension.Keys.OrderBy(extension => extension, StringComparer.Ordinal)];
        _extensionsByMatchLength = [.. _extensions
            .OrderByDescending(extension => extension.Length)
            .ThenBy(extension => extension, StringComparer.Ordinal)];
    }

    /// <summary>Gets the registry with no processors; used by isolated tests and empty projects.</summary>
    internal static BakeProcessorRegistry Empty { get; } = new(
        new Dictionary<string, IBakeAssetProcessor>(StringComparer.Ordinal),
        []);

    /// <summary>Gets the normalized, distinct extensions in ordinal order.</summary>
    internal IReadOnlyList<string> Extensions => _extensions;

    /// <summary>Gets the processors in registration order.</summary>
    internal IReadOnlyList<IBakeAssetProcessor> Processors => _processors;

    /// <summary>Gets a value indicating whether no processor is registered.</summary>
    internal bool IsEmpty => _byExtension.Count == 0;

    /// <summary>Validates the registered processors and builds the immutable registry.</summary>
    /// <param name="processors">The processors in registration order.</param>
    /// <param name="diagnostics">The diagnostics bag to append registration diagnostics to.</param>
    /// <param name="registry">The created registry when every registration is valid.</param>
    /// <returns><see langword="true"/> when every processor registration is valid.</returns>
    internal static bool TryCreate(
        IReadOnlyList<IBakeAssetProcessor> processors,
        BakeDiagnostics diagnostics,
        out BakeProcessorRegistry? registry)
    {
        ArgumentNullException.ThrowIfNull(processors);
        ArgumentNullException.ThrowIfNull(diagnostics);

        registry = null;

        var byExtension = new Dictionary<string, IBakeAssetProcessor>(StringComparer.Ordinal);
        var registered = new List<IBakeAssetProcessor>(processors.Count);
        bool valid = true;

        foreach (IBakeAssetProcessor? processor in processors)
        {
            if (processor is null)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.ProcessorRegistrationInvalid,
                    "A processor registration is null; every registered processor must be an "
                        + "instance implementing IAssetProcessor.");
                valid = false;
                continue;
            }

            string name = Describe(processor);
            registered.Add(processor);

            if (string.IsNullOrWhiteSpace(processor.Name))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.ProcessorRegistrationInvalid,
                    $"The processor '{name}' has no name; every processor must expose a non-empty "
                        + "Name for diagnostics and hashing.");
                valid = false;
            }

            if (processor.Policy is not BakeSidecarPolicy.None and not BakeSidecarPolicy.Required)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.ProcessorRegistrationInvalid,
                    $"The processor '{name}' declares the unsupported sidecar policy "
                        + $"'{processor.Policy}'; only None and Required are valid.");
                valid = false;
            }
            else if (processor.Policy == BakeSidecarPolicy.None && processor.SidecarShape is not null)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.ProcessorRegistrationInvalid,
                    $"The processor '{name}' has SidecarPolicy.None but exposes a sidecar shape; a "
                        + "processor without a sidecar must return null from SidecarShape.");
                valid = false;
            }
            else if (processor.Policy == BakeSidecarPolicy.Required && processor.SidecarShape is null)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.ProcessorRegistrationInvalid,
                    $"The processor '{name}' has SidecarPolicy.Required but no sidecar shape; derive "
                        + "from AssetProcessor<TSidecar> or return a compiled sidecar shape.");
                valid = false;
            }

            IReadOnlyList<string> extensions = processor.Extensions;
            if (extensions is null || extensions.Count == 0)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.ProcessorRegistrationInvalid,
                    $"The processor '{name}' registers no extensions; every processor must own at "
                        + "least one source extension.");
                valid = false;
                continue;
            }

            var ownExtensions = new HashSet<string>(StringComparer.Ordinal);
            foreach (string? declared in extensions)
            {
                if (!TryNormalizeExtension(declared, out string extension, out string? reason))
                {
                    diagnostics.Add(
                        BakeSeverity.Error,
                        BakeDiagnosticCodes.ProcessorRegistrationInvalid,
                        $"The processor '{name}' declares the invalid extension '{declared}': "
                            + $"{reason}.");
                    valid = false;
                    continue;
                }

                if (!ownExtensions.Add(extension))
                {
                    // The same processor declaring "png" and ".PNG" is one registration.
                    continue;
                }

                if (byExtension.TryGetValue(extension, out IBakeAssetProcessor? existing))
                {
                    diagnostics.Add(
                        BakeSeverity.Info,
                        BakeDiagnosticCodes.ProcessorExtensionOverride,
                        $"The extension '{extension}' is overridden: '{name}' replaces "
                            + $"'{Describe(existing)}'.");
                }

                byExtension[extension] = processor;
            }
        }

        if (!valid)
        {
            return false;
        }

        registry = new BakeProcessorRegistry(byExtension, [.. registered]);
        return true;
    }

    /// <summary>Finds the processor that owns a source file.</summary>
    /// <param name="relativePath">The pack-root-relative source path.</param>
    /// <param name="match">The matching processor and normalized extension, when found.</param>
    /// <returns><see langword="true"/> when a registered extension matches the file name.</returns>
    internal bool TryMatch(string relativePath, out BakeProcessorMatch match)
    {
        if (!string.IsNullOrEmpty(relativePath))
        {
            foreach (string extension in _extensionsByMatchLength)
            {
                int suffixLength = extension.Length + 1;
                if (relativePath.Length >= suffixLength
                    && relativePath[relativePath.Length - suffixLength] == '.'
                    && relativePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    match = new BakeProcessorMatch(_byExtension[extension], extension);
                    return true;
                }
            }
        }

        match = default;
        return false;
    }

    private static bool TryNormalizeExtension(
        string? declared,
        out string extension,
        out string? reason)
    {
        extension = string.Empty;

        if (string.IsNullOrWhiteSpace(declared))
        {
            reason = "the extension is empty";
            return false;
        }

        string normalized = declared.Trim();
        if (normalized[0] == '.')
        {
            normalized = normalized[1..];
        }

        normalized = normalized.ToLowerInvariant();
        if (normalized.Length == 0)
        {
            reason = "the extension is empty after removing the leading dot";
            return false;
        }

        if (string.Equals(normalized, ReservedExtension, StringComparison.Ordinal))
        {
            reason = "'.toml' is reserved for data sources and sidecars";
            return false;
        }

        foreach (char character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                reason = "the extension contains whitespace";
                return false;
            }

            if (character is '/' or '\\' or '*' or '?' or ':')
            {
                reason = $"the extension contains the unsupported character '{character}'";
                return false;
            }
        }

        if (normalized[0] == '.' || normalized[^1] == '.')
        {
            reason = "the extension has a leading or trailing '.'";
            return false;
        }

        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            reason = "the extension has an empty '.' segment";
            return false;
        }

        extension = normalized;
        reason = null;
        return true;
    }

    private static string Describe(IBakeAssetProcessor processor)
    {
        if (!string.IsNullOrWhiteSpace(processor.Name))
        {
            return processor.Name;
        }

        return processor.GetType().FullName ?? "<unnamed processor>";
    }
}
