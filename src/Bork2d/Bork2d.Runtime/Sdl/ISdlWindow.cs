namespace Bork2d.Runtime.Sdl;

/// <summary>
/// The internal seam over one SDL window.
/// </summary>
/// <remarks>
/// The two size members keep the units separate: <see cref="Size"/> is window points and
/// <see cref="SizeInPixels"/> is the framebuffer size. The seam deliberately exposes no position,
/// monitor, or icon member because M2 has no such public contract.
/// </remarks>
internal interface ISdlWindow : IDisposable
{
    /// <summary>Gets the SDL window id.</summary>
    uint Id { get; }

    /// <summary>Gets the current window size in window points.</summary>
    SdlWindowSize Size { get; }

    /// <summary>Gets the current framebuffer size in physical pixels.</summary>
    SdlWindowSize SizeInPixels { get; }

    /// <summary>Gets the current SDL window flags.</summary>
    SdlWindowFlags Flags { get; }

    /// <summary>Sets the window title.</summary>
    /// <param name="title">The new title.</param>
    void SetTitle(string title);

    /// <summary>Sets the window size.</summary>
    /// <param name="width">The new width in window points.</param>
    /// <param name="height">The new height in window points.</param>
    void SetSize(int width, int height);

    /// <summary>Enters or leaves borderless desktop fullscreen.</summary>
    /// <param name="fullscreen">Whether the window should be fullscreen.</param>
    /// <exception cref="Bork2d.Engine.Bork2dException">SDL failed to change the fullscreen state.</exception>
    void SetFullscreen(bool fullscreen);

    /// <summary>
    /// Gets the Vulkan instance extensions SDL requires for this window
    /// (<c>SDL_Vulkan_GetInstanceExtensions</c>).
    /// </summary>
    /// <returns>The extension names to pass to <c>vkCreateInstance</c>.</returns>
    /// <remarks>
    /// The caller must load the Vulkan loader through <see cref="ISdlApi.VulkanLoadLibrary"/> first.
    /// The list is SDL's, not a hard-coded platform list.
    /// </remarks>
    /// <exception cref="Bork2d.Engine.Bork2dException">SDL failed or the loader is not loaded.</exception>
    IReadOnlyList<string> GetVulkanInstanceExtensions();

    /// <summary>
    /// Creates the window's Vulkan surface for an instance (<c>SDL_Vulkan_CreateSurface</c>).
    /// </summary>
    /// <param name="instanceHandle">The opaque <c>VkInstance</c> handle; must be non-zero.</param>
    /// <returns>The opaque <c>VkSurfaceKHR</c> handle; the caller destroys it with the instance.</returns>
    /// <exception cref="Bork2d.Engine.Bork2dException">SDL failed to create the surface.</exception>
    nint CreateVulkanSurface(nint instanceHandle);

    /// <summary>
    /// Gets the Vulkan drawable size in framebuffer pixels (<c>SDL_Vulkan_GetDrawableSize</c>).
    /// </summary>
    /// <returns>The framebuffer extent, which is the authority for the swapchain extent.</returns>
    SdlWindowSize GetVulkanDrawableSize();
}
