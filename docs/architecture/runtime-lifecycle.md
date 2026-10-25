# Runtime Lifecycle

> **Status:** the builder, its validation, the one-tick `Bork2dApp.Run()` sequence, and app
> `Dispose()` are `Current` (M1). Window, input, assets, renderer, tick loop, and paced modes are
> `Partial` or `Planned`: every mode runs the same one-tick smoke, `IWindow`/`IInput`/`IAssets`
> are throwing stubs, and `ITime` is never advanced.

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
`NotSupportedException` in every mode. Target behavior (`Planned`, M2):

- `IWindow` calls may originate on any thread and are marshaled to the OS/window thread. Title
  applies immediately; size and fullscreen recreate the swapchain; cursor visibility toggles;
  close requests a graceful exit after the current tick.
- `IInput` is pumped on the OS/window thread and frozen at tick start; edge flags are true for
  exactly one tick; text is dispatched on the tick thread before `Tick`; mouse coordinates are
  framebuffer pixels. On focus loss, held keys/buttons are synthesized as released and
  wheel/text state is cleared.
- `IInput` reads are thread-safe snapshots and are not part of the frame snapshot. The render
  thread cannot read them.
- While minimized, presentation is skipped; on restore the newest snapshot is drawn. DPI handling is
  decided in M2 (see roadmap).

See [Threading](threading.md#5-input-across-threads-windowed-only).

## 8. Related documents

- [Scene ownership](scene-ownership.md) — root-owned lifetime model.
- [Threading and frame handoff](threading.md) — snapshot ring and bounded run-ahead.
- [Rendering](rendering.md) — windowed/offscreen render modes.
- [Assets and tooling](assets-and-tooling.md) — pack loading, tooling, and the default pack.
- [Logging](logging.md) — default ZLogger configuration and `IEngine.LoggerFactory`.
- [NativeAOT](nativeaot.md) — headless/AOT startup constraints.
- [API reference](../api/reference.md#12-runtime-surface) — public builder and app surface.
- [Roadmap](../roadmap.md) — milestone status.
