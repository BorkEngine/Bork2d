# Projects and Dependencies

> **Status:** the project graph is `Current` and enforced by the dependency test. M1, M2, and M3
> are implemented and merged into `develop` (2026-10-26, PR #4), and the post-M3 asset-reference
> refactor (D-035) is merged (PR #6). M4 WP1–WP4 are implemented on `m-4/common` (not yet merged to
> `develop`): `Bork2d.Bake` now contains the descriptor, diagnostics, path safety, host/incremental
> contracts, XXH3 hashing, a `MetadataLoadContext`-based host generator, the non-collectible host
> loader, source discovery/binding, the generic shape-driven Tomlyn DOM binder, the texture pipeline,
> the baked asset index and reference validation, pack composition, and the atomic pack writer
> (internal, with `InternalsVisibleTo` to the host/CLI/editor/tests); `Bork2d.Bake.Cli` exposes
> `--help`, `--version`, `generate-host`, `bake` (full pipeline, `--check`, `--mode`, `--output`),
> and `validate` with or without `--host` (internal `Program`/`CliApplication`). `Bork2d.BakeFixture`
> is the real `Project.Common`-like fixture for the bake/load tests (WP5 adds its committed
> `Assets/` tree and targets, WP6 the AOT baked-pack proof). Incremental/MSBuild/AOT integration
> (WP5/WP6) is not implemented. `Bork2d.Runtime` hosts the SDL
> window, input, time, the windowed loop, and the app-scoped asset service. `Bork2d.Assets` provides
> the v1 pack reader/writer, its public tooling surface
> (`Bork2d.Assets.Pack.PackBuilder`/`PackCompressionMode`), canonical paths, the internal
> base/overlay VFS with `LoadPack`/`Browse`/`Exists`, the loader service (one dedicated thread,
> unbounded FIFO queue, `LoadData`/`LoadAsset<T>`, cancellation/join, startup/stop logging), and
> generic pack-time reference validation. `Bork2d.Api` exposes `AssetInfo`, the shape-only
> `LoadAsset<T>` (D-M4-11), `AssetException`/`AssetErrorKind`, the public
> `SpriteAsset`/`TextureDescriptor`/`AssetRef<T>`/`DataRef` asset model, and the `ColorMarshaler`
> serialization infrastructure (D-M4-12). `Bork2d.AotSmoke` references `Bork2d.Assets` for the AOT
> pack proof. `Bork2d.Render` is still reference-wired with no implementation (`Planned`);
> `Bork2d.Editor` is `Planned` (M5).

## 1. Current projects

All projects target `net11.0` and live in `src/Bork2d/`, listed in
[`Bork2d.slnx`](../../src/Bork2d/Bork2d.slnx).

| Project | References | Status | Responsibility |
| --- | --- | --- | --- |
| `Bork2d.Api` | none (no project references) | `Current` | Public contracts and value types, including `AssetInfo`, the shape-only `LoadAsset<T>`, the public asset model (`SpriteAsset`, `TextureDescriptor`, `SpriteTopology`, `AssetRef<T>`, `DataRef`), the `ColorMarshaler`/`AssetRefMarshaler<TAsset>`/`DataRefMarshaler` serialization infrastructure, and the `AssetException`/`AssetErrorKind` error contract. |
| `Bork2d.Common` | `Bork2d.Api` | `Partial` | Root-owned unordered scene tree, transforms, time, layer validation, MessagePack generated-shape policy. Snapshot capture is M7. |
| `Bork2d.Runtime` | `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render` | `Current` (M3) | Public builder/app, validation, windowed SDL host (window, input, time, uncapped loop), one-tick headless/offscreen host, app-owned logging, and the app-scoped asset-service lifecycle. GPU/rendering is M6/M8. |
| `Bork2d.Assets` | `Bork2d.Api`, `Bork2d.Common` | `Current` (M3/D-035) | v1 pack reader/writer, structural validation, Zstd/XXH3-64, canonical paths, public `Bork2d.Assets.Pack.PackBuilder`/`PackCompressionMode`, the internal base/overlay VFS with `LoadPack`/`Browse`/`Exists`, the app-scoped service (one thread, FIFO queue, `LoadData`/`LoadAsset<T>`, cancellation/join, `AssetException` failures), and generic pack-time reference validation. Grants `Bork2d.Bake` internal access to the canonical path grammar. |
| `Bork2d.Bake` | `Bork2d.Api`, `Bork2d.Assets` (+ Tomlyn, PolyType, `System.Reflection.MetadataLoadContext`, StbImageSharp, `System.IO.Hashing`) | `Partial` (M4 WP4) | JIT bake library, internal by default: project-descriptor model/parser, diagnostics, path/traversal safety, host and incremental contracts, XXH3-128 hashing, the shared canonical path grammar reuse, `Project.Common` metadata resolution (Q-04), the generated-host project/manifest writer, the non-collectible host loader with shared tooling type identity, source discovery/binding, the generic shape-driven Tomlyn DOM binder (Q-07), the texture pipeline (WP3), the baked asset index and §9 reference validation (BK3xxx), pack composition through `PackBuilder`, the atomic pack write, and the full/check bake pipeline. No `Bork2d.Common` reference (Q-20); the incremental manifest/MSBuild integration is WP5. |
| `Bork2d.Bake.Cli` | `Bork2d.Bake` | `Partial` (M4 WP4) | JIT console (`OutputType=Exe`, `AssemblyName=Bork2d.Bake.Cli`, `IsPackable=false`), internal `Program`/`CliApplication`: `--help`, `--version`, `generate-host`, `bake` (full pipeline, `--check`, `--mode debug|release`, `--output`; `--force` is a no-op until WP5), and `validate` (descriptor/texture/TOML-syntax without a host; the full `bake --check` validation with `--host`). |
| `Bork2d.BakeFixture` | `Bork2d.Api` | `Partial` (M4 WP4; Q-16) | Minimal `Project.Common`-like fixture library with public `[GenerateShape] partial` data types (`GameConfig`, `Stats`, `StatsRoot`) and `IsAotCompatible=true`. Referenced by `Bork2d.Tests` (full bake/load and host-reference tests); WP5 adds its committed `Assets/` tree and targets, WP6 the AOT baked-pack proof. It never references the bake tooling. |
| `Bork2d.Render` | `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets` | `Empty` (wired) | Vulkan device, swapchain or offscreen targets, pipelines, bindless textures, draws, present. |
| `Bork2d.AotSmoke` | `Bork2d.Api`, `Bork2d.Assets`, `Bork2d.Common`, `Bork2d.Runtime` | `Current` | NativeAOT smoke executable: the default headless one-tick run with a temporary pack, plus the opt-in `--windowed [--ticks N]` SDL demo; the local publish/run gate. Not a game template project and it never references bake tooling. |
| `Bork2d.Tests` | every solution engine project (`Api`, `Common`, `Assets`, `Render`, `Runtime`, `Bake`, `Bake.Cli`, `BakeFixture`) plus `Bork2d.AotSmoke` | `Current` | 829 deterministic xUnit tests plus 12 opt-in/conditional tests (9 real-window and 3 reparse-point capability), including pack-format/writer, VFS/path/browse, loader/threading, generic reference validation, asset-reference/pack-build contracts, asset error contract, runtime asset lifecycle/error-matrix, D-M4-11 struct roots, D-M4-12 color marshaling, descriptor/diagnostic/path/hash/CLI foundations, the generated-host generator/loader/ALC identity, the generic shape-driven TOML binder matrix (leaves, structs, collections, attributes, spans, enum/date/numeric rules), source discovery/binding, `SpriteAsset` as data with a pack round-trip, the texture pipeline, the baked asset index and BK3xxx reference rules, pack composition/atomic replacement, the full bake → real `AssetService` load acceptance path (including a struct root and its `AssetRef<StatsRoot>`), structural determinism in-process and across separate CLI processes, and dependency-surface tests. The smoke reference copies the app next to the tests; the dependency test loads it by name to check its references. |
| `Bork2d.Editor` | `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Bake` (+ Avalonia) | `Planned` (M5) | Avalonia editor library/app; JIT; reuses the bake library (D-M4-01). |

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
`Bork2d.Bake` is JIT tooling and pins `Tomlyn` `2.10.1` (descriptor/source TOML), `PolyType` `1.4.1`
(direct: the binder walks compiled shape metadata), `System.Reflection.MetadataLoadContext`
`11.0.0-rc.1.26425.128` (build-time `Project.Common` metadata without loading game assemblies),
`StbImageSharp` `2.30.16` (texture decode, used by WP3), and `System.IO.Hashing`
`11.0.0-rc.1.26425.128` (`XxHash128`); it references `Bork2d.Api`/`Bork2d.Assets` and never
`Bork2d.Common` (Q-20). The generated `Bork2d.BakeHost` project is not a solution project and has no
`PackageReference`: it references the exact assemblies by absolute `HintPath` (`Project.Common`, the
Q-04 closure, `Bork2d.Api`/`Assets`/`Bake`, Tomlyn, PolyType, Nerdbank.MessagePack, and
`System.IO.Hashing`). `Bork2d.Bake.Cli` references only `Bork2d.Bake`, is an `Exe`, and is not
packable. `Bork2d.BakeFixture` is the `Project.Common`-like fixture: it references only
`Bork2d.Api` (PolyType flows transitively from that reference), sets `IsAotCompatible`, and has no
TOML/bake reference; `Bork2d.Tests` references it for the full bake/load and host-reference tests
(Q-16). `Bork2d.AotSmoke` is the only project that sets `PublishAot`; libraries do not publish
independently, and no runtime project references the bake tooling or its packages.

## 2. Target dependency graph

The graph is acyclic and flows contracts → implementation → composition. `Runtime` is the
composition root and the only project that knows all the others.

| From \ To | Api | Common | Assets | Render | Runtime | Bake | Bake.Cli | BakeFixture | Editor |
| --- | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: | :-: |
| **Api** | — | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| **Common** | ✓ | — | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| **Assets** | ✓ | ✓ | — | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| **Render** | ✓ | ✓ | ✓ | — | ✗ | ✗ | ✗ | ✗ | ✗ |
| **Runtime** | ✓ | ✓ | ✓ | ✓ | — | ✗ | ✗ | ✗ | ✗ |
| **AotSmoke** | ✓ | ✓ | ✓ | ✗ | ✓ | ✗ | ✗ | ✗ | ✗ |
| **Bake** | ✓ | ✗ | ✓ | ✗ | ✗ | — | ✗ | ✗ | ✗ |
| **Bake.Cli** | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | — | ✗ | ✗ |
| **BakeFixture** | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | — | ✗ |
| **Editor** | ✓ | ✓ | ✓ | ✗ | ✗ | ✓ | ✗ | ✗ | — |

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
- `AotSmoke` uses `Runtime` for the host, `Api`/`Common` for the scene graph, and the public
  `Bork2d.Assets.Pack` tooling surface (`PackBuilder`) to build and load the AOT pack proof; it must not
  pull `Render` types or any bake tooling in.
- `Bake` is JIT tooling: it references `Api` and `Assets` only (no `Common`, Q-20) plus Tomlyn,
  PolyType, `System.Reflection.MetadataLoadContext`, StbImageSharp, and `System.IO.Hashing`; it must
  not reference `Runtime`, `Render`, or `Editor`.
  Its public surface is empty; `InternalsVisibleTo` grants the host, CLI, editor, and tests access.
- `Bake.Cli` references only `Bake`; it is the JIT console front end and its public surface is empty.
- `BakeFixture` is a game-side fixture: it references `Bork2d.Api` only, is AOT-compatible, and is
  never in the runtime graph. Tests reference it; the generated bake host references it as the
  common assembly (its real assembly name, not a hardcoded `Project.Common`).
- `Editor` is independent of the renderer: it edits source data, slices textures, and previews
  with Avalonia imaging. It is JIT and not shipped with the game; it reuses `Bork2d.Bake` (M5,
  D-M4-01).
- Tests may reference everything and may use internals.

A new arrow requires updating this document and the decision log; the dependency test fails
otherwise.

## 3. Public surface and assembly visibility

### Public assemblies

| Assembly | Public types |
| --- | --- |
| `Bork2d.Api` | `IEngine<TLayer>`, `IGame<TLayer>`, `ITime`, `IWindow`, `IInput`, `IAssets`, `ISceneRoot`, `ITransform`, `ICamera<TLayer>`, `ISpriteRender<TLayer>`, `Color`, `ClearKind`, `KeyCode`, `MouseButton`, `AssetInfo`, `AssetRef<TAsset>`, `DataRef`, `SpriteAsset`, `TextureDescriptor`, `SpriteTopology`, `AssetException`, `AssetErrorKind`, `Bork2dException`, plus the `ColorMarshaler`/`AssetRefMarshaler<TAsset>`/`DataRefMarshaler` serialization infrastructure. The types are grouped by area namespace (§4). `IAssets.LoadAsset<T>` carries the shape-only `where T : IShapeable<T>` constraint (D-M4-11). `Bork2d.Api` grants `Bork2d.Assets` internal access to the non-public reference marker used by pack-time traversal (D-035). |
| `Bork2d.Runtime` | `Bork2dAppBuilder<TLayer>`, `Bork2dApp`. Nothing else; options, runtime state, and implementation types stay internal. |
| `Bork2d.Assets` | **Tooling only:** `Bork2d.Assets.Pack.PackBuilder`, `Bork2d.Assets.Pack.PackCompressionMode`. Every implementation type (pack reader/archive, header schema, paths, Zstd helpers, the internal loader service, reference collector, and validation) stays internal. Game code must not reference this assembly; it builds packs in tooling/tests and the runtime consumes them. |
| `Bork2d.Bake` | **None.** The bake library is internal (`Bork2d.Bake.*`), with `InternalsVisibleTo` to `Bork2d.BakeHost`, `Bork2d.Bake.Cli`, `Bork2d.Editor`, and `Bork2d.Tests` (Q-02). There is no public bake API; M5 consumers use the internals through the same grants. |
| `Bork2d.Bake.Cli` | **None.** `Program` and `CliApplication` are internal; the executable is a tooling entry point, not a game or public API surface. |
| `Bork2d.BakeFixture` | `Bork2d.BakeFixture.GameConfig`, `Bork2d.BakeFixture.Stats`, `Bork2d.BakeFixture.StatsRoot` (fixture-only test data, Q-16). Public because the generated host must use them without `InternalsVisibleTo`; the library is never shipped with a game. |
| `Bork2d.Editor` | `Bork2dEditorBuilder`, `Bork2dEditorApp`, and the public editor service types needed by `Project.Editor` (`Planned`, M5). |

`ILoggerFactory` is a Microsoft.Extensions.Logging type exposed by `IEngine`; it is not a Bork2d
public type. `Bork2d.Api` takes it from `Microsoft.Extensions.Logging.Abstractions` in the
.NET 11 shared framework.

`Bork2d.Assets`' tooling types are the one exception to the internal-project rule (D-001,
D-M3-04): they exist for pack creation, not for gameplay. The dependency test asserts that the
assembly exports exactly those two types. `Bork2d.AotSmoke` references `Assets` only for that
public tooling surface in the AOT pack proof.

