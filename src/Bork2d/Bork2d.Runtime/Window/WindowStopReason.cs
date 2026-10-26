namespace Bork2d.Runtime.Window;

/// <summary>
/// Why the windowed platform stopped its event pump.
/// </summary>
/// <remarks>
/// The values are internal diagnostics for the runtime host and the stop log. Later work packages
/// read them to decide how to report a windowed run; they are never part of the public API.
/// </remarks>
internal enum WindowStopReason
{
    /// <summary>The platform has not stopped yet.</summary>
    None = 0,

    /// <summary>Shutdown was requested by the host (an engine <c>Exit</c> or normal teardown).</summary>
    Exit,

    /// <summary>The user closed the window or SDL reported <c>SDL_QUIT</c>.</summary>
    Close,

    /// <summary>An SDL operation failed while the platform was running.</summary>
    Error,
}
