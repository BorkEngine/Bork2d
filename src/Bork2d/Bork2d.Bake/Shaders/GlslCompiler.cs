using System.Text;
using Silk.NET.Shaderc;

namespace Bork2d.Bake.Shaders;

/// <summary>
/// The reusable GLSL-to-SPIR-V compiler for the bake tooling, built directly on Silk.NET.Shaderc.
/// </summary>
/// <remarks>
/// <para>
/// The helper owns the shaderc setup for every bake shader source: GLSL source language, Vulkan 1.3
/// target environment, performance optimization, warnings as errors, and the caller's entry point.
/// <see cref="TryCompile"/> is the general entry point (source text, file name,
/// <see cref="ShaderKind"/>, entry point); <see cref="TryCompileFragment"/> and
/// <see cref="TryCompileVertex"/> cover the common <c>main</c> pairs. The built-in pack tool compiles
/// the engine-owned vertex shader through the same helper, so shaderc setup exists in exactly one
/// place.
/// </para>
/// <para>
/// The helper is internal to the bake, the bake is JIT, and no runtime project references
/// Silk.NET.Shaderc. A failed compilation returns the shaderc diagnostic text verbatim so the caller
/// can report it with source locations; only an environment failure such as a missing native library
/// is folded into the same message instead of throwing.
/// </para>
/// </remarks>
internal static unsafe class GlslCompiler
{
    /// <summary>Compiles one fragment source with entry point <c>main</c>.</summary>
    /// <param name="source">The complete GLSL source text.</param>
    /// <param name="fileName">The file name shaderc uses in diagnostics.</param>
    /// <param name="spirV">The compiled module when compilation succeeds.</param>
    /// <param name="message">The shaderc diagnostic text when compilation fails.</param>
    /// <returns><see langword="true"/> when the source compiled.</returns>
    internal static bool TryCompileFragment(
        string source,
        string fileName,
        out byte[] spirV,
        out string message)
        => TryCompile(
            source,
            fileName,
            ShaderKind.GlslFragmentShader,
            "main",
            out spirV,
            out message);

    /// <summary>Compiles one vertex source with entry point <c>main</c>.</summary>
    /// <param name="source">The complete GLSL source text.</param>
    /// <param name="fileName">The file name shaderc uses in diagnostics.</param>
    /// <param name="spirV">The compiled module when compilation succeeds.</param>
    /// <param name="message">The shaderc diagnostic text when compilation fails.</param>
    /// <returns><see langword="true"/> when the source compiled.</returns>
    internal static bool TryCompileVertex(
        string source,
        string fileName,
        out byte[] spirV,
        out string message)
        => TryCompile(
            source,
            fileName,
            ShaderKind.GlslVertexShader,
            "main",
            out spirV,
            out message);

    /// <summary>Compiles one GLSL source into a SPIR-V module.</summary>
    /// <param name="source">The complete GLSL source text.</param>
    /// <param name="fileName">The file name shaderc uses in diagnostics.</param>
    /// <param name="kind">The shader stage kind.</param>
    /// <param name="entryPoint">The entry point name.</param>
    /// <param name="spirV">The compiled module when compilation succeeds.</param>
    /// <param name="message">
    /// The shaderc diagnostic text for a rejected source, or the environment failure description.
    /// </param>
    /// <returns><see langword="true"/> when the source compiled.</returns>
    internal static bool TryCompile(
        string source,
        string fileName,
        ShaderKind kind,
        string entryPoint,
        out byte[] spirV,
        out string message)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);

        spirV = [];
        message = string.Empty;

        Shaderc? shaderc = null;
        Compiler* compiler = null;
        CompileOptions* options = null;
        CompilationResult* result = null;
        try
        {
            shaderc = Shaderc.GetApi();
            compiler = shaderc.CompilerInitialize();
            options = shaderc.CompileOptionsInitialize();
            if (compiler is null || options is null)
            {
                message = "Shaderc could not create a compiler.";
                return false;
            }

            shaderc.CompileOptionsSetSourceLanguage(options, SourceLanguage.Glsl);
            shaderc.CompileOptionsSetTargetEnv(
                options,
                TargetEnv.Vulkan,
                (uint)EnvVersion.Vulkan13);
            shaderc.CompileOptionsSetOptimizationLevel(options, OptimizationLevel.Performance);
            shaderc.CompileOptionsSetWarningsAsErrors(options);

            result = shaderc.CompileIntoSpv(
                compiler,
                source,
                (nuint)Encoding.UTF8.GetByteCount(source),
                kind,
                fileName,
                entryPoint,
                options);
            if (result is null)
            {
                message = $"Shaderc returned no result for '{fileName}'.";
                return false;
            }

            CompilationStatus status = shaderc.ResultGetCompilationStatus(result);
            string diagnostics = shaderc.ResultGetErrorMessageS(result) ?? string.Empty;
            if (status != CompilationStatus.Success)
            {
                message = string.IsNullOrWhiteSpace(diagnostics)
                    ? $"Shader compilation failed for '{fileName}' with {status}."
                    : diagnostics.Trim();
                return false;
            }

            nuint length = shaderc.ResultGetLength(result);
            byte* bytes = shaderc.ResultGetBytes(result);
            if (bytes is null || length == 0)
            {
                message = $"Shader '{fileName}' produced no SPIR-V.";
                return false;
            }

            spirV = new ReadOnlySpan<byte>(bytes, checked((int)length)).ToArray();
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            message = $"Shaderc failed to compile '{fileName}': {exception.Message}";
            return false;
        }
        finally
        {
            if (shaderc is not null)
            {
                if (result is not null)
                {
                    shaderc.ResultRelease(result);
                }

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
    }
}
