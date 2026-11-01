using Bork2d.Engine;
using Bork2d.Engine.Assets.Loading;
using Bork2d.Engine.Input;
using Bork2d.Engine.Platform;
using Bork2d.Engine.Scene;
using Bork2d.Engine.Time;
using Bork2d.Render;
using Bork2d.Render.Vulkan;
using Bork2d.Runtime.Hosting;
using Bork2d.Runtime.Input;
using Bork2d.Runtime.Sdl;
using Bork2d.Runtime.Window;
using Microsoft.Extensions.Logging;

namespace Bork2d.Runtime;

/// <summary>
/// Configures and builds a <see cref="Bork2dApp"/> for a game.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
/// <remarks>
/// <para>
/// The builder is configured once and is single-use after a successful <see cref="Build"/>: every
/// configuration method and a second <see cref="Build"/> throw
/// <see cref="InvalidOperationException"/>. A failed <see cref="Build"/> does not consume the
/// builder, so the caller may fix the configuration and retry. The built app is owned and disposed
/// by the caller as before; later builder use cannot reconfigure it.
/// </para>
/// <para>
/// It has no thread affinity: configuration and <see cref="Build"/> may run on any thread.
/// <see cref="Build"/> validates the configuration and creates the logger, engine, and scene root;
/// it does not create a window, GPU resources, or threads.
/// </para>
/// <para>
/// The game is injected as a caller-constructed instance. There is no parameterless-constructor
/// constraint, reflection, or <c>Activator</c> usage.
/// </para>
/// </remarks>
public sealed class Bork2dAppBuilder<TLayer> where TLayer : struct, Enum
{
    private readonly Bork2dAppOptions<TLayer> _options = new();
    private bool _built;

    /// <summary>
    /// Gets the collected options. Exposed internally for tests; not part of the public API.
    /// </summary>
    internal Bork2dAppOptions<TLayer> Options => _options;

    /// <summary>
    /// Supplies the caller-constructed game instance.
    /// </summary>
    /// <param name="game">The game instance. It must stay alive through <c>Run</c>.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithGame(IGame<TLayer> game)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(game);

