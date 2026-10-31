using Bork2d.Engine;
using Bork2d.Runtime.Sdl;
using Bork2d.Tests.TestDoubles;
using Xunit;

namespace Bork2d.Tests.Platform.Sdl;

/// <summary>
/// Deterministic tests for the Vulkan members of the SDL seam: loader lifecycle, instance extension
/// query, surface creation, and drawable size.
/// </summary>
/// <remarks>
/// The fake models the success and failure of each call without loading <c>SDL2.dll</c> or
/// <c>vulkan-1.dll</c>; the real calls are exercised by the opt-in windowed GPU test.
/// </remarks>
public sealed class SdlVulkanSeamTests
{
    [Fact]
    public void VulkanLoaderMustBeLoadedBeforeTheExtensionQueryAndIsIdempotent()
    {
        var api = new FakeSdlApi();
        api.InitializeVideo();
        using var window = (FakeSdlWindow)api.CreateWindow(
            "Bork2d",
            1280,
            720,
            SdlWindowFlags.Vulkan);

        var notLoaded = Assert.Throws<Bork2dException>(window.GetVulkanInstanceExtensions);
        Assert.Contains("SDL_Vulkan_LoadLibrary", notLoaded.Message, StringComparison.Ordinal);

        api.VulkanLoadLibrary();
        api.VulkanLoadLibrary();

        Assert.True(api.VulkanLibraryLoaded);
        Assert.Equal(1, api.VulkanLoadCount);
        Assert.Equal(
            new[] { "VK_KHR_surface", "VK_KHR_win32_surface" },
            window.GetVulkanInstanceExtensions());

        api.VulkanUnloadLibrary();
        api.VulkanUnloadLibrary();

        Assert.False(api.VulkanLibraryLoaded);
        Assert.Equal(1, api.VulkanUnloadCount);
        Assert.Throws<Bork2dException>(window.GetVulkanInstanceExtensions);
    }

    [Fact]
    public void VulkanLoaderFailureIsReportedAsABork2dException()
    {
        var api = new FakeSdlApi { VulkanLoadFailure = "SDL_Vulkan_LoadLibrary failed: test" };
        api.InitializeVideo();

        var exception = Assert.Throws<Bork2dException>(api.VulkanLoadLibrary);

        Assert.Contains("SDL_Vulkan_LoadLibrary", exception.Message, StringComparison.Ordinal);
        Assert.False(api.VulkanLibraryLoaded);
    }

    [Fact]
    public void VulkanExtensionListIsTheFakeWindowsListAndCanBeScripted()
    {
        var api = new FakeSdlApi();
        api.InitializeVideo();
        api.VulkanLoadLibrary();
        using var window = (FakeSdlWindow)api.CreateWindow(
            "Bork2d",
            640,
            480,
            SdlWindowFlags.Vulkan);
        window.VulkanInstanceExtensions = ["VK_KHR_surface", "VK_KHR_win32_surface", "VK_EXT_debug_utils"];

        Assert.Equal(
            new[] { "VK_KHR_surface", "VK_KHR_win32_surface", "VK_EXT_debug_utils" },
            window.GetVulkanInstanceExtensions());
    }

    [Fact]
    public void VulkanExtensionQueryFailureIsReportedAsABork2dException()
    {
        var api = new FakeSdlApi();
        api.InitializeVideo();
        api.VulkanLoadLibrary();
        using var window = (FakeSdlWindow)api.CreateWindow(
            "Bork2d",
            640,
            480,
            SdlWindowFlags.Vulkan);
        window.VulkanExtensionsFailure =
            "SDL_Vulkan_GetInstanceExtensions failed: no Vulkan loader";

        var exception = Assert.Throws<Bork2dException>(window.GetVulkanInstanceExtensions);

        Assert.Contains("SDL_Vulkan_GetInstanceExtensions", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VulkanSurfaceCreationReturnsTheOpaqueHandleAndRecordsTheInstance()
    {
        var api = new FakeSdlApi();
        api.InitializeVideo();
        api.VulkanLoadLibrary();
        using var window = (FakeSdlWindow)api.CreateWindow(
            "Bork2d",
            640,
            480,
            SdlWindowFlags.Vulkan);
        window.VulkanSurfaceHandle = 0xABCDUL;

        nint surface = window.CreateVulkanSurface(0x1234);

        Assert.Equal((nint)0xABCD, surface);
        Assert.Equal(1, window.CreateVulkanSurfaceCallCount);
        Assert.Equal((nint)0x1234, window.LastVulkanSurfaceInstanceHandle);
        Assert.Contains("CreateVulkanSurface(0x1234)", api.Calls);
    }

    [Fact]
    public void VulkanSurfaceFailureIsReportedWithTheSdlError()
    {
        var api = new FakeSdlApi();
        api.InitializeVideo();
        api.VulkanLoadLibrary();
        using var window = (FakeSdlWindow)api.CreateWindow(
            "Bork2d",
            640,
            480,
            SdlWindowFlags.Vulkan);
        window.VulkanSurfaceFailure = "SDL_Vulkan_CreateSurface failed: test";

        var exception = Assert.Throws<Bork2dException>(() => window.CreateVulkanSurface(0x1234));

        Assert.Contains("SDL_Vulkan_CreateSurface", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VulkanSurfaceCreationRequiresAnInstanceHandle()
    {
        var api = new FakeSdlApi();
        api.InitializeVideo();
        api.VulkanLoadLibrary();
        using var window = (FakeSdlWindow)api.CreateWindow(
            "Bork2d",
            640,
            480,
            SdlWindowFlags.Vulkan);

        var exception = Assert.Throws<Bork2dException>(() => window.CreateVulkanSurface(0));

        Assert.Contains("VkInstance", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VulkanDrawableSizeIsTheFramebufferAuthority()
    {
        var api = new FakeSdlApi { WindowScale = 1.5f };
        api.InitializeVideo();
        api.VulkanLoadLibrary();
        using var window = (FakeSdlWindow)api.CreateWindow(
            "Bork2d",
            800,
            600,
            SdlWindowFlags.Vulkan | SdlWindowFlags.AllowHighDpi);

        Assert.Equal(new SdlWindowSize(1200, 900), window.GetVulkanDrawableSize());

        window.SetVulkanDrawableSize(1600, 1000);
        Assert.Equal(new SdlWindowSize(1600, 1000), window.GetVulkanDrawableSize());

        window.ClearVulkanDrawableSizeOverride();
        window.SetSize(1000, 500);
        Assert.Equal(new SdlWindowSize(1500, 750), window.GetVulkanDrawableSize());
    }

    [Fact]
    public void DisposedFakeApiIsVulkanUnloadSafeAndRejectsLoading()
    {
        var api = new FakeSdlApi();
        api.InitializeVideo();

        api.VulkanUnloadLibrary();
        Assert.Equal(0, api.VulkanUnloadCount);

        api.Dispose();
        api.VulkanUnloadLibrary();

        Assert.Throws<ObjectDisposedException>(api.VulkanLoadLibrary);
    }

    [Fact]
    public void DisposedFakeWindowRejectsVulkanCalls()
    {
        var api = new FakeSdlApi();
        api.InitializeVideo();
        api.VulkanLoadLibrary();
        var window = api.CreateWindow("Bork2d", 320, 200, SdlWindowFlags.Vulkan);
        window.Dispose();

        Assert.Throws<ObjectDisposedException>(() => window.GetVulkanInstanceExtensions());
        Assert.Throws<ObjectDisposedException>(() => window.CreateVulkanSurface(1));
        Assert.Throws<ObjectDisposedException>(() => window.GetVulkanDrawableSize());
    }
}
