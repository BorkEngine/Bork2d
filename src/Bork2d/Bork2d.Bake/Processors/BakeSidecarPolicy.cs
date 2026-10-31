namespace Bork2d.Bake.Processors;

/// <summary>
/// The internal sidecar policy of a registered processor: no sidecar, or one required sidecar file.
/// </summary>
internal enum BakeSidecarPolicy
{
    /// <summary>The processor has no settings sidecar.</summary>
    None,

    /// <summary>The processor always runs with a settings sidecar next to its source.</summary>
    Required,
}
