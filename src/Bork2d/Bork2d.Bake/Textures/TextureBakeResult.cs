using Bork2d.Bake.Diagnostics;

namespace Bork2d.Bake.Textures;

/// <summary>
/// The result of one texture import pass: every successfully baked texture and every diagnostic.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Textures"/> is ordered by canonical asset path (ordinal), and
/// <see cref="Diagnostics"/> is in the deterministic order the diagnostics model defines. Colliding
/// or invalid sources are absent from <see cref="Textures"/>; a bake with any error must not write a
/// pack, but the library still returns the sources that did succeed so callers can report all
/// problems in one pass.
/// </para>
/// <para>
/// The result owns no file handles and no graphics state; WP4 composes it with custom data and the
/// pack writer.
/// </para>
/// </remarks>
internal sealed class TextureBakeResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextureBakeResult"/> class.
    /// </summary>
    /// <param name="textures">The successfully baked textures, already ordered by asset path.</param>
    /// <param name="diagnostics">The sorted diagnostics.</param>
    internal TextureBakeResult(
        IReadOnlyList<BakedTexture> textures,
        IReadOnlyList<BakeDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Textures = [.. textures];
        Diagnostics = [.. diagnostics];

        bool hasErrors = false;
        foreach (BakeDiagnostic diagnostic in Diagnostics)
        {
            if (diagnostic.Severity == BakeSeverity.Error)
            {
                hasErrors = true;
                break;
            }
        }

        HasErrors = hasErrors;
    }

    /// <summary>Gets the successfully baked textures, ordered by canonical asset path.</summary>
    internal IReadOnlyList<BakedTexture> Textures { get; }

    /// <summary>Gets the diagnostics in deterministic order.</summary>
    internal IReadOnlyList<BakeDiagnostic> Diagnostics { get; }

    /// <summary>Gets a value indicating whether at least one error was reported.</summary>
    internal bool HasErrors { get; }
}
