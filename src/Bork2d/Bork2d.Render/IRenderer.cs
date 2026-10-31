using Bork2d.Render.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Bork2d.Render;

/// <summary>
/// The internal renderer composition contract consumed by <c>Bork2d.Runtime</c> (D-M6-25).
/// </summary>
/// <remarks>
/// <para>
/// The interface is deliberately free of Vulkan, SDL, and window types: the host passes opaque
/// <see cref="nint"/> instance/surface handles, posts coalesced window-state changes, and observes
/// readiness, status, counters, and a one-shot fatal error. Tests inject a fake implementation.
/// </para>
/// <para>
/// <see cref="Start"/> starts the renderer thread without waiting for readiness (D-M6-06). The
/// production host waits for the first tick with <see cref="WaitForReady()"/>; the bounded
/// overload exists for tests only and no production path uses a timeout. Stop is a request
/// followed by a join; no renderer thread survives <see cref="Join()"/>.
/// </para>
/// </remarks>
internal interface IRenderer
{
    /// <summary>Starts the renderer and returns without waiting for readiness.</summary>
    void Start();

    /// <summary>Gets a value indicating whether the renderer reached readiness.</summary>
    bool IsReady { get; }

    /// <summary>Waits without a timeout until readiness or a fatal error.</summary>
    void WaitForReady();

    /// <summary>Waits at most <paramref name="timeout"/> for readiness (tests only).</summary>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when ready; <see langword="false"/> on timeout.</returns>
    bool WaitForReady(TimeSpan timeout);

    /// <summary>Gets the current lifecycle state.</summary>
    RendererStatus Status { get; }

    /// <summary>Gets why the renderer stopped, or <see cref="RendererStopReason.None"/>.</summary>
    RendererStopReason StopReason { get; }

    /// <summary>Gets the latest frame/validation counters.</summary>
    RendererCounters Counters { get; }

    /// <summary>Gets the one-shot fatal error observed by the host at a tick boundary.</summary>
    Exception? FatalException { get; }

    /// <summary>Posts the latest framebuffer extent; the latest value wins.</summary>
    /// <param name="extent">The confirmed framebuffer extent in pixels.</param>
    void PostWindowExtent(WindowExtent extent);

    /// <summary>Posts the latest fullscreen state; the latest value wins.</summary>
    /// <param name="fullscreen">Whether the window is fullscreen.</param>
    void PostFullscreen(bool fullscreen);

    /// <summary>Posts the latest minimized state; the latest value wins.</summary>
    /// <param name="minimized">Whether the window is minimized.</param>
    void PostMinimized(bool minimized);

    /// <summary>Requests a graceful stop; returns immediately.</summary>
    void RequestStop();

    /// <summary>Waits for the renderer thread to join.</summary>
    void Join();

    /// <summary>Waits at most <paramref name="timeout"/> for the renderer thread to join.</summary>
    /// <param name="timeout">The bounded wait.</param>
    /// <returns><see langword="true"/> when joined; <see langword="false"/> on timeout.</returns>
    bool Join(TimeSpan timeout);
}