`Bork2d.Bake` and `Bork2d.Bake.Cli` are fully internal tooling assemblies (Q-02); the dependency
test asserts that each exports zero public types. The generated `Bork2d.BakeHost` assembly is not a
solution project and receives internals through the fixed-name `InternalsVisibleTo`.

`Bork2d.BakeFixture` is a game-side test fixture, not an engine library: its three public data types
exist only so the generated host and the bake/load tests can use the same `Project.Common`-like
assembly without `InternalsVisibleTo` (Q-16). The dependency test asserts its public surface and that
it references `Bork2d.Api` only.

### Internal assemblies

`Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render`, `Bork2d.Bake`, and `Bork2d.Bake.Cli` are
implementation details. Types are `internal` by default and exposed only to the assemblies that need
them. The only exception is the tooling-only public surface listed above:

| Project | Internals visible to |
| --- | --- |
| `Bork2d.Common` | `Bork2d.Runtime`, `Bork2d.Render`, `Bork2d.Assets`, `Bork2d.Editor`, `Bork2d.Tests`, `Bork2d.AotSmoke` |
| `Bork2d.Assets` | `Bork2d.Runtime`, `Bork2d.Render`, `Bork2d.Bake`, `Bork2d.Editor`, `Bork2d.Tests` |
| `Bork2d.Bake` | `Bork2d.BakeHost`, `Bork2d.Bake.Cli`, `Bork2d.Editor`, `Bork2d.Tests` |
| `Bork2d.Bake.Cli` | `Bork2d.Tests` |
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
| `Bork2d.Bake` | `Diagnostics/`, `Project/`, `Paths/`, `Toml/`, `Hosting/`, `Sources/`, `Textures/`, `Baking/`, `Incremental/` | `AssemblyReferenceAnchor` |
| `Bork2d.Bake.Cli` | — | `Program`, `CliApplication` |
| `Bork2d.BakeFixture` | — | `GameConfig`, `Stats`, `StatsRoot` |
| `Bork2d.Runtime` | `Hosting/`, `Input/`, `Sdl/`, `Window/` | `Bork2dApp`, `Bork2dAppBuilder`, `AssemblyReferenceAnchor` |
| `Bork2d.Tests` | `Architecture/`, `Scene/`, `Runtime/`, `Assets/{Pack,Vfs,Service,Sprites}/`, `Bake/{Baking,Cli,Diagnostics,Hosting,Incremental,Paths,Project,Toml,Sources,Textures}/`, `Platform/{Sdl,Window}/`, `Input/`, `TestDoubles/`, `TestSupport/` | none |
| `Bork2d.AotSmoke` | `Smoke/`, `Demo/` | `Program`, `DemoArguments` |
| `Bork2d.Render` | — | `AssemblyReferenceAnchor` (renderer folders arrive with M6/M8) |

