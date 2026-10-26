using Bork2d.Api.Platform;

namespace Bork2d.Common.Time;

/// <summary>
/// The internal clock that the windowed loop advances once per tick.
/// </summary>
/// <remarks>
/// <see cref="ITime"/> stays the public surface; the host owns the concrete implementation and
/// calls <see cref="NewFrame"/> on the tick thread. One-tick modes never call it, so their clock
/// stays at zero.
/// </remarks>
internal interface IFrameTime : ITime
{
    /// <summary>Starts a new tick and publishes the current time and clamped delta.</summary>
    void NewFrame();
}
