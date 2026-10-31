using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Incremental;

namespace Bork2d.Bake.Commands;

/// <summary>
/// The internal command surface shared by every assets executable: argument parsing, configuration
/// validation, dispatch, exit codes, and stdout/stderr routing.
/// </summary>
/// <remarks>
/// <para>
/// Commands write diagnostics to <c>stderr</c> in the canonical <c>path(line,col): error BKxxxx:</c>
/// form and the human summary to <c>stdout</c>. Exit codes are <c>0</c> success/up to date, <c>1</c>
/// source diagnostics, <c>2</c> usage/configuration error, and <c>3</c> an unexpected internal failure.
/// </para>
/// <para>
/// <c>bake</c> runs the incremental planner: it writes every configured pack atomically under the
/// output root (default <see cref="AppContext.BaseDirectory"/>) or skips the bake as up to date when
/// the schema-3 manifest and every output match. <c>--check</c> always validates and writes nothing;
/// <c>--force</c> bypasses the skip. <c>validate</c> runs the same pipeline in check mode without
/// writing or skipping. <c>clean</c> deletes the configured pack outputs and the incremental bake
/// state. <c>copy-packs</c> copies each configured pack from next to the assets executable to
/// <c>--output-root</c>.
/// </para>
/// </remarks>
internal static class BakeCommandLine
{
    /// <summary>The success exit code.</summary>
    internal const int SuccessExitCode = 0;

    /// <summary>One or more source diagnostics failed the command.</summary>
    internal const int DiagnosticsExitCode = 1;

    /// <summary>The invocation or bake configuration is invalid.</summary>
    internal const int UsageExitCode = 2;

    /// <summary>An unexpected internal failure occurred.</summary>
    internal const int InternalExitCode = 3;

    /// <summary>Runs one command.</summary>
    /// <param name="definition">The builder-side configuration.</param>
    /// <param name="args">The arguments, excluding the executable name.</param>
    /// <param name="output">The summary stream (normally <c>stdout</c>).</param>
    /// <param name="error">The diagnostics stream (normally <c>stderr</c>).</param>
    /// <param name="programName">The CLI prefix, normally the entry assembly name.</param>
    /// <returns>The documented process exit code.</returns>
    internal static int Run(
        BakeProjectDefinition definition,
        string[] args,
        TextWriter output,
        TextWriter error,
        string programName)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(programName);

        bool verboseRequested = Array.IndexOf(args, "--verbose") >= 0;

        try
        {
            if (args.Length == 0)
            {
                error.WriteLine($"{programName}: a command is required.");
                WriteUsage(error, programName);
                return UsageExitCode;
            }

            switch (args[0])
            {
                case "--help":
                case "-h":
                    WriteUsage(output, programName);
                    return SuccessExitCode;

                case "--version":
                    output.WriteLine($"{programName} {ToolVersion}");
                    return SuccessExitCode;

                case "bake":
                    return RunBake(definition, args.AsSpan(1), output, error, programName);

                case "validate":
                    return RunValidate(definition, args.AsSpan(1), output, error, programName);

                case "clean":
                    return RunClean(definition, args.AsSpan(1), output, error, programName);

                case "copy-packs":
                    return RunCopyPacks(definition, args.AsSpan(1), output, error, programName);

                default:
                    error.WriteLine($"{programName}: unknown command '{args[0]}'.");
                    WriteUsage(error, programName);
                    return UsageExitCode;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            error.WriteLine(verboseRequested
                ? $"{programName}: internal error: {exception}"
                : $"{programName}: internal error: {exception.Message}");
            return InternalExitCode;
        }
    }

    private static int RunBake(
        BakeProjectDefinition definition,
        ReadOnlySpan<string> args,
        TextWriter output,
        TextWriter error,
        string programName)
        => RunPipeline(
            definition,
            args,
            output,
            error,
            programName,
            "bake",
            allowCheck: true,
            allowForce: true,
            allowOutputRoot: true,
            allowMode: true,
            requireOutputRoot: false,
            forceCheck: false,
            incremental: true);

