using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Toml;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Bork2d.Bake.Textures;

/// <summary>
/// Parses a texture import sidecar (<c>&lt;source file&gt;.toml</c>) strictly.
/// </summary>
/// <remarks>
/// <para>
/// Only <c>filter = "nearest" | "linear"</c> and <c>wrap = "clamp" | "repeat"</c> are allowed (Q-24);
/// <c>format</c> and every source-specific field are errors. Unknown keys, unknown values, and wrong
/// value types are errors carrying the sidecar's file/line/column. A missing key falls back to the
/// project descriptor's <c>[textures]</c> default.
/// </para>
/// <para>
/// Syntax errors are TOML parse failures and therefore use the shared <c>BK2001</c> code; texture
/// rule failures use <c>BK4xxx</c>. The parser never bakes a partially-read sidecar: it returns
/// <see langword="null"/> after adding at least one error.
/// </para>
/// </remarks>
internal static class TextureSidecarParser
{
    private static readonly HashSet<string> _allowedKeys = new(
        ["filter", "wrap"],
        StringComparer.Ordinal);

    /// <summary>
    /// Parses one sidecar and resolves its effective settings.
    /// </summary>
    /// <param name="sidecarPath">The absolute sidecar path.</param>
    /// <param name="displayPath">The diagnostic display path for the sidecar.</param>
    /// <param name="defaultFilter">The descriptor default filter.</param>
    /// <param name="defaultWrap">The descriptor default wrap mode.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The resolved settings, or <see langword="null"/> when an error was reported.</returns>
    internal static TextureImportSettings? Read(
        string sidecarPath,
        string displayPath,
        BakeTextureFilter defaultFilter,
        BakeTextureWrap defaultWrap,
        BakeDiagnostics diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sidecarPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        string text;
        try
        {
            text = File.ReadAllText(sidecarPath);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                TextureDiagnosticCodes.TextureSidecarRead,
                $"Failed to read texture sidecar '{displayPath}': {exception.Message}",
                displayPath);
            return null;
        }

        DocumentSyntax document;
        try
        {
            document = SyntaxParser.Parse(text, sidecarPath, validate: true);
        }
        catch (TomlException exception)
        {
            AddTomlException(diagnostics, displayPath, exception);
            return null;
        }

