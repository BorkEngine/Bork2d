using Bork2d.Api;
using Bork2d.Assets;
using Bork2d.Common;
using Bork2d.Runtime;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bork2d.Tests;

/// <summary>
/// Runtime integration for the M3 asset service: builder selection, startup/shutdown ordering, the
/// error matrix, overlays, empty-VFS behavior, logging, and post-shutdown disposal.
/// </summary>
/// <remarks>
/// Every pack is built into a fresh temporary directory with <see cref="PackBuilder"/>; there is no
/// committed binary fixture. Loads are awaited with a bounded timeout inside the game callbacks, so
/// the tests never sleep or poll.
/// </remarks>
public sealed class AppAssetRuntimeTests
{
    private static readonly TimeSpan _loadTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void BuildDoesNotTouchTheFileSystemAndAMissingBasePackFailsBeforeThePlatform()
    {
        string missingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"bork2d-missing-{Guid.NewGuid():N}");
        string missingPath = Path.Combine(missingDirectory, "assets.pck");
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithTitle("Missing pack")
            .WithAssetsPack(missingPath)
            .WithSdlApiFactory(() => sdl)
            .Build();

        Assert.False(File.Exists(missingPath));
        Assert.False(Directory.Exists(missingDirectory));

        Exception? failure = AppTestSupport.RunCaptured(app);

