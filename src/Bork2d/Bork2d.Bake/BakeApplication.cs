using Bork2d.Bake.Baking;
using Bork2d.Bake.Commands;

namespace Bork2d.Bake;

/// <summary>
/// A configured build-time assets executable: runs the <c>bake</c>, <c>validate</c>, <c>clean</c>,
/// <c>copy-packs</c>, <c>--help</c>, and <c>--version</c> commands against the builder configuration.
/// </summary>
/// <remarks>
/// The same instance can run multiple commands; configuration is immutable after
/// <see cref="BakeApplicationBuilder.Build"/> and is revalidated per invocation. The CLI prefix is the
/// entry assembly's simple name, so the executable identifies itself whether it is the fixture assets
/// tool or a game's assets tool.
/// </remarks>
public sealed class BakeApplication
{
    private readonly BakeProjectDefinition _definition;

    /// <summary>
    /// Initializes a new instance of the <see cref="BakeApplication"/> class.
    /// </summary>
    /// <param name="definition">The builder-side configuration.</param>
    internal BakeApplication(BakeProjectDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
    }

    /// <summary>Runs one command with the process standard output and error streams.</summary>
    /// <param name="args">The command-line arguments, excluding the executable name.</param>
    /// <returns>The documented process exit code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is <see langword="null"/>.</exception>
    public Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return Task.FromResult(
            BakeCommandLine.Run(
                _definition,
                args,
                Console.Out,
                Console.Error,
                EntryAssemblyName()));
    }

    /// <summary>Runs one command against explicit writers and a CLI prefix.</summary>
    /// <param name="args">The command-line arguments, excluding the executable name.</param>
    /// <param name="output">The summary stream.</param>
    /// <param name="error">The diagnostics stream.</param>
    /// <param name="programName">The CLI prefix.</param>
    /// <returns>The documented process exit code.</returns>
    internal int Run(string[] args, TextWriter output, TextWriter error, string programName)
        => BakeCommandLine.Run(_definition, args, output, error, programName);

    private static string EntryAssemblyName()
        => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "bork2d-assets";
}
