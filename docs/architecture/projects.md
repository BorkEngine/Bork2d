# Projects and Dependencies

> **Status:** `Current`, implemented as-built on `task/file-processors` (2026-10-27; D-036/D-037/
> D-038/D-039/D-040) and enforced by the dependency test. M1–M3 and M4 are merged into `develop`; the
> post-M3 asset-reference refactor (D-035) and the M4 serialization/bake work are merged.
> **D-036/D-037/D-038 are implemented:** the public JIT `Bork2d.Bake.Builder` hosts the
> builder/processor contract over the internal `Bork2d.Bake`; `Bork2d.BakeFixture.Assets` is the
> build-only fixture executable; `Bork2d.Bake.Cli`, the project descriptor, the generated host, and
> `MetadataLoadContext` are removed. The game-owned `MyGame.Assets` executable owns assets,
> processors, and builder configuration, registers data types explicitly, and bakes on build.
> `Bork2d.AotSmoke` builds the fixture assets executable out of process and loads the baked packs
> (8 main / 9 merged entries); no tooling assembly enters its publish. The dependency matrix and rows
> below are the current graph.
>
> `Bork2d.Runtime` hosts the SDL window, input, time, the windowed loop, and the app-scoped asset
> service. `Bork2d.Assets` provides the v1 pack reader/writer, its public tooling surface
> (`Bork2d.Assets.Pack.PackBuilder`/`PackCompressionMode`), canonical paths, the internal base/overlay
> VFS with `LoadPack`/`Browse`/`Exists`, the loader service, and generic pack-time reference
> validation. `Bork2d.Api` exposes the public contracts and the
> `SpriteAsset`/`TextureDescriptor`/`AssetRef<T>`/`DataRef` asset model. `Bork2d.Render` is still
> reference-wired with no implementation (`Planned`); `Bork2d.Editor` is `Planned` (M5) and consumes
> the public `Bork2d.Bake.Builder`.

## 1. Current projects

All projects target `net11.0` and live in `src/Bork2d/`, listed in
[`Bork2d.slnx`](../../src/Bork2d/Bork2d.slnx).

