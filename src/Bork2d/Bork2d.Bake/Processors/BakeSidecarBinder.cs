using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bork2d.Api.Assets;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Sources;
using Bork2d.Bake.Toml;
using PolyType;
using PolyType.Abstractions;

namespace Bork2d.Bake.Processors;

/// <summary>
/// Reads and binds one processor's required sidecar through the shared shape-driven TOML binder.
/// </summary>
/// <remarks>
/// <para>
/// Sidecar text is read as strict UTF-8 (an optional BOM is stripped); a read or decoding failure is
/// <see cref="BakeDiagnosticCodes.ProcessorSourceRead"/>, and a syntax error is the shared
/// <c>BK2001</c>. Binding uses <see cref="TomlShapeBinder.BindSidecar"/>, so a root <c>[bork2d]</c>
/// table is an ordinary unknown key rather than filtered metadata.
/// </para>
/// <para>
/// Sidecars are settings-only: a bound value that produced an <c>AssetRef</c>/<c>DataRef</c> reference
/// site is rejected with <see cref="BakeDiagnosticCodes.SidecarReferenceUnsupported"/>.
/// </para>
/// </remarks>
internal static class BakeSidecarBinder
{
    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>Binds one required sidecar file for its processor source.</summary>
    /// <param name="project">The validated project configuration.</param>
    /// <param name="source">The processor source that owns the sidecar.</param>
    /// <param name="sidecarPath">The absolute sidecar path.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <param name="sidecar">The bound sidecar instance when successful.</param>
    /// <returns><see langword="true"/> when the sidecar was read, parsed, and bound successfully.</returns>
    internal static bool TryBind(
        BakeProject project,
        BakeProcessorSource source,
        string sidecarPath,
        BakeDiagnostics diagnostics,
        out object? sidecar)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sidecarPath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        sidecar = null;
        string diagnosticPath = BakeDiagnosticPath.ForSource(project.ProjectRoot, sidecarPath);

        if (!TryReadText(sidecarPath, diagnosticPath, source, diagnostics, out string text))
        {
            return false;
        }

        if (!TomlDocument.TryParse(
            text,
            diagnosticPath,
            diagnostics,
            out Tomlyn.Model.TomlTable root,
            out TomlSyntaxIndex syntax))
        {
            return false;
        }

        var context = new BakeBindContext(
            root,
            syntax,
            diagnostics,
            diagnosticPath,
            source.BaseAssetPath);

        int before = diagnostics.Count;
        object? bound;
        try
        {
            bound = TomlShapeBinder.BindSidecar(source.Processor.SidecarShape!, context);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.BinderUnexpected,
                $"Binding the sidecar '{source.SidecarRelativePath ?? sidecarPath}' for processor "
                    + $"'{source.Processor.Name}' failed unexpectedly: {exception.Message}",
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }

        if (diagnostics.Count != before)
        {
            return false;
        }

        if (bound is null)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.BinderUnexpected,
                $"Binding the sidecar '{source.SidecarRelativePath ?? sidecarPath}' for processor "
                    + $"'{source.Processor.Name}' returned no value without a diagnostic.",
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }

        if (context.References.Count > 0)
        {
            BakeReferenceSite reference = context.References[0];
            AddReferenceUnsupported(
                source,
                diagnosticPath,
                reference.MemberPath,
                reference.Line,
                reference.Column,
                diagnostics);
            return false;
        }

        if (TryFindReferenceMember(source.Processor.SidecarShape!, out string memberPath))
        {
            // The shape declares a reference but the file omitted it; sidecars are settings-only, so
            // the declaration itself is unsupported.
            AddReferenceUnsupported(
                source,
                diagnosticPath,
                memberPath,
                line: null,
                column: null,
                diagnostics);
            return false;
        }

        sidecar = bound;
        return true;
    }

    private static void AddReferenceUnsupported(
        BakeProcessorSource source,
        string diagnosticPath,
        string memberPath,
        int? line,
        int? column,
        BakeDiagnostics diagnostics)
    {
        diagnostics.Add(
            BakeSeverity.Error,
            BakeDiagnosticCodes.SidecarReferenceUnsupported,
            $"The sidecar '{source.SidecarRelativePath ?? source.FullPath + ".toml"}' of processor "
                + $"'{source.Processor.Name}' declares the reference '{memberPath}'; "
                + "sidecars are settings-only and AssetRef/DataRef members are not supported.",
            diagnosticPath,
            line,
            column,
            source.BaseAssetPath,
            memberPath);
    }

    private static bool TryFindReferenceMember(ITypeShape shape, out string memberPath)
    {
        memberPath = string.Empty;
        if (shape is not IObjectTypeShape objectShape)
        {
            return false;
        }

        foreach (IPropertyShape property in objectShape.Properties)
        {
            if (!IsReferenceType(property.PropertyType.Type))
            {
                continue;
            }

            memberPath = ResolveKeyName(property);
            return true;
        }

        return false;
    }

    private static string ResolveKeyName(IPropertyShape property)
    {
        JsonPropertyNameAttribute? attribute =
            property.AttributeProvider.GetCustomAttribute<JsonPropertyNameAttribute>();
        return attribute is not null && !string.IsNullOrEmpty(attribute.Name)
            ? attribute.Name
            : JsonNamingPolicy.CamelCase.ConvertName(property.Name);
    }

    private static bool IsReferenceType(Type type)
        => type == typeof(DataRef)
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AssetRef<>));

    private static bool TryReadText(
        string sidecarPath,
        string diagnosticPath,
        BakeProcessorSource source,
        BakeDiagnostics diagnostics,
        out string text)
    {
        text = string.Empty;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(sidecarPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ProcessorSourceRead,
                $"Failed to read the sidecar '{source.SidecarRelativePath ?? sidecarPath}' of "
                    + $"processor '{source.Processor.Name}': {exception.Message}",
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }

        ReadOnlySpan<byte> content = bytes;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            content = content[3..];
        }

        try
        {
            text = _strictUtf8.GetString(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ProcessorSourceRead,
                $"The sidecar '{source.SidecarRelativePath ?? sidecarPath}' of processor "
                    + $"'{source.Processor.Name}' is not valid UTF-8.",
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }
    }
}
