using System.Reflection;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Project;
using Bork2d.Bake.Sources;

namespace Bork2d.Bake.Cli;

/// <summary>
/// The internal CLI implementation: argument validation, exit codes, and output routing.
/// </summary>
/// <remarks>
/// <para>
/// Commands write diagnostics to <c>stderr</c> in the MSBuild-canonical form and the human summary to
/// <c>stdout</c>. Exit codes are <c>0</c> success/up to date, <c>1</c> aggregated source diagnostics,
/// <c>2</c> usage, descriptor, type-registration, or host-load configuration errors, and <c>3</c> an
/// unexpected internal failure.
/// </para>
/// <para>
/// <c>generate-host</c> writes the generated host project and manifest. <c>validate</c> checks the
/// descriptor; with <c>--host</c> it also loads the host, checks that the host manifest matches the
/// descriptor, and binds every mapped source. <c>validate</c> never validates references against the
/// baked set and never writes a pack; <c>bake</c> stays a later work package.
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

                case "generate-host":
                    return RunGenerateHost(args.AsSpan(1), output, error);

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

    private static int RunGenerateHost(
        ReadOnlySpan<string> args,
        TextWriter output,
        TextWriter error)
    {
        string? projectPath = null;
        string? commonPath = null;
        string? outputDirectory = null;
        string configuration = "Debug";
        bool force = false;
        bool verbose = false;
        bool quiet = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project":
                    if (i + 1 >= args.Length)
                    {
                        return UsageError(error, "generate-host requires a value after --project.");
                    }

                    if (projectPath is not null)
                    {
                        return UsageError(error, "generate-host accepts --project only once.");
                    }

                    projectPath = args[++i];
                    break;

                case "--common":
                    if (i + 1 >= args.Length)
                    {
                        return UsageError(error, "generate-host requires a value after --common.");
                    }

                    if (commonPath is not null)
                    {
                        return UsageError(error, "generate-host accepts --common only once.");
                    }

                    commonPath = args[++i];
                    break;

                case "--output":
                    if (i + 1 >= args.Length)
                    {
                        return UsageError(error, "generate-host requires a value after --output.");
                    }

                    if (outputDirectory is not null)
                    {
                        return UsageError(error, "generate-host accepts --output only once.");
                    }

                    outputDirectory = args[++i];
                    break;

                case "--configuration":
                    if (i + 1 >= args.Length)
                    {
                        return UsageError(error, "generate-host requires a value after --configuration.");
                    }

                    configuration = args[++i];
                    if (!HostGenerationOptions.IsSupportedConfiguration(configuration))
                    {
                        return UsageError(
                            error,
                            $"--configuration must be 'Debug' or 'Release'; got '{configuration}'.");
                    }

                    break;

                case "--force":
                    force = true;
                    break;

                case "--verbose":
                    verbose = true;
                    break;

                case "--quiet":
                    quiet = true;
                    break;

                default:
                    return UsageError(error, $"generate-host does not accept '{args[i]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return UsageError(error, "generate-host requires --project <bork2d.project.toml>.");
        }

        if (string.IsNullOrWhiteSpace(commonPath))
        {
            return UsageError(error, "generate-host requires --common <Project.Common.dll>.");
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return UsageError(error, "generate-host requires --output <directory>.");
        }

        if (verbose && quiet)
        {
            return UsageError(error, "--verbose and --quiet are mutually exclusive.");
        }

        if (!TryResolveCliPath(projectPath, "--project", error, out string resolvedProject)
            || !TryResolveCliPath(commonPath, "--common", error, out string resolvedCommon)
            || !TryResolveCliPath(outputDirectory, "--output", error, out string resolvedOutput))
        {
            return UsageExitCode;
        }

        var diagnostics = new BakeDiagnostics();
        var options = new HostGenerationOptions(
            resolvedProject,
            resolvedCommon,
            resolvedOutput,
            configuration,
            force);

        HostGenerationResult? result = HostGenerator.Generate(options, diagnostics);
        if (result is null || diagnostics.HasErrors)
        {
            WriteDiagnostics(error, diagnostics);
            if (!quiet)
            {
                output.WriteLine(
                    $"{ProgramName}: bake host generation failed "
                        + $"({CountErrors(diagnostics)} error(s)).");
            }

            return UsageExitCode;
        }

        if (!quiet)
        {
            string state = result.Changed ? "generated" : "up to date:";
            output.WriteLine(
                $"{ProgramName}: {state} '{result.ProjectPath}' "
                    + $"({result.Types.Count} mapped type(s), {result.References.Count} "
                    + "assembly reference(s)).");
        }

        if (verbose)
        {
            foreach (BakeMappedType mapping in result.Types)
            {
                output.WriteLine($"  {mapping.SourcePrefix} -> {mapping.TypeName}");
            }

            foreach (BakeHostReference reference in result.References)
            {
                output.WriteLine($"  reference {reference.AssemblyName}: {reference.Path}");
            }
        }

        return SuccessExitCode;
    }

    private static int RunValidate(
        ReadOnlySpan<string> args,
        TextWriter output,
        TextWriter error)
    {
        string? projectPath = null;
        string? hostPath = null;
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

                case "--host":
                    if (i + 1 >= args.Length)
                    {
                        return UsageError(error, "validate requires a value after --host.");
                    }

                    if (hostPath is not null)
                    {
                        return UsageError(error, "validate accepts --host only once.");
                    }

                    hostPath = args[++i];
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

        if (string.IsNullOrWhiteSpace(hostPath))
        {
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
                    output.WriteLine($"  {mapping.Prefix} -> {mapping.TypeName}");
                }
            }

            return SuccessExitCode;
        }

        if (!TryResolveCliPath(hostPath, "--host", error, out string resolvedHost))
        {
            return UsageExitCode;
        }

        return ValidateWithHost(
            descriptor,
            resolvedHost,
            verbose,
            quiet,
            output,
            error,
            diagnostics);
    }

    private static int ValidateWithHost(
        ProjectDescriptor descriptor,
        string hostPath,
        bool verbose,
        bool quiet,
        TextWriter output,
        TextWriter error,
        BakeDiagnostics diagnostics)
    {
        BakeHostHandle? handle = BakeHostLoader.TryLoad(hostPath, diagnostics);
        if (handle is null)
        {
            WriteDiagnostics(error, diagnostics);
            if (!quiet)
            {
                output.WriteLine(
                    $"{ProgramName}: could not load the bake host '{hostPath}' "
                        + $"({CountErrors(diagnostics)} error(s)).");
            }

            return UsageExitCode;
        }

        if (!BakeHostManifestValidator.Validate(descriptor, handle.Manifest, diagnostics)
            || diagnostics.HasErrors)
        {
            WriteDiagnostics(error, diagnostics);
            if (!quiet)
            {
                output.WriteLine(
                    $"{ProgramName}: the bake host '{hostPath}' does not match the project "
                        + "descriptor; regenerate the host.");
            }

            return UsageExitCode;
        }

        IReadOnlyList<BoundAsset> assets = BakeSourceBinder.BindAll(
            descriptor,
            handle.Manifest,
            diagnostics);

        if (diagnostics.HasErrors)
        {
            WriteDiagnostics(error, diagnostics);
            if (!quiet)
            {
                output.WriteLine(
                    $"{ProgramName}: '{descriptor.DescriptorPath}' has "
                        + $"{CountErrors(diagnostics)} source error(s).");
            }

            return DiagnosticsExitCode;
        }

        int referenceCount = assets.Sum(asset => asset.References.Count);
        if (!quiet)
        {
            output.WriteLine(
                $"{ProgramName}: '{descriptor.DescriptorPath}' is valid: "
                    + $"{assets.Count} asset(s) bound, {referenceCount} reference site(s). "
                    + "References are not validated and no pack is written by validate.");
        }

        if (verbose)
        {
            foreach (BoundAsset asset in assets)
            {
                output.WriteLine(
                    $"  {asset.AssetPath} -> {asset.TypeName} "
                        + $"({asset.References.Count} reference(s), '{asset.SourcePath}')");
            }
        }

        return SuccessExitCode;
    }

    private static bool TryResolveCliPath(
        string value,
        string optionName,
        TextWriter error,
        out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(value);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            error.WriteLine($"{ProgramName}: {optionName} '{value}' is not a valid path: {exception.Message}");
            WriteUsage(error);
            fullPath = string.Empty;
            return false;
        }
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
            "  generate-host --project <bork2d.project.toml> --common <Project.Common.dll> "
                + "--output <dir>");
        writer.WriteLine("                [--configuration Debug|Release] [--force] "
            + "[--verbose|--quiet]");
        writer.WriteLine(
            "               write the generated bake host project and manifest");
        writer.WriteLine(
            "  validate --project <bork2d.project.toml> [--host <BakeHost.dll>] "
                + "[--verbose|--quiet]");
        writer.WriteLine(
            "               validate the descriptor; with --host, bind every mapped source");
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
