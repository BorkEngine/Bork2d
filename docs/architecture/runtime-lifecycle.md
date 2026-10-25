# Runtime Lifecycle

> **Status:** the builder, its validation, the one-tick `Bork2dApp.Run()` sequence, app
> `Dispose()`, and the internal M2 windowed platform (OS/window thread, window commands, confirmed
> state) are `Current`. The platform is not yet attached to the engine: every mode still runs the
> same one-tick smoke, `IWindow`/`IAssets` return throwing stubs, `IInput` throws on access, and
> `ITime` is never advanced until M2 WP4.

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
  the startup mode and validation outcome from `Bork2d.Runtime`, then creates the engine, scene
  root, and app runtime (root empty until `Setup`). It creates no window, GPU resources, threads,
  or filesystem state; the factory is shared by the engine and app runtime.
- `Run()` may be called from any thread and only once. In M1 it is a smoke path: `Setup`,
  exactly one `Tick`, then `Teardown`, then return. A failed run still counts as the one call;
  a second call throws `InvalidOperationException`, and `Run()` after `Dispose()` throws
  `ObjectDisposedException`.
- If `Setup` throws there is nothing to tear down, so `Teardown` is not called and the original
  exception propagates. If `Tick` and `Teardown` both throw, `Run()` throws an
  `AggregateException` with both as inner exceptions; otherwise the original exception is
  rethrown. A failed run detaches the root's direct children.
- `Dispose()` is idempotent, safe before or after `Run()`, detaches the root's direct children,
  disposes the app-owned logger factory (including `AddProvider` instances), and never calls
  `IGame.Teardown`. The app owns the engine and, once implemented, the platform, renderer,
  asset service, and snapshot ring; the game instance stays caller-owned.
