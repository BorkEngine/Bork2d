using System.Globalization;
using System.Text;
using Bork2d.Bake;

namespace Bork2d.BuiltInAssets;

/// <summary>
/// Build-time generator for the embedded <c>bork2d-builtin.pck</c>.
/// </summary>
/// <remarks>
/// <para>
/// The tool reuses the ordinary bake pipeline, the default processors, and the shared GLSL compiler on
/// the checked-in source tree, then writes the pack as an AOT/trim-safe C# byte array that
/// <c>Bork2d.Runtime</c> compiles into its assembly. It is JIT and build-only: no runtime project
/// references it and neither the tool nor its packages enter a runtime project's output or publish
/// graph.
/// </para>
/// <para>
/// The pack is always written into a unique temporary output directory, so the checked-in source tree
/// is never used as an output location. The bake's incremental state under the source root is removed
/// after the run so a clean clone stays clean; the temporary directory is removed on every exit path.
/// </para>
/// </remarks>
internal static class Program
{
    private const string AssetsDirectoryName = "Assets";
    private const string PackFileName = "bork2d-builtin.pck";
    private const int BytesPerLine = 12;

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine("Bork2d.BuiltInAssets: " + exception.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (Array.IndexOf(args, "--help") >= 0 || Array.IndexOf(args, "-h") >= 0)
        {
            PrintUsage(Console.Out);
            return 0;
        }

        if (!TryParse(args, out string? output, out string? sourceRoot, out int usageExitCode))
        {
            return usageExitCode;
        }

        string fullSourceRoot;
        try
        {
            fullSourceRoot = Path.GetFullPath(sourceRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            Console.Error.WriteLine(
                $"Bork2d.BuiltInAssets: the source root '{sourceRoot}' is not a valid path: "
                    + exception.Message);
            return 1;
        }

        if (!Directory.Exists(Path.Combine(fullSourceRoot, AssetsDirectoryName)))
        {
            Console.Error.WriteLine(
                $"Bork2d.BuiltInAssets: the source root '{fullSourceRoot}' has no "
                    + $"'{AssetsDirectoryName}' directory.");
            return 1;
        }

        string fullOutput;
        try
        {
            fullOutput = Path.GetFullPath(output);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            Console.Error.WriteLine(
                $"Bork2d.BuiltInAssets: the output path '{output}' is not a valid path: "
                    + exception.Message);
            return 1;
        }

        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"bork2d-builtin-{Guid.NewGuid():N}");

        try
        {
            int bakeExitCode = Bake(fullSourceRoot, temporaryRoot);
            if (bakeExitCode != 0)
            {
                Console.Error.WriteLine(
                    $"Bork2d.BuiltInAssets: the built-in pack bake failed with exit code "
                        + $"{bakeExitCode.ToString(CultureInfo.InvariantCulture)}.");
                return 1;
            }

            string packPath = Path.Combine(temporaryRoot, PackFileName);
            if (!File.Exists(packPath))
            {
                Console.Error.WriteLine(
                    "Bork2d.BuiltInAssets: the built-in pack bake reported success but "
                        + $"'{packPath}' was not written.");
                return 1;
            }

            byte[] pack = File.ReadAllBytes(packPath);
            byte[] generated = Generate(pack);
            WriteIfChanged(fullOutput, generated);
            Console.WriteLine(
                $"Bork2d.BuiltInAssets: generated '{fullOutput}' from {pack.Length} pack bytes.");
            return 0;
        }
        finally
        {
            TryDeleteDirectory(temporaryRoot);
            DeleteIncrementalState(fullSourceRoot);
        }
    }

    private static int Bake(string sourceRoot, string temporaryRoot)
    {
        BakeApplication application = new BakeApplicationBuilder()
            .WithProjectRoot(sourceRoot)
            .WithPack(PackFileName, AssetsDirectoryName)
            .UseDefaultProcessors()
            .AddProcessor(new BuiltInVertexProcessor())
            .AddProcessor(new BuiltInPresentProcessor())
            .AddProcessor(new BuiltInPlaceholderProcessor())
            .Build();

        string[] arguments =
        [
            "bake",
            "--project-root",
            sourceRoot,
            "--output-root",
            temporaryRoot,
            "--mode",
            "debug",
            "--force",
        ];

        return application.RunAsync(arguments).GetAwaiter().GetResult();
    }

