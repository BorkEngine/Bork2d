using Bork2d.Engine.Input;
using Bork2d.Runtime;
using Bork2d.Runtime.Sdl;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Platform.Window;

/// <summary>
/// The wired tick-start handoff: a key held during <c>Setup</c> produces a first-tick edge, text is
/// dispatched before <c>Tick</c> with the current tick's time, events during a tick land in the next
/// freeze, and the one-tick modes never advance the clock.
/// </summary>
public sealed class WindowedInputTimeTests
{
    [Fact]
    public void KeyHeldDuringSetupProducesAFirstTickPressEdge()
    {
        var sdl = new FakeSdlApi { StartFocused = true };
        var pressed = false;
        var down = false;
        var game = new ScriptedGame<GameLayer>();
        game.OnSetup = _ =>
        {
            sdl.Enqueue(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.W));
            WindowedTestSupport.SyncAfterCommand(game, sdl, "sync-key-setup");
        };
        game.OnTick = () =>
        {
            if (game.TickCount != 1)
            {
                return;
            }

            var input = game.Engine!.Input;
            pressed = input.IsKeyPressed(KeyCode.W);
            down = input.IsKeyDown(KeyCode.W);
            game.Engine.Exit();
        };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        AppTestSupport.RunToCompletion(app);

        Assert.True(pressed, "A key held during Setup must produce a first-tick press edge.");
        Assert.True(down, "A key held during Setup must be down at the first freeze.");
        Assert.Equal(1, game.TickCount);
    }

    [Fact]
    public void TextIsDispatchedBeforeTickAndObservesTheCurrentTickTime()
    {
        var sdl = new FakeSdlApi { StartFocused = true };
        var handlerRan = false;
        double? handlerTime = null;
        double? handlerDelta = null;
        double? tickTime = null;
        double? tickDelta = null;
        var game = new ScriptedGame<GameLayer>();
        game.OnSetup = engine =>
        {
            engine.Input.OnTextTyped += _ =>
            {
                handlerRan = true;
                handlerTime = engine.Time.Time;
                handlerDelta = engine.Time.DeltaTime;
            };

            sdl.Enqueue(SdlEvent.TextInput("A"));
            WindowedTestSupport.SyncAfterCommand(game, sdl, "sync-text-setup");
        };
        game.OnTick = () =>
        {
            if (game.TickCount != 1)
            {
                return;
            }

            var engine = game.Engine!;
            tickTime = engine.Time.Time;
            tickDelta = engine.Time.DeltaTime;
            engine.Exit();
        };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        AppTestSupport.RunToCompletion(app);

        Assert.True(handlerRan, "The text handler must run before the first Tick.");
        Assert.Equal(0d, handlerTime);
        Assert.Equal(0d, handlerDelta);
        Assert.Equal(0d, tickTime);
        Assert.Equal(0d, tickDelta);
        Assert.Equal(handlerTime, tickTime);
    }

    [Fact]
    public void EventsAppliedDuringTickLandInTheNextFreeze()
    {
        var sdl = new FakeSdlApi { StartFocused = true };
        var downWithinTickOne = true;
        var downInTickTwo = false;
        var pressedInTickTwo = false;
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            var engine = game.Engine!;
            if (game.TickCount == 1)
            {
                sdl.Enqueue(SdlEvent.Key(SdlEventType.KeyDown, (int)KeyCode.A));
                WindowedTestSupport.SyncAfterCommand(game, sdl, "sync-key-tick");
                downWithinTickOne = engine.Input.IsKeyDown(KeyCode.A);
            }
            else if (game.TickCount == 2)
            {
                downInTickTwo = engine.Input.IsKeyDown(KeyCode.A);
                pressedInTickTwo = engine.Input.IsKeyPressed(KeyCode.A);
                engine.Exit();
            }
        };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        AppTestSupport.RunToCompletion(app);

        Assert.False(
            downWithinTickOne,
            "An event applied during a tick must not change that tick's frozen snapshot.");
        Assert.True(downInTickTwo, "The event must be visible at the next freeze.");
        Assert.True(pressedInTickTwo, "The next freeze must report the press edge.");
        Assert.Equal(2, game.TickCount);
    }

    [Fact]
    public void FirstTickTimeIsZeroAndLaterTicksStayMonotonicWithinTheClamp()
    {
        var sdl = new FakeSdlApi();
        double? firstTime = null;
        double? firstDelta = null;
        double? secondTime = null;
        double? secondDelta = null;
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            var engine = game.Engine!;
            if (game.TickCount == 1)
            {
                firstTime = engine.Time.Time;
                firstDelta = engine.Time.DeltaTime;
            }
            else if (game.TickCount == 2)
            {
                secondTime = engine.Time.Time;
                secondDelta = engine.Time.DeltaTime;
                engine.Exit();
            }
        };
        using var app = WindowedTestSupport.Builder(sdl, game).Build();

        AppTestSupport.RunToCompletion(app);

        Assert.Equal(0d, firstTime);
        Assert.Equal(0d, firstDelta);
        Assert.True(secondTime >= firstTime, "Time must be monotonic.");
        Assert.InRange(secondDelta!.Value, 0d, 0.25d);
    }

    [Fact]
    public void HeadlessModeNeverAdvancesTheClock()
    {
        double? time = null;
        double? delta = null;
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            var engine = game.Engine!;
            time = engine.Time.Time;
            delta = engine.Time.DeltaTime;
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithHeadless()
            .WithFakeGraphics()
            .Build();
        app.Run();

        Assert.Equal(0d, time);
        Assert.Equal(0d, delta);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
    }

    [Fact]
    public void OffscreenLoopAdvancesTheClockUntilExit()
    {
        double? firstTime = null;
        double? secondTime = null;
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            var engine = game.Engine!;
            if (game.TickCount == 1)
            {
                firstTime = engine.Time.Time;
            }
            else
            {
                secondTime = engine.Time.Time;
            }

            if (game.TickCount == 3)
            {
                engine.Exit();
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 200)
            .WithFakeGraphics()
            .Build();
        app.Run();

        Assert.Equal(3, game.TickCount);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(0d, firstTime);
        Assert.True(secondTime!.Value > firstTime!.Value, "Offscreen ITime must advance per tick.");
    }
}
