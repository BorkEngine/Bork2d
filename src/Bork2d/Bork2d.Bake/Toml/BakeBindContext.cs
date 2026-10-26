using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Tomlyn.Model;
using Tomlyn.Syntax;

namespace Bork2d.Bake.Toml;

/// <summary>
/// The per-source state a generated host binds a TOML document into: the parsed DOM, its syntax
/// index, the diagnostic sink, and the reference sites collected while binding.
/// </summary>
/// <remarks>
/// <para>
/// The context is created by the tooling for exactly one source file and is not thread-safe. The
/// generated host never parses TOML itself: it receives this context, dispatches on the mapped type
/// name, and calls <see cref="TomlShapeBinder"/>. The binder resolves every source position through
/// <see cref="Syntax"/>, so diagnostics carry an exact <c>file:line:column</c>.
/// </para>
/// <para>
/// Reference sites are collected in binding order; the member path is the path relative to the
/// asset root (<c>texture</c>, <c>items[2].icon</c>) so the reference validator can point at the
/// value that produced the reference.
/// </para>
/// </remarks>
internal sealed class BakeBindContext
{
    private readonly List<BakeReferenceSite> _references = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="BakeBindContext"/> class.
    /// </summary>
    /// <param name="root">The parsed TOML table.</param>
    /// <param name="syntax">The syntax index used to resolve value and key spans.</param>
    /// <param name="diagnostics">The diagnostics bag the binder appends to.</param>
    /// <param name="filePath">
    /// The source path used in diagnostics, project-relative when the source is under the project
    /// directory and absolute otherwise; <see langword="null"/> when the source has no file.
    /// </param>
    /// <param name="assetPath">The canonical asset path the source produces, when known.</param>
    internal BakeBindContext(
        TomlTable root,
        TomlSyntaxIndex syntax,
        BakeDiagnostics diagnostics,
        string? filePath,
        string? assetPath)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Root = root;
        Syntax = syntax;
        Diagnostics = diagnostics;
        FilePath = filePath;
        AssetPath = assetPath;
    }

    /// <summary>Gets the parsed TOML document root.</summary>
    internal TomlTable Root { get; }

    /// <summary>Gets the syntax index for value and key spans.</summary>
    internal TomlSyntaxIndex Syntax { get; }

    /// <summary>Gets the diagnostics bag the binder appends to.</summary>
    internal BakeDiagnostics Diagnostics { get; }

    /// <summary>Gets the source path used in diagnostics, or <see langword="null"/>.</summary>
    internal string? FilePath { get; }

    /// <summary>Gets the canonical asset path the source produces, or <see langword="null"/>.</summary>
    internal string? AssetPath { get; }

    /// <summary>Gets the reference sites collected so far, in binding order.</summary>
    internal IReadOnlyList<BakeReferenceSite> References => _references;

    /// <summary>Adds a reference site found while binding.</summary>
    /// <param name="memberPath">The member path relative to the asset root.</param>
    /// <param name="referencePath">The canonical path as written in the source.</param>
    /// <param name="isData">Whether the reference targets a raw payload.</param>
    /// <param name="path">The TOML path of the reference value.</param>
    internal void AddReference(string memberPath, string referencePath, bool isData, TomlPath path)
    {
        int? line = null;
        int? column = null;
        if (TryGetSpan(path, out SourceSpan span))
        {
            line = span.Start.Line + 1;
            column = span.Start.Column + 1;
        }

        _references.Add(new BakeReferenceSite(memberPath, referencePath, isData, line, column));
    }

    /// <summary>
    /// Adds a binding diagnostic located at the value or key span of <paramref name="path"/>.
    /// </summary>
    /// <param name="code">The stable <c>BKxxxx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    /// <param name="path">The TOML path whose span locates the diagnostic.</param>
    /// <param name="memberPath">The member path relative to the asset root.</param>
    internal void AddError(string code, string message, TomlPath path, string? memberPath)
    {
        int? line = null;
        int? column = null;
        if (TryGetSpan(path, out SourceSpan span))
        {
            // Tomlyn positions are 0-based; the diagnostic model is 1-based.
            line = span.Start.Line + 1;
            column = span.Start.Column + 1;
        }

        Diagnostics.Add(
            BakeSeverity.Error,
            code,
            message,
            FilePath,
            line,
            column,
            AssetPath,
            memberPath);
    }

    /// <summary>Adds a binding diagnostic without a source position.</summary>
    /// <param name="code">The stable <c>BKxxxx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    /// <param name="memberPath">The member path relative to the asset root.</param>
    internal void AddError(string code, string message, string? memberPath)
        => Diagnostics.Add(
            BakeSeverity.Error,
            code,
            message,
            FilePath,
            assetPath: AssetPath,
            memberPath: memberPath);

    /// <summary>
    /// Adds a binding diagnostic located at the key span of <paramref name="path"/>, falling back
    /// to the value span and then to the file only.
    /// </summary>
    /// <param name="code">The stable <c>BKxxxx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    /// <param name="path">The TOML path whose key span locates the diagnostic.</param>
    /// <param name="memberPath">The member path relative to the asset root.</param>
    internal void AddKeyError(string code, string message, TomlPath path, string? memberPath)
    {
        int? line = null;
        int? column = null;
        if (Syntax.TryGetKeySpan(path, out SourceSpan keySpan)
            || Syntax.TryGetValueSpan(path, out keySpan))
        {
            line = keySpan.Start.Line + 1;
            column = keySpan.Start.Column + 1;
        }

        Diagnostics.Add(
            BakeSeverity.Error,
            code,
            message,
            FilePath,
            line,
            column,
            AssetPath,
            memberPath);
    }

    private bool TryGetSpan(TomlPath path, out SourceSpan span)
        => Syntax.TryGetValueSpan(path, out span) || Syntax.TryGetKeySpan(path, out span);
}