    private static byte[] Generate(byte[] pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        var builder = new StringBuilder((pack.Length * 6) + 256);
        builder.Append("// <auto-generated/>\n");
        builder.Append("// Generated by Bork2d.BuiltInAssets. Do not edit.\n");
        builder.Append("namespace Bork2d.Runtime.Hosting;\n");
        builder.Append("internal static class BuiltInAssets\n");
        builder.Append("{\n");
        builder.Append("    internal static byte[] PackBytes => _pack;\n");
        builder.Append('\n');
        builder.Append("    private static readonly byte[] _pack =\n");
        builder.Append("    [\n");
        for (var index = 0; index < pack.Length; index++)
        {
            if (index % BytesPerLine == 0)
            {
                builder.Append("        ");
            }

            builder.Append("0x")
                .Append(pack[index].ToString("X2", CultureInfo.InvariantCulture));
            if (index != pack.Length - 1)
            {
                builder.Append(", ");
            }

            if (index % BytesPerLine == BytesPerLine - 1 || index == pack.Length - 1)
            {
                builder.Append('\n');
            }
        }

        builder.Append("    ];\n");
        builder.Append("}\n");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString());
    }

    private static void WriteIfChanged(string output, byte[] generated)
    {
        if (File.Exists(output))
        {
            try
            {
                if (File.ReadAllBytes(output).AsSpan().SequenceEqual(generated))
                {
                    // MSBuild decides whether to run the generator by comparing this file's timestamp
                    // with the tool assembly. Keep the output newer than the inputs without changing
                    // its content, so a no-op generation cannot make the target run forever.
                    File.SetLastWriteTimeUtc(output, DateTime.UtcNow);
                    return;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or NotSupportedException or ArgumentException)
            {
                // Fall through and rewrite the generated output.
                _ = exception;
            }
        }

        string? directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(output, generated);
    }

    private static bool TryParse(
        string[] args,
        out string output,
        out string sourceRoot,
        out int exitCode)
    {
        output = string.Empty;
        sourceRoot = DefaultSourceRoot();
        exitCode = 0;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--output":
                    if (!TryTakeValue(args, ref index, "--output", out output))
                    {
                        exitCode = 2;
                        return false;
                    }

                    break;

                case "--source-root":
                    if (!TryTakeValue(args, ref index, "--source-root", out sourceRoot))
                    {
                        exitCode = 2;
                        return false;
                    }

                    break;

                default:
                    Console.Error.WriteLine(
                        $"Bork2d.BuiltInAssets: unknown argument '{args[index]}'.");
                    PrintUsage(Console.Error);
                    exitCode = 2;
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            Console.Error.WriteLine("Bork2d.BuiltInAssets: --output <file.cs> is required.");
            PrintUsage(Console.Error);
            exitCode = 2;
            return false;
        }

        return true;
    }

    private static bool TryTakeValue(
        string[] args,
        ref int index,
        string option,
        out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Length)
        {
            Console.Error.WriteLine($"Bork2d.BuiltInAssets: '{option}' requires a value.");
            return false;
        }

        value = args[++index];
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine($"Bork2d.BuiltInAssets: '{option}' requires a non-empty value.");
            return false;
        }

        return true;
    }

    private static string DefaultSourceRoot()
        => Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "Bork2d",
                "BuiltInAssets"));

    private static void DeleteIncrementalState(string sourceRoot)
    {
        // The bake records a manifest under <project root>/obj even for --force runs. This tool owns
        // the built-in source root and always bakes into a temporary output, so that state cannot be
        // reused; remove it to keep the checked-in tree free of generated files.
        TryDeleteDirectory(Path.Combine(sourceRoot, "obj", "bork2d", "bake"));
        TryDeleteEmptyDirectory(Path.Combine(sourceRoot, "obj", "bork2d"));
        TryDeleteEmptyDirectory(Path.Combine(sourceRoot, "obj"));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            Console.Error.WriteLine(
                $"Bork2d.BuiltInAssets: could not remove '{path}': {exception.Message}");
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
            {
                Directory.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            Console.Error.WriteLine(
                $"Bork2d.BuiltInAssets: could not remove '{path}': {exception.Message}");
        }
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine(
            "Usage: Bork2d.BuiltInAssets --output <file.cs> [--source-root <dir>]");
        writer.WriteLine(
            "  --output       generated C# file that embeds bork2d-builtin.pck");
        writer.WriteLine(
            "  --source-root  built-in asset project root (default: src/Bork2d/BuiltInAssets)");
    }
}
