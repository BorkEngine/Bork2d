using Bork2d.Engine;
using Bork2d.Render;
using Bork2d.Render.Vulkan;
using Bork2d.Runtime.Sdl;
using Bork2d.Runtime.Window;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bork2d.Tests.Platform.Window;

/// <summary>
/// Deterministic tests for the windowed platform's Vulkan wiring: instance/surface creation and
/// destruction on the OS thread (D-M6-09), start-failure cleanup, confirmed-state posts, and the
/// single surface-replacement recovery request (D-M6-16).
/// </summary>
/// <remarks>
/// The fake Vulkan instance and fake SDL window make the whole path runnable without a loader,
/// desktop session, or GPU.
/// </remarks>
public sealed class WindowPlatformVulkanTests
{
    private static readonly TimeSpan _postTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void StartCreatesTheInstanceAndSurfaceOnTheOsThreadAndDestroysThemInOrder()
    {
        var sdl = new FakeSdlApi();
        var instanceFactory = new FakeVulkanInstanceFactory();
        instanceFactory.Instance.RecordCall = sdl.RecordCall;
        var platform = new SdlWindowPlatform(
            WindowTestSupport.Options(title: "Vulkan wiring", validation: true),
            () => sdl,
            instanceFactory,
            NullLoggerFactory.Instance);

        try
        {
            platform.Start();

            VulkanWindowSnapshot snapshot = Assert.IsType<VulkanWindowSnapshot>(platform.VulkanWindow);
            var window = Assert.Single(sdl.Windows);
            Assert.Same(instanceFactory.Instance, snapshot.Instance);
            Assert.NotEqual(0, snapshot.SurfaceHandle);
            Assert.Equal(new Bork2d.Render.WindowExtent(1280, 720), snapshot.DrawableSize);
            Assert.Equal(1, instanceFactory.CreateCount);
            Assert.NotNull(instanceFactory.LastRequest);
            Assert.True(instanceFactory.LastRequest!.ValidationRequested);
            Assert.Equal("Vulkan wiring", instanceFactory.LastRequest.ApplicationName);
            Assert.Equal("Bork2d", instanceFactory.LastRequest.EngineName);
            Assert.Equal(
                new[] { "VK_KHR_surface", "VK_KHR_win32_surface" },
                instanceFactory.LastRequest.RequiredExtensions);
            Assert.Equal(1, window.CreateVulkanSurfaceCallCount);
            Assert.Equal(instanceFactory.Instance.Handle, window.LastVulkanSurfaceInstanceHandle);
            Assert.NotEqual(Environment.CurrentManagedThreadId, window.CreateVulkanSurfaceThreadId);

            platform.Shutdown();

            Assert.True(instanceFactory.Instance.DestroyCount == 1);
            Assert.Equal(snapshot.SurfaceHandle, Assert.Single(instanceFactory.Instance.DestroyedSurfaces));
            Assert.Null(platform.VulkanWindow);
            Assert.True(window.IsDisposed);
            Assert.True(sdl.QuitCalled);
            Assert.True(sdl.Disposed);

            var calls = sdl.Calls.ToList();
            int createSurface = calls.FindIndex(call => call.StartsWith(
                "CreateVulkanSurface(",
                StringComparison.Ordinal));
            int destroySurface = calls.FindIndex(call => call.StartsWith(
                "DestroySurface(",
                StringComparison.Ordinal));
            int destroyInstance = calls.IndexOf("DestroyInstance");
            int unload = calls.IndexOf("VulkanUnloadLibrary");
            int destroyWindow = calls.IndexOf("DestroyWindow");
            int quit = calls.LastIndexOf("Quit");
            int dispose = calls.IndexOf("Dispose");

            Assert.True(createSurface >= 0, "no surface was created.");
            Assert.True(destroySurface > createSurface, "the surface was not destroyed after creation.");
            Assert.True(destroyInstance > destroySurface, "the instance must be destroyed after the surface.");
            Assert.True(unload > destroyInstance, "the loader must be unloaded after the instance.");
            Assert.True(destroyWindow > unload, "the window must be destroyed after the Vulkan objects.");
            Assert.True(quit > destroyWindow, "SDL must quit after the window is destroyed.");
            Assert.True(dispose > quit, "the SDL adapter must be disposed after quitting.");
        }
        finally
        {
            platform.Dispose();
        }
    }

