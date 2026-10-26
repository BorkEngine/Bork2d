using Bork2d.Assets.Vfs;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Toml;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Bork2d.Bake.Project;

/// <summary>
/// Parses and strictly validates <c>bork2d.project.toml</c> (schema 1).
/// </summary>
/// <remarks>
/// <para>
/// The descriptor is TOML and is never packed. Parsing uses Tomlyn's syntax tree for source spans
/// (0-based positions converted to the 1-based <see cref="BakeDiagnostic"/> model) and the Tomlyn
/// model for values. Unknown keys are errors, every relative path resolves against the descriptor's
/// directory, and mapped type existence/<c>IShapeable&lt;T&gt;</c> validation is deliberately left to
/// the generated host (WP2). Texture defaults are <c>linear</c>/<c>clamp</c> (the schema example in
/// the M4 spec §6); a sidecar may override them.
/// </para>
/// <para>
/// The parser aggregates descriptor errors; it returns <see langword="null"/> when any error was
/// added. A missing descriptor file and unreadable files are diagnostics, not exceptions.
/// </para>
/// </remarks>
internal static class ProjectDescriptorParser
{
    private const string DefaultPackPath = "assets.pck";
    private const string DefaultHostPath = "obj/bork2d/bakehost";

    private static readonly HashSet<string> _rootKeys = new(
        ["schema", "name", "assets", "bake", "textures", "types"],
        StringComparer.Ordinal);
    private static readonly HashSet<string> _assetsKeys = new(["root"], StringComparer.Ordinal);
    private static readonly HashSet<string> _bakeKeys = new(
        ["pack", "host"],
        StringComparer.Ordinal);
    private static readonly HashSet<string> _textureKeys = new(
        ["filter", "wrap"],
        StringComparer.Ordinal);
    private static readonly HashSet<string> _typeKeys = new(
        ["prefix", "type"],
        StringComparer.Ordinal);

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

        string? name = null;
        if (root.TryGetValue("name", out object? nameValue))
        {
            if (nameValue is string nameString)
            {
                name = nameString;
            }
            else
            {
                AddValueError(
                    diagnostics,
                    syntax,
                    fullPath,
                    BakeDiagnosticCodes.DescriptorInvalidValue,
                    "'name' must be a string.",
                    TomlPath.Root.Child("name"));
                valid = false;
            }
        }

        valid &= TryValidateAssets(
            diagnostics,
            syntax,
            fullPath,
            directory,
            root,
            out string assetsRoot);

        valid &= TryValidateBake(
            diagnostics,
            syntax,
            fullPath,
            directory,
            root,
            out string packPath,
            out string hostPath);

        valid &= TryValidateTextures(
            diagnostics,
            syntax,
            fullPath,
            root,
            out BakeTextureFilter textureFilter,
            out BakeTextureWrap textureWrap);

        valid &= TryValidateTypes(
            diagnostics,
            syntax,
            fullPath,
            root,
            out List<ProjectTypeMapping> types);

        if (!valid)
        {
            return null;
        }

