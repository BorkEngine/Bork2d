using System.Text;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Sources;
using Bork2d.Bake.Toml;
using PolyType;

namespace Bork2d.Bake.Processors;

/// <summary>
/// Runs every registered processor over its discovered sources, in deterministic source order.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="BakeSidecarPolicy.None"/> processor runs directly. A
/// <see cref="BakeSidecarPolicy.Required"/> processor runs with its bound sidecar; when the sidecar
/// file is missing and source writes are allowed, <see cref="IBakeAssetProcessor.CreateDefaultSidecar"/>
/// is called exactly once, the result is written as canonical TOML with
/// <see cref="TomlSidecarWriter"/>, an informational
/// <see cref="BakeDiagnosticCodes.ProcessorSidecarCreated"/> diagnostic is reported, and the file is
/// read back and bound. An existing file is never overwritten or defaulted. When source writes are
/// disabled (a <c>--check</c>/<c>validate</c> run), the default object is still created and validated
/// with the writer, but no file is written, no creation diagnostic is reported, and the object is
/// passed directly to the processor.
/// </para>
/// <para>
/// The sidecar path is the one discovery attached to the source, falling back to the conventional
/// <c>&lt;source&gt;.toml</c> only when no sidecar was discovered. This keeps a casing-different
/// sidecar filename attached to the source it belongs to.
/// </para>
/// <para>
/// A processor exception is <see cref="BakeDiagnosticCodes.ProcessorFailed"/> and discards that
/// source's outputs; a processor-reported error discards them too, while warnings and infos do not.
/// Other sources still run. Processor instances are reused across sources, and the framework keeps no
/// per-source state on them.
/// </para>
/// </remarks>
internal static class BakeProcessorRunner
{
    private static readonly UTF8Encoding _utf8NoBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>Runs the processors over a set of sources.</summary>
    /// <param name="project">The validated project configuration.</param>
    /// <param name="sources">The processor sources, in deterministic order.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <param name="allowSourceWrites">
    /// Whether a missing required sidecar may be written next to its source. Pass
    /// <see langword="false"/> for <c>--check</c>/<c>validate</c>, which create and validate the
    /// default sidecar in memory but never mutate the source tree.
    /// </param>
    /// <returns>The accepted outputs of every successfully processed source, sorted by path.</returns>
    internal static IReadOnlyList<BakeProcessorOutput> Run(
        BakeProject project,
        IReadOnlyList<BakeProcessorSource> sources,
        BakeDiagnostics diagnostics,
        bool allowSourceWrites = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var outputs = new List<BakeProcessorOutput>();

        foreach (BakeProcessorSource source in sources)
        {
            string diagnosticPath = BakeDiagnosticPath.ForSource(
                project.ProjectRoot,
                source.FullPath);

            var outputSink = new BakeOutputSink(
                source.BaseAssetPath,
                diagnosticPath,
                source.Processor.Name,
                diagnostics);
            var diagnosticSink = new BakeProcessorDiagnosticSink(
                source.Processor.Name,
                diagnosticPath,
                source.BaseAssetPath,
                diagnostics);

            string fileName = Path.GetFileName(source.FullPath);
            var context = new BakeProcessorContext(
                source.FullPath,
                source.RelativePath,
                source.BaseAssetPath,
                fileName,
                Path.GetFileNameWithoutExtension(fileName),
                outputSink,
                diagnosticSink);

            object? sidecar = null;
            if (source.Processor.Policy == BakeSidecarPolicy.Required)
            {
                string sidecarPath = source.SidecarPath ?? source.FullPath + ".toml";
                if (File.Exists(sidecarPath))
                {
                    if (!TryBindSidecar(
                        project,
                        source,
                        sidecarPath,
                        diagnosticPath,
                        diagnostics,
                        out sidecar))
                    {
                        continue;
                    }
                }
                else
                {
                    if (!TryCreateDefaultSidecar(
                        project,
                        source,
                        sidecarPath,
                        context,
                        diagnosticPath,
                        diagnostics,
                        writeFile: allowSourceWrites,
                        out object? created))
                    {
                        continue;
                    }

                    if (diagnosticSink.HasError)
                    {
                        continue;
                    }

                    if (allowSourceWrites)
                    {
                        if (!TryBindSidecar(
                            project,
                            source,
                            sidecarPath,
                            diagnosticPath,
                            diagnostics,
                            out sidecar))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        sidecar = created;
                    }
                }
            }

            try
            {
                source.Processor.Process(context, sidecar);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.ProcessorFailed,
                    $"The processor '{source.Processor.Name}' failed while processing "
                        + $"'{source.RelativePath}': {exception.Message}",
                    diagnosticPath,
                    assetPath: source.BaseAssetPath);
                continue;
            }

            if (diagnosticSink.HasError || outputSink.HasError)
            {
                continue;
            }

            outputs.AddRange(outputSink.Outputs);
        }

