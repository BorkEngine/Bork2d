# Implementation Roadmap

> **Status:** proposed sequence as of 2026-10-25. It is dependency-ordered, not a calendar
> schedule. Each milestone has an exit criterion; a milestone is done when the criterion is
> demonstrated, not when the code is merged.

## 1. How to read this roadmap

- The order is fixed: foundation and platform first, then **assets → serialization/bake →
  editor**, then **Vulkan bring-up → frame handoff → sprite renderer**, then template and
  hardening.
- **Size** is a rough T-shirt estimate, not a promise. **Risk** reflects architectural
  uncertainty, not effort.
- Exit criteria are testable. "It runs on my machine" is not a criterion.
- NativeAOT, no artificial pacing delays, and the pack format are baseline constraints, not
  late optimisations.
- No dates are assigned. Derive them from the sizes and the team if needed.

## 2. Current baseline

| Area | State | Notes |
| --- | --- | --- |
| Build | Green | SDK `11.0.100-rc.1`, `net11.0`, 0 warnings/errors. |
| `Bork2d.Api` | Partial | Interfaces/value types; no builder lifecycle, pack API, or input exposure. |
| `Bork2d.Common` | Partial | Placeholder loop, weak-ref entity list, stub transform globals, camera/sprite impl. |
| `Bork2d.Runtime` | Empty | No builder, modes, SDL, or host. |
| `Bork2d.Render` | Empty | No Vulkan/Silk.NET. |
| `Bork2d.Assets` | Empty | No pack, VFS, descriptors, or sprite assets. |
| `Bork2d.Editor` | Absent | Not in the solution. |
| NativeAOT | None | No publish settings/analyzers/CI. |
| Tests | None | No test project or CI. |
| Docs | This pass | Assets/pack, editor, NativeAOT, pacing, modes, API, roadmap, decisions. |
| Runnable engine | No | No window, renderer, pack, or asset system. |

## 3. Sequencing principles

1. **Assets and editor before the renderer.** The pack format, VFS, sprite/texture assets, TOML
   bake, and editor must exist first so the renderer is built against real content, not a
   throwaway asset stub.
2. **NativeAOT from M1.** Enable analyzers and an AOT publish smoke immediately; do not defer
   trim/AOT problems to release.
3. **No artificial pacing.** No target tick rate, deadline timer, or `Sleep`; bounded
   run-ahead is the only tick/backpressure mechanism.
4. **Keep the pack format stable.** Additive changes only within `version = 1`; format changes
   require a version bump and a reader update.
5. **Keep the build green.** Every milestone compiles, tests, and AOT-publishes the smoke app.
6. **One draw per non-empty camera is a testable invariant** from M8 onward.
7. **Headless is a first-class mode.** Asset/data tests and CI servers must run without SDL or
   Vulkan.
8. **Do not expand scope.** Audio/UI/physics/animation require a decision-log entry and a
   roadmap edit.

## 4. Milestone summary

| ID | Milestone | Outcome | Depends | Size | Risk |
| --- | --- | --- | --- | --- | --- |
| M0 | Documentation and decisions | Shared target, no code | — | S | Low |
| M1 | Foundation + NativeAOT baseline | Clean project graph, builder contract, AOT smoke | M0 | M | Medium |
| M2 | SDL windowed platform | Window/input/window ops and temporary tick loop | M1 | M | Medium |
| M3 | Assets: pack + VFS + descriptors | `assets.pck`, overlays, browse, raw textures, sprite assets | M2 | L | High |
| M4 | Serialization + bake | TOML → MessagePack → pack, release compression | M3 | M | Medium |
| M5 | Editor | Avalonia builder, slicing, TOML editing, bake integration | M3/M4 | L | Medium |
| M6 | Vulkan bring-up | Windowed and offscreen device/clear/validation | M5 | M | High |
| M7 | Frame handoff + pacing | Bounded run-ahead, latest snapshot, headless/offscreen modes | M6 | L | High |
| M8 | Sprite renderer | Real sprite assets, one draw per camera, 165 Hz target | M7 | L | High |
| M9 | Template, samples, packaging | New game solution + AOT publish + editor host | M8 | S | Low |
| M10 | Hardening + v1 | Stability, performance, API review, release | all | L | Medium |

`examples/` stays empty until M9; `docs/` is updated at every milestone.

## 5. Milestones

### M0 — Documentation and decisions

**Goal:** agree on the target before writing engine code.

