using Bork2d.Bake.Sources;
using Bork2d.Engine.Assets.Pack;

namespace Bork2d.Bake.Processors;

/// <summary>
/// One accepted output of a processor source, ready for the baked index and pack composer.
/// </summary>
/// <remarks>
/// A shaped (<see cref="AddAsset{T}"/>-style) output stores the engine write delegate plus the
/// references collected from the instance; the processor never sees MessagePack bytes or the pack
/// writer. A raw (<see cref="AddData"/>) output stores the opaque payload. The source path is the
/// diagnostic display path of the source that produced the output.
/// </remarks>
internal sealed class BakeProcessorOutput
{
    private BakeProcessorOutput(
        string path,
        bool isRaw,
        string? typeName,
        Action<PackBuilder, string>? write,
        byte[]? rawPayload,
        IReadOnlyList<BakeReferenceSite> references,
        string sourcePath,
        string processorName)
    {
        Path = path;
        IsRaw = isRaw;
        TypeName = typeName;
        Write = write;
        RawPayload = rawPayload;
        References = references;
        SourcePath = sourcePath;
        ProcessorName = processorName;
    }

    /// <summary>Gets the canonical output path.</summary>
    internal string Path { get; }

    /// <summary>Gets a value indicating whether the output is an opaque raw payload.</summary>
    internal bool IsRaw { get; }

    /// <summary>Gets the shaped output's CLR type full name, or <see langword="null"/> for raw output.</summary>
    internal string? TypeName { get; }

    /// <summary>Gets the engine write delegate for a shaped output, or <see langword="null"/> for raw output.</summary>
    internal Action<PackBuilder, string>? Write { get; }

    /// <summary>Gets the raw payload for a data output, or <see langword="null"/> for shaped output.</summary>
    internal byte[]? RawPayload { get; }

    /// <summary>Gets the reference sites collected from a shaped output, in traversal order.</summary>
    internal IReadOnlyList<BakeReferenceSite> References { get; }

    /// <summary>Gets the diagnostic display path of the source that produced the output.</summary>
    internal string SourcePath { get; }

    /// <summary>Gets the owning processor's name.</summary>
    internal string ProcessorName { get; }

    /// <summary>Creates a shaped processor output.</summary>
    /// <param name="path">The canonical output path.</param>
    /// <param name="typeName">The output type's CLR full name.</param>
    /// <param name="write">The engine write delegate.</param>
    /// <param name="references">The collected reference sites.</param>
    /// <param name="sourcePath">The source diagnostic path.</param>
    /// <param name="processorName">The owning processor's name.</param>
    /// <returns>The output.</returns>
    internal static BakeProcessorOutput ForAsset(
        string path,
        string typeName,
        Action<PackBuilder, string> write,
        IReadOnlyList<BakeReferenceSite> references,
        string sourcePath,
        string processorName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(processorName);

        return new BakeProcessorOutput(
            path,
            isRaw: false,
            typeName,
            write,
            rawPayload: null,
            references,
            sourcePath,
            processorName);
    }

    /// <summary>Creates a raw processor output.</summary>
    /// <param name="path">The canonical output path.</param>
    /// <param name="payload">The opaque payload bytes.</param>
    /// <param name="sourcePath">The source diagnostic path.</param>
    /// <param name="processorName">The owning processor's name.</param>
    /// <returns>The output.</returns>
    internal static BakeProcessorOutput ForData(
        string path,
        byte[] payload,
        string sourcePath,
        string processorName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(processorName);

        return new BakeProcessorOutput(
            path,
            isRaw: true,
            typeName: null,
            write: null,
            payload,
            references: [],
            sourcePath,
            processorName);
    }
}