| Project | References | Status | Responsibility |
| --- | --- | --- | --- |
| `Bork2d.Api` | none (no project references) | `Current` | Public contracts and value types, including `AssetInfo`, the shape-only `LoadAsset<T>`, the public asset model (`SpriteAsset`, `TextureDescriptor`, `TextureFormat`, `TextureFilter`, `TextureWrap`, `SpriteTopology`, `AssetRef<T>`, `DataRef`), the `ColorMarshaler`/`AssetRefMarshaler<TAsset>`/`DataRefMarshaler` serialization infrastructure, and the `AssetException`/`AssetErrorKind` error contract. |
| `Bork2d.Common` | `Bork2d.Api` | `Partial` | Root-owned unordered scene tree, transforms, time, layer validation, MessagePack generated-shape policy. Snapshot capture is M7. |
| `Bork2d.Runtime` | `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render` | `Current` (M3) | Public builder/app, validation, windowed SDL host (window, input, time, uncapped loop), one-tick headless/offscreen host, app-owned logging, and the app-scoped asset-service lifecycle. GPU/rendering is M6/M8. |
| `Bork2d.Assets` | `Bork2d.Api`, `Bork2d.Common` | `Current` (M3/D-035) | v1 pack reader/writer, structural validation, Zstd/XXH3-64, canonical paths, public `Bork2d.Assets.Pack.PackBuilder`/`PackCompressionMode`, the internal base/overlay VFS with `LoadPack`/`Browse`/`Exists`, the app-scoped service (one thread, FIFO queue, `LoadData`/`LoadAsset<T>`, cancellation/join, `AssetException` failures), and generic pack-time reference validation. Grants `Bork2d.Bake` internal access to the canonical path grammar. |
| `Bork2d.Bake` | `Bork2d.Api`, `Bork2d.Assets` (+ Tomlyn, PolyType, StbImageSharp, `System.IO.Hashing`) | `Current` (D-036/D-037/D-038; internal) | JIT bake library, **internal and descriptor/host-free**: shape-driven Tomlyn binder, explicit type-registration support, processor registry/discovery, sidecar policies/default writer, built-in texture processor, baked-index/reference validation, pack composition, atomic pack writer, schema-3 incremental manifest/planner, and the builder implementation. No `Hosting/`, `Project/`, `MetadataLoadContext`, generated host, host state, or `Bork2d.Bake.Cli`. Grants internals to `Bork2d.Bake.Builder`, `Bork2d.Editor`, and `Bork2d.Tests`. No `Bork2d.Common` reference (Q-20). |
| `Bork2d.Bake.Builder` | `Bork2d.Bake`, `Bork2d.Api`, `Bork2d.Assets` (implementation reference; public surface hides it) | `Current` (D-036/D-037/D-038; public) | **Public JIT tooling surface** for game assets executables: `BakeApplicationBuilder`/`BakeApplication` configuration (packs/roots, texture defaults), `RegisterAsset<T>()`, `UseDefaultAssets()`, `UseDefaultProcessors()`, `IAssetProcessor`/`AssetProcessor<TSidecar>`, `None`/`Required` sidecar policy with `CreateDefaultSidecar`, output sink, processor diagnostics, and the command surface (`bake`/`validate`/`clean`/`copy-packs`). There is no common-assembly option; each registration carries its assembly. No generated host, no reflection, no Tomlyn/Stb types in its public surface. |
| `Bork2d.BakeFixture` | `Bork2d.Api` | `Current` (D-037; AOT-compatible) | Minimal `Project.Common`-like fixture library with public `[GenerateShape] partial` data types (`GameConfig`, `Stats`, `StatsRoot`, `Unit`), `IsAotCompatible=true`, and no TOML/bake reference. The fixture source trees and processors live in `Bork2d.BakeFixture.Assets`; this library stays tooling-free. |
| `Bork2d.BakeFixture.Assets` | `Bork2d.Bake.Builder`, `Bork2d.BakeFixture`, `Bork2d.Api` | `Current` (D-037; build-time only) | Build-time executable for the fixture: `Program.cs` configures packs (`Assets` → `assets.pck`, `DLC1` → `dlc1.pck`), texture defaults, explicit `RegisterAsset<T>()` types, and the PPM/Unit processors; building it bakes the fixture packs and writes a schema-3 manifest. It owns the fixture `Assets/`/`DLC1/` source trees (including `player.png`, `checker.ppm`, and `archer.unit`) and is referenced by tests normally and by the AOT smoke only as a build-only dependency. It is never in the runtime/AOT publish graph and has no generated host. |
| `Bork2d.Render` | `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets` | `Empty` (wired) | Vulkan device, swapchain or offscreen targets, pipelines, bindless textures, draws, present. |
| `Bork2d.AotSmoke` | `Bork2d.Api`, `Bork2d.BakeFixture`, `Bork2d.Common`, `Bork2d.Runtime` | `Current` (D-036/D-037) | NativeAOT smoke executable: the default headless one-tick run mounts the baked main pack from `AppContext.BaseDirectory/assets.pck`, overlays `dlc1.pck` with `LoadPack`, and asserts the custom data, struct root, sprite, texture/PPM descriptors, raw payloads, `Unit`, the DLC `data/dlc`, and its DLC → main texture reference (8 main / 9 merged entries); the opt-in `--windowed [--ticks N]` SDL demo uses `WithoutAssets()`. It imports `build/Bork2d.Assets.Copy.targets`, which builds `Bork2d.BakeFixture.Assets` out of process (it is deliberately not a `ProjectReference`), bakes the packs, and copies them to `$(TargetDir)`/`$(PublishDir)`. It never references builder/processor assemblies in compile or publish and does not reference `Bork2d.Assets` directly (it reaches the publish transitively through `Bork2d.Runtime`). |
| `Bork2d.Tests` | every solution engine project (`Api`, `Common`, `Assets`, `Render`, `Runtime`, `Bake`, `Bake.Builder`, `BakeFixture`, `BakeFixture.Assets`) plus `Bork2d.AotSmoke` | `Current` | 946 deterministic xUnit tests plus 12 opt-in/conditional tests (958 total; 9 real-window and 3 reparse-point capability), including pack/VFS/loader/contract tests, the dependency-surface tests, the explicit builder/no-descriptor/no-host tests, explicit/default registration, processor registration/override, sidecar policies, the shape-driven TOML binder matrix, source discovery/binding, the texture/PPM/Unit processors, the baked index and BK3xxx reference rules, multi-pack scopes, pack composition/atomic replacement, the full bake → real `AssetService` load path, structural determinism across separate processes, schema-3 incremental skip/force/check/manifest/clean rules, the `dotnet build`/publish/clean MSBuild integration (including the second-build no-op), and the 8-main/9-merged fixture pack proof. The smoke reference copies the app next to the tests; the dependency test loads it by name to check its references. |
| `Bork2d.Editor` | `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Bake.Builder` (+ Avalonia) | `Planned` (M5) | Avalonia editor library/app; JIT; consumes the public `Bork2d.Bake.Builder` surface (D-037) so the editor does not take an internals grant. |