        if (document.HasErrors)
        {
            bool added = false;
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
                    displayPath,
                    message.Span.Start.Line + 1,
                    message.Span.Start.Column + 1);
                added = true;
            }

            if (!added)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TomlSyntax,
                    "The texture sidecar is not valid TOML.",
                    displayPath);
            }

            return null;
        }

        TomlSyntaxIndex syntax = TomlSyntaxIndex.Create(document);
        if (syntax.Duplicates.Count > 0)
        {
            foreach ((string displayKey, SourceSpan span) in syntax.Duplicates)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TomlSyntax,
                    $"Duplicate TOML key '{displayKey}'.",
                    displayPath,
                    span.Start.Line + 1,
                    span.Start.Column + 1);
            }

            return null;
        }

        TomlTable root;
        try
        {
            TomlTable? model = TomlSerializer.Deserialize<TomlTable>(
                text,
                new TomlSerializerOptions { SourceName = displayPath });
            if (model is null)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    TextureDiagnosticCodes.TextureSidecarValue,
                    $"The texture sidecar '{displayPath}' must be a TOML table.",
                    displayPath);
                return null;
            }

            root = model;
        }
        catch (TomlException exception)
        {
            AddTomlException(diagnostics, displayPath, exception);
            return null;
        }

        return Validate(
            diagnostics,
            syntax,
            sidecarPath,
            displayPath,
            root,
            defaultFilter,
            defaultWrap);
    }

    private static TextureImportSettings? Validate(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string sidecarPath,
        string displayPath,
        TomlTable root,
        BakeTextureFilter defaultFilter,
        BakeTextureWrap defaultWrap)
    {
        bool valid = true;

        foreach (string key in root.Keys)
        {
            if (_allowedKeys.Contains(key))
            {
                continue;
            }

            AddKeyError(
                diagnostics,
                syntax,
                displayPath,
                TextureDiagnosticCodes.TextureSidecarUnknownKey,
                $"Unknown texture sidecar key '{key}'; only 'filter' and 'wrap' are allowed.",
                TomlPath.Root.Child(key));
            valid = false;
        }

        BakeTextureFilter filter = defaultFilter;
        TextureSourceSpan? filterSpan = null;
        if (root.TryGetValue("filter", out object? filterValue))
        {
            if (filterValue is string filterString)
            {
                switch (filterString)
                {
                    case "nearest":
                        filter = BakeTextureFilter.Nearest;
                        break;
                    case "linear":
                        filter = BakeTextureFilter.Linear;
                        break;
                    default:
                        AddValueError(
                            diagnostics,
                            syntax,
                            displayPath,
                            TextureDiagnosticCodes.TextureSidecarValue,
                            $"Texture sidecar 'filter' must be 'nearest' or 'linear'; got '{filterString}'.",
                            TomlPath.Root.Child("filter"));
                        valid = false;
                        break;
                }

                filterSpan = FindSpan(syntax, TomlPath.Root.Child("filter"), displayPath);
            }
            else
            {
                AddValueError(
                    diagnostics,
                    syntax,
                    displayPath,
                    TextureDiagnosticCodes.TextureSidecarValue,
                    "Texture sidecar 'filter' must be the string 'nearest' or 'linear'.",
                    TomlPath.Root.Child("filter"));
                valid = false;
            }
        }

        BakeTextureWrap wrap = defaultWrap;
        TextureSourceSpan? wrapSpan = null;
        if (root.TryGetValue("wrap", out object? wrapValue))
        {
            if (wrapValue is string wrapString)
            {
                switch (wrapString)
                {
                    case "clamp":
                        wrap = BakeTextureWrap.Clamp;
                        break;
                    case "repeat":
                        wrap = BakeTextureWrap.Repeat;
                        break;
                    default:
                        AddValueError(
                            diagnostics,
                            syntax,
                            displayPath,
                            TextureDiagnosticCodes.TextureSidecarValue,
                            $"Texture sidecar 'wrap' must be 'clamp' or 'repeat'; got '{wrapString}'.",
                            TomlPath.Root.Child("wrap"));
                        valid = false;
                        break;
                }

                wrapSpan = FindSpan(syntax, TomlPath.Root.Child("wrap"), displayPath);
            }
            else
            {
                AddValueError(
                    diagnostics,
                    syntax,
                    displayPath,
                    TextureDiagnosticCodes.TextureSidecarValue,
                    "Texture sidecar 'wrap' must be the string 'clamp' or 'repeat'.",
                    TomlPath.Root.Child("wrap"));
                valid = false;
            }
        }

        return valid
            ? new TextureImportSettings(filter, wrap, sidecarPath, filterSpan, wrapSpan)
            : null;
    }

    private static TextureSourceSpan? FindSpan(
        TomlSyntaxIndex syntax,
        TomlPath path,
        string displayPath)
    {
        if (syntax.TryGetValueSpan(path, out SourceSpan span)
            || syntax.TryGetKeySpan(path, out span))
        {
            return new TextureSourceSpan(displayPath, span.Start.Line + 1, span.Start.Column + 1);
        }

        return null;
    }

    private static void AddValueError(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string displayPath,
        string code,
        string message,
        TomlPath path)
    {
        if (syntax.TryGetValueSpan(path, out SourceSpan span))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                code,
                message,
                displayPath,
                span.Start.Line + 1,
                span.Start.Column + 1);
            return;
        }

        AddKeyError(diagnostics, syntax, displayPath, code, message, path);
    }

    private static void AddKeyError(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string displayPath,
        string code,
        string message,
        TomlPath path)
    {
        if (syntax.TryGetKeySpan(path, out SourceSpan span))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                code,
                message,
                displayPath,
                span.Start.Line + 1,
                span.Start.Column + 1);
            return;
        }

        diagnostics.Add(BakeSeverity.Error, code, message, displayPath);
    }

    private static void AddTomlException(
        BakeDiagnostics diagnostics,
        string displayPath,
        TomlException exception)
    {
        diagnostics.Add(
            BakeSeverity.Error,
            BakeDiagnosticCodes.TomlSyntax,
            exception.Message,
            displayPath,
            exception.Line,
            exception.Column);
    }

    private static bool IsReadFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException;
}