Scope:

- architecture, projects, lifecycle, threading, assets/pack, editor, NativeAOT, rendering,
  serialization, API guide/reference, roadmap, decision log;
- explicitly record deferred work and open format details;
- surface decisions before they block M1–M8.

Exit criteria:

- the documentation set is reviewed and internally consistent;
- the decision log matches the docs;
- M1-facing open questions have owners/defaults;
- the roadmap is accepted as the working sequence.

Status: **in review** (this pass).

### M1 — Foundation and NativeAOT baseline

**Goal:** project graph, public surface, builder contract, and AOT tooling.

Scope:

- add references per the dependency matrix (`Runtime → Api/Common/Assets/Render`,
  `Assets → Api/Common`, `Render → Api/Common/Assets`, `Editor → Api/Common/Assets`);
- make `Common`/`Assets`/`Render` types `internal` by default with `InternalsVisibleTo`;
- add `Directory.Build.props`, analyzers, nullable, **warnings-as-errors**,
  **EnforceCodeStyleInBuild**, root `.editorconfig`, and `global.json`;
- add the `dotnet format --verify-no-changes` CI gate and Microsoft/.NET-standard style rules
  at `error`; apply to all projects (engine, editor, tests, samples);
- enable AOT/trim analyzers on runtime projects; add a sample AOT publish smoke app;
- add `Bork2d.Tests` and a dependency test for the reference matrix;
- add `Bork2dAppBuilder<TLayer>`/`Bork2dApp` skeleton:
  - `WithGame(IGame<TLayer> game)` instance injection (no `new()`/reflection);
  - `WithHeadless(int ticksPerSecond = 60)`, `WithOffscreen(w,h)`, `WithAssetsPack(path)`;
  - `WithLogging(Action<ILoggingBuilder>)` with a pretty ZLogger default;
  - window options, `WithMaxDeltaTime`, `WithValidation`;
  - no target tick rate/Sleep for windowed/offscreen;
- add `IEngine.Input`, `IEngine.Exit`, `IEngine.LoggerFactory`, `IGame.Teardown`,
  `Bork2dException`, `AssetInfo`;
- reference Microsoft.Extensions.Logging.Abstractions from `Api`; ZLogger/MEL from `Runtime`;
- establish Nerdbank.MessagePack shape generation/analyzer policy for AOT;
- replace weak-ref entity ownership with strong ownership + `Dispose`, non-reused IDs, and a
  thread-safe tick-scoped registry;
- implement/evaluate transform hierarchy math and world read-only properties;
- define/validate layers (one bit, 32 max), camera defaults (black/all layers), sprite defaults;
- remove placeholder `Class1` files.

Exit criteria:

- solution builds with warnings-as-errors, AOT/trim analyzers, and style violations as errors;
- `dotnet format --verify-no-changes` passes;
- dependency test passes;
- an AOT-published smoke app builds/starts without reflection or trim warnings (the full
  headless pack/tick smoke lands in M7);
- unit tests cover layer validation, entity IDs/disposal, concurrent distinct-entity
  creation/mutation, transform math, builder validation, and pack path rules;
- pretty ZLogger console logging is visible by default and `IEngine.LoggerFactory` is usable;
- no implementation type is unintentionally public.

Size: M. Risk: medium (public API churn, AOT/Silk.NET toolchain).

### M2 — SDL windowed platform

**Goal:** a window that ticks, reads input, and exits cleanly — no Vulkan yet.

Scope:

- add `Silk.NET.SDL` and the SDL2 native runtime; document distribution;
- create a Vulkan-capable window; show/hide policy (`M6` adds show-after-first-present);
- SDL event pumping, close/resize/fullscreen/cursor, focus loss;
- input state: physical scancodes, mouse buttons/position/delta/wheel, text, edge accumulation;
- window-only `IWindow`/`IInput`; headless/offscreen access throws;
- wire `StopwatchTime`, `IEngine.Exit`, `Run`/`Dispose`;
- temporary tick loop without a fixed rate: until the snapshot ring lands (M7), the loop is
  uncapped. This is a milestone-only state, not a pacing feature.
- tests for input edge aggregation and time clamping.

Exit criteria:

- sample opens a window, shows a title, moves/prints input, exits via close/Escape;
- input edge semantics match the reference (fast tap, focus loss, repeats);
- no Vulkan device is created;
- no `Sleep`/target-rate pacing exists in the loop;
- clean shutdown with no lingering threads/SDL resources.

