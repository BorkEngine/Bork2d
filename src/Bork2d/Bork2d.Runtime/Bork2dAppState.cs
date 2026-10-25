namespace Bork2d.Runtime;

/// <summary>
/// The one-shot lifecycle of a <see cref="Bork2dApp"/>.
/// </summary>
internal enum Bork2dAppState
{
    /// <summary>Built but not yet run or disposed.</summary>
    Created = 0,

    /// <summary><c>Run</c> is executing the M1 smoke sequence.</summary>
    Running = 1,

    /// <summary><c>Run</c> returned or threw; it cannot be called again.</summary>
    Completed = 2,

    /// <summary><c>Dispose</c> ran; the app cannot be run or disposed again.</summary>
    Disposed = 3,
}
