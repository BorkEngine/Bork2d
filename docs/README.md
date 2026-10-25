# Bork2d Engine Documentation

> **Status:** **M0 complete** (2026-10-25). The repository still contains a partial scaffold;
> the documentation describes both what exists and the target design. M1 is specified in
> [M1 — Foundation and NativeAOT baseline](specs/m1-foundation.md). Check the status markers
> before treating any API as implemented.

Bork2d is a focused 2D game engine for .NET. It renders with **Vulkan 1.3** and handles
windowing through **SDL2**, bound through **Silk.NET**. Gameplay ticks on the engine tick thread while
rendering runs on a separate thread; each tick publishes an immutable **frame snapshot** that
the renderer consumes. The runtime is **NativeAOT**; assets are packed into **`assets.pck`**
and referenced by opaque IDs. The engine also supports **windowed**, **offscreen**, and
**headless** modes.

Game developers consume exactly two assemblies:

| Assembly | Role |
| --- | --- |
| `Bork2d.Api` | Public contracts and value types: engine, game, camera, sprite, input, assets, logging factory. No GPU, OS, renderer, or third-party types (logging abstractions excepted). |
| `Bork2d.Runtime` | Public startup surface: builder, host, modes. Composes the internal engine, renderer, platform, and asset implementations. |

`Bork2d.Editor` is the public tool surface used by a game's thin `Project.Editor` host. It is
JIT/Avalonia and is not referenced by game code.

Everything else (`Bork2d.Common`, `Bork2d.Render`, `Bork2d.Assets`) is an implementation detail
and may change without notice.

## Status markers

Implementation status uses these markers. The [decision log](decisions.md) additionally uses
`Accepted`, `Proposed`, and `Superseded` for decisions.

| Marker | Meaning |
| --- | --- |
| `Current` | Present in this repository and buildable today. |
| `Partial` | Present but incomplete, stubbed, or known to be wrong. |
| `Empty` | Project or area exists as a scaffold with no implementation. |
| `Planned` | Decided target design; not implemented yet. |
| `Open` | Decision still required. Do not build against it. |
| `Deferred` | Explicitly postponed; the linked document records the raised questions. |
| `Out of scope` | Not planned for the v1 engine. |

When code and docs disagree, code is current truth for `Current` items; the docs plus the
[roadmap](roadmap.md) are the target truth for `Planned` items. The
[decision log](decisions.md) records why.

## Documentation map

| Document | Audience | Answers |
| --- | --- | --- |
| [Architecture overview](architecture/overview.md) | everyone | Goals, invariants, modes, and how the pieces fit. |
| [Projects and dependencies](architecture/projects.md) | contributors | Project graph, public/internal boundary, game solution layout, AOT build. |
| [Runtime lifecycle](architecture/runtime-lifecycle.md) | contributors | Builder, modes, startup, tick loop, pacing, shutdown, errors. |
| [Scene ownership](architecture/scene-ownership.md) | contributors | Root-owned scene graph, parent/child membership, remove/re-add, read-only root. |
| [Threading and frame handoff](architecture/threading.md) | contributors | Thread ownership, bounded run-ahead snapshot ring, input, resize/close, memory ordering. |
| [Assets and pack files](architecture/assets.md) | contributors, editor | `assets.pck` format, compressed/checksummed header, VFS overlays, browsing, texture descriptors, sprite assets, bake pipeline. |
| [Logging](architecture/logging.md) | contributors | ZLogger/Microsoft.Extensions.Logging, `IEngine.LoggerFactory`, builder default. |
| [Editor](architecture/editor.md) | contributors, editor | Avalonia editor builder, slicing, custom types/windows, JIT boundary. |
| [Data and serialization](architecture/data-and-serialization.md) | contributors, editor | TOML source, Nerdbank.MessagePack bake, NativeAOT. |
| [NativeAOT](architecture/nativeaot.md) | contributors | Runtime AOT constraints, source generation, build/CI, editor exemption. |
| [Rendering](architecture/rendering.md) | contributors | Vulkan 1.3, one draw per camera, generated sprite quads, bindless, dynamic rendering, windowed/offscreen. |
| [API guide](api/guide.md) | game developers | Startup, modes, game loop, layers, cameras, sprites, input, assets, logging, AOT. |
| [API reference](api/reference.md) | game developers | Member contracts, defaults, threading, status. |
| [Implementation roadmap](roadmap.md) | planner | Milestones M0–M10, exit criteria, risks, open decisions. |
| [M1 implementation spec](specs/m1-foundation.md) | implementers | M1 work packages, API changes, scene ownership, tests, acceptance. |
| [Contributing](contributing.md) | contributors | Build, warnings-as-errors, C# style, format gate, PR checklist. |
| [Decision log](decisions.md) | everyone | Decisions taken, alternatives, consequences. |