`Bork2d.Runtime` references `Microsoft.Extensions.Logging` `11.0.0-rc.1.26425.128` and `ZLogger`
`2.5.10`; it also pins `Silk.NET.SDL` `2.23.0` for the internal windowed platform. That package
carries `Ultz.Native.SDL` `2.32.10`; the win-x64 `SDL2.dll` it ships reports `SDL_GetVersion`
**2.30.8**, which is the runtime version M2 is verified against (the package label is not the
runtime version). No SDL type leaves `Bork2d.Runtime`, `Bork2d.Api` stays SDL-free, and the
runtime project-reference graph is unchanged. `Bork2d.Common` references `Nerdbank.MessagePack`
`1.4.95` (generated shapes only, no reflection resolver). `Bork2d.Assets` pins
`Nerdbank.MessagePack` `1.4.95`, `PolyType` `1.4.1` (the public
`PackBuilder.AddMessagePack<T>` shape constraint), and `System.IO.Hashing`
`11.0.0-rc.1.26425.128` (`XxHash3`); Zstd is the .NET 11 built-in
`System.IO.Compression` implementation, so there is no third-party codec package.
`Bork2d.Api` uses `Microsoft.Extensions.Logging.Abstractions` from the .NET 11 shared framework
and pins `PolyType` `1.4.1` for the `IAssets.LoadAsset<T>` `where T : IShapeable<T>`
constraint (D-M3-22, relaxed by D-M4-11). That contract reference is the only package in
`Bork2d.Api`; it stays free of Nerdbank.MessagePack, `System.IO.Hashing`, Zstd, and file I/O.
`Bork2d.Assets` also takes `ILoggerFactory`/`ILogger` from the shared-framework logging
abstractions for the `Bork2d.Assets` category; it adds no logging package reference.
`Bork2d.Bake` and `Bork2d.Bake.Builder` are JIT tooling. `Bork2d.Bake` pins `Tomlyn`
`2.10.1` (source/sidecar TOML), `PolyType` `1.4.1`
(direct: the binder walks compiled shape metadata), `StbImageSharp` `2.30.16` (texture decode),
and `System.IO.Hashing` `11.0.0-rc.1.26425.128` (`XxHash128`); it references
`Bork2d.Api`/`Bork2d.Assets` and never `Bork2d.Common` (Q-20). There is no
`System.Reflection.MetadataLoadContext` package and no generated `Bork2d.BakeHost` project.
`Bork2d.Bake.Builder` is a public JIT library that references `Bork2d.Bake`, `Bork2d.Api`, and
`Bork2d.Assets`, and exposes only the builder/processor/command contract. `Bork2d.BakeFixture` is
the `Project.Common`-like fixture: it references only `Bork2d.Api` (PolyType flows transitively
from that reference), sets `IsAotCompatible`, and has no TOML/bake reference. Its source trees and
processors live in the build-only `Bork2d.BakeFixture.Assets` executable, which references
`Bork2d.Bake.Builder`, `Bork2d.BakeFixture`, and `Bork2d.Api`; the data-type library stays
tooling-free. `Bork2d.Tests` references the fixture and the assets executable for full bake/load
tests; `Bork2d.AotSmoke` takes a build-only dependency on the assets executable for the baked-pack
AOT proof (Q-16). `Bork2d.AotSmoke` is the only project that sets `PublishAot`; libraries do not
publish independently, and no runtime project references the bake tooling or its packages. The build
targets are imported, not referenced: the assets executable is built and executed out of process, so
no `Bork2d.Bake`, `Bork2d.Bake.Builder`, `Bork2d.BakeFixture.Assets`, Tomlyn, or StbImageSharp
assembly enters the smoke app's output or publish graph (the game-side `Bork2d.BakeFixture` assembly
is expected there).

## 2. Target dependency graph

The graph is acyclic and flows contracts → implementation → composition. `Runtime` is the
composition root and the only project that knows all the others.

| From \ To | Api | Common | Assets | Render | Runtime | Bake | Bake.Builder | BakeFixture | BakeFixture.Assets | Editor |
| --- | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: |
| **Api** | — | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| **Common** | ✓ | — | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| **Assets** | ✓ | ✓ | — | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| **Render** | ✓ | ✓ | ✓ | — | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| **Runtime** | ✓ | ✓ | ✓ | ✓ | — | ✗ | ✗ | ✗ | ✗ | ✗ |
| **AotSmoke** | ✓ | ✓ | ✗ | ✗ | ✓ | ✗ | ✗ | ✓ | ✗ | ✗ |
| **Bake** | ✓ | ✗ | ✓ | ✗ | ✗ | — | ✗ | ✗ | ✗ | ✗ |
| **Bake.Builder** | ✓ | ✗ | ✓ | ✗ | ✗ | ✓ | — | ✗ | ✗ | ✗ |
| **BakeFixture** | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | — | ✗ | ✗ |
| **BakeFixture.Assets** | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | ✓ | — | ✗ |
| **Editor** | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ✓ | ✗ | ✗ | — |
| **Tests** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ |

