using Bork2d.Engine;
using Bork2d.Runtime;
using Bork2d.Runtime.Hosting;
using Bork2d.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bork2d.Tests.Runtime;

public sealed class Bork2dAppBuilderTests
{
    [Fact]
    public void BuildWithoutAGameThrows()
    {
        var builder = new Bork2dAppBuilder<GameLayer>();

        var exception = Assert.Throws<Bork2dException>(() => builder.Build());

        Assert.Contains("WithGame", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullGameThrows()
    {
        var builder = new Bork2dAppBuilder<GameLayer>();

        Assert.Throws<ArgumentNullException>(() => builder.WithGame(null!));
    }

    [Fact]
    public void SecondWithGameFailsBuild()
    {
        var builder = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithGame(new TestGame<GameLayer>());

        Assert.Throws<Bork2dException>(() => builder.Build());
    }

    [Fact]
    public void ConflictingHeadlessAndOffscreenModesFailBuild()
    {
        var builder = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithHeadless()
            .WithOffscreen(640, 480);

        Assert.Throws<Bork2dException>(() => builder.Build());
    }

    [Fact]
    public void HeadlessModeRejectsEveryWindowOption()
    {
        Action<Bork2dAppBuilder<GameLayer>>[] setters =
        [
            builder => builder.WithTitle("title"),
            builder => builder.WithSize(800, 600),
            builder => builder.WithResizable(false),
            builder => builder.WithFullscreen(true),
            builder => builder.WithVSync(false),
        ];

        foreach (var setter in setters)
        {
            var builder = new Bork2dAppBuilder<GameLayer>()
                .WithGame(new TestGame<GameLayer>())
                .WithHeadless();
            setter(builder);

            Assert.Throws<Bork2dException>(() => builder.Build());
        }
    }

    [Fact]
    public void OffscreenModeRejectsEveryWindowOption()
    {
        Action<Bork2dAppBuilder<GameLayer>>[] setters =
        [
            builder => builder.WithTitle("title"),
            builder => builder.WithSize(800, 600),
            builder => builder.WithResizable(false),
            builder => builder.WithFullscreen(true),
            builder => builder.WithVSync(false),
        ];

        foreach (var setter in setters)
        {
            var builder = new Bork2dAppBuilder<GameLayer>()
                .WithGame(new TestGame<GameLayer>())
                .WithOffscreen(640, 480);
            setter(builder);

            Assert.Throws<Bork2dException>(() => builder.Build());
        }
    }

    [Fact]
    public void InvalidLayerEnumsFailBuild()
    {
        AssertInvalidLayer<NoFlagsLayer>();
        AssertInvalidLayer<CompositeLayer>();
        AssertInvalidLayer<EmptyLayer>();
        AssertInvalidLayer<LongBackedLayer>();
        AssertInvalidLayer<ByteBackedLayer>();
    }

    [Theory]
    [InlineData(0, 720)]
    [InlineData(1280, 0)]
    [InlineData(-1, 720)]
    [InlineData(1280, -1)]
    public void WindowSizesMustBePositive(int width, int height)
    {
        var builder = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithSize(width, height);

        Assert.Throws<Bork2dException>(() => builder.Build());
    }

    [Theory]
    [InlineData(0, 480)]
    [InlineData(640, 0)]
    [InlineData(-1, 480)]
    [InlineData(640, -1)]
    public void OffscreenSizesMustBePositive(int width, int height)
    {
        var builder = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithOffscreen(width, height);

        Assert.Throws<Bork2dException>(() => builder.Build());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void HeadlessTickRateMustNotBeNegative(int ticksPerSecond)
    {
        var builder = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithHeadless(ticksPerSecond);

        Assert.Throws<Bork2dException>(() => builder.Build());
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-0.25d)]
    [InlineData(double.NaN)]
    public void MaxDeltaTimeMustBePositive(double seconds)
    {
        var builder = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithMaxDeltaTime(seconds);

        Assert.Throws<Bork2dException>(() => builder.Build());
    }

    [Fact]
    public void NullPackPathThrows()
    {
        var builder = new Bork2dAppBuilder<GameLayer>();

        Assert.Throws<ArgumentNullException>(() => builder.WithAssetsPack(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankPackPathThrows(string path)
    {
        var builder = new Bork2dAppBuilder<GameLayer>();

        Assert.Throws<ArgumentException>(() => builder.WithAssetsPack(path));
    }

    [Fact]
    public void NonexistentPackPathIsAccepted()
    {
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithAssetsPack("this/pack/does/not/exist.pck")
            .Build();

        Assert.NotNull(app);
    }

    [Fact]
    public void NullTitleThrows()
    {
        var builder = new Bork2dAppBuilder<GameLayer>();

        Assert.Throws<ArgumentNullException>(() => builder.WithTitle(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTitleThrows(string title)
    {
        var builder = new Bork2dAppBuilder<GameLayer>();

        Assert.Throws<ArgumentException>(() => builder.WithTitle(title));
    }

    [Fact]
    public void NullLoggingConfigurationThrows()
    {
        var builder = new Bork2dAppBuilder<GameLayer>();

        Assert.Throws<ArgumentNullException>(() => builder.WithLogging(null!));
    }

    [Fact]
    public void BuildsTheDefaultWindowedApp()
    {
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .Build();

        Assert.NotNull(app);
    }

    [Fact]
    public void BuildsWindowedWithEveryWindowOption()
    {
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithTitle("My Game")
            .WithSize(800, 600)
            .WithResizable(false)
            .WithFullscreen(true)
            .WithVSync(false)
            .WithValidation(true)
            .Build();

        Assert.NotNull(app);
    }

    [Fact]
    public void BuildsHeadlessWithTheDefaultTickRate()
    {
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithHeadless()
            .Build();

        Assert.NotNull(app);
    }

    [Fact]
    public void BuildsUncappedHeadless()
    {
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithHeadless(0)
            .Build();

        Assert.NotNull(app);
    }

    [Fact]
    public void BuildsOffscreen()
    {
        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithOffscreen(640, 480)
            .Build();

        Assert.NotNull(app);
    }

    [Fact]
    public void OptionsMatchTheM1Defaults()
    {
        var options = new Bork2dAppBuilder<GameLayer>().Options;

        Assert.Equal(Bork2dAppMode.Windowed, options.Mode);
        Assert.False(options.HeadlessSelected);
        Assert.False(options.OffscreenSelected);
        Assert.Null(options.Game);
        Assert.Equal(0, options.GameSetCount);
        Assert.Equal("assets.pck", options.AssetsPackPath);
        Assert.False(options.AssetsPackSelected);
        Assert.False(options.WithoutAssetsSelected);
        Assert.Null(options.ConfigureLogging);
        Assert.False(string.IsNullOrWhiteSpace(options.Title));
        Assert.Equal(1280, options.Width);
        Assert.Equal(720, options.Height);
        Assert.True(options.Resizable);
        Assert.False(options.Fullscreen);
        Assert.True(options.VSync);
        Assert.Equal(60, options.HeadlessTicksPerSecond);
        Assert.Equal(0.25, options.MaxDeltaTime);
        Assert.Null(options.SdlApiFactory);
        Assert.Equal(Bork2dWindowOptionFlags.None, options.WindowOptionsSet);
    }

    [Fact]
    public void ModeSelectorsUpdateTheStoredMode()
    {
        var headless = new Bork2dAppBuilder<GameLayer>().WithHeadless(0).Options;
        Assert.Equal(Bork2dAppMode.Headless, headless.Mode);
        Assert.True(headless.HeadlessSelected);
        Assert.Equal(0, headless.HeadlessTicksPerSecond);

        var offscreen = new Bork2dAppBuilder<GameLayer>().WithOffscreen(320, 200).Options;
        Assert.Equal(Bork2dAppMode.Offscreen, offscreen.Mode);
        Assert.True(offscreen.OffscreenSelected);
        Assert.Equal(320, offscreen.OffscreenWidth);
        Assert.Equal(200, offscreen.OffscreenHeight);
    }

    [Fact]
    public void WindowOptionSettersAreRecorded()
    {
        var options = new Bork2dAppBuilder<GameLayer>()
            .WithTitle("title")
            .WithSize(800, 600)
            .WithResizable(false)
            .WithFullscreen(true)
            .WithVSync(false)
            .Options;

        Assert.Equal("title", options.Title);
        Assert.Equal(800, options.Width);
        Assert.Equal(600, options.Height);
        Assert.False(options.Resizable);
        Assert.True(options.Fullscreen);
        Assert.False(options.VSync);
        Assert.Equal(
            Bork2dWindowOptionFlags.Title
                | Bork2dWindowOptionFlags.Size
                | Bork2dWindowOptionFlags.Resizable
                | Bork2dWindowOptionFlags.Fullscreen
                | Bork2dWindowOptionFlags.VSync,
            options.WindowOptionsSet);
    }

    [Fact]
    public void LoggingConfigurationIsStored()
    {
        var configure = new Action<ILoggingBuilder>(_ => { });

        var options = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithLogging(configure)
            .Options;

        Assert.Same(configure, options.ConfigureLogging);
    }

    [Fact]
    public void ValidationOptionIsStored()
    {
        var enabled = new Bork2dAppBuilder<GameLayer>().WithValidation(true).Options;
        var disabled = new Bork2dAppBuilder<GameLayer>().WithValidation(false).Options;

        Assert.True(enabled.Validation);
        Assert.False(disabled.Validation);
    }

    [Fact]
    public void RuntimeExposesOnlyTheBuilderAndApp()
    {
        var exported = typeof(Bork2dAppBuilder<>).Assembly.GetExportedTypes()
            .Select(type => type.FullName ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Bork2d.Runtime.Bork2dApp", "Bork2d.Runtime.Bork2dAppBuilder`1"],
            exported);
    }

    [Fact]
    public void WithoutAssetsIsStoredAndIdempotent()
    {
        var options = new Bork2dAppBuilder<GameLayer>()
            .WithoutAssets()
            .WithoutAssets()
            .Options;

        Assert.True(options.WithoutAssetsSelected);
        Assert.False(options.AssetsPackSelected);
        Assert.Equal("assets.pck", options.AssetsPackPath);
    }

    [Fact]
    public void WithAssetsPackAndWithoutAssetsFailBuild()
    {
        var builder = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithAssetsPack("custom.pck")
            .WithoutAssets();

        var exception = Assert.Throws<Bork2dException>(() => builder.Build());

        Assert.Contains("mutually exclusive", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAssetsThenWithAssetsPackFailsBuild()
    {
        var builder = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithoutAssets()
            .WithAssetsPack("custom.pck");

        Assert.Throws<Bork2dException>(() => builder.Build());
    }

    [Fact]
    public void WithoutAssetsBuildsWithoutADefaultPackFile()
    {
        string defaultPackPath = Path.Combine(Environment.CurrentDirectory, "assets.pck");
        bool existedBefore = File.Exists(defaultPackPath);

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithoutAssets()
            .Build();

        Assert.False(
            !existedBefore && File.Exists(defaultPackPath),
            "Build must not create the default assets pack.");
    }

    [Fact]
    public void BuildDoesNotTouchTheFileSystemWithAMissingPackPath()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"bork2d-missing-{Guid.NewGuid():N}",
            "assets.pck");

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithAssetsPack(missingPath)
            .Build();

        Assert.False(File.Exists(missingPath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(missingPath)!));
    }

    [Fact]
    public void BuildsWithoutAssetsInEveryMode()
    {
        using var headless = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithHeadless()
            .WithoutAssets()
            .Build();
        using var offscreen = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithOffscreen(320, 200)
            .WithoutAssets()
            .Build();
        using var windowed = new Bork2dAppBuilder<GameLayer>()
            .WithGame(new TestGame<GameLayer>())
            .WithoutAssets()
            .Build();

        Assert.NotNull(headless);
        Assert.NotNull(offscreen);
        Assert.NotNull(windowed);
    }

    private static void AssertInvalidLayer<TLayer>()
        where TLayer : struct, Enum
    {
        var builder = new Bork2dAppBuilder<TLayer>().WithGame(new TestGame<TLayer>());

        Assert.Throws<Bork2dException>(() => builder.Build());
    }

    private enum NoFlagsLayer
    {
        A = 1,
    }

    [Flags]
    private enum CompositeLayer
    {
        None = 0,
        A = 3,
    }

    [Flags]
    private enum EmptyLayer
    {
        None = 0,
    }

    [Flags]
    private enum LongBackedLayer : long
    {
        A = 1,
    }

    [Flags]
    private enum ByteBackedLayer : byte
    {
        A = 1,
    }
}
