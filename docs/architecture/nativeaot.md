# NativeAOT

> **Status:** `Current` for the M1 baseline (WP6). `Bork2d.Api`, `Bork2d.Common`,
> `Bork2d.Assets`, `Bork2d.Render`, and `Bork2d.Runtime` compile with `IsAotCompatible` plus the
> trim and AOT analyzers, and `Bork2d.AotSmoke` publishes and runs with zero AOT/trim warnings.
> The smoke exercises the default logging path and a Nerdbank.MessagePack generated shape. The
> editor and build tools stay JIT; SDL/Vulkan AOT validation arrives in M2/M6.

## 1. Scope

| Component | NativeAOT required? | Why |
| --- | --- | --- |
| `Bork2d.Api` | Yes | It is referenced by the AOT game and must avoid dynamic/reflection traps. |
| `Bork2d.Common` | Yes | Runtime scene, snapshots, transforms, time, generated-shape policy. |
| `Bork2d.Assets` | Yes | Pack parsing, decompression, MessagePack decoding, resource tables. |
| `Bork2d.Render` | Yes | Vulkan renderer and uploads. |
| `Bork2d.Runtime` | Yes | Builder, host, and app-owned logging. |
| `Bork2d.AotSmoke` | Yes | M1 one-tick smoke executable; published with `PublishAot`. The game template's `Project.Desktop` takes over this role in M9. |
| `Bork2d.Editor` | No (JIT) | Avalonia desktop tool; may reflect over the game model. |
| Asset baker / pack tool | No (JIT) | Build-time tool; may reflect over game assemblies. |
| Tests | Mixed | Unit tests JIT; one local AOT publish/run smoke (`Bork2d.AotSmoke`). |

The editor being JIT is deliberate: it keeps a rich Avalonia UI and reflection-based property
grid out of the runtime's AOT/trimming constraints. The compile-time editor builder still
avoids assembly scanning, so the boundary is explicit.

## 2. Build settings

Current settings for runtime/game projects:

```xml
<!-- Api, Common, Assets, Render, Runtime (libraries) -->
<PropertyGroup>
  <IsAotCompatible>true</IsAotCompatible>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <EnableAotAnalyzer>true</EnableAotAnalyzer>
</PropertyGroup>

<!-- Bork2d.AotSmoke (executable) -->
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

Notes:

- `IsAotCompatible`/`EnableAotAnalyzer` on engine libraries catch reflection and dynamic-code
  warnings during normal builds, not only at publish. Warnings-as-errors makes every AOT/trim
  diagnostic an error; a narrow suppression needs a recorded justification.
- `PublishAot` is set only on `Bork2d.AotSmoke`. Libraries do not publish independently, and no
  library sets `PublishAot`.
- `InvariantGlobalization=true` is set for the M1 AOT smoke (human decision). The hosted game
  will choose its globalization mode when the template lands (M9).
- Native assets (SDL2, Vulkan loader) must be deployed for the target RID. The exact
  `RuntimeIdentifier` and native-copy story are part of M2/M6.
- AOT publish is a **required local gate**, not a CI job (human decision 2026-10-25): CI runs
  build/tests/format on `develop` only. See [Testing and CI](#7-testing-and-ci).

## 3. Runtime constraints

The runtime must not depend on:

- `Activator.CreateInstance`, `Type.GetType`, `Assembly.Load`, `AssemblyLoadContext`, or
  assembly scanning;
- `Reflection.Emit`, expression compilation, or dynamically generated types;
- runtime TOML parsing or reflection-based serializers;
- convention-based discovery of game types, assets, or editor registrations;
- `MakeGenericType`/`MethodInfo.Invoke` on unknown types;
- unregistered Nerdbank.MessagePack shapes;
- `dynamic` and runtime code generation in hot paths.

It may use:

- static generics and `typeof(T)` metadata that the AOT compiler can see;
- source-generated Nerdbank.MessagePack shapes, including `PackHeader`/`PackEntry`;
- explicit builder registrations (`WithGame(instance)`, `WithAssetType<T>()`,
  `WithWindow<T>()`);
- bounded validation around the generated pack header/entry shapes and known asset kinds;
- `DllImport`/Silk.NET bindings for SDL2/Vulkan.

### Game startup

The game is constructed by the developer and passed in:

```csharp
var game = new MyGame(logger, network, saveStore);

