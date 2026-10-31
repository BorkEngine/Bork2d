# Architecture Overview

> **Status:** M1–M4 are merged into `develop`; **D-036/D-037/D-038 are merged (2026-10-27, PR #8)**
> and the Pong example (D-043/D-044) is merged into `develop` (2026-10-29, PR #10). M6 (Vulkan
> bring-up) is implemented (2026-10-30; final PR pending human review), so the builder,
> windowed/offscreen Vulkan hosts, headless one-tick host, scene graph, logging, asset pipeline,
> builder-configured bake, and AOT baseline are `Current`. Windowed and offscreen run a real render
> thread that clears an opaque-black sRGB target (windowed presents; offscreen discards and loops
> until `Exit`); frame handoff/pacing (M7) and the sprite renderer (M8) are `Planned`. The
> windowed/offscreen AOT/Pong gate is proven. Status detail is in [the roadmap](../roadmap.md). The engine suite is 1044 tests
> (1028 deterministic + 16 opt-in/conditional).
>
> Source file types are registered asset processors with `[GenerateShape]` sidecars when settings are
> required; the built-in texture importer is one processor. Data types are registered explicitly with
> `RegisterAsset<T>()`; there is no generated host. The project descriptor is removed and the
> game-owned `MyGame.Assets` executable configures/bakes through `BakeApplicationBuilder`. The
> runtime model is unchanged.

## 1. Purpose

Bork2d is a small, opinionated 2D engine, not a general-purpose scene toolkit. It optimises for
three things:

1. **A small public contract.** Game code writes against `Bork2d.Engine` and `Bork2d.Runtime` and
   never sees SDL, Vulkan, Silk.NET, the pack filesystem, or renderer internals.
2. **Predictable frame behavior.** Gameplay is tick-scoped and quiescent before capture;
   rendering consumes immutable snapshots and never touches live game state.
3. **Cheap draws.** Every camera renders its visible sprites with one instanced draw call using
   bindless textures and dynamic rendering.

The design also includes a data-only editor (`Planned`, M5): no scene editing, only texture
slicing and editing of custom serializable classes.

The bake completes the source pipeline: each data TOML declares its CLR type in `[bork2d]`, and the
JIT `Bork2d.Bake`/`MyGame.Assets` flow bakes custom-data TOML and processor sources (anywhere
under each declared pack root) into one main pack plus optional DLC/overlay packs through the
unchanged `PackBuilder`. The runtime still reads MessagePack only, with compiled `[GenerateShape]`
shapes and no TOML or reflection. See [Assets and tooling](assets-and-tooling.md#11-build-pipeline)
and the [file-processors spec](../specs/file-processors.md).

A game-owned `MyGame.Assets` executable contains the asset roots, registers data types explicitly
with `RegisterAsset<T>()`, and registers processors by extension; each processor has a
`[GenerateShape]` sidecar when settings are required and emits base-path-derived shaped asset
instances and optional raw data (the engine serializes MessagePack). The built-in texture importer is
one processor. The builder configures the ordered packs and bakes when the assets project builds;
there is no descriptor and no generated host. See [asset processors](asset-processors.md).

## 2. Goals

- **2D-first, sprite-based rendering** with cameras, layers, z-ordering, tint, and transforms.
- **Vulkan 1.3** renderer with dynamic rendering, descriptor indexing (bindless textures), and
  instancing; no render-pass/framebuffer objects for the swapchain.
- **SDL2** for window creation, input, and Vulkan surface integration.
- **Separate tick and render threads.** While the renderer presents frame N, the game ticks
  frame N+1.
- **Thread-safe scene operations.** The public API has no thread affinity; engine locks
  serialise structural changes and per-object access, and workers join before the tick ends.
- **NativeAOT runtime.** The game/runtime is trim-safe NativeAOT; the editor and build tools
  stay JIT. Serialization uses Nerdbank.MessagePack generated shapes; the game is an instance.
- **Packed, path-addressed assets.** Game code loads `SpriteAsset` like any other public asset and
  assigns it to a scene renderable; there are no opaque sprite handles. Asset-to-asset links are
  typed canonical paths (`AssetRef<TAsset>`/`DataRef`). The runtime reads a main pack plus
  later-wins overlays, loads/decompresses in the background, and skips a sprite whose texture link
  is absent or not ready. A pack holds raw payloads and MessagePack assets with **no per-asset
  version** (only the pack prefix version) and **no policy size caps** (intrinsic `uint32`/`uint64`
  field widths and streaming bounds). TOML is baked by the JIT bake tooling and never parsed at
  runtime; entities have no IDs. The
  pack writer is the public tooling exception in `Bork2d.Engine`; it validates references at pack
  build.
- **Headless and offscreen modes.** Servers run with no window, GPU, renderer, or Vulkan loader;
  headless is one tick today and becomes timer-paced at a configured rate in M7. Offscreen runs a
  real Vulkan render thread against a fixed sRGB target, discards frames, and loops until `Exit`
  (M6); pacing/handoff and readback are M7/M8/M10.
- **Diagnostics and quality.** Microsoft.Extensions.Logging/ZLogger, `IEngine.LoggerFactory`;
  warnings and style violations are build errors.
- **A short, composable startup API** through a builder in `Bork2d.Runtime`.
- **Windows first**, with portable boundaries that do not block Linux/macOS.
- **A clean game layout:** `Project.Common` (game + assets), `Project.Desktop` (host),
  `Project.Editor` (Avalonia editor host).

## 3. Non-goals for v1

These are out of scope until the roadmap says otherwise:

- 3D rendering, HDR rendering, MSAA, post-processing.
- A full ECS, scene-graph editing, prefabs, or a scene editor.
- Physics, collision, audio, networking, and in-game UI.
- Fixed-timestep simulation with render interpolation; v1 uses a variable timestep.
- Hot reload of assets or shaders (a later optimization).
- Multi-window hosting; one app process with one windowed, offscreen, or headless mode.
- Linux/macOS as supported v1 targets. The abstractions must not block them.
- A visual shader/material system, per-sprite custom materials, or indexed non-Quad sprite
  drawing in v1 (the asset schema carries `indices`; the draw path is later).
- Persisting loaded asset instances or opaque handles. Assets are referenced by stable paths in
  data.

## 4. System context

```mermaid
flowchart TB
  PD["Project.Desktop"] --> RT["Bork2d.Runtime"]
  PD --> PC["Project.Common"]
  PE["Project.Editor"] --> ED["Bork2d.Editor"]
  PC --> EN["Bork2d.Engine"]
  RT --> EN
  RT --> R["Bork2d.Render"]
```

The developer-visible dependency chain is short: `Project.Common` references **only**
`Bork2d.Engine` (plus BCL/System.Numerics); `Project.Desktop` references **only** `Bork2d.Runtime`
(plus `Project.Common`); `Project.Editor` references `Bork2d.Editor` plus `Project.Common`.
Game code never references an engine project directly. Exact rules: [Projects](projects.md).

## 5. Runtime topology

The engine invokes game callbacks on its tick thread, which may be any host thread; there is no
process-main-thread affinity. Windowed mode owns one OS/window thread (SDL, Vulkan instance/surface,
window commands, event pump) and one render thread (device, swapchain, clear, present) started by the
host; the game loop runs on the caller's tick thread. Offscreen mode owns the same render thread but
no OS/window thread; headless owns neither. Every mode owns one app-scoped asset loader thread.
The host starts the renderer without waiting for readiness, runs `Setup` immediately, and waits for
readiness only before the first tick (D-M6-06). Windowed/offscreen render frames are continuous and
decoupled from ticks (D-M6-01); the snapshot ring and handoff are M7. No thread touches another
thread's domain (SDL, Vulkan, or game objects).

| Mode | Tick | OS/window thread | Render thread | SDL | Vulkan | Pacing (current) |
| --- | :-: | :-: | :-: | :-: | :-: | --- |
| Windowed | yes | yes | yes | yes | yes | uncapped ticks; continuous present (M7 handoff) |
| Offscreen | yes | no | yes | no | yes | uncapped ticks; GPU-fence-paced discard (M7 handoff) |
| Headless | yes | no | no | no | no | one tick (timer-paced M7) |

All three modes are selected and validated by the builder. Windowed and offscreen run the real
renderer and the tick loop until `Exit`/close/fatal; the window is visible from `Run()` start and
destroyed before `Run()` returns. Headless keeps the one-tick M1 sequence. In the target design the
tick publishes an immutable snapshot into a three-slot ring that the render thread consumes, and the
tick may wait only when the ring is full; headless has no ring and is timer-paced. The state machine
is in [Threading and frame handoff](threading.md).

## 6. Invariants

These rules bind the implementation and every future change. Violating one is an architecture
bug, not a style preference.

1. **Only `Bork2d.Engine` and `Bork2d.Runtime` are public surfaces for game code**; implementation types may change or disappear between releases. The one exception is the **tooling-only** pack writer (`PackBuilder`/`PackCompressionMode`) in `Bork2d.Engine.Assets.Pack` (D-001, D-M3-04, D-034). The public contracts are grouped by area namespace (`Bork2d.Engine.Scene`, `.Rendering`, `.Input`, `.Platform`, `.Assets`); see [Projects §4](projects.md#4-source-layout-and-namespaces).
2. **`Bork2d.Engine` contains no GPU, OS, or renderer types**; the public contract uses BCL types, `Microsoft.Extensions.Logging.Abstractions` for `ILoggerFactory`, and `PolyType` 1.4.1 for the `IShapeable<T>` shape constraint (D-M3-22). The merged pack implementation also uses Nerdbank.MessagePack and `System.IO.Hashing`; the public signatures must not expose them (the dependency test enforces this).
3. **Scene mutation is tick-scoped and quiescent at capture**: mutation happens only during `Setup`/`Tick`/`Teardown`, workers join before `Tick` returns, structural and per-object access use `System.Threading.Lock` (composite updates still need game synchronization), and the tree is unordered.
4. **Snapshots are immutable after publication**: the render thread never reads a live entity, mutates snapshot memory, or blocks the tick on a specific frame.
5. **Snapshots carry immutable asset data, not live entities**: sprite renderables hold a loaded
   `SpriteAsset?` (or `null`), whose texture reference is a canonical path resolved at draw time.
   The renderer skips absent/not-ready links; there is no opaque sprite handle or global registry.
6. **A camera draws its visible sprites with at most one instanced draw call**; empty cameras draw none, and the vertex shader generates four corners from UVs, size, and pivot (the asset stores no vertices).
7. **Layers are single bits, culling masks combinations**: layer `0` or a multi-bit layer is invalid and skipped with a debug diagnostic.
8. **World units are abstract and Y-up; rotation is CCW degrees normalized to `[0, 360)`**: `Camera.Size` is half the visible height, and sprite assets carry UVs, a pivot, a topology, optional indices, and a pixels-per-unit scale.
9. **There is no depth buffer in v1**: draw order is layer then `ZIndex`, ties are unspecified with no entity-ID tie-breaker (D-026), and `ClearKind.Depth` is reserved.
10. **At most one window and one app process in v1**: modes are windowed, offscreen, or headless; headless has no window, GPU, renderer, or Vulkan loader and stores its tick rate (default 60, `0` = uncapped; pacing arrives in M7).
11. **No steady-state per-frame allocations** in the tick snapshot builder or renderer hot path.
12. **The runtime is NativeAOT- and trim-safe**: no runtime reflection, assembly scanning, runtime TOML, or dynamic code; MessagePack shapes are source-generated.
13. **Packs are the runtime asset source**: TOML and source images live in the game-owned
`MyGame.Assets` project, only baked MessagePack/raw assets ship, the builder's first pack is main
and later packs are DLC/overlay, overlays are packs (later wins), and a missing main pack throws at
`Run()` unless the app calls `WithoutAssets()`. There is no per-asset version and no policy size cap.
Source file types are registered processors (D-036).
14. **Logging is available everywhere through `IEngine.LoggerFactory`**, configured with ZLogger; the default is a plain-text console provider.
15. **Warnings and C# style violations are build errors**, with a format gate in CI.
16. **Pre-1.0 breaking changes are allowed**, but every public change updates the API guide, reference, and decision log in the same change.
17. **Scene objects are root-owned**: `IEngine.Root` strongly owns its tree, `Root.DetachAll()` detaches direct children, `ISceneRoot` is read-only, and there is no entity `Dispose`/`IsAlive` (detach with `Parent = null`, re-attach later). See [Scene ownership](scene-ownership.md).
18. **Headless never calls SDL or the Vulkan loader**, creates no instance/device/platform/renderer, and remains one-tick.
19. **Windowed and offscreen require Vulkan 1.3**: the OS/window thread owns the windowed instance/surface; the render thread owns the device, swapchain, offscreen targets, frames, and recording. No renderer thread outlives `Run()`.
20. **`Setup` runs before renderer readiness and is never skipped because the renderer failed**; no `Tick` runs before readiness (D-M6-06/D-M6-14).
21. **Every frame starts with an opaque-black clear and has no depth**; M6 has no snapshot, camera, draw, pacing, or readback.

## 7. Read next

- [Projects and dependencies](projects.md) — reference graph and game solution layout.
- [Runtime lifecycle](runtime-lifecycle.md) — builder, modes, startup, shutdown.
- [Scene ownership](scene-ownership.md) — root-owned lifetime.
- [Threading and frame handoff](threading.md) — bounded run-ahead and the snapshot ring.
- [Assets and tooling](assets-and-tooling.md) — `assets.pck`, overlays, sprites, editor, bake.
- [Asset processors and `MyGame.Assets`](asset-processors.md) — target processor contract,
  sidecars, builder/no descriptor, and Pong's `CircleProcessor` example.
- [Pong example](../specs/pong-example.md) — the living game and AOT gate.
- [Rendering](rendering.md) — windowed/offscreen Vulkan and the one-draw contract.
- [Logging](logging.md) — ZLogger and `IEngine.LoggerFactory`.
- [NativeAOT](nativeaot.md) — runtime constraints and source generation.
- [Contributing](../contributing.md) — build, warnings, style.
- [Implementation roadmap](../roadmap.md) — the build order and status.
