# Runtime Lifecycle

> **Status:** implemented through M6 WP-4 (2026-10-30; M6 is in progress and WP-5 owns the
> AOT/Pong gate). The builder, its validation, the windowed/offscreen hosts with the Vulkan renderer
> thread, the headless one-tick path, app `Dispose()`, the internal windowed platform (SDL window,
> Vulkan instance/surface, input/time handoff), and the app-scoped asset service are `Current`.
> Windowed and offscreen modes run the real renderer (`Bork2d.Render`): windowed creates the
> instance/surface on the OS/window thread and presents a continuous black clear; offscreen owns a
> fixed sRGB target, discards frames, loops until `IEngine.Exit()`, and advances `ITime` per tick
> (D-M6-07). Headless is unchanged: one tick, no SDL, no Vulkan, no renderer. `Setup` runs before
> renderer readiness and the first `Tick` waits for it (D-M6-06); a renderer failure around `Setup`
> still runs `Setup`, attempts `Teardown`, and reports from `Run()` (D-M6-14). Pacing and frame
> handoff are M7; sprites are M8. The suite is 1044 tests: 1028 deterministic tests in CI plus 16
> opt-in/conditional tests (9 real-window, 3 reparse-point, 4 real-GPU).

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
  thread. Windowed mode then starts the platform (SDL window, Vulkan instance, surface) on the
  OS/window thread, creates and starts the renderer thread **without waiting for readiness**
  (D-M6-06), attaches the window/input proxies, runs `Setup` immediately (it may call `Exit()`),
  waits for renderer readiness before the first tick, and runs an uncapped `Setup` → `Tick`… →
  `Teardown` loop until `IEngine.Exit()`, the close button, a renderer fatal, or a platform fatal.
  Offscreen mode starts the renderer the same way, runs `Setup`, waits for readiness, and runs the
  same loop without a window or input until `IEngine.Exit()` (D-M6-07); its `ITime` advances per
  tick. Headless runs `Setup`, exactly one `Tick`, and `Teardown` with no renderer, SDL, or Vulkan.
  Windowed/offscreen teardown then stops and joins the renderer before the platform destroys the
  surface/instance/window and before the assets stop. A failed run still counts as the one call; a
  second call throws `InvalidOperationException`, and `Run()` after `Dispose()` throws
  `ObjectDisposedException`.
- A missing/malformed base pack throws `Bork2dException` from `Run()` before any window, platform,
  renderer, `Setup`, or `Teardown`; the partially started asset service is cleaned up. A
  window-platform start failure (SDL init/window, missing Vulkan loader/instance, surface creation)
  after the assets started stops the assets and throws before the renderer and `Setup`. A renderer
  factory/start/readiness failure after the platform started still runs `Setup`, runs zero ticks,
  attempts `Teardown` (when `Setup` succeeded), joins the renderer, shuts the platform/assets down,
  and throws `Bork2dException` (D-M6-14). If `Setup` also throws, the `Setup` exception wins and
  `Teardown` does not run; the renderer is still stopped/joined. If `Tick` (or a text handler)
  throws, `Teardown` is attempted, the renderer/platform/assets are shut down, and the original
  exception is rethrown. If `Tick` and `Teardown` both throw, `Run()` throws an
  `AggregateException` with both as inner exceptions. A renderer fatal after the first tick stops
  the loop at the next boundary, runs `Teardown`, joins the renderer, and rethrows; a fatal platform
  error follows the M2 path. A renderer cleanup failure during normal shutdown is logged and the
  run's outcome stands (D-M6-27). A failed run detaches the root's direct children.
- `Dispose()` is idempotent and safe before or after `Run()`, but throws
  `InvalidOperationException` while a run is active (D-M2-18), from any thread, without disturbing
  the loop. It detaches the root's direct children, detaches the window/input proxies so retained
  references throw `ObjectDisposedException`, disposes the unstarted or already-stopped asset
  service, disposes the app-owned logger factory (including `AddProvider` instances), and never
  calls `IGame.Teardown`. The app owns the engine, the platform, the asset service, and the renderer
  thread (joined before `Run()` returns); the game instance stays caller-owned.
