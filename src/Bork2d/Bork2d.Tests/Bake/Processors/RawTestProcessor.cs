using Bork2d.Bake.Processors;
using PolyType;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// A raw internal processor double for registry tests: the public adapter normalizes the policy, so
/// invalid-registration tests implement <see cref="IBakeAssetProcessor"/> directly.
/// </summary>
internal sealed class RawTestProcessor : IBakeAssetProcessor
{
    /// <summary>Initializes a new instance of the <see cref="RawTestProcessor"/> class.</summary>
    /// <param name="name">The processor name.</param>
    /// <param name="extensions">The declared extensions.</param>
    /// <param name="policy">The declared sidecar policy.</param>
    /// <param name="sidecarShape">The declared sidecar shape.</param>
    internal RawTestProcessor(
        string name,
        IReadOnlyList<string> extensions,
        BakeSidecarPolicy policy = BakeSidecarPolicy.None,
        ITypeShape? sidecarShape = null)
    {
        Name = name;
        Extensions = extensions;
        Policy = policy;
        SidecarShape = sidecarShape;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string> Extensions { get; }

    /// <inheritdoc/>
    public BakeSidecarPolicy Policy { get; }

    /// <inheritdoc/>
    public ITypeShape? SidecarShape { get; }

    /// <inheritdoc/>
    public object CreateDefaultSidecar(BakeProcessorContext context)
        => throw new NotSupportedException("Registry tests never create sidecars.");

    /// <inheritdoc/>
    public void Process(BakeProcessorContext context, object? sidecar)
        => throw new NotSupportedException("Registry tests never process sources.");
}
