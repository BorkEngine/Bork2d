using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Toml;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Bork2d.Bake.Project;

/// <summary>
/// Parses and strictly validates <c>bork2d.project.toml</c> (schema 1, D-M4-14).
/// </summary>
/// <remarks>
/// <para>
/// The descriptor is TOML and is never packed. Parsing uses Tomlyn's syntax tree for source spans
/// (0-based positions converted to the 1-based <see cref="BakeDiagnostic"/> model) and the Tomlyn
/// model for values. Unknown keys are errors, every relative path resolves against the descriptor's
/// directory, and mapped type declarations are deliberately not part of this model (D-M4-13): each
/// custom-data TOML declares its type in its own <c>[bork2d]</c> table. Texture defaults are
/// <c>linear</c>/<c>clamp</c> (the schema example in the M4 spec §6); a sidecar may override them.
/// </para>
/// <para>
/// The schema is an ordered <c>[[assets]]</c> list. The first entry is the main/base pack; later
/// entries are DLC/overlay packs (D-M4-14). Each entry requires <c>root</c> and <c>pack</c>, both
/// relative to the descriptor. Root and pack paths may not be rooted and may not contain <c>.</c> or
/// <c>..</c> segments; the pack path may contain subdirectories. Pack paths must be unique, roots
/// must be unique and exist, and at least one entry is required. The removed project <c>name</c>,
/// <c>[assets]</c> (single table), and <c>[bake].pack</c> keys are unknown-key errors.
/// </para>
/// <para>
/// The parser aggregates descriptor errors; it returns <see langword="null"/> when any error was
/// added. A missing descriptor file and unreadable files are diagnostics, not exceptions.
/// </para>
/// </remarks>
internal static class ProjectDescriptorParser
{
    private const string DefaultHostPath = "obj/bork2d/bakehost";

    private static readonly HashSet<string> _rootKeys = new(
        ["schema", "assets", "bake", "textures"],
        StringComparer.Ordinal);
    private static readonly HashSet<string> _assetsKeys = new(
        ["root", "pack"],
        StringComparer.Ordinal);
    private static readonly HashSet<string> _bakeKeys = new(["host"], StringComparer.Ordinal);
    private static readonly HashSet<string> _textureKeys = new(
        ["filter", "wrap"],
        StringComparer.Ordinal);

