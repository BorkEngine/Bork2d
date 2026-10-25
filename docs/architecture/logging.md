# Logging

> **Status:** `Current` for M1. The builder creates an app-owned Microsoft.Extensions.Logging
> factory, installs a plain-text ZLogger console provider by default, logs startup
> mode/validation, and exposes the factory through `IEngine.LoggerFactory`. Configuration is
> identical in all modes, and the default path runs in the AOT smoke with zero trim/AOT warnings.

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
- `Bork2d.Api` uses `Microsoft.Extensions.Logging.Abstractions` from the .NET 11 shared
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
| `Microsoft.Extensions.Logging.Abstractions` | .NET 11 shared framework | `Bork2d.Api` (no package reference) |

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
| `Bork2d.Common` | entities, snapshots, time |
| `Bork2d.Assets` | pack parsing, decompression, loading |
| `Bork2d.Render` | device, swapchain, pipelines, frames |
| game-defined | `CreateLogger("MyGame")` or `CreateLogger<T>()` |

Startup logs use `Bork2d.Runtime` and one structured message after validation:

```text
Bork2d starting: mode={Mode}, validation=passed, layers={LayerType}
```

`Mode` is the selected `Bork2dAppMode`; `LayerType` is the game layer enum's full name. The
message uses parameters, not interpolation; never log per sprite or per frame in hot paths.

## 6. NativeAOT

- Microsoft.Extensions.Logging is trim/AOT-friendly; the ZLogger console provider is validated
  under `PublishAot` by `Bork2d.AotSmoke` with zero trim/AOT warnings.
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
