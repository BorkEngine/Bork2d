using Bork2d.Engine;
using Bork2d.Engine.Scene;
using Bork2d.Render;
using Bork2d.Render.Vulkan;
using Bork2d.Runtime.Window;
using Microsoft.Extensions.Logging;

namespace Bork2d.Runtime.Hosting;

/// <summary>
/// Builds the renderer start information shared by the windowed and offscreen hosts and installs the
/// renderer-backed engine sources.
/// </summary>
/// <remarks>
/// The returned renderer is not started: the shared rendered-run lifecycle starts it, records a
/// synchronous factory/start failure, and still runs <c>Setup</c>. The renderer's stats and capture
/// source are installed here, before <c>Setup</c>, so the game can read stats and request a capture
/// from any callback (M8 §6.9). The stats closure is allocated once per renderer.
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class RendererComposition<TLayer> where TLayer : struct, Enum
{
    private readonly GameEngine<TLayer> _engine;
    private readonly Bork2dAppOptions<TLayer> _options;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Initializes a new instance of the <see cref="RendererComposition{TLayer}"/> class.</summary>
    /// <param name="engine">The engine whose assets and sources the renderer backs.</param>
    /// <param name="options">The builder-derived host options.</param>
    /// <param name="loggerFactory">The app-owned logger factory.</param>
    internal RendererComposition(
        GameEngine<TLayer> engine,
        Bork2dAppOptions<TLayer> options,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _engine = engine;
        _options = options;
        _loggerFactory = loggerFactory;
    }

    /// <summary>Creates the unstarted renderer for <paramref name="mode"/>.</summary>
    /// <param name="mode">The host renderer mode.</param>
    /// <param name="platform">
    /// The started windowed platform, or <see langword="null"/> for an offscreen run.
    /// </param>
    /// <returns>The renderer the host must start.</returns>
    /// <exception cref="Bork2dException">
    /// A windowed run was requested without the platform's published Vulkan instance and surface.
    /// </exception>
    internal IRenderer Create(RendererMode mode, SdlWindowPlatform? platform)
    {
        VulkanWindowSnapshot? snapshot = platform?.VulkanWindow;
        if (mode == RendererMode.Windowed && snapshot is null)
        {
            throw new Bork2dException(
                "The windowed platform did not publish a Vulkan instance and surface.");
        }

        IVulkanInstanceFactory instanceFactory =
            _options.VulkanInstanceFactory ?? new VulkanInstanceFactory();
        IRendererFactory factory = _options.RendererFactory
            ?? new VulkanRendererFactory(instanceFactory);

        int width;
        int height;
        if (mode == RendererMode.Windowed)
        {
            VulkanWindowSnapshot window = snapshot!;
            width = window.DrawableSize.Width;
            height = window.DrawableSize.Height;
        }
        else
        {
            width = _options.OffscreenWidth;
            height = _options.OffscreenHeight;
        }

        var startInfo = new RendererStartInfo
        {
            Mode = mode,
            Instance = snapshot?.Instance,
            InstanceHandle = snapshot?.Instance.Handle ?? 0,
            SurfaceHandle = snapshot?.SurfaceHandle ?? 0,
            Width = width,
            Height = height,
            VSync = _options.VSync,
            ValidationRequested = _options.Validation,
            LoggerFactory = _loggerFactory,
            Assets = _engine.Assets,
            CreateReplacementSurface = platform is null
                ? null
                : platform.CreateReplacementSurface,
        };

        IRenderer renderer = factory.Create(startInfo);

        // The renderer publishes the last completed frame's stats; the engine's getter reads it
        // without touching Vulkan. Headless never gets here and keeps returning default.
        _engine.SetRenderStatsSource(() => renderer.RenderStats);

        // The capture delegate is the renderer's thread-safe request queue; installing it before
        // Setup lets the game capture from any callback (M8 §6.9). Headless installs the
        // NotSupported source at Run start instead.
        _engine.SetCaptureSource(renderer.CaptureFrameAsync);

        return renderer;
    }
}
