# Runtime Lifecycle

> **Status:** implemented M3 (M1, M2, and the M3 asset integration, merged into `develop`
> 2026-10-26, PR #4). The builder, its validation, the windowed `Bork2dApp.Run()` loop, the one-tick
> headless/offscreen path, app `Dispose()`, the internal windowed platform, the input/time
> handoff, and the app-scoped asset service are `Current`. Windowed mode attaches real
> `IWindow`/`IInput` proxies and advances `ITime`; headless/offscreen keep throwing stubs and a `0`
> clock until M7. The base asset pack (or the `WithoutAssets()` empty filesystem) opens before the
> window and `Setup` and closes after `Teardown`. GPU work and renderer pacing are later
> milestones. The suite is 587 tests: 578 deterministic tests in CI plus 9 opt-in real-window
> tests.

## 1. Public startup contract

```csharp
var game = new MyGame(logger, network);

using var app = new Bork2dAppBuilder<GameLayer>()
    .WithGame(game)
    .WithTitle("My Game")
    .WithSize(1280, 720)
    .WithAssetsPack("assets.pck")
    .WithVSync(true)
    .Build();

app.Run();
```

- The game is a caller-constructed instance; there is no `new()` constraint, reflection, or
  `Activator` use, so constructor injection and NativeAOT work. The caller keeps it alive
  through `Run()`.
- `Bork2dAppBuilder<TLayer>` is configured and built once and has no thread affinity.
- `Build()` validates, creates the app-owned Microsoft.Extensions.Logging/ZLogger factory, logs
  the startup mode and validation outcome from `Bork2d.Runtime`, then creates the clock, engine,
  scene root, an **unstarted** asset service, and the app runtime (root empty until `Setup`).
  Windowed builds get unattached internal window/input proxies; headless/offscreen get the throwing
  window stub and no input. It creates no SDL state, window, GPU resources, threads, packs, or
  filesystem state; the factory is shared by the engine, app runtime, and asset service, and is
  disposed if a later creation step fails.
- `Run()` may be called from any thread and only once. It starts the asset service first, opening
  and validating the base pack (or the empty `WithoutAssets()` filesystem) and starting the loader
  thread. Windowed mode then starts the platform, attaches the window/input proxies, runs an
  uncapped `Setup` → `Tick`… → `Teardown` loop until `IEngine.Exit()`, the close button, or a fatal
  error, shuts the platform down, stops the assets, and detaches the proxies. Headless/offscreen
  run `Setup`, exactly one `Tick`, and `Teardown`, then stop the assets. A failed run still counts
  as the one call; a second call throws `InvalidOperationException`, and `Run()` after `Dispose()`
  throws `ObjectDisposedException`.
- A missing/malformed base pack throws `Bork2dException` from `Run()` before any window, platform,
  `Setup`, or `Teardown`; the partially started asset service is cleaned up. A window-platform
  start failure after the assets started stops the assets and throws before `Setup`. If `Setup`
  throws, the platform and assets are shut down but `Teardown` is not called and the original
  exception propagates. If `Tick` (or a text handler) throws, `Teardown` is attempted, the platform
  and assets are shut down, and the original exception is rethrown. If `Tick` and `Teardown` both
  throw, `Run()` throws an `AggregateException` with both as inner exceptions. A fatal platform
  error stops the loop at the next boundary, is rethrown after `Teardown`, and is aggregated with a
  teardown failure. A failed run detaches the root's direct children.
- `Dispose()` is idempotent and safe before or after `Run()`, but throws
  `InvalidOperationException` while a run is active (D-M2-18), from any thread, without disturbing
  the loop. It detaches the root's direct children, detaches the window/input proxies so retained
  references throw `ObjectDisposedException`, disposes the unstarted or already-stopped asset
  service, disposes the app-owned logger factory (including `AddProvider` instances), and never
  calls `IGame.Teardown`. The app owns the engine and the platform, asset service, renderer, and
  snapshot ring once implemented; the game instance stays caller-owned.
- Exact member names and signatures are in the
  [API reference](../api/reference.md#12-runtime-surface).

## 2. Modes

| Mode | Builder | Current behavior | Target |
| --- | --- | --- | --- |
| **Windowed** (default) | normal options | Visible SDL window on an OS/window thread, real `IWindow`/`IInput`, advanced `ITime`, uncapped loop until exit/close/fatal; base pack + overlays + loads work | Vulkan swapchain and render thread (M6/M7) |
| **Offscreen** | `WithOffscreen(w, h)` | One-tick `Setup`/`Tick`/`Teardown`; no SDL, window, input, or clock advance; same assets as windowed | Windowless Vulkan internal target, render thread, frames discarded (M6/M8) |
| **Headless** | `WithHeadless(ticksPerSecond = 60)` | One-tick `Setup`/`Tick`/`Teardown`; no SDL, window, input, or clock advance; same assets as windowed | No SDL/Vulkan/render thread; timer-paced ticks (M7) |
| **`WithoutAssets()`** | modifier in any mode | Empty VFS; no base pack required; `LoadPack` may mount overlays; one loader thread still starts | Unchanged |

- **Windowed** is the normal game. **Offscreen** is for tests, benchmarks, and visual
  validation and creates no surface/swapchain; a public readback API is future work (M8/M10).
  **Headless** is for servers and data tools: graphics are absent, so created sprites never
  render.
- `WithHeadless` and `WithOffscreen` are mutually exclusive. Window-only options (`WithTitle`,
  `WithSize`, `WithResizable`, `WithFullscreen`, `WithVSync`) are rejected in headless/offscreen
  by `Build()`.
- Windowed mode makes `IWindow` real and `IInput` available; both proxies are attached before
  `Setup` and detached after the platform shuts down and the assets stop. After the run, retained
  references throw `ObjectDisposedException`; the `IEngine.Window`/`IEngine.Input` getters
  themselves do not throw.
- M3 adds **no Vulkan** instance, device, surface, swapchain, or render thread. The window is
  created with the Vulkan-capable flag so M6 can create a surface on the same window later.
- Headless/offscreen keep the throwing `UnsupportedWindow` (members throw
  `NotSupportedException`) and throw from the `IInput` getter.
- `IEngine.Assets` returns the real asset service in every mode, including `WithoutAssets()`. No
  mode initializes SDL or Vulkan because of assets. Headless/offscreen load packs without SDL;
  windowed loads them without any GPU work.
- Windowed/offscreen have **no** target rate, frame limiter, deadline wait, `Sleep`, or spin; the
  windowed loop is uncapped until M7 adds frame-handoff backpressure. Headless pacing is M7, so
  its stored tick rate has no effect yet.

## 3. Builder options and validation

`Current` covers the builder surface, validation, stored options, and the windowed effect of the
window/time options. Parenthetical notes mark options with no effect yet.

| Option | Default | Status | Meaning |
| --- | --- | --- | --- |
| `WithGame(IGame<TLayer> game)` | required | `Current` | Caller-owned game instance; exactly once. |
| `WithAssetsPack(string path)` | `assets.pck` | `Current` | Base pack filesystem path. Opened at `Run()` before the window/`Setup`; relative to the process current directory, or absolute. Validated as non-empty at the setter. |
| `WithoutAssets()` | off | `Current` | Empty VFS; no base pack required; `LoadPack` overlays still allowed; idempotent; mutually exclusive with `WithAssetsPack` (`Build()` throws). |
| `WithLogging(Action<ILoggingBuilder>)` | pretty ZLogger console | `Current` | Configures the app-owned factory; invoked once at `Build` and suppresses the default provider. |
| `WithTitle(string)` | entry assembly name, else `Bork2d` | `Current` (windowed) | Initial window title; `IWindow.Title` updates it later. |
| `WithSize(int width, int height)` | `1280 × 720` | `Current` (windowed) | Initial framebuffer size in physical pixels. |
| `WithResizable(bool)` | `true` | `Current` (windowed) | Whether the user may resize the window. |
| `WithFullscreen(bool)` | `false` | `Current` (windowed) | Borderless desktop fullscreen on the primary monitor. |
| `WithVSync(bool)` | `true` | `Current (stored)` | Windowed present-mode preference; logged, no effect in M2 (no GPU). |
| `WithHeadless(int ticksPerSecond = 60)` | `60` | `Current (one tick)` | One-tick path; the rate is validated and stored, pacing is M7. |
| `WithOffscreen(int width, int height)` | — | `Current (one tick)` | Windowless path; sizes validated, rendering is M6/M8. |
| `WithMaxDeltaTime(double)` | `0.25` s | `Current` (windowed) | Gameplay clamp for `ITime.DeltaTime`; not a pacing delay. |
| `WithValidation(bool)` | `true` in Debug, `false` in Release | `Current (stored)` | Vulkan validation layers (M6). |
| `Build()` | — | `Current` | Validates and creates the app object. |
| `Bork2dApp.Run()` | — | `Current` | Windowed loop or one-tick `Setup`/`Tick`/`Teardown`. |

Validation and creation performed by `Build()`:

1. `Build` may run on any thread.
2. `WithGame` was called exactly once with a non-null instance.
3. Exactly one mode is selected (default windowed, or headless, or offscreen).
4. Window-only options are rejected in headless/offscreen.
5. `TLayer` is an `int`-backed `[Flags]` enum with 1–32 named single-bit values; `None = 0` is
   allowed and composite non-zero values are rejected.
6. Sizes are positive; `MaxDeltaTime` is greater than zero and not `NaN` (positive infinity
   passes); headless tick rate is `>= 0`; the window title and pack path are non-empty; an explicit
   `WithAssetsPack` plus `WithoutAssets()` is rejected.
7. Validation runs first, then the factory is created and the `WithLogging` callback invoked once
   (or the default ZLogger console provider installed), then the startup mode and validation
   outcome are logged. The app owns and disposes the factory.
8. The clock and engine are created next, with an **unstarted** internal asset service. Windowed
   mode also creates unattached window/input proxies; headless/offscreen create neither. No SDL
   call, window, OS thread, pack open, or path resolution happens here.
9. The base pack is resolved and opened only by `Run()`. Existence, parsing, and checksum failures
   throw `Bork2dException` from there before the window/platform/`Setup`; `WithoutAssets()` skips
   the base pack entirely.

Invalid configuration throws `Bork2dException` from `Build()`. `WithTitle` and `WithAssetsPack`
also reject an invalid string immediately with `ArgumentException`; null arguments throw
`ArgumentNullException` at the setter.

## 4. Time

| Property | M2 status | Semantics |
| --- | --- | --- |
| `ITime.DeltaTime` | `Current` (windowed) | Raw interval between tick starts, exactly `0` on the first tick, otherwise clamped to `MaxDeltaTime`. `0` in the one-tick modes. |
| `ITime.Time` | `Current` (windowed) | `0` on the first tick, then raw monotonic seconds since that tick, never clamped. `0` in the one-tick modes. |
| `MaxDeltaTime` | `Current` (windowed) | Validated at `Build()` and applied by the windowed clock; a gameplay clamp, not a pacing delay. |

- The loop calls `NewFrame` once per tick on the tick thread before text dispatch and `Tick`; the
  one-tick modes never call it, so both values stay `0` until M7.
- The clock is a monotonic `Stopwatch`, never wall-clock time, and is not paused when the window
  is unfocused or minimized; games can pause themselves.
- `ITime` reads take one short lock that publishes both values together, so a reader never sees a
  torn double, and remain readable after `Run()` returns.
- Variable timestep: no fixed step, accumulator, `TickCount`, or interpolation. After a stall
  `Time` still jumps but `DeltaTime` is limited to `MaxDeltaTime`.

## 5. Tick loop and pacing

The host has two drivers, both on the `Run()` caller thread.

- **Windowed** (`Current`):

  ```
  assetService.Start()                // open/validate base pack, start loader; Bork2dException here
  platform.Start()                    // visible window or Bork2dException before Setup
  attach window/input proxies
  if (!Exit && !close && !fatal) Setup(engine)
  while (!Exit && !close && !fatal):
      input.Freeze()                  // tick-start snapshot
      time.NewFrame()
      input.DispatchText()            // OnTextTyped before Tick
      game.Tick()
  if Setup succeeded: Teardown()      // window, input, and assets still alive
  platform.Shutdown()                 // destroy window, quit SDL, join OS thread
  assetService.Stop()                 // cancel loads, close packs, join loader
  detach proxies
  ```

  There is **no** fixed rate, target FPS, deadline wait, `Sleep`, `SpinWait`, or frame limiter;
  the loop consumes a core today, which is accepted until M7 supplies frame-handoff backpressure.
  The only blocking waits in the process are the OS thread's SDL event wait, the asset service's
  loader join, and ordinary locks.
- **Headless/offscreen** (`Current`): `assetService.Start()`, exactly one `Setup`, one `Tick`, and
  one `Teardown`, then `assetService.Stop()`, with no platform and no clock advance. A failed asset
  start runs no `Setup`/`Teardown`; a `Setup` failure runs no `Teardown`. This preserves the M1 AOT
  gate; M7 replaces it with a paced loop.
- `IEngine.Exit()` finishes the current tick and stops the windowed loop before the next freeze; a
  close request between ticks stops it before the next freeze. Exit/close during `Setup` runs zero
  ticks but still tears down once (D-M2-19). Headless/offscreen ignore `Exit()`.
- A fatal platform error stops the loop at the next boundary and is rethrown after `Teardown`.
- The loop logs one Information stop line with reason, tick count, elapsed seconds, and the final
  framebuffer size; there is no per-frame logging.

## 6. Shutdown and error propagation

`IGame.Teardown` is `Current` and is not a future step. It runs once on the engine tick thread
when `Setup` succeeded and is the last chance for the game to detach its subtrees, release
resources, issue best-effort window commands, or use `IEngine.Assets`. Windowed shutdown then
stops input, destroys the window, quits SDL, joins the OS thread; assets are stopped after
`Teardown` (and after platform shutdown in windowed mode), then the proxies are detached. The app
then detaches the root's direct children on `Dispose()`. Headless/offscreen stop the assets after
`Teardown` and have no render/platform step.

| Failure | Where detected | Behavior |
| --- | --- | --- |
| Invalid options / layer enum / conflicting modes / `WithAssetsPack`+`WithoutAssets` | `Build()` | `Bork2dException`; no window, SDL, threads, or file access. |
| Game instance missing or set twice | `Build()` | `Bork2dException`. |
| Missing/malformed base pack | `Run()`, before the window | `Bork2dException` with the resolved path and reason; no `Setup`/`Teardown`; the partially started asset service is cleaned up; no window. |
| SDL init / window creation / platform-thread startup failure | Windowed `Run()`, after assets started, before `Setup` | `Bork2dException` with `SDL_GetError()`; no `Setup`, no `Teardown`; the asset service is stopped, the OS thread joined, SDL released, proxies detached. |
| Exception in `Setup` | `Run()` | Platform and assets shut down; original exception; `Teardown` not called. |
| Exception in `Tick` or a text handler | `Run()` | Platform and assets shut down; `Teardown` attempted; original exception rethrown. |
| `Tick` and `Teardown` both throw | `Run()` | `AggregateException` with both inner exceptions, after platform shutdown and asset stop. |
| Fatal platform error after startup | OS thread → `Run()` | Loop stops at the next boundary; `Teardown` attempted; fatal `Bork2dException` rethrown, aggregated with a teardown failure. Assets are stopped. The OS thread has already cleaned up, so the window may be destroyed before `Teardown`; the proxies remain attached for best-effort access. |
| `IWindow`/`IInput` member after shutdown | Member | `ObjectDisposedException` (D-M2-17). |
| `IAssets` member after `Run()`/`Dispose()` | Member | `ObjectDisposedException`; the `IEngine.Assets` getter keeps returning the disposed service and never throws (D-M3-14). |
| `Dispose()` during `Run()` | `Dispose()` | `InvalidOperationException` from any thread; the loop continues. |
| Second `Run()` / `Run()` after `Dispose()` | `Run()` | `InvalidOperationException` / `ObjectDisposedException`. |

- **Fatal-path exception (accepted for M2).** On an unrecoverable SDL error after startup, the OS
  thread stops pumping and runs its cleanup immediately, which may destroy the window before the
  tick thread reaches `Teardown`. The window/input proxies stay attached until the host shuts the
  platform down, so `Teardown` can still attempt best-effort access (getters work; setters may be
  dropped). Normal `Exit()`/close paths keep the window alive through `Teardown` and destroy it only
  afterwards, so D-M2-03 is unchanged there.
- `Dispose()` after a failed `Run()` still attempts cleanup and suppresses only secondary
  exceptions, so the caller keeps the original exception.
- The engine does not swallow exceptions. `Run()` is the single place where the OS-thread failure
  is rethrown on the caller's thread, after the window and OS thread are cleaned up.

## 7. Window and input (windowed only)

Windowed mode has a real window and input for the duration of `Run()`: the internal
`SdlWindowPlatform` (§7.1) and the input accumulator/freeze (§7.2) are attached through the internal proxies before `Setup` and detached after the platform shuts down and the assets stop. Headless/offscreen
have no window or input: `IWindow` members and the `IInput` getter throw
`NotSupportedException`.

### 7.1 Windowed window platform (M2, internal)

`Status: Current (internal).` `SdlWindowPlatform` owns one engine-created OS/window thread. It is
internal to `Bork2d.Runtime` and has no public surface; the runtime host starts it before
`IGame.Setup` and shuts it down after `Teardown`.

- **Start** blocks until the window exists or startup failed. On the OS thread it sets
  `SDL_WINDOWS_DPI_AWARENESS=permonitorv2` and `SDL_WINDOWS_DPI_SCALING=1` before
  `SDL_Init(SDL_INIT_VIDEO)`, creates one visible `SDL_WINDOW_VULKAN | SDL_WINDOW_ALLOW_HIGHDPI`
  window centered on display 0 (plus `RESIZABLE` when configured and `FULLSCREEN_DESKTOP` when the
  builder starts fullscreen), and applies the builder title and initial framebuffer size. SDL
  accepts window points, so the platform measures the achieved framebuffer size and adjusts the
  point size once with the measured scale. It then publishes the first confirmed state and logs
  the startup line, including the native version the loaded `SDL2.dll` reports (2.30.8 for the
  pinned `Silk.NET.SDL` 2.23.0 package, whose label is 2.32.10). A startup failure throws
  `Bork2dException` with the SDL error text after the thread has cleaned up and been joined.
- **Commands and state.** `IWindow` setters validate, drop requests that repeat the last requested
  value, coalesce pending requests per property, enqueue into one lock-guarded MPSC queue, and wake
  the OS thread with one registered SDL user event. The OS thread drains the queue before waiting
  and re-checks after each wait, applies commands in order, and publishes an immutable confirmed
  snapshot (title, window points, framebuffer pixels, scale, fullscreen, cursor, minimized, focused,
  visible, alive). Getters read the snapshot without blocking and may lag one pump.
- **Events.** The pump handles resize/size-changed (0x0 is ignored while minimized), minimize,
  restore, maximize, focus gained/lost, fullscreen changes, and DPI/scale changes in event order.
  A close request or `SDL_QUIT` requests a graceful exit but leaves the window alive so
  `Teardown` can still use it. An unrecoverable SDL error is stored as a fatal
  `Bork2dException`, requests an error exit, and stops the pump; the OS thread's cleanup may then
  destroy the window before the tick thread reaches `Teardown`, while the proxies stay attached for
  best-effort access (accepted M2 fatal-path behavior; normal exit/close paths tear down first).
- **Shutdown** sets the stop flag, pushes a user event to release the event wait, and joins the
  thread. The OS thread destroys the window first, then quits and disposes SDL. Shutdown is
  idempotent and uses no timeout or sleep. A shutdown requested without a prior close reports the
  `Exit` reason; the platform logs the final framebuffer size, and the runtime loop logs the tick
  count and elapsed time.
- The platform never holds a lock across an SDL call, a game callback, or the event wait. The only
  blocking waits are SDL's event wait, the startup/exit signals, and the thread join.

### 7.2 Input and time

`Status: Current (windowed).`

- `IInput` is pumped on the OS/window thread and frozen at tick start; edge flags are true for
  exactly one tick, text is dispatched on the tick thread before `Tick`, and mouse coordinates are
  framebuffer pixels. On focus loss, held keys/buttons are synthesized as released and wheel/text
  state is cleared; on regain the held state and position are re-seeded without press edges and the
  delta is zero.
- Input reads are thread-safe snapshots and are not part of the frame snapshot; a render thread
  cannot read them. Reads before the first freeze are empty (keys held during `Setup` therefore
  produce first-tick press edges), reads during `Teardown` return the most recent snapshot, and after
  detach every member throws `ObjectDisposedException`. The steady-state freeze path allocates
  nothing; text buffering may allocate only when text arrives.
- The loop order is `input.Freeze()` → `time.NewFrame()` → `input.DispatchText()` → `game.Tick()`,
  so text handlers observe the current tick's time and events that arrive during `Tick` belong to
  the next freeze.
- `IWindow` calls may originate on any thread and are marshaled to the OS/window thread. Title
  applies immediately; size and fullscreen change the window; cursor visibility toggles; close
  requests a graceful exit after the current tick.
- While minimized, ticks continue and `Size` keeps the last non-minimized framebuffer size;
  presentation is irrelevant until M6.
- `ITime` follows D-M2-16 (first-tick zero, raw monotonic `Time`, clamped `DeltaTime`) and keeps its
  last values readable after shutdown; see [Threading](threading.md#5-input-across-threads-windowed-only).

## 8. Related documents

- [Scene ownership](scene-ownership.md) — root-owned lifetime model.
- [Threading and frame handoff](threading.md) — snapshot ring and bounded run-ahead.
- [Rendering](rendering.md) — windowed/offscreen render modes.
- [Assets and tooling](assets-and-tooling.md) — pack loading, tooling, and the default pack.
- [Logging](logging.md) — default ZLogger configuration and `IEngine.LoggerFactory`.
- [NativeAOT](nativeaot.md) — headless/AOT startup constraints.
- [API reference](../api/reference.md#12-runtime-surface) — public builder and app surface.
- [Roadmap](../roadmap.md) — milestone status.
