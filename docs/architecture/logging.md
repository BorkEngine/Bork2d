# Logging

> **Status:** `Current` through M3 (merged into `develop` 2026-10-26, PR #4), M6 (Vulkan
> bring-up; merged 2026-10-31, PR #11), and M7 (frame handoff and pacing; implemented on
> `m-7/common`, the ready-for-review PR is open/awaiting human review and is not merged). The
> builder creates an app-owned
> Microsoft.Extensions.Logging factory, installs a plain-text ZLogger console provider by default,
> logs startup mode/validation, and exposes the factory through `IEngine.LoggerFactory`. The
> windowed platform logs startup/stop/commands/failures, the runtime host logs the windowed,
> offscreen, and headless loop stops with ticks and elapsed time (plus the M7 handoff counters), the
> renderer logs instance/device/swapchain/offscreen target and start/stop counters from
> `Bork2d.Render`, and the asset service logs start/overlay/error/stop events; there is no per-frame
> or per-asset-per-tick logging. Configuration is identical in all modes, and the default path runs in
> the AOT gate (`Pong.Desktop`, headless with the baked pack for 120 ticks by default or
> `--headless --ticks N`). The suite is 1158 tests (1141
> deterministic plus 17 opt-in/conditional). The project-consolidation refactor (D-041/D-042) renamed
> the engine categories to `Bork2d.Engine` and `Bork2d.Engine.Assets`; `Bork2d.Runtime` and
> `Bork2d.Render` are unchanged. M6 makes the `Bork2d.Render` category current for instance,
> validation, and device-selection events (§5.3), the windowed swapchain/clear/present and
> start/stop counter lines, and the offscreen target/start/stop lines and the offscreen
> loop-stop line. M7 adds the handoff fields/counters to the same one-shot renderer start/stop
> lines (§5.3) and the handoff counters to the windowed/offscreen loop-stop lines and the
> headless loop-stop line (§5.1). The Pong example (D-043/D-044) owns the game-side title/logging
> behavior.

## 1. Surface

- `IEngine.LoggerFactory` (`ILoggerFactory`, a Microsoft.Extensions.Logging type) is the public
  access point; game code creates category loggers from it.
- The game instance is constructed **before** the builder, so the engine-owned factory is
  available in `Setup`, not in the game constructor. The game keeps a field initialized to
  `NullLogger<MyGame>.Instance` until then:

```csharp
public void Setup(IEngine<GameLayer> engine)
{
    _logger = engine.LoggerFactory.CreateLogger<MyGame>();
}
```

- `IEngine.LoggerFactory` is never `null` for an app built by `Bork2dAppBuilder`. The internal
  engine falls back to `NullLoggerFactory.Instance` only when constructed directly without a
  factory (tests).
- The factory is created by `Build()` and owned by the app; `Bork2dApp.Dispose()` disposes it
  idempotently. Do not dispose it yourself while the app is alive.
- `Bork2d.Engine` uses `Microsoft.Extensions.Logging.Abstractions` from the .NET 11 shared
  framework; `Bork2d.Runtime` pins the logging packages and owns provider configuration.
- Logging is thread-safe, and the factory is available in every mode.

## 2. Builder configuration

```csharp
var game = new MyGame();
using var app = new Bork2dAppBuilder<GameLayer>()
    .WithGame(game)
    .WithLogging(logging => logging.SetMinimumLevel(LogLevel.Information))
    .Build();

app.Run();
```

- `Build()` validates first, then creates exactly one factory with `LoggerFactory.Create(...)`.
  It sets the default minimum level (`Debug` in debug builds, `Information` in release builds)
  before invoking `WithLogging`.
- If `WithLogging` is supplied, it runs **exactly once** during `Build` and owns provider
  configuration; the builder does **not** add the default console provider. An action that adds
  no provider produces a factory with no providers.
- If `WithLogging` is omitted, the builder installs the default console provider described in §3.
- A custom `ILoggerFactory` injection overload is not part of v1; the builder action is the seam.
  There is no provider discovery, assembly scanning, or reflection.
- Builder lifecycle and validation order are in [Runtime lifecycle](runtime-lifecycle.md).

## 3. Provider ownership and disposal

