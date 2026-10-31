using Bork2d.Bake;
using PolyType;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// A configurable <see cref="SidecarPolicy.None"/> test processor. The framework calls
/// <see cref="Process"/> once per matched source and the test observes the count and context.
/// </summary>
internal sealed class TestProcessor : IAssetProcessor
{
    private readonly Action<AssetProcessorContext>? _process;

    /// <summary>Initializes a new instance of the <see cref="TestProcessor"/> class.</summary>
    /// <param name="name">The processor name used in diagnostics.</param>
    /// <param name="extension">The single owned extension.</param>
    /// <param name="process">The processing callback, or <see langword="null"/> for no outputs.</param>
    /// <param name="policy">The declared sidecar policy; used by registration-validation tests.</param>
    /// <param name="sidecarShape">The declared sidecar shape; used by registration-validation tests.</param>
    internal TestProcessor(
        string name,
        string extension,
        Action<AssetProcessorContext>? process = null,
        SidecarPolicy policy = SidecarPolicy.None,
        ITypeShape? sidecarShape = null)
        : this(name, [extension], process, policy, sidecarShape)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TestProcessor"/> class.</summary>
    /// <param name="name">The processor name used in diagnostics.</param>
    /// <param name="extensions">The owned extensions.</param>
    /// <param name="process">The processing callback, or <see langword="null"/> for no outputs.</param>
    /// <param name="policy">The declared sidecar policy; used by registration-validation tests.</param>
    /// <param name="sidecarShape">The declared sidecar shape; used by registration-validation tests.</param>
    internal TestProcessor(
        string name,
        IReadOnlyList<string> extensions,
        Action<AssetProcessorContext>? process = null,
        SidecarPolicy policy = SidecarPolicy.None,
        ITypeShape? sidecarShape = null)
    {
        Name = name;
        Extensions = extensions;
        _process = process;
        Policy = policy;
        SidecarShape = sidecarShape;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string> Extensions { get; }

    /// <inheritdoc/>
    public SidecarPolicy Policy { get; }

    /// <inheritdoc/>
    public ITypeShape? SidecarShape { get; }

    /// <summary>Gets the number of times the processor ran.</summary>
    internal int ProcessCount { get; private set; }

    /// <inheritdoc/>
    public void Process(AssetProcessorContext context)
    {
        ProcessCount++;
        _process?.Invoke(context);
    }
}
