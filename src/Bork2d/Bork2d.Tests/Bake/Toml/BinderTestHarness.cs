using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Toml;
using PolyType;
using Tomlyn.Model;
using Xunit;

namespace Bork2d.Tests.Bake.Toml;

/// <summary>
/// Binds TOML text with the production parser, context, and shape binder, so a test failure points
/// at the same code path the generated host uses.
/// </summary>
internal static class BinderTestHarness
{
    internal const string SourcePath = "Assets/data/probe.toml";
    internal const string AssetPath = "data/probe";

    /// <summary>Binds <paramref name="toml"/> to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The mapped type.</typeparam>
    /// <param name="toml">The TOML source text.</param>
    /// <returns>The binding result.</returns>
    internal static BindResult Bind<T>(string toml)
        where T : IShapeable<T>
    {
        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeReferenceSite> references = [];

        if (!TomlDocument.TryParse(
            toml,
            SourcePath,
            diagnostics,
            out TomlTable root,
            out TomlSyntaxIndex syntax))
        {
            return new BindResult(null, diagnostics.GetSorted(), references);
        }

        var context = new BakeBindContext(root, syntax, diagnostics, SourcePath, AssetPath);
        object? value = TomlShapeBinder.Bind<T>(context);
        references = context.References;
        return new BindResult(value, diagnostics.GetSorted(), references);
    }

    /// <summary>Asserts the binding produced no diagnostics and returns the typed value.</summary>
    /// <typeparam name="T">The mapped type.</typeparam>
    /// <param name="toml">The TOML source text.</param>
    /// <returns>The bound value.</returns>
    internal static T BindOk<T>(string toml)
        where T : IShapeable<T>
    {
        BindResult result = Bind<T>(toml);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Value);
        return (T)result.Value!;
    }

    /// <summary>Returns the single diagnostic, asserting there is exactly one error with the code.</summary>
    /// <param name="result">The binding result.</param>
    /// <param name="code">The expected diagnostic code.</param>
    /// <returns>The diagnostic.</returns>
    internal static BakeDiagnostic Single(BindResult result, string code)
    {
        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeSeverity.Error, diagnostic.Severity);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(SourcePath, diagnostic.FilePath);
        return diagnostic;
    }
}

/// <summary>The outcome of one harness binding.</summary>
/// <param name="Value">The bound value, or <see langword="null"/> when the source failed.</param>
/// <param name="Diagnostics">The ordered diagnostics.</param>
/// <param name="References">The reference sites found.</param>
internal sealed record BindResult(
    object? Value,
    IReadOnlyList<BakeDiagnostic> Diagnostics,
    IReadOnlyList<BakeReferenceSite> References);
