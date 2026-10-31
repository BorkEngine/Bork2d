# M8 — Sprite Renderer with Real Assets (spec)

> **Status: Approved for implementation (2026-10-31); WP-1 through WP-4 are complete.** This spec
> is the owner-approved M8 contract. Per the owner's decision, the answers are recorded directly here:
> there is no separate M8 decision-register section, no `D-M8-xx` entries are added to
> [decisions.md](../decisions.md), and the previous question history is intentionally not preserved.
> This is an explicit owner exception to the usual decision-log process; the implementation docs
> still change in the same work packages.
>
> As built: WP-1 reconciled the owning docs; WP-2 added the build-time shader compiler/tool, the
> checked-in GLSL and generated SPIR-V, the sprite pipeline/layout/samplers, the shared camera
> projection/rounding helper, the public `WorldToScreen`/`ScreenToWorld`/`WorldSize` members, and the
> pure sprite planning/quad/96-byte instance layout with deterministic tests. WP-3 added the internal
> texture-page cache (async `IAssets` prefetch, content validation, generated placeholder, device-local
> image pool, staging pool, per-page upload fence, bindless descriptors, dynamic capacity), the
> required internal `RendererStartInfo.Assets` seam, and the public `RenderStats` type plus
> `IEngine<T>.RenderStats` with a host-installed, allocation-free source. WP-4 added the draw
> integration (one plan for both targets, per-camera passes/draws, growable mapped instance buffers,
> per-frame `RenderStats` publication, and public `IEngine<T>.CaptureFrameAsync` with pooled staging)
> plus deterministic and opt-in GPU coverage. WP-5 (the 165 FPS benchmark, Pong/AOT presentation gate,
> final docs, and milestone PR) remains planned.
>
> M7 is merged into `develop` (2026-10-31, PR #12, merge `5de5ac3`) and supplies the immutable frame
> snapshot, the three-slot ring, the one-frame-per-snapshot renderer, and the empty per-frame
> instance resources M8 fills. M8 is roadmap
> [M8 — Sprite renderer with real assets](../roadmap.md#5-milestone-detail).

## 1. Goal

M8 turns the M6/M7 clear-only renderer into a sprite renderer:

- resolve a packed `SpriteAsset` → `TextureDescriptor` → raw RGBA8 payload → a resident **GPU
  texture page**;
- build one instanced record per drawable sprite per camera and render it with **one instanced draw
  per non-empty camera**, using a shader that generates the quad corners (no vertex buffer);
- implement camera projection (`Size` is the half visible height, Y-up world), normalized
  viewport → rounded pixel viewport/aspect, and the documented sort/filter/clear/overlap semantics;
- draw the documented sprite state (transform, layer, `ZIndex`, tint, `FlipX`/`FlipY`) and handle
  invalid content explicitly: malformed sprite fields fail the renderer; missing/broken textures
  draw a generated placeholder;
- add the approved public API: `ICamera.WorldToScreen`/`ScreenToWorld`/`WorldSize`,
  `IEngine.RenderStats`, and `IEngine.CaptureFrameAsync`;
- keep the M7 handoff/fence/pacing behavior unchanged, keep windowed and offscreen parity, and add
  zero steady-state allocations in the frame path;
- add internal counters/logging, deterministic planning/math tests, opt-in GPU tests, and the
  50,000-sprite 165 FPS benchmark;
- make the Pong example visibly render its paddles and ball and keep the NativeAOT gate
  zero-warning.

The milestone is done when its exit criteria are demonstrated, not when the code merges.

## 2. Milestone definition and approved deltas

The roadmap [M8 detail](../roadmap.md#5-milestone-detail) says:

> Goal: render packed sprite assets with one instanced draw per non-empty camera.
>
> - Sprite pipeline from MessagePack assets (UVs, pixels-per-unit scale, pivot, `Quad`; no vertices).
> - Texture descriptors + RGBA8 payloads → bindless texture pages; instance SSBO carries UVs,
>   world-unit size, pivot, world transform, tint, texture index; vertex shader generates corners.
> - Camera projection (`Size` half-height, Y-up), viewport/aspect, screen/world helpers; cameras
>   ascending `ZIndex`; layers, z-order, tint, transforms; one dynamic-rendering pass and one draw
>   per non-empty camera; clear/overlap semantics; split-screen and offscreen.
> - 0.1 draws `Quad`; non-Quad assets are preserved for the future indexed path. Sprite field
>   validation (UVs, pivot, topology, `indices`) is not part of Stage 1.
> - Debug counters/validation/capture; golden tests; benchmark at 1920×1080 with no artificial cap.
>
> Exit: packed scene renders with exactly one draw per non-empty camera; layers/z/tint/split-screen/
> camera/clear semantics match docs; 50,000 sprites sustain the measured target; no steady-state
> allocations; validation clean; offscreen matches windowed; not-ready sprites are skipped.

**Approved deltas from the roadmap and the target architecture.** These are the owner's decisions
and supersede the older wording; the owning docs are updated in the implementation WPs.

| Area | Approved M8 decision | Supersedes |
| --- | --- | --- |
| Render pass structure | **One dynamic-rendering pass per camera**, over that camera's pixel viewport; `loadOp=CLEAR` for `Clear.Color`, `LOAD` for `Clear.None`/`Clear.Depth`. **No frame-level safety-clear pass.** Areas outside all camera viewports are not cleared. | The one-pass-per-frame and safety-black statements in [Rendering §3.1/§8](../architecture/rendering.md#31-windowed). |
| Public API | Add `ICamera.WorldToScreen`/`ScreenToWorld`/`WorldSize`, `IEngine.RenderStats`, and `IEngine.CaptureFrameAsync`. | The "no public readback in 0.1" and "no screen/world helpers yet" statements. |
| Visual tests | **No golden/pixel tests.** GPU tests assert counters, validation, lifecycle, and the readback API contract; visual correctness is a manual windowed check. | The roadmap "golden tests" bullet and the "no readback" ambiguity. |
| Texture failure | Missing/malformed/unsupported textures mark the path unloadable and draw a generated **magenta/black checkerboard placeholder**; they do not skip the sprite. | The "missing texture is simply non-drawable" wording for M8. |
| Malformed sprite fields | A malformed `SpriteAsset` field is a **fatal renderer error** naming the field and texture reference. Non-`Quad` topology stays non-fatal and is skipped with a diagnostic. | The "no runtime checks at all" ambiguity; Stage 1 still has no bake/load validation. |
| Texture loading | Renderer reads through the app's `IAssets` seam, prefetches asynchronously, uploads via a separate submit + fence, uses one page per descriptor path, pools GPU memory, and grows page capacity dynamically. | The per-resource allocator and fixed-capacity defaults. |
| Shaders | Build-time **Silk.NET.Shaderc 2.23.0** tool compiles checked-in GLSL; SPIR-V is generated C#/embedded for the runtime. No runtime compilation and no Vulkan SDK requirement. | The `glslangValidator` build-target default and committed-SPIR-V alternatives. |
| Performance gate | 50,000 sprites, one camera, 1920×1080, median **≥165 FPS** in the opt-in offscreen benchmark. | The provisional "under ~2 ms / 165 Hz as a goal" wording. |
| GPU CI | GPU tests stay **local-only**; CI is unchanged. | The roadmap "GPU runner from M8" line. |
| Work packages | Five **sequential** WPs (`m-8/wp-1`…`m-8/wp-5`). | No prior M8 plan. |

## 3. Baseline at spec time (2026-10-31)

### 3.1 Code and test facts

M6/M7 are merged. The current tree contains:

| Area | Current state | M8 consequence |
| --- | --- | --- |
| Engine suite | **1158** tests at spec time = 1141 deterministic + 17 opt-in/conditional (9 `[WindowFact]`, 3 reparse-point, 5 `[GpuFact]`). WP-2 raised the suite to **1252** = 1235 deterministic + 17 conditional; WP-3 raised it to **1310** = 1293 deterministic + 17 conditional; WP-4 raises it to **1360** = 1334 deterministic + 26 conditional (9 `[WindowFact]`, 3 reparse-point, 14 `[GpuFact]`). | M8 adds deterministic plan/math/page/public-API tests and opt-in GPU rendering tests. |
| `Pong.Tests` | **46** tests. | Pong already builds a camera and three sprites; M8 changes what is drawn and what the gate can assert. |
| Scene | `Camera<TLayer>`/`SpriteRender<TLayer>` store raw values with no validation or clamping. `Viewport`, `Size`, `ClearColor`, `Tint`, `Layer`, `ZIndex`, and `FlipX/Y` are settable without range checks. WP-2 adds the shared projection helper and the public `ICamera` screen/world/`WorldSize` members; the renderer still handles invalid values at draw time (cameras skip; malformed sprites are fatal) and the setters stay unchanged. | M8 handles invalid values at draw time (cameras skip; malformed sprites are fatal); setters stay unchanged. |
| Snapshot | M7 `FrameSnapshot`/`CameraSnapshot`/`SpriteSnapshot` are internal, immutable after publication, and value-only except the loaded `SpriteAsset?`. Exact fields are in [Threading §3](../architecture/threading.md#3-frame-snapshot). Capture copies raw camera/sprite values; invalid layers are preserved as `uint`. | The snapshot stays asset-agnostic. The renderer reads `IAssets` directly and resolves textures; no snapshot change. |
| `Bork2d.Render` | Internal `IRenderer` seam, SPSC control channel, windowed and offscreen renderers, three-slot handoff consumer, fence-held slots, suspended minimized drain, counters, validation observation. WP-2 adds the internal `SpritePipeline` (layout/samplers/pipeline-per-format) created at renderer startup, the shared camera-projection bridge, and the pure sprite planner/quad/96-byte instance layout. | M8 adds the page cache/uploader, bindless descriptor set, instance filling, per-camera passes, public stats/capture plumbing, and counters; no public Render type. |
| Per-frame GPU resources | WP-3 replaces the provisional M7 resources: each frame owns a 1024-record (96-byte) host-visible coherent instance SSBO (binding 0) and a bindless descriptor pool/set (binding 1, variable count = current page capacity, `UPDATE_AFTER_BIND`). WP-4 makes the instance buffer growable and mapped once, fills it through the planner, and records the per-camera draws. | WP-4 grows the instance buffer, fills it, and records the per-camera draws. |
| Bindless readiness | Device selection already requires Vulkan 1.3, dynamic rendering, synchronization 2, and the six descriptor-indexing features (`D-M6-08`). WP-3 adds `maxDescriptorSetUpdateAfterBindSampledImages` and `maxStorageBufferRange` to `VulkanDeviceLimits`, validates the initial page capacity, and uses the smaller of the per-stage/per-set update-after-bind sampled-image limits as the layout maximum (saturated to a documented 1,048,576-entry cap when the device reports an unbounded `UINT32_MAX`). | WP-4 uses the bindless pages in the draw; capacity still grows to that effective limit. |
| Texture pages | WP-3 adds the internal `Bork2d.Render.Textures` subsystem: one page per canonical descriptor path, async `IAssets` prefetch with polled completion, content validation, the generated 8×8 placeholder at index 0 (uploaded at startup with a fence wait), a device-local image suballocator, a host-visible staging pool, per-page one-time upload command buffers/fences, bindless descriptor writes, and dynamic capacity with `vkDeviceWaitIdle` growth. WP-4 calls `EnsurePage` per candidate sprite in the plan pre-pass and adds cross-page/content diagnostics and GPU growth coverage. | WP-4 calls `EnsurePage` per candidate sprite and adds cross-page content tests/diagnostics. |
| Shaders/pipeline | WP-2 adds the checked-in GLSL (`Bork2d.Render/Shaders/sprite.vert`, `sprite.frag`), the build-only `build/Bork2d.ShaderCompiler` tool and MSBuild targets, generated SPIR-V C#, and the internal `SpritePipeline` (descriptor-set layout, pipeline layout with a 64-byte push constant, one graphics pipeline per sRGB output format, and a `(filter, wrap)` sampler cache). Both renderer startups create it after the color format is known and destroy it in cleanup. WP-4 binds the pipeline/descriptor set, pushes the view-projection, and records one instanced draw per non-empty camera. | M8 adds checked-in GLSL, a build-time Silk.NET.Shaderc tool, generated SPIR-V, and the sprite pipeline; WP-3/WP-4 write descriptors and record draws. |
| Texture upload | `TextureDescriptor` is a public MessagePack model; `IAssets.LoadData` returns decompressed bytes on the loader thread. WP-3 adds the required internal `RendererStartInfo.Assets` seam, the async prefetch (descriptor then payload), content validation, the placeholder, the pooled GPU upload path with per-page fences, and capacity growth. | WP-4 resolves pages per candidate sprite in the draw/plan path. |
| Readback | Offscreen rendering discards unless a capture is requested. WP-4 adds the public `IEngine.CaptureFrameAsync` readback path with pooled per-frame staging; the engine's own tests still do not assert pixels. | M8 adds the public `IEngine.CaptureFrameAsync` readback path (WP-4); the engine's own tests still do not assert pixels. |
| GPU tests | 5 `[GpuFact]` tests, skipped unless `BORK2D_RUN_GPU_TESTS=1`; serialized `RealGpu` collection; bounded joins; validation enabled. | M8 adds rendering/benchmark tests in the same local-only pattern. |
| Pong game | `Setup` loads settings/sprites/descriptors/raw bytes, creates one camera (`Size=9`, `Clear.Color`, mask `Court|Paddles|Ball`, `ZIndex=0`), one world transform, two paddle sprites, and one ball sprite. Assets use full-rect UVs, `scale=64`, center pivot, `quad`. The baked pack has 7 entries. | After M8 the same scene draws three sprites; the gate asserts render stats and AOT behavior. |
| Host | Windowed/offscreen publish one snapshot per completed tick; the renderer consumes one for a free frame and holds the slot to the fence. Headless has no renderer/capture. WP-4 installs the renderer capture source before `Setup`, the headless `NotSupportedException` source at `Run` start, and disposes the capture lifecycle when `Run` returns. | M8 keeps the loop/handoff; the host passes `IAssets` to the renderer and wires public stats/capture. |
| Docs status | WP-1 reconciled the owning docs; WP-4 updates them as-built for the draw/capture/stats work. The M7 black-clear statements are superseded by per-camera passes; the WP-5 benchmark/Pong presentation gate remains open. | M8 updates them in its documentation WP. |

### 3.2 Environment facts

Same local machine as the M6/M7 evidence: Windows 11 Pro 10.0.26300; AMD Ryzen AI MAX+ 395 with
Radeon 8060S (Vulkan device API 1.4.359, driver 32.0.32015.7029); Vulkan loader/SDK 1.4.363.0 with
`VK_LAYER_KHRONOS_validation`; .NET SDK `11.0.100-rc.1.26425.128` pinned by `global.json`.

For M8 specifically:

- `Silk.NET.Shaderc` **2.23.0** and `Silk.NET.Shaderc.Native` **2.23.0** exist on nuget.org and match
  the repository's Silk.NET 2.23.0 line. They are **build-time only** and never enter the runtime or
  AOT publish graph.
- `glslangValidator`, `glslc`, and `spirv-val` happen to be installed at
  `C:\VulkanSDK\1.4.363.0\Bin\` on this machine; they are **not** build requirements under the
  approved Silk.NET.Shaderc path. CI still installs only the .NET SDK.
- No shader source or SPIR-V is committed today; there is no shader target or build-tool project.
- `Bork2d.Render` is `IsAotCompatible` with trim/AOT analyzers as errors; `Pong.Desktop` is the only
  `PublishAot` project. The public readback/stats API must stay AOT-safe.
- GPU/visual tests, the benchmark, and the AOT publish/run remain local-only.

### 3.3 Reconciled statements

These documentation/spec defects are resolved by this spec and fixed in the implementation docs.

| Location | Old statement | M8 resolution |
| --- | --- | --- |
| [Rendering §4](../architecture/rendering.md#4-snapshot-to-instances) | The indexed non-Quad path is "M8 work." | **Not M8.** Draw `Quad` only; preserve non-Quad assets for the future indexed path; skip non-Quad with a diagnostic. The rendering sentence is corrected. |
| [Roadmap M8](../roadmap.md#5-milestone-detail) | "screen/world helpers" are listed. | **Added in M8:** `ICamera.WorldToScreen`/`ScreenToWorld`/`WorldSize` with the semantics in [§6.9](#69-public-api). |
| [Roadmap M8](../roadmap.md#5-milestone-detail) | "golden tests" are listed. | **Dropped for M8.** No pixel/golden tests; GPU evidence is structural plus a manual visual check. The roadmap bullet is corrected. |
| [Assets and tooling §9](../architecture/assets-and-tooling.md#9-sprite-assets), [API reference §11](../api/reference.md#11-value-types) | Sprite field validation belongs to M5. | M5 is removed. There is **no** bake/load validation in Stage 1; the renderer's defensive draw rules in [§6.4](#64-sprites-validity-and-flips) apply. |
| [Roadmap §6](../roadmap.md#6-cross-cutting-work) | "GPU runner from M8." | **GPU tests stay local-only;** CI is unchanged. |
| [D-M6-21](../decisions.md#m6-vulkan-bring-up-decisions-approved-2026-10-29) vs [Roadmap §9](../roadmap.md#9-open-decisions) | Warnings are fatal in `[GpuFact]`; the roadmap default says warnings are reviewed. | **Keep D-M6-21:** errors fail; warnings are fatal in `[GpuFact]`; release without validation logs. Draw diagnostics are throttled and counted. |
| [D-M6-03](../decisions.md#m6-vulkan-bring-up-decisions-approved-2026-10-29) | One allocation per resource; no suballocator/VMA in M6; revisit in M8. | **Pool/suballocator** for page images plus a staging ring; no VMA. |
| [D-M6-23](../decisions.md#m6-vulkan-bring-up-decisions-approved-2026-10-29) | No readback in M6; internal readback is future work. | **Public readback is added in M8** (`IEngine.CaptureFrameAsync`); offscreen still discards when no capture is requested. |
| [Rendering §3.1/§8](../architecture/rendering.md#31-windowed) | One dynamic-rendering pass per frame; safety clear to opaque black. | **Superseded:** one pass per camera; no safety pass; `Clear.Color`/`Clear.None` use per-pass load ops. See [§6.8](#68-sorting-filtering-and-clears). |
| [D-M6-22](../decisions.md#m6-vulkan-bring-up-decisions-approved-2026-10-29) | Draw labels are M8. | Static labels for the pipeline, page images/views/samplers, instance buffers, and descriptor sets; **no per-draw or per-camera label churn**. |
| [M7 spec §15](m7-frame-handoff.md#15-deferred-open-in-later-milestones) | Sprite pipeline/pages/draws are M8. | Implemented here; the M7 empty instance resources are replaced. |

## 4. Inherited constraints and supersessions

| Constraint | Source | M8 status |
| --- | --- | --- |
| One draw per **non-empty camera**; camera order ascending `ZIndex`; ties unspecified. | D-007, D-026; [Rendering §3.1/§8](../architecture/rendering.md#31-windowed) | Kept. "Non-empty" = at least one drawn instance (§6.8). |
| +X right, +Y up; rotation degrees CCW normalized; `Camera.Size` is half visible height; sprite `Scale` is pixels per world unit; shader-generated corners, no vertices array. | D-008, D-025; [Rendering §7](../architecture/rendering.md#7-coordinates-camera-and-projection) | Kept. |
| Vulkan 1.3, dynamic rendering, bindless pages, one pipeline, no depth/MSAA/HDR, straight alpha, layer-then-`ZIndex` sort. | D-009, D-026 | Kept, except the pass structure is per camera (§6.8). |
| `SpriteAsset` fields and `TextureDescriptor` payload/dimensions/format/filter/wrap semantics. | D-035, D-040; [Assets §8–§9](../architecture/assets-and-tooling.md#8-texture-descriptors) | Kept. |
| M7 snapshot is internal, value-only, asset-agnostic except `SpriteAsset?`, immutable after publication; capture after `Tick`. | D-M7-05, D-M7-06; [Threading §3](../architecture/threading.md#3-frame-snapshot) | Kept. Render resolves textures via `IAssets`; no snapshot change. |
| One GPU frame per consumed snapshot; slot held to the fence; minimized/zero-extent drain with no GPU work. | D-M7-07, D-M7-08 | Kept. |
| Two frames in flight; two offscreen targets; three swapchain images; per-frame command buffer/pool/fence/semaphores. | D-M6-11 | Kept; M8 adds per-frame instance buffers and bindless sets. |
| Device selection requires the six descriptor-indexing features and validates the update-after-bind sampled-image limit. | D-M6-08 | Kept; the page capacity is dynamic up to that limit. |
| `Bork2d.Render` is internal-only and references `Bork2d.Engine` + `Silk.NET.Vulkan*`; no public Render types. | D-001, D-034, D-041 | Kept. The new public API lives in `Bork2d.Engine`. |
| NativeAOT/trim-safe; no runtime shader compilation/reflection/dynamic code; AOT publish is a local gate; GPU tests are local. | D-016, D-M6-05 | Kept. Shaders compile at build time with Silk.NET.Shaderc. |
| Warnings/style/format/docs/AOT are gates; no per-frame logging; no `Thread.Sleep` in windowed/offscreen except the minimized cap. | D-022, D-M7-08 | Kept. |
| Validation errors fail with validation enabled; warnings are logged/counted and fatal in `[GpuFact]`. | D-M6-21 | Kept. |
| Tick-scoped scene mutation; renderer never reads live scene state; asset loader is a background thread. | D-015, D-024 | Kept; `IAssets` calls from the render thread are allowed and asynchronous. |

**M8 baseline that must not break:** windowed presents and offscreen discards one frame per consumed
snapshot; the M7 ring/fence/backpressure/wake/shutdown semantics; the M6 resize/fullscreen/minimize/
surface-recovery behavior; headless never touches SDL/Vulkan; the builder surface stays unchanged
except the new public members listed in [§6.9](#69-public-api); the AOT publish contains no tooling
assembly and no `vulkan-1.dll`.

## 5. Scope

### 5.1 In scope

- **Texture resolution and pages:** `IAssets` seam, async prefetch, page cache keyed by descriptor
  path, pool/suballocator, separate upload submit + fence, placeholder on content failure, dynamic
  capacity growth, and bindless binding 1 with a variable descriptor count.
- **Sprite resolution:** size from UV span × texture dimensions / `Scale`, pivot, UV corners, world
  transform, tint, flips, layer/`ZIndex`, per-camera filtering, and the approved validity/fatal/
  placeholder/skip rules.
- **Camera and clear semantics:** view/projection, rounded pixel viewport/aspect, invalid-camera
  skip, per-camera passes with `Clear.Color`/`Clear.None`/`Clear.Depth`, split-screen, empty cameras,
  and offscreen/windowed parity.
- **Pipeline and shaders:** GLSL vertex/fragment shaders, build-time Silk.NET.Shaderc compilation,
  generated SPIR-V, pipeline/layout/descriptor set, push constants, samplers, blending, and draws.
- **Instance storage:** growable per-frame host-visible instance buffers, 96-byte records, camera
  ranges/`firstInstance`, and the one-draw contract.
- **Public API:** `ICamera.WorldToScreen`/`ScreenToWorld`/`WorldSize`, the immutable `RenderStats`
  snapshot on `IEngine`, and `IEngine.CaptureFrameAsync`.
- **Diagnostics:** internal counters, throttled logs, validation observation, and static debug
  labels for the new objects; no per-frame/per-sprite logging.
- **Evidence:** deterministic plan/math/page/public-API tests; opt-in GPU rendering tests; the
  local 50,000-sprite 165 FPS benchmark; Pong/AOT gate updates.
- **Docs:** the owning architecture/API/guide/roadmap/projects/nativeaot/logging/contributing docs.

### 5.2 Out of scope

- **Indexed non-Quad drawing** (`TriangleStrip`/`Triangles` with `Indices`); M8 draws `Quad` only and
  preserves non-Quad assets for the future path.
- **Sprite field validation at bake/load, slicing, atlases, animation, editor preview.**
- **Mipmaps, KTX2/compressed textures, anisotropy, LOD/bias, texture arrays, sub-image upload.**
- **Texture eviction/streaming/reload, pack unloading** (pages live for the run).
- **Depth, MSAA, HDR, post-processing, compute, indirect draws, batching across cameras, GPU
  timestamps.**
- **Device loss, multi-GPU/device override, a transfer queue, VMA/allocator overhaul.**
- **Golden/pixel tests** (no test readback; the public readback API is not used for visual
  assertions).
- **Public builder options** for page/instance capacity; both capacities grow dynamically.
- **New public Render types**; all new public types/members live in `Bork2d.Engine`.
- **Multi-window, Linux/macOS guarantees, hot reload, editor, audio/UI/physics/animation.**

## 6. Architecture

### 6.1 Modules and boundaries

| Piece | Responsibility | Ownership | Boundary |
| --- | --- | --- | --- |
| Snapshot/capture | M7 immutable frame input (cameras, sprites, world matrices, tint, layer, z, flips). | `Bork2d.Engine` (internal, existing). | No Vulkan/Render dependency; no live entity references. |
| Texture asset source | The app's `IAssets` service, passed to the renderer so it can resolve descriptors and payloads. | `Bork2d.Engine.Assets` (public interface), implemented by `AssetService`; `Bork2d.Runtime` passes it. | Renderer calls `LoadAsset<TextureDescriptor>`/`LoadData`; no Runtime reference, no Vulkan types. |
| Page cache/uploader | Page records, async prefetch, placeholder, pool/suballocator, staging ring, upload fences, bindless descriptors, dynamic capacity. | `Bork2d.Render` (internal). | Render thread only; no public types. |
| Instance planner/builder | Sort/filter/validate, quad math, per-camera ranges, instance records, growable buffers. | `Bork2d.Render` (internal; pure parts testable). | Reads snapshot and loaded assets only; never scene entities. |
| Pipeline/shader | GLSL sources, build-time SPIR-V generation, pipeline/layout, push constants, samplers, draws. | `Bork2d.Render` (internal) plus the build-time `Bork2d.ShaderCompiler` tool. | No runtime compiler/native shader dependency; no tooling in publish. |
| Public stats/capture bridge | Copy per-frame stats and serve `CaptureFrameAsync` requests. | `Bork2d.Runtime` wires an internal bridge; `Bork2d.Engine` exposes the public members. | No Render type in a public signature; no Vulkan types leave Render. |
| Draw pass | Per-camera dynamic-rendering passes, viewport/scissor, clears, one draw per non-empty camera. | `Bork2d.Render` (internal). | Uses snapshot values, pages, and instances. |
| Host integration | Pass `IAssets`, wire stats/capture, keep the M7 loops and shutdown order. | `Bork2d.Runtime` (composition root). | No public change beyond §6.9. |
| Pong | Keep the existing camera/three sprites; expose render stats for the gate. | Example. | Game references Engine/Runtime only. |

Boundary rules:

- No public type in `Bork2d.Render`; no `Bork2d.Render` reference from `Bork2d.Engine`; no Vulkan/SDL
  type outside `Bork2d.Render`/`Bork2d.Runtime`.
- `RendererStartInfo` gains a required `IAssets Assets` member (internal start info only). It is
  supplied after `assetService.Start()` and remains valid until the renderer joins, which is before
  `assetService.Stop()`.
- The public readback/stats members are implemented by `GameEngine` delegating to an internal
  bridge installed by `Bork2d.Runtime`; no Render type appears in a public signature.
- New source folders follow the folder-per-area namespace rule (IDE0130). Shader files are content
  under `Bork2d.Render/Shaders/`; the compiler tool is a build-time project, not a runtime namespace.

### 6.2 Frame flow

M7's consumer selects a free GPU frame and the newest `Ready` snapshot. M8 inserts the sprite work:

```text
ProcessControlMessages / wait for snapshot / wait frame fence / release old slot   (M7, fixed)
slot = TakeLatestReady()                                                            (M7, fixed)
candidates = SortAndFilter(snapshot)          // (Layer, ZIndex), mask, null/invalid checks
for each candidate:
    EnsurePage(texturePath)                   // prefetch/cache -> ready | pending | failed
plan = BuildPlan(candidates, pages)            // skip pending; placeholder for failed; fatal fields
grow this frame's instance buffer if plan.InstanceCount > capacity (fence is free)  (M8)
write instance records into the host-visible buffer
upload completed pages? -> record copy submit + fence for pending uploads            (M8)
Record:
    image barrier to COLOR_ATTACHMENT_OPTIMAL (windowed; offscreen is already there)
    for camera in ascending ZIndex:
        rect = rounded pixel viewport (or skip invalid camera)
        begin ONE dynamic-rendering pass with RenderArea = rect,
              loadOp=CLEAR (Clear.Color) / LOAD (Clear.None or Clear.Depth)
        if instances in this camera: set viewport/scissor; push viewProjection; bind set; draw
        end rendering
    if a capture is pending: copy the composited image to a host-visible buffer (before present)
    image barrier to PRESENT_SRC_KHR (windowed)
Submit; present (windowed) / discard (offscreen); fence releases the slot; publish stats/capture.
```

There is **no frame-level safety pass**. A frame with no cameras records no pass; a frame whose first
camera uses `Clear.None` does not clear the target. This is the approved behavior; docs and tests must
state it.

### 6.3 Cameras, projection, and viewport

**Fixed and approved:**

- World is +X right/+Y up; the camera's world transform is the inverse-view source:
  `view = inverse(cameraWorldMatrix)`.
- `Size` is the half visible height in world units. With a rounded pixel viewport of `W × H`, the
  aspect is `a = W / H`, `visibleHeight = 2 * Size`, `visibleWidth = 2 * Size * a`.
- With view-space `(xv, yv)`: `xNdc = xv / (Size * a)`, `yNdc = -yv / Size`, `zNdc = 0`. The
  negation is the only Y-up → Vulkan Y-down conversion.
- `Viewport` is normalized `(x, y, width, height)` with a top-left origin and defaults to
  `(0,0,1,1)`.
- **Framebuffer source.** The windowed renderer's framebuffer is its swapchain extent; the offscreen
  renderer's is its fixed target extent. The public camera helpers read the engine window's
  `IWindow.Size`, which is the framebuffer size in physical pixels (owner-approved M8 addition; the
  helpers therefore throw `InvalidOperationException` in headless/offscreen apps, where the stub has
  no framebuffer).
- **Visible world size (owner-approved M8 addition).** `ICamera<TLayer>.WorldSize` returns
  `(2 * Size * a, 2 * Size)` using the same rounded pixel-viewport aspect `a` as the projection.
- Camera scale is not zoom. A non-unit camera scale is unsupported.
- v1 culls by layer only; off-viewport sprites are still instanced and clipped by the GPU.
- Cameras apply in ascending `ZIndex`; ties are unspecified.
- `Clear.Color` clears the camera viewport; `Clear.None` preserves earlier output; `Clear.Depth` is
  reserved, logs once, and behaves like `Clear.None`.

**Viewport rounding.** For framebuffer `W × H` and a finite normalized viewport:

```text
roundHalfUp(v) = floor(v + 0.5)
left   = clamp(roundHalfUp(x * W), 0, W)
top    = clamp(roundHalfUp(y * H), 0, H)
right  = clamp(roundHalfUp((x + width) * W), 0, W)
bottom = clamp(roundHalfUp((y + height) * H), 0, H)
pixelWidth  = right - left
pixelHeight = bottom - top
```

If `width`/`height` is zero or negative before rounding, or the resulting `pixelWidth`/
`pixelHeight` is zero, the camera is invalid and is skipped. A positive viewport outside `[0,1]` is
clamped to the framebuffer. Projection uses `a = pixelWidth / pixelHeight` (the rounded aspect).

**Invalid camera (draw-time skip, no setter/capture change).** The whole camera, including its
clear, is skipped with a throttled diagnostic when:

- `Size <= 0` or is NaN/infinite;
- `Viewport` is non-finite or its width/height is <= 0;
- the rounded pixel rect is empty;
- `ClearKind` is not `None`/`Depth`/`Color`;
- `Clear.Color` has a non-finite `ClearColor`;
- the camera world matrix is singular/non-invertible;
- the camera world matrix has a non-unit scale (within a small implementation epsilon).

There is no validation in the public setters or in M7 capture. `WorldToScreen`/`ScreenToWorld`/
`WorldSize` throw `InvalidOperationException` for an invalid camera (see §6.9).

**As built (WP-2/WP-4).** The pure helper lives in `Bork2d.Engine.Rendering.CameraProjection`; both
`Camera<TLayer>` and the renderer's `SpriteCameraProjection` bridge call it, so the public helpers and
the GPU projection share one rounding/aspect/Y-flip implementation. WP-4 sorts the captured cameras by
`ZIndex`, skips invalid cameras (including their clear) with the throttled diagnostic, and records one
dynamic-rendering pass and at most one instanced draw per valid camera.

### 6.4 Sprites, validity, and flips

**Resolution chain:** `SpriteSnapshot.Sprite` (`SpriteAsset?`) → `SpriteAsset.Texture`
(`AssetRef<TextureDescriptor>`) → loaded `TextureDescriptor` → `TextureDescriptor.Payload`
(`DataRef`) → raw bytes → page. A sprite draws only when every part exists and the page is ready; a
`null` sprite draws nothing.

**Placeholder sizing (owner-approved).** A sprite that draws the placeholder because its texture is
absent or failed uses the **descriptor's declared `Width`/`Height`** when the descriptor loaded and
those dimensions are valid, even if the payload was missing, malformed, or unsupported; a format or
payload failure therefore keeps the intended quad size. When no descriptor loaded, or its dimensions
are zero/over `maxImageDimension2D`, the placeholder's own **8×8** extent is used. There is no other
fallback. The internal page lookup carries the resolved width/height, so the WP-4 draw path applies no
second rule.

**Size and geometry** ([Assets §9](../architecture/assets-and-tooling.md#9-sprite-assets)):

```text
pixelWidth  = (maxU - minU) * textureWidth
pixelHeight = (maxV - minV) * textureHeight
worldWidth  = pixelWidth  / SpriteAsset.Scale
worldHeight = pixelHeight / SpriteAsset.Scale
```

`Transform.LocalScale` multiplies through the world matrix. `Pivot` is top-left normalized
(`(0,0)` top-left, `(1,1)` bottom-right, default `(0.5,0.5)`). UVs are top-left origin, ordered
TL/TR/BR/BL. The raw texture is top-left row-major; there is no vertical flip on upload. The local
corner formula is:

```text
corners  = { (0,0), (1,0), (1,1), (0,1) }      // TL, TR, BR, BL
c        = corners[gl_VertexIndex]
sx       = FlipX ? -1 : 1
sy       = FlipY ? -1 : 1
local    = ((c.x - pivot.x) * size.x * sx, (pivot.y - c.y) * size.y * sy)
position = viewProj * vec4(transform * local, 0, 1)
```

**Flip semantics (approved):** `FlipX`/`FlipY` mirror the **geometry** around the pivot in local
space; each corner keeps its UV. They do not modify the transform. `FlipX && FlipY` mirrors both
axes. Negative `LocalScale` composes with the flip naturally; the two are independent.
`Indices`/topology do not change the flip.

**Validity and failure policy:**

| Case | Behavior |
| --- | --- |
| `Sprite == null` | Skip silently. |
| `Layer == 0` or multi-bit | Skip with the invalid-layer diagnostic (existing intent). |
| Non-`Quad` topology | Skip with a once-per-asset diagnostic; never approximate as a quad. |
| Texture absent, missing, not ready, or failed | Pending: skip until ready. Failed: draw the placeholder page (§6.5). |
| `Uvs == null`, length != 4, or any non-finite UV | **Fatal renderer error** (`Bork2dException`), naming the field and the texture reference. |
| UV outside `[0,1]` | Allowed; the descriptor's wrap mode applies. |
| `Scale <= 0` or non-finite | **Fatal renderer error.** |
| `Pivot` non-finite or outside `[0,1]` | **Fatal renderer error.** |
| Unknown `SpriteTopology` value, or non-null `Indices` on a `Quad` | **Fatal renderer error.** |

There is no bake/load-time field validation in Stage 1; the renderer's checks are defensive draw-time
rules and do not change the `LoadAsset<SpriteAsset>` contract. A fatal error on the render thread is
reported through the existing renderer-fatal path (host runs `Teardown` once, joins, and rethrows a
`Bork2dException`; D-M6-14/M7).

**As built (WP-4).** The plan pre-pass validates the fields through `SpriteQuad.Resolve` **before** it
applies the pending-page skip, so a malformed sprite is fatal even while its texture page is still
loading (the pending lookup passes 0×0 only to discard the resolved geometry).

### 6.5 Texture pages, uploads, and bindless

**Asset seam (approved).** `RendererStartInfo` gains a required `IAssets Assets` member. The runtime
passes the app's `AssetService` (which implements `IAssets`) after `assetService.Start()` and before
the renderer is created. The render thread calls `LoadAsset<TextureDescriptor>(path)` and
`LoadData(descriptor.Payload)` asynchronously through the existing loader thread. There is no new
project dependency: `Bork2d.Render` already references `Bork2d.Engine`.

**Page identity and lifetime.** One page per canonical `TextureDescriptor` path. The first
successful load wins for the run; later descriptor instances for the same path reuse the page, and no
eviction/replacement occurs. A missing `Texture` reference, a failed descriptor/payload load, a
payload length mismatch, an unsupported format, zero/over-limit dimensions, or dimensions over
`maxImageDimension2D` marks that path **failed for the run** and maps it to the placeholder. Failed
paths are not retried, even if a later overlay would provide them.

**Placeholder (approved).** A single generated **8×8 magenta/black checkerboard** page, index `0`,
nearest filter, clamp wrap, one mip, generated from CPU bytes at renderer startup. It is always
resident. Any failed/absent texture maps to it; the sprite draws with the placeholder and its tint,
using the owner-approved sizing rule in §6.4. Placeholder creation failure is a renderer startup
failure. The placeholder occupies index `0` and is included in
`RenderStats.TexturePages`/`TexturePageCapacity`; it is not a content page.

**Capacity and the effective limit.** Page indices are `0` = placeholder; `1..N` = real pages. Initial
total capacity is 256 entries (255 real pages). When a new real page is needed and the table is
full, the renderer grows by doubling the capacity, up to the **effective limit**: the smaller of
`maxPerStageDescriptorUpdateAfterBindSampledImages` and
`maxDescriptorSetUpdateAfterBindSampledImages` (and a valid pool/set allocation). A driver that
reports either limit as `UINT32_MAX` ("unbounded") is saturated to the documented implementation cap
of **1,048,576** entries so the capacity and the layout maximum stay representable and match the
largest per-stage update-after-bind sampled-image count real devices report; an allocation failure
below that cap is a fatal `Bork2dException`. The layout's binding-1 descriptor count is that same
effective limit, so growth never recreates the pipeline layout. Growth happens on the render thread
between frames with all in-flight frames idle (`vkDeviceWaitIdle`); the per-frame descriptor
sets/pools are recreated with the new variable descriptor count and every resident page descriptor
is rewritten. If the limit is reached, the renderer fails with a `Bork2dException` naming the limit
and the requested capacity. No VMA; no arbitrary hard-coded page cap.

**Memory.** Page images and staging buffers use a **pool/suballocator**: a small number of
device-local heap allocations for images (aligned, non-overlapping ranges; 4 MiB blocks by default in
WP-3) and a host-visible staging pool leased per upload. No per-page `vkAllocateMemory`; no VMA.
Pool growth allocates a new block; all blocks are destroyed after `vkDeviceWaitIdle` at shutdown.
Exact block size/alignment is an implementation detail constrained by
`vkGetImageMemoryRequirements`/`vkGetBufferMemoryRequirements`.

**Upload path (approved: separate submit + fence).**

1. On first sighting, start `LoadAsset<TextureDescriptor>` and then `LoadData(Payload)`; mark the page
   `Pending`. Pending sprites are skipped this frame.
2. When both complete, validate dimensions/format/payload length and create the image view/sampler.
3. Record a one-time command buffer with the staging copy and the `TRANSFER_DST → SHADER_READ_ONLY`
   barrier; submit on the graphics queue with a fence. The page stays `Uploading`; sprites skip it.
4. At a later frame boundary, when the upload fence has signaled, mark the page `Ready`, write its
   descriptor into **both** per-frame bindless sets (index known), and the page becomes drawable from
   that frame onward. No render-thread block waits for an upload.
5. Content failures at any step mark the page `Failed` and map it to the placeholder. Vulkan
   allocation/copy/device failures remain fatal.

**Images/samplers.** `TextureFormat.Rgba8` → `VK_FORMAT_R8G8B8A8_SRGB`; `SAMPLED | TRANSFER_DST`,
optimal tiling, one mip, one layer, `SHADER_READ_ONLY_OPTIMAL` after upload. Sampler state comes from
the descriptor (`TextureFilter.Nearest`/`Linear`, `TextureWrap.Clamp`/`Repeat`), `maxLod = 0`, no
anisotropy/compare, no mipmaps. Samplers are cached by `(filter, wrap)`; each page descriptor uses
the cached sampler. Descriptor writes use `COMBINED_IMAGE_SAMPLER` with
`PARTIALLY_BOUND | UPDATE_AFTER_BIND` and a variable descriptor count.

**Bindless set.** The per-frame descriptor set has binding 0 (instance storage buffer, vertex stage)
and binding 1 (runtime array of combined image samplers, fragment stage). Binding 1 uses a fixed
layout maximum (the device limit or a documented cap) with `VARIABLE_DESCRIPTOR_COUNT`, so capacity
growth allocates a new set with a larger count without recreating the pipeline layout. Unwritten
indices are unbound; the shader only accesses indices for drawn instances.
`VK_DESCRIPTOR_POOL_CREATE_UPDATE_AFTER_BIND_BIT` and the matching binding flags are required.

**Device limits.** Before use, validate `maxPerStageDescriptorUpdateAfterBindSampledImages`,
`maxDescriptorSetUpdateAfterBindSampledImages`, `maxImageDimension2D`, and
`maxStorageBufferRange` (instance buffer). A limit that cannot support the initial capacity is a
startup failure; growth stops at the device limit and fails explicitly.

**Content failures vs Vulkan failures.** Missing/malformed/unsupported content uses the placeholder;
Vulkan resource/queue/device failures are fatal `Bork2dException`s (the renderer-fatal path). A
missing payload entry is a content failure; an OOM allocating the upload buffer is fatal.

**As built (WP-3, internal `Bork2d.Render.Textures`).** `TexturePageStore` owns the logical state
machine and capacity policy; `IAssets` is called on the render thread but the loads run on the asset
loader thread and are polled without waiting. Both renderers create a `VulkanTexturePages` (images,
`TextureImagePool` device-local blocks, `StagingBufferPool` leases, per-page upload command
buffers/fences) plus a `TexturePageStore` after the frame resources exist, and upload/wait the
placeholder before readiness. `EnsurePage(string?)` is the draw-path entry point (called once per
candidate sprite by the WP-4 planner pre-pass) and returns
`Placeholder` (index 0 plus the resolved quad width/height), `Pending`, or `Ready` (index and the
descriptor's dimensions); the steady-state lookup is a dictionary read returning a value struct. A
page becomes ready in the pump after its upload fence signals; the store then writes its descriptor
into both per-frame sets through the renderer's `IBindlessPageWriter`. Pending loads are observed
and ignored at shutdown (including a load that completed with a fault between frames and was never
pumped). Teardown waits every submitted upload fence without a timeout; if a wait fails (for example
after device loss) it logs the failure and does not free the in-flight page resources rather than
freeing memory the GPU may still read. Default (256) and grown capacities are validated against the
effective limit at startup and on growth.

### 6.6 Instances and the one-draw contract

**Instance record (fixed 96-byte std430 ABI):**

```glsl
struct Instance {          // 6 x vec4 = 96 bytes
    vec4 transform0;       // M11, M12, M21, M22   (2x2 linear part)
    vec4 transform1;       // M31, M32, sizeX, sizeY
    vec4 uv0;              // uTL, vTL, uTR, vTR
    vec4 uv1;              // uBR, vBR, uBL, vBL
    vec4 tint;             // linear RGBA
    vec4 misc;             // pivotX, pivotY, textureIndex, flags
};
```

`flags` is reserved for future use and is zero in M8. The four UVs preserve arbitrary corner UVs,
not just a min/max rectangle. The CPU writes the same layout; a unit test asserts the stride/offsets.
`FlipX`/`FlipY` are encoded as the sign of `sizeX`/`sizeY`, which is algebraically identical to the
approved `size * sx`/`size * sy` formula in §6.4 and keeps `flags` zero; the writer also asserts that
zero.

WP-2 adds the internal `SpriteInstance` record, the mapped-memory writer, the pure
`(Layer, ZIndex)` index sort, the per-camera mask filter with null/invalid-layer/non-Quad/pending
skips, the fatal malformed-field checks, and the quad size/pivot/flip resolver, all with deterministic
tests. WP-3 allocates the initial 1024-record (96-byte) instance buffer per frame and writes binding 0.
**As built (WP-4):** `SpriteFramePlanner` resolves the snapshot once (page lookups, validation, quads,
sorted camera order), assigns contiguous per-camera ranges, calls `IInstanceBuffer.EnsureCapacity` at
most once per frame, and writes every record directly into the mapped buffer; both renderers pass
their `FrameInstanceResources` as the sink.

**Per-frame buffer (approved: grow at frame fences, host-visible coherent).** Each GPU frame owns one
host-visible coherent instance buffer, mapped once at creation. WP-3 allocated the approved initial
capacity of 1024 instances (96-byte records) and wrote binding 0; **as built in WP-4** the buffer is
growable through `InstanceBufferGrowth`: when a frame's total instance count exceeds its buffer
capacity (the frame's fence is already signaled before reuse), the buffer is destroyed and recreated
at `max(required, capacity * 2)`, rounded to a power of two, remapped, and binding 0 of that frame's
descriptor set is updated. No growth occurs while the frame is in flight; the policy refuses it.
Growth is logged once per capacity change and counted. Allocation failure or exceeding
`maxStorageBufferRange` is fatal (`FrameInstanceResources` validates the initial 1024-record size at
startup and throws on growth). There is no CPU frame array and no staging copy.

**Ranges and draws.** Instances are built once per snapshot in `(Layer, ZIndex)` order, filtered per
camera, and appended into contiguous per-camera ranges in the frame buffer. Overlapping cameras get
their own ranges. Each non-empty camera issues
`vkCmdDraw(4, instanceCount, 0, firstInstance)`; `gl_InstanceIndex` addresses the same record. The
instance buffer is bound once per pass; the push constant carries the camera view-projection. The
draw counter equals the number of non-empty cameras.

### 6.7 Pipeline and shaders

- **Sources:** `src/Bork2d/Bork2d.Render/Shaders/sprite.vert` and `sprite.frag` (GLSL 450/460),
  checked in.
- **Build-time compiler (approved, as built in WP-2):** a build-only JIT tool
  `build/Bork2d.ShaderCompiler` references `Silk.NET.Shaderc` **2.23.0** and
  `Silk.NET.Shaderc.Native` **2.23.0**. An MSBuild target
  (`build/Bork2d.ShaderCompiler.targets`, imported by `Bork2d.Render`) restores and builds the tool out
  of process, compiles the GLSL to SPIR-V (Vulkan 1.3 target; debug info in Debug, optimization in
  Release), and emits `obj/<Configuration>/<TFM>/ShaderCompiler/SpriteShaders.g.cs` with the SPIR-V
  byte arrays into `Bork2d.Render`'s compilation before `CoreCompile`. The generated file is neither
  committed nor published as a loose file. The target also runs for design-time/format builds so the
  clean-clone CI sequence (solution restore → `dotnet format --no-restore` → build) sees the type;
  the out-of-process restore/build uses `RemoveProperties` so RID/publish settings do not leak into the
  tool. No Vulkan SDK is required. The tool and packages are never in the runtime/AOT publish graph;
  no runtime shader I/O or compilation.
- **Pipeline (as built in WP-2):** one graphics pipeline per output color format (windowed/offscreen
  formats may differ; both sRGB). Dynamic rendering with one sRGB color attachment, no depth/stencil,
  no vertex input, triangle strip, four vertices per instance, dynamic viewport/scissor, culling and
  depth test/write disabled, straight alpha `src*a + dst*(1-a)`, color write mask RGBA, sample count 1.
  `VkPipelineRenderingCreateInfo` carries the color format only. One pipeline layout: push constant
  64 bytes + descriptor set 0. No per-camera descriptor sets. The `SpritePipeline` owns the
  descriptor-set layout (binding 0 storage buffer/vertex, binding 1 variable-count combined image
  sampler array/fragment with `PARTIALLY_BOUND | UPDATE_AFTER_BIND | VARIABLE_DESCRIPTOR_COUNT`), the
  pipeline layout, and the `(filter, wrap)` sampler cache (`maxLod = 0`, no anisotropy/mipmaps). Both
  renderer startups create it after the color format is known and destroy it in cleanup; creation
  failure is a clean renderer startup failure (D-M6-14). Static debug labels only. No draw or bind is
  recorded in WP-2.
- **Shaders:** `sprite.vert` fetches `Instance` by `gl_InstanceIndex`, generates the corners with the
  pivot/flip formula, applies the world transform and view-projection. `sprite.frag` samples
  `textures[textureIndex]` and multiplies the linear tint. The shader assumes the approved 96-byte
  layout and the `R8G8B8A8_SRGB` page format. The checked-in sources are GLSL `#version 460`; the
  generated modules are SPIR-V 1.3+ and a deterministic test asserts the SPIR-V magic/version and that
  the build integration linked them.
- **Failure:** shader/tool/pipeline/layout creation failure is fatal before the first tick, reported
  through the renderer-error path (Setup still runs, zero ticks, Teardown attempted, renderer joined,
  `Bork2dException` rethrown; D-M6-14).
- **Labels:** static debug labels for the pipeline, layout, page images/views/samplers, instance
  buffers, and descriptor sets. No per-draw or per-camera label churn.

### 6.8 Sorting, filtering, and clears

**Sort/filter (kept):** sort once per snapshot by `Layer` ascending, then `ZIndex` ascending; equal
keys are unspecified (tests use distinct keys). Filter per camera with `(CullingMask & Layer) != 0`;
a mask of `0` sees nothing; invalid layers are skipped. The renderer never reads scene entities.

**One pass per camera (approved).** For each camera in ascending `ZIndex`:

1. Compute the rounded pixel rect; skip the camera if invalid (§6.3).
2. Determine `loadOp`: `CLEAR` for `Clear.Color` (clear value = `ClearColor`, linear RGBA);
   `LOAD` for `Clear.None` and `Clear.Depth`. `Clear.Depth` logs once and never reads/writes depth.
3. Record `vkCmdBeginRendering` with `RenderArea = rect`, one sRGB color attachment, `storeOp =
   STORE`, no depth/stencil; set viewport/scissor to the rect.
4. If the camera has at least one instance, bind the pipeline/descriptor set, push the view-
   projection, and issue one `vkCmdDraw(4, instanceCount, 0, firstInstance)`. If it has no instances,
   record no draw but keep the pass for `Clear.Color` (a clear-only pass); for `Clear.None`/`Depth`
   an empty camera skips the pass entirely.
5. `vkCmdEndRendering`.

**No safety pass.** There is no frame-level clear. Consequences (approved):

- A frame with zero cameras records no pass; the acquired swapchain image/offscreen target is not
  cleared. Its content is whatever the previous use left (offscreen) or undefined (a newly acquired
  swapchain image).
- If the first camera uses `Clear.None`, the target is not cleared; it preserves prior content.
- A camera's `Clear.Color` clears only its own rect. Areas outside every camera rect are not cleared.
- A full-screen `Clear.Color` camera (for example Pong's) effectively clears the whole target.
- The offscreen image stays in `COLOR_ATTACHMENT_OPTIMAL`; the windowed image still transitions
  `UNDEFINED → COLOR_ATTACHMENT_OPTIMAL` before the first pass and `→ PRESENT_SRC_KHR` after the last.

**Draw count:** one draw per non-empty camera (at least one instance). Empty `Clear.Color` cameras
produce a clear-only pass, not a draw. Empty `Clear.None`/`Depth` cameras produce nothing. The
`RenderStats.DrawCalls` counter counts `vkCmdDraw` calls; `NonEmptyCameras` counts cameras with a
draw.

**Split-screen:** two cameras with viewports `(0,0,0.5,1)` and `(0.5,0,0.5,1)`, each with its own
pass/rect; a later `Clear.Color` wipes only its half; `Clear.None` overlays. Ties in camera `ZIndex`
are unspecified; use distinct keys.

**As built (WP-4).** Both renderers call the shared `SpritePassRecorder`, which records the pass and
the optional draw exactly as above (viewport/scissor = the camera's rounded rect, `RenderArea` = the
same rect, one attachment, `STORE`, no depth/stencil). A color-attachment pipeline barrier runs
before each pass so a later `LOAD` observes earlier output; it is a dependency, not an extra pass.
A `Clear.Depth` camera logs once per renderer. The GPU tests cover the split-screen, zero-camera,
clear-only, and empty-`Clear.None` paths with zero validation errors.

### 6.9 Public API

The owner-approved additions are the only public changes in M8. All live in `Bork2d.Engine` /
`Bork2d.Engine.Rendering`; `Bork2d.Render` stays internal.

#### `ICamera<TLayer>` — screen/world helpers and visible world size

```csharp
Vector2 WorldToScreen(Vector2 world);
Vector2 ScreenToWorld(Vector2 screen);
Vector2 WorldSize { get; }
```

- `WorldToScreen` returns **framebuffer pixels**, top-left origin, in global framebuffer coordinates
  (the camera viewport offset is included). It uses the same camera world inverse, `Size`, rounded
  viewport/aspect, and Y-flip as the renderer.
- `ScreenToWorld` is the inverse mapping; a point outside the camera viewport extrapolates (it is not
  clamped to the viewport).
- Both use the rounded pixel rect from §6.3; the projection uses the rounded aspect.
- **Owner-approved M8 addition:** `WorldSize` returns the visible world `(2 * Size * a, 2 * Size)`
  where `a` is the rounded pixel-viewport aspect from §6.3. It is read-only, has no setter, and
  follows the same invalid-camera/no-framebuffer rules as the two helpers.
- **Framebuffer source (owner-approved M8 addition):** the helpers read the engine window's
  `IWindow.Size`, the framebuffer size in physical pixels, because `GameEngine.CreateCamera` passes
  the engine's `IWindow` to `Camera<TLayer>`. In headless/offscreen apps the window stub throws
  `NotSupportedException`, surfaced as `InvalidOperationException` with a clear message; a disposed
  windowed app may propagate `ObjectDisposedException`.
- Non-finite input throws `ArgumentOutOfRangeException`.
- An invalid camera (same criteria as §6.3, including a non-unit camera scale) throws
  `InvalidOperationException`; no silent NaN result.
- The camera's scale is not zoom; the helpers do not add a zoom parameter. Rotation is supported
  through the camera world matrix.
- The helper implementations live in `Camera<TLayer>` and delegate to the shared pure
  `Bork2d.Engine.Rendering.CameraProjection` helper (also used by the renderer). The public XML docs,
  API reference, guide, and deterministic tests update in the same change.
- **Status (as built in WP-2):** `WorldToScreen`, `ScreenToWorld`, and `WorldSize` are `Current` and
  tested. `RenderStats` and `CaptureFrameAsync` are `Current` since WP-3/WP-4 (§6.9).

#### `IEngine<TLayer>.RenderStats`

```csharp
RenderStats RenderStats { get; }
```

`RenderStats` is a public `readonly record struct` in `Bork2d.Engine.Rendering`:

```csharp
public readonly record struct RenderStats(
    long FrameSequence,
    int FramebufferWidth,
    int FramebufferHeight,
    int DrawCalls,
    int Instances,
    int Cameras,
    int NonEmptyCameras,
    int TexturePages,
    int TexturePageCapacity,
    long SkippedSprites,
    long ValidationErrors,
    long ValidationWarnings);
```

- Values describe the **last completed frame**, except `ValidationErrors`/`ValidationWarnings`, which
  are cumulative for the run. `TexturePages`/`TexturePageCapacity` are current at that frame and
  include the placeholder (index 0). `SkippedSprites` counts null/invalid-layer/non-Quad/pending
  sprites skipped in that frame (placeholder-drawn sprites are not skipped).
- Before the first frame, in headless mode, and after `Build()`/`Dispose()`, the getter returns the
  defaults/zero (or the last published values after shutdown); it never throws. Headless has no
  renderer, so it stays zero for the run.
- The getter is thread-safe and consistent (a short lock or an equivalent snapshot mechanism); it
  never blocks on the GPU and never allocates. The renderer publishes once per completed frame; the
  host copies it into the engine's bridge.
- No per-frame logging; the values are also logged once at renderer stop.

**As built (WP-3/WP-4).** The public `RenderStats` record struct and `IEngine<TLayer>.RenderStats` are
`Current`. `GameEngine` keeps the last snapshot under a `System.Threading.Lock` and exposes an
internal `SetRenderStatsSource(Func<RenderStats>?)` seam; `Bork2d.Runtime` installs
`() => renderer.RenderStats` right after the renderer is created, and headless never installs one, so
the getter returns `default` there and before a source exists. The getter invokes the source inside a
`try`/`catch`, keeps the last observed value if the source throws, allocates nothing, and never
blocks on the GPU. Both renderers own a thread-safe `RenderStatsBoard` and publish exactly once per
completed frame from `FrameRenderStats.Create(...)` (after submit/present), so a running game observes
the last completed frame.
`RenderStats` is the only WP-3 public addition besides the property; `CaptureFrameAsync` is the only
WP-4 public addition.

#### `IEngine<TLayer>.CaptureFrameAsync`

```csharp
Task<byte[]> CaptureFrameAsync(CancellationToken cancellationToken = default);
```

- Returns the **latest completed frame** as RGBA8 bytes, row-major, top-left origin, exactly
  `FramebufferWidth * FramebufferHeight * 4` bytes for that frame. If no frame has completed, it
  captures the next completed frame.
- Callable from any thread during `Run()`; it does not block the tick or render thread. The copy is
  recorded after the last camera pass and before present/discard; it includes all camera clears and
  draws, and excludes OS/window chrome. Windowed BGRA targets are swizzled to RGBA; offscreen RGBA
  targets are copied directly.
- `cancellationToken` cancels a pending request; the returned task faults with
  `OperationCanceledException`.
- Headless throws `NotSupportedException` (no renderer/frames). Before `Run()` it throws
  `InvalidOperationException`; after `Run()`/`Dispose()` it throws `ObjectDisposedException`. A
  renderer fatal/stop faults pending requests.
- One `byte[]` allocation per call; internal staging memory is pooled. There is no pixel-format
  option, no screenshot file, and no public capture object in M8.
- The engine's own tests do **not** assert rendered pixels; they assert the API contract (length,
  cancellation, headless/shutdown behavior) and structural counters. Golden/pixel tests are out of
  scope.

**As built (WP-4).** `GameEngine<TLayer>` exposes the member and delegates to an internal
`SetCaptureSource(Func<CancellationToken, Task<byte[]>>?)` seam installed by `Bork2d.Runtime` right
after renderer creation; headless installs `SetCaptureUnsupported()` at `Run` start, and
`MarkCaptureDisposed()` runs when `Run` returns/throws or the app is disposed. Both renderers own a
thread-safe `CaptureRequestQueue` and one lazily created, pooled host-visible staging buffer per GPU
frame (`FrameCapture`); the render thread records the image-to-buffer copy after the last camera pass
and before present/discard (BGRA→RGBA swizzle on the host after the fence), and completes the tasks
when that frame's fence is observed at reuse/shutdown. A frame whose copy was recorded but which was
never submitted (a fatal during recording or a failed `vkQueueSubmit2`) faults its attached requests
instead of copying staging bytes the GPU never wrote; the pure `FrameCaptureState`
(`Recorded`/`Submitted`, `CanComplete`) drives that decision. Requests that arrive while a frame is
recorded
wait for the next completed frame; a stop/fatal drains and faults them. The public-surface test pins
the exact signature.

#### Public-surface rules

- Update the API reference, guide, XML docs, public-surface tests, and dependency tests in the same
  changes.
- No other public member/type is added. No public Render type, no builder option for capacities, no
  public diagnostic stream, no public `RenderStats` mutation.
- Public `RenderStats`/`CaptureFrameAsync` are AOT-safe: no reflection, no dynamic code, no new
  runtime package.

### 6.10 Diagnostics, counters, and logging

Internal counters (exposed to tests; the public subset is `RenderStats`):

| Counter | Meaning |
| --- | --- |
| Frames completed / sequence | Frames and the last snapshot sequence. |
| Camera count / non-empty cameras | Captured and drawn cameras. |
| Draw calls / instances | `vkCmdDraw` calls and submitted instances. |
| Sprites captured / skipped | Null, invalid layer, non-Quad, pending (not placeholder). |
| Texture pages / capacity | Resident pages (including placeholder) and current capacity. |
| Page uploads / upload failures / placeholder uses | Texture health. |
| Page growths / instance-buffer growths | Capacity high-water behavior. |
| Instance capacity / overflow | Per-frame instance buffer state. |
| Plan CPU / upload CPU | Attribution for the benchmark. |
| Validation errors/warnings | Existing behavior. |

Logging:

- Static startup line: mode, pipeline/shaders, page initial capacity, instance initial capacity,
  bindless limit, placeholder on.
- Stop line: frames, draws/instances, pages/capacity/growths, skipped, validation counts, stop
  reason.
- Draw/content diagnostics are **throttled**: once per unique asset path/kind (or once per renderer
  for camera/invalid-layer kinds), with totals in counters. No per-frame, per-sprite, or per-draw
  logging. Placeholder use logs once per failed path.
- Validation: errors fail with validation enabled; warnings are logged/counted and fatal in
  `[GpuFact]` (D-M6-21). Release without validation logs only.
- Static debug labels for the new Vulkan objects; no per-draw/per-camera label churn.

**As built (WP-3/WP-4).** Both renderer start lines log `instanceCapacity=1024`, `texturePages`,
`texturePageCapacity`, `bindlessLimit`, and `placeholder=on`. Both stop lines add `texturePages`,
`texturePageCapacity`, `pageGrowths`, `failedPages`, `instanceCapacity`, `instanceGrowths`,
`drawCalls`, `instances`, `cameras`, `nonEmptyCameras`, `skippedSprites`, and the validation counts,
using the last published `RenderStats`. The page store logs one warning per failed path (never per
frame or per sprite) and one information line per capacity growth; `FrameInstanceResources` logs one
information line per instance-buffer capacity change. `SpriteFramePlanner` logs invalid-layer and
`Clear.Depth` diagnostics once per renderer, invalid-camera diagnostics once per renderer per reason,
and non-Quad diagnostics once per asset path. `RenderStats` is not logged per frame; there is no
per-frame/per-sprite/per-draw/per-camera logging.

### 6.11 Allocation and performance

- No steady-state allocations in the plan/build/draw path after warm-up: sorted candidates and
  per-camera ranges use preallocated renderer-owned arrays; instance records are written directly
  into the mapped per-frame buffer; no LINQ/boxing/per-sprite delegates.
- Page loads/descriptor instances allocate while loading (not steady state); page images use the
  pool, staging uses the ring. Page/instance buffer growth is the only steady-state capacity change
  and is logged/counted.
- One sort per snapshot; one draw per non-empty camera; no GPU→CPU readbacks unless
  `CaptureFrameAsync` is requested; no artificial cap or sleep.
- The opt-in benchmark target is **50,000 sprites, one camera, 1920×1080, median ≥165 FPS** on the
  M6 machine (vsync off, offscreen discard, 10-second measurement window after warm-up, p50/p95
  reported, zero steady-state allocations). The benchmark is local-only; CI is unchanged.

**As built (WP-4).** A deterministic test asserts that 200 warm `SpriteFramePlanner.Plan` calls over
a fixed snapshot allocate 0 bytes, and the GPU instance-growth test grows a frame from 1024 to 2048
records for 1500 sprites. `CaptureFrameAsync` allocates one `byte[]` per request on the frame-fence
completion path, never inside the tick or per-frame record path; page/instance growth remains the
only capacity change. The 50,000-sprite benchmark is WP-5.

### 6.12 Startup, shutdown, and error matrix

| Situation | Where detected | Behavior |
| --- | --- | --- |
| `IAssets` missing/null in `RendererStartInfo` | Host/factory | Internal `Bork2dException`/`ArgumentNullException` at renderer creation; no windowed/offscreen run. |
| Shader tool/SPIR-V/pipeline/layout creation fails | Renderer thread before readiness | `Bork2dException`; renderer never ready; Setup still runs, zero ticks, Teardown attempted, joined, rethrown (D-M6-14). |
| Bindless/instance/device limit cannot support the initial capacity | Renderer setup | Startup failure naming the limit; same path. |
| Sprite field malformed (UV/Scale/Pivot/topology/indices) | Render thread | Fatal `Bork2dException`; renderer-fatal path; host tears down once and rethrows. |
| Texture content missing/malformed/unsupported | Render thread | Mark path failed; draw placeholder; log once; app continues. |
| Vulkan upload/allocation/copy failure | Render thread | Fatal `Bork2dException`; renderer-fatal path. |
| Page capacity reaches the device limit | Render thread growth | Fatal `Bork2dException` naming the limit; no silent drop. |
| Instance buffer exceeds `maxStorageBufferRange`/allocation fails | Render thread growth | Fatal `Bork2dException`; no truncation. |
| Invalid camera | Render thread | Skip whole camera including its clear; throttled diagnostic; app continues. |
| Zero cameras / `Clear.None` first camera | Render thread | No safety pass; target not cleared (documented; previous/undefined content). |
| Minimized/zero extent | Render thread | M7 suspended drain; no GPU work, no uploads; wait for restore. |
| Resize/fullscreen/recreate | Render thread | M6 recreation; pages/pipeline survive; per-frame buffers/sets recreated only as required. |
| `CaptureFrameAsync` request during a frame | Render thread | Copy after the last pass, before present/discard; complete the task after the frame; no tick stall. |
| Stop with uploads/frames in flight | Renderer stop/join | Stop requests, drain/release slots, wait fences/device idle, destroy pages/pipeline/sets/buffers, join; pending captures fault/cancel. |
| Validation error/warning | Messenger | D-M6-21: error fails; warning logged/counted and fatal in `[GpuFact]`. |
| Device loss | Present/acquire/submit | Still fatal; recovery is M10. |
| `CaptureFrameAsync` in headless | Engine | `NotSupportedException`; no renderer is created. |

No new public exception type: renderer failures use `Bork2dException`; capture lifecycle uses
`InvalidOperationException`/`ObjectDisposedException`/`NotSupportedException`; argument errors stay
BCL exceptions.

### 6.13 Threading and lifetime

- All Vulkan/page/pipeline state is render-thread-owned. The tick thread supplies immutable snapshots
  and the public API bridge only; it never calls Vulkan.
- `IAssets` calls from the render thread are allowed and asynchronous; the loader thread remains the
  single loader. The renderer must not block the loader indefinitely; pending tasks are ignored on
  stop, and the host stops assets only after the renderer joins.
- `IEngine.RenderStats` is a consistent snapshot; `CaptureFrameAsync` is thread-safe and
  non-blocking. No lock is held across a Vulkan call, fence wait, loader wait, or game callback.
- The renderer joins before the asset service stops and before the platform destroys the
  surface/instance/window. No render thread survives `Join()`.
- No public API gains thread affinity; the existing any-thread rules hold.
- **As built (WP-3/WP-4):** the render thread starts descriptor/payload loads through `IAssets` and
  polls the returned tasks in the per-frame pump; no render-thread wait exists for a page load. The
  only upload wait is the startup placeholder fence. The `RenderStats` bridge is a short-lock value
  copy with no Vulkan call; `vkDeviceWaitIdle` is used only for capacity growth between frames and for
  recreation/shutdown, never per frame. `CaptureFrameAsync` requests are queued under a short lock,
  recorded on the render thread, completed from the frame fence, and never block the tick or render
  thread; cancellation/fatal/stop drains them.

## 7. Invariants

1. Each non-empty camera produces **exactly one** `vkCmdDraw`; no camera produces more than one. An
   empty `Clear.Color` camera produces a clear-only pass; an empty `Clear.None`/`Depth` camera
   produces nothing.
2. `RenderStats.DrawCalls` equals the number of non-empty cameras and is ≤ `RenderStats.Cameras`.
3. Sprites draw in `(Layer, ZIndex)` ascending order per camera; cameras apply in ascending
   `ZIndex`; ties are unspecified.
4. A sprite draws only when it is a `Quad`, has a valid layer, has a ready texture page or the
   placeholder, and has valid fields; malformed fields are fatal, not silently repaired.
5. Quad size/pivot/UV/orientation follow the fixed formulas; flips mirror geometry around the pivot
   and never modify the transform.
6. Each camera has its own dynamic-rendering pass with `CLEAR` for `Clear.Color` and `LOAD` for
   `Clear.None`/`Depth`; there is no frame-level safety pass. The frame's cleared regions are exactly
   the `Clear.Color` camera rects.
7. Windowed and offscreen produce the same camera/sprite/instance/draw/clear plan for the same
   snapshot; only the target format/presentation differs.
8. One page per descriptor path; failed paths map to the placeholder; page capacity grows by
   doubling up to the device limit and never silently drops a page.
9. Texture loads never block the render thread; pending pages are skipped; uploads complete through
   a fence and become drawable at a later frame.
10. No steady-state allocation in plan/build/draw; instance buffers grow only at a free frame fence
    and page capacity only between frames.
11. The renderer never reads live scene state; it consumes the snapshot and `IAssets` only; the M7
    slot/fence lifetime is unchanged.
12. `CaptureFrameAsync` returns RGBA8 of a completed frame, never blocks the tick, and never
    affects rendering when unused; `RenderStats` describes the last completed frame consistently.
13. `WorldToScreen`/`ScreenToWorld`/`WorldSize` use the same projection/viewport as the renderer and
    throw for invalid cameras.
14. Validation reports zero errors in M8 GPU runs; warnings follow D-M6-21.
15. Shutdown with pages/uploads/frames in flight is clean: no leaked Vulkan object, no
    use-after-free, no surviving render thread.
16. `Bork2d.Render` exposes no public type; the only public additions are §6.9.
17. Pong draws its three sprites on windowed/offscreen runs and reports non-zero draw/instance/page
    counters.

## 8. Test strategy

### 8.1 Deterministic tests (no GPU)

- **Camera math (WP-2, as built):** view/projection for translation/rotation, `Size`, rounded
  viewport/aspect, split-screen, Y-up→Vulkan-Y-down, half-up rounding/clamping/seams, zero/invalid
  viewport, singular matrix, non-unit scale, invalid camera reasons, `WorldSize`, and the shared
  renderer bridge.
- **Screen/world helpers (WP-2, as built):** round-trip within tolerance, viewport offsets,
  split-screen cameras, points outside the viewport, invalid camera throws, non-finite input throws,
  headless no-framebuffer throws, disposed-window propagation, cross-thread consistency.
- **Sort/filter (WP-2, as built):** layer numeric order, `ZIndex`, equal-key ties (not asserted as
  stable), mask `0`, multi-bit/zero layers, null sprites, non-Quad, pending classification.
- **Sprite validity (WP-2, as built):** fatal fields (null/short/non-finite UVs, bad Scale/Pivot,
  unknown topology, indices on Quad), allowed UVs outside `[0,1]`, null sprite/layer/non-Quad skip,
  flips/pivot math, signed-size flip encoding, negative `LocalScale` composition.
- **Texture page state machine (WP-3, as built):** fake `IAssets` and a fake GPU seam; descriptor/
  payload readiness, page-key dedupe/one load per path, pending and uploading skips, every content
  failure mapping to the placeholder with the owner-approved declared-dimension sizing, no retry
  after failure, the 8×8 magenta/black placeholder pattern, the 256→doubling→device-limit capacity
  policy with the explicit failure, `TexturePages`/`TexturePageCapacity`/failed-path accounting,
  allocation-free steady lookups, and shutdown with a pending load.
- **Public API (WP-3/WP-4, as built):** the record struct's exact fields/order/types,
  `IEngine<TLayer>`'s pinned surface, defaults before the source/headless, the host-installed source,
  consistent concurrent reads, never throwing, and an allocation-free read path. WP-4 adds the
  `CaptureFrameAsync` lifecycle tests (pre-Run, headless, after-run/dispose, cancellation, any-thread,
  the renderer source forwarding) and the exact-signature public-surface test.
- **Frame plan/passes (WP-4, as built):** per-camera pass order, `Clear.Color`/`None`/`Depth`, empty
  clear-only pass, zero-camera no-pass, one draw per non-empty camera, ranges/`firstInstance`,
  instance total, overlapping cameras, masks/layers/null/non-Quad/pending/placeholder, flips/pivot/UV/
  size math, placeholder sizing, invalid-camera skips, throttled diagnostics, and fatal fields.
- **Instance buffer (WP-2 layout + WP-4 growth, as built):** layout offsets/stride (96 bytes),
  mapped-memory writer, builder field order, zero `flags`; the growth policy's power-of-two rule, the
  explicit `maxStorageBufferRange` failure, and the free-fence invariant.
- **Public API:** `RenderStats` defaults/headless/after-shutdown/consistency and
  `CaptureFrameAsync` length/cancellation/headless/shutdown behavior (as built; no pixel assertions),
  plus the full public-surface test.
- **Allocation:** warm plan/build with fixed counts allocates 0 bytes
  (`GC.GetAllocatedBytesForCurrentThread`), and `RenderStats` reads allocate nothing.
- **No regression:** dependency matrix/public-surface tests confirm only §6.9 public additions; docs
  link check passes.

### 8.2 Opt-in real-GPU tests (`BORK2D_RUN_GPU_TESTS=1`)

`[GpuFact]`, serialized `RealGpu` collection, skipped by default, bounded joins/wait, no sleeps, all
Vulkan objects destroyed:

- Offscreen scene with generated textures: counters show one draw per non-empty camera, expected
  instances, pages/resident capacity; validation zero.
- Split-screen two-camera scene: per-camera passes, clear/overlap/order, per-viewport draws.
- `Clear.Color`/`Clear.None`/`Clear.Depth` behavior; zero-camera/no-clear behavior (no crash; stats);
  empty `Clear.Color` clear-only pass.
- Placeholder: a missing/malformed texture draws the checkerboard; the app continues; stats show the
  failure.
- Fatal malformed sprite: the run fails with `Bork2dException`, teardown once, renderer joined.
- Page growth: many generated textures cross the initial capacity; growth succeeds; no validation
  errors; device-limit failure is tested with a fake/small limit if practical.
- Windowed small scene: present per snapshot, resize, fullscreen, minimize/restore, stop with frames
  in flight; validation zero.
- `CaptureFrameAsync`: returns a byte array of the expected length after a frame; cancellation;
  headless `NotSupportedException`; after-shutdown behavior. No pixel assertions.
- Pong windowed/offscreen smoke with the baked pack; counters/validation; no pixel assertion.
- AOT-published `Pong.Desktop` runs per §10.

### 8.3 Visual/golden tests

**None.** M8 has no test readback and no pixel/golden assertions. The public `CaptureFrameAsync` API
is tested for contract/lifecycle only. Visual acceptance is a manual windowed Pong check (paddles,
ball, tints, clear) recorded in the PR. This supersedes the roadmap "golden tests" bullet.

### 8.4 Benchmark

An opt-in `[GpuFact]` benchmark in `Bork2d.Tests`, local-only:

- Offscreen 1920×1080, one camera, 50,000 sprites (generated textures), 10-second measurement window
  after warm-up, no artificial cap/sleep, vsync off (no present).
- Report p50/p95/max frame time, FPS, render-thread CPU, allocations, machine/GPU/driver/SDK.
- Pass criterion: **median ≥165 FPS** (p50 frame time ≤ ~6.06 ms); report p95 as the stability
  metric. Zero steady-state allocations in the measured path.
- The benchmark is not CI; the PR records the machine and evidence. If the target cannot be met,
  stop and report; do not lower the target without the owner.

### 8.5 CI

Unchanged: PRs to `develop`/pushes to `develop` restore, check docs, verify format, build Debug, and
run Debug tests. No GPU or AOT CI job. Deterministic tests and headless builds must run without a
GPU. GPU tests/benchmark remain local.

### 8.6 Existing-suite impact

- `FakeRenderer`/host tests: add stats/capture bridge tests; no loop/handoff change.
- Pong tests: existing 46 stay; add a `PongGame` render-stats capture assertion if feasible without
  referencing `Bork2d.Render` (Pong.Tests does not reference Render today).
- GPU tests: the five M6/M7 tests remain; M8 adds rendering/paging/readback/benchmark tests.
- AOT: the publish now includes generated SPIR-V C# (linked into the exe); no shaderc assemblies or
  loose shader files.

## 9. Runtime and host integration

- **Asset seam:** `RendererStartInfo.Assets` is the app's `IAssets` (`AssetService`), supplied after
  `assetService.Start()` and before renderer creation (as built in WP-3); both renderers and the
  factory reject a missing seam with a `Bork2dException`. The renderer uses it for
  descriptor/payload loads; no Runtime reference. The renderer joins before `assetService.Stop()`.
- **Stats/capture bridge (WP-3/WP-4, as built):** Runtime installs an internal stats source in
  `GameEngine` right after the renderer is created (`() => renderer.RenderStats`); headless installs
  none and stays `default`. Runtime installs `renderer.CaptureFrameAsync` as the engine's capture
  source before `Setup`, installs the headless `NotSupportedException` source at `Run` start, and
  calls `MarkCaptureDisposed()` when `Run` returns/throws or the app is disposed. After shutdown the
  engine retains the last stats.
- **Loops:** windowed/offscreen keep acquire → freeze/time/text → tick → capture → publish; M8 adds
  no pacing/sleep/thread. Minimized/zero extent still drains without GPU work and performs no
  uploads. Headless remains renderer-free.
- **Builder:** no new public builder option. The new public members are on `IEngine`/`ICamera`.
- **Errors:** shader/pipeline/limit startup failures follow D-M6-14; renderer fatal/stop wakes a
  blocked producer (M7 D-M7-09); malformed sprite fields are renderer-fatal; invalid cameras skip.
- **Offscreen:** fixed extent, two targets, discard by default; capture only when requested. No
  resize/present.

## 10. Pong example and AOT gate

Pong already creates one camera and three sprites (`sprites/paddle` twice, `sprites/ball`), loads
descriptors/raw bytes in `Setup`, and uses a full-screen `Clear.Color` camera. After M8 it draws the
two paddles and the ball with their tints.

- **Game code:** no new engine API is required for drawing. `PongGame` captures
  `engine.RenderStats` in `Teardown` into a public `RenderStats` property and exposes
  `RefreshRenderStats()` so the desktop host can re-read the completed run's counters after `Run`;
  `Pong.Desktop` prints and validates the §10 relationships for `--offscreen --ticks N` and
  `--windowed --ticks N`.
- **Windowed/offscreen gate:** exit 0, one `Setup`/`Teardown`, exactly N ticks, validation zero
  (when enabled), and `RenderStats` relationships: `Cameras >= 1`, `NonEmptyCameras >= 1`,
  `DrawCalls >= 1`, `Instances >= 3`, `TexturePages >= 3` (placeholder + paddle + ball). No frame
  count/duration assertion; no pixel assertion. The manual visual check is recorded in the PR.
- **As-built timing note (WP-4).** The M7 windowed/offscreen tick loop is unpaced: the automated
  `--ticks 120` run can complete all 120 ticks in ~20 ms after renderer readiness, before the renderer
  has consumed the 3 frames the asynchronous page upload needs (`EnsurePage` → upload submit →
  fence-ready). Such a run still exits 0; `Pong.Desktop` prints a diagnostic note and skips the
  draw/instance relationship assertions when the run ended before the two pages were drawable (it
  reports `texturePages=1`, `drawCalls=0`, `instances=0`, `skippedSprites=3`). A longer run (for
  example `--windowed --ticks 20000`) draws the three sprites with `drawCalls=1`, `instances=3`,
  `texturePages=3`, validation zero. The final presentation gate remains WP-5; changing the
  windowed/offscreen tick loop or the acceptance-run semantics to make the 120-tick run draw is
  escalated, not silently worked around.
- **Headless gate:** unchanged; no renderer, no stats/capture (defaults/`NotSupportedException`);
  120 ticks by default or `--headless --ticks N`.
- **AOT publish:** zero AOT/trim warnings; single-file exe; `assets.pck`, `SDL2.dll`; no
  `vulkan-1.dll`, no tooling assemblies, no `Bork2d.ShaderCompiler`/Silk.NET.Shaderc assemblies, no
  loose `.spv`; generated SPIR-V is linked in.
- **Published runs:** `--headless`, `--headless --ticks N`, `--windowed --ticks 120`, and
  `--offscreen --ticks 120` exit 0. Since WP-3 each windowed/offscreen publish creates and uploads the
  placeholder page at startup; WP-4 draws the Pong sprites once the two texture pages are ready
  (observed on longer runs; see the timing note above). The start/stop logs report the page, instance,
  draw, camera, skipped, and validation counters.

## 11. Documentation changes

Same-change rule: each WP updates its owning docs. **No `decisions.md` entry is added** (owner
instruction); this spec is the M8 record.

| Document | Change |
| --- | --- |
| [Rendering](../architecture/rendering.md) | Per-camera passes/no safety pass; `IAssets` seam, pages/pool/dynamic growth/placeholder; pipeline/shader tool; instance layout/growth; public stats/capture; screen/world helpers; counters/status; remove the one-pass/safety-black/indexed-M8 statements. |
| [Assets and tooling](../architecture/assets-and-tooling.md) | Renderer reads descriptor/payload via `IAssets`; page residency/placeholder; remove the M5 validation claim; state the draw-time fatal/skip rules. |
| [Threading](../architecture/threading.md) | Render-thread `IAssets`/loader coordination; stats/capture bridge; no snapshot change; shutdown ordering. |
| [Runtime lifecycle](../architecture/runtime-lifecycle.md) | `RendererStartInfo.Assets`, stats/capture bridge, shutdown, error precedence. |
| [NativeAOT](../architecture/nativeaot.md) | Silk.NET.Shaderc build-time tool, generated SPIR-V, publish content, no runtime shader I/O. |
| [Projects](../architecture/projects.md) | Build-only `Bork2d.ShaderCompiler` project/tool, no new runtime dependency; dependency matrix unchanged. |
| [Logging](../architecture/logging.md) | M8 counters/events, throttled diagnostics, no per-frame logging; stats/capture. |
| [API reference](../api/reference.md) | `WorldToScreen`/`ScreenToWorld`/`WorldSize`, `RenderStats`, `CaptureFrameAsync`; camera/sprite statuses/validity; readback/stats errors; windowed/offscreen behavior. |
| [API guide](../api/guide.md) | Camera/sprite draw semantics, flips, placeholder/fatal content, screen/world helpers, stats/readback examples. |
| [Contributing](../contributing.md) | Shader compiler/package, no Vulkan SDK requirement, GPU/benchmark commands, M8 gates. |
| [Roadmap](../roadmap.md) | M8 status/evidence; remove the golden-test/GPU-runner/one-pass assumptions where they conflict; progress log. |
| [docs/README](../README.md) | Add the M8 spec to the spec index; status. |
| M6/M7 specs | Historical; do not rewrite as-built evidence. |

## 12. Work packages (approved: five sequential)

The owner approved five sequential work packages. Each is one branch under `m-8/`, merged into
`m-8/common` in order; no parallel wave.

| WP | Branch | Scope | Acceptance |
| --- | --- | --- | --- |
| WP-1 | `m-8/wp-1` | Documentation/status reconciliation with this spec: rendering/assets/API/guide/threading/nativeaot/projects/logging/contributing/roadmap/README; mark M8 in progress. No code. | Docs check; all conflicting statements fixed; no code. |
| WP-2 | `m-8/wp-2` | (Complete) Shader compiler tool + Silk.NET.Shaderc 2.23.0 + GLSL/pipeline/layout/samplers; camera projection/rounding; public `WorldToScreen`/`ScreenToWorld`/`WorldSize`; deterministic math/plan/instance-layout tests. | Zero-warning build; pipeline path compiles; public helper tests; format/docs. |
| WP-3 | `m-8/wp-3` | (Complete) `RendererStartInfo.Assets`; texture page cache/prefetch/pool/placeholder/upload fence/dynamic capacity/bindless; public `RenderStats` type/`IEngine.RenderStats`; fake-`IAssets` tests. | Page state machine/placeholder/growth/limit tests; no public Render type; no leak. |
| WP-4 | `m-8/wp-4` | Draw integration: per-camera passes, sort/filter/validity/flips, growable instance buffers, stats publishing, public `CaptureFrameAsync`, GPU rendering/readback-contract tests. | One draw per non-empty camera; no safety pass; validation zero; readback contract; allocation tests. |
| WP-5 | `m-8/wp-5` | Pong/AOT gate, 165 FPS benchmark, manual visual evidence, final docs, full gates, PR. | AOT zero warnings/runs; benchmark evidence; PR; docs. |

Dependencies: WP-1 → WP-2 → WP-3 → WP-4 → WP-5. Shared Vulkan/docs files rule out parallel waves.

**As-built status (2026-10-31):** WP-1 (`m-8/wp-1`), WP-2 (`m-8/wp-2`), WP-3 (`m-8/wp-3`), and WP-4
(`m-8/wp-4`) are complete. WP-2 landed the build-only shader compiler and MSBuild integration, the
GLSL/SPIR-V modules, the internal sprite pipeline (descriptor-set layout, 64-byte push-constant
pipeline layout, pipeline-per-sRGB-format, and sampler cache) created/destroyed by both renderers, the
shared camera projection/rounding helper, the public `WorldToScreen`/`ScreenToWorld`/`WorldSize`
members, and the pure sprite planner/quad/96-byte instance layout with deterministic tests. WP-3 added
the required internal `RendererStartInfo.Assets` seam, the internal `Bork2d.Render.Textures` page
subsystem (one page per path, async `IAssets` prefetch, content validation, the generated 8×8
placeholder uploaded and resident at startup, a device-local image suballocator, a host-visible staging
pool, per-page one-shot uploads with fences, bindless descriptor writes, and dynamic capacity up to the
effective update-after-bind limit with an explicit limit failure), the replacement per-frame resources
(binding 0 instance SSBO plus binding 1 variable bindless set), the extended `VulkanDeviceLimits`, the
public `RenderStats` record struct and `IEngine<T>.RenderStats` with the runtime-installed stats
source, and deterministic/GPU placeholder evidence. WP-4 added the shared `SpriteFramePlanner`
(resolve once: sort, page lookups, validation, quads, per-camera ranges, direct mapped-instance
writes), growable mapped `FrameInstanceResources` with the power-of-two/free-fence/limit policy, the
per-camera passes and one instanced draw per non-empty camera in both renderers, per-frame
`RenderStats` publication, the public `IEngine<T>.CaptureFrameAsync` with its renderer queue/pooled
staging/BGRA swizzle, Pong render-stats capture, deterministic plan/growth/stats/capture tests, and
opt-in GPU drawing/paging/capture tests (including the extended windowed resize/fullscreen/minimize/
stop test). Local suite: **1360 = 1334 deterministic + 26 conditional** (9 `[WindowFact]`, 3 reparse,
14 `[GpuFact]`); all 14 GPU tests pass with zero validation errors/warnings. WP-5 (the 165 FPS
benchmark, Pong presentation gate, final docs, and milestone PR) remains pending.

## 13. Acceptance criteria

1. **Build/static gates.** Debug/Release builds of `src/Bork2d/Bork2d.slnx` are zero-warning; tests
   pass; `dotnet format --verify-no-changes`, `pwsh scripts/check-docs.ps1`, and `git diff --check`
   pass.
2. **Public API.** Only the §6.9 additions: `ICamera.WorldToScreen`/`ScreenToWorld`/`WorldSize`,
   `IEngine.RenderStats`, `IEngine.CaptureFrameAsync`; public-surface/dependency tests agree; no
   public Render type; no other public member. WP-2 makes the three camera members `Current`; WP-3
   makes `RenderStats` `Current`; WP-4 makes `CaptureFrameAsync` `Current`.
3. **Cameras.** Projection/viewport/aspect/Y orientation are correct; half-up rounding/clamping;
   invalid cameras skip the whole camera with diagnostics; split-screen and camera order match §6.8.
4. **Screen/world helpers.** Round-trip/viewport/offset/rotation behavior, `WorldSize`, invalid-camera
   throw, non-finite-input throw, headless/disposed-window behavior; tests/docs.
5. **Sprites.** Size/pivot/UV/tint/transform/flips are correct; malformed fields fatal; non-Quad/
   null/invalid-layer skip; pending pages skip; failed pages draw the placeholder.
6. **Texture pages.** `IAssets` seam works; async prefetch never blocks the render thread; separate
   upload submit/fence; one page per path; placeholder for content failures (with the approved
   declared-dimension sizing); pool/suballocator; dynamic capacity to the device limit; explicit
   failure at the limit. WP-3 implements the subsystem and startup placeholder and proves it with
   fake-`IAssets` and GPU tests; WP-4 calls `EnsurePage` in the plan pre-pass and adds the page-growth
   GPU test.
7. **Draw/passes.** Exactly one draw per non-empty camera; per-camera passes with the approved
   load ops; no frame-level safety pass; empty `Clear.Color` clear-only pass; zero-camera/no-clear
   behavior documented and tested; one dynamic-rendering pass per camera.
8. **Instances.** 96-byte ABI; growable host-visible per-frame buffer; growth at a free fence;
   ranges/`firstInstance`; one draw per camera; no steady-state allocation.
9. **Pipeline/shader.** Build-time Silk.NET.Shaderc 2.23.0; checked-in GLSL; generated SPIR-V; no
   runtime shader I/O/compiler; pipeline creation failure is a clean renderer startup failure.
10. **Public stats/capture.** `RenderStats` semantics/defaults/headless/after-shutdown; consistent
    snapshot; `CaptureFrameAsync` RGBA8 length, latest/next completed frame, cancellation, headless/
    shutdown behavior, no tick block; no pixel/golden tests.
11. **Windowed/offscreen parity.** Same plan/draws/instances/clears for the same snapshot; both
    validation-clean; offscreen discard by default and capture on request.
12. **Performance.** Opt-in benchmark: 50,000 sprites, one camera, 1920×1080, median ≥165 FPS,
    zero steady-state allocations, p50/p95/machine evidence; no artificial cap/sleep.
13. **GPU evidence.** Opt-in GPU tests cover rendering/passes/placeholder/fatal/paging/windowed/
    readback contract with zero validation errors; warnings per D-M6-21; GPU tests local-only.
14. **Pong/AOT.** Zero-warning publish and approved content; `--headless`/`--windowed --ticks 120`/
    `--offscreen --ticks 120` exit 0 with one Setup/Teardown and exactly N ticks; Pong draws three
    sprites; `RenderStats` relationships; manual visual evidence.
15. **Docs/process.** Every §11 doc reflects the decisions/as-built behavior; no decisions.md entry;
    five sequential WPs; no direct `develop` commits; no auto-merge.

## 14. Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| 165 FPS at 50k on the integrated GPU is not achievable | Milestone exit fails | The owner set the target; measure early in WP-4/WP-5 and report; do not lower it silently. |
| Per-camera passes/no safety clear leave undefined content | Visual artifacts when there is no `Clear.Color` camera | Documented behavior; tests for zero-camera/clear-only; use a full-screen `Clear.Color` camera in samples; no hidden safety pass. |
| Dynamic page growth + descriptor-set recreation | Hitches, descriptor state loss, validation errors | Grow between frames with idle device; rewrite all page descriptors; GPU tests; log growth; fail at the device limit. |
| Pool/suballocator bugs | Corruption/leaks | Small, tested allocator; image granularity/alignment; shutdown destroy tests; no VMA. |
| Async `IAssets` from the render thread | Loader stalls, shutdown races, duplicate loads | Bounded prefetch; page states; ignore pending on stop; host joins renderer before asset stop; fake-asset tests. |
| Placeholder masks missing content | A game ships without noticing | Log once per failed path; placeholder counter; fatal for malformed sprite fields; docs. |
| Malformed sprite fatal is harsh | One bad asset kills the app | Explicit approved policy; clear exception naming the field; content authors fix the asset. |
| Public readback/stats API adds surface/threading risk | Contract bugs, AOT/trim issues | Minimal API; contract tests; no pixel tests; AOT-safe; update reference/guide; no Render types. |
| `CaptureFrameAsync` without dimensions | Caller may mismatch extent | Document that bytes match the captured frame; `RenderStats.FramebufferWidth/Height` reports the last frame; no resize guarantee. |
| Silk.NET.Shaderc build-time dependency | Restore/package/CI failure | Pin 2.23.0; build-only project; no runtime dependency; CI restore; verify the tool before merging. |
| 96-byte ABI drift CPU/GLSL | Garbage rendering | One layout constant, compile-time/unit tests, shader comment; GPU visual smoke. |
| Viewport rounding/seams | 1-pixel gaps/overlaps | Half-up rule; split-screen tests; docs. |
| Public API change without decision-log entry | Process deviation | Explicit owner instruction; this spec is the record; all other docs update in WP-1. |

## 15. Deferred

- Indexed non-Quad drawing, sprite slicing/validation, atlas/animation, editor preview.
- Mipmaps, KTX2/compressed textures, anisotropy, LOD/bias, texture arrays, sub-image upload.
- Texture eviction/streaming/reload, pack unloading.
- Depth, MSAA, HDR, post-processing, compute, indirect draws, batching, GPU timestamps.
- Device loss, multi-GPU/device override, transfer queue, VMA.
- Golden/pixel tests (no test readback); public screenshot files.
- Public builder capacity options, public diagnostics stream, public Render types.
- Multi-window, Linux/macOS guarantees, hot reload, audio/UI/physics/animation.
- The 0.1 release/API review (M10).

## 16. Related documents

- [Roadmap M8](../roadmap.md#5-milestone-detail) and [open decisions](../roadmap.md#9-open-decisions).
- [Rendering](../architecture/rendering.md) — pipeline, pages, camera, sorting, synchronization.
- [Assets and tooling](../architecture/assets-and-tooling.md#8-texture-descriptors) — texture
  descriptors and sprite assets.
- [Threading](../architecture/threading.md#3-frame-snapshot) — snapshot model and handoff.
- [Runtime lifecycle](../architecture/runtime-lifecycle.md) — startup/shutdown.
- [API reference](../api/reference.md#8-icameratlayer) and [guide](../api/guide.md#5-cameras).
- [Decision log](../decisions.md) — M6/M7 decisions referenced here (no M8 entries).
- [M7 spec](m7-frame-handoff.md#18-as-built-evidence-2026-10-31) — immediate as-built dependency.
- [M6 spec](m6-vulkan-bringup.md#18-as-built-evidence-2026-10-30) — renderer foundation.
- [Pong example spec](pong-example.md) — living example and AOT gate.
- [Contributing](../contributing.md), [NativeAOT](../architecture/nativeaot.md), [Projects](../architecture/projects.md).