Size: M. Risk: medium (SDL native packaging, DPI, temporary uncapped loop).

### M3 — Assets: pack, VFS, descriptors

**Goal:** the runtime can load, overlay, browse, and describe packed assets without a renderer.

Scope:

- implement the `assets.pck` reader/writer from
  [Assets and pack files](architecture/assets.md):
  - magic `BORK2DPK`, 28-byte prefix, compressed/uncompressed header lengths, XXH3-64 checksum;
  - fixed-prefix `version` selects the header parser (v1 `PackHeader`);
  - always-Zstd-compressed MessagePack `PackHeader`/`PackEntry` classes (Nerdbank shapes);
  - `System.IO.Hashing` package for `XxHash3`;
  - uint64 offsets/lengths/sizes, little-endian binary fields;
  - path validation, duplicate/overlap checks, bounds/checksum validation;
  - missing/malformed base pack throws at startup;
- virtual filesystem: default `assets.pck`, `LoadPack`, later-wins overlays, no unload;
- `Browse`/`Exists` with merged shadowed view;
- compression: .NET 11 Zstd; header always compressed; debug assets uncompressed; release
  assets max only if smaller; asset payloads have no checksum;
- asset loader threads, immutable resource table, readiness/skipping;
- texture descriptor assets + raw RGBA8 payloads (`textures/<name>`,
  `textures/raw/<name>`);
- sprite asset schema (texture, UVs, pixels-per-unit scale, pivot, topology, optional
  `indices`) with Nerdbank generated shapes; v1 renderer is `Quad` only; no vertices array;
- `IAssets.LoadData`/`Exists`/`Browse`; `LoadAsset<T>` for built-in MessagePack types;
- pack builder that can pack pre-baked MessagePack assets and raw payloads;
- headless tests: create packs with raw textures, descriptors, sprites, data; overlay and
  browse; corrupt-pack/header-checksum/missing-pack failures; AOT smoke.

Exit criteria:

- a pack with a raw texture, texture descriptor, sprite asset, and data asset loads;
- later pack overlays shadow earlier paths; browsing hides shadowed entries;
- header is compressed, checksummed, and deserializes to `PackHeader`/`PackEntry`;
- a corrupt checksum or malformed header is rejected;
- a missing base pack throws at startup (no empty FS);
- debug assets are uncompressed; release assets compress only when smaller;
- corrupted magic/version/offset/length/path/checksum cases fail safely;
- loader runs on background threads and the resource table is race-free under stress;
- AOT headless app can load/browse without reflection or GPU.

Size: L. Risk: high (format correctness, overlays, AOT, compression API).

### M4 — Serialization and bake

**Goal:** TOML source becomes MessagePack assets and a packed release artifact.

Scope:

- TOML model/attributes and Nerdbank.MessagePack generated shapes (NativeAOT);
- TOML parser/writer (Tomlyn proposed) for the JIT baker/editor;
- custom data TOML → MessagePack;
- texture sidecar (`<file>.toml`) → MessagePack descriptor + raw RGBA8 payload;
- sprite TOML → MessagePack sprite asset (UVs/scale/pivot/topology/optional indices, no
  vertices);
- asset baker: validation, image import, Nerdbank serialization, deterministic output;
- pack builder integration: `assets.pck`, compressed header + XXH3-64, release max Zstd,
  debug asset payloads uncompressed;
- project descriptor (`bork2d.project.toml`) for asset root/type mapping;
- build command / MSBuild target; incremental bake;
- tests: round-trip, missing fields, bad references, deterministic bake, AOT load, header
  compression/checksum.

Exit criteria:

- a source asset tree bakes to MessagePack and packs into `assets.pck`;
- header is compressed and XXH3-64 verified; debug and release differ only in asset payload
  compression;
- invalid TOML/missing asset/typo fails with file/line diagnostics;
- runtime (AOT headless) loads the baked data with Nerdbank generated shapes only;
- no TOML parser or reflection serializer in runtime projects;
- a custom data class round-trips through TOML → MessagePack → runtime.

Size: M. Risk: medium (source generators, TOML edge cases, build integration).

### M5 — Editor

**Goal:** a developer-extensible Avalonia editor for source slicing and data.

Scope:

- `Bork2d.Editor` project (Avalonia, JIT) with `Bork2dEditorBuilder`:
  - `WithProject`, `WithAssetType<T>()`, `WithWindow<TWindow>()`/instance;
  - compile-time registrations; no assembly scanning;