`Bork2d.Api` keeps `IEngine`, `IGame`, and `Bork2dException` in the root namespace because they are
the two entry contracts and the shared exception. The rest of the public contract is grouped by
area:

| Namespace | Public types |
| --- | --- |
| `Bork2d.Api` | `IEngine<TLayer>`, `IGame<TLayer>`, `Bork2dException` |
| `Bork2d.Api.Assets` | `IAssets`, `AssetInfo`, `AssetException`, `AssetErrorKind`, `AssetRef<TAsset>`, `DataRef`, `SpriteAsset`, `TextureDescriptor`, `SpriteTopology` |
| `Bork2d.Api.Scene` | `ISceneRoot`, `ITransform`, `ICamera<TLayer>`, `ISpriteRender<TLayer>` |
| `Bork2d.Api.Rendering` | `Color`, `ClearKind` |
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
      Game/                      game code: IGame implementation, systems, data types
      Assets/                    textures, TOML data, sprite metadata
    Project.Desktop/
      Project.Desktop.csproj     -> Bork2d.Runtime, Project.Common
      Program.cs                 builder startup
    Project.Editor/
      Project.Editor.csproj      -> Bork2d.Editor, Project.Common
      Program.cs                 editor host (thin)
  bork2d.project.toml            project descriptor (M4 tooling; bake pipeline WP2+)
