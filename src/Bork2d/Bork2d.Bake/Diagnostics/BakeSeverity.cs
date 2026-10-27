namespace Bork2d.Bake.Diagnostics;

/// <summary>
/// The severity of a bake diagnostic.
/// </summary>
internal enum BakeSeverity
{
    /// <summary>A validation failure that fails the bake.</summary>
    Error,

    /// <summary>A recoverable condition that does not fail the bake.</summary>
    Warning,

    /// <summary>Informational output that is not a problem.</summary>
    Info,
}