    private static readonly StringComparison _pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Parses and validates a project descriptor.
    /// </summary>
    /// <param name="descriptorPath">
    /// The descriptor path. A relative path resolves against the process current directory, matching
    /// the CLI contract; descriptor-internal paths resolve against the descriptor's directory.
    /// </param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The validated descriptor, or <see langword="null"/> when a diagnostic error occurred.</returns>
    internal static ProjectDescriptor? Parse(string descriptorPath, BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(descriptorPath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(descriptorPath);
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.DescriptorReadFailed,
                $"Invalid project descriptor path '{descriptorPath}': {exception.Message}",
                descriptorPath);
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(fullPath);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.DescriptorReadFailed,
                $"Failed to read the project descriptor '{fullPath}': {exception.Message}",
                fullPath);
            return null;
        }

        DocumentSyntax document;
        try
        {
            document = SyntaxParser.Parse(text, fullPath, validate: true);
        }
        catch (TomlException exception)
        {
            AddTomlException(diagnostics, fullPath, exception);
            return null;
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
                    fullPath,
                    message.Span.Start.Line + 1,
                    message.Span.Start.Column + 1);
            }

            if (!diagnostics.HasErrors)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TomlSyntax,
                    "The project descriptor is not valid TOML.",
                    fullPath);
            }

            return null;
        }

        TomlSyntaxIndex syntax = TomlSyntaxIndex.Create(document);
        if (syntax.Duplicates.Count > 0)
        {
            foreach ((string displayPath, SourceSpan span) in syntax.Duplicates)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TomlSyntax,
                    $"Duplicate TOML key '{displayPath}'.",
                    fullPath,
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
                new TomlSerializerOptions { SourceName = fullPath });
            if (model is null)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.DescriptorInvalidValue,
                    "The project descriptor must be a TOML table.",
                    fullPath);
                return null;
            }

            root = model;
        }
        catch (TomlException exception)
        {
            AddTomlException(diagnostics, fullPath, exception);
            return null;
        }

        string directory = Path.GetDirectoryName(fullPath) is { Length: > 0 } value
            ? value
            : fullPath;

        return Validate(fullPath, directory, diagnostics, syntax, root);
    }

    private static ProjectDescriptor? Validate(
        string fullPath,
        string directory,
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        TomlTable root)
    {
        bool valid = true;

        valid &= AddUnknownKeys(
            diagnostics,
            syntax,
            fullPath,
            root,
            TomlPath.Root,
            parentDisplay: string.Empty,
            _rootKeys);

        valid &= ValidateSchema(diagnostics, syntax, fullPath, root);

        valid &= TryValidateAssets(
            diagnostics,
            syntax,
            fullPath,
            directory,
            root,
            out IReadOnlyList<ProjectPack> packs);

        valid &= TryValidateBake(
            diagnostics,
            syntax,
            fullPath,
            directory,
            root,
            out string hostPath);

        valid &= TryValidateTextures(
            diagnostics,
            syntax,
            fullPath,
            root,
            out BakeTextureFilter textureFilter,
            out BakeTextureWrap textureWrap);

        if (!valid)
        {
            return null;
        }

        return new ProjectDescriptor(
            fullPath,
            packs,
            hostPath,
            textureFilter,
            textureWrap);
    }

    private static bool ValidateSchema(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
        TomlTable root)
    {
        if (!root.TryGetValue("schema", out object? schemaValue))
        {
            AddKeyError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorSchemaMissing,
                "Missing required key 'schema'.",
                TomlPath.Root);
            return false;
        }

        if (schemaValue is not long schema)
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                "'schema' must be the integer 1.",
                TomlPath.Root.Child("schema"));
            return false;
        }

        if (schema != 1)
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorSchemaUnsupported,
                $"'schema' value {schema} is not supported; expected 1.",
                TomlPath.Root.Child("schema"));
            return false;
        }

        return true;
    }

    private static bool TryValidateAssets(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
        string directory,
        TomlTable root,
        out IReadOnlyList<ProjectPack> packs)
    {
        packs = [];

        if (!root.TryGetValue("assets", out object? assetsValue))
        {
            AddMissingKey(
                diagnostics,
                syntax,
                fullPath,
                "assets",
                TomlPath.Root);
            return false;
        }

        if (assetsValue is not TomlTableArray assets)
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                "'assets' must be an array of tables: declare at least one [[assets]] entry with "
                    + "'root' and 'pack' (the first entry is the main pack).",
                TomlPath.Root.Child("assets"));
            return false;
        }

        if (assets.Count == 0)
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                "'assets' must declare at least one [[assets]] entry; the first entry is the main "
                    + "pack.",
                TomlPath.Root.Child("assets"));
            return false;
        }

        bool valid = true;
        var result = new List<ProjectPack>(assets.Count);
        var roots = new HashSet<string>(StringComparer.FromComparison(_pathComparison));
        var packPaths = new HashSet<string>(StringComparer.FromComparison(_pathComparison));

        for (int index = 0; index < assets.Count; index++)
        {
            if (assets[index] is not TomlTable entry)
            {
                AddValueError(
                    diagnostics,
                    syntax,
                    fullPath,
                    BakeDiagnosticCodes.DescriptorInvalidValue,
                    $"'assets[{index}]' must be a table.",
                    TomlPath.Root.Child("assets").Element(index));
                valid = false;
                continue;
            }

            TomlPath entryPath = TomlPath.Root.Child("assets").Element(index);
            string entryDisplay = $"assets[{index}]";

            valid &= AddUnknownKeys(
                diagnostics,
                syntax,
                fullPath,
                entry,
                entryPath,
                parentDisplay: entryDisplay,
                _assetsKeys);

            if (!TryValidateRoot(
                diagnostics,
                syntax,
                fullPath,
                directory,
                entry,
                entryPath,
                entryDisplay,
                index,
                roots,
                out string assetsRoot,
                out string relativeRoot))
            {
                valid = false;
            }

            if (!TryValidatePack(
                diagnostics,
                syntax,
                fullPath,
                directory,
                entry,
                entryPath,
                entryDisplay,
                index,
                packPaths,
                out string packPath))
            {
                valid = false;
            }

            if (valid)
            {
                result.Add(new ProjectPack(index, assetsRoot, relativeRoot, packPath));
            }
        }

        if (valid)
        {
            packs = result;
        }

        return valid;
    }

    private static bool TryValidateRoot(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
        string directory,
        TomlTable entry,
        TomlPath entryPath,
        string entryDisplay,
        int index,
        HashSet<string> roots,
        out string assetsRoot,
        out string relativeRoot)
    {
        assetsRoot = string.Empty;
        relativeRoot = string.Empty;

        if (!entry.TryGetValue("root", out object? rootValue))
        {
            AddMissingKey(
                diagnostics,
                syntax,
                fullPath,
                $"{entryDisplay}.root",
                entryPath);
            return false;
        }

        if (rootValue is not string rootString || string.IsNullOrWhiteSpace(rootString))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                $"'{entryDisplay}.root' must be a non-empty path string.",
                entryPath.Child("root"));
            return false;
        }

        if (!TryResolveRelativeDirectory(
            directory,
            rootString,
            out assetsRoot,
            out relativeRoot,
            out string? reason))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                $"'{entryDisplay}.root' is not a valid asset root: {reason}.",
                entryPath.Child("root"));
            return false;
        }

        if (!Directory.Exists(assetsRoot))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorAssetsRoot,
                $"The assets root '{assetsRoot}' of '{entryDisplay}' does not exist or is not a "
                    + "directory.",
                entryPath.Child("root"));
            return false;
        }

        if (!roots.Add(assetsRoot))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorDuplicateRoot,
                $"The assets root '{assetsRoot}' is declared by more than one [[assets]] entry; "
                    + "each pack must have its own root.",
                entryPath.Child("root"));
            return false;
        }

        _ = index;
        return true;
    }

    private static bool TryValidatePack(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
        string directory,
        TomlTable entry,
        TomlPath entryPath,
        string entryDisplay,
        int index,
        HashSet<string> packPaths,
        out string packPath)
    {
        packPath = string.Empty;

        if (!entry.TryGetValue("pack", out object? packValue))
        {
            AddMissingKey(
                diagnostics,
                syntax,
                fullPath,
                $"{entryDisplay}.pack",
                entryPath);
            return false;
        }

        if (packValue is not string packString || string.IsNullOrWhiteSpace(packString))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                $"'{entryDisplay}.pack' must be a non-empty path string.",
                entryPath.Child("pack"));
            return false;
        }

        if (!TryNormalizePackPath(
            directory,
            packString,
            out packPath,
            out string? reason))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                $"'{entryDisplay}.pack' is not a valid pack path: {reason}.",
                entryPath.Child("pack"));
            return false;
        }

        if (!packPaths.Add(packPath))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorDuplicatePack,
                $"The pack path '{packPath}' is declared by more than one [[assets]] entry; every "
                    + "pack output must be unique.",
                entryPath.Child("pack"));
            return false;
        }

        _ = index;
        return true;
    }

    private static bool TryValidateBake(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
        string directory,
        TomlTable root,
        out string hostPath)
    {
        hostPath = ResolveDefault(directory, DefaultHostPath);

        if (!root.TryGetValue("bake", out object? bakeValue))
        {
            return true;
        }

        if (bakeValue is not TomlTable bake)
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                "'bake' must be a table.",
                TomlPath.Root.Child("bake"));
            return false;
        }

        bool valid = AddUnknownKeys(
            diagnostics,
            syntax,
            fullPath,
            bake,
            TomlPath.Root.Child("bake"),
            parentDisplay: "bake",
            _bakeKeys);

        if (bake.TryGetValue("host", out object? hostValue))
        {
            valid &= TryReadPath(
                diagnostics,
                syntax,
                fullPath,
                directory,
                "bake.host",
                TomlPath.Root.Child("bake").Child("host"),
                hostValue,
                out hostPath);
        }

        return valid;
    }

    private static bool TryValidateTextures(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
        TomlTable root,
        out BakeTextureFilter filter,
        out BakeTextureWrap wrap)
    {
        filter = BakeTextureFilter.Linear;
        wrap = BakeTextureWrap.Clamp;

        if (!root.TryGetValue("textures", out object? texturesValue))
        {
            return true;
        }

        if (texturesValue is not TomlTable textures)
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                "'textures' must be a table.",
                TomlPath.Root.Child("textures"));
            return false;
        }

        bool valid = AddUnknownKeys(
            diagnostics,
            syntax,
            fullPath,
            textures,
            TomlPath.Root.Child("textures"),
            parentDisplay: "textures",
            _textureKeys);

        if (textures.TryGetValue("filter", out object? filterValue))
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
                        AddTextureSettingError(
                            diagnostics,
                            syntax,
                            fullPath,
                            "filter",
                            filterString);
                        valid = false;
                        break;
                }
            }
            else
            {
                AddTextureTypeError(diagnostics, syntax, fullPath, "filter");
                valid = false;
            }
        }

        if (textures.TryGetValue("wrap", out object? wrapValue))
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
                        AddTextureSettingError(
                            diagnostics,
                            syntax,
                            fullPath,
                            "wrap",
                            wrapString);
                        valid = false;
                        break;
                }
            }
            else
            {
                AddTextureTypeError(diagnostics, syntax, fullPath, "wrap");
                valid = false;
            }
        }

        return valid;
    }

    private static bool TryReadPath(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
        string directory,
        string displayPath,
        TomlPath path,
        object? value,
        out string resolved)
    {
        resolved = string.Empty;
        if (value is not string stringValue || string.IsNullOrWhiteSpace(stringValue))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                $"'{displayPath}' must be a non-empty path string.",
                path);
            return false;
        }

        if (!TryResolvePath(directory, stringValue, out resolved, out string? reason))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                $"'{displayPath}' is not a valid path: {reason}.",
                path);
            return false;
        }

        return true;
    }

    private static bool AddUnknownKeys(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
        TomlTable table,
        TomlPath parentPath,
        string parentDisplay,
        IReadOnlySet<string> known)
    {
        bool valid = true;
        foreach (string key in table.Keys)
        {
            if (known.Contains(key))
            {
                continue;
            }

            string displayPath = parentDisplay.Length == 0 ? key : $"{parentDisplay}.{key}";
            AddKeyError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorUnknownKey,
                $"Unknown key '{displayPath}'.",
                parentPath.Child(key));
            valid = false;
        }

        return valid;
    }

    private static void AddTextureSettingError(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
        string setting,
        string value)
    {
        string expected = setting == "filter" ? "'nearest' or 'linear'" : "'clamp' or 'repeat'";
        AddValueError(
            diagnostics,
            syntax,
            fullPath,
            BakeDiagnosticCodes.DescriptorTextureSetting,
            $"'textures.{setting}' must be {expected}; got '{value}'.",
            TomlPath.Root.Child("textures").Child(setting));
    }

    private static void AddTextureTypeError(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
        string setting)
    {
        AddValueError(
            diagnostics,
            syntax,
            fullPath,
            BakeDiagnosticCodes.DescriptorInvalidValue,
            $"'textures.{setting}' must be a string.",
            TomlPath.Root.Child("textures").Child(setting));
    }

    private static void AddMissingKey(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
        string displayPath,
        TomlPath containingPath)
    {
        AddKeyError(
            diagnostics,
            syntax,
            fullPath,
            BakeDiagnosticCodes.DescriptorMissingKey,
            $"Missing required key '{displayPath}'.",
            containingPath);
    }

    private static void AddValueError(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
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
                fullPath,
                span.Start.Line + 1,
                span.Start.Column + 1);
            return;
        }

        AddKeyError(diagnostics, syntax, fullPath, code, message, path);
    }

    private static void AddKeyError(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
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
                fullPath,
                span.Start.Line + 1,
                span.Start.Column + 1);
            return;
        }

        diagnostics.Add(BakeSeverity.Error, code, message, fullPath);
    }

    private static void AddTomlException(
        BakeDiagnostics diagnostics,
        string fullPath,
        TomlException exception)
    {
        diagnostics.Add(
            BakeSeverity.Error,
            BakeDiagnosticCodes.TomlSyntax,
            exception.Message,
            fullPath,
            exception.Line,
            exception.Column);
    }

    private static string ResolveDefault(string directory, string relativePath)
    {
        try
        {
            return Path.GetFullPath(relativePath, directory);
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            return relativePath;
        }
    }

    private static bool TryResolvePath(
        string directory,
        string value,
        out string resolved,
        out string? reason)
    {
        try
        {
            resolved = Path.GetFullPath(value, directory);
            reason = null;
            return true;
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            resolved = string.Empty;
            reason = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Resolves a descriptor-relative asset root and proves it is a normal descendant of the
    /// descriptor directory.
    /// </summary>
    private static bool TryResolveRelativeDirectory(
        string directory,
        string value,
        out string resolved,
        out string relative,
        out string? reason)
    {
        resolved = string.Empty;
        relative = string.Empty;

        if (Path.IsPathRooted(value))
        {
            reason = "the path must be relative to the descriptor";
            return false;
        }

        if (HasParentSegment(value))
        {
            reason = "the path must not contain '..' segments";
            return false;
        }

        if (!TryResolvePath(directory, value, out resolved, out reason))
        {
            return false;
        }

        relative = Path.GetRelativePath(directory, resolved).Replace('\\', '/');
        if (relative.Length == 0
            || relative.StartsWith("../", StringComparison.Ordinal)
            || relative == ".."
            || Path.IsPathRooted(relative))
        {
            reason = "the path must resolve to a directory under the descriptor";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Normalizes a descriptor-relative pack path and proves it is a normal relative path that may
    /// contain subdirectories.
    /// </summary>
    private static bool TryNormalizePackPath(
        string directory,
        string value,
        out string normalized,
        out string? reason)
    {
        normalized = string.Empty;

        if (Path.IsPathRooted(value))
        {
            reason = "the path must be relative to the output root";
            return false;
        }

        string candidate = value.Replace('\\', '/');
        if (candidate.StartsWith('/'))
        {
            reason = "the path must be relative to the output root";
            return false;
        }

        string[] segments = candidate.Split('/');
        foreach (string segment in segments)
        {
            if (segment.Length == 0)
            {
                reason = "the path must not contain empty segments";
                return false;
            }

            if (segment is "." or "..")
            {
                reason = "the path must not contain '.' or '..' segments";
                return false;
            }
        }

        try
        {
            _ = Path.GetFullPath(candidate, directory);
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            reason = exception.Message;
            return false;
        }

        normalized = candidate;
        reason = null;
        return true;
    }

    private static bool HasParentSegment(string value)
    {
        string[] segments = value.Split(['/', '\\'], StringSplitOptions.None);
        foreach (string segment in segments)
        {
            if (segment == "..")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPathFailure(Exception exception)
        => exception is ArgumentException or NotSupportedException or PathTooLongException;

    private static bool IsReadFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException;
}
