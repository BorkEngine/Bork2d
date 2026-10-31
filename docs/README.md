# Bork2d Documentation

> **Status:** **M0–M4 are merged into `develop`; the post-M4 asset-processor task
> (D-036/D-037/D-038/D-039/D-040) is merged (2026-10-27, PR #8).** The repository contains the M1
> build/quality
> baseline, public contracts, root-owned scene tree, builder, logging, the NativeAOT gate, the
> M2 SDL windowed host (window, input, time, uncapped loop), the M3 asset pipeline (pack reader/writer,
> VFS, loader, public `SpriteAsset`/`TextureDescriptor`, typed `AssetRef<T>`/`DataRef` links,
> runtime integration), and the builder-configured bake: a game-owned `MyGame.Assets` executable
> registers explicit data types and asset processors, bakes on build, and records a schema-3
> incremental manifest. The project descriptor, generated host, `Bork2d.Bake.Cli`, and
> `generate-host` are removed. The editor is out of Stage 1 (M5 removed); the sprite renderer is the
> approved M8 target. **M6 (Vulkan bring-up) is merged into `develop` (2026-10-31, PR #11, merge
> `5de725c`)**. The Render foundation, the windowed renderer, the offscreen
> renderer, and the Runtime host wiring are complete, and the AOT/Pong gate proves the published
> `--headless`, `--windowed --ticks 120`, and `--offscreen --ticks 120` runs with a zero-warning
> publish. **M7 (frame handoff and pacing) is merged into `develop` (2026-10-31, PR #12, merge
> `5de5ac3`).** WP-1–WP-5 landed sequentially: the snapshot ring/capture, renderer consumption,
> host backpressure, and the paced headless loop are implementation-complete, and the Pong
> `--headless` gate defaults to 120 ticks (`--headless --ticks N` overrides). The spec decisions are recorded (all 23 questions and F5–F8, in the
> [decision log](decisions.md#m7-frame-handoff-and-pacing-decisions-approved-2026-10-31) as
> `D-M7-01…D-M7-27`); as-built evidence is in [M7 spec §18](specs/m7-frame-handoff.md#18-as-built-evidence-2026-10-31).
> **M8 (sprite renderer) is implementation-complete through WP-5; the milestone PR is open for
> review and is not merged.** The owner-approved [M8 spec](specs/m8-sprite-renderer.md)
> (2026-10-31) is the M8 contract. WP-1 reconciled the owning docs; WP-2 added the build-only shader
> compiler + generated SPIR-V, the internal sprite pipeline/layout/sampler cache, the shared camera
> projection, and the public `ICamera.WorldToScreen`/`ScreenToWorld`/`WorldSize` members; WP-3 added
> the internal texture pages/placeholder/bindless resources and the public
> `RenderStats`/`IEngine.RenderStats`; WP-4 added the shared frame planner, per-camera passes/draws,
> growable mapped instance buffers, per-frame stats publication, and the public
> `IEngine.CaptureFrameAsync` (all `Current`). WP-5 added the explicit short-vs-long Pong/AOT
> presentation gate (`PongAcceptanceGate.LongRunTickThreshold = 1000`; the 120-tick runs assert
> lifecycle/content/validation, the 20,000-tick runs assert the draw/statistics relationships), the
> opt-in Release 50,000-sprite benchmark (median **177.3 FPS**, p50 5.640 ms, p95 6.562 ms, zero
> measured allocations, validation 0/0), the manual `CaptureFrameAsync` visual artifact, and the
> final as-built docs. The milestone PR is not merged; the 165 FPS target and all other gates were
> kept.
> Check status markers before treating an API as implemented.
>
> The [file-processors spec](specs/file-processors.md) owns implementation scope/acceptance; the
> [asset processors](architecture/asset-processors.md) doc owns the architecture. The
> project-consolidation refactor (D-041/D-042,
> [spec](specs/refactor-projects.md)) is merged into `develop` (2026-10-28, PR #9); it reduced the
> engine solution to five projects. The Pong example (D-043/D-044,
> [spec](specs/pong-example.md)) is merged into `develop` (2026-10-29, PR #10) and replaces the exploratory smoke/fixture projects with
> `examples/pong` and is the AOT gate: `Pong.Desktop` publishes with no tooling assembly in the
> output, loads the 7-entry Pong pack headless, and runs `--headless` (120 ticks by default, or
> `--headless --ticks N`), the short `--windowed --ticks 120`/`--offscreen --ticks 120` gate, and
> the long `--offscreen --ticks 20000`/`--windowed --ticks 20000` presentation gate. Verification is
> the local gate set: Debug/Release zero-warning builds, 1335 deterministic plus 27
> opt-in/conditional tests (1362 total) for the engine, 58 Pong tests, `dotnet format` for both
> solutions, the docs link check, `git diff --check`, the 50,000-sprite Release benchmark, and the
> local AOT publish/run. CI runs on pull
> requests to `develop` and pushes to `develop`; it does not build the Pong solution.

Bork2d is a focused 2D game engine for .NET: **Vulkan 1.3** rendering, **SDL2** windowing through
**Silk.NET**, a NativeAOT runtime, and **`assets.pck`** assets addressed by canonical paths.
Gameplay ticks on the tick thread; rendering consumes immutable snapshots on a separate thread.
Windowed, offscreen, and headless are the three modes.

Game code consumes exactly two assemblies:

| Assembly | Role |
| --- | --- |
| `Bork2d.Engine` | Public contract and value types: engine, game, camera, sprite, input, assets, logging factory. No GPU/OS/Silk types. |
| `Bork2d.Runtime` | Public startup surface: builder, app host, modes. Composes the engine, renderer, and platform implementations. |

`Bork2d.Editor` is the planned JIT/Avalonia tool surface, not in Stage 1 (M5 removed). The
`Bork2d.Engine` scene/time/asset implementation and `Bork2d.Render` are implementation details,
except the tooling-only pack writer
(`Bork2d.Engine.Assets.Pack.PackBuilder`/`PackCompressionMode`). `Bork2d.Bake` contains both the
internal JIT bake implementation and its public builder/processor surface: the game-owned build-time
`MyGame.Assets` executable references it, registers data types explicitly, has no generated host,
and is not part of the runtime app. **Runtime game code** never references the bake tooling.

## Status markers

| Marker | Meaning |
| --- | --- |
| `Current` | Implemented in this tree and covered by code/tests. |
| `Partial` | Declaration/shape exists but behavior is stubbed, unwired, or incomplete. |
| `Empty` | Project or area exists as a scaffold with no implementation. |
| `Planned` | Decided target design, not implemented. |
| `Open` | Decision still required; do not build against it. |
| `Deferred` | Explicitly postponed; the linked document records the questions. |
| `Out of scope` | Not planned for v1. |

`Current` markers describe what the tree implements; `Planned` markers describe the target. The
current milestone spec and the accepted architecture decisions define intended behavior. When code
and documentation disagree, that is a defect: record it and reconcile it in the owning documentation
and the milestone change ([AGENTS.md §10](../AGENTS.md)) rather than assuming either side
automatically wins. The [decision log](decisions.md) records the accepted choices and their status.

## Documentation map

| Document | Audience | Answers |
| --- | --- | --- |
| [Architecture overview](architecture/overview.md) | everyone | Goals, invariants, modes, subsystem fit. |
| [Projects and dependencies](architecture/projects.md) | contributors | Project graph, public/internal boundary, source layout and namespaces, game layout, build. |
| [Runtime lifecycle](architecture/runtime-lifecycle.md) | contributors | Builder, modes, startup, tick loop, pacing, shutdown, errors. |
| [Scene ownership](architecture/scene-ownership.md) | contributors | Root-owned graph, membership, remove/re-add, read-only root. |
| [Threading](architecture/threading.md) | contributors | Thread ownership, locks, snapshot ring, input, memory ordering. |
| [Assets and tooling](architecture/assets-and-tooling.md) | contributors, editor | Pack format, VFS, descriptors, sprites, TOML bake, editor. |
| [Asset processors and `MyGame.Assets`](architecture/asset-processors.md) | contributors, game developers | Processor contract, sidecar policies, builder/no-descriptor/no-host flow, Pong's `CircleProcessor` example. |
| [File-processors implementation spec](specs/file-processors.md) | implementers | Locked scope, public API, semantics, work packages, tests, acceptance criteria. |
| [Pong example spec](specs/pong-example.md) | implementers | Pong game/asset contract, work packages, acceptance criteria, and as-built evidence (§12). |
| [Rendering](architecture/rendering.md) | contributors | Vulkan 1.3, one draw per camera, generated quads, bindless, present. |
| [M6 spec](specs/m6-vulkan-bringup.md) | implementers | Vulkan bring-up scope, threads, windowed/offscreen clear, work packages, answered decisions (§16), acceptance. |
| [M7 spec](specs/m7-frame-handoff.md) | implementers | Frame handoff/pacing scope, ring/capture semantics, headless loop, work packages, owner answers (§16), acceptance. |
| [M8 spec](specs/m8-sprite-renderer.md) | implementers | Sprite renderer scope, owner-approved deltas (§2), per-camera passes, `IAssets`/pages/placeholder, build-time shader tool, 96-byte instances, public stats/capture, work packages, acceptance. |
| [NativeAOT](architecture/nativeaot.md) | contributors | Runtime AOT constraints, generated shapes, local publish gate. |
| [Logging](architecture/logging.md) | contributors | MEL/ZLogger, `IEngine.LoggerFactory`, default format. |
| [API guide](api/guide.md) | game developers | Startup, modes, game loop, layers, cameras, sprites, input, assets. |
| [API reference](api/reference.md) | game developers | Members, defaults, threading, status, errors. |
| [Implementation roadmap](roadmap.md) | planner | M0–M10, exit criteria, risks, open decisions, progress. |
| [M1 record](specs/m1-foundation.md) | implementers | Historical M1 goal, delivery, and acceptance evidence. |
| [M2 record](specs/m2-sdl-platform.md) | implementers | Historical M2 SDL window/input decisions, semantics, and evidence. |
| [M3 record](specs/m3-assets.md) | implementers | Historical M3 asset pack/VFS decisions, delivery, and evidence; D-035 supersessions. |
| [M4 spec](specs/m4-serialization-and-bake.md) | implementers | M4 serialization/bake scope, decisions, acceptance criteria, and as-built evidence (§22). |
| [M4 WP0 report](specs/m4-wp0-spike.md) | implementers | Feasibility findings: shapes, struct roots, `Color`, Tomlyn binder/spans, host/ALC, AOT, XXH3. |
| [Asset-reference refactor record](specs/refactor-asset-references.md) | implementers | Public sprite/texture assets, typed `AssetRef`/`DataRef` links, pack-time reference validation, and the removal of sprite handles/resources. |
| [Project-consolidation spec](specs/refactor-projects.md) | implementers | Five-project engine solution, `Bork2d.Engine`/`Bork2d.Bake` consolidation, examples split, acceptance, and as-built evidence. |
| [Contributing](contributing.md) | contributors | Build, warnings, style, format, docs check, PR checklist. |
| [Decision log](decisions.md) | everyone | Accepted decisions and their status (superseded entries kept as history). |

### Reading paths

- **Writing a game:** [API guide](api/guide.md) → [API reference](api/reference.md) →
  [Assets and tooling](architecture/assets-and-tooling.md).
- **Engine work:** [Overview](architecture/overview.md) → [Projects](architecture/projects.md) →
  [Lifecycle](architecture/runtime-lifecycle.md) → [Scene ownership](architecture/scene-ownership.md)
  → [Threading](architecture/threading.md).
- **Assets/editor/bake:** [Assets and tooling](architecture/assets-and-tooling.md) →
  [Asset processors](architecture/asset-processors.md) →
  [file-processors spec](specs/file-processors.md) →
  [M4 spec](specs/m4-serialization-and-bake.md) → [Roadmap](roadmap.md#5-milestone-detail) M3–M4,
  M6–M8.
- **NativeAOT/server:** [NativeAOT](architecture/nativeaot.md) →
  [Lifecycle modes](architecture/runtime-lifecycle.md#2-modes) →
  [API guide](api/guide.md#12-nativeaot).

## Ownership

One fact has one owner. Update the owner and link to it; do not copy.

| Fact | Owner |
| --- | --- |
| API members, defaults, exceptions | [API reference](api/reference.md) |
| Usage examples | [API guide](api/guide.md) |
| Builder, modes, pacing, shutdown | [Runtime lifecycle](architecture/runtime-lifecycle.md) |
| Scene lifetime and parenting | [Scene ownership](architecture/scene-ownership.md) |
| Threads, locks, snapshots, handoff | [Threading](architecture/threading.md) |
| Project graph and boundaries | [Projects](architecture/projects.md) |
| Source layout and namespaces | [Projects §4](architecture/projects.md#4-source-layout-and-namespaces) |
| Pack, assets, TOML bake, editor | [Assets and tooling](architecture/assets-and-tooling.md) |
| Asset processors, sidecars, builder/no descriptor | [Asset processors](architecture/asset-processors.md) |
| File-processor implementation scope/work packages | [File-processors spec](specs/file-processors.md) |
| Renderer | [Rendering](architecture/rendering.md) |
| AOT constraints | [NativeAOT](architecture/nativeaot.md) |
| Logging | [Logging](architecture/logging.md) |
| Milestone status/sequence | [Roadmap](roadmap.md) |
| Milestone history and evidence | [`docs/specs/`](specs/) |
| Accepted decisions | [Decision log](decisions.md) |
| Build and quality gates | [Contributing](contributing.md) |

## Conventions

- Coordinates: +X right, +Y up; rotation in CCW degrees normalized to `[0, 360)`. Screen/input
  coordinates are top-left pixels.
- Asset paths are canonical lowercase `/` keys without source extensions; they are not filesystem
  paths.
- A custom-data root uses PolyType `[GenerateShape]` and is `partial`; `partial` is PolyType's
  requirement, not a Bork2d attribute or generator. Registration is each data TOML's own
  `[bork2d].type` plus an explicit `RegisterAsset<T>()` in the assets executable; there is no type
  list and no generated host.
- A non-TOML source type is an `IAssetProcessor` registered in `MyGame.Assets`; its sidecar, when
  required, is `<source>.toml` with a `[GenerateShape]` type. Data types are registered explicitly
  with `RegisterAsset<T>()` and their TOML selects one with `[bork2d].type`. There is no generated
  host and no reflection. The builder configures ordered packs: the first is the main/base pack and
  later entries are DLC/overlay packs; a DLC reference may resolve locally or in main only. The M4
  project descriptor (`bork2d.project.toml`) and generated host are removed.
- "Tick thread" means the thread running `IGame.Setup/Tick/Teardown`; the public API has no thread
  affinity.
- Source layout is folder-per-area with the namespace equal to the project plus folder path; IDE0130
  is an error ([Projects §4](architecture/projects.md#4-source-layout-and-namespaces)).
- Status markers change with the code; the roadmap is the source of truth for milestone state.
- `AGENTS.md` is process documentation only; milestone content lives in specs, roadmap, decisions,
  and the PR.

## Maintaining these documents

1. Update the owning document in the same change as the code.
2. Keep status markers and the roadmap progress log accurate.
3. Record public-contract decisions in the [decision log](decisions.md).
4. Keep the API guide/reference in sync with `Bork2d.Engine`.
5. Run `pwsh scripts/check-docs.ps1` after moving files or headings.

## Glossary

| Term | Meaning |
| --- | --- |
| **Tick** | One gameplay update executed by the tick thread. |
| **Tick window** | `Setup`, `Tick` (including joined workers), or `Teardown`; scene mutation is legal only here. |
| **Snapshot** | Immutable copy of cameras and sprites produced after a tick and consumed by the renderer. |
| **Snapshot slot** | One of three reusable snapshot buffers, held until its frame fence. |
| **Frame slot** | One of two GPU frame resources (command/instance buffers, semaphores, fence). |
| **Bounded run-ahead** | The tick runs until the snapshot ring is full, then waits for a GPU fence. |
| **Windowed / offscreen / headless** | Normal present; windowless Vulkan with discarded frames; no SDL/Vulkan/render thread. |
| **Scene root** | `IEngine.Root`; read-only strong owner of the live tree. |
| **Rooted / unrooted** | Reachable / not reachable from the root. |
| **Pack / overlay** | A `.pck` file; a later mounted pack shadows earlier paths by canonical path. |
| **Bake** | The JIT tooling pipeline that turns TOML sources and textures into MessagePack/raw pack entries; never in the runtime. |
| **Bake host (M4, removed by D-038)** | The M4 generated `Bork2d.BakeHost` assembly that statically knew mapped types and called generated bind/pack code. Removed; the builder uses explicit registrations instead. |
| **Project descriptor (M4, removed by D-037)** | `bork2d.project.toml`: M4's ordered `[[assets]]` entries with `root` + `pack`, plus host/texture defaults. Removed; the builder in `MyGame.Assets` replaces it. |
| **Asset processor** | A registered `IAssetProcessor` for a source extension set with a `[GenerateShape]` sidecar when settings are required, a `None`/`Required` policy, and processing code that emits base-path-derived shaped asset instances (`AddAsset<T>`; the engine serializes MessagePack) and optional raw data (`AddData`). |
| **Sidecar** | `<source file>.toml` settings file for a processor source; bound by the shared TOML binder from the processor's `[GenerateShape]` type. `Required` sidecars are created by `CreateDefaultSidecar` when missing; `None` has no sidecar. |
| **`MyGame.Assets`** | Game-owned build-time executable that contains the asset roots/processors, configures the bake with `BakeApplicationBuilder`, acts as the CLI, and bakes when built. Never shipped with the runtime. |
| **Main / DLC pack** | In the builder's ordered pack list, the first pack / every later entry; there is no `main` flag. |
| **Source type metadata** | The reserved top-level `[bork2d] type = "Full.Type.Name"` table in a custom-data TOML (D-M4-13); the root table is binder-ignored metadata. |
| **Pong example** | `examples/pong`: `Pong.Common` (game/simulation/AI/settings), `Pong.Assets` (build-time bake, `CircleProcessor`), `Pong.Desktop` (AOT headless/windowed/offscreen host), and `Pong.Tests`. It is the living game example and the NativeAOT gate; its committed `paddle.png`, `ball.circle`, and TOMLs are game **source assets**, not test/pack binary fixtures. |
| **Structural determinism** | Same entry table/order and byte-identical decompressed payloads for identical inputs; whole-pack byte identity is not guaranteed. |
| **Incremental bake** | Content-hash gate that skips a bake when the builder configuration, registered types, sources, game assembly, processor set/code, tool, and mode are unchanged; a multi-pack project skips only when every output is current. |
| **Texture descriptor** | Public MessagePack metadata (payload `DataRef`, width, height, `TextureFormat` format, `TextureFilter` filter, `TextureWrap` wrap); the three settings are non-null enums (D-040). |
| **Sprite asset** | Public MessagePack quad definition: texture `AssetRef<TextureDescriptor>`, UVs, pivot, topology, optional indices, pixels-per-unit. |
| **Asset reference** | `AssetRef<TAsset>`: a typed canonical path string to a MessagePack asset; `null` is absent. |
| **Data reference** | `DataRef`: a canonical path string to a raw asset; `null` is absent. |
| **Bindless** | Shader indexing of a descriptor array, so all texture pages are available in one draw. |
| **NativeAOT** | Ahead-of-time publish mode; no runtime reflection or dynamic code. |
| **Draw call** | One `vkCmdDraw` issuing all visible sprites of one camera as instances. |
