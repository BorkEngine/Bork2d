using Bork2d.Engine.Assets;
using Bork2d.Render.Vulkan;
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

    /// <summary>
    /// Gets the platform/test-owned Vulkan instance used for device selection, debug labels, and
    /// validation observation, or <see langword="null"/> when the renderer creates its own
    /// (offscreen, WP-4).
    /// </summary>
    /// <remarks>
    /// Windowed starts must pass the instance that owns <see cref="SurfaceHandle"/>; the renderer
    /// never destroys it and never destroys <see cref="InstanceHandle"/>. <see cref="InstanceHandle"/>
    /// remains the opaque handle for callers that do not expose an instance object.
    /// </remarks>
    internal IVulkanInstance? Instance { get; init; }

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
    /// Gets the app's asset service, used by the render thread to load texture descriptors and
    /// payloads asynchronously (M8 §6.5).
    /// </summary>
    /// <remarks>
    /// The host supplies it after <c>AssetService.Start()</c> and before the renderer is created;
    /// it stays valid until the renderer joins, which happens before the asset service stops. A
    /// missing value is a renderer-creation failure (M8 §6.12).
    /// </remarks>
    internal required IAssets Assets { get; init; }

    /// <summary>
    /// Gets the host callback that creates the replacement surface for the single D-M6-16
    /// recovery, or <see langword="null"/> when recovery is unavailable.
    /// </summary>
    /// <remarks>
    /// The callback runs on the render thread after the swapchain is gone; the host creates the new
    /// surface on the OS thread and destroys the old one. It returns <c>0</c> on failure.
    /// </remarks>
    internal Func<nint>? CreateReplacementSurface { get; init; }
}
