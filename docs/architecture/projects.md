# Projects and Dependencies

> **Status:** `Current` after the project-consolidation refactor
> ([refactor-projects](../specs/refactor-projects.md), D-041/D-042, 2026-10-27) and enforced by the
> dependency test. The engine solution has five projects; the smoke and fixture projects live in a
> separate examples solution. `Bork2d.Engine` carries the `Bork2d.Engine.*` public contract plus the
> internal engine implementation, and `Bork2d.Bake` carries the public bake-tooling surface plus the
> internal bake implementation.

## 1. Current projects

All engine projects target `net11.0` and live in `src/Bork2d/`; the examples live in `examples/`.
They are listed in [`Bork2d.slnx`](../../src/Bork2d/Bork2d.slnx) and
[`Bork2d.Examples.slnx`](../../examples/Bork2d.Examples.slnx).

| Project | References | Status | Responsibility |
| --- | --- | --- | --- |
| `Bork2d.Engine` | none (Bork2d) | `Current` | Public contract (`Bork2d.Engine`, `.Scene`, `.Rendering`, `.Input`, `.Platform`, `.Assets`) plus the internal root-owned scene tree, transforms, time, MessagePack shape policy, v1 pack reader/writer, VFS, loader, and asset service. The tooling-only `Bork2d.Engine.Assets.Pack.PackBuilder`/`PackCompressionMode` stay public for pack creation. |
| `Bork2d.Bake` | `Bork2d.Engine` | `Current` | Public JIT tooling surface (`BakeApplicationBuilder`, `BakeApplication`, `RegisterAsset<T>()`, `UseDefaultAssets()`, `UseDefaultProcessors()`, processor contracts, sidecar policy, output sink, commands) plus the internal implementation (Tomlyn binder, processor registry, texture processor, pack composition, schema-3 incremental planner). |
| `Bork2d.Render` | `Bork2d.Engine` | `Empty` (wired) | Vulkan device, swapchain or offscreen targets, pipelines, bindless textures, draws, present. M6/M8. |
| `Bork2d.Runtime` | `Bork2d.Engine`, `Bork2d.Render` | `Current` (M3) | Public builder/app, validation, windowed SDL host (window, input, time, uncapped loop), one-tick headless/offscreen host, app-owned logging, and the app-scoped asset-service lifecycle. |
| `Bork2d.Tests` | `Bork2d.Engine`, `Bork2d.Bake`, `Bork2d.Render`, `Bork2d.Runtime`, examples `Bork2d.BakeFixture`, examples `Bork2d.BakeFixture.Assets` | `Current` | Deterministic xUnit suite, dependency-matrix/public-surface tests, and the Q-20 metadata guard. The smoke is not referenced. |
| `Bork2d.AotSmoke` (examples) | `Bork2d.Engine`, `Bork2d.Runtime`, `Bork2d.BakeFixture` (+ build-only `Bork2d.BakeFixture.Assets`) | `Current` | NativeAOT smoke executable: headless one-tick run loading the baked fixture packs (8 main / 9 merged entries) plus `--windowed [--ticks N]` SDL demo. It imports `build/Bork2d.Assets.Copy.targets`; the fixture assets executable is built out of process. Never in the runtime graph. |
| `Bork2d.BakeFixture` (examples) | `Bork2d.Engine` | `Current` (D-037; AOT-compatible) | Minimal `Project.Common`-like fixture library with public `[GenerateShape] partial` data types (`GameConfig`, `Stats`, `StatsRoot`, `Unit`). It stays tooling-free and keeps its `Bork2d.BakeFixture` namespace. |
| `Bork2d.BakeFixture.Assets` (examples) | `Bork2d.Bake`, `Bork2d.BakeFixture` | `Current` (D-037; build-time only) | Build-time executable for the fixture: configures packs (`Assets` → `assets.pck`, `DLC1` → `dlc1.pck`), texture defaults, explicit `RegisterAsset<T>()` types, and the PPM/Unit processors; building it bakes the packs and writes a schema-3 manifest. It owns the fixture `Assets/`/`DLC1/` source trees and is never in the runtime/AOT publish graph. |
| `Bork2d.Editor` | `Bork2d.Engine`, `Bork2d.Bake` (+ Avalonia) | `Planned` | Avalonia editor library/app; JIT; consumes the public `Bork2d.Bake` builder/processor surface. |