        return new ProjectDescriptor(
            fullPath,
            name,
            assetsRoot,
            packPath,
            hostPath,
            textureFilter,
            textureWrap,
            types);
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
        out string assetsRoot)
    {
        assetsRoot = string.Empty;

        if (!root.TryGetValue("assets", out object? assetsValue))
        {
            AddMissingKey(
                diagnostics,
                syntax,
                fullPath,
                "assets.root",
                TomlPath.Root);
            return false;
        }

        if (assetsValue is not TomlTable assets)
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                "'assets' must be a table.",
                TomlPath.Root.Child("assets"));
            return false;
        }

        bool valid = AddUnknownKeys(
            diagnostics,
            syntax,
            fullPath,
            assets,
            TomlPath.Root.Child("assets"),
            parentDisplay: "assets",
            _assetsKeys);

        if (!assets.TryGetValue("root", out object? rootValue))
        {
            AddMissingKey(
                diagnostics,
                syntax,
                fullPath,
                "assets.root",
                TomlPath.Root.Child("assets"));
            return false;
        }

        if (rootValue is not string rootString || string.IsNullOrWhiteSpace(rootString))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                "'assets.root' must be a non-empty path string.",
                TomlPath.Root.Child("assets").Child("root"));
            return false;
        }

        if (!TryResolvePath(directory, rootString, out string resolved, out string? reason))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                $"'assets.root' is not a valid path: {reason}.",
                TomlPath.Root.Child("assets").Child("root"));
            return false;
        }

        if (!Directory.Exists(resolved))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorAssetsRoot,
                $"The assets root '{resolved}' does not exist or is not a directory.",
                TomlPath.Root.Child("assets").Child("root"));
            return false;
        }

        assetsRoot = resolved;
        return valid;
    }

    private static bool TryValidateBake(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
        string directory,
        TomlTable root,
        out string packPath,
        out string hostPath)
    {
        packPath = ResolveDefault(directory, DefaultPackPath);
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

        if (bake.TryGetValue("pack", out object? packValue))
        {
            valid &= TryReadPath(
                diagnostics,
                syntax,
                fullPath,
                directory,
                "bake.pack",
                TomlPath.Root.Child("bake").Child("pack"),
                packValue,
                out packPath);
        }

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

    private static bool TryValidateTypes(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
        TomlTable root,
        out List<ProjectTypeMapping> types)
    {
        types = [];
        if (!root.TryGetValue("types", out object? typesValue))
        {
            return true;
        }

        var mappings = new List<ProjectTypeMapping>();
        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        bool valid = true;

        switch (typesValue)
        {
            case TomlTableArray tableArray:
                for (int i = 0; i < tableArray.Count; i++)
                {
                    valid &= TryValidateTypeEntry(
                        diagnostics,
                        syntax,
                        fullPath,
                        tableArray[i],
                        TomlPath.Root.Child("types").Element(i),
                        $"types[{i}]",
                        mappings,
                        prefixes);
                }

                break;

            case TomlArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    if (array[i] is not TomlTable table)
                    {
                        AddValueError(
                            diagnostics,
                            syntax,
                            fullPath,
                            BakeDiagnosticCodes.DescriptorInvalidValue,
                            $"'types[{i}]' must be a table.",
                            TomlPath.Root.Child("types").Element(i));
                        valid = false;
                        continue;
                    }

                    valid &= TryValidateTypeEntry(
                        diagnostics,
                        syntax,
                        fullPath,
                        table,
                        TomlPath.Root.Child("types").Element(i),
                        $"types[{i}]",
                        mappings,
                        prefixes);
                }

                break;

            default:
                AddValueError(
                    diagnostics,
                    syntax,
                    fullPath,
                    BakeDiagnosticCodes.DescriptorInvalidValue,
                    "'types' must be an array of tables.",
                    TomlPath.Root.Child("types"));
                return false;
        }

        if (!valid)
        {
            return false;
        }

        types = mappings;
        return true;
    }

    private static bool TryValidateTypeEntry(
        BakeDiagnostics diagnostics,
        TomlSyntaxIndex syntax,
        string fullPath,
        TomlTable entry,
        TomlPath entryPath,
        string entryDisplay,
        List<ProjectTypeMapping> mappings,
        HashSet<string> prefixes)
    {
        bool valid = AddUnknownKeys(
            diagnostics,
            syntax,
            fullPath,
            entry,
            entryPath,
            entryDisplay,
            _typeKeys);

        string? prefix = null;
        string? typeName = null;

        if (!entry.TryGetValue("prefix", out object? prefixValue))
        {
            AddMissingKey(diagnostics, syntax, fullPath, $"{entryDisplay}.prefix", entryPath);
            valid = false;
        }
        else if (prefixValue is not string prefixString || string.IsNullOrWhiteSpace(prefixString))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                $"'{entryDisplay}.prefix' must be a non-empty string.",
                entryPath.Child("prefix"));
            valid = false;
        }
        else if (!AssetPath.TryValidateCanonical(prefixString, out string? reason))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidPrefix,
                $"'{entryDisplay}.prefix' is not a canonical asset path: {reason}.",
                entryPath.Child("prefix"));
            valid = false;
        }
        else
        {
            prefix = prefixString;
        }

        if (!entry.TryGetValue("type", out object? typeValue))
        {
            AddMissingKey(diagnostics, syntax, fullPath, $"{entryDisplay}.type", entryPath);
            valid = false;
        }
        else if (typeValue is not string typeString || string.IsNullOrWhiteSpace(typeString))
        {
            AddValueError(
                diagnostics,
                syntax,
                fullPath,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                $"'{entryDisplay}.type' must be a non-empty string.",
                entryPath.Child("type"));
            valid = false;
        }
        else
        {
            typeName = typeString;
        }

        if (prefix is not null && typeName is not null)
        {
            if (!prefixes.Add(prefix))
            {
                AddKeyError(
                    diagnostics,
                    syntax,
                    fullPath,
                    BakeDiagnosticCodes.DescriptorDuplicatePrefix,
                    $"Duplicate type prefix '{prefix}'.",
                    entryPath.Child("prefix"));
                valid = false;
            }
            else
            {
                (int? prefixLine, int? prefixColumn) = GetValueLocation(
                    syntax,
                    entryPath.Child("prefix"));
                (int? typeLine, int? typeColumn) = GetValueLocation(
                    syntax,
                    entryPath.Child("type"));
                mappings.Add(new ProjectTypeMapping(prefix, typeName)
                {
                    PrefixLine = prefixLine,
                    PrefixColumn = prefixColumn,
                    TypeLine = typeLine,
                    TypeColumn = typeColumn,
                });
            }
        }

        return valid;
    }

    private static (int? Line, int? Column) GetValueLocation(TomlSyntaxIndex syntax, TomlPath path)
        => syntax.TryGetValueSpan(path, out SourceSpan span)
            ? (span.Start.Line + 1, span.Start.Column + 1)
            : (null, null);

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

    private static bool IsPathFailure(Exception exception)
        => exception is ArgumentException or NotSupportedException or PathTooLongException;

    private static bool IsReadFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException;
}