- `Project.Editor` thin host template referencing `Bork2d.Editor` + `Project.Common`;
- asset browser over `Project.Common/Assets`;
- texture viewer: zoom/pan, manual (and optionally grid) slicing, pivot, pixels-per-unit
  scale, topology (Quad), filter/wrap, validation;
- writes sprite TOML (UVs/scale/pivot/topology/optional indices) and texture sidecar import
  settings; previews source slices with Avalonia imaging;
- custom data editing: property grid + raw TOML, validation, save;
- bake/pack invocation through M4 code; diagnostics panel;
- no Vulkan, no scene editing, no play mode;
- tests for slicing math, metadata round-trip, validation, builder registrations.

Exit criteria:

- open a project, slice a texture, save TOML, bake/pack, and inspect the resulting pack;
- edit a custom class and produce valid TOML;
- invalid data (missing texture, out-of-bounds slice, bad reference) is caught before bake;
- custom window and custom asset type appear through the builder;
- editor runs on Windows without a Vulkan device; template host builds;
- no reflection-based assembly scanning.

Size: L. Risk: medium (UI scope, descriptor/type mapping, comments).

### M6 — Vulkan bring-up (windowed + offscreen)

**Goal:** a Vulkan 1.3 render thread that clears the window or an offscreen target.

Scope:

- add `Silk.NET.Vulkan`; choose allocator (VMA vs minimal);
- validation layers/debug messenger; device/queue/feature logging;
- windowed: `VkInstance`/`VkSurfaceKHR` on the main thread from SDL, render thread device/
  swapchain/dynamic-rendering clear;
- offscreen: no surface/swapchain; fixed-extent offscreen color target; same clear path;
- headless: no Vulkan/SDL initialization;
- resize/fullscreen/minimize and swapchain recreation (windowed); offscreen fixed extent;
- SPSC control queue, fatal-error channel, joinable render thread;
- per-frame synchronization, present modes (FIFO default), device wait on recreation/shutdown;
- AOT publish with windowed/offscreen smoke;
- validation-clean shutdown.

Exit criteria:

- windowed clear color reaches the window; resize/fullscreen/minimize do not hang/leak;
- offscreen clears its target and discards frames; no surface/swapchain is created;
- headless does not initialize SDL/Vulkan (validated by logs/API trace);
- validation layers report zero errors through startup/frame/resize/shutdown;
- AOT-published windowed/offscreen builds run on a GPU runner.

Size: M. Risk: high (WSI threading, device features, swapchain/target lifetime).

### M7 — Frame handoff and pacing

**Goal:** bounded run-ahead snapshot ring for windowed/offscreen, plus timer-paced headless and
offscreen modes.

Scope:

- three-slot snapshot ring with atomic `Free/Writing/Ready/Rendering`;
- slots held until the GPU frame fence; tick waits only when the ring is full;
- render takes newest `Ready`; older snapshots dropped; `latest` cleared on take;
- no target tick rate, deadline timer, or `Sleep` in windowed/offscreen; `MaxDeltaTime` safety
  clamp only;
- headless tick loop with no snapshot ring, timer-paced by `ticksPerSecond` (default 60,
  `0` = uncapped);
- tick capture after `Tick` (workers joined), layer flattening, immutable snapshots;
- windowed/offscreen render loop; headless loop without SDL/Vulkan;
- frames in flight 2; GPU frame slots/fences; instance/command resources;
- resize/close/error handling across modes; shutdown with in-flight frames;
- stress tests with fake fences, slot invariants, bounded-run-ahead pacing tests, headless
  timer tests;
- no per-frame allocations in the handoff path.

Exit criteria:

- tick/render overlap; tick runs ahead at most the ring depth, then waits;
- no `Sleep`/fixed-rate code in windowed/offscreen loops;
- newest snapshot wins; older undisplayed snapshots are dropped;
- headless mode ticks without SDL/Vulkan, has no render thread, and honors its configured rate
  (`0` uncapped);
- offscreen mode renders/discards frames and releases slots on fences;
- 10k-frame stress run has no deadlock/slot leak; shutdown during publish/take is clean;
- an AOT-published headless app loads a pack, ticks at its configured rate, and exits without
  SDL/Vulkan;
- windowed resize/close remains responsive.

Size: L. Risk: high (state machine, memory ordering, cross-mode lifecycle).