Rules behind the matrix:

- `Api` references the BCL plus `Microsoft.Extensions.Logging.Abstractions` for `ILoggerFactory`
  and `PolyType` `1.4.1` for the `IShapeable<T>` constraint only; no Silk.NET, SDL, Vulkan,
  ZLogger, Avalonia, Nerdbank.MessagePack, `System.IO.Hashing`, Zstd, Tomlyn, StbImageSharp, or
  file I/O.
- `Common` is platform-agnostic: no SDL, Vulkan, or asset implementation; it consumes `IAssets`
  through the API interface.
- `Assets` implements `IAssets` and owns pack loading/serialization. It may use `Common` internals;
  it must not reference `Render` or bake tooling. It grants `Bork2d.Bake` internal access to the one
  canonical path grammar (`AssetPath`); Bake does not add a second grammar.
- `Render` consumes internal snapshot contracts and resolves public `SpriteAsset`/`TextureDescriptor`
  values at draw time. It must not reference `Runtime` or call SDL; window/surface lifetime is handed
  to it, offscreen targets are its own.
- `Runtime` is the composition root: builder, app host, SDL window/input (windowed), Vulkan
  bootstrap, mode selection, ZLogger configuration, service wiring.
- `AotSmoke` uses `Runtime` for the host, `Api`/`Common` for the scene graph, and `BakeFixture` as
  the game-side `Project.Common`. It imports `build/Bork2d.Assets.Copy.targets`, which builds
  `BakeFixture.Assets` out of process (no `ProjectReference`), bakes the fixture packs, and copies
  them next to the app/publish. Its default run loads those packs through the public `IAssets` API
  (8 main / 9 merged entries). It does not reference `Bork2d.Assets` directly (the assembly is in the
  publish transitively through `Runtime`), uses no pack tooling, and must not pull `Render` types or
  any bake tooling/assets-executable assembly in.
- `Bake` is JIT tooling: it references `Api` and `Assets` only (no `Common`, Q-20) plus Tomlyn,
  PolyType, StbImageSharp, and `System.IO.Hashing`; it must not reference `Runtime`, `Render`, or
  `Editor`. It has no project descriptor, `Hosting/`, `Project/`, `MetadataLoadContext`, or host
  state. Its public surface is empty; `InternalsVisibleTo` grants `Bake.Builder`, `Editor`, and
  `Tests` access.
- `Bake.Builder` is the public JIT tooling surface. It references `Bake` (implementation), `Api`, and
  `Assets`; its public types are the builder, `RegisterAsset<T>()`/`UseDefaultAssets()`/
  `UseDefaultProcessors()`, processor contracts, sidecar policy, output sink, and command result
  types only. It must not expose Tomlyn, StbImageSharp, `Bake` internals, or `PackBuilder` as the
  processor contract.
- `BakeFixture` is a game-side fixture: it references `Bork2d.Api` only, is AOT-compatible, and is
  never in the runtime graph. It owns the data types (`GameConfig`, `Stats`, `StatsRoot`, `Unit`);
  the source trees and processors live in `BakeFixture.Assets`.
- `BakeFixture.Assets` is a build-time executable: it references `Api`, `Bake.Builder`, and
  `BakeFixture`, owns the fixture source trees and PPM/Unit processors, and is never part of the
  runtime/AOT publish. Tests reference it normally to instantiate its internal processors; the AOT
  smoke builds it out of process.
- `Editor` is independent of the renderer: it edits source data, slices textures, and previews with
  Avalonia imaging. It is JIT and not shipped with the game; it consumes the public
  `Bake.Builder` surface (M5) rather than taking an internals grant.
- `Tests` may reference everything and may use internals. The dependency test asserts the runtime
  assemblies never reference the bake tooling or the assets executable, and that the fixture assets
  executable references only `Api`/`Bake.Builder`/`BakeFixture`.

A new arrow requires updating this document and the decision log; the dependency test fails
otherwise.

## 3. Public surface and assembly visibility

### Public assemblies