    [Fact]
    public void VulkanLoaderFailureIsAStartFailureBeforeSetupAndCleansUp()
    {
        var sdl = new FakeSdlApi { VulkanLoadFailure = "SDL_Vulkan_LoadLibrary failed: no loader" };
        var instanceFactory = new FakeVulkanInstanceFactory();
        var platform = new SdlWindowPlatform(
            WindowTestSupport.Options(),
            () => sdl,
            instanceFactory,
            NullLoggerFactory.Instance);

        var failure = Assert.Throws<Bork2dException>(platform.Start);

        Assert.Contains("no loader", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, instanceFactory.CreateCount);
        Assert.True(sdl.QuitCalled);
        Assert.True(sdl.Disposed);
        var window = Assert.Single(sdl.Windows);
        Assert.True(window.IsDisposed);
        platform.Dispose();
        platform.Dispose();
    }

    [Fact]
    public void InstanceFactoryFailureIsAStartFailureAndCleansUpTheWindowAndSdl()
    {
        var sdl = new FakeSdlApi();
        var instanceFactory = new FakeVulkanInstanceFactory
        {
            CreateException = new Bork2dException("no Vulkan 1.3 loader"),
        };
        var platform = new SdlWindowPlatform(
            WindowTestSupport.Options(),
            () => sdl,
            instanceFactory,
            NullLoggerFactory.Instance);

        var failure = Assert.Throws<Bork2dException>(platform.Start);

        Assert.Contains("no Vulkan 1.3 loader", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, instanceFactory.CreateCount);
        Assert.Equal(0, instanceFactory.Instance.DestroyCount);
        Assert.True(sdl.QuitCalled);
        Assert.True(sdl.Disposed);
        Assert.True(Assert.Single(sdl.Windows).IsDisposed);
        platform.Dispose();
    }

    [Fact]
    public void ExtensionQueryFailureIsAStartFailureAndCleansUp()
    {
        var sdl = new FakeSdlApi
        {
            OnWindowCreated = window =>
                window.VulkanExtensionsFailure = "SDL_Vulkan_GetInstanceExtensions failed: boom",
        };
        var instanceFactory = new FakeVulkanInstanceFactory();
        var platform = new SdlWindowPlatform(
            WindowTestSupport.Options(),
            () => sdl,
            instanceFactory,
            NullLoggerFactory.Instance);

        var failure = Assert.Throws<Bork2dException>(platform.Start);

        Assert.Contains("SDL_Vulkan_GetInstanceExtensions", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, instanceFactory.CreateCount);
        Assert.True(sdl.Disposed);
        Assert.True(Assert.Single(sdl.Windows).IsDisposed);
        platform.Dispose();
    }

    [Fact]
    public void SurfaceCreationFailureDestroysTheInstanceAndWindow()
    {
        var sdl = new FakeSdlApi
        {
            OnWindowCreated = window => window.VulkanSurfaceFailure = "SDL_Vulkan_CreateSurface failed: boom",
        };
        var instanceFactory = new FakeVulkanInstanceFactory();
        var platform = new SdlWindowPlatform(
            WindowTestSupport.Options(),
            () => sdl,
            instanceFactory,
            NullLoggerFactory.Instance);

        var failure = Assert.Throws<Bork2dException>(platform.Start);

        Assert.Contains("SDL_Vulkan_CreateSurface", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, instanceFactory.CreateCount);
        Assert.Equal(1, instanceFactory.Instance.DestroyCount);
        Assert.Empty(instanceFactory.Instance.DestroyedSurfaces);
        Assert.True(sdl.QuitCalled);
        Assert.True(sdl.Disposed);
        Assert.True(Assert.Single(sdl.Windows).IsDisposed);
        Assert.Null(platform.VulkanWindow);
        platform.Dispose();
    }