- Exact member names and signatures are in the
  [API reference](../api/reference.md#12-runtime-surface).

## 2. Modes

| Mode | Builder | Target behavior (`Planned`) | M1 behavior |
| --- | --- | --- | --- |
| **Windowed** (default) | normal options | SDL window, Vulkan swapchain, render thread, input | One-tick smoke; no window |
| **Offscreen** | `WithOffscreen(w, h)` | Windowless Vulkan internal target, render thread, frames discarded | One-tick smoke |
| **Headless** | `WithHeadless(ticksPerSecond = 60)` | No SDL, Vulkan, or render thread; timer-paced ticks | One-tick smoke |

- **Windowed** is the normal game. **Offscreen** is for tests, benchmarks, and visual
  validation and creates no surface/swapchain; a public readback API is future work (M8/M10).
  **Headless** is for servers and data tools: graphics are absent, so created sprites never
  render.
- `WithHeadless` and `WithOffscreen` are mutually exclusive. Window-only options (`WithTitle`,
  `WithSize`, `WithResizable`, `WithFullscreen`, `WithVSync`) are rejected in headless/offscreen
  by `Build()`.
- In M1 all three modes take the same one-tick path and differ only in option validation.
  `IEngine.Window` returns an `UnsupportedWindow` in every mode: reading the property does not
  throw, but every member throws `NotSupportedException`. `IEngine.Input` throws
  `NotSupportedException` from the getter, and `IEngine.Assets` returns an `UnsupportedAssets`
  whose methods throw `NotSupportedException`.
- In windowed/offscreen, tick pacing comes from the bounded snapshot ring and the renderer's
  frame completion (`Planned`); headless uses the configured tick rate and is the one mode with
  an intentional timer wait ([Threading](threading.md#4-triple-buffer-handoff)).

## 3. Builder options and validation

`Current` covers the builder surface, validation, and stored options; parenthetical notes mark
options whose window/GPU/timer effect is not implemented.

| Option | Default | Status | Meaning |
| --- | --- | --- | --- |
| `WithGame(IGame<TLayer> game)` | required | `Current` | Caller-owned game instance; exactly once. |
| `WithAssetsPack(string path)` | `assets.pck` | `Current (stored)` | Base pack path. Stored as a relative path; M1 does not resolve it next to the executable or touch the filesystem. |
| `WithLogging(Action<ILoggingBuilder>)` | pretty ZLogger console | `Current` | Configures the app-owned factory; invoked once at `Build` and suppresses the default provider. |
| `WithTitle(string)` | entry assembly name, else `Bork2d` | `Current (stored)` | Window title; windowed only. |
| `WithSize(int width, int height)` | `1280 × 720` | `Current (stored)` | Initial framebuffer size; windowed only. |
| `WithResizable(bool)` | `true` | `Current (stored)` | Windowed only. |
| `WithFullscreen(bool)` | `false` | `Current (stored)` | Windowed only; exact mode is M2 (see roadmap). |
| `WithVSync(bool)` | `true` | `Current (stored)` | Windowed present-mode preference. |
| `WithHeadless(int ticksPerSecond = 60)` | `60` | `Current (stored)` | No window/GPU/render thread; `0` = uncapped. |
| `WithOffscreen(int width, int height)` | — | `Current (stored)` | Windowless Vulkan rendering; frames discarded. |
| `WithMaxDeltaTime(double)` | `0.25` s | `Current (stored)` | Safety clamp for `ITime.DeltaTime`; not a pacing delay. |
| `WithValidation(bool)` | `true` in Debug, `false` in Release | `Current (stored)` | Vulkan validation layers (windowed/offscreen) |
| `Build()` | — | `Current` | Validates and creates the app object. |
| `Bork2dApp.Run()` | — | `Current (M1 smoke)` | One `Setup`/`Tick`/`Teardown`; returns. |

Validation and creation performed by `Build()`:

1. `Build` may run on any thread.
2. `WithGame` was called exactly once with a non-null instance.
3. Exactly one mode is selected (default windowed, or headless, or offscreen).
4. Window-only options are rejected in headless/offscreen.
5. `TLayer` is an `int`-backed `[Flags]` enum with 1–32 named single-bit values; `None = 0` is
   allowed and composite non-zero values are rejected.
6. Sizes are positive; `MaxDeltaTime` is greater than zero and not `NaN` (positive infinity
   passes); headless tick rate is `>= 0`; the window title and pack path are non-empty.
7. Validation runs first, then the factory is created and the `WithLogging` callback invoked once
   (or the default ZLogger console provider installed), then the startup mode and validation
   outcome are logged. The app owns and disposes the factory.
8. The pack path is shape-checked only: non-empty, stored relative (default `assets.pck`), no
   filesystem access. Existence, parsing, and checksum failures arrive with the M3 pack and
   throw at startup then.

Invalid configuration throws `Bork2dException` from `Build()`. `WithTitle` and `WithAssetsPack`
also reject an invalid string immediately with `ArgumentException`; null arguments throw
`ArgumentNullException` at the setter.

## 4. Time

| Property | M1 status | Planned semantics |
| --- | --- | --- |
| `ITime.DeltaTime` | `Partial`: always `0` | Seconds since the previous tick start, clamped to `MaxDeltaTime`; first tick is `0`. |
| `ITime.Time` | `Partial`: always `0` | Monotonic gameplay time in seconds, starting at `0`. |
| `MaxDeltaTime` | `Partial`: validated and stored, never applied | Safety clamp after a stall or debugger pause; not a pacing delay. |

- `Build()` creates a `StopwatchTime`, but the M1 smoke never calls `NewFrame`, so both values
  stay `0`; the engine never advances or clamps them. Wiring them with the semantics above is
  M2/M7.
- The clock is a monotonic `Stopwatch`, never wall-clock time, and is not paused when the window
  is unfocused or minimized; games can pause themselves.
- `ITime` reads are thread-safe snapshots. Workers do not need a passed delta unless the game
  wants a consistent logical step.
- `DeltaTime` includes time spent waiting for a snapshot slot, so after a long stall it can be
  large until the clamp limits the gameplay step.

## 5. Tick loop and pacing

M1 has no loop: `Run()` performs one `Setup`, one `Tick`, one `Teardown`. The target loop is
`Planned`.

- **Windowed/offscreen:** no fixed tick rate, target FPS, deadline wait, `Sleep`, or artificial
  frame limiter. Three snapshot slots provide bounded run-ahead; the tick blocks only when all
  slots are occupied, and the renderer always takes the newest ready snapshot, dropping older
  ones. See [Threading](threading.md#4-triple-buffer-handoff).
- **Headless:** paced to `ticksPerSecond` (default `60`; `0` = uncapped) with a timer, not a
  busy-spin. This is the one intentional pacing delay in the engine.
- `WithMaxDeltaTime` clamps `DeltaTime`; it does not delay ticks, and there is no interpolation:
  the renderer draws the newest snapshot as-is.
- `IEngine.Exit()` finishes the current tick, publishes the final snapshot, and shuts down. In
  M1 it only sets a flag: the one-tick smoke ignores it and no loop consumes it.
- A renderer fatal error stops the loop at the next tick boundary and `Run()` throws
  (`Planned`, M7).

## 6. Shutdown and error propagation

`IGame.Teardown` is `Current` and is not a future step. It runs once on the engine tick thread
when `Setup` succeeded and is the last chance for the game to detach its subtrees or release
resources. The app then detaches the root's direct children; asset requests during `Teardown`
are not guaranteed to complete. Headless skips render/asset shutdown steps (all `Planned`).

| Failure | Where detected | Behavior |
| --- | --- | --- |
| Invalid options / layer enum / conflicting modes | `Build()` | `Bork2dException`; no window or threads. |
| Game instance missing or set twice | `Build()` | `Bork2dException`. |
| Exception in `Setup` | `Run()` | Original exception; `Teardown` not called. |
| Exception in `Tick` | `Run()` | `Teardown` attempted; original exception rethrown. |
| `Tick` and `Teardown` both throw | `Run()` | `AggregateException` with both inner exceptions. |
| Second `Run()` / `Run()` after `Dispose()` | `Run()` | `InvalidOperationException` / `ObjectDisposedException`. |
| Window, Vulkan, pack, shader, device-lost failures | Startup / render thread | Fatal; `Run()` throws (`Planned`, M2/M3/M6/M7). |

- `Dispose()` after a failed `Run()` still attempts cleanup and suppresses only secondary
  exceptions, so the caller keeps the original exception.
- The engine does not swallow exceptions. `Run()` is the single place where background-thread
  failures are rethrown on the caller's thread, with cleanup in a `finally` (`Planned` once
  background threads exist).

## 7. Window and input (windowed only)

M1 has no window or input: `IWindow` members and the `IInput` getter throw
`NotSupportedException` in every mode. M2 WP2 adds the internal windowed platform described in
§7.1 and M2 WP3 adds the internal input accumulator/freeze described in §7.2. M2 WP4 attaches both
proxies to the engine and runs the tick loop; until then `IEngine.Window` returns the throwing stub
and `IEngine.Input` throws.

### 7.1 Windowed window platform (M2 WP2, internal)

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
  the startup line. A startup failure throws `Bork2dException` with the SDL error text after the
  thread has cleaned up and been joined.
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
  `Bork2dException`, requests an error exit, and stops the pump.
- **Shutdown** sets the stop flag, pushes a user event to release the event wait, and joins the
  thread. The OS thread destroys the window first, then quits and disposes SDL. Shutdown is
  idempotent and uses no timeout or sleep. A shutdown requested without a prior close reports the
  `Exit` reason; the final framebuffer size is logged and ticks/elapsed time are left to WP4.
- The platform never holds a lock across an SDL call, a game callback, or the event wait. The only
  blocking waits are SDL's event wait, the startup/exit signals, and the thread join.

### 7.2 Input and time

`Status: Current (internal, M2 WP3) for input; M2 WP4 for the loop and clock.`

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
- `IWindow` calls may originate on any thread and are marshaled to the OS/window thread. Title
  applies immediately; size and fullscreen change the window; cursor visibility toggles; close
  requests a graceful exit after the current tick.
- While minimized, ticks continue and `Size` keeps the last non-minimized framebuffer size;
  presentation is irrelevant until M6.
- `ITime` wiring (first-tick zero, raw monotonic `Time`, clamped `DeltaTime`), the windowed loop, and
  the public proxy attachment are M2 WP4; see
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