```

| Project | May reference | Must not reference |
| --- | --- | --- |
| `Project.Common` | `Bork2d.Api` | `Bork2d.Runtime`, `Bork2d.Common`, `Bork2d.Render`, `Bork2d.Assets`, bake tooling |
| `Project.Desktop` | `Bork2d.Runtime`, `Project.Common` | `Bork2d.Render`, `Bork2d.Common`, `Bork2d.Assets`, bake tooling |
| `Project.Editor` | `Bork2d.Editor`, `Project.Common` | `Bork2d.Render`, `Bork2d.Common`, `Bork2d.Assets` |

A developer therefore writes against exactly two engine assemblies: `Bork2d.Api` in
`Project.Common`, and `Bork2d.Runtime` in `Project.Desktop`. `Bork2d.Editor` is distributed by the
engine; the game's `Project.Editor` is a thin host that adds game-specific types and windows
through the editor builder. The generated solution references `Project.Common` directly and
registers editable types at compile time; assembly scanning is not used. A mapped data type may
live in an assembly that `Project.Common` references: resolution searches `Project.Common`, its
referenced assemblies, then `Bork2d.Api` (Q-04). The project descriptor format is implemented by
the M4 tooling ([assets and tooling §10](assets-and-tooling.md#10-toml-source-model)).

## 6. Build, packaging, and versioning

- `Directory.Build.props` sets `net11.0`, nullable, implicit usings, warnings-as-errors,
  `EnforceCodeStyleInBuild`, deterministic builds, and .NET analyzers for every project.
  `.editorconfig` and `.gitattributes` apply as well.
- The SDK is `11.0.100-rc.1`, pinned by `global.json` with `rollForward: latestPatch` and
  `allowPrerelease: true`.
- `Api`, `Common`, `Assets`, `Render`, `Runtime`, and `BakeFixture` set `IsAotCompatible`,
  `EnableTrimAnalyzer`, and `EnableAotAnalyzer`; they do not set `PublishAot`.
  `Bork2d.AotSmoke` sets `PublishAot` and `InvariantGlobalization`. `Bork2d.Bake` and
  `Bork2d.Bake.Cli` are JIT tooling and set neither AOT-compatibility nor `PublishAot`; they are
  never in the AOT publish graph.
- Build: `dotnet build src/Bork2d/Bork2d.slnx`. Local AOT gate:
  `dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64`.
  AOT publish/run is a local gate, not a CI job; the publish output must contain no
  `Bork2d.Bake*`, Tomlyn, or StbImageSharp assemblies. See
  [NativeAOT](nativeaot.md#7-testing-and-ci).
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
- [NativeAOT](nativeaot.md) — runtime build constraints.
- [Roadmap](../roadmap.md) — milestone sequence and progress.
