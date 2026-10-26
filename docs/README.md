# Bork2d Documentation

> **Status:** **M0 complete; M1 merged into `develop` (2026-10-25, PR #1); M2 implemented on
> `m-2/common` (WP1–WP5) and pending PR review.** The repository contains the M1 build/quality
> baseline, public contracts, root-owned scene tree, builder, logging, the NativeAOT smoke app, and
> the M2 SDL windowed host (window, input, time, uncapped loop). Assets, the editor, and the
> renderer are target designs for later milestones. Check status markers before treating an API as
> implemented.

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
`Bork2d.Render` are implementation details.

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

Code is current truth for `Current` items; docs plus the roadmap are target truth for `Planned`
items. The [decision log](decisions.md) records why.

## Documentation map

| Document | Audience | Answers |
| --- | --- | --- |
| [Architecture overview](architecture/overview.md) | everyone | Goals, invariants, modes, subsystem fit. |
| [Projects and dependencies](architecture/projects.md) | contributors | Project graph, public/internal boundary, game layout, build. |
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
| [M1 spec](specs/m1-foundation.md) | implementers | Historical M1 goal, decisions, and acceptance evidence. |
| [M2 spec](specs/m2-sdl-platform.md) | implementers | Implemented M2 SDL window/input decisions, semantics, work packages, evidence, acceptance. |
| [Contributing](contributing.md) | contributors | Build, warnings, style, format, docs check, PR checklist. |
| [Decision log](decisions.md) | everyone | Accepted decisions and consequences. |

### Reading paths

- **Writing a game:** [API guide](api/guide.md) → [API reference](api/reference.md) →
  [Assets and tooling](architecture/assets-and-tooling.md).
- **Engine work:** [Overview](architecture/overview.md) → [Projects](architecture/projects.md) →
  [Lifecycle](architecture/runtime-lifecycle.md) → [Scene ownership](architecture/scene-ownership.md)
  → [Threading](architecture/threading.md).
- **Assets/editor:** [Assets and tooling](architecture/assets-and-tooling.md) →
  [Roadmap](roadmap.md#5-milestone-detail) M3–M5.
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
| Pack, assets, TOML bake, editor | [Assets and tooling](architecture/assets-and-tooling.md) |
| Renderer | [Rendering](architecture/rendering.md) |
| AOT constraints | [NativeAOT](architecture/nativeaot.md) |
| Logging | [Logging](architecture/logging.md) |
| Milestone status/sequence | [Roadmap](roadmap.md) |
| Accepted decisions | [Decision log](decisions.md) |
| Build and quality gates | [Contributing](contributing.md) |

## Conventions

- Coordinates: +X right, +Y up; rotation in CCW degrees normalized to `[0, 360)`. Screen/input
  coordinates are top-left pixels.
- Asset paths are canonical lowercase `/` keys without source extensions; they are not filesystem
  paths.
- "Tick thread" means the thread running `IGame.Setup/Tick/Teardown`; the public API has no thread
  affinity.
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
| **Pack / overlay** | `assets.pck` file; a later mounted pack that shadows earlier paths. |
| **Texture descriptor** | MessagePack metadata (payload, width, height, format, filter, wrap). |
| **Sprite asset** | MessagePack quad definition: texture, UVs, pivot, topology, optional indices, pixels-per-unit. |
| **Bindless** | Shader indexing of a descriptor array, so all texture pages are available in one draw. |
| **NativeAOT** | Ahead-of-time publish mode; no runtime reflection or dynamic code. |
| **Draw call** | One `vkCmdDraw` issuing all visible sprites of one camera as instances. |