- The app owns the factory and disposes it exactly once (`Bork2dApp.Dispose()` is idempotent).
  Microsoft.Extensions.Logging 11 creates providers through an internal service provider, which
  disposes providers it creates: the default ZLogger console provider and providers registered by
  type (`logging.Services.AddSingleton<ILoggerProvider, MyProvider>()`) or by factory.
- A provider **instance** passed to `logging.AddProvider(instance)` is not created by that
  container, so the app-owned factory disposes those instances after the container shuts down;
  that flushes ZLogger file sinks and other user providers. Container providers are disposed
  first, then tracked instances; factory disposal is idempotent.

## 4. Default configuration

| Package | Version | Project |
| --- | --- | --- |
| `Microsoft.Extensions.Logging` | `11.0.0-rc.1.26425.128` | `Bork2d.Runtime` |
| `ZLogger` | `2.5.10` | `Bork2d.Runtime` |
| `Microsoft.Extensions.Logging.Abstractions` | .NET 11 shared framework | `Bork2d.Engine` (no package reference) |

- ZLogger console provider (`AddZLoggerConsole`) with the plain-text formatter
  (`UsePlainTextFormatter`);
- prefix `"{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}: "`, formatted from `info.Timestamp.Local`,
  `info.LogLevel`, and `info.Category.Name`;
- local time with milliseconds; no ANSI color codes;
- minimum level `Debug` in debug builds, `Information` in release builds;
- one line per event; exception text follows the message;
- no file or structured sink by default; add ZLogger file/JSON providers with `WithLogging`.

A default headless run produces:

```text
2026-10-25 21:45:15.981 [Information] Bork2d.Runtime: Bork2d starting: mode=Headless, validation=passed, layers=Layer
2026-10-25 21:45:15.983 [Information] MyGame: Loaded level start
```

`AddZLoggerConsole()` with no formatter configuration emits only the message in ZLogger 2.5.10,
so the engine always installs the prefix formatter.

## 5. Categories

Engine categories use the assembly/area name. `Bork2d.Runtime` exposes them as internal constants;
game code creates its own categories.

| Category | Source |
| --- | --- |
| `Bork2d.Runtime` | builder, modes, lifecycle, windowing |
| `Bork2d.Engine` | reserved for scene/time/snapshot diagnostics; no current log sites |
| `Bork2d.Engine.Assets` | pack parsing, decompression, loading |
| `Bork2d.Render` | Vulkan instance/validation/device selection (M6 WP-2); windowed swapchain create/recreate/minimize and renderer start/stop counters (WP-3); offscreen target/start/stop counters (WP-4) |
| game-defined | `CreateLogger("MyGame")` or `CreateLogger<T>()` |
Startup logs use `Bork2d.Runtime` and one structured message after validation:

```text
Bork2d starting: mode={Mode}, validation=passed, layers={LayerType}
```

`Mode` is the selected `Bork2dAppMode`; `LayerType` is the game layer enum's full name. The
message uses parameters, not interpolation; never log per sprite or per frame in hot paths.

### 5.1 Windowed platform (M2)

The internal windowed platform logs from `Bork2d.Runtime`. One startup line and one stop line are
emitted per window, and the runtime host emits one loop-stop line with the tick count for windowed,
offscreen, and headless runs; window
commands and state transitions are Debug, and SDL failures are Error. There is no per-frame,
per-key, or per-mouse-move logging.

