namespace Bork2d.Bake.Baking;

/// <summary>
/// The inputs of one <c>bork2d-bake bake</c> (or <c>validate --host</c>) invocation.
/// </summary>
/// <remarks>
/// CLI paths are already resolved against the process current directory by the caller. The output
/// path is optional: when it is <see langword="null"/> the descriptor's <c>[bake].pack</c> is the
/// target. <see cref="Check"/> validates everything and writes nothing, and never skips the bake.
/// <see cref="Force"/> bypasses the incremental manifest skip so a full bake always runs.
/// </remarks>
internal sealed class BakeOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BakeOptions"/> class.
    /// </summary>
    /// <param name="projectPath">The absolute descriptor path.</param>
    /// <param name="hostPath">The absolute generated host assembly path.</param>
    /// <param name="outputPath">The absolute pack path override, or <see langword="null"/>.</param>
    /// <param name="mode">The payload compression mode.</param>
    /// <param name="check">Whether to validate without writing a pack.</param>
    /// <param name="force">Whether <c>--force</c> was passed (no-op until WP5).</param>
    internal BakeOptions(
        string projectPath,
        string hostPath,
        string? outputPath,
        BakeMode mode,
        bool check,
        bool force)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostPath);

        ProjectPath = projectPath;
        HostPath = hostPath;
        OutputPath = outputPath;
        Mode = mode;
        Check = check;
        Force = force;
    }

    /// <summary>Gets the absolute descriptor path.</summary>
    internal string ProjectPath { get; }

    /// <summary>Gets the absolute generated host assembly path.</summary>
    internal string HostPath { get; }

    /// <summary>Gets the absolute pack path override, or <see langword="null"/> for the descriptor's.</summary>
    internal string? OutputPath { get; }

    /// <summary>Gets the payload compression mode.</summary>
    internal BakeMode Mode { get; }

    /// <summary>Gets a value indicating whether validation runs without writing a pack.</summary>
    internal bool Check { get; }

    /// <summary>
    /// Gets a value indicating whether <c>--force</c> was passed, which bypasses the incremental
    /// manifest skip and always runs a full bake.
    /// </summary>
    internal bool Force { get; }
}