`Bork2d.Engine` references `PolyType` `1.4.1` (the `IShapeable<T>` contract and the public
generated shapes), `Nerdbank.MessagePack` `1.4.95` (generated-shape serialization; no reflection
resolver), and `System.IO.Hashing` `11.0.0-rc.1.26425.128` (`XxHash3`/`XxHash128`). Zstd is the
.NET 11 built-in `System.IO.Compression` implementation. `Bork2d.Runtime` references
`Microsoft.Extensions.Logging` `11.0.0-rc.1.26425.128`, `ZLogger` `2.5.10`, and `Silk.NET.SDL`
`2.23.0` (which carries `Ultz.Native.SDL` `2.32.10`; the shipped win-x64 `SDL2.dll` reports
`SDL_GetVersion` **2.30.8**, the runtime version M2 is verified against). No SDL type leaves
`Bork2d.Runtime`, and `Bork2d.Engine` stays free of GPU/OS/Silk/ZLogger/Avalonia.

`Bork2d.Bake` references `Bork2d.Engine` plus `Tomlyn` `2.10.1`,
`PolyType` `1.4.1`, `StbImageSharp` `2.30.16`, and `System.IO.Hashing`
`11.0.0-rc.1.26425.128`; it is JIT and sets neither `IsAotCompatible` nor `PublishAot`. The public
surface hides Tomlyn, StbImageSharp, the internal bake implementation, and `PackBuilder`.

`Bork2d.AotSmoke` is the only project that sets `PublishAot`; libraries do not publish
independently, and no runtime project references the bake tooling or its packages. The
`Bork2d.Bake`/`Bork2d.BakeFixture.Assets` tooling assemblies are built and executed out of process
by the imported targets, so they never enter the smoke app's output or publish graph (the game-side
`Bork2d.BakeFixture` assembly is expected there).

## 2. Target dependency graph

The graph is acyclic and flows contracts → implementation → composition. `Runtime` is the
composition root and the only project that knows both the engine and the renderer.

| From \ To | Engine | Render | Runtime | Bake | Fixture | Fixture.Assets |
| --- | :-: | :-: | :-: | :-: | :-: | :-: |
| **Engine** | — | ✗ | ✗ | ✗ | ✗ | ✗ |
| **Render** | ✓ | — | ✗ | ✗ | ✗ | ✗ |
| **Runtime** | ✓ | ✓ | — | ✗ | ✗ | ✗ |
| **Bake** | ✓ | ✗ | ✗ | — | ✗ | ✗ |
| **AotSmoke** | ✓ | ✗ | ✓ | ✗ | ✓ | ✗ |
| **Fixture** | ✓ | ✗ | ✗ | ✗ | — | ✗ |
| **Fixture.Assets** | ✗ | ✗ | ✗ | ✓ | ✓ | — |
| **Tests** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |

Rules behind the matrix:

- `Engine` references no Bork2d assembly and only the BCL, `Microsoft.Extensions.Logging.Abstractions`,
  `PolyType`, `Nerdbank.MessagePack`, and `System.IO.Hashing`. The public contract must not expose
  MessagePack or hashing types; the dependency test enforces both the reference set and the public
  signatures.
- `Render` consumes the engine contract and internal snapshot contracts; it must not reference
  `Runtime`, `Bake`, or SDL. Window/surface lifetime is handed to it; offscreen targets are its own.
- `Runtime` is the composition root: builder, app host, SDL window/input (windowed), Vulkan
  bootstrap, mode selection, ZLogger configuration, service wiring.
- `Bake` is JIT tooling: it references `Engine` only and never `Runtime`, `Render`, or the fixture.
  Q-20 (no engine implementation dependency) is no longer assembly-enforced because the contract and
  implementation share `Bork2d.Engine`; the dependency test enforces it at metadata level: Bake may
  only reference the `Bork2d.Engine`, `.Rendering`, `.Assets`, `.Assets.Pack`, and `.Assets.Vfs`
  namespaces (the last two are the pack-writer and canonical-path seams).
- `AotSmoke` uses `Runtime` for the host, `Engine` for the public scene/asset contract, and
  `BakeFixture` as the game-side `Project.Common`. It imports `build/Bork2d.Assets.Copy.targets`,
  which builds `BakeFixture.Assets` out of process and copies the baked packs next to the app and
  the publish. It must not pull `Render` types or any bake-tooling/assets-executable assembly in.
- `Fixture` is a game-side fixture: it references `Bork2d.Engine` only, is AOT-compatible, and is
  never in the runtime graph. `Fixture.Assets` is the build-time executable that owns the source
  trees and processors; Tests reference it normally to instantiate its internal processors, and the
  AOT smoke builds it out of process.