| Event | Level | Fields |
| --- | --- | --- |
| Windowed platform started | Information | `mode=Windowed`, `sdlVersion` (the `SDL_GetVersion` the loaded binary reports), `videoDriver`, `windowPoints`, `framebufferPixels`, `scale`, `fullscreen`, `resizable`, `vsync`, `vulkanValidation` (`on`/`off`), `vulkanSurface` (opaque handle). |
| Window command applied | Debug | `command` (`Title`/`Size`/`Fullscreen`/`CursorVisible`), `confirmed` value. |
| Focus gained/lost, minimized/restored/maximized, DPI scale changed | Debug | the new state, plus points/framebuffer for a scale change. |
| `IWindow.Size` ignored while fullscreen, or deferred while minimized | Debug | the requested framebuffer size. |
| SDL failure (startup or after startup) | Error | the SDL operation and the `SDL_GetError` text; the exception also carries the failure. |
| Vulkan surface replaced after surface loss | Warning | old/new surface handle and the new extent (D-M6-16). |
| Windowed loop stopped (runtime host) | Information | `reason` (`Exit`/`Close`/`Error`), `ticks` (completed ticks), `elapsedSeconds` (Setup through Teardown, rounded to milliseconds), `backpressureWaits`, `maxLiveSnapshots` (≤ 3), `suspendedConsumptions` (M7), `finalFramebuffer` (pixels). |
| Offscreen loop stopped (runtime host) | Information | `reason` (`Exit`/`Error`/`Stop`), `ticks`, `elapsedSeconds`, `backpressureWaits`, `maxLiveSnapshots`, `suspendedConsumptions` (M7). |
| Headless loop stopped (runtime host) | Information | `reason` (`Exit`/`Stop`/`Error`), `ticks` (completed ticks), `elapsedSeconds`, `ticksPerSecond` (the configured positive rate). |
| Windowed platform stopped | Information | `reason` (`Exit`/`Close`/`Error`), `finalFramebuffer` pixels. |

The startup and loop-stop lines at 150% display scaling look like:

```text
2026-10-26 01:40:39.981 [Information] Bork2d.Runtime: Bork2d windowed platform started: mode=Windowed, sdlVersion=2.30.8, videoDriver=windows, windowPoints=853x480, framebufferPixels=1280x720, scale=1.5006, fullscreen=False, resizable=True, vsync=True
2026-10-26 01:40:47.412 [Information] Bork2d.Runtime: Bork2d windowed loop stopped: reason=Exit, ticks=120, elapsedSeconds=2.031, backpressureWaits=0, maxLiveSnapshots=3, suspendedConsumptions=0, finalFramebuffer=1280x720
```

A headless run ends with its own one-shot line instead:

```text
2026-10-31 05:47:03.490 [Information] Bork2d.Runtime: Bork2d headless loop stopped: reason=Exit, ticks=120, elapsedSeconds=2.011, ticksPerSecond=60
```

A normal shutdown therefore ends with the loop-stop line followed by the platform-stop line; both
are one-shot, Information, and carry no per-frame data. The Pong `--windowed` AOT run logs its
setup/teardown through the same default path; game input transitions are Information-level events
(they occur on a transition, never once per frame), and the window title carries the score/prompt.

### 5.2 Asset service (M3)

The app-scoped asset service logs from `Bork2d.Engine.Assets`. The runtime starts it at `Run()` before the
window/`Setup` and stops it after `Teardown`; every event is one-shot and none is emitted per frame,
per tick, or per sprite.

| Event | Level | Fields |
| --- | --- | --- |
| Asset service started | Information | mode (`Pack`/`Empty`), configured path, resolved path, pack version, entry count, stored bytes |
| Overlay mounted | Information | supplied pack path, entry count |
| Base/overlay validation failure | Error | path and reason; the exception also carries the failure |
| Asset load failure | Error | canonical path, `AssetErrorKind`, reason |
| Asset load success | Debug | canonical path and kind (`data`/`messagepack`); never per tick |
| Service stopped | Information | pending, canceled, and mounted-pack counts |

A default headless run with a five-entry pack looks like:

```text
2026-10-26 16:11:58.721 [Information] Bork2d.Engine.Assets: Asset service started: mode Pack, configured pack C:\Temp\bork2d-aot-….pck, resolved path C:\Temp\bork2d-aot-….pck, version 1, entries 5, stored bytes 275.
2026-10-26 16:11:58.721 [Debug] Bork2d.Engine.Assets: Asset loaded: path data/smoke, kind data.
2026-10-26 16:11:58.721 [Information] Bork2d.Engine.Assets: Asset service stopped: pending 0, canceled 0, mounted packs 1.
```

The `WithoutAssets()` mode logs `mode Empty` with zero entries and the same start/stop lines. Pong's
headless AOT run exercises these lines through the default logging path without trim/AOT warnings,
and its `PongGame` category logs setup/teardown and score/state transitions at Information.

### 5.3 Renderer (M6 WP-2/WP-3; M7 WP-3 handoff)

