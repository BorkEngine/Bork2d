using System.Reflection;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Project;

namespace Bork2d.Bake.Cli;

/// <summary>
/// The internal CLI implementation: argument validation, exit codes, and output routing.
/// </summary>
/// <remarks>
/// <para>
/// Commands write diagnostics to <c>stderr</c> in the MSBuild-canonical form and the human summary
/// to <c>stdout</c>. Exit codes are <c>0</c> success, <c>1</c> aggregated source diagnostics,
/// <c>2</c> usage or descriptor configuration errors, and <c>3</c> an unexpected internal failure.
/// </para>
/// <para>
/// WP1 implements <c>--help</c>, <c>--version</c>, and <c>validate</c> only. <c>validate</c> checks
/// the descriptor; source binding and reference validation arrive with WP2/WP4. The dispatch is
/// structured so those commands slot in without changing the exit-code contract.
/// </para>
/// </remarks>
internal static class CliApplication
{
    /// <summary>The success exit code.</summary>
    internal const int SuccessExitCode = 0;

    /// <summary>One or more source diagnostics failed the command.</summary>
    internal const int DiagnosticsExitCode = 1;

    /// <summary>The invocation or descriptor configuration is invalid.</summary>
    internal const int UsageExitCode = 2;

    /// <summary>An unexpected internal failure occurred.</summary>
    internal const int InternalExitCode = 3;

    private const string ProgramName = "bork2d-bake";

    /// <summary>Runs one CLI invocation.</summary>
    /// <param name="args">The arguments, excluding the executable name.</param>
    /// <param name="output">The summary stream (normally <c>stdout</c>).</param>
    /// <param name="error">The diagnostics stream (normally <c>stderr</c>).</param>
    /// <returns>The documented process exit code.</returns>
    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        bool verboseRequested = Array.IndexOf(args, "--verbose") >= 0;

        try
        {
            if (args.Length == 0)
            {
                error.WriteLine($"{ProgramName}: a command is required.");
                WriteUsage(error);
                return UsageExitCode;
            }

            switch (args[0])
            {
                case "--help":
                case "-h":
                    WriteUsage(output);
                    return SuccessExitCode;

                case "--version":
                    output.WriteLine($"{ProgramName} {ToolVersion}");
                    return SuccessExitCode;

                case "validate":
                    return RunValidate(args.AsSpan(1), output, error);

                default:
                    error.WriteLine($"{ProgramName}: unknown command '{args[0]}'.");
                    WriteUsage(error);
                    return UsageExitCode;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            error.WriteLine(verboseRequested
                ? $"{ProgramName}: internal error: {exception}"
                : $"{ProgramName}: internal error: {exception.Message}");
            return InternalExitCode;
        }
    }

    private static int RunValidate(ReadOnlySpan<string> args, TextWriter output, TextWriter error)
    {
        string? projectPath = null;
        bool verbose = false;
        bool quiet = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project":
                    if (i + 1 >= args.Length)
                    {
                        return UsageError(error, "validate requires a value after --project.");
                    }

                    if (projectPath is not null)
                    {
                        return UsageError(error, "validate accepts --project only once.");
                    }

                    projectPath = args[++i];
                    break;

                case "--verbose":
                    verbose = true;
                    break;

                case "--quiet":
                    quiet = true;
                    break;

                default:
                    return UsageError(error, $"validate does not accept '{args[i]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return UsageError(error, "validate requires --project <bork2d.project.toml>.");
        }

        if (verbose && quiet)
        {
            return UsageError(error, "--verbose and --quiet are mutually exclusive.");
        }

        var diagnostics = new BakeDiagnostics();
        ProjectDescriptor? descriptor = ProjectDescriptorParser.Parse(projectPath, diagnostics);

        if (descriptor is null || diagnostics.HasErrors)
        {
            WriteDiagnostics(error, diagnostics);
            if (!quiet)
            {
                output.WriteLine(
                    $"{ProgramName}: project descriptor '{projectPath}' is invalid "
                        + $"({CountErrors(diagnostics)} error(s)).");
            }

            return UsageExitCode;
        }

        // WP2 adds source discovery/binding here: source diagnostics are aggregated and map to
        // DiagnosticsExitCode, while descriptor configuration errors stay UsageExitCode.
        if (!quiet)
        {
            output.WriteLine(
                $"{ProgramName}: '{descriptor.DescriptorPath}' is valid: assets root "
                    + $"'{descriptor.AssetsRoot}', pack '{descriptor.PackPath}', host "
                    + $"'{descriptor.HostPath}', {descriptor.Types.Count} type mapping(s).");
        }

        if (verbose)
        {
            foreach (ProjectTypeMapping mapping in descriptor.Types)
            {
                output.WriteLine(
                    $"  {mapping.Prefix} -> {mapping.TypeName}");
            }
        }

        return SuccessExitCode;
    }

    private static int UsageError(TextWriter error, string message)
    {
        error.WriteLine($"{ProgramName}: {message}");
        WriteUsage(error);
        return UsageExitCode;
    }

    private static void WriteDiagnostics(TextWriter error, BakeDiagnostics diagnostics)
    {
        foreach (BakeDiagnostic diagnostic in diagnostics.GetSorted())
        {
            error.WriteLine(diagnostic.Format());
        }
    }

    private static int CountErrors(BakeDiagnostics diagnostics)
    {
        int count = 0;
        foreach (BakeDiagnostic diagnostic in diagnostics.GetSorted())
        {
            if (diagnostic.Severity == BakeSeverity.Error)
            {
                count++;
            }
        }

        return count;
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine($"usage: {ProgramName} <command> [options]");
        writer.WriteLine("  --help       show this help");
        writer.WriteLine("  --version    show the tool version");
        writer.WriteLine(
            $"  validate --project <bork2d.project.toml> [--verbose|--quiet]");
        writer.WriteLine("               validate the project descriptor without reading sources");
        writer.WriteLine(
            "exit codes: 0 success, 1 source diagnostics, 2 usage/descriptor error, 3 internal error");
    }

    private static string ToolVersion
        => typeof(CliApplication).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? typeof(CliApplication).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";
}
