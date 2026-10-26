using Bork2d.Api;
using Bork2d.Runtime;
using Bork2d.Runtime.Hosting;
using Bork2d.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bork2d.Tests.Runtime;

public sealed class LoggingTests
{
    [Fact]
    public void WithLoggingActionIsInvokedExactlyOnceAtBuild()
    {
        var calls = 0;

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithHeadless()
            .WithLogging(_ => calls++)
            .Build();

        Assert.Equal(1, calls);
    }

    [Fact]
    public void WithLoggingIsNotInvokedWhenOnlyConfigured()
    {
        var calls = 0;
        _ = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithLogging(_ => calls++);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void StartupLogReportsModeAndValidationFromRuntimeCategory()
    {
        var provider = new CapturingLoggerProvider();

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithHeadless()
            .WithLogging(logging => logging.AddProvider(provider))
            .Build();

        var entry = Assert.Single(
            provider.Entries,
            captured => captured.Category == Bork2dLogging.RuntimeCategory);

        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("mode=Headless", entry.Message, StringComparison.Ordinal);
        Assert.Contains("validation=passed", entry.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(GameLayer), entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Windowed")]
    [InlineData("Offscreen")]
    [InlineData("Headless")]
    public void StartupLogNamesTheSelectedMode(string mode)
    {
        var provider = new CapturingLoggerProvider();
        var builder = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithLogging(logging => logging.AddProvider(provider));

        switch (mode)
        {
            case "Windowed":
                break;

            case "Offscreen":
                builder.WithOffscreen(320, 200);
                break;

            case "Headless":
                builder.WithHeadless();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown test mode.");
        }

        using var app = builder.Build();

        var entry = Assert.Single(
            provider.Entries,
            captured => captured.Category == Bork2dLogging.RuntimeCategory);
        Assert.Contains($"mode={mode}", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineLoggerFactoryIsUsableInSetup()
    {
        var provider = new CapturingLoggerProvider();
        var game = new ScriptedGame<GameLayer>
        {
            OnSetup = engine =>
            {
                engine.LoggerFactory.CreateLogger(Bork2dLogging.CommonCategory)
                    .LogInformation("setup common {Value}", 42);
                engine.LoggerFactory.CreateLogger("MyGame").LogWarning("custom category");
            },
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .WithLogging(logging => logging.AddProvider(provider))
            .Build();

        app.Run();

        Assert.Contains(
            provider.Entries,
            captured => captured.Category == Bork2dLogging.CommonCategory
                && captured.Level == LogLevel.Information
                && captured.Message == "setup common 42");
        Assert.Contains(
            provider.Entries,
            captured => captured.Category == "MyGame"
                && captured.Level == LogLevel.Warning
                && captured.Message == "custom category");
    }

    [Fact]
    public void EngineCategoriesCanBeCreatedAndLogged()
    {
        var provider = new CapturingLoggerProvider();
        string[] categories =
        [
            Bork2dLogging.RuntimeCategory,
            Bork2dLogging.CommonCategory,
            Bork2dLogging.AssetsCategory,
            Bork2dLogging.RenderCategory,
        ];
        var enabled = new List<bool>();
        var game = new ScriptedGame<GameLayer>
        {
            OnSetup = engine =>
            {
                foreach (var category in categories)
                {
                    var logger = engine.LoggerFactory.CreateLogger(category);
                    enabled.Add(logger.IsEnabled(LogLevel.Information));
                    logger.LogInformation("category {Category} available", category);
                }
            },
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .WithLogging(logging => logging.AddProvider(provider))
            .Build();
        app.Run();

        Assert.All(enabled, Assert.True);
        foreach (var category in categories)
        {
            Assert.Contains(
                provider.Entries,
                captured => captured.Category == category
                    && captured.Message == $"category {category} available");
        }
    }

    [Fact]
    public void EngineExposesTheInjectedFactoryToTheGame()
    {
        IEngine<GameLayer>? captured = null;
        var game = new ScriptedGame<GameLayer> { OnSetup = engine => captured = engine };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();
        app.Run();

        var engine = captured ?? throw new InvalidOperationException("Setup did not capture the engine.");
        Assert.NotNull(engine.LoggerFactory);
        Assert.True(engine.LoggerFactory.CreateLogger("Probe").IsEnabled(LogLevel.Information));
    }

    [Fact]
    public void DefaultFactoryIsNonNullAndRunSucceeds()
    {
        var game = new ScriptedGame<GameLayer>
        {
            OnSetup = engine => engine.LoggerFactory.CreateLogger("MyGame").LogInformation("default setup"),
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();

        app.Run();

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.NotNull(game.Engine);
        Assert.NotNull(game.Engine.LoggerFactory);
    }

    [Fact]
    public void CustomProviderIsDisposedOnceWhenTheAppIsDisposed()
    {
        var provider = new CapturingLoggerProvider();
        var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithHeadless()
            .WithLogging(logging => logging.AddProvider(provider))
            .Build();

        app.Dispose();
        app.Dispose();

        Assert.Equal(1, provider.DisposeCount);
    }

    [Fact]
    public void CustomActionWithoutProviderDoesNotInstallTheDefaultConsoleProvider()
    {
        // A factory with zero providers reports every level disabled. If Build added the default
        // console provider behind the caller's WithLogging action, this would be true.
        bool? enabled = null;
        var game = new ScriptedGame<GameLayer>
        {
            OnSetup = engine =>
                enabled = engine.LoggerFactory.CreateLogger("Probe").IsEnabled(LogLevel.Information),
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .WithLogging(_ => { })
            .Build();
        app.Run();

        Assert.False(enabled);
    }

    [Fact]
    public void DefaultConfigurationInstallsTheConsoleProvider()
    {
        bool? enabled = null;
        var game = new ScriptedGame<GameLayer>
        {
            OnSetup = engine =>
                enabled = engine.LoggerFactory.CreateLogger("Probe").IsEnabled(LogLevel.Information),
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .Build();
        app.Run();

        Assert.True(enabled);
    }
}
