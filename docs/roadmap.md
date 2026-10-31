# Implementation Roadmap

> **Status:** M0 complete; **M1 merged into `develop` (2026-10-25, PR #1)**; **M2 (SDL windowed
> platform) merged into `develop` (2026-10-26, PR #2)**; **M3 (assets: pack, VFS, descriptors)
> merged into `develop` (2026-10-26, PR #4)**; **the D-035 asset-reference refactor merged (PR #6,
> 2026-10-26)**; **M4 (serialization and bake) merged into `develop` (2026-10-27, PR #7), including
> the D-M4-13 per-source type/location-free revision and the D-M4-14/D-M4-15 multi-pack revision.**
> **M6 (Vulkan bring-up) merged into `develop` (2026-10-31, PR #11, merge `5de725c`).**
> **M7 (frame handoff and pacing) is merged into `develop` (2026-10-31, PR #12, merge `5de5ac3`).**
> **M8 (sprite renderer) is in progress:** the owner-approved spec
> ([M8 spec](specs/m8-sprite-renderer.md)) is the M8 contract; WP-1 reconciled the docs, WP-2 added
> the shader pipeline/camera helpers/instance planning, and WP-3 added the texture pages/placeholder
> and the public `RenderStats`. Draws, capture, and the benchmark remain WP-4/WP-5. Evidence so far:
> [M7 spec §18](specs/m7-frame-handoff.md#18-as-built-evidence-2026-10-31) and the M8 spec §12.
> The sequence is dependency-ordered.
> A milestone is done when its exit criterion is demonstrated, not merely when code is merged.
>
> **Post-M4 task (not a milestone): D-036/D-037/D-038/D-039/D-040 asset processors and `MyGame.Assets`.**
> **Implemented as-built on `task/file-processors` (2026-10-27) and merged into `develop` (2026-10-27, PR #8).** It
> replaces the project descriptor with builder configuration, makes source file types registered
> processors (built-in texture importer included), removes the generated host in favor of explicit
> `RegisterAsset<T>()` registrations, and moves assets/processors to a game-owned executable that
> bakes on build with a schema-3 incremental manifest. The runtime model and milestone sequence are
> unchanged. Design: [asset processors](architecture/asset-processors.md); implementation spec:
> [file processors](specs/file-processors.md).
>
> **Post-M4 task (not a milestone): the Pong example (D-043/D-044).**
> **Merged into `develop` (2026-10-29, PR #10, merge `0273f58`).** It
> replaces `Bork2d.AotSmoke`/`Bork2d.BakeFixture`/`Bork2d.BakeFixture.Assets` with one real game
> solution, `examples/pong` (`Pong.Common`, `Pong.Assets`, `Pong.Desktop`, `Pong.Tests`), makes
> `Pong.Desktop` the NativeAOT headless/windowed gate, and moves the exploratory smoke/fixture tests
> to self-contained engine bake tests. The runtime model and milestone sequence are unchanged.
> Spec: [Pong example](specs/pong-example.md); decisions: D-043/D-044.
>
> **Stage 1 — engine version 0.1.** Every milestone below belongs to Stage 1 and ships as engine
> 0.1. There is **no API freeze**: the public API stays flexible until 1.0, and 1.0 is not
> planned. M5 (Editor) is removed from Stage 1; later milestone IDs are unchanged, and no editor
> work is planned for Stage 1.

## 1. How to read this roadmap

- The order is fixed: foundation and platform first, then **assets → bake**, then
  **Vulkan → frame handoff → sprite renderer**, then template, hardening, and the 0.1 release.
  The editor is outside Stage 1.
- Each milestone lists 3–5 scope bullets and testable exit criteria. Status is tracked in the
  progress log; details live in the owning architecture document and the milestone PR.
- No dates are assigned; sizes are rough estimates, not promises.

## 2. Current baseline

| Area | State | Notes |
| --- | --- | --- |
| Build | Green | SDK `11.0.100-rc.1`, `net11.0`, zero warnings/errors in Debug and Release; `dotnet format` clean. |
| `Bork2d.Engine` | Current | Public contract for engine, game, time, window, input, assets, camera, sprite, transform, root, `ZIndex`, `Bork2dException`, `AssetInfo`, `AssetException`/`AssetErrorKind`, the public asset model (`SpriteAsset`, `TextureDescriptor`, `TextureFormat`, `TextureFilter`, `TextureWrap`, `SpriteTopology`, `AssetRef<T>`, `DataRef`), the full `KeyCode` scancode set, and five `MouseButton` members; plus the root-owned unordered scene tree, transforms (degrees normalized), structural/per-object locks, time wrapper, layer validation, MessagePack shape policy, v1 pack reader/writer, canonical paths, base/overlay VFS, one loader thread, generic pack-time reference validation, and the M7 internal snapshot model/capture plus public `FlipX`/`FlipY` (Q2=B1). |
| `Bork2d.Runtime` | Current (M6/M7) | Builder, validation, app-owned logging, SDL windowed host (window/input/time), Vulkan instance/surface and renderer composition, windowed/offscreen loops with snapshot acquire/capture/publish and ring backpressure, paced headless host (K1 loop, L2 synthesized clock, M4/F1 strict grid), `Exit`/`Dispose`/post-shutdown contract, and the app-scoped asset-service lifecycle. |
| `Bork2d.Bake` | Implemented on `task/file-processors` (2026-10-27; D-036/D-037/D-038); merged with the builder by D-041/D-042 | JIT tooling: builder-configured ordered packs, shape-driven Tomlyn binder, processor registry/sidecars, built-in texture processor, reference validation, structural determinism, schema-3 incremental bake, per-pack atomic writes, MSBuild targets. The public builder/processor surface and the internal implementation share the one `Bork2d.Bake` assembly. No descriptor, generated host, host state, or `Bork2d.Bake.Cli`. The Pong example's `CircleProcessor` lives in `Pong.Assets` and uses this surface. |
| `Bork2d.Render` | Current (M6/M7; M8 WP-2 pipeline + WP-3 pages) | Vulkan packages, the internal renderer seam and SPSC control channel, instance/validation/debug-utils bootstrap, physical/logical device selection, the pure surface-format/present-mode/swapchain policies, the windowed swapchain/clear/present renderer with resize/minimize/fullscreen recreation and surface-loss recovery, the offscreen target/renderer with clear/discard, the M7 three-slot ring/handoff consumer with one frame per consumed snapshot, fence-held slots, suspended minimized drain, and the per-frame instance/bindless resources. M8 WP-2 adds the internal sprite pipeline (descriptor-set layout, 64-byte push constant, pipeline per sRGB format, sampler cache), the shared camera projection bridge, and the pure 96-byte instance/quad planner; WP-3 adds the internal texture-page subsystem (async `IAssets` prefetch, validation, generated placeholder, device-local and staging pools, upload fences, bindless writes, dynamic capacity) and the per-frame binding 0/1 resources. Per-camera draws and stats/capture publication remain WP-4. Counters and validation observation cover both. |
| `Bork2d.Editor` | Absent | Not in the solution; the editor is removed from Stage 1 and deferred (M5 removed). |
| `Bork2d.Tests` | Current | 1141 deterministic xUnit tests plus 17 opt-in/conditional tests (1158 total): ownership/GC, root, transforms, camera, builder, logging, concurrency, dependency matrix/public surfaces, the Q-20 metadata guard, fake-SDL window/input/loop/time, the SDL Vulkan surface seam, pack/VFS/loader, asset references, runtime lifecycle, the builder/processor/sidecar/bake suites, schema-3 incremental rules, the M6 renderer seam/policy/GPU-bootstrap tests, the SPSC control-channel, the windowed/offscreen renderer start-info/lifecycle/GPU tests, the renderer host lifecycle matrix, the platform Vulkan start/posts/surface-replacement tests, the offscreen factory validation tests, and the M7 snapshot ring/stress/capture/allocation, host handoff, minimized tick-cap, and headless loop/pacing tests. The exploratory smoke/fixture tests were removed by D-043; bake coverage is self-contained. |
| `examples/pong` (Pong example) | Current (D-043/D-044; merged 2026-10-29, PR #10; M6 offscreen mode; M7 headless tick gate) | `Pong.Common` (game/simulation/AI/settings), build-only `Pong.Assets` (circle processor, TOML, pack bake), AOT `Pong.Desktop` (`--headless` 120-tick default with `--headless --ticks N`, the interactive `--windowed` example, `--windowed [--ticks N]`, and `--offscreen --ticks N`), and `Pong.Tests` (46 tests, including the linked `PongArgumentsTests`). Builds the pack out of process through `build/Bork2d.Assets.Copy.targets`. |
| NativeAOT | Current (D-043; merged 2026-10-29, PR #10; M6 three-mode gate; M7 headless tick gate) | Libraries use `IsAotCompatible` + trim/AOT analyzers; `Pong.Desktop` is the only `PublishAot` project and publishes/runs locally with zero warnings. The headless run mounts the **baked 7-entry pack** (`art/paddle`, `art/paddle-raw`, `game_settings`, `sprites/ball`, `sprites/paddle`, `textures/ball`, `textures/ball-raw`) and asserts the class-root settings, sprite/descriptor/raw chain, exactly 120 ticks by default (`--headless --ticks N` overrides), and the detached root; `--windowed --ticks 120` runs the same pack and presents a black clear; `--offscreen --ticks 120` runs the same pack through the offscreen renderer and discards frames (count not asserted). No tooling assembly is in the publish and no `vulkan-1.dll` is copied. Local gate only. |
| CI | Current | GitHub Actions on PRs to `develop` and pushes to `develop`: restore, format verify, build, test. No AOT or real-window job. |
| Runnable engine | Partial (windowed/offscreen Vulkan clear with handoff + assets + texture pages; no sprite draws yet) | A real window, input, time, the M7 ring-backpressured tick loop, the pack/VFS/loader/CPU descriptor pipeline, and the M6/M7 renderers: windowed presents a black clear per consumed snapshot; offscreen clears/discards a fixed target and loops until `Exit`; headless runs the paced loop with no SDL/Vulkan. The M8 WP-3 texture pages/placeholder are created at startup; the per-camera sprite draws are WP-4. |

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
| M5 | — | Removed: the editor is out of scope for Stage 1 | — | — | — |
| M6 | Vulkan bring-up | Windowed and offscreen device/clear/validation | M4 | M | High |
| M7 | Frame handoff + pacing | Bounded run-ahead, latest snapshot, headless/offscreen loops | M6 | L | High |
| M8 | Sprite renderer | Real sprite assets, one draw per camera | M7 | L | High |
| M9 | Template, samples, packaging | Game solution, samples, AOT desktop/server | M8 | S/M | Low/Medium |
| M10 | Hardening + 0.1 | Stability, performance, API review, release | all | L | Medium |

`examples/` contains only the Pong example (`examples/pong`), which is the engine's living example
and its NativeAOT gate; M9 adds the solution template and the remaining samples.

**M5 (Editor) is removed** from Stage 1: no `Bork2d.Editor`, no `Project.Editor` host, no texture
slicing, and no editor preview. Sprite UV/pivot/topology/indices validation is likewise not planned
until an editor exists; sprite assets remain ordinary custom data (D-M4-06). IDs M6–M10 are
unchanged so existing specs, decisions, and branch history stay unambiguous.

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
overlay/corruption matrix. The current tree has 958 tests (946 deterministic + 12 conditional).
See the [M3 record](specs/m3-assets.md).

### M4 — Serialization and bake

**Status:** **Merged into `develop` (2026-10-27, PR #7; merge commit `8039587`).** WP0–WP6 and the
owner revisions are landed: D-M4-13 (per-source `[bork2d].type`, no descriptor `[[types]]`,
location-free data/textures, `-raw` payloads), D-M4-14 (ordered `[[assets]]` multi-pack descriptors,
first-is-main, main-only cross-pack references), and D-M4-15 (texture sidecars retained). Full
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
  source schema, no UV/pivot/scale/topology/indices validation, and no slicing in M4; those remain
  out of scope until an editor exists.
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
- **Build (historical, replaced by the post-M4 task).** `build/Bork2d.Bake.targets` ran
  `generate-host` → host build → `bake` after `Build`, copied every declared pack on `Publish`, and
  cleaned the generated host/manifest/packs. D-036/D-037/D-038 replaced it with
  `build/Bork2d.Assets.targets` (assets executable bake/clean) and
  `build/Bork2d.Assets.Copy.targets` (out-of-process assets build + pack copy); there is no
  generated host or host state in the current flow.

Exit (demonstrated): a source tree bakes and packs; the header is compressed/checksummed; invalid
source fails with file/line diagnostics and does not replace the pack; the runtime loads with
generated shapes only; the AOT publish is zero-warning, contains no bake/TOML/image-tooling
assemblies, and its default run loads the baked pack and exits 0.

**Post-M4 task (not a milestone).** D-036/D-037/D-038 replace the descriptor/texture-sidecar special
cases with asset processors, explicit type registration, and a game-owned `MyGame.Assets`
builder/CLI. The generated host is removed. D-039 locks the implementation baseline and D-040 is the
owner-approved exception that makes the `TextureDescriptor` sampler fields non-null enums; the task
was delivered on `task/file-processors` and merged into `develop` (2026-10-27, PR #8, merge commit
`a98e0ab`) per the [file-processors spec](specs/file-processors.md). It does not change
the M6–M10 sequence. See [asset processors](architecture/asset-processors.md) and
[D-036/D-037/D-038/D-039/D-040](decisions.md#post-m4-asset-processors-approved-2026-10-27).

### M6 — Vulkan bring-up (windowed + offscreen)

**Status:** Merged into `develop` (2026-10-31, PR #11, merge `5de725c`). WP-1–WP-5 are complete.
Evidence is recorded in
[the M6 as-built section](specs/m6-vulkan-bringup.md#18-as-built-evidence-2026-10-30): engine suite
1044 (1028 deterministic + 16 opt-in/conditional) and 41 Pong tests; the four `[GpuFact]` and nine
`[WindowFact]` tests pass locally with zero validation errors/warnings; the AOT publish is
zero-warning and the published `--headless`, `--windowed --ticks 120`, and `--offscreen --ticks 120`
runs all exit 0.

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

**Status:** **Merged into `develop` (2026-10-31, PR #12, merge `5de5ac3`).** WP-1–WP-5 landed
sequentially. Evidence is recorded in
[M7 spec §18](specs/m7-frame-handoff.md#18-as-built-evidence-2026-10-31): engine suite 1158 (1141
deterministic + 17 opt-in/conditional) and 46 Pong tests; the five `[GpuFact]` tests pass locally
with zero validation errors/warnings; the 10,000-snapshot deterministic stress and slot invariants
pass; the AOT publish is zero-warning and the published `--headless` (120 ticks by default),
`--headless --ticks 7`, `--windowed --ticks 120`, and `--offscreen --ticks 120` runs all exit 0,
with the headless run reporting exactly its requested tick count.

Goal: bounded run-ahead snapshot ring for windowed/offscreen, plus timer-paced headless.

- Three-slot ring (`Free/Writing/Ready/Rendering`); slots held until the GPU fence; tick waits only
  when full; newest `Ready` wins; older snapshots dropped.
- No target tick rate/deadline/`Sleep` in windowed/offscreen; the builder's `WithMaxDeltaTime`
  gameplay clamp only, with the single minimized-only 30 Hz tick-cap exception (Q8/F4).
- Headless loop with no ring, paced by a positive `ticksPerSecond` (default 60; `0` is a `Build()`
  error, F6), fully synthesized `ITime` (tick 0 = 0/0, then `N/rate` and `1/rate`), strict absolute
  grid with pure `Thread.Sleep`, and unbounded catch-up (Q11–Q13/F5–F8). The Pong gate defaults to
  120 ticks and accepts `--headless --ticks N` (Q15=O2/F3).
- Capture after `Tick`: unordered traversal into internal value-only `CameraSnapshot` /
  `SpriteSnapshot`, layer flattening, immutable snapshots.
- Two GPU frames in flight; instance/command resources; resize/close/error handling; shutdown with
  in-flight frames; no per-frame allocations.
- Stress tests: fake fences, slot invariants, pacing, headless timer.

Exit: tick/render overlap with at most ring-depth run-ahead; no sleeps in windowed/offscreen except
the minimized cap; newest snapshot wins; headless honors its rate; 10k-frame stress has no
deadlock/slot leak; AOT headless loads a pack, ticks, exits.

### M8 — Sprite renderer with real assets

**Status:** **In progress** (approved 2026-10-31; WP-1 reconciled the owning docs, WP-2 added the
shader tool/pipeline, camera projection/public helpers, and the pure sprite planning/instance
layout, and WP-3 added the texture pages/placeholder/bindless resources and the public
`RenderStats`; WP-4–WP-5 remain). The contract is the [M8 spec](specs/m8-sprite-renderer.md); the
work packages are five sequential branches (`m-8/wp-1`…`m-8/wp-5`). Not merged and not done.

Goal: render packed sprite assets with one instanced draw per non-empty camera.

- **Texture pipeline:** the renderer resolves `SpriteAsset` → `TextureDescriptor` → RGBA8 payload
  through the app's `IAssets` seam; async prefetch, one page per descriptor path, an 8×8
  magenta/black placeholder at index `0`, a pool/suballocator plus staging ring, a separate upload
  submit + fence, and page capacity that starts at 256 and doubles up to the device limit (no VMA,
  no hard-coded cap).
- **Sprites and cameras:** quad size from UV span/texture size/`Scale`, pivot, tint, layer/`ZIndex`,
  and `FlipX`/`FlipY` geometry mirrored around the pivot; `Size` is the half-height Y-up projection,
  normalized viewport → rounded pixel rect/aspect; an invalid camera is skipped as a whole.
  `Quad` only: non-`Quad` assets are preserved for the future indexed path and skipped (never
  approximated); malformed sprite fields are fatal at draw time.
- **Passes:** one dynamic-rendering pass **per camera** over its pixel viewport
  (`loadOp=CLEAR` for `Clear.Color`, `LOAD` for `Clear.None`/`Clear.Depth`), **no frame-level
  safety pass**, one draw per non-empty camera; an empty `Clear.Color` camera is a clear-only pass
  and a frame with no cameras clears nothing.
- **Pipeline/instances:** checked-in GLSL compiled at build time by the build-only
  `Bork2d.ShaderCompiler` tool (Silk.NET.Shaderc 2.23.0) into generated SPIR-V linked into
  `Bork2d.Render`; 96-byte std430 instance records in a growable per-frame host-visible buffer
  (growth only at a free fence); a bindless sampler array with a variable descriptor count. WP-2
  landed the tool, pipeline/layout/sampler cache, camera projection, and pure 96-byte
  planner/instance layout; the per-frame buffer growth and draws are WP-4.
- **Approved public API (the only public changes):** `ICamera.WorldToScreen`/`ScreenToWorld`/
  `WorldSize` with framebuffer-pixel/world-size semantics and throws (all `Current` in WP-2); the
  `RenderStats` record struct and `IEngine.RenderStats` (`Current` in WP-3); `IEngine.CaptureFrameAsync`
  (RGBA8 readback of a completed frame, WP-4). **No golden/pixel tests**; GPU tests stay local-only
  (CI unchanged) and visual correctness is a manual windowed check.
- **Evidence:** deterministic plan/math/page/public-API tests, opt-in GPU tests, the
  **50,000-sprite, one camera, 1920×1080, median ≥165 FPS** local benchmark, and the Pong/AOT gate
  (`Cameras >= 1`, `NonEmptyCameras >= 1`, `DrawCalls >= 1`, `Instances >= 3`,
  `TexturePages >= 3`; one `Setup`/`Teardown`, exactly N ticks, manual visual check).

Exit: the packed Pong scene draws its three sprites with exactly one draw per non-empty camera;
layers/z/tint/split-screen/camera/clear semantics match the docs; the 50,000-sprite benchmark meets
median ≥165 FPS with zero steady-state allocations; validation is clean; offscreen matches windowed;
pending pages are skipped and failed textures draw the placeholder; the AOT publish is zero-warning
and its runs report the approved stats. The milestone is done when its exit criteria are
demonstrated, not when code is merged.

### M9 — Template, samples, packaging

Goal: a new game can be created, packed, and AOT-published.

- Solution template: `Project.Common`, `MyGame.Assets`, `Project.Desktop`, assets;
  builder configuration in `MyGame.Assets` (no project descriptor, D-037); `dotnet new bork2d` or
  documented copy; CI template build.
- Sample game: sprites, layers, camera, input, data, and AOT desktop; the Pong example is the first
  sample, and M9 adds overlay/DLC, headless server, and offscreen samples, the NativeAOT publish
  profile, and native SDL2/Vulkan deployment.
- Docs samples compile; guide/reference match the surface; packaging decision and versioning.

Exit: clean machine + SDK + Vulkan 1.3 builds/runs the template; game code references only
`Bork2d.Engine`/`Bork2d.Runtime`; debug/release packs load; AOT desktop and headless start/shut down.

### M10 — Hardening and 0.1

Goal: stability, performance, and a supportable public contract for the 0.1 release.

- Performance: tick cost, pack/decompression, snapshot copy, sorting, instance build, GPU.
- Device loss, resize stress, alt-tab, multi-monitor/DPI, minimize/restore.
- Pack compatibility/corruption, overlay stress, missing-pack behavior; memory/page budgets,
  allocation-free steady state, shutdown under load.
- AOT/trimming matrix, clean-machine deployment, validation matrix, flaky-test quarantine.
- API review (no freeze), docs final pass, and 0.1 release notes.

Exit: the 0.1 definition below is demonstrated and engine 0.1 is released. The public API remains
unfrozen and may break until 1.0, which is not planned.

## 6. Cross-cutting work

| Area | Policy |
| --- | --- |
| Documentation | Same-change docs for contracts; one fact, one owner (see [docs index](README.md)); decisions for public changes. |
| Tests | Unit tests each milestone; pack/format from M3; AOT gate from M1 (now `Pong.Desktop`); handoff stress from M7; GPU tests from M8 (local-only, structural/counter/contract assertions; no golden/pixel tests). |
| CI | PRs to `develop` and pushes to `develop`: docs link check, format, build, test. AOT is local; GPU tests stay local (no GPU CI runner); template build from M9. |
| NativeAOT | Analyzers + the `Pong.Desktop` publish/run gate; no reflection/dynamic code/TOML in runtime; generated MessagePack shapes. |
| Diagnostics | ZLogger/MEL, `IEngine.LoggerFactory`, counters, validation layers, labels; no silent failures. |
| Quality | Warnings-as-errors, style errors, format gate, AOT/trim analyzers on every project. |
| Scope | Audio/UI/physics/animation need a decision entry and roadmap change. |

## 7. 0.1 definition of done

Stage 1 ships as engine 0.1. **0.1 is not an API-stability milestone:** the public API stays
flexible until 1.0, which is not planned, and breaking changes are allowed throughout Stage 1.
Every public change still updates the API guide, reference, and decision log in the same change.

- **Engine/runtime:** generated game builds/runs on Windows 10/11 with Vulkan 1.3; NativeAOT desktop
  and headless server start/stop cleanly; windowed/offscreen/headless behave as documented;
  tick-scoped workers; builder-owned logging with `IEngine.LoggerFactory`.
- **Assets:** `assets.pck` format as specified; header compressed/checksummed; debug payloads
  uncompressed, release Zstd only when smaller; overlays/browse; missing/malformed base pack fails;
  textures/sprites baked from TOML; AOT uses generated shapes only.
- **Rendering:** Vulkan 1.3 dynamic rendering, bindless pages, instanced generated quads, one draw
  per non-empty camera, documented sorting/clear semantics, windowed present and offscreen
  discard, zero validation errors.
- **Process:** docs and decision log match the implementation; CI gates pass; warnings/style are
  errors; public API reviewed, with no freeze.

**Non-goals for 0.1:** 3D, HDR, MSAA, post-processing, physics, audio, in-game UI, networking,
scene editing, an editor, hot reload, multi-window, Linux/macOS guarantees, indexed non-Quad
drawing, asset/pack unloading, screenshot files, and public pixel-format/readback options beyond
`CaptureFrameAsync`.

## 8. Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| NativeAOT vs Silk.NET/native dependencies | High | AOT gate from M1 (now `Pong.Desktop`); pin versions; isolate adapters. |
| Pack format/overlay bugs | High | Versioned reader, bounds/duplicate tests, fuzzing, AOT tests. |
| Frame pacing races/deadlocks | High | Three-slot state machine, fake-fence stress, invariants, no sleeps. |
| Vulkan WSI/offscreen lifetime | High | Split windowed/offscreen paths; validation; local GPU tests. |
| Bindless limits on target GPUs | Medium | Startup feature validation; configurable capacity; explicit failure. |
| Documentation drift | Medium | One-fact ownership, same-change rule, link/anchor CI check. |

## 9. Open decisions

Resolved choices are recorded in the [decision log](decisions.md), including the M2 and M3
approvals; only unresolved choices are listed here.

The former M8 rows are resolved by the [M8 spec](specs/m8-sprite-renderer.md) (the owner-approved M8
record; **no `decisions.md` entry is added** by owner instruction): bindless page capacity is
**dynamic up to the device limit** (initial 256, doubling, explicit fatal at the limit); the shader
compiler is the **build-time Silk.NET.Shaderc 2.23.0 tool** with generated SPIR-V (no Vulkan SDK, no
runtime compiler); the readback/capture API is the public **`IEngine.CaptureFrameAsync`** in M8; and
the validation warning policy **keeps D-M6-21** (errors fail; warnings are logged/counted and fatal
in `[GpuFact]`).

| Decision | Needed by | Default if unresolved |
| --- | --- | --- |
| Multi-GPU / device override | M10 | First suitable device; no override in 0.1. |
| Packaging: project references vs NuGet | M9 | Project references in the template; NuGet later. |

The M6 allocator, sRGB, and frame-count choices are resolved in D-M6-03, D-M6-04, and D-M6-11. The
M7 headless timer/rate choice is resolved by D-M7-14/D-M7-25 (positive integer `ticksPerSecond`; `0`
is rejected).

## 10. Progress log

| Milestone | Status | Evidence |
| --- | --- | --- |
| M0 Documentation | Complete (2026-10-25) | Docs set, decision log, accepted ownership/pack/editor/pacing decisions. |
| M1 Foundation/AOT | Merged (2026-10-25, PR #1) | 135 tests; Debug/Release zero warnings; format clean; AOT smoke publishes/runs locally; CI build/test/format on PRs to `develop` and `develop` pushes. [M1 record](specs/m1-foundation.md). |
| M2 SDL platform | Merged (2026-10-26, PR #2) | 292 tests (283 in CI, 292 with `BORK2D_RUN_WINDOW_TESTS=1`); Debug/Release zero warnings; format/docs clean; AOT headless and `--windowed --ticks 120` exit 0 with `SDL2.dll`; live-window 5,000,000-tick run exit 0; Escape/close graceful. [M2 record](specs/m2-sdl-platform.md). |
| M3 Assets/pack | Merged (2026-10-26, PR #4) | 587 tests (578 deterministic + 9 opt-in real-window); Debug/Release zero warnings; format/docs clean; AOT publish zero AOT/trim warnings with `SDL2.dll`; default AOT run exit 0 loading a 2-entry temporary pack (raw + generated-shape MessagePack) and one tick; `--windowed --ticks 120` exit 0 with `WithoutAssets()`; crash-free pack/overlay failure matrix. [M3 record](specs/m3-assets.md). |
| Asset-reference refactor | Merged (2026-10-26, PR #6, D-035) | 528 tests (519 deterministic + 9 opt-in real-window); Debug/Release zero warnings; format/docs/`git diff --check` clean; AOT publish zero AOT/trim warnings; default AOT run exit 0 with a 5-entry pack including the public `SpriteAsset`/`TextureDescriptor` shapes; `--windowed --ticks 120` exit 0. [Refactor record](specs/refactor-asset-references.md). |
| M4 Serialization/bake | Merged (2026-10-27, PR #7) | WP0–WP6 landed: bake library + CLI, generated host, per-source `[bork2d]` metadata, descriptor/TOML binder, location-free texture pipeline, reference validation, structural determinism, incremental bake, MSBuild targets, `Bork2d.BakeFixture`, and the AOT baked-pack smoke. The fix-1 revision removed the descriptor `[[types]]`/prefix model, moved type metadata into each data TOML, allowed data/textures anywhere under the asset root, and changed raw payload paths to `<descriptor>-raw`. Fix-2 commits the fixture **source** PNG at `Bork2d.BakeFixture/Assets/art/player.png` (sidecar `art/player.png.toml`; data/sprite TOMLs reference `art/player`), removes the texture generator, and leaves test images/packs generated in temp directories. Fix-3 applies D-M4-14/D-M4-15: ordered `[[assets]]` multi-pack descriptors (first is main; no `name`/`[bake].pack`), one host over all roots, per-pack bake with main-only external references, internal `PackBuilder` external-main seam, `bake --output-root` (no `--output`), all-output incremental manifest schema 2, MSBuild bake/publish/clean for every declared pack, fixture `DLC1/data/dlc.toml`, and the AOT main + `dlc1.pck` overlay proof; texture sidecars are retained. The main fixture pack has 5 entries (ending in `art/player` and `art/player-raw`) and the DLC pack 1 (`data/dlc`). 911 deterministic + 12 conditional tests (923 total); Debug/Release zero warnings; format/docs/diff checks clean. Fix-3 AOT publish is zero-warning with `assets.pck`/`dlc1.pck`/`SDL2.dll` and no Bake/Tomlyn/StbImageSharp assemblies; the published headless run exits 0 with `mainEntries=5`, `mergedEntries=6`, the DLC → main texture reference, and 16 raw bytes; `--windowed --ticks 120` exits 0. Final CI run [40455684420](https://github.com/BorkEngine/Bork2d/actions/runs/40455684420) succeeded: 914 passed, 9 skipped (the 3 reparse tests ran with symlink capability; the local gate is 911 passed, 12 skipped). [M4 spec §22](specs/m4-serialization-and-bake.md#22-as-built-evidence-m4-merged-to-develop). |
| Post-M4 asset processors | Merged into `develop` (2026-10-27, PR #8, merge `a98e0ab`; CI green on `39563f9`) | D-036/D-037/D-038/D-039/D-040 as-built: public `Bork2d.Bake`, build-time `MyGame.Assets`/`Bork2d.BakeFixture.Assets`, explicit `RegisterAsset<T>()` + defaults, processor extension point with PPM/Unit acceptance processors, no descriptor/generated host/`Bake.Cli`, schema-3 incremental manifest and build targets, and the AOT fixture flow (8 main / 9 merged entries, no tooling assemblies in publish). D-040 makes the `TextureDescriptor` sampler fields the non-null `TextureFormat`/`TextureFilter`/`TextureWrap` enums. 946 deterministic + 12 conditional tests (958 total); Debug/Release, format, docs, and AOT gates pass locally. Design: [asset processors](architecture/asset-processors.md); spec: [file processors](specs/file-processors.md). |
| Project consolidation | Merged (2026-10-28, PR #9; D-041/D-042) | Five-project engine solution: `Bork2d.Engine` = Api+Common+Assets with `Bork2d.Engine.*` namespaces; `Bork2d.Bake` = Bake+Builder with the public `Bork2d.Bake` namespace; smoke/fixture in `examples/Bork2d.Examples.slnx`; Tests decoupled from the smoke; dependency tests and the Q-20 metadata guard. 942 deterministic + 12 conditional tests (954); Debug/Release zero warnings; both format gates; docs check; AOT headless and `--windowed --ticks 120` exit 0; PR CI run 40563643324 succeeded. [Spec](specs/refactor-projects.md). |
| Pong example | Merged into `develop` (2026-10-29, PR #10, merge `0273f58`) | Replaces the exploratory examples with `examples/pong` (`Pong.Common`, `Pong.Assets`, `Pong.Desktop`, `Pong.Tests`) and makes `Pong.Desktop` the NativeAOT gate. 20 Pong tests; engine suite 907 passed + 12 skipped (919) after WP-2; Debug/Release, tests, and both format gates zero-warning; AOT publish zero warnings with a 7-entry `assets.pck`, `Pong.Desktop.exe`, `SDL2.dll`, and no `Pong.Assets`/`Bork2d.Bake`/Tomlyn/StbImageSharp assembly; headless and `--windowed --ticks 120` exit 0; docs link check and `git diff --check` clean. [Spec](specs/pong-example.md#12-as-built-evidence). |
| M6 Vulkan bring-up | Merged into `develop` (2026-10-31, PR #11, merge `5de725c`) | D-M6-01…D-M6-27 recorded in the [decision log](decisions.md#m6-vulkan-bring-up-decisions-approved-2026-10-29). WP-1 recorded the decisions, WP-2 delivered the `Bork2d.Render` foundation (instance/validation/device selection and pure policies), WP-3 delivered the SDL Vulkan surface seam, SPSC control channel, and the windowed renderer (swapchain create/recreate, two frames in flight, dynamic-rendering clear, present, resize/fullscreen/minimize, surface-loss policy, counters, validation observation), WP-4 delivered the offscreen target/renderer (two permanent sRGB images, one allocation each, clear/discard, fence pacing), the Runtime composition (Setup-before-readiness, first-tick gate, offscreen `Exit`/`ITime`, error precedence, instance/surface wiring and surface-recovery request), and the deterministic host/platform tests, and WP-5 delivered the Pong `--offscreen --ticks N` mode and arguments tests, the three-mode AOT publish/run gate, and the final docs. Local evidence: **1044 engine tests** (1028 deterministic + 16 opt-in/conditional) and **41 Pong tests** green; the four `[GpuFact]` tests (windowed present/clear/resize/fullscreen/minimize and offscreen clear/discard/app loop, validation enabled) and nine `[WindowFact]` tests pass with zero validation errors/warnings; AOT publish zero warnings with `Pong.Desktop.exe`, `assets.pck`, `SDL2.dll`, and the engine/runtime/game PDBs (no `vulkan-1.dll`, no tooling assemblies); published `--headless`, `--windowed --ticks 120`, and `--offscreen --ticks 120` exit 0. **Merged** into `develop` (2026-10-31, PR #11, merge `5de725c`). [As-built evidence](specs/m6-vulkan-bringup.md#18-as-built-evidence-2026-10-30). |
| M7 Frame handoff/pacing | Merged into `develop` (2026-10-31, PR #12, merge `5de5ac3`) | WP-1–WP-5 landed sequentially: decisions `D-M7-01…D-M7-27`; the Engine snapshot model/capture and public `FlipX`/`FlipY` (Q2=B1); the `Bork2d.Render` three-slot ring, consumer, fence-held slots, suspended minimized drain, and empty instance resources (Q1/Q7/Q8/Q10); the Runtime windowed/offscreen acquire/capture/publish loop with backpressure and the paced headless K1 loop with L2 synthesized time and M4/F1 pacing; and the Pong `--headless` 120-tick default/`--headless --ticks N` gate. Local evidence: **1158 engine tests** (1141 deterministic + 17 opt-in/conditional) and **46 Pong tests** green; Debug/Release zero warnings; both format gates, docs check, and `git diff --check` clean; five `[GpuFact]` tests pass with zero validation errors/warnings; 10,000-snapshot stress and slot invariants pass; AOT publish zero warnings with `Pong.Desktop.exe`, `assets.pck`, `SDL2.dll`, and the engine/runtime/game PDBs (no `vulkan-1.dll`, no tooling assemblies); published `--headless` (1/120/1), `--headless --ticks 7` (1/7/1), `--windowed --ticks 120` (1/120/1), and `--offscreen --ticks 120` (1/120/1) exit 0. **Merged** (2026-10-31, PR #12, merge `5de5ac3`). [As-built evidence](specs/m7-frame-handoff.md#18-as-built-evidence-2026-10-31). |
| M8 Sprite renderer | In progress (WP-1/WP-2/WP-3 done; not merged) | Owner-approved [M8 spec](specs/m8-sprite-renderer.md) (2026-10-31): five sequential WPs `m-8/wp-1`…`m-8/wp-5`. WP-1 reconciled the owning docs. WP-2 added the build-only `Bork2d.ShaderCompiler` tool + `build/Bork2d.ShaderCompiler.targets`, the checked-in GLSL and generated SPIR-V, the internal sprite pipeline/layout/sampler cache created by both renderers, the shared camera projection/rounding helper, the public `ICamera.WorldToScreen`/`ScreenToWorld`/`WorldSize` members, and the pure sprite sort/filter/quad/96-byte instance layout. WP-3 added the internal texture-page subsystem (one page per path, async `IAssets` prefetch, content validation, the generated 8×8 placeholder uploaded at startup, device-local image blocks, staging pool, per-page upload fences, bindless descriptor writes, dynamic capacity with an explicit limit failure), the replacement per-frame binding 0/1 resources, the extended `VulkanDeviceLimits`, the required internal `RendererStartInfo.Assets` seam, and the public `RenderStats`/`IEngine.RenderStats` with the runtime stats source. Local WP-2 evidence: **1252 engine tests** (1235 deterministic + 17 conditional); local WP-3 evidence: **1310 engine tests** (1293 deterministic + 17 conditional; 1310 run with the 5 GPU + 9 window opt-ins), Debug/Release zero warnings, format/docs checks clean, 5 `[GpuFact]` tests with zero validation errors and leak-free placeholder/startup shutdown, and a zero-warning AOT publish (8.3 MB exe, no `Bork2d.ShaderCompiler`/Silk.NET.Shaderc assembly, no loose `.spv`) whose `--headless`, `--offscreen --ticks 120`, and `--windowed --ticks 120` runs exit 0 with `texturePages=1`/`texturePageCapacity=256`/`placeholder=on`. Per-camera draws, `CaptureFrameAsync`, Pong visuals, and the benchmark remain WP-4/WP-5. |
| M9 Template/samples | Partially started (Pong sample merged) | `examples/pong` is the first sample; the solution template, packaging, and the remaining samples are pending. |
| M10 Hardening/0.1 | Not started | — |

**Planning change (2026-10-27):** M5 (Editor) was removed from Stage 1; M6–M10 IDs are unchanged.
No editor work is planned.

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