### M8 — Sprite renderer with real assets

**Goal:** render packed sprite assets with one instanced draw per camera.

Scope:

- sprite pipeline using real MessagePack sprite assets (UVs, pixels-per-unit scale, pivot,
  `Quad` topology; no vertices array);
- texture descriptors + raw RGBA8 payloads → bindless texture pages;
- instance SSBO carries UVs, world-unit size, pivot, world transform, tint, texture index;
- vertex shader generates the four corners from UVs/size/pivot; no vertex array in the asset;
- camera projection (Size half-height, Y-up), viewport/aspect, screen/world helpers;
- layers, z-order, tint, transforms, generated quads, no depth buffer;
- one dynamic rendering pass, one draw per non-empty camera; clear/overlap semantics;
- multi-camera/split-screen; offscreen rendering; windowed present;
- debug counters, validation, labels, capture; golden/screenshot tests;
- validate `indices`; v1 draws `Quad`; non-Quad assets are preserved for the indexed path;
- performance benchmark on the 165 Hz test machine; no artificial cap;
- AOT publish windowed/offscreen.

Exit criteria:

- a scene from `assets.pck` renders with exactly one draw per non-empty camera;
- layers, z-order, tint, split-screen, camera Size/viewport, clear semantics match docs;
- 50,000 sprites at 1920×1080 sustain the measured target (165 Hz goal, no artificial cap);
- no steady-state per-frame allocations in snapshot/render paths;
- validation clean; offscreen output matches windowed output for the same scene;
- asset-not-ready sprites are skipped, never blocked on.

Size: L. Risk: high (bindless limits, per-sprite instances, projection/sorting, performance).

### M9 — Template, samples, packaging

**Goal:** a new game can be created, packed, and AOT-published.

Scope:

- solution template: `Project.Common`, `Project.Desktop`, `Project.Editor`,
  `bork2d.project.toml`, assets;
- `dotnet new bork2d` or documented copy; CI template build;
- sample game: sprites, layers, camera, input, data, pack overlays, editor slicing;
- headless server sample (`WithHeadless(60)`) and offscreen smoke sample;
- NativeAOT publish profile for Desktop, native SDL2/Vulkan deployment;
- docs samples compile; API guide/reference match actual surface;
- packaging decision (project references vs NuGet) and versioning.

Exit criteria:

- from a clean machine with SDK + Vulkan 1.3, template builds and runs;
- game code references only `Bork2d.Api`/`Bork2d.Runtime`;
- release pack is compressed; debug pack is uncompressed; both load;
- AOT-published desktop and headless server start and shut down cleanly;
- editor host opens the sample project and slices a texture.

Size: S/M. Risk: low/medium (native deployment, template drift).

### M10 — Hardening and v1

**Goal:** stability, performance, and a supportable public contract.

Scope:

- performance: tick cost, pack/decompression, snapshot copy, sorting, instance build, GPU;
- device loss, resize stress, alt-tab, multi-monitor/DPI, minimize/restore;
- pack compatibility/corruption tests, overlay stress, missing-pack behaviour;
- memory: page budgets, leaks, allocation-free steady state, shutdown under load;
- AOT/trimming matrix and clean-machine native deployment;
- validation matrix, CI (build/tests; GPU tests where possible), flaky-test quarantine;
- API review and freeze for v1; docs final pass; release notes.

Exit criteria: see the v1 definition below.

Size: L (ongoing). Risk: medium.

## 6. Cross-cutting work

| Area | Policy |
| --- | --- |
| Documentation | Update architecture/API docs in the same change as a contract; add a decision entry for public changes. |
| Tests | Unit tests every milestone; pack/format tests from M3; AOT smoke from M1; handoff stress from M7; GPU/visual tests from M8. |
| CI | Build + unit + AOT publish; GPU/validation on a Vulkan 1.3 runner; template build from M9. |
| NativeAOT | Analyzers and publish smoke from M1; no reflection/dynamic code/TOML in runtime; Nerdbank.MessagePack shapes. |
| Diagnostics | ZLogger/Microsoft.Extensions.Logging, `IEngine.LoggerFactory`, per-frame counters, validation layers, debug labels; no silent failures. |
| Assets | Pack format is versioned; header compressed/checksummed; additive changes within v1; deterministic builds; debug/release asset compression modes. |
| Quality | Warnings-as-errors, C# style rules as errors, `dotnet format --verify-no-changes`, AOT/trim analyzers; all projects. |
| Scope | Audio/UI/physics/animation require a decision-log entry and roadmap change. |