        _options.Game = game;
        _options.GameSetCount++;
        return this;
    }

    /// <summary>
    /// Sets the base assets pack path.
    /// </summary>
    /// <param name="path">
    /// A non-empty pack path. It is a filesystem path, not a canonical asset path; relative paths
    /// resolve against the process current directory when <c>Run</c> opens the pack, and absolute
    /// paths are accepted.
    /// </param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <remarks>
    /// The pack is not opened by <see cref="Build"/>: a missing or malformed base pack throws
    /// <see cref="Bork2dException"/> from <c>Run</c> before the window, platform, <c>Setup</c>, and
    /// <c>Teardown</c>. The default is <c>"assets.pck"</c>. Mutually exclusive with
    /// <see cref="WithoutAssets"/>.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithAssetsPack(string path)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The assets pack path must be non-empty.", nameof(path));
        }

        _options.AssetsPackPath = path;
        _options.AssetsPackSelected = true;
        return this;
    }

    /// <summary>
    /// Opts out of the base assets pack: the app starts with an empty virtual filesystem.
    /// </summary>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// No base pack is required or opened. <see cref="IAssets.Browse"/> returns an empty list,
    /// <see cref="IAssets.Exists"/> is <see langword="false"/>, and
    /// <see cref="IAssets.LoadData"/> and <see cref="IAssets.LoadAsset{T}"/> fault with an
    /// <c>AssetException</c> whose kind is <c>NotFound</c>. The asset service, including its one
    /// loader thread, still starts, so <see cref="IAssets.LoadPack"/> can mount overlays on top of
    /// the empty filesystem later (for example during <c>Setup</c>).
    /// </para>
    /// <para>
    /// Available in windowed, offscreen, and headless modes. It is mutually exclusive with
    /// <see cref="WithAssetsPack"/>: if both are called, <see cref="Build"/> throws
    /// <see cref="Bork2dException"/>. Calling it more than once is idempotent.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithoutAssets()
    {
        ThrowIfBuilt();
        _options.WithoutAssetsSelected = true;
        return this;
    }

    /// <summary>
    /// Configures Microsoft.Extensions.Logging for the app.
    /// </summary>
    /// <param name="configure">The logging configuration callback.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <remarks>
    /// The callback is stored and invoked exactly once by <see cref="Build"/>, after validation
    /// and minimum-level setup. When supplied, the callback owns provider configuration: the
    /// builder does not add its default pretty ZLogger console provider. When omitted, the
    /// default console provider is installed. The app owns and disposes the resulting factory.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithLogging(Action<ILoggingBuilder> configure)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(configure);
        _options.ConfigureLogging = configure;
        return this;
    }

    /// <summary>
    /// Sets the window title. Windowed mode only.
    /// </summary>
    /// <param name="title">The non-empty window title.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="title"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithTitle(string title)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(title);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("The window title must be non-empty.", nameof(title));
        }

        _options.Title = title;
        _options.WindowOptionsSet |= Bork2dWindowOptionFlags.Title;
        return this;
    }

    /// <summary>
    /// Sets the initial windowed framebuffer size.
    /// </summary>
    /// <param name="width">The positive width in pixels.</param>
    /// <param name="height">The positive height in pixels.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithSize(int width, int height)
    {
        ThrowIfBuilt();
        _options.Width = width;
        _options.Height = height;
        _options.WindowOptionsSet |= Bork2dWindowOptionFlags.Size;
        return this;
    }

    /// <summary>
    /// Sets whether the window is resizable. Windowed mode only.
    /// </summary>
    /// <param name="resizable">Whether the window may be resized.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithResizable(bool resizable)
    {
        ThrowIfBuilt();
        _options.Resizable = resizable;
        _options.WindowOptionsSet |= Bork2dWindowOptionFlags.Resizable;
        return this;
    }

    /// <summary>
    /// Sets whether the window starts fullscreen. Windowed mode only.
    /// </summary>
    /// <param name="fullscreen">Whether the window starts fullscreen.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithFullscreen(bool fullscreen)
    {
        ThrowIfBuilt();
        _options.Fullscreen = fullscreen;
        _options.WindowOptionsSet |= Bork2dWindowOptionFlags.Fullscreen;
        return this;
    }

    /// <summary>
    /// Sets the preferred vertical-sync mode. Windowed mode only.
    /// </summary>
    /// <param name="enabled">Whether vertical sync is preferred.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithVSync(bool enabled)
    {
        ThrowIfBuilt();
        _options.VSync = enabled;
        _options.WindowOptionsSet |= Bork2dWindowOptionFlags.VSync;
        return this;
    }

    /// <summary>
    /// Selects headless mode: no window, GPU, or render thread.
    /// </summary>
    /// <param name="ticksPerSecond">
    /// The required positive tick rate used by the paced headless loop.
    /// </param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Headless runs a paced loop: asset start, <c>Setup</c>, <c>Tick</c>… until
    /// <c>IEngine.Exit()</c>, one <c>Teardown</c>, asset stop, with no window, SDL, Vulkan loader,
    /// renderer, snapshot ring, or capture. Ticks follow a strict absolute deadline grid with a pure
    /// <c>Thread.Sleep</c> wait and unbounded catch-up after a stall; <c>Exit</c> stops the loop
    /// before the next tick.
    /// </para>
    /// <para>
    /// The headless <see cref="ITime"/> is fully synthesized: tick <c>0</c> reports
    /// <c>Time = DeltaTime = 0</c>; tick <c>N &gt;= 1</c> reports <c>Time = N / ticksPerSecond</c>
    /// and <c>DeltaTime = 1 / ticksPerSecond</c>. <see cref="WithMaxDeltaTime"/> does not clamp the
    /// synthesized step.
    /// </para>
    /// <para>
    /// The rate must be positive; <c>0</c> and negative values are <see cref="Bork2dException"/>
    /// errors from <see cref="Build"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithHeadless(int ticksPerSecond = 60)
    {
        ThrowIfBuilt();
        _options.Mode = Bork2dAppMode.Headless;
        _options.HeadlessSelected = true;
        _options.HeadlessTicksPerSecond = ticksPerSecond;
        return this;
    }

    /// <summary>
    /// Selects offscreen mode: rendering to a fixed internal target.
    /// </summary>
    /// <param name="width">The positive offscreen target width.</param>
    /// <param name="height">The positive offscreen target height.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <remarks>
    /// The run creates a fixed sRGB Vulkan target, publishes one snapshot per completed tick, and
    /// is backpressured by the snapshot ring; the renderer discards one frame per consumed snapshot
    /// until <c>IEngine.Exit()</c>. There is no surface, SDL window, input, or readback;
    /// <c>IEngine.Window</c> is a functional facade reporting the fixed target extent.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithOffscreen(int width, int height)
    {
        ThrowIfBuilt();
        _options.Mode = Bork2dAppMode.Offscreen;
        _options.OffscreenSelected = true;
        _options.OffscreenWidth = width;
        _options.OffscreenHeight = height;
        return this;
    }

    /// <summary>
    /// Sets the gameplay delta-time clamp.
    /// </summary>
    /// <param name="seconds">The positive clamp in seconds. This is not a pacing delay.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <remarks>
    /// The clamp applies to the windowed/offscreen <c>ITime.DeltaTime</c> only. The headless
    /// synthesized step is exactly <c>1 / ticksPerSecond</c> and is never clamped (M7 F7).
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithMaxDeltaTime(double seconds)
    {
        ThrowIfBuilt();
        _options.MaxDeltaTime = seconds;
        return this;
    }

    /// <summary>
    /// Sets whether GPU validation layers are requested.
    /// </summary>
    /// <param name="enabled">Whether validation is requested.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithValidation(bool enabled)
    {
        ThrowIfBuilt();
        _options.Validation = enabled;
        return this;
    }

    /// <summary>
    /// Sets the texture residency budget carried to the renderer's page store.
    /// </summary>
    /// <param name="bytes">
    /// The positive budget in bytes. The default is 1 GiB (1,073,741,824 bytes).
    /// </param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <remarks>
    /// The budget describes how much texture-page image memory the residency policy may keep
    /// resident. It is validated and carried now; eviction itself is not implemented, so a page
    /// created since the last asset mount stays GPU-resident.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithTextureResidencyBudget(long bytes)
    {
        ThrowIfBuilt();
        _options.TextureResidencyBudgetBytes = bytes;
        return this;
    }

    /// <summary>
    /// Sets the maximum texture upload bytes the renderer starts per frame.
    /// </summary>
    /// <param name="bytesPerFrame">
    /// The positive per-frame budget in bytes. The default is 128 MiB (134,217,728 bytes).
    /// </param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <remarks>
    /// Each page upload is charged its validated payload size, <c>width * height * 4</c>. The first
    /// queued upload of a frame always starts even when it alone exceeds the budget, so one large
    /// texture cannot starve. <see cref="Build"/> rejects a non-positive value.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dAppBuilder<TLayer> WithTextureUploadBudget(int bytesPerFrame)
    {
        ThrowIfBuilt();
        _options.TextureUploadBudgetBytes = bytesPerFrame;
        return this;
    }

    /// <summary>
    /// Supplies an internal test seam for the SDL adapter factory. Not part of the public API.
    /// </summary>
    /// <param name="factory">Creates the SDL seam on the OS/window thread.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <remarks>
    /// <see cref="Build"/> never invokes the factory and never touches SDL. Only a windowed
    /// <c>Run</c> invokes it, on the engine-owned OS/window thread. Tests use this to drive the real
    /// windowed loop and platform with an in-memory fake.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    internal Bork2dAppBuilder<TLayer> WithSdlApiFactory(Func<ISdlApi> factory)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(factory);
        _options.SdlApiFactory = factory;
        return this;
    }

    /// <summary>
    /// Supplies an internal test seam for the renderer factory. Not part of the public API.
    /// </summary>
    /// <param name="factory">Creates the renderer for windowed/offscreen runs.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <remarks>
    /// <see cref="Build"/> never invokes the factory. A windowed or offscreen <c>Run</c> creates the
    /// renderer through it; headless never constructs or invokes it. Tests use this to drive the
    /// real host lifecycle with a deterministic fake renderer.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    internal Bork2dAppBuilder<TLayer> WithRendererFactory(IRendererFactory factory)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(factory);
        _options.RendererFactory = factory;
        return this;
    }

    /// <summary>
    /// Supplies an internal test seam for the monotonic clock/wait used by the headless pacing grid
    /// and the windowed minimized tick cap. Not part of the public API.
    /// </summary>
    /// <param name="clock">The monotonic clock and wait seam.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <remarks>
    /// <see cref="Build"/> never calls it and creates no clock state. A headless run paces with it;
    /// a windowed run uses it only to keep the minimized tick-start interval at one thirtieth of a
    /// second; offscreen never sleeps. Tests inject a fake so schedule assertions are deterministic.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    internal Bork2dAppBuilder<TLayer> WithSchedulerClock(ISchedulerClock clock)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(clock);
        _options.SchedulerClock = clock;
        return this;
    }

    /// <summary>
    /// Supplies an internal test seam for the Vulkan instance factory. Not part of the public API.
    /// </summary>
    /// <param name="factory">Creates the platform/offscreen Vulkan instance.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <remarks>
    /// <see cref="Build"/> never invokes the factory and never loads Vulkan. A windowed or offscreen
    /// <c>Run</c> uses it lazily; headless never constructs it. Tests inject a fake so windowed runs
    /// do not need a Vulkan loader or GPU.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    internal Bork2dAppBuilder<TLayer> WithVulkanInstanceFactory(IVulkanInstanceFactory factory)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(factory);
        _options.VulkanInstanceFactory = factory;
        return this;
    }

    /// <summary>
    /// Validates the configuration and creates the app.
    /// </summary>
    /// <returns>The built app. The caller owns it and must dispose it.</returns>
    /// <remarks>
    /// <para>
    /// Validation runs first, then the app-owned logger factory is created, the startup
    /// mode/validation message is logged, and the internal engine, clock, an <b>unstarted</b> asset
    /// service, and the runtime are created. Windowed/offscreen apps get a monotonic
    /// <c>StopwatchTime</c> (first tick <c>0/0</c>, clamped delta); headless apps get a fully
    /// synthesized <c>SynthesizedTime</c> (<c>Time = tickIndex / rate</c>, <c>DeltaTime = 1 / rate</c>,
    /// no clamp). The factory and the asset service are disposed if creation fails after they were
    /// built.
    /// </para>
    /// <para>
    /// <see cref="Build"/> stays filesystem-free: it does not resolve a pack path, open a file,
    /// parse a pack, or start the asset loader thread. The base pack (or the
    /// <see cref="WithoutAssets"/> empty filesystem) is opened by <c>Run</c> before the window and
    /// <c>Setup</c>.
    /// </para>
    /// <para>
    /// Windowed apps are created with unattached internal window/input proxies; offscreen apps get
    /// a functional window facade reporting the fixed target extent and no input; headless apps
    /// keep the throwing window stub and no input. <see cref="Build"/> creates no SDL state, OS
    /// window, or thread in any mode.
    /// </para>
    /// <para>
    /// A successful <see cref="Build"/> consumes the builder: every configuration method and a
    /// second <see cref="Build"/> throw <see cref="InvalidOperationException"/>. A failed
    /// <see cref="Build"/> does not consume the builder, so the caller may fix the configuration
    /// and retry. The built app is owned and disposed by the caller as before; later builder use
    /// cannot reconfigure it.
    /// </para>
    /// </remarks>
    /// <exception cref="Bork2dException">The configuration is invalid.</exception>
    /// <exception cref="InvalidOperationException">
    /// The builder was already successfully built; a successful <see cref="Build"/> consumes it.
    /// </exception>
    public Bork2dApp Build()
    {
        ThrowIfBuilt();

        var game = Validate();

        ILoggerFactory loggerFactory = Bork2dLogging.CreateLoggerFactory(_options.ConfigureLogging);
        AssetService? assetService = null;
        try
        {
            var logger = loggerFactory.CreateLogger(Bork2dLogging.RuntimeCategory);
            logger.LogInformation(
                "Bork2d starting: mode={Mode}, validation=passed, layers={LayerType}",
                _options.Mode,
                typeof(TLayer).FullName);

            var time = CreateTime();
            var schedulerClock = _options.SchedulerClock ?? new SystemSchedulerClock();

            WindowProxy? windowProxy = null;
            InputProxy? inputProxy = null;
            IWindow window;
            IInput? input;
            if (_options.Mode == Bork2dAppMode.Windowed)
            {
                windowProxy = new WindowProxy();
                inputProxy = new InputProxy();
                window = windowProxy;
                input = inputProxy;
            }
            else if (_options.Mode == Bork2dAppMode.Offscreen)
            {
                window = new OffscreenWindow(
                    _options.OffscreenWidth,
                    _options.OffscreenHeight,
                    _options.Title);
                input = null;
            }
            else
            {
                window = new UnsupportedWindow();
                input = null;
            }

            // Unstarted: the constructor allocates the in-memory filesystem only. The base pack is
            // opened by Run() before the window and Setup (D-M3-01).
            assetService = new AssetService(
                _options.WithoutAssetsSelected ? null : _options.AssetsPackPath,
                loggerFactory);

            var engine = new GameEngine<TLayer>(
                time,
                window,
                assetService,
                input,
                loggerFactory);

            var runtime = new AppRuntime<TLayer>(
                engine,
                game,
                time,
                loggerFactory,
                assetService,
                _options,
                windowProxy,
                inputProxy,
                schedulerClock);

            var app = new Bork2dApp(runtime);

            // Set only after the app and its runtime exist: a failed Build stays retryable.
            _built = true;
            return app;
        }
        catch
        {
            assetService?.Dispose();
            if (loggerFactory is IDisposable disposable)
            {
                disposable.Dispose();
            }

            throw;
        }
    }

    private void ThrowIfBuilt()
    {
        if (_built)
        {
            throw new InvalidOperationException(
                "The builder was already built. A successful Build() consumes the builder; create "
                + "a new builder to configure another app.");
        }
    }

    private ITime CreateTime()
        => _options.Mode == Bork2dAppMode.Headless
            ? new SynthesizedTime()
            : new StopwatchTime(_options.MaxDeltaTime);

    private IGame<TLayer> Validate()
    {
        if (_options.GameSetCount == 0)
        {
            throw new Bork2dException(
                "A game instance is required. Call WithGame(...) with a caller-constructed instance "
                + "before Build().");
        }

        if (_options.GameSetCount > 1)
        {
            throw new Bork2dException(
                "WithGame may be called only once per builder; the game instance is ambiguous.");
        }

        if (_options.Game is not { } game)
        {
            throw new Bork2dException("A non-null game instance is required before Build().");
        }

        if (_options.HeadlessSelected && _options.OffscreenSelected)
        {
            throw new Bork2dException(
                "Headless and offscreen modes are mutually exclusive. Select exactly one mode.");
        }

        LayerValidation.Validate<TLayer>();

        switch (_options.Mode)
        {
            case Bork2dAppMode.Windowed:
                RequirePositive(_options.Width, "window width");
                RequirePositive(_options.Height, "window height");
                break;

            case Bork2dAppMode.Headless:
                RejectWindowOptions();
                if (_options.HeadlessTicksPerSecond <= 0)
                {
                    throw new Bork2dException(
                        "Headless ticks per second must be greater than zero; got "
                        + $"{_options.HeadlessTicksPerSecond}.");
                }

                break;

            case Bork2dAppMode.Offscreen:
                RejectWindowOptions();
                RequirePositive(_options.OffscreenWidth, "offscreen width");
                RequirePositive(_options.OffscreenHeight, "offscreen height");
                break;

            default:
                throw new Bork2dException($"Unknown app mode '{_options.Mode}'.");
        }

        if (_options.MaxDeltaTime <= 0 || double.IsNaN(_options.MaxDeltaTime))
        {
            throw new Bork2dException(
                $"Max delta time must be greater than zero; got {_options.MaxDeltaTime}.");
        }

        if (_options.TextureResidencyBudgetBytes <= 0)
        {
            throw new Bork2dException(
                "The texture residency budget must be greater than zero; got "
                + $"{_options.TextureResidencyBudgetBytes} bytes.");
        }

        if (_options.TextureUploadBudgetBytes <= 0)
        {
            throw new Bork2dException(
                "The texture upload budget must be greater than zero; got "
                + $"{_options.TextureUploadBudgetBytes} bytes per frame.");
        }

        if (string.IsNullOrWhiteSpace(_options.AssetsPackPath))
        {
            throw new Bork2dException("The assets pack path must be non-empty.");
        }

        if (_options.AssetsPackSelected && _options.WithoutAssetsSelected)
        {
            throw new Bork2dException(
                "WithAssetsPack and WithoutAssets are mutually exclusive. Select a base pack or the "
                + "empty asset filesystem, not both.");
        }

        if (string.IsNullOrWhiteSpace(_options.Title))
        {
            throw new Bork2dException("The window title must be non-empty.");
        }

        return game;
    }

    private void RejectWindowOptions()
    {
        if (_options.WindowOptionsSet != Bork2dWindowOptionFlags.None)
        {
            throw new Bork2dException(
                $"Window options ({_options.WindowOptionsSet}) cannot be combined with "
                + $"'{_options.Mode}' mode.");
        }
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new Bork2dException($"The {name} must be positive; got {value}.");
        }
    }
}
