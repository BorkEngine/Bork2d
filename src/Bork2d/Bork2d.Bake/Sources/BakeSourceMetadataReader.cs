using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Toml;
using Tomlyn.Model;
using Tomlyn.Syntax;

namespace Bork2d.Bake.Sources;

/// <summary>
/// Reads the reserved root <c>[bork2d]</c> metadata table from a custom-data TOML source.
/// </summary>
/// <remarks>
/// <para>
/// D-M4-13 moves type registration out of the project descriptor and into each data document:
/// <c>[bork2d] type = "Full.Type.Name"</c>. The table is reserved top-level metadata. Only
/// <c>type</c> is accepted in v1; unknown keys, a non-table <c>bork2d</c> root value, a missing
/// <c>type</c>, and a non-string or empty <c>type</c> are diagnostics with the source line/column.
/// The declared type value span is returned for host-generation resolution diagnostics.
/// </para>
/// <para>
/// The reader is shared by discovery, host generation, <c>validate</c>, and the texture pipeline's
/// sidecar discrimination. It uses the same <see cref="TomlDocument"/> parser as binding, so a syntax
/// or duplicate-key failure is reported once by the caller that owns the source.
/// </para>
/// <para>
/// <see cref="TryHasBork2dTable"/> is the silent classification probe: it never reports diagnostics
/// and answers only whether a root <c>bork2d</c> key exists (a valid, malformed, or inline/dotted
/// table all count). Callers use it before deciding whether a <c>.toml</c> file is data or a texture
/// sidecar.
/// </para>
/// </remarks>
internal static class BakeSourceMetadataReader
{
    private const string TableName = "bork2d";
    private const string TypeName = "type";

    private static readonly TomlPath _tablePath = TomlPath.Root.Child(TableName);

    /// <summary>Reads and validates the metadata of a source document.</summary>
    /// <param name="text">The decoded TOML source.</param>
    /// <param name="filePath">The diagnostic path for the source.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The metadata outcome and, when typed, the declared type name and value span.</returns>
    internal static BakeSourceMetadata Read(
        string text,
        string? filePath,
        BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (!TomlDocument.TryParse(text, filePath, diagnostics, out TomlTable root, out TomlSyntaxIndex syntax))
        {
            return new BakeSourceMetadata(BakeSourceMetadataKind.Unreadable, null, null, null);
        }

        if (!root.TryGetValue(TableName, out object? tableValue))
        {
            return new BakeSourceMetadata(BakeSourceMetadataKind.Untyped, null, null, null);
        }

        if (tableValue is not TomlTable table)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SourceTypeInvalid,
                $"The root '{TableName}' value must be a table; declare the type as "
                    + $"[{TableName}] type = \"Full.Type.Name\".",
                filePath,
                GetKeyLine(syntax),
                GetKeyColumn(syntax));
            return new BakeSourceMetadata(BakeSourceMetadataKind.Invalid, null, null, null);
        }

        bool valid = true;
        foreach (string key in table.Keys)
        {
            if (string.Equals(key, TypeName, StringComparison.Ordinal))
            {
                continue;
            }

            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SourceTypeInvalid,
                $"Unknown key '{TableName}.{key}'; the reserved '{TableName}' table accepts only "
                    + $"'{TypeName}' in v1.",
                filePath,
                GetKeyLine(syntax, _tablePath.Child(key)),
                GetKeyColumn(syntax, _tablePath.Child(key)));
            valid = false;
        }

        if (!table.TryGetValue(TypeName, out object? typeValue))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SourceTypeMissing,
                $"Missing required key '{TableName}.{TypeName}'; every custom-data TOML must declare "
                    + "its full CLR type name.",
                filePath,
                GetKeyLine(syntax),
                GetKeyColumn(syntax));
            return new BakeSourceMetadata(BakeSourceMetadataKind.Invalid, null, null, null);
        }

        if (typeValue is not string typeName || string.IsNullOrWhiteSpace(typeName))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SourceTypeInvalid,
                $"'{TableName}.{TypeName}' must be a non-empty full CLR type name string.",
                filePath,
                GetValueLine(syntax),
                GetValueColumn(syntax));
            return new BakeSourceMetadata(BakeSourceMetadataKind.Invalid, null, null, null);
        }

        return valid
            ? new BakeSourceMetadata(
                BakeSourceMetadataKind.Typed,
                typeName,
                GetValueLine(syntax),
                GetValueColumn(syntax))
            : new BakeSourceMetadata(BakeSourceMetadataKind.Invalid, null, null, null);
    }

    /// <summary>
    /// Silently classifies a document by whether it has a root <c>bork2d</c> key.
    /// </summary>
    /// <param name="text">The TOML source.</param>
    /// <returns>
    /// <see langword="true"/> when the document parses and has a root <c>bork2d</c> key; otherwise
    /// <see langword="false"/>. The probe never reports diagnostics.
    /// </returns>
    internal static bool TryHasBork2dTable(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var scratch = new BakeDiagnostics();
        return TomlDocument.TryParse(text, filePath: null, scratch, out TomlTable root, out _)
            && root.ContainsKey(TableName);
    }

    private static int? GetValueLine(TomlSyntaxIndex syntax)
        => syntax.TryGetValueSpan(_tablePath.Child(TypeName), out SourceSpan span)
            ? span.Start.Line + 1
            : null;

    private static int? GetValueColumn(TomlSyntaxIndex syntax)
        => syntax.TryGetValueSpan(_tablePath.Child(TypeName), out SourceSpan span)
            ? span.Start.Column + 1
            : null;

    private static int? GetKeyLine(TomlSyntaxIndex syntax, TomlPath? path = null)
        => syntax.TryGetKeySpan(path ?? _tablePath, out SourceSpan span)
            ? span.Start.Line + 1
            : null;

    private static int? GetKeyColumn(TomlSyntaxIndex syntax, TomlPath? path = null)
        => syntax.TryGetKeySpan(path ?? _tablePath, out SourceSpan span)
            ? span.Start.Column + 1
            : null;
}