## 7. v1 definition of done

**Engine/runtime**

- Generated game solution builds/runs on Windows 10/11 with a Vulkan 1.3 driver.
- NativeAOT-published Desktop and headless server start, run, and shut down cleanly.
- Windowed, offscreen, and headless modes behave as documented. Headless is timer-paced
  (`WithHeadless`, default 60, `0` = uncapped).
- Window/input/window ops, variable timestep with bounded run-ahead; no artificial delays in
  windowed/offscreen.
- Tick/render threads with three-slot snapshot ring; no unbounded latency; clean shutdown.
- Tick-scoped workers for distinct entities; same-entity synchronization documented.
- ZLogger/Microsoft.Extensions.Logging is configured by the builder, `IEngine.LoggerFactory` is
  exposed, and the default console output is pretty.

**Assets**

- `assets.pck` with magic, 28-byte prefix (fixed version, compressed/uncompressed header
  lengths, XXH3-64 checksum), compressed `PackHeader`/`PackEntry` classes, uint64 offsets.
- Header always compressed/checksummed; debug asset payloads uncompressed; release payloads
  Zstd-compressed only when smaller.
- Missing/malformed base pack throws at startup; no empty FS.
- Overlays: later `LoadPack` wins; `Browse`/`Exists` merged view; no unload in v1.
- Texture descriptors (width/height/format/filter/wrap) + raw RGBA8 payloads; source sidecars
  carry import settings.
- Sprite assets (texture, UVs, pixels-per-unit scale, pivot, topology, optional `indices`)
  baked from TOML; v1 `Quad` only; no vertices array.
- TOML source → MessagePack → pack pipeline; runtime reads MessagePack only.
- Missing/not-ready assets are skipped with diagnostics; no placeholders.
- NativeAOT serialization uses Nerdbank.MessagePack generated shapes only.

**Editor**

- Avalonia `Bork2d.Editor` with builder; `Project.Editor` host.
- Open source project, browse assets, slice textures, set pivot/scale/topology/filter, save TOML.
- Property grid + raw TOML for registered custom types; validation.
- Bake/pack invocation; custom windows/types via compile-time registrations.
- No Vulkan, scene editing, or assembly scanning; editor stays JIT.

**Rendering**

- Vulkan 1.3 dynamic rendering, bindless texture pages, instanced generated quads.
- One draw per non-empty camera; layers/z/tint/transform/cameras/clear semantics.
- Windowed present and offscreen discard paths; no depth buffer.
- Validation layers zero errors; performance target measured at 165 Hz without artificial cap.

**Process**

- Architecture, threading, assets, editor, NativeAOT, logging, rendering, API docs and decision
  log match the implementation.
- CI builds, unit tests, AOT publish, style/format gates, and template checks pass.
- Warnings and C# style violations are errors in every project.
- Public API reviewed/frozen for v1; breaking changes afterward need a major-version decision.

**Explicit non-goals for v1**

3D, HDR, MSAA, post-processing, physics, audio, in-game UI, networking, scene editor, hot
reload, multi-window, Linux/macOS guarantees, indexed non-Quad sprite drawing in v1 (the schema
carries indices), asset/pack unloading, and public offscreen readback.

## 8. Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| NativeAOT vs Silk.NET/native dependencies | High | AOT smoke from M1; pin versions; isolate incompatible paths. |
| Pack format/overlay bugs | High | Versioned reader, bounds/duplicate tests, corrupt-pack fuzzing, AOT tests. |
| Frame pacing races/deadlocks | High | Three-slot state machine, fake-fence stress, slot invariants, no sleeps. |
| Assets/editor before renderer hides visual issues | Medium | Kinematic asset tests, editor previews, integrate real pack in M8 early. |
| .NET 11 built-in Zstd API differs | Medium | Isolate codec behind reader/writer; keep flags/format stable. |
| Pack header checksum/compression | High | XXH3-64 over uncompressed header; corrupt-pack tests; format versioning. |
| TOML/source-gen mismatch | Medium | One model, Nerdbank shape validation, round-trip tests. |
| ZLogger/MEL AOT compatibility | Medium | AOT publish smoke; isolate incompatible providers behind `ILoggerFactory`. |
| Vulkan WSI/offscreen lifetime | High | Split windowed/offscreen paths; validation; GPU runner. |
| Bindless limits on target GPUs | Medium | Startup feature validation; configurable page capacity; explicit failure. |
| Editor scope grows | Medium | No scene editing; slicing/data only; builder registrations. |
| Native library deployment | Medium | RID packaging, clean-machine tests, documented redistributables. |
| Scope creep (audio/UI/physics) | Medium | Decision + roadmap edit required; v1 non-goals explicit. |
| Documentation drift | Medium | Same-change docs rule; status markers; per-milestone update. |

