using Bork2d.Engine.Platform;

namespace Bork2d.Engine.Time;

/// <summary>
/// The internal clock that the windowed/offscreen loop advances once per tick.
/// </summary>
/// <remarks>
/// <see cref="ITime"/> stays the public surface; the host owns the concrete implementation and
/// calls <see cref="NewFrame"/> on the tick thread. Headless mode uses the synthesized
/// <see cref="IHeadlessTime"/> instead, so this interface is never advanced there.
/// </remarks>
internal interface IFrameTime : ITime
{
    /// <summary>Starts a new tick and publishes the current time and clamped delta.</summary>
    void NewFrame();
}