| Assembly | Public types |
| --- | --- |
| `Bork2d.Api` | `IEngine<TLayer>`, `IGame<TLayer>`, `ITime`, `IWindow`, `IInput`, `IAssets`, `ISceneRoot`, `ITransform`, `ICamera<TLayer>`, `ISpriteRender<TLayer>`, `Color`, `ClearKind`, `KeyCode`, `MouseButton`, `AssetInfo`, `AssetRef<TAsset>`, `DataRef`, `SpriteAsset`, `TextureDescriptor`, `SpriteTopology`, `TextureFormat`, `TextureFilter`, `TextureWrap`, `AssetException`, `AssetErrorKind`, `Bork2dException`, plus the `ColorMarshaler`/`AssetRefMarshaler<TAsset>`/`DataRefMarshaler` serialization infrastructure. The types are grouped by area namespace (§4). `IAssets.LoadAsset<T>` carries the shape-only `where T : IShapeable<T>` constraint (D-M4-11). `Bork2d.Api` grants `Bork2d.Assets` internal access to the non-public reference marker used by pack-time traversal (D-035). |
| `Bork2d.Runtime` | `Bork2dAppBuilder<TLayer>`, `Bork2dApp`. Nothing else; options, runtime state, and implementation types stay internal. |
| `Bork2d.Assets` | **Tooling only:** `Bork2d.Assets.Pack.PackBuilder`, `Bork2d.Assets.Pack.PackCompressionMode`. Every implementation type (pack reader/archive, header schema, paths, Zstd helpers, the internal loader service, reference collector, and validation) stays internal. Game code must not reference this assembly; it builds packs in tooling/tests and the runtime consumes them. |
| `Bork2d.Bake` | **None.** The bake library is internal (`Bork2d.Bake.*`), with `InternalsVisibleTo` to `Bork2d.Bake.Builder`, `Bork2d.Editor`, and `Bork2d.Tests` (Q-02). There is no host/CLI grant. |
| `Bork2d.Bake.Builder` | `BakeApplicationBuilder`, `BakeApplication`, `RegisterAsset<T>()`, `UseDefaultAssets()`, `UseDefaultProcessors()`, `IAssetProcessor`, `AssetProcessor<TSidecar>`, `AssetProcessorContext`, `IAssetOutputSink`, `IAssetProcessorDiagnostics`, `SidecarPolicy`, and the command surface. The implementation types (`Bake`, Tomlyn, Stb) stay hidden; no host type exists. The texture enums (`TextureFormat`/`TextureFilter`/`TextureWrap`) live in `Bork2d.Api.Assets`. |
| `Bork2d.BakeFixture` | `Bork2d.BakeFixture.GameConfig`, `Bork2d.BakeFixture.Stats`, `Bork2d.BakeFixture.StatsRoot`, and `Bork2d.BakeFixture.Unit` (fixture-only test data, Q-16/Q-35). Public because the bake/load and processor tests use them without `InternalsVisibleTo`; the library is never shipped with a game. |
| `Bork2d.BakeFixture.Assets` | **None.** `Program` and the PPM/Unit processors are internal (the processors are visible to `Bork2d.Tests` through `InternalsVisibleTo`); the executable is build-time tooling, not a public surface. |
| `Bork2d.Editor` | `Bork2dEditorBuilder`, `Bork2dEditorApp`, and the public editor service types needed by `Project.Editor` (`Planned`, M5). |

`ILoggerFactory` is a Microsoft.Extensions.Logging type exposed by `IEngine`; it is not a Bork2d
public type. `Bork2d.Api` takes it from `Microsoft.Extensions.Logging.Abstractions` in the
.NET 11 shared framework.

`Bork2d.Assets`' tooling types are the one exception to the internal-project rule (D-001,
D-M3-04): they exist for pack creation, not for gameplay. The dependency test asserts that the
assembly exports exactly those two types. `Bork2d.AotSmoke` no longer references `Assets` directly:
its default run loads the baked pack and consumes it through `IAssets` from `Bork2d.Api`; the
`Bork2d.Assets` assembly is still present in the publish because `Bork2d.Runtime` references it.

`Bork2d.Bake` is a fully internal tooling assembly (Q-02); the dependency test asserts that it
exports zero public types. There is no generated `Bork2d.BakeHost` assembly, host project, host ALC,
host manifest, or host-generation state. The incremental manifest/planner and MSBuild targets add no
public type; the model/planner/JSON stores live in the internal `Bork2d.Bake.Incremental` namespace
and the targets are build files that invoke the assets executable.