### Suggested reading paths

- **Writing a game:** [API guide](api/guide.md) → [API reference](api/reference.md) →
  [assets](architecture/assets.md).
- **Working on the engine:** [overview](architecture/overview.md) →
  [projects](architecture/projects.md) → [lifecycle](architecture/runtime-lifecycle.md) →
  [scene ownership](architecture/scene-ownership.md) → [threading](architecture/threading.md)
  → [assets](architecture/assets.md) → [rendering](architecture/rendering.md).
- **Starting M1:** [M1 spec](specs/m1-foundation.md) → [scene ownership](architecture/scene-ownership.md)
  → [contributing](contributing.md).
- **Assets/editor:** [assets](architecture/assets.md) →
  [data and serialization](architecture/data-and-serialization.md) →
  [editor](architecture/editor.md) → roadmap M3–M5.
- **NativeAOT/server:** [nativeaot](architecture/nativeaot.md) →
  [runtime modes](architecture/runtime-lifecycle.md#2-modes) →
  [API guide](api/guide.md#12-nativeaot).
- **Contributing:** [contributing](contributing.md) → [logging](architecture/logging.md) →
  [roadmap](roadmap.md).
- **Planning the next pass:** [roadmap](roadmap.md) → [decision log](decisions.md) →
  [threading](architecture/threading.md).

## Decisions at a glance

These are the rules the rest of the documentation assumes. Details/alternatives are in the
[decision log](decisions.md).

| Topic | Decision |
| --- | --- |
| Public surface | Only `Bork2d.Api` and `Bork2d.Runtime` are supported for game code; `Bork2d.Editor` is the tool surface. |
| Runtime | NativeAOT and trim-safe. Editor/baker stay JIT. No runtime reflection, TOML, or assembly scanning. |
| Threads | The engine tick thread runs ticks; game workers may create/mutate distinct entities inside a tick and must join; a render thread owns Vulkan; asset loaders are background. No public API requires the process main thread. |
| Ownership | `IEngine.Root` owns a strong scene graph. Parented objects persist; unparented objects are collectible. Detach to remove, re-attach the same object later. No weak entity registry, no entity `Dispose`/`IsAlive`. |
| Modes | Windowed, offscreen (windowless Vulkan, frames discarded), headless (no SDL/Vulkan/render thread; timer-paced, default 60, 0 = uncapped). |
| Frame pacing | Bounded run-ahead in windowed/offscreen. No target tick rate, deadline timer, or `Sleep` there. Headless uses its configured tick rate. |
| Assets | `assets.pck` (magic/28-byte prefix with fixed-prefix version, compressed `PackHeader`/`PackEntry` classes with XXH3-64 checksum, uint64 offsets); later `LoadPack` overlays win; browse merged; no unload in v1; missing base pack throws. |
| Compression | Header always Zstd-compressed. Debug assets uncompressed; release assets maximum Zstd, stored only when smaller. |
| Textures/sprites | Separate MessagePack texture descriptor + raw RGBA8 payload; sprite MessagePack assets with UVs, pivot, topology, optional indices, and pixels-per-unit scale. TOML source, baked. |
| Layers | `[Flags]` enum. One layer bit per renderable; camera culling mask. 32 flags maximum. |
| Cameras | N cameras → N instanced draws, one per non-empty camera/viewport. |
| World | Abstract units, Y-up, CCW radians. `Camera.Size` = half visible height. Per-sprite pixels-per-unit scale; no global PPU. |
| Renderer | Vulkan 1.3, dynamic rendering, bindless texture pages, instanced generated quads, no depth, one pipeline. |
| Editor | Avalonia, JIT; compile-time builder registrations; source asset slicing/TOML editing; no Vulkan, no scene editing. |
| Logging | Microsoft.Extensions.Logging + ZLogger; `IEngine.LoggerFactory`; pretty console default. |
| Quality | From M1: warnings-as-errors, enforced C# style (`error`), `dotnet format --verify-no-changes`, AOT/trim analyzers. |
| Stability | Pre-1.0: breaking changes allowed until the v1 API review. |

## Repository map

```
Bork2dEngine/
  docs/                      this documentation
    specs/                   implementation specs (M1+)
  examples/                  empty; sample projects are a roadmap item
  src/Bork2d/
    Bork2d.slnx
    Bork2d.Api/              public contracts (Current/Partial)
    Bork2d.Common/           engine state and snapshot building (Partial)
    Bork2d.Runtime/          builder, modes, platform host (Empty)
    Bork2d.Render/           Vulkan renderer (Empty)
    Bork2d.Assets/           pack VFS, loading, bake support (Empty)
  (Bork2d.Editor)            planned; not in the solution yet
```

A game solution has `Project.Common`, `Project.Desktop`, and `Project.Editor`; see
[Projects and dependencies](architecture/projects.md#5-game-solution-layout).

## Building the current tree

```powershell
dotnet build src/Bork2d/Bork2d.slnx
```

Today this builds with .NET SDK `11.0.100-rc.1` and target framework `net11.0`, with zero
warnings/errors. The solution is **not runnable**: no window, renderer, pack, asset system, or
logging stack, and `Engine.Run()` is a placeholder loop. From M1, `Directory.Build.props` and a
root `.editorconfig` add warnings-as-errors and style-as-errors; see
[Contributing](contributing.md).

Finished-engine requirements: a Vulkan 1.3 GPU/driver, SDL2 native binaries, Windows x64 for
the first NativeAOT target.

## Glossary

| Term | Meaning |
| --- | --- |
| **Tick** | One gameplay update executed by the engine tick thread. |
| **Tick window** | The interval in which scene mutation is legal: `Setup`, `Tick` (including joined game workers), or `Teardown`. |
| **Frame** | One rendered image. Ticks and frames are not one-to-one. |
| **Snapshot** | Immutable copy of the scene (cameras + sprite renderables) produced by a tick and consumed by the renderer. |
| **Snapshot slot** | One of three reusable CPU/GPU-lifetime snapshot buffers; held until its frame fence. |
| **Frame slot** | One of two GPU frame resources (command buffer, instance buffer, semaphores, fence). |
| **Bounded run-ahead** | The tick runs until the snapshot ring is full, then waits for a GPU fence; no fixed rate or artificial delay. |
| **Windowed / offscreen / headless** | Normal SDL+Vulkan+present; windowless Vulkan with discarded frames; no SDL/Vulkan/render thread, timer-paced. |
| **Game worker** | Game-owned thread spawned inside a tick; distinct entities; must join before `Tick` returns. |
| **Scene root** | `IEngine.Root`; read-only strong owner of the live transform/camera/sprite tree. `Root.DetachAll()` detaches children. |
| **Rooted / unrooted** | Reachable / not reachable from `Root`. Rooted objects persist; unrooted objects are collectible. |
| **Detach / re-attach** | `Parent = null` removes from the scene without destroying the object; assigning a parent again restores it. |
| **Pack** | `assets.pck` file containing a compressed/checksummed `PackHeader`/`PackEntry` MessagePack header and payloads. |
| **Overlay** | A pack mounted above the base pack; later packs win; browsing hides shadowed paths. |
| **Texture descriptor** | MessagePack texture metadata (payload, width, height, format, filter, wrap). |
| **Sprite asset** | MessagePack quad definition: texture path, UVs, pivot, topology, optional indices, pixels-per-unit scale. |
| **Bindless** | Indexing a descriptor array from the shader, so all texture pages are available in one draw. |
| **ZLogger** | Structured logging provider; the builder's default console configuration. |
| **NativeAOT** | Ahead-of-time publish mode for the runtime/game; no runtime reflection/dynamic code. |
| **Draw call** | Here: one `vkCmdDraw` executing all visible sprites of one camera as instances. |
| **Unit** | Abstract world-space distance. Not a pixel. |
| **In flight** | A submitted GPU frame whose fence has not signalled yet. |

## Conventions

- Code samples are C# against the target API. Members that do not exist yet are `Planned`; some
  samples therefore do not compile against the current scaffold.
- Coordinates: +X right, +Y up, radians CCW. Screen/input coordinates are top-left pixels.
  Sprite UVs and pivot are top-left origin; the renderer generates a Y-up quad from the UV span,
  texture size, pivot, and pixels-per-unit scale.
- "Tick thread" means the thread the engine uses to invoke `IGame.Setup/Tick/Teardown`. The
  public API itself has no thread affinity.
- Asset paths are canonical lowercase `/` keys without source extensions; they are not
  filesystem paths.
- Mermaid diagrams cover dependencies, sequences, and states.
- Status markers are updated when a milestone lands; the roadmap is the source of truth.

## Maintaining these documents

1. Land documentation changes with the code change that invalidates them.
2. Update status markers and the roadmap progress table.
3. Record public-contract decisions in the [decision log](decisions.md), with alternatives and
   consequences.
4. Keep the API guide/reference in sync with `Bork2d.Api`; both are part of the public contract.
5. Keep the editor/NativeAOT boundary explicit: no Avalonia, TOML, or reflection in runtime
   projects.