- `Tests` may reference everything and use internals. The dependency test asserts the runtime
  assemblies never reference the bake tooling and that Tests no longer references the smoke.
- `Editor` is independent of the renderer: it edits source data, slices textures, and previews with
  Avalonia imaging; it consumes the public `Bork2d.Bake` surface.

A new arrow requires updating this document and the dependency test; the dependency test fails
otherwise.

## 3. Public surface and assembly visibility

### Public assemblies

| Assembly | Public types |
| --- | --- |
| `Bork2d.Engine` | `IEngine<TLayer>`, `IGame<TLayer>`, `ITime`, `IWindow`, `IInput`, `IAssets`, `ISceneRoot`, `ITransform`, `ICamera<TLayer>`, `ISpriteRender<TLayer>`, `Color`, `ClearKind`, `KeyCode`, `MouseButton`, `AssetInfo`, `AssetRef<TAsset>`, `DataRef`, `SpriteAsset`, `TextureDescriptor`, `SpriteTopology`, `TextureFormat`, `TextureFilter`, `TextureWrap`, `AssetException`, `AssetErrorKind`, `Bork2dException`, the `ColorMarshaler`/`AssetRefMarshaler<TAsset>`/`DataRefMarshaler` serialization infrastructure, and the tooling-only `Bork2d.Engine.Assets.Pack.PackBuilder`/`PackCompressionMode`. |
| `Bork2d.Runtime` | `Bork2dAppBuilder<TLayer>`, `Bork2dApp`. Nothing else. |
| `Bork2d.Bake` | `BakeApplicationBuilder`, `BakeApplication`, `RegisterAsset<T>()`, `UseDefaultAssets()`, `UseDefaultProcessors()`, `IAssetProcessor`, `AssetProcessor<TSidecar>`, `AssetProcessorContext`, `IAssetOutputSink`, `IAssetProcessorDiagnostics`, `SidecarPolicy`, and the command surface. Tomlyn/Stb/pack internals stay hidden. |
| `Bork2d.BakeFixture` | `Bork2d.BakeFixture.GameConfig`, `.Stats`, `.StatsRoot`, `.Unit` (fixture-only test data). |
| `Bork2d.BakeFixture.Assets` | **None.** `Program` and the PPM/Unit processors are internal (visible to Tests through `InternalsVisibleTo`). |
| `Bork2d.Editor` | `Bork2dEditorBuilder`, `Bork2dEditorApp`, and the public editor service types (`Planned`, M5). |

`ILoggerFactory` is a Microsoft.Extensions.Logging type exposed by `IEngine`; it is not a Bork2d
public type. `Bork2d.Engine` takes it from the .NET 11 shared framework.

The pack writer is the one documented exception to the "implementation types stay internal" rule
(D-001, D-M3-04): `PackBuilder`/`PackCompressionMode` exist for pack creation, not gameplay. The
dependency test asserts exactly those two public types from the asset implementation. Game code
consumes assets through `IAssets`; it must not call the pack writer.

### Internal assemblies

`Bork2d.Engine`'s scene/time/asset implementation, `Bork2d.Render`, and `Bork2d.Bake`'s
implementation are implementation details. Types are `internal` by default and exposed only to the
assemblies that need them.

| Project | Internals visible to |
| --- | --- |
| `Bork2d.Engine` | `Bork2d.Render`, `Bork2d.Runtime`, `Bork2d.Bake`, `Bork2d.Editor`, `Bork2d.Tests` |
| `Bork2d.Bake` | `Bork2d.Editor`, `Bork2d.Tests` |
| `Bork2d.Render` | `Bork2d.Runtime`, `Bork2d.Tests` |
| `Bork2d.Runtime` | `Bork2d.Tests` |
| `Bork2d.BakeFixture.Assets` | `Bork2d.Tests` |

The `Bork2d.Engine` → `Bork2d.Bake` grant is intentional: the baker validates canonical asset paths
with the engine's one path grammar. The metadata guard in the dependency test restricts Bake to the
allowed engine namespaces so the grant does not become a general implementation dependency.

Consequences:

- There is **no accidental public API** from the engine implementation: a public type outside the
  documented contract and pack tooling is a bug.
- `Runtime` may reference engine internals directly; it does not create a parallel public model.
- Trimming/AOT compatibility is **not** deferred: `IsAotCompatible` and the trim/AOT analyzers are
  on from M1 (see [NativeAOT](nativeaot.md)).

## 4. Source layout and namespaces

Every project uses one folder per area and a namespace derived from the project and folder path.
Folder names are PascalCase and match the namespace segment exactly; a file in
`Bork2d.Engine/Assets/Loading/AssetService.cs` declares
`namespace Bork2d.Engine.Assets.Loading;`. IDE0130 is an error (D-034).