`Bork2d.Bake.Builder` is the **public** tooling assembly. Its surface is deliberately narrow:
builder/configuration, `RegisterAsset<T>()`/`UseDefaultAssets()`/`UseDefaultProcessors()`, processor
contract, sidecar policy, output sink, diagnostics, and command results. It may reference
`Bork2d.Bake`, `Bork2d.Api`, and `Bork2d.Assets`, but no Tomlyn, StbImageSharp, descriptor, or
`PackBuilder` type appears in its public signatures. The dependency test asserts that surface and
that `Bork2d.Bake` exports zero public types.

`Bork2d.BakeFixture` is a game-side test fixture, not an engine library: its public data types exist
only so the explicit `RegisterAsset<T>()` registrations and the bake/load tests can use the same
`Project.Common`-like assembly without `InternalsVisibleTo` (Q-16/Q-35). Its source trees live in the
separate build-only `Bork2d.BakeFixture.Assets` executable; the data-type library stays
`Bork2d.Api`-only and AOT-compatible and adds no source-tree public surface. The dependency test
asserts both surfaces, that `Bork2d.BakeFixture` references `Bork2d.Api` only, and that the assets
executable references only `Bork2d.Api`/`Bork2d.Bake.Builder`/`Bork2d.BakeFixture` and exports no
public type.

### Internal assemblies

`Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render`, `Bork2d.Bake`, and `Bork2d.BakeFixture.Assets`
are implementation details. Types are `internal` by default and exposed only to the assemblies that
need them. The only exception is the tooling-only public surface listed above:

| Project | Internals visible to |
| --- | --- |
| `Bork2d.Common` | `Bork2d.Runtime`, `Bork2d.Render`, `Bork2d.Assets`, `Bork2d.Editor`, `Bork2d.Tests`, `Bork2d.AotSmoke` |
| `Bork2d.Assets` | `Bork2d.Runtime`, `Bork2d.Render`, `Bork2d.Bake`, `Bork2d.Editor`, `Bork2d.Tests` |
| `Bork2d.Bake` | `Bork2d.Bake.Builder`, `Bork2d.Editor`, `Bork2d.Tests` |
| `Bork2d.BakeFixture.Assets` | `Bork2d.Tests` |
| `Bork2d.Render` | `Bork2d.Runtime`, `Bork2d.Tests` |
| `Bork2d.Runtime` | `Bork2d.Tests` |

Consequences:

- There is **no accidental public API** from implementation projects. A public type in `Common`
  is a bug unless it is a contract intentionally re-exported by `Runtime`.
- `Runtime` may reference internals directly; it does not create a parallel public model.
- Trimming/AOT compatibility is **not** deferred: `IsAotCompatible` and the trim/AOT analyzers
  are on from M1 (see [NativeAOT](nativeaot.md)).

## 4. Source layout and namespaces

Every project uses one folder per area and a namespace derived from the project and folder path.
Folder names are PascalCase and match the namespace segment exactly; a file in
`Bork2d.Assets/Loading/AssetService.cs` declares `namespace Bork2d.Assets.Loading;`. IDE0130 is an
error, so a misplaced file fails the build (D-034). Files at the project root declare the project
namespace; the public startup/tooling types stay there where the table notes it.

| Project | Folders (namespace) | Root-namespace files |
| --- | --- | --- |
| `Bork2d.Api` | `Assets/` → `.Assets`; `Scene/` → `.Scene`; `Rendering/` → `.Rendering`; `Input/` → `.Input`; `Platform/` → `.Platform` | `IEngine`, `IGame`, `Bork2dException` |
| `Bork2d.Common` | `Scene/`, `Time/`, `Serialization/` | none |
| `Bork2d.Assets` | `Pack/` (public tooling, the v1 pack reader/writer, and reference validation), `Vfs/`, `Loading/` | `CommonReferenceAnchor` |
| `Bork2d.Bake` | `Diagnostics/`, `Paths/`, `Toml/`, `Sources/`, `Textures/`, `Baking/`, `Incremental/`, `Processors/` | `AssemblyReferenceAnchor` |
| `Bork2d.Bake.Builder` | `Processors/`, `Commands/` | `BakeApplicationBuilder`, `BakeApplication`, processor contracts, sidecar policy, texture defaults |
| `Bork2d.BakeFixture` | — (data-type library) | `GameConfig`, `Stats`, `StatsRoot`, `Unit` |
| `Bork2d.BakeFixture.Assets` | `Processors/`; `Assets/` and `DLC1/` (source TOML, texture/processor sources and sidecars; build-time inputs, not namespaces) | `Program` (builder config) |
| `Bork2d.Runtime` | `Hosting/`, `Input/`, `Sdl/`, `Window/` | `Bork2dApp`, `Bork2dAppBuilder`, `AssemblyReferenceAnchor` |
| `Bork2d.Tests` | `Architecture/`, `Scene/`, `Runtime/`, `Assets/{Pack,Vfs,Service,Sprites}/`, `Bake/{Baking,Cli,Diagnostics,Incremental,MsBuild,Paths,Processors,Sources,Textures,Toml}/`, `Platform/{Sdl,Window}/`, `Input/`, `TestDoubles/`, `TestSupport/` | none |
| `Bork2d.AotSmoke` | `Smoke/`, `Demo/` | `Program`, `DemoArguments` |
| `Bork2d.Render` | — | `AssemblyReferenceAnchor` (renderer folders arrive with M6/M8) |

