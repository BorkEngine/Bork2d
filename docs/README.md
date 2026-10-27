# Bork2d Documentation

> **Status:** **M0 complete; M1 merged into `develop` (2026-10-25, PR #1); M2 merged into `develop`
> (2026-10-26, PR #2); M3 (assets: pack, VFS, descriptors) merged into `develop` (2026-10-26,
> PR #4); the post-M3 asset-reference refactor (D-035) is merged (PR #6); **M4 (serialization and
> bake) is merged into `develop` (2026-10-27, PR #7), including D-M4-13 (per-source
> `[bork2d].type`, location-free data/textures, `-raw` payload paths, and the committed fixture
> source PNG) and D-M4-14/D-M4-15 (ordered multi-pack `[[assets]]`, main-only cross-pack references,
> retained texture sidecars).** The repository contains the
> M1 build/quality baseline, public contracts, root-owned
> scene tree, builder, logging, the NativeAOT smoke app, the M2 SDL windowed host (window, input,
> time, uncapped loop), the M3 asset pipeline (pack reader/writer, VFS, loader, public
> `SpriteAsset`/`TextureDescriptor`, typed `AssetRef<T>`/`DataRef` links, runtime integration), and
> the M4 TOML→MessagePack bake tooling (descriptor, generated host, texture import, reference
> validation, incremental bake, MSBuild targets, AOT baked-pack proof). The editor and renderer are
> target designs for later milestones. Check status markers before treating an API as implemented.

Bork2d is a focused 2D game engine for .NET: **Vulkan 1.3** rendering, **SDL2** windowing through
**Silk.NET**, a NativeAOT runtime, and **`assets.pck`** assets addressed by canonical paths.
Gameplay ticks on the tick thread; rendering consumes immutable snapshots on a separate thread.
Windowed, offscreen, and headless are the target modes.

Game code consumes exactly two assemblies:

| Assembly | Role |
| --- | --- |
| `Bork2d.Api` | Public contracts and value types: engine, game, camera, sprite, input, assets, logging factory. No GPU/OS/Silk types. |
| `Bork2d.Runtime` | Public startup surface: builder, app host, modes. Composes the internal engine and later platform/render/asset implementations. |

`Bork2d.Editor` is the planned JIT/Avalonia tool surface (M5). `Bork2d.Common`, `Bork2d.Assets`, and
`Bork2d.Render` are implementation details, except the tooling-only pack writer
(`Bork2d.Assets.Pack.PackBuilder`/`PackCompressionMode`). The M4 bake tooling (`Bork2d.Bake`,
`Bork2d.Bake.Cli`, generated host) is internal JIT tooling driven by the MSBuild targets; game code
never references it.

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
| [Rendering](architecture/rendering.md) | contributors | Vulkan 1.3, one draw per camera, generated quads, bindless, present. |
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
| [Contributing](contributing.md) | contributors | Build, warnings, style, format, docs check, PR checklist. |
| [Decision log](decisions.md) | everyone | Accepted decisions and their status (superseded entries kept as history). |

### Reading paths

- **Writing a game:** [API guide](api/guide.md) → [API reference](api/reference.md) →
  [Assets and tooling](architecture/assets-and-tooling.md).
- **Engine work:** [Overview](architecture/overview.md) → [Projects](architecture/projects.md) →
  [Lifecycle](architecture/runtime-lifecycle.md) → [Scene ownership](architecture/scene-ownership.md)
  → [Threading](architecture/threading.md).
- **Assets/editor/bake:** [Assets and tooling](architecture/assets-and-tooling.md) →
  [M4 spec](specs/m4-serialization-and-bake.md) → [Roadmap](roadmap.md#5-milestone-detail) M3–M5.
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
  `[bork2d].type`; the descriptor declares no type list.
- A project descriptor declares ordered `[[assets]]` entries: the first is the main/base pack and
  later entries are DLC/overlay packs; a DLC reference may resolve locally or in main only.
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
4. Keep the API guide/reference in sync with `Bork2d.Api`.
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
| **Bake host** | The single generated `Bork2d.BakeHost` assembly that statically knows the mapped types and calls generated bind/pack code; not a solution project. |
| **Project descriptor** | `bork2d.project.toml`: ordered `[[assets]]` entries with `root` + `pack` (first is main, later entries are DLC), plus the host and texture defaults; it declares no types (each data TOML carries `[bork2d].type`). |
| **Main / DLC pack** | The first `[[assets]]` entry / every later entry; there is no `main` flag. |
| **Source type metadata** | The reserved top-level `[bork2d] type = "Full.Type.Name"` table in a custom-data TOML (D-M4-13); the root table is binder-ignored metadata. |
| **Bake fixture** | `Bork2d.BakeFixture`: the game-side `Project.Common`-like data types plus `Assets/` and `DLC1/` trees used by the bake and AOT proofs; its committed `Assets/art/player.png` is a fixture **source asset** that the baker imports, not a test/pack binary fixture. |
| **Structural determinism** | Same entry table/order and byte-identical decompressed payloads for identical inputs; whole-pack byte identity is not guaranteed. |
| **Incremental bake** | Content-hash gate that skips host generation/bake when the descriptor, sources, game assembly, tool, and mode are unchanged; a multi-pack project skips only when every output is current. |
| **Texture descriptor** | Public MessagePack metadata (payload `DataRef`, width, height, format, filter, wrap). |
| **Sprite asset** | Public MessagePack quad definition: texture `AssetRef<TextureDescriptor>`, UVs, pivot, topology, optional indices, pixels-per-unit. |
| **Asset reference** | `AssetRef<TAsset>`: a typed canonical path string to a MessagePack asset; `null` is absent. |
| **Data reference** | `DataRef`: a canonical path string to a raw asset; `null` is absent. |
| **Bindless** | Shader indexing of a descriptor array, so all texture pages are available in one draw. |
| **NativeAOT** | Ahead-of-time publish mode; no runtime reflection or dynamic code. |
| **Draw call** | One `vkCmdDraw` issuing all visible sprites of one camera as instances. |
