# Logging

> **Status:** `Current` for M1 (WP5). The builder creates an app-owned
> `Microsoft.Extensions.Logging` factory, installs a pretty ZLogger console provider by default,
> logs startup mode/validation, and exposes the factory through `IEngine.LoggerFactory`.
> Windowed, offscreen, and headless configurations are identical. WP6 validated the default
> logging path inside the AOT smoke app with zero AOT/trim warnings.

## 1. Surface

- `IEngine.LoggerFactory` (`ILoggerFactory`) is the public access point.
- Game code creates category loggers. The game instance is constructed **before** the builder,
  so the engine-owned factory is available in `Setup`, not in the game constructor:

```csharp
public sealed class MyGame : IGame<GameLayer>
{
    private ILogger<MyGame> _logger = NullLogger<MyGame>.Instance;

    public void Setup(IEngine<GameLayer> engine)
    {
        _logger = engine.LoggerFactory.CreateLogger<MyGame>();
        _logger.LogInformation("Loaded level {Level}", "start");
    }
}
```

- `IEngine.LoggerFactory` is never `null` for an app built by `Bork2dAppBuilder`. The internal
  engine falls back to `NullLoggerFactory.Instance` only when it is constructed directly without
  a factory (tests/internal composition).
- The factory is created by `Build()` and owned by the app; `Bork2dApp.Dispose()` disposes it
  (idempotently) via the app runtime. Do not dispose it yourself while the app is alive.
- `Bork2d.Api` uses `Microsoft.Extensions.Logging.Abstractions` from the .NET 11 shared
  framework for these types. It has no logging package reference and no ZLogger dependency; no
  GPU/OS/Silk types are allowed there.
- `Bork2d.Runtime` references the pinned `Microsoft.Extensions.Logging` package and ZLogger and
  owns provider configuration.
- Logging is thread-safe. Render, asset, and game worker threads may log; the engine's per-frame
  hot paths must avoid allocating log messages unless the level is enabled.
- `IEngine.LoggerFactory` is available in windowed, offscreen, and headless modes.
- Constructor injection remains useful for services the host owns (network, save store, a
  host-created logger). It is not the path for the engine's factory, which does not exist until
  `Build()`.

## 2. Builder configuration

```csharp
var game = new MyGame();

using var app = new Bork2dAppBuilder<GameLayer>()
    .WithGame(game)
    .WithLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Information);
        // Add/replace ZLogger providers here, e.g. console or file.
    })
    .Build();

app.Run();
```

- `Build()` validates the configuration first, then creates exactly one factory with
  `LoggerFactory.Create(...)`.
- The builder sets the default minimum level (`Debug` in debug builds, `Information` in release
  builds) before invoking `WithLogging`.
- If `WithLogging` is supplied, it is invoked **exactly once** during `Build` and owns provider
  configuration. The builder does **not** add the default console provider in that case. An
  action that adds no provider therefore produces a factory with no providers (and loggers that
  report every level disabled).
- If `WithLogging` is not called, the builder installs the default pretty ZLogger console
  provider described in §3.
- A custom `ILoggerFactory` injection overload is not part of v1; the builder action is the
  configuration seam.
- There is no provider discovery, assembly scanning, or reflection. Providers are configured
  explicitly by the builder or the caller.

### Provider ownership and disposal

- The app owns the factory and disposes it exactly once. `Bork2dApp.Dispose()` is idempotent;
  disposing the app twice still disposes the factory once.
