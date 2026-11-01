# Runtime

`Bork2d.Runtime` is the composition root and the only assembly a game normally references alongside
`Bork2d.Engine`. It owns the public builder and app types, SDL windowing and input, the Vulkan
instance/surface, renderer composition, the three host loops, and logging. It exports exactly
`Bork2dApp` and `Bork2dAppBuilder<TLayer>`.

## Builder

| Method | Effect |
| --- | --- |
| `WithGame(IGame<TLayer>)` | Required, exactly once. The game is caller-constructed; there is no reflection or parameterless-constructor constraint. |
| `WithAssetsPack(string)` | Base pack path (default `assets.pck`). Opened by `Run`, not `Build`. |
| `WithoutAssets()` | Starts with an empty asset filesystem; overlays may still be mounted later. |
| `WithHeadless(int ticksPerSecond = 60)` | Selects the no-window, no-GPU mode. |
| `WithOffscreen(int width, int height)` | Selects offscreen rendering to a fixed sRGB target. |
| `WithTitle`, `WithSize`, `WithResizable`, `WithFullscreen`, `WithVSync` | Windowed-only options. |
| `WithMaxDeltaTime(double)` | Gameplay delta clamp for windowed/offscreen ticks (default `0.25` s). |
| `WithValidation(bool)` | Requests Vulkan validation layers. Default is on in `DEBUG`, off otherwise. |
| `WithLogging(Action<ILoggingBuilder>)` | Replaces the default console provider; the callback owns provider configuration. |

`WithHeadless` and `WithOffscreen` are mutually exclusive, and window options are rejected in either
mode. Configuration errors throw `Bork2dException` from `Build`.

`Build()` validates the configuration and creates the logger factory, engine, clock, an unstarted
asset service, and (for windowed apps) detached window/input proxies. Offscreen apps get a
functional window facade reporting the target extent; headless apps get the throwing window stub. It
does not open a pack, create an OS window, render, or start a thread. `Run()` starts the base pack,
platform, renderer, and game, and is callable once from any thread.

A successful `Build()` consumes the builder: every later configuration method and a second `Build()`
throw `InvalidOperationException`. A failed `Build()`, from either validation or resource creation,
does not consume the builder, so the caller can fix the configuration and retry. The built app
remains owned and disposed by the caller as before; later builder use cannot reconfigure it.

## Host loops

### Windowed

1. Open the base pack, start the asset loader, then create the SDL window, Vulkan instance, and
   surface on the OS thread.
2. Start the renderer thread without waiting, and call `Setup` immediately.
3. Wait for renderer readiness before the first tick.
4. Repeat: acquire a snapshot slot, freeze input, advance the clock, dispatch text, call `Tick`,
   capture the scene, publish. The acquire blocks while all three slots are occupied.
5. Stop on `IEngine.Exit()`, the close button/`SDL_QUIT`, a renderer fatal, or a fatal platform error.
6. Call `Teardown`, stop and join the renderer, shut down the platform, stop assets, then detach the
   window/input proxies.

The loop is not artificially frame-limited and there is no fixed tick rate. While the window is
minimized, tick starts are capped at 30 per second; no cap applies once the loop is stopping.

### Offscreen

The same handoff loop without SDL, input, or presentation. It renders against a fixed sRGB target
and discards each frame. `IEngine.Window` is a functional facade reporting the fixed target extent;
its title, fullscreen, and cursor setters store inert state and no member throws. It stops on
`Exit` or a renderer fatal.

### Headless

Assets start, `Setup` runs, then a paced loop runs until `Exit`. Tick `N` is due at
`start + N / ticksPerSecond`; waiting is a plain sleep, and a late loop catches up without a tick
cap. The clock is fully synthesized and `MaxDeltaTime` does not clamp it. No window, SDL, Vulkan
loader, renderer, snapshot ring, or capture is created.

## Window and input

The window thread owns SDL, the event pump, and the confirmed window state. Setters enqueue
coalesced commands (the latest value per property wins) and never block on SDL; getters return the
last confirmed state and may lag one event-pump pass. Sizes and mouse positions are framebuffer
physical pixels, not logical points. The platform supports resize, borderless fullscreen, minimize,
and cursor visibility, and performs one surface-recovery attempt after a surface loss.

Input is frozen at each tick start, so every read during one `Tick` sees the same snapshot. Press
and release helpers are true for exactly one tick (a press and release inside one interval reports
both edges with `IsKeyDown` false). Focus loss synthesizes releases for held keys/buttons and clears
wheel and pending text; focus regain re-seeds state without creating press edges. Input is available
only in windowed mode and never crosses to the render thread. Offscreen apps expose the fixed target
extent through `IEngine.Window` instead of an SDL window.

## Lifecycle and failures

- `Run()` may be called once; a failed run still counts as the one call.
- `Dispose()` is idempotent and may run before or after `Run`, but throws while a run is active.
  Disposal detaches the root's direct children, detaches proxies, and disposes the stopped asset
  service and logger factory. It never calls `Teardown` again.
- After a run, every `IAssets` and input member on a retained reference throws
  `ObjectDisposedException`; so does every window member of a windowed app. The offscreen window
  facade stays functional. `IEngine.RenderStats` stays readable and may keep the last values.
- A platform start failure precedes `Setup`. A renderer factory/start/readiness failure still lets
  `Setup` finish, runs zero ticks, attempts `Teardown`, joins the renderer, and rethrows.
- A `Setup` failure wins over a renderer failure and runs no `Teardown`. Tick/capture/text/`Teardown`
  failures propagate with the documented precedence; renderer cleanup failures during normal
  shutdown are logged and do not change the run's outcome.

## Logging

The builder creates an app-owned `Microsoft.Extensions.Logging` factory exposed as
`IEngine.LoggerFactory`. Without `WithLogging`, a ZLogger plain-text console provider is installed
with a timestamp/level/category prefix. Minimum level is `Debug` in `DEBUG` builds and
`Information` otherwise. Categories are `Bork2d.Runtime`, `Bork2d.Engine`,
`Bork2d.Engine.Assets`, and `Bork2d.Render`.

## NativeAOT

The runtime, renderer, and engine are AOT- and trim-analyzer clean. The Pong desktop app sets
`PublishAot` and is the publish gate; it suppresses only `IL3000`/`IL3002` for Silk.NET's native
loader probes. SDL2 ships as a native asset from `Silk.NET.SDL`, and `vulkan-1.dll` comes from the
system loader, so the publish output is self-contained apart from the Vulkan driver.
