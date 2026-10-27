namespace Bork2d.Bake.Baking;

/// <summary>
/// The inputs of one <c>bork2d-bake bake</c> (or <c>validate --host</c>) invocation.
/// </summary>
/// <remarks>
/// CLI paths are already resolved against the process current directory by the caller. The output
/// root is optional: when it is <see langword="null"/> the descriptor directory is used, and every
/// descriptor pack path is resolved under it (D-M4-14). <see cref="Check"/> validates everything and
/// writes nothing, and never skips the bake. <see cref="Force"/> bypasses the incremental manifest
/// skip so a full bake always runs.
/// </remarks>
internal sealed class BakeOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BakeOptions"/> class.
    /// </summary>
    /// <param name="projectPath">The absolute descriptor path.</param>
    /// <param name="hostPath">The absolute generated host assembly path.</param>
    /// <param name="outputRoot">
    /// The absolute output root every pack path is joined under, or <see langword="null"/> for the
    /// descriptor directory.
    /// </param>
    /// <param name="mode">The payload compression mode.</param>
    /// <param name="check">Whether to validate without writing packs.</param>
    /// <param name="force">Whether <c>--force</c> was passed (bypasses the incremental skip).</param>
    internal BakeOptions(
        string projectPath,
        string hostPath,
        string? outputRoot,
        BakeMode mode,
        bool check,
        bool force)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostPath);

        ProjectPath = projectPath;
        HostPath = hostPath;
        OutputRoot = outputRoot;
        Mode = mode;
        Check = check;
        Force = force;
    }

    /// <summary>Gets the absolute descriptor path.</summary>
    internal string ProjectPath { get; }

    /// <summary>Gets the absolute generated host assembly path.</summary>
    internal string HostPath { get; }

    /// <summary>
    /// Gets the absolute output root every pack path is joined under, or <see langword="null"/> for
    /// the descriptor directory.
    /// </summary>
    internal string? OutputRoot { get; }

    /// <summary>Gets the payload compression mode.</summary>
    internal BakeMode Mode { get; }

    /// <summary>Gets a value indicating whether validation runs without writing packs.</summary>
    internal bool Check { get; }

    /// <summary>
    /// Gets a value indicating whether <c>--force</c> was passed, which bypasses the incremental
    /// manifest skip and always runs a full bake.
    /// </summary>
    internal bool Force { get; }
}