    private static int RunValidate(
        BakeProjectDefinition definition,
        ReadOnlySpan<string> args,
        TextWriter output,
        TextWriter error,
        string programName)
        => RunPipeline(
            definition,
            args,
            output,
            error,
            programName,
            "validate",
            allowCheck: false,
            allowForce: false,
            allowOutputRoot: false,
            allowMode: true,
            requireOutputRoot: false,
            forceCheck: true,
            incremental: false);

    private static int RunPipeline(
        BakeProjectDefinition definition,
        ReadOnlySpan<string> args,
        TextWriter output,
        TextWriter error,
        string programName,
        string command,
        bool allowCheck,
        bool allowForce,
        bool allowOutputRoot,
        bool allowMode,
        bool requireOutputRoot,
        bool forceCheck,
        bool incremental)
    {
        if (!TryParse(
            args,
            command,
            allowCheck,
            allowForce,
            allowOutputRoot,
            allowMode,
            requireOutputRoot,
            out CommandOptions options,
            out string? parseError))
        {
            error.WriteLine($"{programName}: {parseError}");
            WriteUsage(error, programName);
            return UsageExitCode;
        }

        if (!TryResolveProjectRoot(options, error, programName, out string? projectRoot))
        {
            return UsageExitCode;
        }

        if (!TryResolveOutputRoot(options.OutputRoot, error, programName, out string outputRoot))
        {
            return UsageExitCode;
        }

        var diagnostics = new BakeDiagnostics();
        if (!BakeProjectFactory.TryCreate(definition, projectRoot, diagnostics, out BakeProject? project))
        {
            WriteDiagnostics(error, diagnostics);
            if (!options.Quiet)
            {
                output.WriteLine(
                    $"{programName}: the bake configuration is invalid "
                        + $"({CountErrors(diagnostics)} error(s)).");
            }

            return UsageExitCode;
        }

        bool check = forceCheck || options.Check;
        var bakeOptions = new BakeOptions(outputRoot, options.Mode, check, options.Force);
        BakeResult result = incremental
            ? BakeIncrementalPlanner.Run(project, bakeOptions, diagnostics)
            : BakePipeline.Run(project, bakeOptions, diagnostics);
        WriteDiagnostics(error, result.Diagnostics);

        if (result.Status is BakeStatus.Success or BakeStatus.UpToDate)
        {
            if (!options.Quiet)
            {
                WriteSuccessSummary(output, programName, command, result, options.Mode, check);
            }

            if (options.Verbose)
            {
                WriteEntryList(output, result.Entries);
            }

            return SuccessExitCode;
        }

        if (!options.Quiet)
        {
            WriteFailureSummary(output, programName, result);
        }

        return result.Status == BakeStatus.ConfigurationError
            ? UsageExitCode
            : DiagnosticsExitCode;
    }

    private static int RunClean(
        BakeProjectDefinition definition,
        ReadOnlySpan<string> args,
        TextWriter output,
        TextWriter error,
        string programName)
    {
        if (!TryParse(
            args,
            "clean",
            allowCheck: false,
            allowForce: false,
            allowOutputRoot: true,
            allowMode: false,
            requireOutputRoot: false,
            out CommandOptions options,
            out string? parseError))
        {
            error.WriteLine($"{programName}: {parseError}");
            WriteUsage(error, programName);
            return UsageExitCode;
        }

        if (!TryResolveProjectRoot(options, error, programName, out string? projectRoot)
            || !TryResolveOutputRoot(options.OutputRoot, error, programName, out string outputRoot))
        {
            return UsageExitCode;
        }

        var diagnostics = new BakeDiagnostics();
        if (!BakeProjectFactory.TryCreate(definition, projectRoot, diagnostics, out BakeProject? project))
        {
            WriteDiagnostics(error, diagnostics);
            if (!options.Quiet)
            {
                output.WriteLine(
                    $"{programName}: the bake configuration is invalid "
                        + $"({CountErrors(diagnostics)} error(s)).");
            }

            return UsageExitCode;
        }

        if (!BakeOutputLayout.TryResolve(
            project,
            outputRoot,
            diagnostics,
            out string root,
            out IReadOnlyList<BakePackOutput> outputs))
        {
            WriteDiagnostics(error, diagnostics);
            if (!options.Quiet)
            {
                output.WriteLine($"{programName}: the output configuration is invalid.");
            }

            return UsageExitCode;
        }

        int deleted = 0;
        foreach (BakePackOutput packOutput in outputs)
        {
            try
            {
                if (File.Exists(packOutput.Path))
                {
                    File.Delete(packOutput.Path);
                    deleted++;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or NotSupportedException or ArgumentException)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.PackWriteFailed,
                    $"Failed to delete the pack '{packOutput.Path}': {exception.Message}",
                    packOutput.Path);
            }
        }