using var app = new Bork2dAppBuilder<GameLayer>()
    .WithGame(game)
    .WithAssetsPack("assets.pck")
    .Build();

app.Run();
```

There is no `new()` constraint and no reflection-based construction. This is required for
AOT and also allows arbitrary constructor injection.

## 4. Serialization and assets

- Runtime data assets are MessagePack, decoded by
  [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) **source-generated
  shapes**.
- The M1 policy (WP6) pins `Nerdbank.MessagePack` `1.4.95` in `Bork2d.Common`. An internal
  `[GenerateShape]` smoke type and a round-trip helper prove the generated-shape path; they are
  visible to `Bork2d.AotSmoke` through an intentional `InternalsVisibleTo`. No reflection
  resolver is configured. Pack and asset shapes are M3/M4; see
  [Data and serialization](data-and-serialization.md).
- `LoadAsset<T>` works only for types with a generated shape (for example `[GenerateShape]` on
  the root type). A missing shape is a build/startup error; there is no reflection fallback.
- Pack header/entry classes, texture descriptors, and sprite assets use Nerdbank generated
  shapes; they are part of the engine, not user types.
- Custom game data uses the Nerdbank generated-shape path; TOML exists only in
  `Project.Common` and is consumed by the JIT baker/editor.
- Zstd uses the .NET 11 built-in implementation; the pack header checksum uses the
  `System.IO.Hashing` package's `XxHash3`.
- Asset paths are data, not type names. Loading never resolves a type from a string.

See [Assets and pack files](assets.md) and [Data and serialization](data-and-serialization.md).

## 5. Editor and tooling boundary

`Bork2d.Editor` and the asset baker may use:

- reflection over `Project.Common` types for the property grid;
- TOML parsing/writing with reflection;
- `AssemblyLoadContext` if the project descriptor loads a game assembly;
- Avalonia and other desktop-tool dependencies.

They must not leak these dependencies into `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets`, or
`Bork2d.Render`. The runtime projects compile with AOT analyzers and no reference to the
editor.

The editor builder still uses explicit registrations:

```csharp
new Bork2dEditorBuilder()
    .WithProject("../MyGame.Common")
    .WithAssetType<EnemyDefinition>()
    .WithWindow<MyToolWindow>()
    .Build()
    .Run();
