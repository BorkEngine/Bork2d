using Microsoft.Extensions.Logging;

namespace Bork2d.Render;

/// <summary>
/// The immutable request the host passes to <see cref="IRendererFactory"/>.
/// </summary>
/// <remarks>
/// Windowed starts carry the opaque instance and surface handles created on the OS/window thread;
/// the renderer never calls SDL and never touches the surface after its swapchain is gone.
/// Offscreen starts carry the fixed extent and no handles. <see cref="CreateReplacementSurface"/>
/// is the single D-M6-16 recovery path: the render thread calls it after dropping the swapchain,
/// the host creates the new surface from the same window/instance on the OS thread, and returns
/// its opaque handle.
/// </remarks>
internal sealed class RendererStartInfo
{
    /// <summary>Gets the host mode.</summary>
    internal required RendererMode Mode { get; init; }

    /// <summary>Gets the opaque <c>VkInstance</c> handle, or <c>0</c> when the renderer owns it.</summary>
    internal required nint InstanceHandle { get; init; }

    /// <summary>Gets the opaque <c>VkSurfaceKHR</c> handle, or <c>0</c> offscreen.</summary>
    internal nint SurfaceHandle { get; init; }

    /// <summary>Gets the initial framebuffer width in pixels (windowed) or target width (offscreen).</summary>
    internal int Width { get; init; }

    /// <summary>Gets the initial framebuffer height in pixels (windowed) or target height (offscreen).</summary>
    internal int Height { get; init; }

    /// <summary>Gets a value indicating whether vertical sync is requested.</summary>
    internal bool VSync { get; init; } = true;

    /// <summary>Gets a value indicating whether validation was requested.</summary>
    internal bool ValidationRequested { get; init; }

    /// <summary>Gets the logger factory used for the <c>Bork2d.Render</c> category.</summary>
    internal required ILoggerFactory LoggerFactory { get; init; }

    /// <summary>
    /// Gets the host callback that creates the replacement surface for the single D-M6-16
    /// recovery, or <see langword="null"/> when recovery is unavailable.
    /// </summary>
    internal Func<nint>? CreateReplacementSurface { get; init; }
}