The renderer logs from `Bork2d.Render`. WP-2 covers the instance/validation/device foundation;
WP-3 adds the windowed swapchain, frames, clear/present, recreation, and renderer start/stop
counters. M7 WP-3 adds the handoff fields and counters to the same one-shot start/stop lines
(handoff on, ring depth 3, one frame per snapshot, instance resources on; snapshots
published/consumed/superseded, backpressure waits, max live, suspended consumptions). Every event
is one-shot: physical-device candidates are enumerated once at startup, the selected device is
logged once, the swapchain is logged when it is created (Information) or recreated/minimized
(Debug), and validation messages are emitted only when the layer reports them. There is no
per-frame, per-slot, per-draw, or per-image logging; no line is emitted from the handoff
acquire/take/release hot path.

| Event | Level | Fields |
| --- | --- | --- |
| Vulkan loader version | Debug | the API version the loader reports |
| Instance created | Information | `apiVersion`, `validation` on/off, enabled layers/extensions |
| Validation layer requested but missing | Warning | the requested layer; the run continues without validation (D-M6-20) |
| Debug utils unavailable while validation is on | Warning | the missing extension; no messenger/labels |
| Physical-device candidate | Debug | name, type, API version, driver version, queue families, extension count, accepted/rejected reason |
| Device selected | Information | name, type, vendor/device id, API/driver version, queue family, enabled extensions/features, attachment/image/framebuffer limits, `maxPerStageDescriptorUpdateAfterBindSampledImages` |
| Swapchain created | Information | format, color space, requested/actual present mode, extent, image count |
| Swapchain recreated | Debug | reason (`Resize`/`Fullscreen`/`OutOfDate`/`Suboptimal`/`SurfaceLost`), old/new extent, format, present mode, image count |
| Minimized / zero extent | Debug | skipped acquire/present, the extent, and that snapshots are drained without GPU work (one line per minimized period) |
| Offscreen target created (renderer start line) | Information | extent, format, image count, frames in flight, total memory bytes, handoff on, ring depth 3, one frame per snapshot, instance resources on, validation on/off, counters |
| Renderer started | Information | mode, extent, format, color space/present mode (windowed), image count, frames in flight, handoff on, ring depth 3, one frame per snapshot, instance resources on, validation on/off, counters |
| Surface lost and recovered | Warning | `VkResult`, attempt number, new extent, present mode |
| Renderer stopped | Information | mode, reason, snapshots published/consumed/superseded, backpressure waits, max live snapshots, suspended consumptions (M7), frames presented/discarded, swapchain recreations, validation errors/warnings, last frame number, last present result |
| Fatal renderer failure | Error | the exception (Vulkan operation/result or validation detail); the ring is stopped so a blocked producer wakes, and the host rethrows it after teardown |
| Validation message | Warning/Error (Info/Verbose at Debug) | severity, type, message id/name, objects, message; errors are fatal only with validation enabled (D-M6-21) |
| Surface destruction or debug-messenger/device cleanup failure | Error/Warning | the operation and result; normal shutdown keeps its existing outcome (D-M6-27) |

A validation-enabled startup ends with the loader/instance/device lines followed by one selected
device line, the swapchain-created (windowed) or target line (offscreen), and the renderer-started
line. The `[GpuFact]` windowed and offscreen tests assert zero validation errors/warnings at clean
shutdown and record the counters and frame/consumed relationships. The renderer logs
frame-presented/discarded, handoff, and validation counters once at stop. No line is emitted from
the acquire/record/present hot path.

## 6. NativeAOT

- Microsoft.Extensions.Logging is trim/AOT-friendly; the ZLogger console provider is validated
  under `PublishAot` by `Pong.Desktop` with zero trim/AOT warnings.
- No runtime reflection-based provider discovery; providers are configured explicitly.
- Source-generated log methods (`[LoggerMessage]`) are optional but encouraged for hot or
  high-volume messages. If ZLogger or a provider becomes AOT-incompatible, isolate it in the
  logging configuration and choose an AOT-safe console provider; the `ILoggerFactory` surface
  stays.

## 7. Related documents

- [Runtime lifecycle](runtime-lifecycle.md) — builder options, validation, startup.
- [NativeAOT](nativeaot.md) — AOT/trimming validation.
- [API reference](../api/reference.md#2-ienginetlayer) — `IEngine.LoggerFactory`.
- [Projects and dependencies](projects.md#3-public-surface-and-assembly-visibility) — logging boundary.