        outputs.Sort(static (left, right) =>
        {
            int result = string.CompareOrdinal(left.Path, right.Path);
            return result != 0
                ? result
                : string.CompareOrdinal(left.SourcePath, right.SourcePath);
        });
        return outputs;
    }

    private static bool TryBindSidecar(
        BakeProject project,
        BakeProcessorSource source,
        string sidecarPath,
        string diagnosticPath,
        BakeDiagnostics diagnostics,
        out object? sidecar)
    {
        try
        {
            return BakeSidecarBinder.TryBind(project, source, sidecarPath, diagnostics, out sidecar);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            sidecar = null;
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.BinderUnexpected,
                $"Binding the sidecar of processor '{source.Processor.Name}' for "
                    + $"'{source.RelativePath}' failed unexpectedly: {exception.Message}",
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }
    }

    private static bool TryCreateDefaultSidecar(
        BakeProject project,
        BakeProcessorSource source,
        string sidecarPath,
        BakeProcessorContext context,
        string diagnosticPath,
        BakeDiagnostics diagnostics,
        bool writeFile,
        out object? created)
    {
        string sidecarDisplayPath = BakeDiagnosticPath.ForSource(project.ProjectRoot, sidecarPath);
        created = null;

        object? defaultSidecar;
        try
        {
            defaultSidecar = source.Processor.CreateDefaultSidecar(context);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ProcessorSidecarCreateFailed,
                $"The processor '{source.Processor.Name}' failed to create the default sidecar "
                    + $"'{sidecarDisplayPath}' for '{source.RelativePath}': {exception.Message}",
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }

        if (defaultSidecar is null)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ProcessorSidecarCreateFailed,
                $"The processor '{source.Processor.Name}' returned no default sidecar for "
                    + $"'{source.RelativePath}', but its sidecar policy is Required.",
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }

        ITypeShape? shape = source.Processor.SidecarShape;
        if (shape is null)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ProcessorSidecarCreateFailed,
                $"The processor '{source.Processor.Name}' has no sidecar shape, so the default "
                    + $"sidecar '{sidecarDisplayPath}' cannot be written.",
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }

        string text;
        string? reason;
        try
        {
            if (!TomlSidecarWriter.TryWrite(shape, defaultSidecar, out text, out reason))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.ProcessorSidecarCreateFailed,
                    $"The default sidecar '{sidecarDisplayPath}' of processor "
                        + $"'{source.Processor.Name}' cannot be written: {reason}.",
                    diagnosticPath,
                    assetPath: source.BaseAssetPath);
                return false;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ProcessorSidecarCreateFailed,
                $"The default sidecar '{sidecarDisplayPath}' of processor "
                    + $"'{source.Processor.Name}' cannot be written: {exception.Message}",
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }

        if (!writeFile)
        {
            // Validate the default object without touching the source tree; the caller passes it
            // directly to the processor.
            created = defaultSidecar;
            return true;
        }

        try
        {
            byte[] bytes = _utf8NoBom.GetBytes(text);
            using var stream = new FileStream(
                sidecarPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            stream.Write(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ProcessorSidecarCreateFailed,
                $"The required sidecar '{sidecarDisplayPath}' of processor "
                    + $"'{source.Processor.Name}' could not be created: {exception.Message}",
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }

        diagnostics.Add(
            BakeSeverity.Info,
            BakeDiagnosticCodes.ProcessorSidecarCreated,
            $"Created the required sidecar '{sidecarDisplayPath}' for processor "
                + $"'{source.Processor.Name}' and source '{source.RelativePath}'.",
            sidecarDisplayPath);
        return true;
    }
}
