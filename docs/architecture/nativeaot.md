# NativeAOT

> **Status:** `Current` through M2 (WP1–WP5; merged into `develop` 2026-10-26, PR #2).
> `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render`, and `Bork2d.Runtime` compile with
> `IsAotCompatible` plus the trim and AOT analyzers, and `Bork2d.AotSmoke` publishes and runs with
> zero AOT/trim warnings. The smoke exercises the default logging path, a Nerdbank.MessagePack
> generated shape, the unchanged headless one-tick run, and the windowed `--windowed [--ticks N]`
> SDL demo. M3 WP2 adds the `Bork2d.Api` PolyType contract reference for the constrained
> `IAssets.LoadAsset<T>`; the asset loader and AOT pack smoke are WP3–WP5. The editor and build
> tools stay JIT; Vulkan AOT validation is M6.

## 1. Scope

| Component | NativeAOT required? | Why |
| --- | --- | --- |
| `Bork2d.Api` | Yes | Referenced by the AOT game; must avoid dynamic/reflection traps. |
| `Bork2d.Common` | Yes | Runtime scene, transforms, time, snapshot policy, generated shapes. |
| `Bork2d.Assets` | Yes | Pack parsing, decompression, MessagePack decoding, resource tables. |
| `Bork2d.Render` | Yes | Vulkan renderer and GPU uploads. |
| `Bork2d.Runtime` | Yes | Builder, host, and app-owned logging. |
| `Bork2d.AotSmoke` | Yes | One-tick smoke executable published with `PublishAot`; the local gate. The game template's `Project.Desktop` takes over this role in M9. |
| `Bork2d.Editor` | No (JIT) | Avalonia desktop tool; may reflect over the game model. M5. |
| Asset baker / pack tool | No (JIT) | Build-time tool; may reflect over game assemblies. M4. |
| Tests | Mixed | Unit tests run JIT; `Bork2d.AotSmoke` is the one AOT publish/run smoke. |

## 2. Build settings

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
  <!-- Narrow Silk.NET resolver suppression; see §6. -->
  <NoWarn>$(NoWarn);IL3000;IL3002</NoWarn>
</PropertyGroup>
```

- `IsAotCompatible`/`EnableAotAnalyzer` catch reflection and dynamic-code warnings during normal
  builds, not only at publish. Warnings-as-errors makes every AOT/trim diagnostic an error; a
  narrow suppression needs a recorded justification.
- `PublishAot` is set only on `Bork2d.AotSmoke`; libraries do not publish independently.
- `InvariantGlobalization=true` is set for the M1 smoke (human decision); the template chooses its
  globalization mode in M9.
- AOT publish is a **required local gate**, not a CI job (human decision 2026-10-25); see §7.

## 3. Runtime constraints

The runtime must not depend on:

- `Activator.CreateInstance`, `Type.GetType`, `Assembly.Load`, `AssemblyLoadContext`, or
  assembly scanning;
- `Reflection.Emit`, expression compilation, or dynamically generated types;
- runtime TOML parsing or reflection-based serializers;
- convention-based discovery of game types, assets, or editor registrations;
- `MakeGenericType`/`MethodInfo.Invoke` on unknown types;
- unregistered Nerdbank.MessagePack shapes;
- `dynamic` or runtime code generation in hot paths.

It may use:

- static generics and `typeof(T)` metadata visible to the AOT compiler;
- source-generated Nerdbank.MessagePack shapes, including `PackHeader`/`PackEntry` (M3);
- explicit registrations: `WithGame(instance)` now, editor `WithAssetType<T>()`/
  `WithWindow<T>()` in M5;
- bounded validation around generated pack header/entry shapes and known asset kinds.

The game is a caller-constructed instance: `WithGame(instance)` has no `new()` constraint and no
reflection-based construction, which also allows constructor injection. See the
[API guide](../api/guide.md).

## 4. Serialization and assets

- Runtime data assets are MessagePack (M3/M4), decoded by
  [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) **source-generated
  shapes**; no reflection resolver is configured.
- M1 pins `Nerdbank.MessagePack` `1.4.95` in `Bork2d.Common`. An internal `[GenerateShape]` smoke
  type and round-trip helper prove the generated-shape path; `Bork2d.AotSmoke` sees them through
  the intentional `InternalsVisibleTo`. The v1 pack header/entry shapes are `Current` (M3 WP1);
  descriptor/sprite shapes are WP4.
- `LoadAsset<T>` is declared as `Task<T> LoadAsset<T>(string assetPath) where T : class,
  IShapeable<T>` (D-M3-22). `T` must carry a compiled generated shape (for example `[GenerateShape]`
  on the root type), so a missing shape is a **compile-time** error. There is no unconstrained call,
  reflection-shape fallback, runtime "missing shape" fault, or `NBMsgPack051` suppression. Pack
  header/entry classes, texture descriptors, sprite assets, and custom game data all use generated
  shapes; TOML exists only in `Project.Common` and the JIT baker/editor.
- The M3 WP3 loader body deserializes with the shape from `T.GetTypeShape()` passed explicitly to
  the serializer, so the reflection-shape provider is never consulted. The loader thread performs
  file I/O, Zstd decompression, exact-size checking, and generated-shape deserialization only; it
  never touches SDL, Vulkan, or dynamic code. The AOT smoke pack/load/browse proof is WP5.
- `Bork2d.Api` references PolyType `1.4.1` for the `IShapeable<T>` constraint only. It stays free
  of Nerdbank.MessagePack, Zstd/`System.IO.Compression`, `System.IO.Hashing`, and file I/O, so the
  public contract does not pull the pack implementation into game code. `Bork2d.Assets` keeps its
  PolyType pin for the tooling API and its Nerdbank/Hashing pins for the pack.
- Zstd is the .NET 11 built-in codec; the pack header checksum uses the `System.IO.Hashing`
  package's `XxHash3`.
- Asset paths are data, not type names; loading never resolves a type from a string.

See [Assets and tooling](assets-and-tooling.md).

## 5. Editor and tooling boundary

`Bork2d.Editor` and the asset baker may use reflection over `Project.Common` types, TOML
parsing/writing with reflection, `AssemblyLoadContext` for a game assembly loaded by the project
descriptor, and Avalonia or other desktop-tool dependencies. They must not leak those
dependencies into `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets`, or `Bork2d.Render`; the runtime
projects compile with AOT analyzers and no editor reference.

Editor registrations are explicit (`WithProject`, `WithAssetType<T>`, `WithWindow<T>`, M5) for
determinism and a possible future AOT editor, not a current AOT requirement.

## 6. Native libraries and Silk.NET

- SDL2 and the Vulkan loader are native dependencies, deployed per target RID.
- `Bork2d.Runtime` pins `Silk.NET.SDL` `2.23.0` for the windowed platform (M2, D-M2-06). No other
  engine project references it, no SDL type appears in `Bork2d.Api`, and Vulkan validation remains
  M6.
- The package brings `Ultz.Native.SDL` `2.32.10` transitively, which supplies
  `runtimes/win-x64/native/SDL2.dll`; the AOT publish output copies it next to the executable. The
  loaded binary reports `SDL_GetVersion` `2.30.8` (its Windows file version is also `2.30.8`), so
  the package version label is ahead of the binary it carries. It exposes every call M2 uses,
  including `SDL_GetWindowSizeInPixels` (2.26+) and the Windows DPI hints (2.24+).
- The SDL adapter uses `Sdl.GetApi()`, Silk.NET's default native-library resolver. That resolver
  probes `Assembly.Location`, `Assembly.CodeBase`, and `DependencyContext` for candidate paths,
  which ILC reports as `IL3000`/`IL3002` single-file trim warnings under NativeAOT. `Bork2d.AotSmoke`
  — the AOT publisher, and the host of M2 WP5's `--windowed` demo — suppresses exactly those two
  codes with a project-level `NoWarn`. No other warning code is suppressed and
  `SuppressTrimAnalysisWarnings` is not used. The suppression is justified: the SDL2 native ships
  from the package and is resolved by the runtime's normal native-asset lookup, so the flagged
  fallback probes are not used. Remove it when Silk.NET stops probing those APIs.
- M2 WP1 verified the dependency with an AOT publish/run of a throwaway windowed probe that reaches
  the adapter through the default resolver: with the suppression applied the publish reports no
  warnings (the same graph reports six `IL3000`/`IL3002` warnings when `NoWarn` is cleared); SDL
  video init, window create/destroy, and quit succeed on an engine-created (non-main) thread; and
  `SDL2.dll` is present in the output. M2 WP5 verified the real windowed runtime and its AOT demo
  (`--windowed --ticks 120`, and a 5,000,000-tick live-window run) with the same zero-warning
  publish.
- No runtime shader compilation: SPIR-V is built offline.
- The headless server must not initialize SDL or Vulkan; `WithHeadless(...)` must not load or
  call the graphics/windowing paths.

These are validation items, not assumptions. If a Silk.NET path uses reflection or dynamic code
incompatibly with AOT, isolate it behind an adapter and record the workaround.

## 7. Testing and CI

1. `dotnet build` with AOT/trim analyzers and warnings-as-errors, Debug and Release.
2. `dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64`
   with zero AOT/trim warnings; `SDL2.dll` is copied next to the executable.
3. Run the AOT-published headless smoke locally (no GPU): default logging, one
   `Setup`/`Tick`/`Teardown`, root cleared, generated-shape round trip, exit `0`.
4. Run the AOT-published windowed demo locally on a desktop session:
   `Bork2d.AotSmoke.exe --windowed --ticks 120` must exit `0` after exactly 120 ticks with one
   `Setup` and one `Teardown`; the interactive `--windowed` run observes the visible window and
   exits on Escape/close.
5. Run a windowed/offscreen AOT-published app on a GPU machine before release (M8+).
6. A smoke test that no loaded asset type lacks a generated shape (M3/M4 adds the loader; M1
   covers the policy with `MessagePackShapeTests`).
7. The AOT app logs through the default ZLogger/Microsoft.Extensions.Logging path without trim
   warnings; `IEngine.LoggerFactory` is usable.

CI scope is exactly: pull requests targeting `develop` and pushes to `develop`; it restores,
verifies formatting, builds Debug, and runs Debug tests. There is **no AOT job** in CI, so the
publish/run gate above is mandatory locally.

## 8. Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Silk.NET or a transitive dependency uses reflection/dynamic code | AOT publish or runtime failure | Validate in M2/M6; isolate behind adapters; pin versions. |
| MessagePack source generation misses a custom type | Runtime load failure | Build-time validation and registration checks; tests. |
| TOML/reflection leaks into runtime | AOT warnings or failure | Project boundary, analyzers, TOML only in baker/editor. |
| Native SDL2/Vulkan deployment | `DllNotFound` at runtime | RID-specific packaging and clean-machine tests. |
| Headless pulls in SDL/Vulkan initialization | Unwanted window/GPU dependency | `WithHeadless` never initializes graphics. |

## 9. Related documents

- [Projects and dependencies](projects.md#5-build-packaging-and-versioning) — project graph and
  build settings.
- [Assets and tooling](assets-and-tooling.md) — pack format, descriptors, sprites, bake.
- [Logging](logging.md) — ZLogger/MEL AOT notes.
- [Decision log](../decisions.md) — AOT and serialization decisions.
- [Roadmap](../roadmap.md) — M1 baseline and status.