## 9. Open decisions

| Decision | Needed by | Default if unresolved |
| --- | --- | --- |
| Builder names/signatures, `Run` return shape | M1 | Use the reference proposal. |
| SDK pin/roll-forward; globalization | M1 | Pin current SDK; decide globalization in M1. |
| Nerdbank.MessagePack shape setup | M3/M4 | `[GenerateShape]` roots; no reflection resolver. |
| Zstd built-in API shape | M3 | Adapter behind pack reader/writer; keep format. |
| Texture descriptor/payload paths | M3 | `textures/<name>` + `textures/raw/<name>`. |
| Texture sidecar naming/keys | M4 | `<file>.toml` sidecar; strip `.toml` + source extension. |
| Sprite geometry | M4/M5 | Quad from UVs + texture size / scale; pivot center; `Quad` only. |
| TOML library/comment preservation | M4 | Tomlyn; preserve comments if practical. |
| Project descriptor/type mapping | M4/M5 | `bork2d.project.toml`; define schema. |
| Editor custom-window construction | M5 | Compile-time type/instance registration. |
| Offscreen readback | M8/M10 | Internal discard in v1; public later. |
| Allocator | M6 | Minimal suballocator first; VMA if needed. |
| Shader compiler/build integration | M8 | glslangValidator in a build target; embedded SPIR-V. |
| Bindless capacity/overflow | M8 | 4096 pages; hard failure on overflow. |
| Camera order | M8 | Entity ID (creation order). |
| Frustum culling | M8 | Layer mask only in v1. |
| Headless timer accuracy | M7 | Default 60; `0` uncapped; decide fractional rates later. |
| ZLogger default format | M1 | Pretty console; exact colors/timestamp options open. |
| Style rule set | M1 | Microsoft/.NET standard; list enforced IDE rules in `.editorconfig`. |
| Packaging | M9 | Project references in template; NuGet later. |
| Public API freeze | M10 | Freeze at v1.0. |

## 10. Immediate next actions

1. Review the revised documentation and sign off on the pack/editor/pacing/logging/style
   decisions.
2. Start M1: project graph, builder contract (`WithGame(instance)`, modes, pack, logging),
   API lifecycle, warnings/style rules, AOT analyzers/smoke, tests.
3. Prototype the pack reader/writer against the exact header layout in M3; validate the
   .NET 11 Zstd and `System.IO.Hashing.XxHash3` APIs early.
4. Keep the editor JIT boundary intact; do not pull Avalonia/TOML into runtime projects.
5. Add the AOT publish smoke, dependency test, format gate, and no-warnings rule in M1.

## 11. Progress log

| Milestone | Status | Evidence |
| --- | --- | --- |
| M0 Documentation | In review | `docs/` set and decision log. |
| M1 Foundation/AOT | Not started | — |
| M2 SDL platform | Not started | — |
| M3 Assets/pack | Not started | — |
| M4 Serialization/bake | Not started | — |
| M5 Editor | Not started | — |
| M6 Vulkan bring-up | Not started | — |
| M7 Frame handoff/pacing | Not started | — |
| M8 Sprite renderer | Not started | — |
| M9 Template/samples | Not started | — |
| M10 Hardening/v1 | Not started | — |

## 12. Related documents

- [Architecture overview](architecture/overview.md) — invariants that shape the milestones.
- [Assets and pack files](architecture/assets.md) — M3/M4 format and VFS.
- [Logging](architecture/logging.md) — M1 logging surface.
- [Editor](architecture/editor.md) — M5.
- [Data and serialization](architecture/data-and-serialization.md) — M4.
- [NativeAOT](architecture/nativeaot.md) — M1 baseline, M9 gate.
- [Contributing](contributing.md) — M1 warnings/style gate.
- [Threading](architecture/threading.md) — M7 design/tests.
- [Rendering](architecture/rendering.md) — M6/M8 design.
- [Decision log](decisions.md) — why the sequence looks like this.
