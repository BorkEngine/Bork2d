# Projects and Dependencies

> **Status:** the project graph is `Current` and enforced by the dependency test. M1, M2, and M3
> are implemented and merged into `develop` (2026-10-26, PR #4), and the post-M3 asset-reference
> refactor (D-035) is merged (PR #6). **M4 WP1–WP6 are merged into `develop` (2026-10-27, PR #7),
> including the D-M4-13 revision (per-source `[bork2d].type` and the committed fixture source PNG)
> and the D-M4-14/D-M4-15 multi-pack revision:** `Bork2d.Bake` contains the descriptor, diagnostics, path safety,
> host/incremental contracts, XXH3 hashing, a `MetadataLoadContext`-based host generator, the
> non-collectible host loader, source discovery/binding with per-source `[bork2d].type` metadata
> (D-M4-13), the generic shape-driven Tomlyn DOM binder,
> the texture pipeline, the baked asset index and reference validation, pack composition, the atomic
> pack writer, the incremental planner/manifest, and host-generation state (internal, with
> `InternalsVisibleTo` to the host/CLI/editor/tests); `Bork2d.Bake.Cli` exposes
> `--help`, `--version`, `generate-host`, `bake` (full pipeline or up-to-date skip, `--check`,
> `--mode`, `--output`, `--force`), and `validate` with or without `--host` (internal
> `Program`/`CliApplication`). `Bork2d.BakeFixture` is the real `Project.Common`-like fixture for the
> bake/load tests and the AOT proof, with a committed `Assets/` tree and descriptor that the MSBuild
> targets prepare and bake. `Bork2d.AotSmoke` imports
> [`build/Bork2d.Bake.targets`](../../build/Bork2d.Bake.targets), references the fixture, bakes the
> fixture pack into its output/publish, and its default headless run loads that pack from
> `AppContext.BaseDirectory` (D-M4-09/Q-22); it no longer uses the pack tooling or a temporary pack.
> `Bork2d.Runtime` hosts the SDL window, input, time, the windowed loop, and the app-scoped asset
> service. `Bork2d.Assets` provides the v1 pack reader/writer, its public tooling surface
> (`Bork2d.Assets.Pack.PackBuilder`/`PackCompressionMode`), canonical paths, the internal
> base/overlay VFS with `LoadPack`/`Browse`/`Exists`, the loader service (one dedicated thread,
> unbounded FIFO queue, `LoadData`/`LoadAsset<T>`, cancellation/join, startup/stop logging), and
> generic pack-time reference validation. `Bork2d.Api` exposes `AssetInfo`, the shape-only
> `LoadAsset<T>` (D-M4-11), `AssetException`/`AssetErrorKind`, the public
> `SpriteAsset`/`TextureDescriptor`/`AssetRef<T>`/`DataRef` asset model, and the `ColorMarshaler`
> serialization infrastructure (D-M4-12). `Bork2d.Render` is still reference-wired with no
> implementation (`Planned`); `Bork2d.Editor` is `Planned` (M5) and reuses the bake library.
>
> **Post-M4 target (D-036/D-037/D-038, Planned on `task/file-processors`).** Add a public JIT
> `Bork2d.Bake.Builder` project (builder + processor contract + explicit type registry) over the
> internal `Bork2d.Bake`, and a build-only `Bork2d.BakeFixture.Assets` executable. The game gains
> `MyGame.Assets`, an executable that owns assets/processors, configures the bake through
> `BakeApplicationBuilder`, registers data types explicitly, and bakes on build. The project
> descriptor and generated host are removed. `Bork2d.Bake.Cli` is retired as the game-facing CLI.
> The matrix and rows below are the current M4 graph; the target delta is in §2.1,
> [asset processors](asset-processors.md), and the
> [file-processors spec](../specs/file-processors.md).

## 1. Current projects

All projects target `net11.0` and live in `src/Bork2d/`, listed in
[`Bork2d.slnx`](../../src/Bork2d/Bork2d.slnx).

| Project | References | Status | Responsibility |
| --- | --- | --- | --- |
| `Bork2d.Api` | none (no project references) | `Current` | Public contracts and value types, including `AssetInfo`, the shape-only `LoadAsset<T>`, the public asset model (`SpriteAsset`, `TextureDescriptor`, `SpriteTopology`, `AssetRef<T>`, `DataRef`), the `ColorMarshaler`/`AssetRefMarshaler<TAsset>`/`DataRefMarshaler` serialization infrastructure, and the `AssetException`/`AssetErrorKind` error contract. |
| `Bork2d.Common` | `Bork2d.Api` | `Partial` | Root-owned unordered scene tree, transforms, time, layer validation, MessagePack generated-shape policy. Snapshot capture is M7. |
| `Bork2d.Runtime` | `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render` | `Current` (M3) | Public builder/app, validation, windowed SDL host (window, input, time, uncapped loop), one-tick headless/offscreen host, app-owned logging, and the app-scoped asset-service lifecycle. GPU/rendering is M6/M8. |
| `Bork2d.Assets` | `Bork2d.Api`, `Bork2d.Common` | `Current` (M3/D-035) | v1 pack reader/writer, structural validation, Zstd/XXH3-64, canonical paths, public `Bork2d.Assets.Pack.PackBuilder`/`PackCompressionMode`, the internal base/overlay VFS with `LoadPack`/`Browse`/`Exists`, the app-scoped service (one thread, FIFO queue, `LoadData`/`LoadAsset<T>`, cancellation/join, `AssetException` failures), and generic pack-time reference validation. Grants `Bork2d.Bake` internal access to the canonical path grammar. |
| `Bork2d.Bake` | `Bork2d.Api`, `Bork2d.Assets` (+ Tomlyn, PolyType, StbImageSharp, `System.IO.Hashing`) | `Current` (M4; D-M4-13/D-M4-14 revisions; target implementation behind `Bake.Builder`) | JIT bake library, **internal**: target is descriptor-free and host-free, with the shape-driven Tomlyn binder, explicit type-registration support, processor registry/discovery, built-in texture processor, baked-index/reference validation, pack composition, incremental manifest, and the builder implementation. Current M4 still has the project descriptor, generated-host generation/loading, `MetadataLoadContext`, per-pack binding, the texture pipeline, indexes/reference validation, pack composition, and the incremental planner. Target removes `Hosting/`, `Project/`, and the `MetadataLoadContext` package. No `Bork2d.Common` reference (Q-20). |
| `Bork2d.Bake.Builder` | `Bork2d.Bake`, `Bork2d.Api`, `Bork2d.Assets` (implementation reference; public surface hides it) | `Planned` (`task/file-processors`, D-036/D-037/D-038) | **Public JIT tooling surface** for game assets executables: `BakeApplicationBuilder`/`BakeApplication` configuration (packs/roots, output, texture defaults), `RegisterAsset<T>()`, `UseDefaultAssets()`, `UseDefaultProcessors()`, `IAssetProcessor`/`AssetProcessor<TSidecar>`, `None`/`Required` sidecar policy with `CreateDefaultSidecar`, output sink, processor diagnostics, and the command surface (`bake`, `validate`, `clean`, `copy-packs`). There is no common-assembly option; each registration carries its assembly. No generated host, no reflection, no Tomlyn/Stb types in its public surface. |
| `Bork2d.Bake.Cli` | `Bork2d.Bake` | `Current` (M4/D-M4-14); **retired for games by D-037/D-038** | JIT console used by the M4 targets/tests: `--help`, `--version`, `generate-host` (scans every root's source types; up-to-date skip, `--force`), `bake` (every pack, all-pack up-to-date skip, `--check`, `--mode debug|release`, `--output-root`, `--force`; `--output` removed), and `validate` (descriptor/all roots/source-type-metadata/TOML-syntax/textures without a host; the full `bake --check` validation with `--host`). Target: the public builder command surface in `MyGame.Assets`/fixture assets executables replaces it; no host. |
| `Bork2d.BakeFixture` | `Bork2d.Api` | `Current` (M4; Q-16/D-M4-14; committed source PNG); target keeps the data-type library | Minimal `Project.Common`-like fixture library with public `[GenerateShape] partial` data types (`GameConfig`, `Stats`, `StatsRoot`; target adds `Unit` for the processor test), `IsAotCompatible=true`, and the committed fixture source assets. **Target (D-037):** assets move to a build-only `Bork2d.BakeFixture.Assets` executable (builder config, `Assets/`, `DLC1/`, PPM/Unit processors); the `bork2d.project.toml` is removed. The PNG remains a committed fixture **source asset** (2x2 RGBA), not a generated output or a test/pack binary fixture; data/sprite TOMLs reference `art/player`, whose asset path is `art/player` with raw payload `art/player-raw`. The library never references the bake tooling. |
| `Bork2d.BakeFixture.Assets` | `Bork2d.Bake.Builder`, `Bork2d.BakeFixture`, `Bork2d.Api` | `Planned` (`task/file-processors`, D-037/D-038) | Build-time executable for the fixture: `Program.cs` configures packs (`Assets` → `assets.pck`, `DLC1` → `dlc1.pck`), texture defaults, explicit `RegisterAsset<T>()` types, and the PPM/Unit processors; building it bakes the fixture packs. It owns the fixture `Assets/`/`DLC1/` source trees (including `player.png` and sidecars) and is referenced by tests/AOT only as a build dependency. It is never in the runtime/AOT publish graph and has no generated host. |
| `Bork2d.Render` | `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets` | `Empty` (wired) | Vulkan device, swapchain or offscreen targets, pipelines, bindless textures, draws, present. |
| `Bork2d.AotSmoke` | `Bork2d.Api`, `Bork2d.BakeFixture`, `Bork2d.Common`, `Bork2d.Runtime` | `Current` (M4/D-M4-14); target uses fixture assets executable build-only | NativeAOT smoke executable: the default headless one-tick run mounts the baked main pack from `AppContext.BaseDirectory/assets.pck`, overlays `dlc1.pck` with `LoadPack`, and asserts the custom data, struct root, sprite, descriptor, raw payload, the DLC `data/dlc`, and its DLC → main texture reference; the opt-in `--windowed [--ticks N]` SDL demo uses `WithoutAssets()`. Current M4 imports [`build/Bork2d.Bake.targets`](../../build/Bork2d.Bake.targets), so a Debug/Release build generates and builds the host and bakes both packs to `$(TargetDir)` (`Bork2dBakePackPaths=assets.pck;dlc1.pck`, copied to `$(PublishDir)`). **Target (D-037/D-038):** it takes a build-only reference to `Bork2d.BakeFixture.Assets` and asks that executable to copy packs; it never references builder/processor assemblies in compile or publish. It no longer references `Bork2d.Assets` directly (the assembly reaches the publish transitively through `Bork2d.Runtime`). There is no generated host or host build step. |
| `Bork2d.Tests` | every solution engine project (`Api`, `Common`, `Assets`, `Render`, `Runtime`, `Bake`, `Bake.Builder`, `Bake.Cli` until retired, `BakeFixture`, `BakeFixture.Assets`) plus `Bork2d.AotSmoke` | `Current`; target adds builder/processor tests | 911 deterministic xUnit tests plus 12 opt-in/conditional tests (923 total; 9 real-window and 3 reparse-point capability), including pack-format/writer, VFS/path/browse, loader/threading, generic reference validation, asset-reference/pack-build contracts, asset error contract, runtime asset lifecycle/error-matrix, D-M4-11 struct roots, D-M4-12 color marshaling, descriptor/diagnostic/path/hash/CLI foundations, the generated-host generator/loader/ALC identity, the generic shape-driven TOML binder matrix (leaves, structs, collections, attributes, spans, enum/date/numeric rules), source discovery/binding, `SpriteAsset` as data with a pack round-trip, the texture pipeline, the baked asset index and BK3xxx reference rules, multi-pack descriptors and local + main reference scopes, pack composition/atomic replacement, the full bake → real `AssetService` load acceptance path (including a struct root and its `AssetRef<StatsRoot>`), structural determinism in-process and across separate CLI processes, incremental skip/force/manifest/host-state rules, the `dotnet build`/publish/clean MSBuild integration for two packs, and dependency-surface tests. **Target (D-036/D-037/D-038):** builder/no-descriptor/no-host tests, explicit/default registration tests, processor registration/override, PPM/Unit end-to-end bakes, sidecar policies, and `MyGame.Assets`-style build auto-bake. The smoke reference copies the app next to the tests; the dependency test loads it by name to check its references. |
| `Bork2d.Editor` | `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Bake.Builder` (+ Avalonia) | `Planned` (M5) | Avalonia editor library/app; JIT; reuses the bake library/builder (D-M4-01). Current M4 targets the internal `Bork2d.Bake`; D-037 target uses the public `Bork2d.Bake.Builder` surface so the editor does not take an internals grant. |

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
`Bork2d.Bake` and the target `Bork2d.Bake.Builder` are JIT tooling. `Bork2d.Bake` pins `Tomlyn`
`2.10.1` (source/sidecar TOML), `PolyType` `1.4.1`
(direct: the binder walks compiled shape metadata), `System.Reflection.MetadataLoadContext`
`11.0.0-rc.1.26425.128` (build-time `Project.Common` metadata without loading game assemblies),
`StbImageSharp` `2.30.16` (texture decode, used by WP3), and `System.IO.Hashing`
`11.0.0-rc.1.26425.128` (`XxHash128`); it references `Bork2d.Api`/`Bork2d.Assets` and never
`Bork2d.Common` (Q-20). The generated `Bork2d.BakeHost` project is not a solution project and has no
`PackageReference`: it references the exact assemblies by absolute `HintPath` (`Project.Common`, the
Q-04 closure, `Bork2d.Api`/`Assets`/`Bake`, Tomlyn, PolyType, Nerdbank.MessagePack, and
`System.IO.Hashing`). `Bork2d.Bake.Cli` references only `Bork2d.Bake`, is an `Exe`, and is not
packable; the target retires it for games. `Bork2d.Bake.Builder` is a public JIT library that
references `Bork2d.Bake`, `Bork2d.Api`, and `Bork2d.Assets`, and exposes only the builder/processor
contract. `Bork2d.BakeFixture` is the `Project.Common`-like fixture: it references only
`Bork2d.Api` (PolyType flows transitively from that reference), sets `IsAotCompatible`, and has no
TOML/bake reference. **Target (D-037):** its source trees move to the build-only
`Bork2d.BakeFixture.Assets` executable, which references `Bork2d.Bake.Builder` and
`Bork2d.BakeFixture`; the data-type library stays tooling-free. Current M4 copy/prepare behavior is
historical; `Bork2d.Tests` references the fixture for full bake/load and host-reference tests, and
`Bork2d.AotSmoke` takes a build-only dependency for the baked-pack AOT proof (Q-16). `Bork2d.AotSmoke`
is the only project that sets `PublishAot`; libraries do not publish independently, and no runtime
project references the bake tooling or its packages. The build targets are imported, not referenced:
the assets executable/bake tooling is built and executed out of process and the generated
`Bork2d.BakeHost` project is built with MSBuild, so no `Bork2d.Bake`, `Bork2d.Bake.Builder`,
`Bork2d.Bake.Cli`, `Bork2d.BakeFixture.Assets`, `Bork2d.BakeHost`, Tomlyn, or StbImageSharp assembly
enters the smoke app's output
or publish graph (the game-side `Bork2d.BakeFixture` assembly is expected there).

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
| **AotSmoke** | ✓ | ✓ | ✗ | ✗ | ✓ | ✗ | ✗ | ✓ | ✗ |
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
- `AotSmoke` uses `Runtime` for the host, `Api`/`Common` for the scene graph, and `BakeFixture` as
  the game-side `Project.Common`; it imports `build/Bork2d.Bake.targets` to bake the fixture pack and
  its default run loads that pack through the public `IAssets` API. It does not reference
  `Bork2d.Assets` directly (the assembly is in the publish transitively through `Runtime`), uses no
  pack tooling, and must not pull `Render` types or any bake tooling assembly in.
- `Bake` is JIT tooling: it references `Api` and `Assets` only (no `Common`, Q-20) plus Tomlyn,
  PolyType, StbImageSharp, and `System.IO.Hashing`; it must not reference `Runtime`, `Render`, or
  `Editor`. The target removes `System.Reflection.MetadataLoadContext`, `Hosting/`, and `Project/`.
  Its public surface is empty; `InternalsVisibleTo` grants the builder, editor, and tests access.
  The target gives `Bake.Builder` the implementation grant; `Bake` itself stays internal.
- `Bake.Builder` is the public JIT tooling surface. It references `Bake` (implementation), `Api`, and
  `Assets`; its public types are the builder, `RegisterAsset<T>()`/`UseDefaultAssets()`/
  `UseDefaultProcessors()`, processor contracts, sidecar policy, output sink, and command result
  types only. It must not expose Tomlyn, StbImageSharp, `Bake` internals, or `PackBuilder` as the
  processor contract.
- `Bake.Cli` references only `Bake`; it is the current M4 console front end. The target retires it
  as the game-facing CLI; the builder command surface in `MyGame.Assets`/fixture assets executables
  replaces it.
- `BakeFixture` is a game-side fixture: it references `Bork2d.Api` only, is AOT-compatible, and is
  never in the runtime graph. Tests and the AOT smoke reference it; the generated bake host
  references it as the common assembly (its real assembly name, not a hardcoded `Project.Common`).
  The target adds `BakeFixture.Assets` (build-only) for the builder/processor flow; the data-type
  library remains tooling-free.
- `Editor` is independent of the renderer: it edits source data, slices textures, and previews
  with Avalonia imaging. It is JIT and not shipped with the game; it reuses the bake library (M5,
  D-M4-01). The target uses `Bake.Builder` rather than an internals grant.
- Tests may reference everything and may use internals.

A new arrow requires updating this document and the decision log; the dependency test fails
otherwise.

### 2.1 Post-M4 target delta (planned)

The matrix above is the current M4 graph. `task/file-processors` adds these arrows; the current
dependency test remains authoritative until the task lands and is updated with it.

| From \ To | Api | Assets | Bake | Bake.Builder | BakeFixture | BakeFixture.Assets | Editor |
| --- | :-: | :-: | :-: | :-: | :-: | :-: | :-: |
| **Bake.Builder** | ✓ | ✓ | ✓ | — | ✗ | ✗ | ✗ |
| **BakeFixture.Assets** | ✓ | ✗ | ✗ | ✓ | ✓ | — | ✗ |
| **Editor** | ✓ | ✓ | ✗ | ✓ | ✗ | ✗ | — |
| **Bake** | ✓ | ✓ | — | ✗ | ✗ | ✗ | ✗ |

Rules for the delta:

- `Bake.Builder` is the only new **public** tooling assembly. It references `Bake` (implementation),
  `Api`, and `Assets` (for the pack writer behind its sink), and may not expose Tomlyn/Stb or the
  internal descriptor/pipeline types. Its explicit `RegisterAsset<T>()` generic entries are the
  only data-type registration mechanism.
- `Bake` stays internal; it gains `InternalsVisibleTo("Bork2d.Bake.Builder")` and loses nothing.
  `Bake.Cli` is retired as the game-facing entry point; the fixture uses `BakeFixture.Assets`.
- The target removes `Bake`'s `Hosting/` and `Project/` areas and its `MetadataLoadContext`
  package/reference. There is no generated host project in the graph and no host IVT.
- `BakeFixture.Assets` is a build-time executable: it references `Bake.Builder` and the
  `BakeFixture` data-type library, owns the fixture source trees, and is never referenced by the
  runtime/AOT publish. `BakeFixture` keeps referencing only `Api`.
- `BakeFixture` gains the `Unit` data type used by the processor acceptance test; it remains
  AOT-compatible and shape-only.
- `Editor` may reference `Bake.Builder` instead of taking an internals grant on `Bake`; M5 decides
  the final editor surface.
- Game projects (`MyGame.Assets`) are outside this matrix: they reference `Bake.Builder`,
  `Project.Common`, and `Bork2d.Api`; `Project.Desktop` references `MyGame.Assets` for build order
  only. `Project.Common` still references `Bork2d.Api` only.

## 3. Public surface and assembly visibility

### Public assemblies

| Assembly | Public types |
| --- | --- |
| `Bork2d.Api` | `IEngine<TLayer>`, `IGame<TLayer>`, `ITime`, `IWindow`, `IInput`, `IAssets`, `ISceneRoot`, `ITransform`, `ICamera<TLayer>`, `ISpriteRender<TLayer>`, `Color`, `ClearKind`, `KeyCode`, `MouseButton`, `AssetInfo`, `AssetRef<TAsset>`, `DataRef`, `SpriteAsset`, `TextureDescriptor`, `SpriteTopology`, `AssetException`, `AssetErrorKind`, `Bork2dException`, plus the `ColorMarshaler`/`AssetRefMarshaler<TAsset>`/`DataRefMarshaler` serialization infrastructure. The types are grouped by area namespace (§4). `IAssets.LoadAsset<T>` carries the shape-only `where T : IShapeable<T>` constraint (D-M4-11). `Bork2d.Api` grants `Bork2d.Assets` internal access to the non-public reference marker used by pack-time traversal (D-035). |
| `Bork2d.Runtime` | `Bork2dAppBuilder<TLayer>`, `Bork2dApp`. Nothing else; options, runtime state, and implementation types stay internal. |
| `Bork2d.Assets` | **Tooling only:** `Bork2d.Assets.Pack.PackBuilder`, `Bork2d.Assets.Pack.PackCompressionMode`. Every implementation type (pack reader/archive, header schema, paths, Zstd helpers, the internal loader service, reference collector, and validation) stays internal. Game code must not reference this assembly; it builds packs in tooling/tests and the runtime consumes them. |
| `Bork2d.Bake` | **None (current M4).** The bake library is internal (`Bork2d.Bake.*`), with `InternalsVisibleTo` to `Bork2d.BakeHost`, `Bork2d.Bake.Cli`, `Bork2d.Editor`, and `Bork2d.Tests` (Q-02). Target: remove the host/CLI grants and add `Bork2d.Bake.Builder`; `Bake` itself stays internal. |
| `Bork2d.Bake.Builder` | **Planned (D-037/D-038):** `BakeApplicationBuilder`, `BakeApplication`, `RegisterAsset<T>()`, `UseDefaultAssets()`, `UseDefaultProcessors()`, `IAssetProcessor`, `AssetProcessor<TSidecar>`, `AssetProcessorContext`, `IAssetOutputSink`, `IAssetProcessorDiagnostics`, `SidecarPolicy`, the public texture-default enums, and command/result types. The implementation types (`Bake`, Tomlyn, Stb) stay hidden; there is no host type on the target surface. |
| `Bork2d.Bake.Cli` | **None.** `Program` and `CliApplication` are internal; the executable is a tooling entry point, not a game or public API surface. Retired for games by the target (D-037/D-038). |
| `Bork2d.BakeFixture` | `Bork2d.BakeFixture.GameConfig`, `Bork2d.BakeFixture.Stats`, `Bork2d.BakeFixture.StatsRoot`, and (target) `Unit` (fixture-only test data, Q-16/Q-35). Public because the bake/load and processor tests use them without `InternalsVisibleTo`; the library is never shipped with a game. |
| `Bork2d.Editor` | `Bork2dEditorBuilder`, `Bork2dEditorApp`, and the public editor service types needed by `Project.Editor` (`Planned`, M5). |

`ILoggerFactory` is a Microsoft.Extensions.Logging type exposed by `IEngine`; it is not a Bork2d
public type. `Bork2d.Api` takes it from `Microsoft.Extensions.Logging.Abstractions` in the
.NET 11 shared framework.

`Bork2d.Assets`' tooling types are the one exception to the internal-project rule (D-001,
D-M3-04): they exist for pack creation, not for gameplay. The dependency test asserts that the
assembly exports exactly those two types. `Bork2d.AotSmoke` no longer references `Assets` directly:
its default run loads the baked pack and consumes it through `IAssets` from `Bork2d.Api`; the
`Bork2d.Assets` assembly is still present in the publish because `Bork2d.Runtime` references it.

`Bork2d.Bake` and `Bork2d.Bake.Cli` are fully internal tooling assemblies (Q-02); the dependency
test asserts that each exports zero public types. The target removes the generated `Bork2d.BakeHost`
assembly entirely; there is no host project, host ALC, host manifest, or host-generation state.
WP5's incremental manifest/planner and MSBuild targets add no public type; the model/planner/JSON
stores live in the internal `Bork2d.Bake.Incremental` namespace and the targets are build files
that invoke the assets executable.

D-036/D-037/D-038 add the **public** `Bork2d.Bake.Builder` assembly. Its surface is deliberately
narrow: builder/configuration, `RegisterAsset<T>()`/`UseDefaultAssets()`/`UseDefaultProcessors()`,
processor contract, sidecar policy, output sink, diagnostics, and command results. It may reference
`Bork2d.Bake`, `Bork2d.Api`, and `Bork2d.Assets`, but no Tomlyn, StbImageSharp, descriptor, or
`PackBuilder` type appears in its public signatures. The dependency test must assert
that surface before the task lands; the current test still asserts zero public types in
`Bake`/`Bake.Cli`.

`Bork2d.BakeFixture` is a game-side test fixture, not an engine library: its public data types exist
only so the explicit `RegisterAsset<T>()` registrations and the bake/load tests can use the same
`Project.Common`-like assembly without `InternalsVisibleTo` (Q-16/Q-35). Its source trees are not
code and add no public surface.
The target adds `Bork2d.BakeFixture.Assets` as a separate build-only executable; the data-type
library stays `Bork2d.Api`-only and AOT-compatible. The dependency test asserts both surfaces and
that `Bork2d.BakeFixture` references `Bork2d.Api` only.

### Internal assemblies

`Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render`, `Bork2d.Bake`, and `Bork2d.Bake.Cli` are
implementation details. Types are `internal` by default and exposed only to the assemblies that need
them. The only exception is the tooling-only public surface listed above:

| Project | Internals visible to |
| --- | --- |
| `Bork2d.Common` | `Bork2d.Runtime`, `Bork2d.Render`, `Bork2d.Assets`, `Bork2d.Editor`, `Bork2d.Tests`, `Bork2d.AotSmoke` |
| `Bork2d.Assets` | `Bork2d.Runtime`, `Bork2d.Render`, `Bork2d.Bake`, `Bork2d.Editor`, `Bork2d.Tests` |
| `Bork2d.Bake` | Current: `Bork2d.BakeHost`, `Bork2d.Bake.Builder`, `Bork2d.Bake.Cli`, `Bork2d.Editor`, `Bork2d.Tests`; target: `Bork2d.Bake.Builder`, `Bork2d.Editor`, `Bork2d.Tests` (no host/CLI) |
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
| `Bork2d.Bake` | Current: `Diagnostics/`, `Project/`, `Paths/`, `Toml/`, `Hosting/`, `Sources/`, `Textures/`, `Baking/`, `Incremental/`; target removes `Project/`, `Hosting/`, adds `Processors/` | `AssemblyReferenceAnchor` |
| `Bork2d.Bake.Builder` | `Configuration/`, `Processors/`, `Commands/` (target) | `BakeApplicationBuilder`, `BakeApplication`, `RegisterAsset<T>()`, defaults, processor contracts, sidecar policy |
| `Bork2d.Bake.Cli` | — | `Program`, `CliApplication` (current M4; retired for games by target) |
| `Bork2d.BakeFixture` | — (data-type library; source trees move to the assets executable in the target) | `GameConfig`, `Stats`, `StatsRoot`, and target `Unit` |
| `Bork2d.BakeFixture.Assets` | `Assets/` and `DLC1/` (source TOML, texture/processor sidecars; build-time inputs, not namespaces) | `Program` (builder config); target |
| `Bork2d.Runtime` | `Hosting/`, `Input/`, `Sdl/`, `Window/` | `Bork2dApp`, `Bork2dAppBuilder`, `AssemblyReferenceAnchor` |
| `Bork2d.Tests` | `Architecture/`, `Scene/`, `Runtime/`, `Assets/{Pack,Vfs,Service,Sprites}/`, `Bake/{Baking,Cli,Diagnostics,Hosting,Incremental,MsBuild,Paths,Project,Toml,Sources,Textures,Processors}/`, `Platform/{Sdl,Window}/`, `Input/`, `TestDoubles/`, `TestSupport/` | none |
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
  `Bork2d.Bake`, `Bork2d.Bake.Builder`, `Bork2d.Bake.Cli`, `Bork2d.BakeFixture.Assets`, and
  `MyGame.Assets` are JIT tooling/executables and set neither AOT-compatibility nor `PublishAot`;
  they are never in the AOT publish graph.
- Build: `dotnet build src/Bork2d/Bork2d.slnx`. The AOT smoke takes a build-only dependency on
  `Bork2d.BakeFixture.Assets`, whose build bakes the fixture packs (`assets.pck`, `dlc1.pck`) and
  copies them next to the smoke app/publish. Current M4 imports
  [`build/Bork2d.Bake.targets`](../../build/Bork2d.Bake.targets); the target replaces it with the
  assets-executable flow.
  Local AOT gate:
  `dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64`, then run
  the published exe (default headless: mounts the main pack, overlays the DLC pack, and exits 0) and
  `--windowed --ticks 120` (exit 0 after exactly 120 ticks).
  AOT publish/run is a local gate, not a CI job; the publish output must contain no `Bork2d.Bake`,
  `Bork2d.Bake.Builder`, `Bork2d.Bake.Cli`, `Bork2d.BakeFixture.Assets`, Tomlyn, or StbImageSharp
  assembly (`Bork2d.BakeFixture` is the expected game-side common assembly) and must contain both
  baked packs and `SDL2.dll`. See
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
- [Asset processors and `MyGame.Assets`](asset-processors.md) — target builder/processor model and
  `MyGame.Assets` project layout.
- [NativeAOT](nativeaot.md) — runtime build constraints.
- [Roadmap](../roadmap.md) — milestone sequence and progress.
