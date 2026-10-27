using Bork2d.Bake.Builder;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// A configurable required-sidecar test processor that counts default-sidecar creation separately
/// from processing.
/// </summary>
internal sealed class TestSidecarProcessor : AssetProcessor<TestSidecar>
{
    private readonly string _name;
    private readonly string _extension;
    private readonly Func<AssetProcessorContext, TestSidecar> _createDefault;
    private readonly Action<AssetProcessorContext, TestSidecar> _process;

    /// <summary>Initializes a new instance of the <see cref="TestSidecarProcessor"/> class.</summary>
    /// <param name="name">The processor name used in diagnostics.</param>
    /// <param name="extension">The single owned extension.</param>
    /// <param name="createDefault">The default-sidecar factory; the test configures it explicitly.</param>
    /// <param name="process">The processing callback, or <see langword="null"/> for no outputs.</param>
    internal TestSidecarProcessor(
        string name,
        string extension,
        Func<AssetProcessorContext, TestSidecar> createDefault,
        Action<AssetProcessorContext, TestSidecar>? process = null)
    {
        _name = name;
        _extension = extension;
        _createDefault = createDefault;
        _process = process ?? ((_, _) => { });
    }

    /// <inheritdoc/>
    public override string Name => _name;

    /// <inheritdoc/>
    public override IReadOnlyList<string> Extensions => [_extension];

    /// <summary>Gets the number of default sidecars this processor created.</summary>
    internal int CreateDefaultSidecarCount { get; private set; }

    /// <summary>Gets the number of times the processor ran.</summary>
    internal int ProcessCount { get; private set; }

    /// <inheritdoc/>
    protected override TestSidecar CreateDefaultSidecar(AssetProcessorContext context)
    {
        CreateDefaultSidecarCount++;
        return _createDefault(context);
    }

    /// <inheritdoc/>
    protected override void Process(AssetProcessorContext context, TestSidecar sidecar)
    {
        ProcessCount++;
        _process(context, sidecar);
    }
}