- Microsoft.Extensions.Logging 11 creates providers through an internal service provider. That
  container disposes providers it creates: the default ZLogger console provider (registered by
  ZLogger's factory delegate), and user providers registered by type
  (`logging.Services.AddSingleton<ILoggerProvider, MyProvider>()`) or by factory.
- A provider **instance** passed to `logging.AddProvider(instance)` is not created by that
  container, so the app-owned factory disposes those instances after the container shuts down.
  This flushes ZLogger file sinks and other user providers. Register a provider this way when
  your code owns the instance; the app takes over disposal at `Dispose`.
- Disposal order is: inner container/factory providers first, then tracked provider instances.
  Factory disposal is idempotent.

## 3. Default configuration

Pinned packages (no floating versions):

| Package | Version | Project |
| --- | --- | --- |
| `Microsoft.Extensions.Logging` | `11.0.0-rc.1.26425.128` | `Bork2d.Runtime` |
| `ZLogger` | `2.5.10` | `Bork2d.Runtime` |
| `Microsoft.Extensions.Logging.Abstractions` | .NET 11 shared framework | `Bork2d.Api` (no package reference) |

The default is:

- ZLogger console provider: `AddZLoggerConsole(...)`;
- ZLogger plain-text formatter: `UsePlainTextFormatter(...)`;
- prefix `"{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}: "` formatted from
  `info.Timestamp.Local`, `info.LogLevel`, and `info.Category.Name`;
- local time with milliseconds; no ANSI color codes (readable when redirected or captured);
- minimum level `Information` in release builds, `Debug` in debug builds;
- one line per event; exception text follows the message;
- no file sink and no structured sink by default (add ZLogger file/JSON providers explicitly
  with `WithLogging`);
- local/UTC choice: local by default, as shown; use a custom formatter for UTC.

A default headless run produces:

```text
2026-10-25 21:45:15.981 [Information] Bork2d.Runtime: Bork2d starting: mode=Headless, validation=passed, layers=Layer
2026-10-25 21:45:15.983 [Information] MyGame: Loaded level start
```

`AddZLoggerConsole()` with no formatter configuration emits only the message in ZLogger 2.5.10,
so the engine always installs the prefix formatter.

## 4. Categories

Engine categories use the assembly/area name. `Bork2d.Runtime` exposes them as internal
constants for engine code and tests; game code creates its own categories.

| Category | Source |
| --- | --- |
| `Bork2d.Runtime` | builder, modes, lifecycle, windowing |
| `Bork2d.Common` | entities, snapshots, time |
| `Bork2d.Assets` | pack parsing, decompression, loading |
| `Bork2d.Render` | device, swapchain, pipelines, frames |
| game-defined | `CreateLogger("MyGame")` or `CreateLogger<T>()` |

Startup logs use `Bork2d.Runtime`. WP5 logs one structured startup message after validation:

```text
Bork2d starting: mode={Mode}, validation=passed, layers={LayerType}
```

`Mode` is the selected `Bork2dAppMode`; `LayerType` is the game layer enum's full name. The
message is structured (parameters, not interpolation). Logging configuration is identical in
windowed/headless/offscreen; the mode value is the only difference.

Diagnostics should include the pack path/asset key, frame sequence, camera, or GPU object name
where relevant.

## 5. Threading and performance

- Logging calls are thread-safe and may occur from the render thread, asset loaders, and game
  workers.
- Do not log per sprite/instance in a hot loop. Use counts and summaries.
- Use structured parameters; avoid string interpolation for disabled levels.
- The default console provider writes through ZLogger's background processor. App disposal
  flushes/stops it. File output should use ZLogger's file/rolling providers, which are also
  flushed at app disposal.
- Logging must not become a synchronization point: the default provider should not block the
  tick or render thread on slow I/O.
- A fatal render/device error is logged and propagated; logging must not swallow it.

## 6. NativeAOT

- Microsoft.Extensions.Logging is trim/AOT-friendly; ZLogger 2.5.10 with the console provider
  was validated under `PublishAot` in M1 spikes.
- No runtime reflection-based provider discovery; providers are configured explicitly by the
  builder/default.
- Source-generated log methods (`[LoggerMessage]`) are optional but encouraged for hot/high-
  volume messages.
- If ZLogger or a provider is not AOT-compatible, isolate it behind the logging configuration
  and choose an AOT-safe console provider; the `ILoggerFactory` surface stays.
- WP6 validated the default logging path inside the AOT smoke app with zero trim/AOT warnings.
- `Bork2d.Editor`/baker are JIT and may use the same logging stack without AOT constraints.

## 7. Errors and diagnostics

- The engine logs startup mode and validation outcome, device/feature selection, pack mounts,
  asset failures, resize/swapchain recreation, and fatal errors as those land in later
  milestones.
- Asset-not-ready is not logged per frame; missing/failed loads log once with the key.
- `IEngine.LoggerFactory` is not a replacement for exceptions; fatal errors still propagate
  through `Run()`.
- User code should not log from the render thread through game objects; use the factory with a
  category and immutable values.

## 8. Open questions

| Topic | Decision |
| --- | --- |
| ZLogger version | Pinned `2.5.10` with `Microsoft.Extensions.Logging` `11.0.0-rc.1.26425.128`. |
| Default format | Plain-text ZLogger console with the local timestamp/milliseconds, level, and category prefix above; no ANSI colors. |
| Minimum levels | Debug builds: `Debug`; release builds: `Information`. A caller may override in `WithLogging`. |
| File logging | No default file sink; opt-in through `WithLogging`. |
| UTC | Local time in the default prefix; custom formatter for UTC. |
| Custom factory | Builder action only in v1. |
| Scopes | Not enabled in the default provider; opt-in ZLogger/MEL configuration can enable them. |
| Provider disposal | App disposes the factory and provider instances added with `AddProvider`; container-created providers are disposed by the logging container. |
| AOT fallback | Not needed: ZLogger console is AOT-safe in the M1 validation; the `ILoggerFactory` surface stays if a future provider is not. |

## 9. Current status

| Area | Current |
| --- | --- |
| `IEngine.LoggerFactory` | Implemented (WP5): returns the app-owned configured factory; `NullLoggerFactory` fallback only for direct internal construction. |
| Builder `WithLogging` | Implemented (WP5): null-checked, invoked exactly once at `Build`, suppresses the default provider. |
| ZLogger default | Implemented (WP5): pretty plain-text console provider with timestamp/level/category prefix. |
| Engine categories/log calls | Categories available; startup mode/validation logging implemented. Feature-level logs arrive with their milestones. |
| NativeAOT validation | Validated (WP6): the AOT smoke uses the default ZLogger console path with zero AOT/trim warnings. |
| File/structured sinks | None by default; opt-in. |

## 10. Related documents

- [Runtime lifecycle](runtime-lifecycle.md#3-builder-options-and-validation) — builder options.
- [NativeAOT](nativeaot.md) — AOT/trimming validation.
- [API reference](../api/reference.md#2-iengine) — `IEngine.LoggerFactory`.
- [Projects and dependencies](projects.md#3-public-surface-and-assembly-visibility) — Api
  logging dependency boundary.
- [Decision log](../decisions.md#d-021-zlogger-logging-surface) — logging decision.