- Exact member names and signatures are in the
  [API reference](../api/reference.md#12-runtime-surface).

## 2. Modes

| Mode | Builder | Current behavior | Target |
| --- | --- | --- | --- |
| **Windowed** (default) | normal options | Visible SDL window on an OS/window thread; Vulkan 1.3 instance/surface created there; render thread owns device/swapchain and presents a continuous opaque-black clear; real `IWindow`/`IInput`, advanced `ITime`, uncapped loop until exit/close/fatal; base pack + overlays + loads work | Snapshot handoff/pacing (M7), sprites (M8) |
| **Offscreen** | `WithOffscreen(w, h)` | Vulkan 1.3 render thread with a fixed sRGB target and two frames in flight; `Setup` → `Tick`… → `Teardown` until `IEngine.Exit()`, `ITime` advanced, frames cleared/discarded; no SDL, window, input, surface, swapchain, or readback | Pacing/handoff (M7), sprites/readback (M8/M10) |
| **Headless** | `WithHeadless(ticksPerSecond = 60)` | One-tick `Setup`/`Tick`/`Teardown`; no SDL, window, input, renderer, Vulkan loader, or clock advance; same assets as windowed; `Exit` ignored | Timer-paced ticks (M7) |
| **`WithoutAssets()`** | modifier in any mode | Empty VFS; no base pack required; `LoadPack` may mount overlays; one loader thread still starts | Unchanged |

- **Windowed** is the normal game. **Offscreen** is for tests, benchmarks, and visual
  validation; it creates no surface/swapchain and adds no public readback API (M8/M10).
  **Headless** is for servers and data tools: graphics are absent, so created sprites never
  render.
- Windowed and offscreen require a Vulkan 1.3 loader/device (D-M6-02); there is no public opt-out.
  A missing loader/instance/device/feature/format (and, windowed, a surface failure) is a
  `Bork2dException`. Windowed failures before the renderer (instance/surface) happen in
  `platform.Start()` before `Setup`; renderer factory/start/readiness failures happen after
  `Setup` (D-M6-06/D-M6-14). Headless never loads Vulkan or SDL.
- `WithHeadless` and `WithOffscreen` are mutually exclusive. Window-only options (`WithTitle`,
  `WithSize`, `WithResizable`, `WithFullscreen`, `WithVSync`) are rejected in headless/offscreen
  by `Build()`.
- Windowed mode makes `IWindow` real and `IInput` available; both proxies are attached before
  `Setup` and detached after the platform shuts down and the assets stop. After the run, retained
  references throw `ObjectDisposedException`; the `IEngine.Window`/`IEngine.Input` getters
  themselves do not throw.
- Headless/offscreen keep the throwing `UnsupportedWindow` (members throw
  `NotSupportedException`) and throw from the `IInput` getter. Offscreen now advances `ITime` per
  tick and honors `IEngine.Exit()` (D-M6-07); headless keeps its one-tick zero clock.
- `IEngine.Assets` returns the real asset service in every mode, including `WithoutAssets()`. No
  mode initializes SDL or Vulkan because of assets. Headless/offscreen load packs without SDL;
  headless loads them without Vulkan, while offscreen uses Vulkan only for its renderer.
- Windowed mode has **no** target rate, frame limiter, deadline wait, `Sleep`, or spin; the loop is
  uncapped until M7 adds frame-handoff backpressure. Offscreen ticks are likewise uncapped and its
  render frames are paced only by GPU fences (D-M6-12). Headless pacing is M7, so its stored tick
  rate has no effect yet.
- Windowed and offscreen render a black clear in M6; sprites arrive in M8.

## 3. Builder options and validation

`Current` covers the builder surface, validation, stored options, and their M6 effects. The status
column marks options that remain stored-only.

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
| `WithVSync(bool)` | `true` | `Current` (M6) | Windowed present-mode preference: FIFO when `true`; MAILBOX → IMMEDIATE → FIFO when `false` (D-M6-17). Logged with the actual mode. |
| `WithHeadless(int ticksPerSecond = 60)` | `60` | `Current (one tick)` | One-tick path; the rate is validated and stored, pacing is M7. |
| `WithOffscreen(int width, int height)` | — | `Current` (M6) | Windowless Vulkan target; the sizes are validated and fixed, frames clear/discard, and the loop runs until `Exit` (D-M6-07); no readback (D-M6-23). |
| `WithMaxDeltaTime(double)` | `0.25` s | `Current` | Gameplay clamp for `ITime.DeltaTime`; not a pacing delay. |
| `WithValidation(bool)` | `true` in Debug, `false` in Release | `Current` (M6) | Requests `VK_LAYER_KHRONOS_validation` and `VK_EXT_debug_utils`; a missing layer logs a warning and the run continues (D-M6-20). |
| `Build()` | — | `Current` | Validates and creates the app object. |
| `Bork2dApp.Run()` | — | `Current` | Windowed/offscreen renderer host, or the headless one tick. |

Validation and creation performed by `Build()`:

1. `Build` may run on any thread.
2. `WithGame` was called exactly once with a non-null instance.
3. Exactly one mode is selected (default windowed, or headless, or offscreen).
4. Window-only options are rejected in headless/offscreen.
5. `TLayer` is an `int`-backed `[Flags]` enum with 1–32 named single-bit values; `None = 0` is
   allowed and composite non-zero values are rejected.
6. Sizes are positive; the max-delta option is greater than zero and not `NaN` (positive infinity
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

| Property | Status | Semantics |
| --- | --- | --- |
| `ITime.DeltaTime` | `Current` (windowed/offscreen) | Raw interval between tick starts, exactly `0` on the first tick, otherwise clamped to the builder's `WithMaxDeltaTime` value. `0` in headless. |
| `ITime.Time` | `Current` (windowed/offscreen) | `0` on the first tick, then raw monotonic seconds since that tick, never clamped. `0` in headless. |

- `ITime` has no `MaxDeltaTime` member. The clamp is configured with
  `WithMaxDeltaTime` (default `0.25` s), validated at `Build()`, and applied by the windowed and
  offscreen clocks; it is a gameplay clamp, not a pacing delay.

- The loop calls `NewFrame` once per tick on the tick thread before text dispatch and `Tick`;
  offscreen calls it once per tick without input, and headless never calls it, so both values stay
  `0` there.
- The clock is a monotonic `Stopwatch`, never wall-clock time, and is not paused when the window
  is unfocused or minimized; games can pause themselves.
- `ITime` reads take one short lock that publishes both values together, so a reader never sees a
  torn double, and remain readable after `Run()` returns.
- Variable timestep: no fixed step, accumulator, `TickCount`, or interpolation. After a stall
  `Time` still jumps but `DeltaTime` is limited to the configured clamp.

## 5. Tick loop and pacing

The host has three drivers, all on the `Run()` caller thread; the renderer runs on its own thread.

- **Windowed** (`Current`):

  ```
  assetService.Start()                 // open/validate base pack, start loader; Bork2dException here
  platform.Start()                     // SDL window + Vulkan instance + surface, or Bork2dException
  renderer = factory.Create(startInfo); renderer.Start()   // returns without readiness (D-M6-06)
  attach window/input proxies
  platform.AttachRenderer(renderer)    // OS thread posts confirmed extent/fullscreen/minimized
  if (!Exit && !close) Setup(engine)   // may call Exit(); never interrupted by renderer state
  if (!Exit && !close && no renderer failure) renderer.WaitForReady()
  while (!Exit && !close && !rendererFatal && !platformFatal):
      input.Freeze()                   // tick-start snapshot
      time.NewFrame()
      input.DispatchText()             // OnTextTyped before Tick
      game.Tick()
  if Setup succeeded: Teardown()       // window, input, and assets still alive
  renderer.RequestStop(); renderer.Join()   // before the surface/instance/window are destroyed
  platform.Shutdown()                  // surface, instance, loader, window, SDL, OS thread
  assetService.Stop()                  // cancel loads, close packs, join loader
  detach proxies
  ```

  There is **no** fixed rate, target FPS, deadline wait, `Sleep`, `SpinWait`, or frame limiter;
  the loop consumes a core, which is accepted until M7 supplies frame-handoff backpressure. The
  only blocking waits in the process are the OS thread's SDL event wait, readiness before the
  first tick, the renderer join, the asset service's loader join, and ordinary locks.
- **Offscreen** (`Current`, D-M6-07): the same shape without a window, input, or SDL:

  ```
  assetService.Start()
  renderer = factory.Create(offscreen startInfo); renderer.Start()
  if (!Exit) Setup(engine)
  if (!Exit && no renderer failure) renderer.WaitForReady()
  while (!Exit && !rendererFatal):
      time.NewFrame()
      game.Tick()
  Teardown()                          // once, when Setup succeeded
  renderer.RequestStop(); renderer.Join()
  assetService.Stop()
  ```

  Ticks are uncapped and render frames are paced only by GPU fences (D-M6-12); `IWindow` members
  and the `IInput` getter still throw `NotSupportedException`, while `ITime` advances per tick.
- **Headless** (`Current`): `assetService.Start()`, exactly one `Setup`, one `Tick`, and one
  `Teardown`, then `assetService.Stop()`, with no platform, renderer, Vulkan loader, or clock
  advance. A failed asset start runs no `Setup`/`Teardown`; a `Setup` failure runs no `Teardown`.
  This preserves the M1 AOT gate; M7 replaces it with a paced loop.
- `IEngine.Exit()` finishes the current tick and stops the windowed or offscreen loop before the
  next boundary; a close request between ticks stops the windowed loop before the next freeze.
  Exit/close during `Setup` runs zero ticks, skips the readiness wait, and still tears down once
  (D-M2-19/D-M6-06). Headless ignores `Exit()`.
- A renderer fatal after the first tick stops the loop at the next boundary, runs `Teardown`,
  joins the renderer, and is rethrown. A fatal platform error follows the M2 path and is rethrown
  after `Teardown`.
- The windowed loop logs one Information stop line with reason, tick count, elapsed seconds, and
  the final framebuffer size; the offscreen loop logs one reason/ticks/elapsed line; the renderer
  logs start/stop counters once. There is no per-frame logging.

## 6. Shutdown and error propagation

`IGame.Teardown` is `Current` and is not a future step. It runs once on the engine tick thread
when `Setup` succeeded and is the last chance for the game to detach its subtrees, release
resources, issue best-effort window commands, or use `IEngine.Assets`. Windowed shutdown then
stops input and joins the renderer **before** the OS/window thread destroys the surface, instance,
and window; then the platform quits SDL and joins its thread, and the assets stop. Offscreen stops
and joins the renderer after `Teardown`, then stops the assets. The proxies are detached last. The
app then detaches the root's direct children on `Dispose()`. A renderer cleanup failure during
normal shutdown is logged and does not change the run's outcome (D-M6-27).

| Failure | Where detected | Behavior |
| --- | --- | --- |
| Invalid options / layer enum / conflicting modes / `WithAssetsPack`+`WithoutAssets` | `Build()` | `Bork2dException`; no window, SDL, threads, or file access. |
| Game instance missing or set twice | `Build()` | `Bork2dException`. |
| Missing/malformed base pack | `Run()`, before the window | `Bork2dException` with the resolved path and reason; no `Setup`/`Teardown`; the partially started asset service is cleaned up; no window. |
| SDL init / window creation / platform-thread startup failure | Windowed `Run()`, after assets started, before the renderer and `Setup` | `Bork2dException` with `SDL_GetError()`; no renderer, `Setup`, or `Teardown`; the asset service is stopped, the OS thread joined, SDL released, proxies detached. |
| Missing Vulkan loader / no Vulkan 1.3 / instance creation failure / invalid SDL instance extensions | Windowed `platform.Start()` (OS thread), before the renderer and `Setup` | `Bork2dException` naming the loader/version/extension; the partial instance/surface are destroyed, the window destroyed, SDL released and its thread joined; assets stopped; no `Teardown`. |
| Surface creation failure | Windowed `platform.Start()` (OS thread), before the renderer and `Setup` | `Bork2dException` with the SDL error; no surface; the created instance is destroyed, the window destroyed, SDL released; assets stopped; no `Teardown`. |
| Renderer factory/start failure | `Run()`, after platform start and during/after `Setup` | `Setup` still runs (D-M6-14); zero ticks; `Teardown` attempted when `Setup` succeeded; the renderer is stopped/joined; platform/assets shut down; `Bork2dException`. If `Setup` also throws, the `Setup` exception wins. |
| Renderer readiness failure (no device/feature/format, swapchain or offscreen target creation) | Renderer thread; observed before the first tick (D-M6-06) | `Setup` already ran; zero ticks; `Teardown` attempted; renderer stopped/joined; platform/assets shut down; `Bork2dException` rethrown (aggregated with a teardown failure). |
| Exception in `Setup` | `Run()` | Renderer stopped/joined, platform and assets shut down; original exception; `Teardown` not called. A renderer failure is ignored in favour of the `Setup` exception (D-M6-14). |
| Exception in `Tick` or a text handler | `Run()` | Renderer stopped/joined, platform and assets shut down; `Teardown` attempted; original exception rethrown. |
| `Tick` and `Teardown` both throw | `Run()` | `AggregateException` with both inner exceptions, after renderer join, platform shutdown, and asset stop. |
| Fatal renderer error after the first tick | Renderer thread → `Run()` | Loop stops at the next boundary; `Teardown` attempted; renderer joined; platform/assets shut down; fatal `Bork2dException` rethrown, aggregated with a teardown failure. |
| Fatal platform error after startup | OS thread → `Run()` | Loop stops at the next boundary; `Teardown` attempted; fatal `Bork2dException` rethrown, aggregated with a teardown failure. Assets are stopped. The OS thread has already cleaned up, so the window may be destroyed before `Teardown`; the proxies remain attached for best-effort access. |
| Renderer cleanup failure during normal shutdown | Renderer `Stop`/join | Logged; the run's existing outcome stands (D-M6-27). |
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

- **Start** blocks until the window, Vulkan instance, and surface exist or startup failed. On the
  OS thread it sets `SDL_WINDOWS_DPI_AWARENESS=permonitorv2` and
  `SDL_WINDOWS_DPI_SCALING=1` before `SDL_Init(SDL_INIT_VIDEO)`, creates one visible
  `SDL_WINDOW_VULKAN | SDL_WINDOW_ALLOW_HIGHDPI` window centered on display 0 (plus `RESIZABLE`
  when configured and `FULLSCREEN_DESKTOP` when the builder starts fullscreen), and applies the
  builder title and initial framebuffer size. SDL accepts window points, so the platform measures
  the achieved framebuffer size and adjusts the point size once with the measured scale. It then
  loads the Vulkan loader through SDL, queries SDL's required instance extensions, creates the
  instance through the injected `IVulkanInstanceFactory` (validation request from
  `WindowPlatformOptions.Validation`, application name from the title, engine name `Bork2d`), and
  creates the surface with `SDL_Vulkan_CreateSurface` on the same thread; the drawable size comes
  from `SDL_Vulkan_GetDrawableSize`. It publishes the first confirmed state and an immutable
  `VulkanWindowSnapshot` (instance, surface handle, drawable size), then logs the startup line
  including the native SDL version (2.30.8 for the pinned `Silk.NET.SDL` 2.23.0 package, whose
  label is 2.32.10) and the Vulkan validation/surface state. Any startup failure throws
  `Bork2dException` after the thread has destroyed the partial surface/instance, released the
  loader and window, quit SDL, and been joined (D-M6-09).
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
- **Renderer state posts.** The host calls `AttachRenderer` after the renderer starts. The
  OS/window thread is the single control producer: it posts the current confirmed extent (drawable
  size), fullscreen, and minimized values immediately and then only when one of those properties
  changes. Title, focus, cursor, and visibility changes never post, and the platform never blocks on
  the renderer. While minimized it posts the last non-minimized drawable extent with
  `minimized=true`; a zero drawable size never becomes the requested extent.
- **Surface recovery (D-M6-16).** `CreateReplacementSurface` is the single render→platform request.
  The render thread calls it after dropping its swapchain; it wakes the pump, and the OS thread
  creates a new surface from the same window/instance, destroys the old one, publishes a replacement
  snapshot, and returns the new handle. The wait is bounded, so a stopped pump fails the recovery
  with `0` instead of hanging; the renderer treats a failed replacement as fatal. The OS thread
  never waits on the renderer.
- **Shutdown** sets the stop flag, pushes a user event to release the event wait, and joins the
  thread. Once the host has stopped/joined the renderer, the OS thread destroys the surface, then
  the instance, unloads the Vulkan loader, destroys the window, and quits/disposes SDL (D-M6-09).
  Shutdown is idempotent and uses no timeout or sleep. A shutdown requested without a prior close
  reports the `Exit` reason; the platform logs the final framebuffer size, and the runtime loop logs
  the tick count and elapsed time.
- The platform never holds a lock across an SDL call, a game callback, or the event wait. The only
  blocking waits are SDL's event wait, the startup/exit signals, the thread join, and the bounded
  surface-recovery wait on the render thread.

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
- `IWindow` calls may originate on any thread and are marshaled to the OS/window thread. Title,
  size, fullscreen, and cursor commands apply on the next pump; close requests a graceful exit after
  the current tick.
- While minimized, ticks continue and `Size` keeps the last non-minimized framebuffer size; the
  renderer skips acquire/present and waits on its control wake event.
- `ITime` follows D-M2-16 (first-tick zero, raw monotonic `Time`, clamped `DeltaTime`), also
  advances per tick in offscreen mode, and keeps its last values readable after shutdown; see
  [Threading](threading.md#5-input-across-threads-windowed-only).

## 8. Related documents

- [Scene ownership](scene-ownership.md) — root-owned lifetime model.
- [Threading and frame handoff](threading.md) — snapshot ring and bounded run-ahead.
- [Rendering](rendering.md) — windowed/offscreen render modes.
- [Assets and tooling](assets-and-tooling.md) — pack loading, tooling, and the default pack.
- [Logging](logging.md) — default ZLogger configuration and `IEngine.LoggerFactory`.
- [NativeAOT](nativeaot.md) — headless/AOT startup constraints.
- [API reference](../api/reference.md#12-runtime-surface) — public builder and app surface.
- [Roadmap](../roadmap.md) — milestone status.
