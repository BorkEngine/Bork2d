namespace Bork2d.Bake.Builder.Processors;

/// <summary>
/// Whether a processor runs with a settings sidecar next to its source.
/// </summary>
public enum SidecarPolicy
{
    /// <summary>The processor has no settings sidecar.</summary>
    None,

    /// <summary>The processor always runs with a settings sidecar next to its source.</summary>
    Required,
}
