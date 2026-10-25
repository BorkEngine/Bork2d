# Logging

> **Status:** planned for M1. The engine has no logging today. The target is
> `Microsoft.Extensions.Logging` with [ZLogger](https://github.com/Cysharp/ZLogger) providers,
> exposed through `IEngine.LoggerFactory` and configured from the builder.

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

- `Bork2d.Api` references `Microsoft.Extensions.Logging.Abstractions` for these types. That is
  the only logging dependency allowed in the contracts assembly.
- `Bork2d.Runtime` references Microsoft.Extensions.Logging and ZLogger and owns the default
  provider configuration.
- Logging is thread-safe. Render, asset, and game worker threads may log; the engine's
  per-frame hot paths must avoid allocating log messages unless the level is enabled.
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

- `WithLogging(Action<ILoggingBuilder>)` runs while the app builds the factory.
- If `WithLogging` is not called, the builder installs a **pretty ZLogger console provider**.
  A default run should be immediately readable: timestamp, level, category, message, and
  exception with colors when the console supports it.
- If `WithLogging` is called, it is responsible for provider configuration; the builder does
  not add the default console provider twice. The app owns/disposes the resulting factory.
- Logging configuration is independent of mode. Headless servers get the same console default;
  file/structured sinks are user/engine configuration.
- A custom `ILoggerFactory` injection overload is not part of v1; the builder action is the
  configuration seam.

## 3. Default configuration

The proposed pretty default:

- ZLogger console provider;
- timestamp with milliseconds and local time (UTC option `Open`);
- level, category, message, scope/exception rendering;
- colors when attached to an interactive console; plain text otherwise;
- minimum level `Information` in release, `Debug` in debug builds;
- no log file by default (add ZLogger file/rolling providers explicitly);
- one line per event where possible; multi-line exceptions follow.

Exact formatter options are settled in M1 when ZLogger is wired.

## 4. Categories

Engine categories use the assembly/area name:

| Category | Source |
| --- | --- |
| `Bork2d.Runtime` | builder, modes, lifecycle, windowing |
| `Bork2d.Common` | entities, snapshots, time |
| `Bork2d.Assets` | pack parsing, decompression, loading |
| `Bork2d.Render` | device, swapchain, pipelines, frames |
| game-defined | `CreateLogger("MyGame")` or `CreateLogger<T>()` |

Diagnostics should include the pack path/asset key, frame sequence, camera, or GPU object name
where relevant.

## 5. Threading and performance

- Logging calls are thread-safe and may occur from the render thread, asset loaders, and game
  workers.
- Do not log per sprite/instance in a hot loop. Use counts and summaries.
- Use structured parameters; avoid string interpolation for disabled levels. ZLogger's
  interpolation/formatting should be validated for allocation behavior.
- Logging must not become a synchronization point: the default provider should not block the
  tick or render thread on slow I/O. Async/background sinks are preferred for file output.
- A fatal render/device error is logged and propagated; logging must not swallow it.

## 6. NativeAOT

- Microsoft.Extensions.Logging is trim/AOT-friendly; validate ZLogger and its providers under
  `PublishAot` in M1.
- No runtime reflection-based provider discovery; providers are configured explicitly by the
  builder/default.
- Source-generated log methods (`[LoggerMessage]`) are optional but encouraged for hot/high-
  volume messages.
- If ZLogger or a provider is not AOT-compatible, isolate it behind the logging
  configuration and choose an AOT-safe console provider; the `ILoggerFactory` surface stays.
- `Bork2d.Editor`/baker are JIT and may use the same logging stack without AOT constraints.

## 7. Errors and diagnostics

- The engine logs startup mode, device/feature selection, pack mounts, asset failures, resize/
  swapchain recreation, validation messages, and fatal errors.
- Asset-not-ready is not logged per frame; missing/failed loads log once with the key.
- `IEngine.LoggerFactory` is not a replacement for exceptions; fatal errors still propagate
  through `Run()`.
- User code should not log from the render thread through game objects; use the factory with a
  category and immutable values.

## 8. Open questions

| Topic | Question | Proposed default |
| --- | --- | --- |
| ZLogger version | Pin latest stable compatible with .NET 11/NativeAOT. | Latest stable at M1. |
| Default format | Timestamp/level/category colors and line layout. | Pretty console, local time, milliseconds. |
| Minimum levels | Debug vs release defaults. | Debug: Debug; Release: Information. |
| File logging | Include a default rolling file? | No; opt-in provider. |
| UTC | Local or UTC timestamps? | Local default, UTC option. |
| Custom factory | Support injecting a prebuilt `ILoggerFactory`? | Builder action only in v1. |
| Scopes | Enable scopes for frames/ticks? | Optional; avoid per-frame overhead. |
| Provider disposal | Who disposes user-added providers? | App disposes the factory; user providers follow it. |
| AOT fallback | What if ZLogger provider fails AOT validation? | AOT-safe console provider behind the same API. |

## 9. Current status

| Area | Current | Target |
| --- | --- | --- |
| `IEngine.LoggerFactory` | Missing | M1 |
| Builder `WithLogging` | Missing | M1 |
| ZLogger default | Missing | M1 |
| Engine categories/log calls | None | M1+ |
| NativeAOT validation | None | M1 |
| File/structured sinks | None | Later/opt-in |

## 10. Related documents

- [Runtime lifecycle](runtime-lifecycle.md#3-builder-options-and-validation) — builder options.
- [NativeAOT](nativeaot.md) — AOT/trimming validation.
- [API reference](../api/reference.md#2-iengine) — `IEngine.LoggerFactory`.
- [Projects and dependencies](projects.md#3-public-surface-and-assembly-visibility) — Api
  logging dependency boundary.
- [Decision log](../decisions.md#d-021-zlogger-logging-surface) — logging decision.