`Bork2d.Api` keeps `IEngine`, `IGame`, and `Bork2dException` in the root namespace because they are
the two entry contracts and the shared exception. The rest of the public contract is grouped by
area:

| Namespace | Public types |
| --- | --- |
| `Bork2d.Api` | `IEngine<TLayer>`, `IGame<TLayer>`, `Bork2dException` |
| `Bork2d.Api.Assets` | `IAssets`, `AssetInfo`, `AssetException`, `AssetErrorKind`, `AssetRef<TAsset>`, `DataRef`, `SpriteAsset`, `TextureDescriptor`, `SpriteTopology`, `TextureFormat`, `TextureFilter`, `TextureWrap`, `AssetRefMarshaler<TAsset>`, `DataRefMarshaler` |
| `Bork2d.Api.Scene` | `ISceneRoot`, `ITransform`, `ICamera<TLayer>`, `ISpriteRender<TLayer>` |
| `Bork2d.Api.Rendering` | `Color`, `ColorMarshaler`, `ClearKind` |
| `Bork2d.Api.Input` | `IInput`, `KeyCode`, `MouseButton` |
| `Bork2d.Api.Platform` | `IWindow`, `ITime` |

The tooling pack writer is `Bork2d.Assets.Pack.PackBuilder`/`PackCompressionMode`; it is still
tooling-only and not part of the game contract (D-001, D-M3-04, D-034). A namespace change is a
public-contract change: update this section, the API guide/reference, and the decision log in the
same change.

## 5. Game solution layout

A game generated for Bork2d uses three projects. The names below are the template's generic
names; a real game uses its own prefix (`MyGame.Common`, `MyGame.Desktop`, …).

```
MyGame/
  MyGame.sln
  src/
    Project.Common/
      Project.Common.csproj      -> Bork2d.Api
      Game/                      game code: IGame implementation, systems, runtime data types
    MyGame.Assets/
      MyGame.Assets.csproj       -> Bork2d.Bake.Builder, Project.Common; Exe;
                                    imports build/Bork2d.Assets.targets
      Assets/                    main pack root: data TOML, textures, processor sources
      DLC1/                      optional overlay pack root
      Program.cs                 BakeApplicationBuilder config + CLI entry point
    Project.Desktop/
      Project.Desktop.csproj     -> Bork2d.Runtime, Project.Common;
                                    build-only reference to MyGame.Assets; imports copy target
      Program.cs                 builder startup
    Project.Editor/
      Project.Editor.csproj      -> Bork2d.Editor, Project.Common
      Program.cs                 editor host (thin)
```

There is no `bork2d.project.toml` (D-037). `MyGame.Assets` configures packs, roots, output root,
texture defaults, processors, and explicit `RegisterAsset<T>()` types in `Program.cs`; each
registration carries its own assembly. Building `MyGame.Assets` runs its imported target, which invokes the executable's
`bake` command; the executable discovers and processes sources, binds data through the registered
type entries, and writes every configured pack. There is no generated host. `Project.Desktop` takes
a **build-only** reference to `MyGame.Assets` and imports a thin copy
target that asks the executable to copy packs to `$(TargetDir)` after `Build` and `$(PublishDir)`
after `Publish`; `Clean` delegates to the assets executable. MSBuild has no descriptor properties
and no pack-path list. `Project.Desktop` never references the builder or processor assemblies in
its compile or publish graph.

A `dotnet run` still resolves `WithAssetsPack("assets.pck")` against the process current directory
(Q-10); the host application passes an absolute path or sets its working directory, and mounts DLC
packs with `IAssets.LoadPack`. The M9 template owns the final ergonomics.

