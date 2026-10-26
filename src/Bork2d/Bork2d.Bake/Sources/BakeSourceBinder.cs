using System.Text;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Project;
using Bork2d.Bake.Toml;
using Tomlyn.Model;

namespace Bork2d.Bake.Sources;

/// <summary>
/// Reads every discovered source, parses it, and binds it through the generated host manifest.
/// </summary>
/// <remarks>
/// <para>
/// Binding is sequential and ordinal by canonical asset path, so diagnostics are deterministic.
/// A source that fails to read, parse, or bind is dropped from the result while its diagnostics stay
/// in the bag; a successful source yields its typed value and reference sites for WP4.
/// </para>
/// <para>
/// Source text is strict UTF-8 with an optional BOM: invalid bytes are a
/// <see cref="BakeDiagnosticCodes.SourceNotUtf8"/> diagnostic instead of replacement characters.
/// This pass does not validate references against the baked set and never writes a pack.
/// </para>
/// </remarks>
internal static class BakeSourceBinder
{
    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>Discovers and binds every custom-data source of a project.</summary>
    /// <param name="descriptor">The parsed project descriptor.</param>
    /// <param name="manifest">The loaded generated host manifest.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The successfully bound assets, sorted ordinal by canonical asset path.</returns>
    internal static IReadOnlyList<BoundAsset> BindAll(
        ProjectDescriptor descriptor,
        IBakeManifest manifest,
        BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(diagnostics);

        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);
        var assets = new List<BoundAsset>(sources.Count);

        foreach (BakeSource source in sources)
        {
            string? text = ReadSource(source, descriptor, diagnostics);
            if (text is null)
            {
                continue;
            }

            string diagnosticPath = BakeDiagnosticPath.ForSource(
                descriptor.DescriptorDirectory,
                source.FullPath);

            int before = diagnostics.Count;
            if (!TomlDocument.TryParse(
                text,
                diagnosticPath,
                diagnostics,
                out TomlTable root,
                out TomlSyntaxIndex syntax))
            {
                continue;
            }

            var context = new BakeBindContext(
                root,
                syntax,
                diagnostics,
                diagnosticPath,
                source.AssetPath);

            object? value;
            try
            {
                value = manifest.Bind(source.TypeName, context);
            }
            catch (BakeBindException exception)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.HostStale,
                    $"The bake host could not bind '{source.AssetPath}': {exception.Message}",
                    diagnosticPath,
                    assetPath: source.AssetPath);
                continue;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.HostLoadFailed,
                    $"Binding '{source.AssetPath}' failed: {exception.Message}",
                    diagnosticPath,
                    assetPath: source.AssetPath);
                continue;
            }

            if (diagnostics.Count != before)
            {
                continue;
            }

            if (value is null)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.HostContractInvalid,
                    $"The bake host returned no value for '{source.AssetPath}' without a diagnostic.",
                    diagnosticPath,
                    assetPath: source.AssetPath);
                continue;
            }

            assets.Add(new BoundAsset(
                source.AssetPath,
                source.TypeName,
                value,
                diagnosticPath,
                source.RelativePath,
                context.References));
        }

        return assets;
    }

    private static string? ReadSource(
        BakeSource source,
        ProjectDescriptor descriptor,
        BakeDiagnostics diagnostics)
    {
        string diagnosticPath = BakeDiagnosticPath.ForSource(
            descriptor.DescriptorDirectory,
            source.FullPath);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(source.FullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SourceReadFailed,
                $"Failed to read '{source.RelativePath}': {exception.Message}",
                diagnosticPath,
                assetPath: source.AssetPath);
            return null;
        }

        ReadOnlySpan<byte> content = bytes;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            content = content[3..];
        }

        try
        {
            return _strictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SourceNotUtf8,
                $"'{source.RelativePath}' is not valid UTF-8.",
                diagnosticPath,
                assetPath: source.AssetPath);
            return null;
        }
    }
}
