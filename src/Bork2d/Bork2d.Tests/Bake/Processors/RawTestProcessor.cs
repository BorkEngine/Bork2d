using Bork2d.Bake;
using PolyType;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// A raw public processor double for registry tests: it declares its registration metadata directly
/// so invalid-registration tests reach the registry's validation.
/// </summary>
internal sealed class RawTestProcessor : IAssetProcessor
{
    /// <summary>Initializes a new instance of the <see cref="RawTestProcessor"/> class.</summary>
    /// <param name="name">The processor name.</param>
    /// <param name="extensions">The declared extensions.</param>
    /// <param name="policy">The declared sidecar policy.</param>
    /// <param name="sidecarShape">The declared sidecar shape.</param>
    internal RawTestProcessor(
        string name,
        IReadOnlyList<string> extensions,
        SidecarPolicy policy = SidecarPolicy.None,
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
    public SidecarPolicy Policy { get; }

    /// <inheritdoc/>
    public ITypeShape? SidecarShape { get; }

    /// <inheritdoc/>
    public void Process(AssetProcessorContext context)
        => throw new NotSupportedException("Registry tests never process sources.");
}
