using Bork2d.Bake.Diagnostics;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Bork2d.Bake.Toml;

/// <summary>
/// Parses a custom-data TOML source into the Tomlyn DOM plus its syntax index, reporting every
/// syntax error (including duplicate keys) as a <c>BK2001</c> diagnostic with a source span.
/// </summary>
/// <remarks>
/// The parser is shared by the source binder and by tests so one code path decides what a valid
/// source document is. Duplicate keys are rejected before any binding runs; a document with any
/// error produces no DOM, so the binder never sees a partially valid table.
/// </remarks>
internal static class TomlDocument
{
    /// <summary>Parses and validates a source document.</summary>
    /// <param name="text">The TOML source text.</param>
    /// <param name="filePath">The source path used in diagnostics, or <see langword="null"/>.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <param name="root">The parsed root table when parsing and validation succeed.</param>
    /// <param name="syntax">The syntax index when parsing and validation succeed.</param>
    /// <returns><see langword="true"/> when the document parsed without errors.</returns>
    internal static bool TryParse(
        string text,
        string? filePath,
        BakeDiagnostics diagnostics,
        out TomlTable root,
        out TomlSyntaxIndex syntax)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(diagnostics);

        root = null!;
        syntax = null!;

        string sourceName = filePath ?? "<source>";

        DocumentSyntax document;
        try
        {
            document = SyntaxParser.Parse(text, sourceName, validate: true);
        }
        catch (TomlException exception)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TomlSyntax,
                exception.Message,
                filePath,
                exception.Line,
                exception.Column);
            return false;
        }

        if (document.HasErrors)
        {
            foreach (DiagnosticMessage message in document.Diagnostics)
            {
                if (message.Kind != DiagnosticMessageKind.Error)
                {
                    continue;
                }

                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TomlSyntax,
                    message.Message,
                    filePath,
                    message.Span.Start.Line + 1,
                    message.Span.Start.Column + 1);
            }

            if (!diagnostics.HasErrors)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TomlSyntax,
                    "The TOML source is not valid.",
                    filePath);
            }

            return false;
        }

        TomlSyntaxIndex index = TomlSyntaxIndex.Create(document);
        if (index.Duplicates.Count > 0)
        {
            foreach ((string displayPath, SourceSpan span) in index.Duplicates)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TomlSyntax,
                    $"Duplicate TOML key '{displayPath}'.",
                    filePath,
                    span.Start.Line + 1,
                    span.Start.Column + 1);
            }

            return false;
        }

        TomlTable? model;
        try
        {
            model = TomlSerializer.Deserialize<TomlTable>(
                text,
                new TomlSerializerOptions { SourceName = sourceName });
        }
        catch (TomlException exception)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TomlSyntax,
                exception.Message,
                filePath,
                exception.Line,
                exception.Column);
            return false;
        }

        if (model is null)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.BinderTypeMismatch,
                "The TOML source root must be a table.",
                filePath);
            return false;
        }

        root = model;
        syntax = index;
        return true;
    }
}