| Project | May reference | Must not reference |
| --- | --- | --- |
| `Project.Common` | `Bork2d.Api` | `Bork2d.Runtime`, `Bork2d.Common`, `Bork2d.Render`, `Bork2d.Assets`, `Bork2d.Bake.Builder` |
| `MyGame.Assets` | `Bork2d.Bake.Builder`, `Project.Common`, `Bork2d.Api` | `Bork2d.Runtime`, `Bork2d.Common`, `Bork2d.Render`, `Bork2d.Assets` (use the builder surface) |
| `Project.Desktop` | `Bork2d.Runtime`, `Project.Common`; build-only `MyGame.Assets` | `Bork2d.Render`, `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Bake.Builder`, `MyGame.Assets` (compile/publish) |
| `Project.Editor` | `Bork2d.Editor`, `Project.Common`; M5 may use `Bork2d.Bake.Builder` | `Bork2d.Render`, `Bork2d.Common`, `Bork2d.Assets` (direct) |

A developer's **runtime** code still writes against exactly two engine assemblies: `Bork2d.Api` in
`Project.Common`, and `Bork2d.Runtime` in `Project.Desktop`. The build-time `MyGame.Assets` project
adds `Bork2d.Bake.Builder`; it is JIT, build-only, and never shipped. `Bork2d.Editor` is distributed
by the engine; the game's `Project.Editor` is a thin host that adds game-specific types and windows
through the editor builder. The generated solution references `Project.Common` directly and
registers editable types at compile time; assembly scanning is not used. A data type may live in an
assembly that `Project.Common` references: each data TOML declares its type in `[bork2d]`, and
resolution is the builder's explicit `RegisterAsset<T>()` dictionary keyed by full type name; there
is no reflection or common-assembly lookup. The builder and processor model are owned by
[asset processors](asset-processors.md#3-project-model); the M5 editor consumes the same builder
configuration.

## 6. Build, packaging, and versioning

- `Directory.Build.props` sets `net11.0`, nullable, implicit usings, warnings-as-errors,
  `EnforceCodeStyleInBuild`, deterministic builds, and .NET analyzers for every project.
  `.editorconfig` and `.gitattributes` apply as well.
- The SDK is `11.0.100-rc.1`, pinned by `global.json` with `rollForward: latestPatch` and
  `allowPrerelease: true`.
- `Api`, `Common`, `Assets`, `Render`, `Runtime`, and `BakeFixture` (the data-type library) set
  `IsAotCompatible`, `EnableTrimAnalyzer`, and `EnableAotAnalyzer`; they do not set `PublishAot`.
  `Bork2d.AotSmoke` sets `PublishAot` and `InvariantGlobalization`.
  `Bork2d.Bake`, `Bork2d.Bake.Builder`, `Bork2d.BakeFixture.Assets`, and `MyGame.Assets` are JIT
  tooling/executables and set neither AOT-compatibility nor `PublishAot`; they are never in the AOT
  publish graph.
- Build: `dotnet build src/Bork2d/Bork2d.slnx`. The AOT smoke imports the copy target, which builds
  `Bork2d.BakeFixture.Assets` out of process; that build bakes the fixture packs (`assets.pck`,
  `dlc1.pck`) and copies them next to the smoke app/publish. A second unchanged build is an
  up-to-date no-op through the schema-3 manifest.
  Local AOT gate:
  `dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64`, then run
  the published exe (default headless: mounts the main pack, overlays the DLC pack, asserts 8 main /
  9 merged entries, and exits 0) and `--windowed --ticks 120` (exit 0 after exactly 120 ticks).
  AOT publish/run is a local gate, not a CI job; the publish output must contain no `Bork2d.Bake`,
  `Bork2d.Bake.Builder`, `Bork2d.BakeFixture.Assets`, Tomlyn, or StbImageSharp assembly
  (`Bork2d.BakeFixture` is the expected game-side common assembly) and must contain both baked packs
  and `SDL2.dll`. See [NativeAOT](nativeaot.md#7-testing-and-ci).
- CI restores, verifies formatting, builds Debug, and runs Debug tests on pull requests to
  `develop` and on pushes to `develop`.
- Distribution starts as source/project references; NuGet packaging is M9 (see roadmap). The game
  template's `Project.Desktop`/`Project.Common` take over the smoke app's role in M9.
- Versioning: pre-1.0 assemblies may break. A public API change updates the API guide, reference,
  and decision log in the same change.

## 7. Related documents

- [Architecture overview](overview.md) — invariants and context.
- [Runtime lifecycle](runtime-lifecycle.md) — modes, builder ownership, startup.
- [Assets and tooling](assets-and-tooling.md) — pack VFS, editor, and `Project.Editor`.
- [Asset processors and `MyGame.Assets`](asset-processors.md) — builder/processor model and
  `MyGame.Assets` project layout.
- [NativeAOT](nativeaot.md) — runtime build constraints.
- [Roadmap](../roadmap.md) — milestone sequence and progress.
