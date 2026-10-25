# Decision Log

> Format: lightweight ADRs. Each entry records status, context, the decision, consequences,
> and alternatives. Status is `Accepted` (in force), `Proposed` (needs sign-off before the
> relevant milestone), `Superseded`, or `Deferred`.
>
> Date for this pass: **2026-10-25**. Decisions from the architecture and asset discussions
> are marked accordingly; implementation details proposed in the docs are marked `Proposed`.

## D-001 Public surface: Api + Runtime

- **Status:** Accepted
- **Context:** The engine has several projects, but game code should only see contracts and
  startup, not the renderer, SDL, asset internals, or editor.
- **Decision:** Game code references only `Bork2d.Api` and `Bork2d.Runtime`.
  `Bork2d.Common`, `Bork2d.Assets`, and `Bork2d.Render` are implementation details with
  `internal` types shared through `InternalsVisibleTo`. `Bork2d.Editor` is a separate public
  tool surface used by `Project.Editor`, not by game code.
- **Consequences:** Small public API; internal projects can change freely. Runtime must be the
  composition root and must not leak implementation types. The editor has its own public
  builder surface.
- **Alternatives:** Make `Common` public; one assembly; let games reference `Render` directly.
- **Docs:** [Projects and dependencies](architecture/projects.md#3-public-surface-and-assembly-visibility)

## D-002 Thread model: main/tick + render + loaders

- **Status:** Accepted
- **Context:** Ticking and rendering must overlap. Windowed, offscreen, and headless modes have
  different platform needs.
- **Decision:** The main thread owns time, game callbacks, tick scope, and snapshot publication.
  A render thread owns Vulkan in windowed/offscreen modes. Asset loading uses background
  threads. Headless mode has no render thread and no SDL/Vulkan. Game workers may run inside a
  tick and must be joined before it returns.
- **Consequences:** No lock per property; game code is single-threaded per entity. Renderer and
  game share only immutable snapshots. Windowed control needs a channel. Headless/offscreen
  are mode branches, not separate engines.
- **Alternatives:** Three-thread coordinator; render on main; single-thread headless only.
- **Docs:** [Threading](architecture/threading.md#1-threads-by-mode)

## D-003 Bounded run-ahead snapshot ring

- **Status:** Accepted (supersedes the earlier "tick never waits" phrasing)
- **Context:** Fixed 60 Hz pacing is wrong for 120/165 Hz displays. Pacing must follow the
  frame rate without artificial delays, while preventing an unbounded producer.
- **Decision:** Three snapshot slots with states `Free → Writing → Ready → Rendering → Free`.
  A slot is held until the GPU frame fence. The tick can run ahead while slots are free and
  blocks only when all slots are occupied. The renderer always takes the newest `Ready`
  snapshot; older undisplayed snapshots are dropped. Windowed/offscreen have no target tick
  rate, deadline timer, or `Sleep`.
- **Consequences:** Tick rate follows the renderer/GPU frame rate and can burst up to the ring
  depth. Latency and memory are bounded. Headless mode has no ring and uses a timer at its
  configured tick rate (default 60, `0` = uncapped). The ring is the highest-risk concurrency
  code and needs fake-fence stress tests.
- **Alternatives:** Double buffer with backpressure; strict alternation; unbounded queue;
  uncapped tick with dropped frames.
- **Docs:** [Threading](architecture/threading.md#4-triple-buffer-handoff)

## D-004 Variable timestep, no fixed rate

- **Status:** Accepted
- **Context:** The engine must work on 60, 120, 165 Hz and uncapped displays without a fixed
  simulation rate.
- **Decision:** `ITime.DeltaTime` is wall-clock time between tick starts, clamped by a
  configurable max-delta safety value (default 0.25 s). Windowed/offscreen have no target tick
  rate, no accumulator, and no render interpolation in v1. Pacing comes from the bounded
  snapshot ring. Headless uses `WithHeadless(ticksPerSecond)` (default 60, `0` = uncapped) with
  a timer.
- **Consequences:** Simple loop and API; display-rate-independent. Windowed/offscreen frame
  pacing is bounded by the ring, not by a timer; headless pacing is the one intentional timer.
  Games needing deterministic simulation implement their own accumulator. `MaxDeltaTime` is a
  safety clamp, not a delay.
- **Alternatives:** Fixed step + interpolation; fixed step without interpolation; 60 Hz cap.
- **Docs:** [Runtime lifecycle](architecture/runtime-lifecycle.md#5-tick-loop-and-pacing)

## D-005 Windows first

- **Status:** Accepted
- **Context:** Cross-platform support costs CI, windowing quirks, and native packaging.
- **Decision:** v1 targets Windows. SDL/Vulkan boundaries stay portable; no Win32 API leaks into
  `Api`/`Runtime`. Linux/macOS are not supported guarantees in v1. NativeAOT publish is
  Windows x64 first.
- **Consequences:** Faster path; macOS main-thread constraints are not designed in yet. Native
  SDL2/Vulkan deployment is a Windows-first concern.
- **Alternatives:** Windows + Linux; all platforms; Windows-only with no portability effort.
- **Docs:** [Overview](architecture/overview.md#2-goals), [Roadmap](roadmap.md#7-v1-definition-of-done)

## D-006 Layer flags and culling masks

- **Status:** Accepted; 32-layer cap proposed
- **Context:** `TLayer` is a game-defined generic enum used for both renderable layer and
  camera mask.
- **Decision:** `TLayer` is an `int`-backed `[Flags]` enum. A renderable has exactly one
  non-zero bit; a camera has a mask. The engine validates at startup, flattens to `uint` at the
  snapshot boundary, and supports at most 32 layer flags. Layer `0`/composite renderable values
  are invalid and skipped with a diagnostic.
- **Consequences:** Renderer is non-generic and cheap; users declare bits in ascending order.
  Validation catches bad enums at `Build()`.
- **Alternatives:** Single layer + visible set; public `LayerMask`; no flags contract.
- **Docs:** [API guide](api/guide.md#4-layers), [Rendering](architecture/rendering.md#81-sort-order)

## D-007 N cameras, N instanced draws

- **Status:** Accepted
- **Context:** "One draw per camera" needs a precise multi-camera contract.
- **Decision:** Each non-empty camera produces one instanced draw; cameras render in ascending
  entity ID (creation order) into normalized viewports. Empty cameras produce no draw. There is
  no global one-draw-for-all-cameras requirement.
- **Consequences:** Split-screen/HUD work naturally; draw count scales with non-empty cameras,
  not sprites/textures. The invariant is testable.
- **Alternatives:** One active camera; one combined draw; offscreen composite.
- **Docs:** [Rendering](architecture/rendering.md#3-frame-structure)

## D-008 Units, Y-up, half-height camera, per-sprite scale

- **Status:** Accepted; pivot/topology conventions proposed
- **Context:** The engine needs unambiguous coordinates and sprite sizing.
- **Decision:** World units are abstract; +X right, +Y up; radians CCW. `Camera.Size` is the
  **half** visible height; viewport aspect determines width. Each sprite asset carries a
  **pixels-per-unit** `scale`; world size divides by it, and `Transform.LocalScale` multiplies.
  No global PPU. Input is framebuffer pixels, top-left, converted through camera helpers.
  Sprites have no vertices array: the shader generates the `Quad` corners from UVs, texture
  size, scale, and a normalized pivot (default center). `topology` is a
  `Quad`/`TriangleStrip`/`Triangles` enum; `indices` is null for `Quad` and non-null for the
  other topologies.
- **Consequences:** Pixel art and arbitrary unit scales coexist. Camera numbers are Unity-like
  (half-height). Sprite size depends on asset data; unready sprites are skipped. v1 draws
  `Quad`; non-Quad indices are preserved for the indexed path.
- **Alternatives:** Global PPU; Y-down world; full-height Size; pixel-sized world; explicit
  vertices array.
- **Docs:** [Rendering](architecture/rendering.md#7-coordinates-camera-and-projection),
  [Assets](architecture/assets.md#9-sprite-assets)

## D-009 Vulkan 1.3, instancing, bindless, dynamic rendering

- **Status:** Accepted; technical details proposed
- **Context:** Many sprites/texture pages need cheap drawing.
- **Decision:** Vulkan 1.3 + SDL2/Silk.NET. Sprites are instanced four-corner quads generated
  from sprite assets (UVs/scale/pivot/topology) in one pipeline. Texture pages use a descriptor-
  indexed bindless sampler array; instances select the page. Dynamic rendering; no render
  pass/framebuffer for the swapchain. v1: no depth, no MSAA/HDR, no per-sprite materials;
  ordering layer → `ZIndex` → entity ID; straight alpha. Windowed and offscreen modes share the
  pipeline; offscreen discards frames.
- **Consequences:** One draw per camera; no per-sprite descriptors. Device must support
  descriptor indexing/bindless limits. Transparency needs explicit sorting. Variable-topology
  meshes need a future geometry path.
- **Alternatives:** Traditional passes; per-page draws; depth-based ordering; explicit
  per-sprite vertex buffers.
- **Docs:** [Rendering](architecture/rendering.md)

## D-010 Packed assets and virtual filesystem

- **Status:** Accepted
- **Context:** Assets must ship compactly, be loadable in the background, support overlays, and
  avoid filesystem/path ambiguity.
- **Decision:** Runtime assets live in `assets.pck` (magic `BORK2DPK`, 28-byte prefix:
  fixed-prefix `version`, compressed/uncompressed header lengths, XXH3-64 checksum, then a
  Zstd-compressed MessagePack `PackHeader` object, then data). The version is **outside** the
  compressed header so a future loader can select a v1/v2 parser before deserializing.
  `PackHeader` is the v1 shape and contains a `PackEntry[]`; each `PackEntry` is a class with
  path/raw/compressed/offset/length/size, not a positional array. Both are Nerdbank.MessagePack
  generated shapes. `assets.pck` is the base pack; `IAssets.LoadPack` mounts later packs above
  it (later wins, no unload in v1). Browsing is a merged, shadowed-hidden view. The header is
  always compressed and checksummed; asset payloads have no checksum, are uncompressed in
  debug, and use max Zstd in release only when smaller. Paths are lowercase `/` keys without
  source extensions. TOML never ships. A missing or malformed base pack throws at startup; there
  is no empty-FS fallback.
- **Consequences:** Deterministic, bounded loading; overlays for DLC/tests. Format becomes a
  compatibility surface from M3. No unload means memory is load-and-keep in v1. Header
  corruption is detected; raw asset corruption is not. Deployments must include a valid base
  pack.
- **Alternatives:** Loose files; ZIP; one uncompressed blob; first-wins overlays; runtime TOML.
- **Docs:** [Assets and pack files](architecture/assets.md)

## D-011 Avalonia editor, not in-engine UI

- **Status:** Accepted
- **Context:** The editor is a data tool: slicing and TOML editing, no scene editing. The
  runtime has no UI/text renderer.
- **Decision:** `Bork2d.Editor` is an Avalonia library/app; `Project.Editor` is a thin JIT host.
  It shares source asset/serialization logic, not the renderer or game loop. The editor uses
  compile-time builder registrations and may reflect over the game model for the property grid.
- **Consequences:** Rich desktop UI quickly; no engine UI dependency; editor is a second UI
  stack to maintain; no Vulkan preview. Editor is exempt from NativeAOT.
- **Alternatives:** In-engine ImGui; WPF; defer.
- **Docs:** [Editor](architecture/editor.md)

## D-012 TOML authoring, MessagePack baking

- **Status:** Accepted direction; schema details open
- **Context:** Data should be git-friendly while runtime loading is compact and AOT-safe.
- **Decision:** TOML is the source of truth for custom data, sprite definitions, and texture
  sidecar metadata. The JIT baker converts TOML to MessagePack and packs it with raw payloads.
  The NativeAOT runtime reads MessagePack only with
  [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) generated shapes.
  Data references assets by path, never `Sprite.Id`. Editor/baker may use reflection and TOML;
  runtime may not.
- **Consequences:** Readable diffs, compact runtime, one model. Requires a build step,
  validators, source generators, and comment/round-trip decisions. Runtime cannot hot-load TOML.
- **Alternatives:** JSON; binary-only; TOML at runtime; reflection serialization.
- **Docs:** [Data and serialization](architecture/data-and-serialization.md)

## D-013 Game solution layout

- **Status:** Accepted
- **Context:** Separate gameplay/content from host and editor tooling.
- **Decision:** `Project.Common` (game + source assets; references only `Bork2d.Api`),
  `Project.Desktop` (entry point; references `Bork2d.Runtime` + Common), and `Project.Editor`
  (thin host; references `Bork2d.Editor` + Common). Generated projects use compile-time
  registrations and do not scan assemblies.
- **Consequences:** Fast builds, clean gameplay boundary, explicit editor type set. Project
  names are template/generic.
- **Alternatives:** Single project; engine source in game solution; editor inside Desktop.
- **Docs:** [Projects and dependencies](architecture/projects.md#5-game-solution-layout)

## D-014 Pre-1.0 breaking changes

- **Status:** Proposed
- **Context:** The engine is a scaffold with planned API additions.
- **Decision:** Until the v1 API review (M10), public API changes are allowed. Every breaking
  change updates the API guide, reference, and decision log in the same change. After v1,
  breaking changes require a major-version decision.
- **Consequences:** Fast iteration; early adopters must expect churn; docs must mark status.
- **Alternatives:** Semver-stable immediately; no discipline.
- **Docs:** [API reference](api/reference.md)

## D-015 Tick-scoped multi-threaded mutation

- **Status:** Accepted
- **Context:** Gameplay wants many threads inside a tick, joined before it ends, without the
  renderer reading live state.
- **Decision:** All scene mutation happens inside the tick window. Game workers may create and
  mutate **distinct** entities concurrently; the engine serializes structural state and
  captures the snapshot after `Tick` returns. The engine does not serialize concurrent access
  to the same entity/property; the game synchronizes that. Main-only APIs stay main-only.
  Workers join before `Tick` returns.
- **Consequences:** Parallel spawning/per-entity updates are legal and cheap; no per-property
  locks. Same-entity races are the game's responsibility. Registry must stay thread-safe and
  IDs unreused.
- **Alternatives:** Single-writer main-thread mutation; locks on every property; ECS/job system.
- **Docs:** [Threading](architecture/threading.md#2-tick-scoped-concurrency)

## D-016 NativeAOT runtime

- **Status:** Accepted
- **Context:** Game and dedicated-server builds should be AOT-published/trimmed; the editor and
  build tools have different constraints.
- **Decision:** Runtime/game projects (`Api`, `Common`, `Assets`, `Render`, `Runtime`,
  `Project.Desktop`) are NativeAOT- and trim-safe from M1. No runtime reflection, assembly
  scanning, runtime TOML, or dynamic code. Nerdbank.MessagePack shapes are source-generated.
  The editor and baker remain JIT and may use reflection/TOML. `WithGame(instance)` avoids
  reflection-based construction.
- **Consequences:** AOT/trim analyzers and publish smoke are baseline; some libraries may need
  adapters; game constructors can take dependencies; missing generated shapes fail fast.
  Editor stays rich/JIT.
- **Alternatives:** AOT everything; AOT only at release; reflection runtime.
- **Docs:** [NativeAOT](architecture/nativeaot.md)

## D-017 Headless and offscreen modes

- **Status:** Accepted
- **Context:** Dedicated servers should not initialize a GPU/window; tests still need to render
  without a window.
- **Decision:** Two builder modes: `WithHeadless(int ticksPerSecond = 60)` (no
  SDL/Vulkan/render thread; data/assets/tick; timer-paced; `0` = uncapped) and
  `WithOffscreen(w,h)` (Vulkan, no surface/swapchain, internal target, frames discarded, bounded
  run-ahead). Windowed is the default. Modes are mutually exclusive; `IWindow`/`IInput` throw in
  headless/offscreen.
- **Consequences:** Server deployments avoid GPU/window dependencies and get a useful default
  60 Hz tick rate; `0` preserves uncapped runs. CI can exercise Vulkan without a display.
  Offscreen output is not publicly readable in v1; public readback is future work. Headless
  timer pacing is the one intentional artificial delay.
- **Alternatives:** One offscreen headless mode; no-render only; a renderer on/off flag.
- **Docs:** [Runtime lifecycle](architecture/runtime-lifecycle.md#10-headless-and-offscreen-modes)

## D-018 Game instance injection

- **Status:** Accepted
- **Context:** The builder must not assume a parameterless constructor, and NativeAOT cannot
  reflectively construct the game.
- **Decision:** `WithGame(IGame<TLayer> game)` accepts a caller-constructed instance. The
  builder validates non-null and owns the app lifetime; construction/DI belongs to the game
  host. No `new()` constraint or `Activator`.
- **Consequences:** Constructor injection (logging, networking, services) works; AOT-safe;
  one game instance per app. The caller must keep it alive through `Run()`.
- **Alternatives:** `new()`; factory delegate; service container.
- **Docs:** [Runtime lifecycle](architecture/runtime-lifecycle.md#1-public-startup-contract)

## D-019 Texture descriptors and sprite assets

- **Status:** Accepted; exact schemas partly proposed
- **Context:** Raw texture bytes need dimensions/format, and sprites need geometry/UVs/scale.
- **Decision:** Textures use a separate MessagePack descriptor (`textures/<name>`) plus a raw
  payload (`textures/raw/<name>`): width, height, format (`rgba8` now/KTX2 later), filter,
  wrap. Sprites are MessagePack assets with texture path, UVs, a **pixels-per-unit** scale, a
  normalized pivot (default center), a `topology` enum (`Quad`/`TriangleStrip`/`Triangles`), and
  `indices` (`ushort[]?`): null for `Quad`, non-null for the other topologies. There is no
  vertices array; the shader generates the four corners from UVs/texture size/scale/pivot.
  Source is TOML, baked by M4. v1 draws `Quad` only; non-Quad indices are preserved for the
  future indexed path, not a separate mesh asset.
- **Consequences:** Textures can be shared; descriptors carry sampler metadata; KTX2 migration
  is contained. Quad corners are generated in the vertex shader; the indexed path consumes
  `indices` and needs no vertex array.
- **Alternatives:** Metadata in sprite asset; self-describing raw header; KTX2 only.
- **Docs:** [Assets: texture descriptors](architecture/assets.md#8-texture-descriptors),
  [Assets: sprite assets](architecture/assets.md#9-sprite-assets)

## D-020 Compile-time editor registrations

- **Status:** Accepted
- **Context:** Developers extend `Bork2d.Editor` with custom editable types and windows without
  forking it.
- **Decision:** The editor builder accepts compile-time registrations: `WithProject`,
  `WithAssetType<T>()`, `WithWindow<TWindow>()`/instance. No assembly scanning. The editor is
  JIT and may reflect for property metadata, but the editable type/window set is explicit.
- **Consequences:** Deterministic startup, simple tooling, future AOT-editor option. Developers
  must edit their `Project.Editor` host to register new types; no drop-in plugin scanning.
- **Alternatives:** Assembly scanning; source-generator-only registrations; deferred.
- **Docs:** [Editor](architecture/editor.md#3-editor-builder)

## D-021 ZLogger logging surface

- **Status:** Accepted
- **Context:** Engine and game code need structured, low-overhead logging with a good default
  experience; the builder should own configuration.
- **Decision:** Use Microsoft.Extensions.Logging with ZLogger providers. `IEngine` exposes
  `ILoggerFactory`. The builder has `WithLogging(Action<ILoggingBuilder>)`; if omitted, it
  configures a pretty ZLogger console provider. `Bork2d.Api` references
  `Microsoft.Extensions.Logging.Abstractions` only; `Bork2d.Runtime` carries ZLogger. The app
  owns/disposes the default factory; user-supplied configuration uses the same lifetime.
- **Consequences:** Game code can create category loggers from the engine factory; a default
  run is readable without configuration. ZLogger/MEL must be validated for NativeAOT/trimming.
  Logging is thread-safe and available in windowed/offscreen/headless modes.
- **Alternatives:** No logging dependency; custom logger interface; file-only logging.
- **Docs:** [Logging](architecture/logging.md)

## D-022 Warnings-as-errors and C# style gate

- **Status:** Accepted
- **Context:** The codebase should stay uniform and free of warning debt from the start.
- **Decision:** All projects (engine, editor, tests, samples) use `TreatWarningsAsErrors`,
  `EnforceCodeStyleInBuild`, and a Microsoft/.NET-standard `.editorconfig`; relevant IDE style
  rules are `error`. CI runs `dotnet format --verify-no-changes`. AOT/trim analyzers are also
  errors for runtime projects.
- **Consequences:** Style violations fail the build, not just review. Some legacy code will
  need formatting as it lands. Suppressions require a justified `#pragma`/attribute and a
  comment. This applies from M1.
- **Alternatives:** Warnings only; style warnings only in CI; AnalysisMode=All with more noise.
- **Docs:** [Contributing](contributing.md#3-build-style-and-warnings)

## Proposed defaults awaiting sign-off

These are decisions embedded in the docs that were not explicitly chosen. They are safe to
change before the milestone that implements them.

| Topic | Proposed value | Milestone |
| --- | --- | --- |
| Builder method names / `Run` return | `WithGame(instance)`, fluent options, `void Run()` + exceptions | M1 |
| Camera defaults | size 10, full viewport, all layers, black clear | M1/M2 |
| Layer cap | 32, `int`-backed flags, validation at `Build` | M1 |
| Max delta | 0.25 s safety clamp (not a pacing delay) | M2/M7 |
| DPI | physical pixels, no logical scaling | M2 |
| Default pack missing | Accepted: startup error; game throws, no empty FS | M1/M3 |
| Header representation | Fixed-prefix version + `PackHeader`/`PackEntry` classes via Nerdbank.MessagePack | M3 |
| Header checksum | XXH3-64 seed 0 over uncompressed header, uint64 LE | M3 |
| Texture descriptor paths | `textures/<name>` descriptor, `textures/raw/<name>` payload | M3 |
| Zstd API | .NET 11 built-in; adapter if API differs | M3 |
| Frames in flight | 2 GPU frames; 3 snapshot slots | M6/M7 |
| Allocator | Minimal suballocator first; VMA if needed | M6 |
| Sprite geometry | Quad from UVs + texture size / scale; pivot center | M4/M5 |
| TOML library | Tomlyn; preserve comments if practical | M4 |
| Project descriptor | `bork2d.project.toml` with asset/type mapping | M4/M5 |
| Shader compiler | glslangValidator build target; embedded SPIR-V | M8 |
| Bindless pages | 4096 default, hard failure on overflow | M8 |
| Camera order | Entity ID (creation order) | M8 |
| v1 residency | Load-and-keep; unload API later | M3 |
| Editor custom windows | Type/instance registration; no scanning | M5 |
| Offscreen readback | Internal discard in v1 | M7/M10 |
| Logging default | Pretty ZLogger console | M1 |
| Packaging | Project references in template; NuGet later | M9 |
| v1 API freeze | At v1.0 release review | M10 |

## Superseding a decision

Add a new entry with `Status: Accepted`, reference the superseded ID, and update every linked
document in the same change. Do not rewrite history by deleting an old decision; mark it
`Superseded by D-XXX`.

## Index

| ID | Decision | Status |
| --- | --- | --- |
| D-001 | Public surface Api + Runtime | Accepted |
| D-002 | Main/tick, render, loader threads across modes | Accepted |
| D-003 | Bounded run-ahead snapshot ring | Accepted |
| D-004 | Variable timestep, no fixed rate | Accepted |
| D-005 | Windows first | Accepted |
| D-006 | Layer flags and masks | Accepted / 32 cap proposed |
| D-007 | N cameras, N draws | Accepted |
| D-008 | Abstract units, Y-up, half-height camera, per-sprite scale | Accepted / conventions proposed |
| D-009 | Vulkan 1.3 per-sprite instancing/bindless/dynamic rendering | Accepted / details proposed |
| D-010 | Packed assets (assets.pck) and VFS overlays | Accepted |
| D-011 | Avalonia editor, JIT | Accepted |
| D-012 | TOML source, MessagePack bake | Accepted direction / schema open |
| D-013 | Game solution layout | Accepted |
| D-014 | Pre-1.0 breaking changes | Proposed |
| D-015 | Tick-scoped multi-threaded mutation | Accepted |
| D-016 | NativeAOT runtime, JIT editor/tools | Accepted |
| D-017 | Headless and offscreen modes | Accepted |
| D-018 | Game instance injection | Accepted |
| D-019 | Texture descriptors + sprite assets | Accepted / schema partly proposed |
| D-020 | Compile-time editor registrations | Accepted |
| D-021 | ZLogger logging surface | Accepted |
| D-022 | Warnings-as-errors + C# style gate | Accepted |