        bool hadIncrementalState = Directory.Exists(
            BakeManifestPaths.Directory(project.ProjectRoot));
        if (!TryDeleteIncrementalState(project.ProjectRoot, diagnostics))
        {
            if (!options.Quiet)
            {
                output.WriteLine($"{programName}: failed to clean the incremental bake state.");
            }

            WriteDiagnostics(error, diagnostics);
            return DiagnosticsExitCode;
        }

        if (diagnostics.HasErrors)
        {
            WriteDiagnostics(error, diagnostics);
            if (!options.Quiet)
            {
                output.WriteLine($"{programName}: failed to clean every configured pack.");
            }

            return DiagnosticsExitCode;
        }

        if (!options.Quiet)
        {
            string state = hadIncrementalState
                ? "and the incremental manifest directory"
                : "(no incremental manifest was present)";
            output.WriteLine(
                $"{programName}: cleaned {deleted} pack(s) under '{root}' {state}.");
        }

        return SuccessExitCode;
    }

    private static bool TryDeleteIncrementalState(
        string projectRoot,
        BakeDiagnostics diagnostics)
    {
        string directory = BakeManifestPaths.Directory(projectRoot);
        if (!Directory.Exists(directory))
        {
            return true;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.PackWriteFailed,
                $"Failed to delete the incremental bake state '{directory}': {exception.Message}",
                directory);
            return false;
        }
    }

    private static int RunCopyPacks(
        BakeProjectDefinition definition,
        ReadOnlySpan<string> args,
        TextWriter output,
        TextWriter error,
        string programName)
    {
        if (!TryParse(
            args,
            "copy-packs",
            allowCheck: false,
            allowForce: false,
            allowOutputRoot: true,
            allowMode: false,
            requireOutputRoot: true,
            out CommandOptions options,
            out string? parseError))
        {
            error.WriteLine($"{programName}: {parseError}");
            WriteUsage(error, programName);
            return UsageExitCode;
        }

        if (!TryResolveProjectRoot(options, error, programName, out string? projectRoot)
            || !TryResolveOutputRoot(options.OutputRoot, error, programName, out string outputRoot))
        {
            return UsageExitCode;
        }

        var diagnostics = new BakeDiagnostics();
        if (!BakeProjectFactory.TryCreate(definition, projectRoot, diagnostics, out BakeProject? project))
        {
            WriteDiagnostics(error, diagnostics);
            if (!options.Quiet)
            {
                output.WriteLine(
                    $"{programName}: the bake configuration is invalid "
                        + $"({CountErrors(diagnostics)} error(s)).");
            }

            return UsageExitCode;
        }

        int copied = 0;
        foreach (BakePack pack in project.Packs)
        {
            string sourcePath = Path.Combine(
                AppContext.BaseDirectory,
                pack.PackPath.Replace('/', Path.DirectorySeparatorChar));
            string destinationPath = Path.Combine(
                outputRoot,
                pack.PackPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(sourcePath))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.PackCopyFailed,
                    $"The configured pack '{pack.PackPath}' was not found next to the assets "
                        + $"executable ('{sourcePath}'); build the assets executable first.",
                    sourcePath);
                continue;
            }

            try
            {
                string? directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.Copy(sourcePath, destinationPath, overwrite: true);
                copied++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or NotSupportedException or ArgumentException)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.PackCopyFailed,
                    $"Failed to copy the pack '{sourcePath}' to '{destinationPath}': "
                        + exception.Message,
                    sourcePath);
            }
        }

        if (diagnostics.HasErrors)
        {
            WriteDiagnostics(error, diagnostics);
            if (!options.Quiet)
            {
                output.WriteLine($"{programName}: failed to copy every configured pack.");
            }

            return DiagnosticsExitCode;
        }

        if (!options.Quiet)
        {
            string root = Path.GetFullPath(outputRoot);
            output.WriteLine(
                $"{programName}: copied {copied} pack(s) to '{root}'.");
        }

        return SuccessExitCode;
    }

    private static bool TryResolveProjectRoot(
        CommandOptions options,
        TextWriter error,
        string programName,
        out string? projectRoot)
    {
        projectRoot = null;
        if (string.IsNullOrWhiteSpace(options.ProjectRoot))
        {
            return true;
        }

        if (!TryResolvePath(options.ProjectRoot, "--project-root", error, programName, out string resolved))
        {
            return false;
        }

        projectRoot = resolved;
        return true;
    }

    private static bool TryResolveOutputRoot(
        string? value,
        TextWriter error,
        string programName,
        out string outputRoot)
    {
        string requested = string.IsNullOrWhiteSpace(value)
            ? AppContext.BaseDirectory
            : value;

        return TryResolvePath(requested, "--output-root", error, programName, out outputRoot);
    }

    private static bool TryResolvePath(
        string value,
        string optionName,
        TextWriter error,
        string programName,
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
            error.WriteLine(
                $"{programName}: {optionName} '{value}' is not a valid path: {exception.Message}");
            fullPath = string.Empty;
            return false;
        }
    }

    private static bool TryParse(
        ReadOnlySpan<string> args,
        string command,
        bool allowCheck,
        bool allowForce,
        bool allowOutputRoot,
        bool allowMode,
        bool requireOutputRoot,
        out CommandOptions options,
        out string? error)
    {
        options = new CommandOptions();
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            string argument = args[i];
            switch (argument)
            {
                case "--project-root":
                    if (!TryTakeValue(args, ref i, command, argument, out string? projectRoot, out error))
                    {
                        return false;
                    }

                    if (options.ProjectRoot is not null)
                    {
                        error = $"{command} accepts --project-root only once.";
                        return false;
                    }

                    options.ProjectRoot = projectRoot;
                    break;

                case "--output-root" when allowOutputRoot:
                    if (!TryTakeValue(args, ref i, command, argument, out string? outputRoot, out error))
                    {
                        return false;
                    }

                    if (options.OutputRoot is not null)
                    {
                        error = $"{command} accepts --output-root only once.";
                        return false;
                    }

                    options.OutputRoot = outputRoot;
                    break;

                case "--mode" when allowMode:
                    if (!TryTakeValue(args, ref i, command, argument, out string? mode, out error))
                    {
                        return false;
                    }

                    if (!BakeModeExtensions.TryParse(mode, out BakeMode parsed))
                    {
                        error = $"--mode must be 'debug' or 'release'; got '{mode}'.";
                        return false;
                    }

                    options.Mode = parsed;
                    break;

                case "--check" when allowCheck:
                    options.Check = true;
                    break;

                case "--force" when allowForce:
                    options.Force = true;
                    break;

                case "--verbose":
                    options.Verbose = true;
                    break;

                case "--quiet":
                    options.Quiet = true;
                    break;

                default:
                    error = $"{command} does not accept '{argument}'.";
                    return false;
            }
        }

        if (options.Verbose && options.Quiet)
        {
            error = "--verbose and --quiet are mutually exclusive.";
            return false;
        }

        if (requireOutputRoot && string.IsNullOrWhiteSpace(options.OutputRoot))
        {
            error = $"{command} requires --output-root <dir>.";
            return false;
        }

        return true;
    }

    private static bool TryTakeValue(
        ReadOnlySpan<string> args,
        ref int index,
        string command,
        string optionName,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;

        if (index + 1 >= args.Length)
        {
            error = $"{command} requires a value after {optionName}.";
            return false;
        }

        value = args[++index];
        return true;
    }

    private static void WriteSuccessSummary(
        TextWriter output,
        string programName,
        string command,
        BakeResult result,
        BakeMode mode,
        bool check)
    {
        string packs = DescribePacks(result.Packs);

        if (result.Status == BakeStatus.UpToDate)
        {
            output.WriteLine(
                $"{programName}: up to date: {packs}; no inputs changed.");
            return;
        }

        string modeName = mode.ToName();

        if (check)
        {
            output.WriteLine(
                $"{programName}: {(command == "validate" ? "valid" : "valid (--check)")}: "
                    + $"{packs} ({result.Packs.Count} pack(s), {result.AssetCount} asset(s), "
                    + $"{result.TextureCount} texture(s), {result.ReferenceCount} reference(s)); no "
                    + "pack written.");
            return;
        }

        output.WriteLine(
            $"{programName}: baked {packs} ({result.Packs.Count} pack(s), {result.AssetCount} "
                + $"asset(s), {result.TextureCount} texture(s), {result.ReferenceCount} "
                + $"reference(s), {modeName} mode).");
    }

    private static void WriteFailureSummary(
        TextWriter output,
        string programName,
        BakeResult result)
    {
        int errors = CountErrors(result.Diagnostics);
        switch (result.FailureStage)
        {
            case BakeFailureStage.Configuration:
                output.WriteLine(
                    $"{programName}: the output configuration is invalid ({errors} error(s)).");
                break;

            case BakeFailureStage.Pack:
                output.WriteLine(
                    $"{programName}: failed to write the pack '{result.PackPath}' ({errors} "
                        + "error(s)); the previous pack(s) are unchanged.");
                break;

            default:
                output.WriteLine(
                    $"{programName}: the bake has {errors} error(s); no pack was written.");
                break;
        }
    }

    private static void WriteEntryList(TextWriter output, IReadOnlyList<BakedAssetEntry> entries)
    {
        foreach (BakedAssetEntry entry in entries)
        {
            string target = entry.TypeName ?? "raw payload";
            output.WriteLine($"  {entry.Path} -> {target} ({entry.Kind})");
        }
    }

    private static string DescribePacks(IReadOnlyList<BakedPackResult> packs)
        => string.Join(", ", packs.Select(pack => $"'{pack.PackPath}'"));

    private static void WriteDiagnostics(TextWriter error, BakeDiagnostics diagnostics)
        => WriteDiagnostics(error, diagnostics.GetSorted());

    private static void WriteDiagnostics(TextWriter error, IReadOnlyList<BakeDiagnostic> diagnostics)
    {
        foreach (BakeDiagnostic diagnostic in diagnostics)
        {
            error.WriteLine(diagnostic.Format());
        }
    }

    private static int CountErrors(BakeDiagnostics diagnostics) => CountErrors(diagnostics.GetSorted());

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

    private static void WriteUsage(TextWriter writer, string programName)
    {
        writer.WriteLine($"usage: {programName} <command> [options]");
        writer.WriteLine("  --help       show this help");
        writer.WriteLine("  --version    show the tool version");
        writer.WriteLine(
            "  bake      [--project-root <dir>] [--output-root <dir>] [--mode debug|release]");
        writer.WriteLine("            [--check] [--force] [--verbose|--quiet]");
        writer.WriteLine(
            "            bake every configured pack (the first is the main pack) and atomically write "
                + "each pack under --output-root");
        writer.WriteLine(
            "            (default: the assets executable directory); --check validates and writes "
                + "nothing");
        writer.WriteLine("  validate  [--project-root <dir>] [--mode debug|release] [--verbose|--quiet]");
        writer.WriteLine("            run the full bake validation without writing a pack");
        writer.WriteLine("  clean     [--project-root <dir>] [--output-root <dir>] [--verbose|--quiet]");
        writer.WriteLine("            delete every configured pack output");
        writer.WriteLine(
            "  copy-packs --output-root <dir> [--project-root <dir>] [--verbose|--quiet]");
        writer.WriteLine(
            "            copy every configured pack from next to the assets executable to "
                + "--output-root");
        writer.WriteLine(
            "exit codes: 0 success, 1 source diagnostics, 2 usage/configuration error, 3 internal error");
    }

    private static string ToolVersion => BakeToolInfo.Version;
}
