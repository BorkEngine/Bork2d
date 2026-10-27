namespace Bork2d.Bake.Baking;

/// <summary>
/// The invocation inputs of one <c>bake</c>/<c>validate</c>/<c>clean</c> command.
/// </summary>
/// <remarks>
/// The configuration (packs, roots, texture defaults, registrations, processors) lives in
/// <see cref="BakeProject"/>; these options describe the invocation. <see cref="OutputRoot"/> is
/// resolved to an absolute path before this instance is created. <see cref="Check"/> validates
/// everything and writes nothing, and never skips the bake. <see cref="Force"/> is accepted and
/// reserved for the WP5 incremental skip; WP1 always runs a full bake.
/// </remarks>
internal sealed class BakeOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BakeOptions"/> class.
    /// </summary>
    /// <param name="outputRoot">The absolute output root every pack path is resolved under.</param>
    /// <param name="mode">The payload compression mode.</param>
    /// <param name="check">Whether to validate without writing packs.</param>
    /// <param name="force">Whether <c>--force</c> was passed.</param>
    internal BakeOptions(string outputRoot, BakeMode mode, bool check, bool force)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        OutputRoot = outputRoot;
        Mode = mode;
        Check = check;
        Force = force;
    }

    /// <summary>Gets the absolute output root every pack path is joined under.</summary>
    internal string OutputRoot { get; }

    /// <summary>Gets the payload compression mode.</summary>
    internal BakeMode Mode { get; }

    /// <summary>Gets a value indicating whether validation runs without writing packs.</summary>
    internal bool Check { get; }

    /// <summary>
    /// Gets a value indicating whether <c>--force</c> was passed. WP1 accepts it; WP5 gives it the
    /// incremental-skip meaning.
    /// </summary>
    internal bool Force { get; }
}