        var error = Assert.IsType<Bork2dException>(failure);
        Assert.Contains(missingPath, error.Message, StringComparison.Ordinal);
        Assert.Empty(sdl.Calls);
        Assert.Equal(0, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.TeardownCount);
    }

    [Theory]
    [InlineData("Headless")]
    [InlineData("Offscreen")]
    [InlineData("Windowed")]
    public void MissingDefaultAssetsPackFailsRunBeforeSetupInEveryMode(string mode)
    {
        Assert.False(File.Exists("assets.pck"));
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        using var app = CreateBuilder(mode, game, () => sdl).Build();

        Exception? failure = AppTestSupport.RunCaptured(app);

        Assert.IsType<Bork2dException>(failure);
        Assert.Equal(0, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.TeardownCount);
        Assert.Empty(sdl.Calls);
    }

    [Fact]
    public void MalformedBasePackFailsRunWithTheParseReason()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string malformedPath = directory.WriteBytes("malformed.pck", [1, 2, 3, 4]);
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithTitle("Malformed pack")
            .WithAssetsPack(malformedPath)
            .WithSdlApiFactory(() => sdl)
            .Build();

        Exception? failure = AppTestSupport.RunCaptured(app);

        var error = Assert.IsType<Bork2dException>(failure);
        Assert.Contains(malformedPath, error.Message, StringComparison.Ordinal);
        Assert.Contains("shorter than the 28-byte prefix", error.Message, StringComparison.Ordinal);
        Assert.Empty(sdl.Calls);
        Assert.Equal(0, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.TeardownCount);
    }

    [Fact]
    public void FailedAssetStartLeavesTheServiceDisposedAndProxiesDetached()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"bork2d-missing-{Guid.NewGuid():N}",
            "assets.pck");
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        var loggerFactory = new FakeLoggerFactory();
        var service = new AssetService(missingPath, loggerFactory);
        var time = new StopwatchTime(0.25);
        var window = new WindowProxy();
        var input = new InputProxy();
        var engine = new Engine<GameLayer>(time, window, service, input);
        var options = new Bork2dAppOptions<GameLayer>
        {
            Game = game,
            GameSetCount = 1,
            AssetsPackPath = missingPath,
            SdlApiFactory = () => sdl,
        };
        using var app = new Bork2dApp(
            new AppRuntime<GameLayer>(
                engine,
                game,
                time,
                loggerFactory,
                service,
                options,
                window,
                input));

        Exception? failure = AppTestSupport.RunCaptured(app);

        Assert.IsType<Bork2dException>(failure);
        Assert.Empty(sdl.Calls);
        Assert.Equal(0, game.SetupCount);
        Assert.Equal(0, game.TeardownCount);
        Assert.Null(service.LoaderThread);

        // The service is disposed for retained references; the engine getter never throws.
        Assert.Same(service, engine.Assets);
        Assert.Throws<ObjectDisposedException>(() => { _ = service.Browse(); });
        Assert.Throws<ObjectDisposedException>(() => { _ = service.LoadData("data/missing"); });
        Assert.Throws<ObjectDisposedException>(() => { _ = window.Title; });
        Assert.Throws<ObjectDisposedException>(() => { _ = input.IsKeyDown(KeyCode.A); });
    }

    [Theory]
    [InlineData("Headless")]
    [InlineData("Offscreen")]
    [InlineData("Windowed")]
    public void WithoutAssetsStartsAnEmptyVfsInEveryMode(string mode)
    {
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        IReadOnlyList<AssetInfo>? browse = null;
        bool? exists = null;
        Sprite sprite = default;
        Task<byte[]>? dataTask = null;
        Task<PackTestData>? assetTask = null;
        AssetService? service = null;
        game.OnSetup = engine =>
        {
            service = Assert.IsType<AssetService>(engine.Assets);
            browse = engine.Assets.Browse();
            exists = engine.Assets.Exists("data/missing");
            sprite = engine.Assets.LoadSprite("sprites/missing");
            dataTask = engine.Assets.LoadData("data/missing");
            assetTask = engine.Assets.LoadAsset<PackTestData>("data/config");
        };
        ExitOnFirstTick(mode, game);
        using var app = CreateBuilder(mode, game, () => sdl).WithoutAssets().Build();

        AppTestSupport.RunToCompletion(app);

        Assert.Empty(browse!);
        Assert.False(exists!.Value);
        Assert.Equal(Sprite.Empty, sprite);
        Assert.Equal(AssetErrorKind.NotFound, AssetErrorOf(dataTask!));
        Assert.Equal(AssetErrorKind.NotFound, AssetErrorOf(assetTask!));
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);

        var engine = game.Engine
            ?? throw new InvalidOperationException("Setup did not capture the engine.");
        Assert.Same(service, engine.Assets);
        Assert.NotNull(engine.Assets);
        Assert.Throws<ObjectDisposedException>(() => { _ = engine.Assets.Browse(); });
        Assert.Throws<ObjectDisposedException>(() => { _ = engine.Assets.Exists("data/missing"); });
        Assert.Throws<ObjectDisposedException>(() => { _ = engine.Assets.LoadSprite("sprites/missing"); });
        Assert.Throws<ObjectDisposedException>(() => { _ = engine.Assets.LoadData("data/missing"); });
        Assert.Throws<ObjectDisposedException>(() => { engine.Assets.LoadPack("overlays/missing.pck"); });
        Assert.Null(service!.LoaderThread);

        if (IsWindowed(mode))
        {
            Assert.Contains(sdl.Calls, call => call == "InitializeVideo");
        }
        else
        {
            Assert.Empty(sdl.Calls);
        }
    }

    [Fact]
    public void WithoutAssetsAllowsAnOverlayMountedInSetup()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string overlayPath = directory.WritePack(
            "overlay.pck",
            builder =>
            {
                builder.AddRaw("data/overlay", [7, 8, 9]);
                builder.AddMessagePack(
                    "data/overlay-config",
                    new PackTestData { Name = "overlay", Value = 11 });
            });
        var game = new ScriptedGame<GameLayer>();
        IReadOnlyList<AssetInfo>? browse = null;
        string? packPath = null;
        byte[]? overlayBytes = null;
        PackTestData? overlayConfig = null;
        game.OnSetup = engine =>
        {
            engine.Assets.LoadPack(overlayPath);
            browse = engine.Assets.Browse();
            packPath = Assert.Single(engine.Assets.Browse("data/overlay")).PackPath;
            overlayBytes = engine.Assets.LoadData("data/overlay")
                .WaitAsync(_loadTimeout)
                .GetAwaiter()
                .GetResult();
            overlayConfig = engine.Assets.LoadAsset<PackTestData>("data/overlay-config")
                .WaitAsync(_loadTimeout)
                .GetAwaiter()
                .GetResult();
        };
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();

        AppTestSupport.RunToCompletion(app);

        Assert.Equal(new[] { "data/overlay", "data/overlay-config" }, browse!.Select(info => info.Path));
        Assert.Equal(overlayPath, packPath);
        Assert.Equal(new byte[] { 7, 8, 9 }, overlayBytes);
        Assert.Equal("overlay", overlayConfig!.Name);
        Assert.Equal(11, overlayConfig.Value);
    }

    [Theory]
    [InlineData("Headless")]
    [InlineData("Offscreen")]
    [InlineData("Windowed")]
    public void BasePackAndSetupOverlayServeLoadsWithoutSdlOrGpuInHeadlessModes(string mode)
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string basePath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddMessagePack(SpriteTestSupport.TexturePath, SpriteTestSupport.Descriptor());
                builder.AddRaw(
                    SpriteTestSupport.PayloadPath,
                    SpriteTestSupport.Payload(SpriteTestSupport.Descriptor()));
                builder.AddMessagePack(SpriteTestSupport.SpritePath, SpriteTestSupport.Quad());
                builder.AddRaw("data/shared", [1]);
            });
        string overlayPath = directory.WritePack(
            "overlay.pck",
            builder =>
            {
                // Shadows the base pack's data/shared and adds a typed asset.
                builder.AddRaw("data/shared", [2, 3]);
                builder.AddMessagePack(
                    "data/config",
                    new PackTestData { Name = "overlay", Value = 9 });
            });

        var sdl = new FakeSdlApi();
        var sdlInvoked = false;
        var game = new ScriptedGame<GameLayer>();
        IReadOnlyList<AssetInfo>? browse = null;
        string? sharedPackPath = null;
        byte[]? sharedBytes = null;
        PackTestData? config = null;
        bool? exists = null;
        Sprite sprite = default;
        bool spriteReady = false;
        game.OnSetup = engine =>
        {
            AssetService service = Assert.IsType<AssetService>(engine.Assets);
            engine.Assets.LoadPack(overlayPath);
            browse = engine.Assets.Browse();
            sharedPackPath = Assert.Single(engine.Assets.Browse("data/shared")).PackPath;
            exists = engine.Assets.Exists("sprites/player")
                && engine.Assets.Exists("textures/player")
                && engine.Assets.Exists("textures/raw/player")
                && engine.Assets.Exists("data/shared")
                && engine.Assets.Exists("data/config");
            sharedBytes = engine.Assets.LoadData("data/shared")
                .WaitAsync(_loadTimeout)
                .GetAwaiter()
                .GetResult();
            config = engine.Assets.LoadAsset<PackTestData>("data/config")
                .WaitAsync(_loadTimeout)
                .GetAwaiter()
                .GetResult();
            sprite = engine.Assets.LoadSprite(SpriteTestSupport.SpritePath);
            spriteReady = service.GetSpriteCompletion(sprite)
                .WaitAsync(_loadTimeout)
                .GetAwaiter()
                .GetResult();
            Assert.True(service.TryGetSpriteResource(sprite, out SpriteResource? resource));
            Assert.True(resource.IsValid);
            Assert.False(service.TryGetTextureResource("textures/missing", out _));
        };
        ExitOnFirstTick(mode, game);
        using var app = CreateBuilder(
                mode,
                game,
                IsWindowed(mode)
                    ? () => sdl
                    : () =>
                    {
                        sdlInvoked = true;
                        throw new InvalidOperationException(
                            "Headless/offscreen asset apps must not initialize SDL.");
                    })
            .WithAssetsPack(basePath)
            .Build();

        AppTestSupport.RunToCompletion(app);

        Assert.False(sdlInvoked);
        Assert.True(exists!.Value);
        Assert.Contains(browse!, info => info.Path == "sprites/player");
        Assert.Contains(browse!, info => info.Path == "textures/player");
        Assert.Contains(browse!, info => info.Path == "textures/raw/player");
        Assert.Contains(browse!, info => info.Path == "data/config");
        Assert.Single(browse!, info => info.Path == "data/shared");
        Assert.Equal(5, browse!.Count);
        Assert.Equal(overlayPath, sharedPackPath);
        Assert.Equal(new byte[] { 2, 3 }, sharedBytes);
        Assert.Equal("overlay", config!.Name);
        Assert.Equal(9, config.Value);
        Assert.NotEqual(Sprite.Empty, sprite);
        Assert.True(spriteReady, "The sprite and descriptor CPU resources must load.");

        if (!IsWindowed(mode))
        {
            Assert.Empty(sdl.Calls);
        }
    }

    [Fact]
    public void WindowPlatformStartFailureStopsAssetsAndDetachesProxiesBeforeSetup()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string basePath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/base", [1]));
        var sdl = new FakeSdlApi { InitializeFailure = "SDL_Init failed for the test" };
        var game = new ScriptedGame<GameLayer>();
        var loggerFactory = new FakeLoggerFactory();
        var service = new AssetService(basePath, loggerFactory);
        var time = new StopwatchTime(0.25);
        var window = new WindowProxy();
        var input = new InputProxy();
        var engine = new Engine<GameLayer>(time, window, service, input);
        var options = new Bork2dAppOptions<GameLayer>
        {
            Game = game,
            GameSetCount = 1,
            AssetsPackPath = basePath,
            SdlApiFactory = () => sdl,
        };
        using var app = new Bork2dApp(
            new AppRuntime<GameLayer>(
                engine,
                game,
                time,
                loggerFactory,
                service,
                options,
                window,
                input));

        Exception? failure = AppTestSupport.RunCaptured(app);

        Assert.IsType<Bork2dException>(failure);
        Assert.Equal(0, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.TeardownCount);

        // The asset service started before the platform and was stopped again.
        Assert.Null(service.LoaderThread);
        Assert.Throws<ObjectDisposedException>(() => { _ = service.Browse(); });
        Assert.Throws<ObjectDisposedException>(() => { _ = engine.Assets.LoadData("data/base"); });

        // The proxies are detached even though the platform never came up.
        Assert.Throws<ObjectDisposedException>(() => { _ = window.Title; });
        Assert.Throws<ObjectDisposedException>(() => { _ = input.IsKeyDown(KeyCode.A); });
    }

    [Fact]
    public void SetupFailureShutsDownThePlatformStopsAssetsAndSkipsTeardown()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string basePath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/base", [1]));
        var sdl = new FakeSdlApi();
        var error = new InvalidOperationException("setup failed");
        var game = new ScriptedGame<GameLayer> { OnSetup = _ => throw error };
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithTitle("Setup failure")
            .WithAssetsPack(basePath)
            .WithSdlApiFactory(() => sdl)
            .Build();

        Exception? failure = AppTestSupport.RunCaptured(app);

        Assert.Same(error, failure);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.TeardownCount);
        Assert.Contains(sdl.Calls, call => call == "Quit");
        var retained = game.Engine
            ?? throw new InvalidOperationException("Setup did not capture the engine.");
        Assert.Throws<ObjectDisposedException>(() => { _ = retained.Assets.Browse(); });
        Assert.Throws<ObjectDisposedException>(() => { _ = retained.Window.Title; });
    }

    [Fact]
    public void TickAndTeardownFailureIsAnAggregateAndStopsAssets()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string basePath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/base", [1]));
        var sdl = new FakeSdlApi();
        var tickError = new InvalidOperationException("tick failed");
        var teardownError = new InvalidOperationException("teardown failed");
        var game = new ScriptedGame<GameLayer>
        {
            OnTick = () => throw tickError,
            OnTeardown = () => throw teardownError,
        };
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithTitle("Tick and teardown failure")
            .WithAssetsPack(basePath)
            .WithSdlApiFactory(() => sdl)
            .Build();

        Exception? failure = AppTestSupport.RunCaptured(app);

        var aggregate = Assert.IsType<AggregateException>(failure);
        Assert.Contains(tickError, aggregate.InnerExceptions);
        Assert.Contains(teardownError, aggregate.InnerExceptions);
        Assert.Equal(1, game.TeardownCount);
        var retained = game.Engine
            ?? throw new InvalidOperationException("Setup did not capture the engine.");
        Assert.Throws<ObjectDisposedException>(() => { _ = retained.Assets.Browse(); });
    }

    [Fact]
    public void DisposeBeforeRunDisposesTheUnstartedServiceAndRunAfterDisposeThrows()
    {
        var game = new ScriptedGame<GameLayer>();
        var loggerFactory = new FakeLoggerFactory();
        var service = new AssetService(basePackPath: null, loggerFactory);
        var time = new StopwatchTime(0.25);
        var window = new WindowProxy();
        var input = new InputProxy();
        var engine = new Engine<GameLayer>(time, window, service, input);
        var options = new Bork2dAppOptions<GameLayer>
        {
            Game = game,
            GameSetCount = 1,
            Mode = Bork2dAppMode.Headless,
        };
        var app = new Bork2dApp(
            new AppRuntime<GameLayer>(
                engine,
                game,
                time,
                loggerFactory,
                service,
                options,
                window,
                input));

        app.Dispose();
        app.Dispose();

        Assert.Null(service.LoaderThread);
        Assert.Throws<ObjectDisposedException>(() => app.Run());
        Assert.Equal(0, game.SetupCount);
        Assert.Throws<ObjectDisposedException>(() => { _ = service.Browse(); });
        Assert.Throws<ObjectDisposedException>(() => { _ = engine.Assets.Exists("data/missing"); });
    }

    [Fact]
    public void DisposeAfterRunDisposesTheStoppedServiceAndNoLoaderRemains()
    {
        var game = new ScriptedGame<GameLayer>();
        AssetService? service = null;
        game.OnSetup = engine => service = Assert.IsType<AssetService>(engine.Assets);
        var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();

        app.Run();

        Assert.NotNull(service);
        Assert.Null(service!.LoaderThread);
        Assert.Throws<ObjectDisposedException>(() => { _ = service.Browse(); });

        app.Dispose();
        app.Dispose();

        Assert.Null(service.LoaderThread);
        Assert.Throws<ObjectDisposedException>(() => { _ = service.Exists("data/missing"); });
    }

    [Fact]
    public void AssetLoggingCoversStartOverlayLoadAndStopWithoutPerTickNoise()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string basePath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/base", [1]));
        string overlayPath = directory.WritePack(
            "overlay.pck",
            builder => builder.AddRaw("data/overlay", [2]));
        var provider = new CapturingLoggerProvider();
        var game = new ScriptedGame<GameLayer>();
        game.OnSetup = engine =>
        {
            engine.Assets.LoadPack(overlayPath);
            engine.Assets.LoadData("data/base")
                .WaitAsync(_loadTimeout)
                .GetAwaiter()
                .GetResult();
        };
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithAssetsPack(basePath)
            .WithLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddProvider(provider);
            })
            .Build();

        AppTestSupport.RunToCompletion(app);

        var assets = provider.Entries
            .Where(entry => entry.Category == Bork2dLogging.AssetsCategory)
            .ToArray();

        Assert.Contains(assets, entry => entry.Level == LogLevel.Information
            && entry.Message.Contains("Asset service started", StringComparison.Ordinal)
            && entry.Message.Contains("mode Pack", StringComparison.Ordinal)
            && entry.Message.Contains(basePath, StringComparison.Ordinal)
            && entry.Message.Contains("entries 1", StringComparison.Ordinal));
        Assert.Contains(assets, entry => entry.Level == LogLevel.Information
            && entry.Message.Contains("Mounted asset pack", StringComparison.Ordinal)
            && entry.Message.Contains(overlayPath, StringComparison.Ordinal));
        Assert.Contains(assets, entry => entry.Level == LogLevel.Information
            && entry.Message.Contains("Asset service stopped", StringComparison.Ordinal));
        Assert.Contains(assets, entry => entry.Level == LogLevel.Debug
            && entry.Message.Contains("Asset loaded", StringComparison.Ordinal)
            && entry.Message.Contains("data/base", StringComparison.Ordinal));

        // One start, one overlay, one load, one stop: nothing is emitted per tick or per frame.
        Assert.Equal(4, assets.Length);
        Assert.DoesNotContain(assets, entry => entry.Message.Contains("Tick", StringComparison.Ordinal));
        Assert.DoesNotContain(assets, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public void BasePackValidationFailureLogsThePathAndReasonFromTheAssetsCategory()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string malformedPath = directory.WriteBytes("malformed.pck", [1, 2, 3, 4]);
        var provider = new CapturingLoggerProvider();
        var sdl = new FakeSdlApi();
        var game = new ScriptedGame<GameLayer>();
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithTitle("Malformed pack logging")
            .WithAssetsPack(malformedPath)
            .WithSdlApiFactory(() => sdl)
            .WithLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddProvider(provider);
            })
            .Build();

        Exception? failure = AppTestSupport.RunCaptured(app);

        Assert.IsType<Bork2dException>(failure);
        var error = Assert.Single(
            provider.Entries,
            entry => entry.Category == Bork2dLogging.AssetsCategory
                && entry.Level == LogLevel.Error);
        Assert.Contains(malformedPath, error.Message, StringComparison.Ordinal);
        Assert.Contains("shorter than the 28-byte prefix", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.Exception);
    }

    private static Bork2dAppBuilder<GameLayer> CreateBuilder(
        string mode,
        ScriptedGame<GameLayer> game,
        Func<ISdlApi> sdlFactory)
    {
        var builder = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithSdlApiFactory(sdlFactory);

        return ParseMode(mode) switch
        {
            Bork2dAppMode.Headless => builder.WithHeadless(),
            Bork2dAppMode.Offscreen => builder.WithOffscreen(320, 200),
            Bork2dAppMode.Windowed => builder.WithTitle("Asset runtime test"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown test mode."),
        };
    }

    private static void ExitOnFirstTick(string mode, ScriptedGame<GameLayer> game)
    {
        if (IsWindowed(mode))
        {
            game.OnTick = () => game.Engine?.Exit();
        }
    }

    private static AssetErrorKind AssetErrorOf<T>(Task<T> task)
    {
        var exception = Assert.Throws<AssetException>(() => task.GetAwaiter().GetResult());
        return exception.Kind;
    }

    private static bool IsWindowed(string mode) => ParseMode(mode) == Bork2dAppMode.Windowed;

    private static Bork2dAppMode ParseMode(string mode) => mode switch
    {
        "Headless" => Bork2dAppMode.Headless,
        "Offscreen" => Bork2dAppMode.Offscreen,
        "Windowed" => Bork2dAppMode.Windowed,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown test mode."),
    };
}