    [Fact]
    public void AttachRendererPostsExtentFullscreenAndMinimizedButNotTitleOrFocus()
    {
        var sdl = new FakeSdlApi();
        var platform = WindowTestSupport.Start(sdl);
        var renderer = new FakeRenderer { AutoReady = true };

        try
        {
            platform.AttachRenderer(renderer);
            Assert.True(
                renderer.WaitForPosts(
                    () => renderer.ExtentPostCount >= 1
                        && renderer.FullscreenPostCount >= 1
                        && renderer.MinimizedPostCount >= 1,
                    _postTimeout),
                "the platform did not post the initial confirmed state.");
            Assert.Equal(new Bork2d.Render.WindowExtent(1280, 720), renderer.LastExtent);
            Assert.False(renderer.LastFullscreen);
            Assert.False(renderer.LastMinimized);

            int extentPosts = renderer.ExtentPostCount;
            int fullscreenPosts = renderer.FullscreenPostCount;
            int minimizedPosts = renderer.MinimizedPostCount;
            var proxy = WindowTestSupport.Attach(platform);
            var window = Assert.Single(sdl.Windows);

            proxy.Title = "renamed";
            window.SetFocused(true);
            sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowFocusGained));
            WindowTestSupport.WaitUntil(platform, () => platform.State.Title == "renamed"
                && platform.State.Focused);

            platform.SetFullscreen(true);
            Assert.True(
                renderer.WaitForPosts(() => renderer.LastFullscreen == true, _postTimeout),
                "the platform did not post the fullscreen change.");
            Assert.Equal(extentPosts, renderer.ExtentPostCount);
            Assert.Equal(fullscreenPosts + 1, renderer.FullscreenPostCount);
            Assert.Equal(minimizedPosts, renderer.MinimizedPostCount);

            window.SetMinimized(true);
            sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowMinimized));
            Assert.True(
                renderer.WaitForPosts(() => renderer.LastMinimized == true, _postTimeout),
                "the platform did not post the minimized state.");
            Assert.Equal(new Bork2d.Render.WindowExtent(1280, 720), renderer.LastExtent);

            window.SetMinimized(false);
            sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowRestored));
            Assert.True(
                renderer.WaitForPosts(() => renderer.LastMinimized == false, _postTimeout),
                "the platform did not post the restored state.");

            window.Resize(1024, 768);
            sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowSizeChanged, 1024, 768));
            Assert.True(
                renderer.WaitForPosts(
                    () => renderer.LastExtent == new Bork2d.Render.WindowExtent(1024, 768),
                    _postTimeout),
                "the platform did not post the new extent.");

            proxy.Detach();
            platform.Shutdown();
        }
        finally
        {
            platform.Dispose();
        }
    }

    [Fact]
    public void SurfaceReplacementCreatesNewSurfaceDestroysOldAndPublishes()
    {
        var sdl = new FakeSdlApi();
        var instanceFactory = new FakeVulkanInstanceFactory();
        var platform = WindowTestSupport.Start(sdl, instanceFactory: instanceFactory);

        try
        {
            VulkanWindowSnapshot original = Assert.IsType<VulkanWindowSnapshot>(platform.VulkanWindow);
            var window = Assert.Single(sdl.Windows);
            window.VulkanSurfaceHandle = 0x7777;

            nint replacement = platform.CreateReplacementSurface();

            Assert.Equal((nint)0x7777, replacement);
            Assert.Equal(2, window.CreateVulkanSurfaceCallCount);
            Assert.Equal(original.SurfaceHandle, Assert.Single(instanceFactory.Instance.DestroyedSurfaces));
            VulkanWindowSnapshot updated = Assert.IsType<VulkanWindowSnapshot>(platform.VulkanWindow);
            Assert.Equal(replacement, updated.SurfaceHandle);
            Assert.Same(original.Instance, updated.Instance);

            platform.Shutdown();
            Assert.Equal(replacement, Assert.Single(instanceFactory.Instance.DestroyedSurfaces, handle => handle == replacement));
        }
        finally
        {
            platform.Dispose();
        }
    }

    [Fact]
    public void SurfaceReplacementFailureReturnsZeroAndLeavesTheOldSurface()
    {
        var sdl = new FakeSdlApi();
        var instanceFactory = new FakeVulkanInstanceFactory();
        var platform = WindowTestSupport.Start(sdl, instanceFactory: instanceFactory);

        try
        {
            VulkanWindowSnapshot original = Assert.IsType<VulkanWindowSnapshot>(platform.VulkanWindow);
            var window = Assert.Single(sdl.Windows);
            window.VulkanSurfaceFailure = "SDL_Vulkan_CreateSurface failed: replacement";

            nint replacement = platform.CreateReplacementSurface();

            Assert.Equal(0, replacement);
            Assert.Empty(instanceFactory.Instance.DestroyedSurfaces);
            Assert.Equal(original.SurfaceHandle, platform.VulkanWindow!.SurfaceHandle);
        }
        finally
        {
            platform.Dispose();
        }
    }
}
