namespace Bork2d.Bake.Cli;

/// <summary>
/// The JIT bake tool entry point.
/// </summary>
/// <remarks>
/// The entry point is internal and delegates to <see cref="CliApplication.Run"/> so the command
/// surface is driven by tests without spawning a process. WP2/WP4 add the <c>generate-host</c> and
/// <c>bake</c> commands to the same dispatch.
/// </remarks>
internal static class Program
{
    /// <summary>Runs the CLI.</summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The documented process exit code.</returns>
    private static int Main(string[] args)
        => CliApplication.Run(args, Console.Out, Console.Error);
}
