using Bork2d.Bake.Processors;

namespace Bork2d.Bake.Processors;

/// <summary>
/// The internal bridge the public required-sidecar base implements so the framework can create and
/// bind a sidecar without knowing <c>TSidecar</c>.
/// </summary>
internal interface ITypedAssetProcessor
{
    /// <summary>Creates the default sidecar for a source whose sidecar file is missing.</summary>
    /// <param name="context">The internal per-source processing context.</param>
    /// <returns>The default sidecar instance.</returns>
    object CreateDefaultSidecar(BakeProcessorContext context);

    /// <summary>Processes one source with its bound sidecar.</summary>
    /// <param name="context">The internal per-source processing context.</param>
    /// <param name="sidecar">The bound sidecar instance.</param>
    void Process(BakeProcessorContext context, object sidecar);
}
