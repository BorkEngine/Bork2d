using Bork2d.Engine;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// One windowed swapchain, its images, and its image views (D-M6-11, D-M6-15).
/// </summary>
/// <remarks>
/// The render thread owns the swapchain. A recreation creates the replacement with the old handle
/// as <c>oldSwapchain</c>, then disposes the old wrapper; the old handle is never passed to the
/// driver after the replacement exists. <see cref="Dispose"/> is idempotent and never throws
/// (D-M6-27).
/// </remarks>
internal sealed unsafe class WindowedSwapchain : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly KhrSwapchain _swapchainApi;
    private readonly ILogger _logger;
    private readonly Image[] _images;
    private readonly ImageView[] _views;
    private readonly Semaphore[] _renderFinished;
    private int _destroyed;

    private WindowedSwapchain(
        Vk vk,
        Device device,
        KhrSwapchain swapchainApi,
        ILogger logger,
        SwapchainKHR handle,
        Image[] images,
        ImageView[] views,
        Semaphore[] renderFinished,
        Extent2D extent,
        Format format,
        ColorSpaceKHR colorSpace,
        PresentModeKHR presentMode,
        PresentModeKHR requestedPresentMode)
    {
        _vk = vk;
        _device = device;
        _swapchainApi = swapchainApi;
        _logger = logger;
        _images = images;
        _views = views;
        _renderFinished = renderFinished;
        Handle = handle;
        Extent = extent;
        Format = format;
        ColorSpace = colorSpace;
        PresentMode = presentMode;
        RequestedPresentMode = requestedPresentMode;
    }

    /// <summary>Gets the swapchain handle.</summary>
    internal SwapchainKHR Handle { get; }

    /// <summary>Gets the selected image extent.</summary>
    internal Extent2D Extent { get; }

    /// <summary>Gets the selected image format.</summary>
    internal Format Format { get; }

    /// <summary>Gets the selected color space.</summary>
    internal ColorSpaceKHR ColorSpace { get; }

    /// <summary>Gets the actual present mode.</summary>
    internal PresentModeKHR PresentMode { get; }

    /// <summary>Gets the preferred present mode the request asked for.</summary>
    internal PresentModeKHR RequestedPresentMode { get; }

    /// <summary>Gets the number of swapchain images.</summary>
    internal uint ImageCount => (uint)_views.Length;

    /// <summary>Gets the image view for an acquired image index.</summary>
    /// <param name="index">The swapchain image index.</param>
    /// <returns>The image view used as the dynamic-rendering attachment.</returns>
    internal ImageView GetView(uint index) => _views[index];

    /// <summary>Gets the swapchain image for an acquired image index.</summary>
    /// <param name="index">The swapchain image index.</param>
    /// <returns>The image that receives the clear.</returns>
    internal Image GetImage(uint index) => _images[index];

    /// <summary>
    /// Gets the render-finished semaphore for an acquired image index. It is per image, not per
    /// frame, so a presentation operation that still uses it cannot race a later submit
    /// (VUID-vkQueueSubmit2-semaphore-03868).
    /// </summary>
    /// <param name="index">The swapchain image index.</param>
    /// <returns>The semaphore signalled by the submit and awaited by present.</returns>
    internal Semaphore GetRenderFinished(uint index) => _renderFinished[index];

    /// <summary>Creates a swapchain for the surface.</summary>
    /// <param name="device">The logical device and its physical device.</param>
    /// <param name="surfaceApi">The loaded <c>VK_KHR_surface</c> entry points.</param>
    /// <param name="swapchainApi">The loaded <c>VK_KHR_swapchain</c> entry points.</param>
    /// <param name="surfaceHandle">The opaque <c>VkSurfaceKHR</c> handle.</param>
    /// <param name="requestedExtent">The latest requested framebuffer extent in pixels.</param>
    /// <param name="vsync">The requested vertical-sync setting (D-M6-17).</param>
    /// <param name="oldSwapchain">The handle to pass as <c>oldSwapchain</c>, or default.</param>
    /// <param name="oldExtent">The old extent for the recreation log.</param>
    /// <param name="reason">
    /// The recreation reason, or <see langword="null"/> for the initial creation.
    /// </param>
    /// <param name="instance">The instance seam used for debug labels.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger.</param>
    /// <returns>The created swapchain.</returns>
    /// <exception cref="Bork2dException">
    /// No sRGB format, no surface format/present mode, a zero extent, or a Vulkan failure.
    /// </exception>
    internal static WindowedSwapchain Create(
        VulkanDevice device,
        KhrSurface surfaceApi,
        KhrSwapchain swapchainApi,
        nint surfaceHandle,
        WindowExtent requestedExtent,
        bool vsync,
        SwapchainKHR oldSwapchain,
        Extent2D oldExtent,
        SwapchainRecreateReason? reason,
        IVulkanInstance instance,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(surfaceApi);
        ArgumentNullException.ThrowIfNull(swapchainApi);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(logger);

        var surface = new SurfaceKHR((ulong)surfaceHandle);
        PhysicalDevice physicalDevice = device.PhysicalDevice;
        Device logicalDevice = device.Device;
        Vk vk = device.Api;

        SurfaceCapabilitiesKHR capabilities;
        Result result = surfaceApi.GetPhysicalDeviceSurfaceCapabilities(
            physicalDevice,
            surface,
            out capabilities);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkGetPhysicalDeviceSurfaceCapabilitiesKHR failed with VkResult {result}.");
        }

        VulkanSurfaceFormat format = SelectSurfaceFormat(
            surfaceApi,
            physicalDevice,
            surface);
        PresentModeKHR presentMode = SelectPresentMode(
            surfaceApi,
            physicalDevice,
            surface,
            vsync,
            out PresentModeKHR requestedPresentMode);
        Extent2D extent = ResolveExtent(capabilities, requestedExtent);
        uint imageCount = SwapchainPolicy.SelectImageCount(
            capabilities.MinImageCount,
            capabilities.MaxImageCount);
        SwapchainSurfaceSelection selection = SwapchainPolicy.SelectSurfaceProperties(capabilities);

        // CaptureFrameAsync copies the presented image, so every swapchain image needs transfer-source
        // usage in addition to color attachment (M8 §6.9).
        const ImageUsageFlags usage = ImageUsageFlags.ColorAttachmentBit
            | ImageUsageFlags.TransferSrcBit;
        if ((capabilities.SupportedUsageFlags & ImageUsageFlags.TransferSrcBit) == 0)
        {
            throw new Bork2dException(
                "The Vulkan surface does not support VK_IMAGE_USAGE_TRANSFER_SRC_BIT on its "
                    + "swapchain images, which CaptureFrameAsync requires; the windowed renderer "
                    + "cannot start on this surface.");
        }

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = surface,
            MinImageCount = imageCount,
            ImageFormat = format.Format,
            ImageColorSpace = format.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = usage,
            ImageSharingMode = SharingMode.Exclusive,
            QueueFamilyIndexCount = 0,
            PQueueFamilyIndices = null,
            PreTransform = selection.PreTransform,
            CompositeAlpha = selection.CompositeAlpha,
            PresentMode = presentMode,
            Clipped = selection.Clipped,
            OldSwapchain = oldSwapchain,
        };

        result = swapchainApi.CreateSwapchain(
            logicalDevice,
            in createInfo,
            null,
            out SwapchainKHR handle);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkCreateSwapchainKHR failed with VkResult {result} for extent "
                    + $"{extent.Width}x{extent.Height}.");
        }

        Image[] images = [];
        var views = Array.Empty<ImageView>();
        var renderFinished = Array.Empty<Semaphore>();
        try
        {
            images = GetImages(swapchainApi, logicalDevice, handle);
            views = new ImageView[images.Length];
            renderFinished = new Semaphore[images.Length];
            CreateViews(vk, logicalDevice, images, format.Format, views);
            CreateRenderFinishedSemaphores(vk, logicalDevice, renderFinished);
            NameObjects(instance, logicalDevice, handle, images, views);
            Log(
                logger,
                reason,
                format,
                presentMode,
                requestedPresentMode,
                extent,
                oldExtent,
                imageCount);

            return new WindowedSwapchain(
                vk,
                logicalDevice,
                swapchainApi,
                logger,
                handle,
                images,
                views,
                renderFinished,
                extent,
                format.Format,
                format.ColorSpace,
                presentMode,
                requestedPresentMode);
        }
        catch
        {
            DestroyViews(vk, logicalDevice, logger, views);
            DestroySwapchain(vk, swapchainApi, logicalDevice, logger, handle);
            DestroySemaphores(vk, logicalDevice, logger, renderFinished);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <summary>Destroys the image views and swapchain; idempotent and never throws.</summary>
    internal void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        DestroyViews(_vk, _device, _logger, _views);
        DestroySwapchain(_vk, _swapchainApi, _device, _logger, Handle);
        DestroySemaphores(_vk, _device, _logger, _renderFinished);
    }

    private static VulkanSurfaceFormat SelectSurfaceFormat(
        KhrSurface surfaceApi,
        PhysicalDevice physicalDevice,
        SurfaceKHR surface)
    {
        uint formatCount = 0;
        Result result = surfaceApi.GetPhysicalDeviceSurfaceFormats(
            physicalDevice,
            surface,
            ref formatCount,
            (SurfaceFormatKHR*)null);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkGetPhysicalDeviceSurfaceFormatsKHR failed with VkResult {result}.");
        }

        if (formatCount == 0)
        {
            throw new Bork2dException("The Vulkan surface reports no supported formats.");
        }

        var formats = new SurfaceFormatKHR[formatCount];
        fixed (SurfaceFormatKHR* pointer = formats)
        {
            result = surfaceApi.GetPhysicalDeviceSurfaceFormats(
                physicalDevice,
                surface,
                ref formatCount,
                pointer);
        }

        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkGetPhysicalDeviceSurfaceFormatsKHR failed with VkResult {result}.");
        }

        var available = new VulkanSurfaceFormat[formatCount];
        for (var index = 0; index < available.Length; index++)
        {
            available[index] = new VulkanSurfaceFormat(
                formats[index].Format,
                formats[index].ColorSpace);
        }

        return SurfaceFormatPolicy.SelectWindowed(available);
    }

    private static PresentModeKHR SelectPresentMode(
        KhrSurface surfaceApi,
        PhysicalDevice physicalDevice,
        SurfaceKHR surface,
        bool vsync,
        out PresentModeKHR requestedPresentMode)
    {
        uint modeCount = 0;
        Result result = surfaceApi.GetPhysicalDeviceSurfacePresentModes(
            physicalDevice,
            surface,
            ref modeCount,
            (PresentModeKHR*)null);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkGetPhysicalDeviceSurfacePresentModesKHR failed with VkResult {result}.");
        }

        if (modeCount == 0)
        {
            throw new Bork2dException("The Vulkan surface reports no supported present modes.");
        }

        var modes = new PresentModeKHR[modeCount];
        fixed (PresentModeKHR* pointer = modes)
        {
            result = surfaceApi.GetPhysicalDeviceSurfacePresentModes(
                physicalDevice,
                surface,
                ref modeCount,
                pointer);
        }

        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkGetPhysicalDeviceSurfacePresentModesKHR failed with VkResult {result}.");
        }

        requestedPresentMode = vsync ? PresentModeKHR.FifoKhr : PresentModeKHR.MailboxKhr;
        return PresentModePolicy.Select(vsync, modes);
    }

    private static Extent2D ResolveExtent(
        SurfaceCapabilitiesKHR capabilities,
        WindowExtent requestedExtent)
    {
        Extent2D extent;
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            // A defined current extent is the surface's actual size and cannot be changed.
            extent = capabilities.CurrentExtent;
        }
        else
        {
            var requested = new Extent2D(
                (uint)Math.Max(0, requestedExtent.Width),
                (uint)Math.Max(0, requestedExtent.Height));
            extent = SwapchainPolicy.ClampExtent(
                requested,
                capabilities.MinImageExtent,
                capabilities.MaxImageExtent);
        }

        if (SwapchainPolicy.IsZeroOrMinimized(extent))
        {
            throw new Bork2dException(
                "A zero-extent Vulkan swapchain must not be created; the window is minimized.");
        }

        return extent;
    }

    private static Image[] GetImages(
        KhrSwapchain swapchainApi,
        Device device,
        SwapchainKHR handle)
    {
        uint imageCount = 0;
        Result result = swapchainApi.GetSwapchainImages(
            device,
            handle,
            ref imageCount,
            (Image*)null);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkGetSwapchainImagesKHR failed with VkResult {result}.");
        }

        if (imageCount == 0)
        {
            throw new Bork2dException("vkGetSwapchainImagesKHR reported zero images.");
        }

        var images = new Image[imageCount];
        fixed (Image* pointer = images)
        {
            result = swapchainApi.GetSwapchainImages(device, handle, ref imageCount, pointer);
        }

        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkGetSwapchainImagesKHR failed with VkResult {result}.");
        }

        return images;
    }

    private static void CreateViews(
        Vk vk,
        Device device,
        Image[] images,
        Format format,
        ImageView[] views)
    {
        var range = new ImageSubresourceRange
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        var components = new ComponentMapping
        {
            R = ComponentSwizzle.Identity,
            G = ComponentSwizzle.Identity,
            B = ComponentSwizzle.Identity,
            A = ComponentSwizzle.Identity,
        };

        for (var index = 0; index < images.Length; index++)
        {
            var createInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = images[index],
                ViewType = ImageViewType.Type2D,
                Format = format,
                Components = components,
                SubresourceRange = range,
            };

            Result result = vk.CreateImageView(device, in createInfo, null, out views[index]);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkCreateImageView failed for swapchain image {index} with VkResult "
                        + $"{result}.");
            }
        }
    }

    private static void CreateRenderFinishedSemaphores(
        Vk vk,
        Device device,
        Semaphore[] renderFinished)
    {
        var createInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo,
        };
        for (var index = 0; index < renderFinished.Length; index++)
        {
            Result result = vk.CreateSemaphore(device, in createInfo, null, out renderFinished[index]);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkCreateSemaphore failed for swapchain image {index} with VkResult "
                        + $"{result}.");
            }
        }
    }

    private static void NameObjects(
        IVulkanInstance instance,
        Device device,
        SwapchainKHR handle,
        Image[] images,
        ImageView[] views)
    {
        instance.SetObjectName(
            device.Handle,
            ObjectType.SwapchainKhr,
            (nint)handle.Handle,
            "Bork2d swapchain");
        for (var index = 0; index < images.Length; index++)
        {
            instance.SetObjectName(
                device.Handle,
                ObjectType.Image,
                (nint)images[index].Handle,
                $"Bork2d swapchain image {index}");
            instance.SetObjectName(
                device.Handle,
                ObjectType.ImageView,
                (nint)views[index].Handle,
                $"Bork2d swapchain image view {index}");
        }
    }

    private static void Log(
        ILogger logger,
        SwapchainRecreateReason? reason,
        VulkanSurfaceFormat format,
        PresentModeKHR presentMode,
        PresentModeKHR requestedPresentMode,
        Extent2D extent,
        Extent2D oldExtent,
        uint imageCount)
    {
        if (reason is null)
        {
            logger.LogInformation(
                "Vulkan swapchain created: format={Format}, colorSpace={ColorSpace}, "
                    + "requestedPresentMode={RequestedPresentMode}, presentMode={PresentMode}, "
                    + "extent={Width}x{Height}, images={ImageCount}.",
                format.Format,
                format.ColorSpace,
                requestedPresentMode,
                presentMode,
                extent.Width,
                extent.Height,
                imageCount);
            return;
        }

        logger.LogDebug(
            "Vulkan swapchain recreated: reason={Reason}, oldExtent={OldWidth}x{OldHeight}, "
                + "newExtent={Width}x{Height}, format={Format}, presentMode={PresentMode}, "
                + "images={ImageCount}.",
            reason,
            oldExtent.Width,
            oldExtent.Height,
            extent.Width,
            extent.Height,
            format.Format,
            presentMode,
            imageCount);
    }

    private static void DestroyViews(
        Vk vk,
        Device device,
        ILogger logger,
        ImageView[] views)
    {
        for (var index = 0; index < views.Length; index++)
        {
            if (views[index].Handle == 0)
            {
                continue;
            }

            try
            {
                vk.DestroyImageView(device, views[index], null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan swapchain image view destruction failed.");
            }
        }
    }

    private static void DestroySemaphores(
        Vk vk,
        Device device,
        ILogger logger,
        Semaphore[] semaphores)
    {
        for (var index = 0; index < semaphores.Length; index++)
        {
            if (semaphores[index].Handle == 0)
            {
                continue;
            }

            try
            {
                vk.DestroySemaphore(device, semaphores[index], null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan swapchain semaphore destruction failed.");
            }
        }
    }

    private static void DestroySwapchain(
        Vk vk,
        KhrSwapchain swapchainApi,
        Device device,
        ILogger logger,
        SwapchainKHR handle)
    {
        if (handle.Handle == 0)
        {
            return;
        }

        try
        {
            swapchainApi.DestroySwapchain(device, handle, null);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Vulkan swapchain destruction failed.");
        }
    }
}
