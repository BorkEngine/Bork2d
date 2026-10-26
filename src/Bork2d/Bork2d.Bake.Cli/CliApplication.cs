using System.Reflection;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Incremental;
using Bork2d.Bake.Project;
using Bork2d.Bake.Sources;
using Bork2d.Bake.Textures;

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
/// <c>generate-host</c> writes the generated host project and manifest, and skips when its recorded
/// inputs and generated content are unchanged. <c>bake</c> runs the complete pipeline (descriptor,
/// host, custom-data binding, textures, reference validation, pack) unless the incremental manifest
/// shows that the pack is current, and writes the pack and manifest atomically. <c>--check</c>
/// validates everything and writes nothing, and never skips. <c>validate</c> without a host checks
/// the descriptor, the reachable texture sources, and custom-data TOML syntax; with <c>--host</c> it
/// runs the same validation pipeline as <c>bake --check</c> but never writes a pack. <c>--force</c>
/// bypasses both incremental skips.
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

                case "bake":
                    return RunBake(args.AsSpan(1), output, error);

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
            if (result.Changed)
            {
                output.WriteLine(
                    $"{ProgramName}: generated '{result.ProjectPath}' "
                        + $"({result.Types.Count} mapped type(s), {result.References.Count} "
                        + "assembly reference(s)).");
            }
            else
            {
                output.WriteLine($"{ProgramName}: up to date: '{result.ProjectPath}'.");
            }
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

    private static int RunBake(
        ReadOnlySpan<string> args,
        TextWriter output,
        TextWriter error)
    {
        string? projectPath = null;
        string? hostPath = null;
        string? outputPath = null;
        BakeMode mode = BakeMode.Debug;
        bool check = false;
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
                        return UsageError(error, "bake requires a value after --project.");
                    }

                    if (projectPath is not null)
                    {
                        return UsageError(error, "bake accepts --project only once.");
                    }

                    projectPath = args[++i];
                    break;

                case "--host":
                    if (i + 1 >= args.Length)
                    {
                        return UsageError(error, "bake requires a value after --host.");
                    }

                    if (hostPath is not null)
                    {
                        return UsageError(error, "bake accepts --host only once.");
                    }

                    hostPath = args[++i];
                    break;

                case "--output":
                    if (i + 1 >= args.Length)
                    {
                        return UsageError(error, "bake requires a value after --output.");
                    }

                    if (outputPath is not null)
                    {
                        return UsageError(error, "bake accepts --output only once.");
                    }

                    outputPath = args[++i];
                    break;

                case "--mode":
                    if (i + 1 >= args.Length)
                    {
                        return UsageError(error, "bake requires a value after --mode.");
                    }

                    if (!BakeModeExtensions.TryParse(args[++i], out mode))
                    {
                        return UsageError(
                            error,
                            $"--mode must be 'debug' or 'release'; got '{args[i]}'.");
                    }

                    break;

                case "--check":
                    check = true;
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
                    return UsageError(error, $"bake does not accept '{args[i]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return UsageError(error, "bake requires --project <bork2d.project.toml>.");
        }

        if (string.IsNullOrWhiteSpace(hostPath))
        {
            return UsageError(error, "bake requires --host <BakeHost.dll>.");
        }

        if (verbose && quiet)
        {
            return UsageError(error, "--verbose and --quiet are mutually exclusive.");
        }

        if (!TryResolveCliPath(projectPath, "--project", error, out string resolvedProject)
            || !TryResolveCliPath(hostPath, "--host", error, out string resolvedHost))
        {
            return UsageExitCode;
        }

        string? resolvedOutput = null;
        if (!string.IsNullOrWhiteSpace(outputPath)
            && !TryResolveCliPath(outputPath, "--output", error, out resolvedOutput))
        {
            return UsageExitCode;
        }

        var options = new BakeOptions(
            resolvedProject,
            resolvedHost,
            resolvedOutput,
            mode,
            check,
            force);
        var diagnostics = new BakeDiagnostics();
        BakeResult result = BakeIncrementalPlanner.Run(options, diagnostics);
        WriteDiagnostics(error, result.Diagnostics);

        if (result.Status is BakeStatus.Success or BakeStatus.UpToDate)
        {
            if (!quiet)
            {
                if (result.Status == BakeStatus.UpToDate)
                {
                    WriteUpToDateSummary(output, options, result);
                }
                else
                {
                    WriteBakeSuccessSummary(output, options, result);
                }
            }

            if (verbose)
            {
                WriteEntryList(output, result.Entries);
            }

            return SuccessExitCode;
        }

        if (!quiet)
        {
            WriteBakeFailureSummary(output, options, result);
        }

        return result.Status == BakeStatus.ConfigurationError
            ? UsageExitCode
            : DiagnosticsExitCode;
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

        if (!TryResolveCliPath(projectPath, "--project", error, out string resolvedProject))
        {
            return UsageExitCode;
        }

        if (!string.IsNullOrWhiteSpace(hostPath))
        {
            if (!TryResolveCliPath(hostPath, "--host", error, out string resolvedHost))
            {
                return UsageExitCode;
            }

            return ValidateWithHost(resolvedProject, resolvedHost, verbose, quiet, output, error);
        }

        return ValidateWithoutHost(resolvedProject, verbose, quiet, output, error);
    }

    private static int ValidateWithoutHost(
        string projectPath,
        bool verbose,
        bool quiet,
        TextWriter output,
        TextWriter error)
    {
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

        TextureBakeResult textures = TexturePipeline.Import(descriptor);
        foreach (BakeDiagnostic diagnostic in textures.Diagnostics)
        {
            diagnostics.Add(diagnostic);
        }

        int sourceCount = BakeSourceBinder.ValidateSyntax(descriptor, diagnostics);

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

        if (!quiet)
        {
            output.WriteLine(
                $"{ProgramName}: '{descriptor.DescriptorPath}' is valid: "
                    + $"{sourceCount} custom-data source(s), {textures.Textures.Count} texture(s), "
                    + $"{descriptor.Types.Count} type mapping(s); no pack written.");
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

    private static int ValidateWithHost(
        string projectPath,
        string hostPath,
        bool verbose,
        bool quiet,
        TextWriter output,
        TextWriter error)
    {
        var options = new BakeOptions(
            projectPath,
            hostPath,
            outputPath: null,
            BakeMode.Debug,
            check: true,
            force: false);
        var diagnostics = new BakeDiagnostics();
        BakeResult result = BakePipeline.Run(options, diagnostics);
        WriteDiagnostics(error, result.Diagnostics);

        if (result.Status == BakeStatus.Success)
        {
            if (!quiet)
            {
                output.WriteLine(
                    $"{ProgramName}: '{projectPath}' is valid: "
                        + $"{result.AssetCount} asset(s) bound, {result.TextureCount} texture(s), "
                        + $"{result.ReferenceCount} reference site(s); no pack written.");
            }

            if (verbose)
            {
                WriteEntryList(output, result.Entries);
            }

            return SuccessExitCode;
        }

        if (!quiet)
        {
            WriteValidateFailureSummary(output, projectPath, hostPath, result);
        }

        return result.Status == BakeStatus.ConfigurationError
            ? UsageExitCode
            : DiagnosticsExitCode;
    }

    private static void WriteEntryList(TextWriter output, IReadOnlyList<BakedAssetEntry> entries)
    {
        foreach (BakedAssetEntry entry in entries)
        {
            string target = entry.TypeName ?? "raw payload";
            output.WriteLine($"  {entry.Path} -> {target} ({entry.Kind})");
        }
    }

    private static void WriteBakeSuccessSummary(
        TextWriter output,
        BakeOptions options,
        BakeResult result)
    {
        if (result.PackWritten)
        {
            string mode = options.Mode == BakeMode.Release ? "release" : "debug";
            output.WriteLine(
                $"{ProgramName}: baked '{result.PackPath}' ({result.AssetCount} asset(s), "
                    + $"{result.TextureCount} texture(s), {result.ReferenceCount} reference(s), "
                    + $"{mode} mode).");
            return;
        }

        output.WriteLine(
            $"{ProgramName}: '{options.ProjectPath}' is valid: {result.AssetCount} asset(s), "
                + $"{result.TextureCount} texture(s), {result.ReferenceCount} reference(s); "
                + "no pack written (--check).");
    }

    private static void WriteUpToDateSummary(
        TextWriter output,
        BakeOptions options,
        BakeResult result)
    {
        string mode = options.Mode == BakeMode.Release ? "release" : "debug";
        output.WriteLine(
            $"{ProgramName}: up to date: '{result.PackPath}' ({result.AssetCount} asset(s), "
                + $"{result.TextureCount} texture(s), {result.ReferenceCount} reference(s), "
                + $"{mode} mode); no inputs changed.");
    }

    private static void WriteBakeFailureSummary(
        TextWriter output,
        BakeOptions options,
        BakeResult result)
    {
        int errors = CountErrors(result.Diagnostics);
        switch (result.FailureStage)
        {
            case BakeFailureStage.Descriptor:
                output.WriteLine(
                    $"{ProgramName}: project descriptor '{options.ProjectPath}' is invalid "
                        + $"({errors} error(s)).");
                break;

            case BakeFailureStage.Host:
                output.WriteLine(
                    $"{ProgramName}: the bake host '{options.HostPath}' could not be loaded or "
                        + $"does not match the project descriptor; regenerate the host ({errors} "
                        + "error(s)).");
                break;

            case BakeFailureStage.Pack:
                output.WriteLine(
                    $"{ProgramName}: failed to write the pack '{result.PackPath}' "
                        + $"({errors} error(s)); the previous pack is unchanged.");
                break;

            default:
                output.WriteLine(
                    $"{ProgramName}: '{options.ProjectPath}' has {errors} error(s); no pack was "
                        + "written.");
                break;
        }
    }

    private static void WriteValidateFailureSummary(
        TextWriter output,
        string projectPath,
        string hostPath,
        BakeResult result)
    {
        int errors = CountErrors(result.Diagnostics);
        switch (result.FailureStage)
        {
            case BakeFailureStage.Descriptor:
                output.WriteLine(
                    $"{ProgramName}: project descriptor '{projectPath}' is invalid "
                        + $"({errors} error(s)).");
                break;

            case BakeFailureStage.Host:
                output.WriteLine(
                    $"{ProgramName}: the bake host '{hostPath}' could not be loaded or does not "
                        + $"match the project descriptor; regenerate the host ({errors} error(s)).");
                break;

            default:
                output.WriteLine(
                    $"{ProgramName}: '{projectPath}' has {errors} source error(s).");
                break;
        }
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
        => WriteDiagnostics(error, diagnostics.GetSorted());

    private static void WriteDiagnostics(TextWriter error, IReadOnlyList<BakeDiagnostic> diagnostics)
    {
        foreach (BakeDiagnostic diagnostic in diagnostics)
        {
            error.WriteLine(diagnostic.Format());
        }
    }

    private static int CountErrors(BakeDiagnostics diagnostics)
        => CountErrors(diagnostics.GetSorted());

    private static int CountErrors(IEnumerable<BakeDiagnostic> diagnostics)
    {
        int count = 0;
        foreach (BakeDiagnostic diagnostic in diagnostics)
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
            "               write the generated bake host project and manifest; skip when the "
                + "recorded inputs and generated files are unchanged");
        writer.WriteLine(
            "  bake --project <bork2d.project.toml> --host <BakeHost.dll> [--output <assets.pck>]");
        writer.WriteLine("       [--mode debug|release] [--check] [--force] [--verbose|--quiet]");
        writer.WriteLine(
            "               run the full bake and atomically write the pack; skip when the "
                + "incremental manifest is current;");
        writer.WriteLine(
            "               --check validates and writes nothing; --force always re-bakes");
        writer.WriteLine(
            "  validate --project <bork2d.project.toml> [--host <BakeHost.dll>] "
                + "[--verbose|--quiet]");
        writer.WriteLine(
            "               without --host: descriptor, texture sources, and TOML syntax;");
        writer.WriteLine(
            "               with --host: the full bake validation (binding + references), no pack");
        writer.WriteLine(
            "exit codes: 0 success, 1 source diagnostics, 2 usage/descriptor/host error, 3 internal error");
    }

    private static string ToolVersion
        => typeof(CliApplication).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? typeof(CliApplication).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";
}
