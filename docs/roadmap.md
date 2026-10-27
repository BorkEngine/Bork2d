# Implementation Roadmap

> **Status:** M0 complete; **M1 merged into `develop` (2026-10-25, PR #1)**; **M2 (SDL windowed
> platform) merged into `develop` (2026-10-26, PR #2)**; **M3 (assets: pack, VFS, descriptors)
> merged into `develop` (2026-10-26, PR #4)**; **the D-035 asset-reference refactor merged (PR #6,
> 2026-10-26)**; **M4 (serialization and bake) is implementation complete on `m-4/common`; the
> owner-requested revisions (D-M4-13 plus the committed fixture source PNG) are complete on
> `m-4/fix-2`, and the D-M4-14/D-M4-15 multi-pack revision is complete on `m-4/fix-3` — PR #7 is
> open and M4 is not merged.** The sequence is dependency-ordered.
> A milestone is done when its exit criterion is demonstrated, not merely when code is merged.

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
| `Bork2d.Api` | Current | Contracts for engine, game, time, window, input, assets, camera, sprite, transform, root, `ZIndex`, `Bork2dException`, `AssetInfo`, `AssetException`/`AssetErrorKind`, the public asset model (`SpriteAsset`, `TextureDescriptor`, `SpriteTopology`, `AssetRef<T>`, `DataRef`), the full `KeyCode` scancode set, and five `MouseButton` members. Window/input are implemented for windowed mode; assets are implemented in M3 and the reference model in D-035. |
| `Bork2d.Common` | Partial | Root-owned unordered scene tree, transforms (degrees normalized), structural/per-object locks, time wrapper, layer validation, MessagePack shape policy. Snapshot structs are M7. |
| `Bork2d.Runtime` | Current (M3) | Builder, validation, app-owned logging, SDL windowed host (window/input/time, uncapped loop), one-tick headless/offscreen host, `Exit`/`Dispose`/post-shutdown contract, and the app-scoped asset-service lifecycle. Renderer wiring is M6/M8. |
| `Bork2d.Assets` | Current (M3/D-035) | v1 pack reader/writer, public `Bork2d.Assets.Pack.PackBuilder`/`PackCompressionMode`, canonical paths, base/overlay VFS, one loader thread with `LoadData`/`LoadAsset<T>`, and generic pack-time reference validation. |
| `Bork2d.Bake` / `Bork2d.Bake.Cli` | Current (M4, m-4/fix-3) | JIT tooling: multi-pack descriptor, one generated host over all roots, shape-driven Tomlyn DOM binder, per-pack texture import, local + main reference validation, structural determinism, all-pack incremental bake, per-pack atomic writes, MSBuild targets. Internal; never referenced by the runtime. PR #7 open, not merged. |
| `Bork2d.Render` | Empty | Reference-wired; no Vulkan implementation (M6/M8). |
| `Bork2d.Editor` | Absent | Not in the solution (M5); reuses the implemented `Bork2d.Bake`. |
| `Bork2d.Tests` | Current | 911 deterministic xUnit tests plus 12 opt-in/conditional tests (923 total): ownership/GC, root, transforms, camera, builder, logging, concurrency, dependency matrix, fake-SDL window/input/loop/time, pack/VFS/loader, asset references, runtime lifecycle, and the M4 bake/host/TOML/texture/multi-pack/incremental/MSBuild suites. |
| NativeAOT | M4 baseline (m-4/fix-3) | Libraries use `IsAotCompatible` + trim/AOT analyzers; `Bork2d.AotSmoke` publishes/runs locally with zero warnings. The default headless run mounts the **baked main pack** (5 entries: `data/game`, `roots/stats`, `sprites/player`, `art/player`, `art/player-raw`) and overlays `dlc1.pck` (1 entry: `data/dlc`, whose texture resolves to main); `--windowed --ticks 120` uses `WithoutAssets()`. Local gate only. |
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
gate, and AOT smoke. Evidence: 135 tests; Debug/Release zero warnings; format clean;
`Bork2d.AotSmoke` publishes and runs locally with zero AOT/trim warnings. See the
[M1 record](specs/m1-foundation.md).

### M2 — SDL windowed platform

**Status:** merged into `develop` (2026-10-26, PR #2).

Goal: a window that ticks, reads input, and exits cleanly; no Vulkan yet.

- Visible window at `Run()` start on one engine-owned OS/window thread, with queued window commands.
- Full physical-scancode input, five mouse buttons, text, focus/minimize/DPI semantics.
- Windowed-only real loop plus `Exit`/close/`Dispose` contracts; headless/offscreen stay one-tick.

Evidence: 292 tests (283 deterministic + 9 opt-in); zero-warning Debug/Release and AOT publish;
`--windowed --ticks 120` exit 0 at 150% DPI; a 5,000,000-tick live-window run exit 0. See the
[M2 record](specs/m2-sdl-platform.md) and the
[D-M2 decisions](decisions.md#m2-platform-decisions-approved-2026-10-26).

### M3 — Assets: pack, VFS, descriptors

**Status:** merged into `develop` (2026-10-26, PR #4). The sprite-specific parts were superseded by
the [D-035 refactor](specs/refactor-asset-references.md) (merged, PR #6).

Goal: load, overlay, browse, and describe packed assets without a renderer.

- v1 `assets.pck` reader/writer and public `PackBuilder`; canonical paths; base/overlay VFS.
- One loader thread with `LoadData`/`LoadAsset<T>`; `Browse`/`Exists`/`AssetInfo`; `WithoutAssets()`.
- Public `SpriteAsset`/`TextureDescriptor`/`AssetRef<T>`/`DataRef` after D-035; the old
  `LoadSprite`/`Sprite`/resource-table path is gone.

Evidence at merge: 587 tests (578 deterministic + 9 opt-in); zero-warning Debug/Release and AOT
publish; the default AOT run exited 0 with a **2-entry** pack (5 entries after D-035); crash-free
overlay/corruption matrix. The current tree (M4) has 923 tests (911 deterministic + 12 conditional).
See the [M3 record](specs/m3-assets.md).

### M4 — Serialization and bake

**Status:** implementation complete on `m-4/common` (WP0–WP6); the owner-requested revision
(D-M4-13 plus the committed fixture source PNG) is complete on `m-4/fix-2`, and the
D-M4-14/D-M4-15 multi-pack revision is complete on `m-4/fix-3`. PR #7 to `develop` is
open and M4 is not merged. Full
scope, decisions, and as-built evidence:
[M4 spec](specs/m4-serialization-and-bake.md) §22; feasibility findings:
[WP0 report](specs/m4-wp0-spike.md).

Goal: TOML source becomes MessagePack assets and a packed release artifact, with the same generated
shapes the NativeAOT runtime uses.

- **Approved scope.** `[GenerateShape]` + `partial` (PolyType's rule, not a Bork2d attribute) with
  each data TOML declaring its type in `[bork2d]` (D-M4-13); `Color` via `ColorMarshaler`;
  custom-data TOML → MessagePack with required/unknown/key/type/range rules and typed
  `AssetRef`/`DataRef` validation (`file:line:column`); PNG/JPG/BMP/TGA textures anywhere under the
  asset root → `TextureDescriptor` + RGBA8 payload with `<descriptor>-raw` payload paths; structural
  determinism (no pack-byte guarantee); hash-based incremental bake; project descriptor and
  generated host; MSBuild targets; CLI; AOT load of a baked fixture pack; **no TOML or reflection in
  the runtime**.
- **Scope delta (D-M4-06).** `SpriteAsset` is baked as ordinary custom data only. There is no sprite
  source schema, no UV/pivot/scale/topology/indices validation, and no slicing in M4; those are M5.
- **Struct roots (D-M4-11).** A `readonly record struct` root bakes, loads through the relaxed
  `where T : IShapeable<T>` contract, and is referenced with `AssetRef<T>`; a `nil` root is a
  `Decode` fault.
- **Owner revision (D-M4-13, fix-1/fix-2).** Type registration moved from the descriptor's
  `[[types]]` list into each data TOML's reserved `[bork2d]` table; data and texture sources may
  live anywhere under the asset root; texture raw payloads are `<descriptor>-raw`; the descriptor
  keeps only `[assets]`/`[bake]`/`[textures]`. The host generator scans and hashes the source
  declarations. The fixture and tests are migrated; `m-4/fix-2` commits the deterministic 2x2
  fixture **source** PNG at `Assets/art/player.png` (sidecar `art/player.png.toml`, data/sprite TOMLs
  reference `art/player`), removes the texture generator, and re-runs the full gates including AOT.
  Test images and packs remain generated in temp directories; no binary test/pack fixture is
  committed.
- **Multi-pack revision (D-M4-14/D-M4-15, fix-3).** The descriptor declares ordered `[[assets]]`
  entries: the first is the main/base pack, later entries are DLC/overlay packs; `root` and `pack`
  are required per entry, duplicate roots/pack paths are errors, and the project `name` and
  `[bake].pack` are removed. One generated host scans all roots. Each pack is baked from its own
  root; a DLC reference may resolve locally or in main (main is strict, DLC→DLC is an error), the
  same canonical path may exist across packs (runtime overlay), and duplicates inside one pack stay
  errors. `bake --output-root` replaces `--output`; the incremental manifest records every output
  and skips all-or-nothing; the MSBuild targets bake/copy/clean every declared pack. Texture
  sidecars (`<source>.toml`) remain the minimal authoring-only settings file (D-M4-15). The fixture
  gains `DLC1/data/dlc.toml`, and the AOT smoke mounts the main pack plus `dlc1.pck` and verifies a
  DLC → main texture reference.
- **Build.** `build/Bork2d.Bake.targets` runs `generate-host` → host build → `bake` after `Build`,
  copies every declared pack on `Publish`, and cleans the generated host/manifest/packs.

Exit (demonstrated): a source tree bakes and packs; the header is compressed/checksummed; invalid
source fails with file/line diagnostics and does not replace the pack; the runtime loads with
generated shapes only; the AOT publish is zero-warning, contains no bake/TOML/image-tooling
assemblies, and its default run loads the baked pack and exits 0.

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
- No target tick rate/deadline/`Sleep` in windowed/offscreen; the builder's `WithMaxDeltaTime`
  gameplay clamp only.
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

Resolved choices are recorded in the [decision log](decisions.md), including the M2 and M3
approvals; only unresolved choices are listed here.

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
| Multi-GPU / device override | M10 | First suitable device; no override in v1. |
| Packaging: project references vs NuGet | M9 | Project references in the template; NuGet later. |
| Public API freeze | M10 | Freeze at v1.0. |

## 10. Progress log

| Milestone | Status | Evidence |
| --- | --- | --- |
| M0 Documentation | Complete (2026-10-25) | Docs set, decision log, accepted ownership/pack/editor/pacing decisions. |
| M1 Foundation/AOT | Merged (2026-10-25, PR #1) | 135 tests; Debug/Release zero warnings; format clean; AOT smoke publishes/runs locally; CI build/test/format on PRs to `develop` and `develop` pushes. [M1 record](specs/m1-foundation.md). |
| M2 SDL platform | Merged (2026-10-26, PR #2) | 292 tests (283 in CI, 292 with `BORK2D_RUN_WINDOW_TESTS=1`); Debug/Release zero warnings; format/docs clean; AOT headless and `--windowed --ticks 120` exit 0 with `SDL2.dll`; live-window 5,000,000-tick run exit 0; Escape/close graceful. [M2 record](specs/m2-sdl-platform.md). |
| M3 Assets/pack | Merged (2026-10-26, PR #4) | 587 tests (578 deterministic + 9 opt-in real-window); Debug/Release zero warnings; format/docs clean; AOT publish zero AOT/trim warnings with `SDL2.dll`; default AOT run exit 0 loading a 2-entry temporary pack (raw + generated-shape MessagePack) and one tick; `--windowed --ticks 120` exit 0 with `WithoutAssets()`; crash-free pack/overlay failure matrix. [M3 record](specs/m3-assets.md). |
| Asset-reference refactor | Merged (2026-10-26, PR #6, D-035) | 528 tests (519 deterministic + 9 opt-in real-window); Debug/Release zero warnings; format/docs/`git diff --check` clean; AOT publish zero AOT/trim warnings; default AOT run exit 0 with a 5-entry pack including the public `SpriteAsset`/`TextureDescriptor` shapes; `--windowed --ticks 120` exit 0. [Refactor record](specs/refactor-asset-references.md). |
| M4 Serialization/bake | Implementation complete on `m-4/common`; owner revisions complete on `m-4/fix-3`; PR #7 open, not merged | WP0–WP6 landed: bake library + CLI, generated host, per-source `[bork2d]` metadata, descriptor/TOML binder, location-free texture pipeline, reference validation, structural determinism, incremental bake, MSBuild targets, `Bork2d.BakeFixture`, and the AOT baked-pack smoke. The fix-1 revision removed the descriptor `[[types]]`/prefix model, moved type metadata into each data TOML, allowed data/textures anywhere under the asset root, and changed raw payload paths to `<descriptor>-raw`. Fix-2 commits the fixture **source** PNG at `Bork2d.BakeFixture/Assets/art/player.png` (sidecar `art/player.png.toml`; data/sprite TOMLs reference `art/player`), removes the texture generator, and leaves test images/packs generated in temp directories. Fix-3 applies D-M4-14/D-M4-15: ordered `[[assets]]` multi-pack descriptors (first is main; no `name`/`[bake].pack`), one host over all roots, per-pack bake with main-only external references, internal `PackBuilder` external-main seam, `bake --output-root` (no `--output`), all-output incremental manifest schema 2, MSBuild bake/publish/clean for every declared pack, fixture `DLC1/data/dlc.toml`, and the AOT main + `dlc1.pck` overlay proof; texture sidecars are retained. The main fixture pack has 5 entries (ending in `art/player` and `art/player-raw`) and the DLC pack 1 (`data/dlc`). 911 deterministic + 12 conditional tests (923 total); Debug/Release zero warnings; format/docs/diff checks clean. Fix-3 AOT publish is zero-warning with `assets.pck`/`dlc1.pck`/`SDL2.dll` and no Bake/Tomlyn/StbImageSharp assemblies; the published headless run exits 0 with `mainEntries=5`, `mergedEntries=6`, the DLC → main texture reference, and 16 raw bytes; `--windowed --ticks 120` exits 0. Not merged. [M4 spec §22](specs/m4-serialization-and-bake.md#22-as-built-evidence-m-4fix-3-revision). |
| M5 Editor | Not started | — |
| M6 Vulkan bring-up | Not started | — |
| M7 Frame handoff/pacing | Not started | — |
| M8 Sprite renderer | Not started | — |
| M9 Template/samples | Not started | — |
| M10 Hardening/v1 | Not started | — |

## 11. Related documents

- [Architecture overview](architecture/overview.md) — invariants.
- [M1 record](specs/m1-foundation.md), [M2 record](specs/m2-sdl-platform.md),
  [M3 record](specs/m3-assets.md) — historical milestone records.
- [Decision log](decisions.md) — accepted decisions.
- [Scene ownership](architecture/scene-ownership.md), [Threading](architecture/threading.md),
  [Runtime lifecycle](architecture/runtime-lifecycle.md).
- [Assets and tooling](architecture/assets-and-tooling.md), [Rendering](architecture/rendering.md).
- [NativeAOT](architecture/nativeaot.md), [Logging](architecture/logging.md),
  [Contributing](contributing.md).
