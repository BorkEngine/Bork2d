# Implementation Roadmap

> **Status:** M0 complete; **M1 merged into `develop` (2026-10-25, PR #1)**; **M2 (SDL windowed
> platform) merged into `develop` (2026-10-26, PR #2)**. **M3 (assets: pack, VFS, descriptors)
> merged into `develop` (2026-10-26, PR #4)**. The sequence is
> dependency-ordered. A milestone is done when its exit criterion is demonstrated, not merely when
> code is merged.

## 1. How to read this roadmap

- The order is fixed: foundation and platform first, then **assets → bake → editor**, then
  **Vulkan → frame handoff → sprite renderer**, then template and hardening.
- Each milestone lists 3–5 scope bullets and testable exit criteria. Status is tracked in the
  progress log; details live in the owning architecture document and the milestone PR.
- No dates are assigned; sizes are rough estimates, not promises.

## 2. Current baseline

| Area | State | Notes |
| --- | --- | --- |
| Build | Green | SDK `11.0.100-rc.1`, `net11.0`, zero warnings/errors in Debug and Release; `dotnet format` clean. |
| `Bork2d.Api` | Current | Contracts for engine, game, time, window, input, assets, camera, sprite, transform, root, `ZIndex`, `Bork2dException`, `AssetInfo`, `AssetException`/`AssetErrorKind`, the full `KeyCode` scancode set, and five `MouseButton` members. Window/input are implemented for windowed mode; assets are implemented in M3. |
| `Bork2d.Common` | Partial | Root-owned unordered scene tree, transforms (degrees normalized), structural/per-object locks, time wrapper, layer validation, MessagePack shape policy. Snapshot structs are M7. |
| `Bork2d.Runtime` | Current (M3) | Builder, validation, app-owned logging, SDL windowed host (window/input/time, uncapped loop), one-tick headless/offscreen host, `Exit`/`Dispose`/post-shutdown contract, and the app-scoped asset-service lifecycle. Renderer wiring is M6/M8. |
| `Bork2d.Assets` | Current (M3) | v1 pack reader/writer, public `Bork2d.Assets.Pack.PackBuilder`/`PackCompressionMode`, canonical paths, base/overlay VFS, one loader thread with `LoadData`/`LoadAsset<T>`/`LoadSprite` CPU metadata, and the CPU resource table. |
| `Bork2d.Render` | Empty | Reference-wired; no Vulkan implementation (M6/M8). |
| `Bork2d.Editor` | Absent | Not in the solution (M5). |
| `Bork2d.Tests` | Current | 588 xUnit tests: 579 deterministic in CI (ownership/GC, root, transforms, camera, builder, logging, concurrency, dependency matrix, fake-SDL window/input/loop/time, pack/VFS/loader/descriptor/sprite, runtime asset lifecycle) plus 9 opt-in real-window tests. |
| NativeAOT | M3 baseline | Libraries use `IsAotCompatible` + trim/AOT analyzers; `Bork2d.AotSmoke` publishes/runs locally with zero warnings, headless with a temporary pack and `--windowed --ticks 120`. Local gate only. |
| CI | Current | GitHub Actions on PRs to `develop` and pushes to `develop`: restore, format verify, build, test. No AOT or real-window job. |
| Runnable engine | Partial (windowed shell + assets) | A real window, input, time, and an uncapped tick loop plus the pack/VFS/loader/CPU descriptor pipeline; no renderer yet. |

## 3. Sequencing principles

- NativeAOT, no artificial pacing in windowed/offscreen, and the pack format are baseline
  constraints, not late optimizations.
- Exit criteria are testable; "it runs on my machine" is not a criterion.
- Keep the build green; every milestone compiles, tests, and passes the local AOT gate.
- One draw per non-empty camera is a testable invariant from M8.
- Headless is a first-class mode: asset/data tests and servers must run without SDL or Vulkan.
- Scope changes (audio/UI/physics/animation) need a decision-log entry and a roadmap edit.

## 4. Milestone summary

| ID | Milestone | Outcome | Depends | Size | Risk |
| --- | --- | --- | --- | --- | --- |
| M0 | Documentation and decisions | Shared target, no code | — | S | Low |
| M1 | Foundation + NativeAOT baseline | Project graph, contracts, scene tree, builder, AOT smoke | M0 | M | Medium |
| M2 | SDL windowed platform | Window/input/window ops and temporary tick loop | M1 | M | Medium |
| M3 | Assets: pack + VFS + descriptors | `assets.pck`, overlays, browse, textures, sprite assets | M2 | L | High |
| M4 | Serialization + bake | TOML → MessagePack → pack, release compression | M3 | M | Medium |
| M5 | Editor | Avalonia builder, slicing, TOML editing, bake integration | M3/M4 | L | Medium |
| M6 | Vulkan bring-up | Windowed and offscreen device/clear/validation | M5 | M | High |
| M7 | Frame handoff + pacing | Bounded run-ahead, latest snapshot, headless/offscreen loops | M6 | L | High |
| M8 | Sprite renderer | Real sprite assets, one draw per camera | M7 | L | High |
| M9 | Template, samples, packaging | Game solution, samples, AOT desktop/server | M8 | S/M | Low/Medium |
| M10 | Hardening + v1 | Stability, performance, API review, release | all | L | Medium |

`examples/` stays empty until M9.

## 5. Milestone detail

### M0 — Documentation and decisions

**Status:** complete (2026-10-25). Architecture, pack, editor, AOT, API, roadmap, and decision
docs agreed; deferred work recorded. Exit criteria: reviewed docs, consistent decision log,
M1-facing questions resolved, roadmap accepted.

### M1 — Foundation and NativeAOT baseline

**Status:** merged into `develop` (2026-10-25, PR #1).

Goal: project graph, public surface, builder contract, root-owned scene graph, logging, quality
gate, and AOT smoke. Evidence: 135 tests, Debug/Release zero warnings, format clean,
`Bork2d.AotSmoke` publishes/runs locally with zero AOT/trim warnings. Full record:
[M1 spec](specs/m1-foundation.md).

### M2 — SDL windowed platform

**Status:** merged into `develop` (2026-10-26, PR #2). Full record:
[M2 spec](specs/m2-sdl-platform.md).

Goal: a window that ticks, reads input, and exits cleanly; no Vulkan yet.

- Add `Silk.NET.SDL` 2.23.0 and the SDL2 native runtime; document distribution and the 2.30.8
  runtime version the shipped `SDL2.dll` reports.
- Create a Vulkan-capable window that is visible at `Run()` start, with event pumping and
  close/resize/fullscreen/cursor/focus handling.
- Input: physical scancodes, five mouse buttons, position/delta/wheel, UTF-16 text, edge
  accumulation, focus loss/regain.
- Window-only `IWindow`/`IInput` and advanced `ITime`; headless/offscreen stay one-tick.
- `IEngine.Exit`, `Dispose`, close, and post-shutdown contracts; uncapped loop until M7 supplies
  backpressure.
- Tests: 292 total (283 deterministic, 9 opt-in real-window), including the two real-window
  `Run()` exit paths.

Evidence: Debug/Release builds with zero warnings; 292 tests (283 in CI, 292 with
`BORK2D_RUN_WINDOW_TESTS=1`); format/docs/`git diff --check` clean; AOT publish zero warnings with
`SDL2.dll`; headless AOT run exit 0 with the M1 assertions; windowed AOT
`--windowed --ticks 120` exit 0; a 5,000,000-tick run with an observed live window
(`MainWindowTitle = "Bork2d M2 Demo"`) exit 0; interactive Escape/close and WASD movement verified.

Exit: sample opens a window, shows a title, prints/moves on input, exits via close/Escape; edge
semantics match the reference; no Vulkan device; no `Sleep`/target rate; clean shutdown.

### M3 — Assets: pack, VFS, descriptors

**Status:** merged into `develop` (2026-10-26, PR #4); all five WPs complete. Full
record and acceptance evidence: [M3 spec](specs/m3-assets.md).

Goal: load, overlay, browse, and describe packed assets without a renderer.

- `assets.pck` reader/writer per [Assets and tooling](architecture/assets-and-tooling.md): magic,
  28-byte prefix, compressed/XXH3-64 header, `uint64` offsets, `uint32` length/size,
  `PackHeader`/`PackEntry` integer-keyed shapes, validation, missing/malformed base pack throws.
  No per-asset schema version (pack version only) and no policy size caps (intrinsic/streaming
  bounds).
- Base pack opened in `Run()` before the window and `Setup`; `WithoutAssets()` opts out; relative
  filesystem paths resolve against the process current directory.
- VFS: default `assets.pck`, `LoadPack`, later-wins overlays, no unload, merged `Browse`/`Exists`.
- Compression: header always Zstd max; debug payloads uncompressed; release max Zstd only when
  smaller; no payload checksum; no byte-level determinism.
- One asset loader thread, FIFO queue, internal CPU resource table (no public readiness API in
  M3); cancellation and join at shutdown.
- Texture descriptors + raw RGBA8 payloads; sprite asset schema (texture, UVs, pixels-per-unit
  scale, pivot, topology, optional `indices`; no vertices array). M3 loads sprite + descriptor,
  not the raw payload.
- `IAssets.LoadData`/`Exists`/`Browse`/`LoadPack`; shape-generic `LoadAsset<T>` (M3 tests use
  internal shapes; game types can satisfy the compiled `IShapeable<T>` constraint, with no M4
  dependency); public tooling `Bork2d.Assets.Pack.PackBuilder` in `Bork2d.Assets`.
- Headless tests: overlays, browse, corrupt header/checksum, missing pack, loader threads, AOT smoke.

Exit: a pack with texture/sprite/data loads; overlays shadow correctly; corrupt and missing packs
fail safely; loader is race-free under stress; AOT headless can load/browse without reflection/GPU.

**Evidence (WP5):** Debug/Release builds with zero warnings/errors; 587 tests (578 deterministic +
9 opt-in real-window) pass; `dotnet format`/docs/`git diff --check` clean; NativeAOT publish has
zero AOT/trim warnings with `SDL2.dll` present; the AOT default run exits `0` after building,
browsing, and loading a 2-entry temporary pack (one raw asset, one generated-shape MessagePack
asset) and one `Setup`/`Tick`/`Teardown`; `--windowed --ticks 120` exits `0` with `WithoutAssets()`;
the runtime tests cover the base-pack-before-window order, empty VFS, overlays, the platform/
`Setup`/`Tick`+`Teardown`/fatal error matrix, post-shutdown `ObjectDisposedException`, logging, and
`Dispose()` before/after `Run()`. Detailed commands/results are in
[M3 spec §12.3](specs/m3-assets.md#123-acceptance-evidence-recorded-by-wp5).

### M4 — Serialization and bake

Goal: TOML source becomes MessagePack assets and a packed release artifact.

- TOML model/attributes and Nerdbank generated shapes (NativeAOT).
- Tomlyn parser/writer for the JIT baker/editor; comment preservation if practical.
- Custom data TOML → MessagePack; texture sidecars → descriptor + raw payload; sprite TOML → sprite
  asset; deterministic baker and pack integration.
- Project descriptor (`bork2d.project.toml`) for asset root/type mapping; build command/MSBuild
  target with incremental bake.
- Tests: round-trip, missing fields, bad references, deterministic output, AOT load.

Exit: a source tree bakes and packs; header is compressed/checksummed; invalid source fails with
file/line diagnostics; runtime loads with generated shapes only; no TOML/reflection in runtime.

### M5 — Editor

Goal: a developer-extensible Avalonia editor for source slicing and data.

- `Bork2d.Editor` (JIT) with `Bork2dEditorBuilder`: `WithProject`, `WithAssetType<T>()`,
  `WithWindow<T>()`/instance; compile-time registrations, no scanning.
- `Project.Editor` thin host; asset browser over `Project.Common/Assets`.
- Texture viewer: zoom/pan, manual (optionally grid) slicing, pivot, pixels-per-unit, topology,
  filter/wrap, validation; writes sprite TOML/sidecars; Avalonia imaging preview.
- Custom data: property grid + raw TOML, validation, save; bake/pack invocation and diagnostics.
- No Vulkan, scene editing, or play mode.

Exit: open project → slice → save → bake/pack → inspect pack; edit custom data to valid TOML;
invalid data caught before bake; custom types/windows registered; runs without a GPU.

### M6 — Vulkan bring-up (windowed + offscreen)

Goal: a Vulkan 1.3 render thread that clears the window or an offscreen target.

- Add `Silk.NET.Vulkan`; choose allocator (minimal vs VMA).
- Validation layers/debug messenger; device/queue/feature logging.
- Windowed: instance/surface on the OS/window thread; render thread owns device/swapchain/dynamic
  rendering clear. Offscreen: no surface, fixed-extent target, same clear path. Headless: no
  SDL/Vulkan.
- Resize/fullscreen/minimize and swapchain recreation; SPSC control queue and fatal-error channel;
  joinable render thread; FIFO default; validation-clean shutdown.
- AOT publish with windowed/offscreen smoke.

Exit: windowed clear reaches the window; resize/fullscreen/minimize do not hang/leak; offscreen
clears and discards; headless initializes neither SDL nor Vulkan; validation reports zero errors.

### M7 — Frame handoff and pacing

Goal: bounded run-ahead snapshot ring for windowed/offscreen, plus timer-paced headless.

- Three-slot ring (`Free/Writing/Ready/Rendering`); slots held until the GPU fence; tick waits only
  when full; newest `Ready` wins; older snapshots dropped.
- No target tick rate/deadline/`Sleep` in windowed/offscreen; `MaxDeltaTime` clamp only.
- Headless loop with no ring, paced by `ticksPerSecond` (default 60, `0` uncapped).
- Capture after `Tick`: unordered traversal into internal value-only `CameraSnapshot` /
  `SpriteSnapshot`, layer flattening, immutable snapshots.
- Two GPU frames in flight; instance/command resources; resize/close/error handling; shutdown with
  in-flight frames; no per-frame allocations.
- Stress tests: fake fences, slot invariants, pacing, headless timer.

Exit: tick/render overlap with at most ring-depth run-ahead; no sleeps in windowed/offscreen;
newest snapshot wins; headless honors its rate; 10k-frame stress has no deadlock/slot leak; AOT
headless loads a pack, ticks, exits.

### M8 — Sprite renderer with real assets

Goal: render packed sprite assets with one instanced draw per non-empty camera.

- Sprite pipeline from MessagePack assets (UVs, pixels-per-unit scale, pivot, `Quad`; no vertices).
- Texture descriptors + RGBA8 payloads → bindless texture pages; instance SSBO carries UVs,
  world-unit size, pivot, world transform, tint, texture index; vertex shader generates corners.
- Camera projection (`Size` half-height, Y-up), viewport/aspect, screen/world helpers; cameras
  ascending `ZIndex`; layers, z-order, tint, transforms; one dynamic-rendering pass and one draw
  per non-empty camera; clear/overlap semantics; split-screen and offscreen.
- Validate `indices`; v1 draws `Quad`; non-Quad assets preserved for the future indexed path.
- Debug counters/validation/capture; golden tests; benchmark at 1920×1080 with no artificial cap.

Exit: packed scene renders with exactly one draw per non-empty camera; layers/z/tint/split-screen/
camera/clear semantics match docs; 50,000 sprites sustain the measured target; no steady-state
allocations; validation clean; offscreen matches windowed; not-ready sprites are skipped.

### M9 — Template, samples, packaging

Goal: a new game can be created, packed, and AOT-published.

- Solution template: `Project.Common`, `Project.Desktop`, `Project.Editor`,
  `bork2d.project.toml`, assets; `dotnet new bork2d` or documented copy; CI template build.
- Sample game: sprites, layers, camera, input, data, overlays, editor slicing; headless server and
  offscreen samples; NativeAOT publish profile and native SDL2/Vulkan deployment.
- Docs samples compile; guide/reference match the surface; packaging decision and versioning.

Exit: clean machine + SDK + Vulkan 1.3 builds/runs the template; game code references only
`Bork2d.Api`/`Bork2d.Runtime`; debug/release packs load; AOT desktop and headless start/shut down;
editor opens and slices the sample.

### M10 — Hardening and v1

Goal: stability, performance, and a supportable public contract.

- Performance: tick cost, pack/decompression, snapshot copy, sorting, instance build, GPU.
- Device loss, resize stress, alt-tab, multi-monitor/DPI, minimize/restore.
- Pack compatibility/corruption, overlay stress, missing-pack behavior; memory/page budgets,
  allocation-free steady state, shutdown under load.
- AOT/trimming matrix, clean-machine deployment, validation matrix, flaky-test quarantine.
- API review and freeze for v1; docs final pass; release notes.

Exit: the v1 definition below is demonstrated and the public API is frozen.

## 6. Cross-cutting work

| Area | Policy |
| --- | --- |
| Documentation | Same-change docs for contracts; one fact, one owner (see [docs index](README.md)); decisions for public changes. |
| Tests | Unit tests each milestone; pack/format from M3; AOT smoke from M1; handoff stress from M7; GPU/visual from M8. |
| CI | PRs to `develop` and pushes to `develop`: docs link check, format, build, test. AOT is local; GPU runner from M8; template build from M9. |
| NativeAOT | Analyzers + publish smoke from M1; no reflection/dynamic code/TOML in runtime; generated MessagePack shapes. |
| Diagnostics | ZLogger/MEL, `IEngine.LoggerFactory`, counters, validation layers, labels; no silent failures. |
| Quality | Warnings-as-errors, style errors, format gate, AOT/trim analyzers on every project. |
| Scope | Audio/UI/physics/animation need a decision entry and roadmap change. |

## 7. v1 definition of done

- **Engine/runtime:** generated game builds/runs on Windows 10/11 with Vulkan 1.3; NativeAOT desktop
  and headless server start/stop cleanly; windowed/offscreen/headless behave as documented;
  tick-scoped workers; builder-owned logging with `IEngine.LoggerFactory`.
- **Assets:** `assets.pck` format as specified; header compressed/checksummed; debug payloads
  uncompressed, release Zstd only when smaller; overlays/browse; missing/malformed base pack fails;
  textures/sprites baked from TOML; AOT uses generated shapes only.
- **Editor:** Avalonia JIT editor with builder, slicing, metadata editing, property grid/raw TOML,
  bake/pack integration, compile-time registrations; no Vulkan, scene editing, or scanning.
- **Rendering:** Vulkan 1.3 dynamic rendering, bindless pages, instanced generated quads, one draw
  per non-empty camera, documented sorting/clear semantics, windowed present and offscreen
  discard, zero validation errors.
- **Process:** docs and decision log match the implementation; CI gates pass; warnings/style are
  errors; public API reviewed and frozen.

**Non-goals for v1:** 3D, HDR, MSAA, post-processing, physics, audio, in-game UI, networking, scene
editing, hot reload, multi-window, Linux/macOS guarantees, indexed non-Quad drawing, asset/pack
unloading, public offscreen readback.

## 8. Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| NativeAOT vs Silk.NET/native dependencies | High | AOT smoke from M1; pin versions; isolate adapters. |
| Pack format/overlay bugs | High | Versioned reader, bounds/duplicate tests, fuzzing, AOT tests. |
| Frame pacing races/deadlocks | High | Three-slot state machine, fake-fence stress, invariants, no sleeps. |
| Vulkan WSI/offscreen lifetime | High | Split windowed/offscreen paths; validation; GPU runner. |
| Bindless limits on target GPUs | Medium | Startup feature validation; configurable capacity; explicit failure. |
| Documentation drift | Medium | One-fact ownership, same-change rule, link/anchor CI check. |

## 9. Open decisions

The five M2 decisions (window visibility timing, fullscreen mode, minimize behavior, exposing the
mode, and DPI/`IWindow.Size`) are resolved in the [M2 spec §4.1](specs/m2-sdl-platform.md#41-roadmap-open-decisions-resolved-by-this-spec)
and the [decision log](decisions.md).

The five M3 decisions are resolved by the approved [M3 spec §4](specs/m3-assets.md#4-locked-decisions)
and implemented/merged (M3 merged 2026-10-26, PR #4):
Zstd is the .NET 11 built-in (`ZstandardStream`, max quality 22); no automatic retry; no loose-file
overlay; no policy size caps (`uint32` entry length/size, `uint64` offset, intrinsic/streaming
bounds); RGBA8 + sRGB only. Remaining open decisions:

| Decision | Needed by | Default if unresolved |
| --- | --- | --- |
| Frame count in flight / ring depth | M6/M7 | 2 GPU frames, 3 snapshot slots. |
| Allocator: minimal suballocator vs VMA | M6 | Minimal first; VMA if profiling demands. |
| sRGB fallback when no sRGB format exists | M6 | Fail startup; require sRGB. |
| Headless timer accuracy / fractional rates | M7 | Integer `ticksPerSecond`; `0` uncapped. |
| Bindless page capacity and overflow policy | M8 | 4096 pages; hard failure on overflow. |
| Shader compiler/build integration | M8 | `glslangValidator` build target; embedded SPIR-V. |
| Offscreen readback/capture API | M8/M10 | Internal discard in v1; public later. |
| Validation warning policy | M8 | Errors fail; warnings are reviewed. |
| TOML library and comment preservation | M4 | Tomlyn; preserve comments if practical. |
| Project descriptor schema/type mapping | M4/M5 | `bork2d.project.toml`; define schema in M4. |
| MSBuild vs CLI incremental bake | M4 | MSBuild target; hash-based incremental build. |
| Multi-GPU / device override | M10 | First suitable device; no override in v1. |
| Packaging: project references vs NuGet | M9 | Project references in the template; NuGet later. |
| Public API freeze | M10 | Freeze at v1.0. |

## 10. Progress log

| Milestone | Status | Evidence |
| --- | --- | --- |
| M0 Documentation | Complete (2026-10-25) | Docs set, decision log, accepted ownership/pack/editor/pacing decisions. |
| M1 Foundation/AOT | Merged (2026-10-25, PR #1) | 135 tests; Debug/Release zero warnings; format clean; AOT smoke publishes/runs locally; CI build/test/format on PRs to `develop` and `develop` pushes. [M1 spec](specs/m1-foundation.md). |
| M2 SDL platform | Merged (2026-10-26, PR #2) | 292 tests (283 in CI, 292 with `BORK2D_RUN_WINDOW_TESTS=1`); Debug/Release zero warnings; format/docs clean; AOT headless and `--windowed --ticks 120` exit 0 with `SDL2.dll`; live-window 5,000,000-tick run exit 0; Escape/close graceful. [M2 spec](specs/m2-sdl-platform.md#125-acceptance-evidence-recorded-by-wp5). |
| M3 Assets/pack | Merged (2026-10-26, PR #4) | 587 tests (578 deterministic + 9 opt-in real-window); Debug/Release zero warnings; format/docs clean; AOT publish zero AOT/trim warnings with `SDL2.dll`; default AOT run exit 0 loading a 2-entry temporary pack (raw + generated-shape MessagePack) and one tick; `--windowed --ticks 120` exit 0 with `WithoutAssets()`; crash-free pack/overlay failure matrix. [M3 spec](specs/m3-assets.md#123-acceptance-evidence-recorded-by-wp5). |
| M4 Serialization/bake | Not started | — |
| M5 Editor | Not started | — |
| M6 Vulkan bring-up | Not started | — |
| M7 Frame handoff/pacing | Not started | — |
| M8 Sprite renderer | Not started | — |
| M9 Template/samples | Not started | — |
| M10 Hardening/v1 | Not started | — |

## 11. Related documents

- [Architecture overview](architecture/overview.md) — invariants.
- [M1 spec](specs/m1-foundation.md), [M2 spec](specs/m2-sdl-platform.md), [M3 spec](specs/m3-assets.md)
  — historical records and the current draft.
- [Decision log](decisions.md) — accepted decisions.
- [Scene ownership](architecture/scene-ownership.md), [Threading](architecture/threading.md),
  [Runtime lifecycle](architecture/runtime-lifecycle.md).
- [Assets and tooling](architecture/assets-and-tooling.md), [Rendering](architecture/rendering.md).
- [NativeAOT](architecture/nativeaot.md), [Logging](architecture/logging.md),
  [Contributing](contributing.md).
