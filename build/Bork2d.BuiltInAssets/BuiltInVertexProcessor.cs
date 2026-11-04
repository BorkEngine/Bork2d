using System.Text;
using Bork2d.Bake;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Shaders;
using PolyType;

namespace Bork2d.BuiltInAssets;

/// <summary>
/// The tool-only processor that compiles the engine-owned sprite and line vertex stages into the
/// built-in pack's raw SPIR-V payloads.
/// </summary>
/// <remarks>
/// <para>
/// The processor is deliberately not part of <c>UseDefaultProcessors()</c>: <c>.vert</c> is not a
/// user-facing source type. Only this build-time tool registers it, so the built-in pack is produced
/// by the same bake pipeline, pack writer, and shared <see cref="GlslCompiler"/> as game content.
/// Every <c>.vert</c> source under the built-in asset tree is compiled, so the sprite stage emits
/// <c>shader/vertex-spv</c> and the line stage emits <c>shader/line-vertex-spv</c>.
/// </para>
/// <para>
/// A source is read as strict UTF-8 (an optional BOM is stripped) and compiled verbatim with the
/// <c>main</c> entry point; the engine owns this stage, so there is no user prologue. It emits one raw
/// payload at <c>&lt;base&gt;-spv</c> and no <c>ShaderAsset</c>.
/// </para>
/// </remarks>
internal sealed class BuiltInVertexProcessor : IAssetProcessor
{
    private const string PayloadSuffix = "-spv";

    private static readonly string[] _extensions = ["vert"];

    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <inheritdoc/>
    public string Name => "builtin-vertex";

    /// <inheritdoc/>
    public IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    public SidecarPolicy Policy => SidecarPolicy.None;

    /// <inheritdoc/>
    public ITypeShape? SidecarShape => null;

    /// <inheritdoc/>
    public void Process(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryReadSource(context, out string source))
        {
            return;
        }

        if (!GlslCompiler.TryCompileVertex(
                source,
                context.SourceFileName,
                out byte[] spirV,
                out string message))
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ShaderCompileFailed,
                $"Failed to compile the engine-owned vertex shader '{context.RelativePath}'. "
                    + message.Trim());
            return;
        }

        context.Outputs.AddData(context.BaseAssetPath + PayloadSuffix, spirV);
    }

    private static bool TryReadSource(AssetProcessorContext context, out string source)
    {
        source = string.Empty;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(context.SourcePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ProcessorSourceRead,
                $"Failed to read the built-in vertex shader '{context.RelativePath}': "
                    + exception.Message);
            return false;
        }

        ReadOnlySpan<byte> content = bytes;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            content = content[3..];
        }

        try
        {
            source = _strictUtf8.GetString(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            context.Diagnostics.Error(
                BakeDiagnosticCodes.ProcessorSourceRead,
                $"The built-in vertex shader '{context.RelativePath}' is not valid UTF-8.");
            return false;
        }
    }
}