```

That is a deliberate choice for determinism and a possible future AOT editor, not a current
AOT requirement.

## 6. Native libraries and Silk.NET

- SDL2 and the Vulkan loader are native dependencies; they are deployed per target RID.
- Silk.NET bindings are used instead of dynamic P/Invoke discovery. M1 deliberately excludes
  Silk.NET/SDL; AOT validation of the SDL path is M2 and of the Vulkan path is M6.
- No runtime shader compilation: SPIR-V is built offline.
- The headless server should not initialize SDL or Vulkan; `WithHeadless(...)` must not load or
  call into the graphics/windowing paths.

These are validation items, not assumptions. If a Silk.NET path uses reflection/dynamic code
incompatibly with AOT, isolate it behind an adapter and record the workaround.

## 7. Testing and CI

Required checks:

1. `dotnet build` with AOT/trim analyzers and warnings-as-errors (Debug and Release).
2. `dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64`
   with zero AOT/trim warnings.
3. Run the AOT-published **headless** smoke app locally (no GPU required): default logging,
   one `Setup`/`Tick`/`Teardown`, clear the root, round-trip the generated shape, exit `0`.
4. Run a windowed/offscreen AOT-published app on a GPU runner before release (M8+; not M1).
5. Trim/AOT warning budget: zero warnings for every project; style and compiler warnings are
   errors. AOT/trim analyzers are errors on runtime/game projects.
6. A smoke test that no loaded asset type lacks a Nerdbank generated shape (M3/M4 introduces
   the asset loader; M1 covers the policy with `MessagePackShapeTests`).
7. AOT-published app logs through the default ZLogger/Microsoft.Extensions.Logging path without
   trim warnings; `IEngine.LoggerFactory` is usable.

The AOT publish/run commands are a **required local gate** for WP6+, WP7, and the final
`m-1/common` verification. CI is build/test/format only and runs on pushes to `develop` after a
milestone merge, so it does not catch AOT regressions itself. A JIT unit-test pass is necessary
but not sufficient; AOT failures often appear only at publish/runtime.

## 8. Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Silk.NET or a dependency uses reflection/dynamic code | AOT publish/runtime failure | Validate in M2/M6; isolate adapters; pin versions. |
| ZLogger/MEL provider not AOT-safe | Trim warnings or startup failure | Validated by the M1 AOT smoke; keep providers behind `ILoggerFactory`. |
| MessagePack source generation misses a custom type | Runtime load failure | Build-time validation; `LoadAsset<T>` registration checks; tests. |
| TOML/reflection leaks into runtime | AOT warnings/failure | Project boundary and analyzers; TOML only in baker/editor. |
| Trimming removes needed code | Subtle runtime failures | `IsAotCompatible`, analyzers, AOT smoke tests. |
| Native SDL2/Vulkan deployment | Missing DllNotFound at runtime | RID-specific packaging and clean-machine tests. |
| Server pulls in SDL/Vulkan initialization | Unwanted GPU/window dependency | `WithHeadless` path that never initializes graphics. |
| Editor keeps reflection while runtime forbids it | Boundary confusion | Separate assemblies/projects; editor JIT; runtime analyzers. |

## 9. Decisions

| Topic | Decision |
| --- | --- |
| MessagePack generation | `Nerdbank.MessagePack` `1.4.95`; `[GenerateShape]` roots; reflection resolver disabled. M1 proves the policy with an internal smoke type and round trip. |
| Globalization | `InvariantGlobalization=true` for the M1 AOT smoke (human decision); the game template chooses its mode in M9. |
| AOT target RIDs | `win-x64` for the M1 local gate; Linux when platform support is added. |
| Trimming game code | All projects warnings-as-errors; trim/AOT warnings are errors unless narrowly suppressed with recorded justification. |
| Editor AOT | Not required; keep registrations AOT-friendly anyway. |
| Native library packaging | MSBuild targets + documented redistributables (M2/M6). |
| AOT diagnostics | Fail fast with the type name for missing Nerdbank shapes (M3/M4). |

## 10. Current status

| Area | Current | Target |
| --- | --- | --- |
| AOT analyzers/settings | Implemented (WP6): `IsAotCompatible`, trim and AOT analyzers on `Api`/`Common`/`Assets`/`Render`/`Runtime`; no `PublishAot` on libraries. | M1 |
| `WithGame(instance)` | Implemented (WP4/WP5). | M1 |
| Runtime source-generated assets | Policy + internal generated-shape smoke in `Common`; no pack/asset shapes yet. | M3/M4 |
| AOT headless smoke | Implemented (WP6): `Bork2d.AotSmoke` publishes/runs one tick with default logging and a generated-shape round trip; zero AOT/trim warnings. | M1; full pack/tick/exit in M7; release gate M9 |
| Silk.NET AOT validation | Not started; Silk.NET/SDL deliberately excluded from M1. | M2 (SDL), M6 (Vulkan) |
| Editor JIT boundary | Editor absent. | M5 |
| AOT publish CI | Local gate only (human decision); CI is build/test/format on `develop`. | M1 baseline, M9 release gate |

## 11. Related documents

- [Projects and dependencies](projects.md#6-build-packaging-and-versioning) — project graph and
  build settings.
- [Assets and pack files](assets.md#13-nativeaot) — runtime pack constraints.
- [Data and serialization](data-and-serialization.md) — Nerdbank.MessagePack source generation.
- [Logging](logging.md) — ZLogger/MEL AOT notes.
- [Editor](editor.md#9-runtime-and-aot-boundary) — JIT editor boundary.
- [Roadmap](../roadmap.md) — M1 baseline, M9 release gate.
