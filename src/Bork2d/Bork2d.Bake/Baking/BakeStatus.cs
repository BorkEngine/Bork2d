namespace Bork2d.Bake.Baking;

/// <summary>
/// The classification of one bake pipeline run, which the CLI maps to its documented exit codes.
/// </summary>
internal enum BakeStatus
{
    /// <summary>Validation succeeded; a pack was written unless the run was a check.</summary>
    Success,

    /// <summary>The incremental manifest matched and the existing pack is current (CLI exit 0).</summary>
    UpToDate,

    /// <summary>The descriptor or generated host configuration is invalid (CLI exit 2).</summary>
    ConfigurationError,

    /// <summary>Source, texture, reference, or pack diagnostics failed the bake (CLI exit 1).</summary>
    DiagnosticsError,
}
