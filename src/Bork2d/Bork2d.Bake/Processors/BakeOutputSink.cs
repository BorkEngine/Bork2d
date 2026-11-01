using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Sources;
using Bork2d.Engine.Assets.Pack;
using Bork2d.Engine.Assets.Vfs;
using PolyType;

namespace Bork2d.Bake.Processors;

/// <summary>
/// Collects the outputs of one processor source, validating every path against the source's canonical
/// base asset path.
/// </summary>
/// <remarks>
/// <para>
/// A shaped output is stored as the typed instance plus an engine write delegate, and its references
/// are collected with the compiled shape walk. A raw output stores copied bytes. Output paths must be
/// canonical and equal the source's base asset path or
/// <c>BaseAssetPath + "-" + suffix</c>, where the suffix is one canonical <c>[a-z0-9_-]+</c> segment;
/// anything else is <see cref="BakeDiagnosticCodes.ProcessorOutputPathInvalid"/>.
/// </para>
/// <para>
/// A path produced twice for this source is <see cref="BakeDiagnosticCodes.ProcessorOutputDuplicate"/>.
/// Collisions across sources and custom data are left to the baked index (<c>BK3005</c>). A source may
/// produce any number of outputs, including none.
/// </para>
/// </remarks>
internal sealed class BakeOutputSink : IAssetOutputSink
{
    private readonly List<BakeProcessorOutput> _outputs = [];
    private readonly HashSet<string> _paths = new(StringComparer.Ordinal);
    private readonly string _baseAssetPath;
    private readonly string _sourcePath;
    private readonly string _processorName;
    private readonly BakeDiagnostics _diagnostics;

    /// <summary>
    /// Initializes a new instance of the <see cref="BakeOutputSink"/> class.
    /// </summary>
    /// <param name="baseAssetPath">The source's canonical base asset path.</param>
    /// <param name="sourcePath">The source diagnostic display path.</param>
    /// <param name="processorName">The owning processor's name.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    internal BakeOutputSink(
        string baseAssetPath,
        string sourcePath,
        string processorName,
        BakeDiagnostics diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseAssetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(processorName);
        ArgumentNullException.ThrowIfNull(diagnostics);

        _baseAssetPath = baseAssetPath;
        _sourcePath = sourcePath;
        _processorName = processorName;
        _diagnostics = diagnostics;
    }

    /// <summary>Gets the accepted outputs, in emission order.</summary>
    internal IReadOnlyList<BakeProcessorOutput> Outputs => _outputs;

    /// <summary>Gets a value indicating whether an output path was rejected.</summary>
    internal bool HasError { get; private set; }

    /// <inheritdoc/>
    public void AddAsset<T>(string assetPath, T value)
        where T : IShapeable<T>
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!TryAcceptPath(assetPath))
        {
            return;
        }

        IReadOnlyList<AssetReferenceInfo> collected =
            AssetReferenceCollector.Collect(assetPath, value);
        var references = new List<BakeReferenceSite>(collected.Count);
        foreach (AssetReferenceInfo reference in collected)
        {
            if (reference.Path is null)
            {
                continue;
            }

            references.Add(new BakeReferenceSite(
                reference.PropertyPath,
                reference.Path,
                reference.IsData,
                Line: null,
                Column: null,
                reference.TargetTypeName));
        }

        string? typeName = typeof(T).FullName;
        if (typeName is null)
        {
            _diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ProcessorOutputPathInvalid,
                $"The processor '{_processorName}' produced an output of a type without a full "
                    + $"name at '{assetPath}'.",
                _sourcePath,
                assetPath: assetPath);
            return;
        }

        _outputs.Add(BakeProcessorOutput.ForAsset(
            assetPath,
            typeName,
            (builder, path) => builder.AddMessagePack(path, value),
            references,
            _sourcePath,
            _processorName));
    }

    /// <inheritdoc/>
    public void AddData(string assetPath, ReadOnlySpan<byte> payload)
    {
        if (!TryAcceptPath(assetPath))
        {
            return;
        }

        _outputs.Add(BakeProcessorOutput.ForData(
            assetPath,
            payload.ToArray(),
            _sourcePath,
            _processorName));
    }

    private bool TryAcceptPath(string assetPath)
    {
        if (!AssetPath.TryValidateCanonical(assetPath, out string? reason))
        {
            HasError = true;
            _diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ProcessorOutputPathInvalid,
                $"The processor '{_processorName}' produced the output path '{assetPath}', which is "
                    + $"not canonical: {reason}.",
                _sourcePath,
                assetPath: assetPath);
            return false;
        }

        if (!IsBaseDerived(assetPath))
        {
            HasError = true;
            _diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ProcessorOutputPathInvalid,
                $"The processor '{_processorName}' produced the output path '{assetPath}', which is "
                    + $"not derived from the source's base asset path '{_baseAssetPath}'. An output "
                    + "must be the base path or the base path plus one '-' and a canonical "
                    + "[a-z0-9_-]+ suffix.",
                _sourcePath,
                assetPath: assetPath);
            return false;
        }

        if (!_paths.Add(assetPath))
        {
            HasError = true;
            _diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ProcessorOutputDuplicate,
                $"The processor '{_processorName}' produced the output path '{assetPath}' more than "
                    + "once for one source.",
                _sourcePath,
                assetPath: assetPath);
            return false;
        }

        return true;
    }

    private bool IsBaseDerived(string assetPath)
    {
        if (string.Equals(assetPath, _baseAssetPath, StringComparison.Ordinal))
        {
            return true;
        }

        int prefixLength = _baseAssetPath.Length + 1;
        if (assetPath.Length <= prefixLength
            || !assetPath.StartsWith(_baseAssetPath, StringComparison.Ordinal)
            || assetPath[_baseAssetPath.Length] != '-')
        {
            return false;
        }

        for (int index = prefixLength; index < assetPath.Length; index++)
        {
            char character = assetPath[index];
            bool canonical = character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_' or '-';
            if (!canonical)
            {
                return false;
            }
        }

        return true;
    }
}
