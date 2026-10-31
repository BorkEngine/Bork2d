using System.Globalization;
using System.Text;
using Silk.NET.Shaderc;

namespace Bork2d.ShaderCompiler;

/// <summary>
/// Build-time GLSL to SPIR-V compiler for the Bork2d sprite pipeline.
/// </summary>
/// <remarks>
/// This tool is JIT, build-only, and deliberately outside the runtime/AOT graph. It emits a generated
/// C# file with the SPIR-V byte arrays; no shader I/O or compilation happens at runtime.
/// </remarks>
internal static class Program
{
    private const string GeneratedClassName = "SpriteShaderSpirV";
    private const string GeneratedNamespace = "Bork2d.Render.Vulkan";
    private const int BytesPerLine = 12;

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Bork2d.ShaderCompiler: " + exception.Message);
            return 1;
        }
    }

    private static unsafe int Run(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "compile", StringComparison.Ordinal))
        {
            PrintUsage();
            return 2;
        }

        string? output = null;
        string? vertex = null;
        string? fragment = null;
        var optimize = false;
        for (var index = 1; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--output":
                    output = NextValue(args, ref index, argument);
                    break;

                case "--vertex":
                    vertex = NextValue(args, ref index, argument);
                    break;

                case "--fragment":
                    fragment = NextValue(args, ref index, argument);
                    break;

                case "--optimize":
                    optimize = true;
                    break;

                case "--debug":
                    optimize = false;
                    break;

                default:
                    Console.Error.WriteLine(
                        $"Bork2d.ShaderCompiler: unknown argument '{argument}'.");
                    PrintUsage();
                    return 2;
            }
        }

        if (string.IsNullOrWhiteSpace(output)
            || string.IsNullOrWhiteSpace(vertex)
            || string.IsNullOrWhiteSpace(fragment))
        {
            Console.Error.WriteLine(
                "Bork2d.ShaderCompiler: --output, --vertex, and --fragment are required.");
            PrintUsage();
            return 2;
        }

        foreach (string source in new[] { vertex, fragment })
        {
            if (!File.Exists(source))
            {
                Console.Error.WriteLine($"Bork2d.ShaderCompiler: shader source not found: {source}");
                return 1;
            }
        }

        Shaderc shaderc = Shaderc.GetApi();
        Compiler* compiler = null;
        CompileOptions* options = null;
        try
        {
            compiler = shaderc.CompilerInitialize();
            options = shaderc.CompileOptionsInitialize();
            if (compiler is null || options is null)
            {
                throw new InvalidOperationException("Shaderc could not create a compiler.");
            }

            shaderc.CompileOptionsSetSourceLanguage(options, SourceLanguage.Glsl);
            shaderc.CompileOptionsSetTargetEnv(
                options,
                TargetEnv.Vulkan,
                (uint)EnvVersion.Vulkan13);
            shaderc.CompileOptionsSetOptimizationLevel(
                options,
                optimize ? OptimizationLevel.Performance : OptimizationLevel.Zero);
            shaderc.CompileOptionsSetWarningsAsErrors(options);
            if (!optimize)
            {
                shaderc.CompileOptionsSetGenerateDebugInfo(options);
            }

            byte[] vertexSpirV = Compile(
                shaderc,
                compiler,
                options,
                vertex,
                ShaderKind.GlslVertexShader);
            byte[] fragmentSpirV = Compile(
                shaderc,
                compiler,
                options,
                fragment,
                ShaderKind.GlslFragmentShader);

            string generated = Generate(
                output,
                vertex,
                fragment,
                vertexSpirV,
                fragmentSpirV,
                optimize);
            string? directory = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                output,
                generated,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine(
                $"Bork2d.ShaderCompiler: compiled '{Path.GetFileName(vertex)}' "
                    + $"({vertexSpirV.Length} bytes) and '{Path.GetFileName(fragment)}' "
                    + $"({fragmentSpirV.Length} bytes) -> {output}.");
            return 0;
        }
        finally
        {
            if (options is not null)
            {
                shaderc.CompileOptionsRelease(options);
            }

            if (compiler is not null)
            {
                shaderc.CompilerRelease(compiler);
            }

            shaderc.Dispose();
        }
    }

    private static unsafe byte[] Compile(
        Shaderc shaderc,
        Compiler* compiler,
        CompileOptions* options,
        string sourcePath,
        ShaderKind kind)
    {
        string source = File.ReadAllText(sourcePath);
        string fileName = Path.GetFileName(sourcePath);
        CompilationResult* result = shaderc.CompileIntoSpv(
            compiler,
            source,
            (nuint)source.Length,
            kind,
            fileName,
            "main",
            options);
        if (result is null)
        {
            throw new InvalidOperationException($"Shaderc returned no result for '{fileName}'.");
        }

        try
        {
            CompilationStatus status = shaderc.ResultGetCompilationStatus(result);
            string message = shaderc.ResultGetErrorMessageS(result) ?? string.Empty;
            if (status != CompilationStatus.Success)
            {
                throw new InvalidOperationException(
                    $"Shader compilation failed for '{fileName}' with {status}:{Environment.NewLine}"
                        + message);
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                Console.Error.WriteLine(
                    $"Bork2d.ShaderCompiler: warning in '{fileName}':{Environment.NewLine}{message}");
            }

            nuint length = shaderc.ResultGetLength(result);
            byte* bytes = shaderc.ResultGetBytes(result);
            if (bytes is null || length == 0)
            {
                throw new InvalidOperationException($"Shader '{fileName}' produced no SPIR-V.");
            }

            return new ReadOnlySpan<byte>(bytes, checked((int)length)).ToArray();
        }
        finally
        {
            shaderc.ResultRelease(result);
        }
    }

    private static string Generate(
        string output,
        string vertexPath,
        string fragmentPath,
        byte[] vertexSpirV,
        byte[] fragmentSpirV,
        bool optimize)
    {
        var builder = new StringBuilder();
        builder.Append("// <auto-generated/>\n");
        builder.Append("// Generated by Bork2d.ShaderCompiler from the checked-in GLSL. Do not edit.\n");
        builder.Append("//\n");
        builder.Append("// Sources: ").Append(Path.GetFileName(vertexPath)).Append(", ")
            .Append(Path.GetFileName(fragmentPath)).Append('\n');
        builder.Append("// Target: Vulkan 1.3 SPIR-V, ")
            .Append(optimize ? "optimized" : "debug")
            .Append(".\n");
        builder.Append("//\n");
        builder.Append("// Regenerated by the Bork2d.Render shader build target; never committed and\n");
        builder.Append("// never published as a loose file.\n");
        builder.Append('\n');
        builder.Append("#nullable enable\n");
        builder.Append('\n');
        builder.Append("namespace ").Append(GeneratedNamespace).Append(";\n");
        builder.Append('\n');
        builder.Append("/// <summary>SPIR-V modules for the Bork2d sprite pipeline.</summary>\n");
        builder.Append("internal static class ").Append(GeneratedClassName).Append('\n');
        builder.Append("{\n");
        AppendArray(builder, "Vertex", vertexSpirV);
        builder.Append('\n');
        AppendArray(builder, "Fragment", fragmentSpirV);
        builder.Append("}\n");
        return builder.ToString();
    }

    private static void AppendArray(StringBuilder builder, string name, byte[] data)
    {
        builder.Append("    internal static readonly byte[] ").Append(name).Append(" =\n");
        builder.Append("    [\n");
        for (var index = 0; index < data.Length; index++)
        {
            if (index % BytesPerLine == 0)
            {
                builder.Append("        ");
            }

            builder.Append("0x")
                .Append(data[index].ToString("X2", CultureInfo.InvariantCulture));
            if (index != data.Length - 1)
            {
                builder.Append(", ");
            }

            if (index % BytesPerLine == BytesPerLine - 1 || index == data.Length - 1)
            {
                builder.Append('\n');
            }
        }

        builder.Append("    ];\n");
    }

    private static string NextValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"'{option}' requires a value.");
        }

        return args[++index];
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage: Bork2d.ShaderCompiler compile --output <file.cs> --vertex <sprite.vert> "
                + "--fragment <sprite.frag> [--optimize|--debug]");
    }
}