| Project | Folders (namespace) | Root-namespace files |
| --- | --- | --- |
| `Bork2d.Engine` | `Scene/` → `.Scene`; `Rendering/` → `.Rendering`; `Input/` → `.Input`; `Platform/` → `.Platform`; `Time/` → `.Time`; `Assets/` → `.Assets`, with `Pack/`, `Vfs/`, `Loading/` | `IEngine`, `IGame`, `Bork2dException` |
| `Bork2d.Bake` | `Baking/`, `Commands/`, `Diagnostics/`, `Incremental/`, `Paths/`, `Processors/`, `Sources/`, `Textures/`, `Toml/` | `BakeApplicationBuilder`, `BakeApplication`, the processor/sidecar contracts, `SidecarPolicy` |
| `Bork2d.Runtime` | `Hosting/`, `Input/`, `Sdl/`, `Window/` | `Bork2dApp`, `Bork2dAppBuilder`, `AssemblyReferenceAnchor` |
| `Bork2d.Tests` | `Architecture/`, `Scene/`, `Runtime/`, `Assets/{Pack,Vfs,Service,Sprites}/`, `Bake/{Baking,Cli,Diagnostics,Incremental,MsBuild,Paths,Processors,Sources,Textures,Toml}/`, `Platform/{Sdl,Window}/`, `Input/`, `TestDoubles/`, `TestSupport/` | none |
| `Bork2d.AotSmoke` (examples) | `Smoke/`, `Demo/` | `Program`, `DemoArguments` |
| `Bork2d.BakeFixture` (examples) | — (data-type library) | `GameConfig`, `Stats`, `StatsRoot`, `Unit` |
| `Bork2d.BakeFixture.Assets` (examples) | `Processors/`; `Assets/` and `DLC1/` (source TOML, textures, sidecars) | `Program` (top-level statements) |
| `Bork2d.Render` | — | `AssemblyReferenceAnchor` (renderer folders arrive with M6/M8) |

`Bork2d.Engine` keeps `IEngine`, `IGame`, and `Bork2dException` in the root namespace. The rest of
the public contract is grouped by area:

| Namespace | Public types |
| --- | --- |
| `Bork2d.Engine` | `IEngine<TLayer>`, `IGame<TLayer>`, `Bork2dException` |
| `Bork2d.Engine.Scene` | `ISceneRoot`, `ITransform`, `ICamera<TLayer>`, `ISpriteRender<TLayer>` |
| `Bork2d.Engine.Rendering` | `Color`, `ColorMarshaler`, `ClearKind` |
| `Bork2d.Engine.Input` | `IInput`, `KeyCode`, `MouseButton` |
| `Bork2d.Engine.Platform` | `IWindow`, `ITime` |
| `Bork2d.Engine.Assets` | `IAssets`, `AssetInfo`, `AssetException`, `AssetErrorKind`, `AssetRef<TAsset>`, `DataRef`, `SpriteAsset`, `TextureDescriptor`, `SpriteTopology`, `TextureFormat`, `TextureFilter`, `TextureWrap`, `AssetRefMarshaler<TAsset>`, `DataRefMarshaler` |
| `Bork2d.Engine.Assets.Pack` | `PackBuilder`, `PackCompressionMode` (tooling only) |

The tooling pack writer is `Bork2d.Engine.Assets.Pack.PackBuilder`/`PackCompressionMode`; it is not
part of the game contract (D-001, D-M3-04, D-034). A namespace change is a public-contract change:
update this section, the API guide/reference, and the decision log in the same change.

Internal implementation namespaces: `Bork2d.Engine.Time` (frame clock),
`Bork2d.Engine.Assets.Vfs` (pack stack and canonical paths),
`Bork2d.Engine.Assets.Loading` (asset service/loader), `Bork2d.Engine.Assets.Pack` (reader/writer),
and the internal scene types inside `Bork2d.Engine.Scene` (`Transform`, `SceneRoot`, `GameEngine`,
`Camera`, `SpriteRender`, `ISceneNode`, `LayerValidation`).

## 5. Game solution layout

A game generated for Bork2d uses three projects. The names below are the template's generic names; a
real game uses its own prefix (`MyGame.Common`, `MyGame.Desktop`, …).

