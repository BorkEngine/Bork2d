using Bork2d.Api;
using Bork2d.Api.Input;
using Bork2d.Api.Platform;
using Bork2d.Assets.Loading;
using Bork2d.Common.Scene;
using Bork2d.Common.Time;
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
/// The builder is configured and built once. It has no thread affinity: configuration and
/// <see cref="Build"/> may run on any thread. <see cref="Build"/> validates the configuration and
/// creates the logger, engine, and scene root; it does not create a window, GPU resources, or
/// threads.
/// </para>
/// <para>
/// The game is injected as a caller-constructed instance. There is no parameterless-constructor
/// constraint, reflection, or <c>Activator</c> usage.
/// </para>
/// </remarks>
public sealed class Bork2dAppBuilder<TLayer> where TLayer : struct, Enum
{
    private readonly Bork2dAppOptions<TLayer> _options = new();

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
    public Bork2dAppBuilder<TLayer> WithGame(IGame<TLayer> game)
    {
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
    public Bork2dAppBuilder<TLayer> WithAssetsPack(string path)
    {
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
    public Bork2dAppBuilder<TLayer> WithoutAssets()
    {
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
    public Bork2dAppBuilder<TLayer> WithLogging(Action<ILoggingBuilder> configure)
    {
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
    public Bork2dAppBuilder<TLayer> WithTitle(string title)
    {
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
    public Bork2dAppBuilder<TLayer> WithSize(int width, int height)
    {
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
    public Bork2dAppBuilder<TLayer> WithResizable(bool resizable)
    {
        _options.Resizable = resizable;
        _options.WindowOptionsSet |= Bork2dWindowOptionFlags.Resizable;
        return this;
    }

    /// <summary>
    /// Sets whether the window starts fullscreen. Windowed mode only.
    /// </summary>
    /// <param name="fullscreen">Whether the window starts fullscreen.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public Bork2dAppBuilder<TLayer> WithFullscreen(bool fullscreen)
    {
        _options.Fullscreen = fullscreen;
        _options.WindowOptionsSet |= Bork2dWindowOptionFlags.Fullscreen;
        return this;
    }

    /// <summary>
    /// Sets the preferred vertical-sync mode. Windowed mode only.
    /// </summary>
    /// <param name="enabled">Whether vertical sync is preferred.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public Bork2dAppBuilder<TLayer> WithVSync(bool enabled)
    {
        _options.VSync = enabled;
        _options.WindowOptionsSet |= Bork2dWindowOptionFlags.VSync;
        return this;
    }

    /// <summary>
    /// Selects headless mode: no window, GPU, or render thread.
    /// </summary>
    /// <param name="ticksPerSecond">The tick rate; <c>0</c> means uncapped.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public Bork2dAppBuilder<TLayer> WithHeadless(int ticksPerSecond = 60)
    {
        _options.Mode = Bork2dAppMode.Headless;
        _options.HeadlessSelected = true;
        _options.HeadlessTicksPerSecond = ticksPerSecond;
        return this;
    }

    /// <summary>
    /// Selects offscreen mode: windowless rendering to an internal target.
    /// </summary>
    /// <param name="width">The positive offscreen target width.</param>
    /// <param name="height">The positive offscreen target height.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public Bork2dAppBuilder<TLayer> WithOffscreen(int width, int height)
    {
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
    public Bork2dAppBuilder<TLayer> WithMaxDeltaTime(double seconds)
    {
        _options.MaxDeltaTime = seconds;
        return this;
    }

    /// <summary>
    /// Sets whether GPU validation layers are requested.
    /// </summary>
    /// <param name="enabled">Whether validation is requested.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public Bork2dAppBuilder<TLayer> WithValidation(bool enabled)
    {
        _options.Validation = enabled;
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
    internal Bork2dAppBuilder<TLayer> WithSdlApiFactory(Func<ISdlApi> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _options.SdlApiFactory = factory;
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
    /// service, and the runtime are created. The factory and the asset service are disposed if
    /// creation fails after they were built.
    /// </para>
    /// <para>
    /// <see cref="Build"/> stays filesystem-free: it does not resolve a pack path, open a file,
    /// parse a pack, or start the asset loader thread. The base pack (or the
    /// <see cref="WithoutAssets"/> empty filesystem) is opened by <c>Run</c> before the window and
    /// <c>Setup</c>.
    /// </para>
    /// <para>
    /// Windowed apps are created with unattached internal window/input proxies; headless and
    /// offscreen apps keep the throwing window stub and no input. <see cref="Build"/> creates no
    /// SDL state, window, or thread in any mode.
    /// </para>
    /// </remarks>
    /// <exception cref="Bork2dException">The configuration is invalid.</exception>
    public Bork2dApp Build()
    {
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

            var time = new StopwatchTime(_options.MaxDeltaTime);

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

            var engine = new Engine<TLayer>(
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
                inputProxy);
            return new Bork2dApp(runtime);
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
                if (_options.HeadlessTicksPerSecond < 0)
                {
                    throw new Bork2dException(
                        $"Headless ticks per second must be >= 0; got {_options.HeadlessTicksPerSecond}.");
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