```
MyGame/
  MyGame.sln
  src/
    Project.Common/
      Project.Common.csproj      -> Bork2d.Engine
      Game/                      game code: IGame implementation, systems, runtime data types
    MyGame.Assets/
      MyGame.Assets.csproj       -> Bork2d.Bake, Project.Common; Exe;
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
texture defaults, processors, and explicit `RegisterAsset<T>()` types in `Program.cs`. Building it
runs its imported target, invokes the executable's `bake` command, and writes every configured pack.
`Project.Desktop` takes a **build-only** reference to `MyGame.Assets` and imports a thin copy target.
`Project.Desktop` never references the builder or processor assemblies in its compile or publish
graph.

A `dotnet run` still resolves `WithAssetsPack("assets.pck")` against the process current directory
(Q-10); the host passes an absolute path or sets its working directory. The M9 template owns the
final ergonomics.

| Project | May reference | Must not reference |
| --- | --- | --- |
| `Project.Common` | `Bork2d.Engine` | `Bork2d.Runtime`, `Bork2d.Render`, `Bork2d.Bake` |
| `MyGame.Assets` | `Bork2d.Bake`, `Project.Common` | `Bork2d.Runtime`, `Bork2d.Render` |
| `Project.Desktop` | `Bork2d.Runtime`, `Project.Common`; build-only `MyGame.Assets` | `Bork2d.Render`, `Bork2d.Bake` (compile/publish) |
| `Project.Editor` | `Bork2d.Editor`, `Project.Common`; `Bork2d.Bake` | `Bork2d.Render` |

A developer's **runtime** code writes against two engine assemblies: `Bork2d.Engine` in
`Project.Common` and `Bork2d.Runtime` in `Project.Desktop`. The build-time `MyGame.Assets` project
adds `Bork2d.Bake`; it is JIT, build-only, and never shipped. A data type may live in an assembly
that `Project.Common` references; resolution is the builder's explicit `RegisterAsset<T>()`
dictionary keyed by full type name; there is no reflection or common-assembly lookup. The builder
and processor model are owned by
[asset processors](asset-processors.md#3-project-model).

## 6. Build, packaging, and versioning

- `Directory.Build.props` sets `net11.0`, nullable, implicit usings, warnings-as-errors,
  `EnforceCodeStyleInBuild`, deterministic builds, and .NET analyzers for every project.
- The SDK is `11.0.100-rc.1`, pinned by `global.json` with `rollForward: latestPatch` and
  `allowPrerelease: true`.
- `Engine`, `Render`, `Runtime`, and `BakeFixture` set `IsAotCompatible`, `EnableTrimAnalyzer`, and
  `EnableAotAnalyzer`; they do not set `PublishAot`. `Bork2d.AotSmoke` sets `PublishAot` and
  `InvariantGlobalization`. `Bork2d.Bake` and `Bork2d.BakeFixture.Assets` are JIT tooling and set
  neither AOT-compatibility nor `PublishAot`.
- Build: `dotnet build src/Bork2d/Bork2d.slnx`. The examples solution is
  `dotnet build examples/Bork2d.Examples.slnx`; it is not built by CI (the local AOT gate exercises
  its smoke app).
- Local AOT gate:
  `dotnet publish examples/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64`, then run
  the published exe (default headless: mounts the main pack, overlays the DLC pack, asserts 8 main /
  9 merged entries, exits 0) and `--windowed --ticks 120` (exit 0 after exactly 120 ticks). The
  publish must contain `Bork2d.Engine.dll`, `Bork2d.Runtime.dll`, `Bork2d.Render.dll`,
  `Bork2d.BakeFixture.dll`, both packs, and `SDL2.dll`, and no `Bork2d.Bake`,
  `Bork2d.BakeFixture.Assets`, Tomlyn, or StbImageSharp assembly.
- CI restores, verifies formatting, builds Debug, and runs Debug tests on pull requests to
  `develop` and on pushes to `develop`. CI does not build the examples solution.
- Distribution starts as source/project references; NuGet packaging is M9.
- Versioning: pre-1.0 assemblies may break. A public API change updates the API guide, reference,
  and decision log in the same change.

## 7. Related documents

- [Architecture overview](overview.md) — invariants and context.
- [Runtime lifecycle](runtime-lifecycle.md) — modes, builder ownership, startup.
- [Assets and tooling](assets-and-tooling.md) — pack VFS, editor, and `Project.Editor`.
- [Asset processors and `MyGame.Assets`](asset-processors.md) — builder/processor model and layout.
- [NativeAOT](nativeaot.md) — runtime build constraints.
- [Project consolidation](../specs/refactor-projects.md) — this refactor's scope and as-built record.
- [Roadmap](../roadmap.md) — milestone sequence and progress.
