# M6 — Vulkan Bring-Up (spec)

> **Status:** **Implementation in progress (started 2026-10-30; WP-1 records the decisions).** This
> file defines the scope, semantics, evidence, work packages, and decision register for M6 (Vulkan
> bring-up); the roadmap progress log tracks implementation.
>
> Twenty-seven owner decisions (the original five gating questions plus nine follow-ups and thirteen
> final answers, all on 2026-10-29) are recorded in §3. They are approved decisions and are recorded
> verbatim in the [decision log](../decisions.md) by WP-1. **No owner decision is open**: §16 is kept
> as the answered decision register for traceability, and implementation details are left to the work
> packages. Do not treat a "proposed" note as a decision.
>
> M6 is roadmap [M6 — Vulkan bring-up](../roadmap.md#5-milestone-detail): a Vulkan 1.3 render thread
> that clears a window or an offscreen target. Frame handoff and pacing are M7; sprites and texture
> pages are M8.

## 1. Goal

Add a real Vulkan 1.3 renderer behind `Bork2d.Render` and compose it in `Bork2d.Runtime` so that:

- windowed mode creates an instance and a Win32 surface on the existing OS/window thread, and a
  render thread owns the device, the swapchain, and a dynamic-rendering **clear** pass presented to
  the window;
- offscreen mode creates no surface or swapchain, owns a fixed-extent sRGB color target, runs the
  same clear path on the render thread, and discards every frame;
- headless mode initializes neither SDL nor Vulkan, exactly as it does today;
- callbacks, assets, the builder surface, and the tick loop keep their current contracts; M6 adds no
  snapshot ring, no backpressure, no pacing, and no sprites;
- the AOT gate (`Pong.Desktop`) publishes and runs headless, windowed, and offscreen (D-M6-13)
  with zero AOT/trim warnings and validation-clean output.

The milestone is done when the exit criteria in §13 are demonstrated, not when the code merges.

## 2. Milestone definition (from the roadmap)

- Add `Silk.NET.Vulkan`; choose an allocator.
- Validation layers/debug messenger; device/queue/feature logging.
- Windowed: instance/surface on the OS/window thread; the render thread owns the device, swapchain,
  and dynamic-rendering clear. Offscreen: no surface, fixed-extent target, same clear path. Headless:
  no SDL/Vulkan.
- Resize/fullscreen/minimize and swapchain recreation; SPSC control queue and fatal-error channel;
  joinable render thread; FIFO default; validation-clean shutdown.
- AOT publish with windowed/offscreen smoke.

**Exit (roadmap):** the windowed clear reaches the window; resize/fullscreen/minimize do not
hang or leak; offscreen clears and discards; headless initializes neither SDL nor Vulkan; validation
reports zero errors.

## 3. Owner answers (recorded 2026-10-29)

These are the M6 decisions. They are approval-scoped to M6; re-ask if scope grows. WP-1 records
them in the decision log as `D-M6-01`…`D-M6-27` and links this spec.

| ID | Decision | Consequence |
| --- | --- | --- |
| D-M6-01 | **Render frames are continuous and decoupled from ticks.** After device/swapchain (or offscreen target) readiness, the render thread presents/renders continuously at the swapchain/vsync (windowed) or GPU-completion (offscreen) rate. The tick loop stays uncapped and M6 adds no tick/render backpressure. | M6 keeps the M2 uncapped loop; M7 adds the snapshot ring, bounded run-ahead, and coupling. The renderer must wake/wait without `Sleep`, and must not gate the tick. |
| D-M6-02 | **Windowed and offscreen require Vulkan; there is no public opt-out.** `Bork2d.Runtime` gets an **internal** renderer seam (`IRenderer` + factory) so deterministic host tests inject a fake renderer. A missing loader/device/feature is a `Bork2dException`. | No new public builder option; tests do not require a GPU; existing `FakeSdl` host tests keep running deterministically with a fake renderer. Headless still creates no renderer. |
| D-M6-03 | **One `vkAllocateMemory` per resource; no suballocator and no VMA in M6.** Zeroing/reuse strategy is implementation detail. | M6 allocates only offscreen color images (swapchain images are driver-owned); revisit in M8 when texture pages/buffers arrive. |
| D-M6-04 | **sRGB is required; fail startup.** If no sRGB surface/offscreen format exists, renderer setup throws `Bork2dException` naming the enumerated formats. | No silent UNORM fallback. Because of D-M6-06 the failure is reported from `Run()` before the first tick; `Setup` may already have run (§7). |
| D-M6-05 | **GPU tests are opt-in and local.** Real-device tests use a `[GpuFact]` attribute gated by `BORK2D_RUN_GPU_TESTS=1`, run in a serialized collection like `[WindowFact]`, and are not a CI job. Deterministic tests use internal seams and never touch Vulkan. | CI policy is unchanged (no GPU runner; runner still M8). GPU tests are part of the local M6 gate and the PR evidence. |
| D-M6-06 | **`Setup` does not wait for the renderer; the first `Tick` does.** `Run()` starts the renderer thread and proceeds to `Setup` immediately, even if device/swapchain readiness is incomplete. The loop waits for readiness before the first `Tick`; no tick runs before the renderer is ready. | `Setup` can build the scene and even call `Exit()` without a GPU; the window may be uncleared during `Setup`. A readiness failure before the first tick is reported from `Run()` after `Setup` has run (§7). |
| D-M6-07 | **Offscreen loops until `Exit`.** Offscreen runs `Setup` → `Tick`… → `Teardown` until `IEngine.Exit()` (or a host failure), like windowed, with no window/input and no pacing in M6. | `IEngine.Exit()` now stops the offscreen loop (public behavior change); offscreen `ITime` advances per tick like windowed; the Pong offscreen gate uses `--offscreen --ticks N` (D-M6-13). M7 adds pacing/handoff on top of the loop. |
| D-M6-08 | **The full descriptor-indexing set is required now.** Device selection requires and enables the six features listed in the rendering target (bindless array and update-after-bind set) in addition to Vulkan 1.3, dynamic rendering, and synchronization 2. | A GPU without bindless support is a startup failure in M6; log the enabled features and validate `maxPerStageDescriptorUpdateAfterBindSampledImages` for M8 capacity planning. |
| D-M6-09 | **Surface/instance lifetime (owner-delegated choice).** The OS/window thread creates and destroys both: after the renderer has joined and its device is destroyed, the OS thread calls `vkDestroySurfaceKHR`, then `vkDestroyInstance`, then destroys the window. The only exception is the single D-M6-16 surface-loss recovery, which replaces the surface on the same thread after the renderer drops its swapchain. | Single-threaded instance/surface lifetime; no render-thread destruction ordering; the render thread never touches the surface after its swapchain is gone. |
| D-M6-10 | **AOT resolver: a narrow suppression is acceptable if AOT is proven.** If ILC reports `IL3000`/`IL3002` from Silk.NET's Vulkan resolver, extend the existing narrow `Pong.Desktop` `NoWarn` with a written justification and prove a zero-warning publish and run; if that cannot be made clean, stop and re-ask. | No custom resolver work in M6 unless the suppression fails; revisit the suppression when Silk.NET changes. |
| D-M6-11 | **Two frames in flight; swapchain `minImageCount + 1` (typically 3, clamped to `maxImageCount`); two offscreen targets.** | CPU preparation overlaps GPU execution; per-frame command buffer/fence/semaphores and two offscreen color images. The counts are the M7/M8 baseline. |
| D-M6-12 | **Offscreen ticks are uncapped; render frames are paced only by GPU fences** while the D-M6-07 loop runs. No fixed frame cap or idle wait in M6. | Offscreen may keep the CPU and GPU busy until `Exit`; this is the pre-M7 baseline and is recorded as a known cost. |
| D-M6-13 | **The Pong AOT gate adds `--offscreen --ticks N`.** The game calls `IEngine.Exit()` after exactly `N` ticks; frames are continuous and their count is not asserted. | `PongArguments` gains an offscreen mode with a required tick limit; the gate runs `--headless`, `--windowed --ticks 120`, and `--offscreen --ticks 120`. |
| D-M6-14 | **A renderer failure around `Setup` lets `Setup` finish, attempts `Teardown`, and throws `Bork2dException` with zero ticks.** If `Setup` also fails, the `Setup` exception wins. | Confirms the §7 behavior: `Setup` is never interrupted or skipped because of renderer state; `Teardown` follows a successful `Setup`. |
| D-M6-15 | **Swapchain recreation uses `vkDeviceWaitIdle` + a new swapchain** (the old handle is passed as `oldSwapchain` and destroyed after the new one exists). `OUT_OF_DATE` recreates and retries without presenting; `SUBOPTIMAL` presents the current frame first, then recreates on the next iteration. | Simple and predictable; a short device-wide stall on resize/fullscreen. No lock-free recreation path in M6. |
| D-M6-16 | **Surface loss gets exactly one recovery attempt; device loss is always fatal.** On `VK_ERROR_SURFACE_LOST_KHR` the renderer destroys its swapchain, the OS thread creates a new surface from the same window/instance and destroys the old one, and the renderer creates a new swapchain on the same device. A second surface loss, a failed recovery, or `VK_ERROR_DEVICE_LOST` is fatal. | Bounded recovery with the surface still owned by the OS thread; the only render→OS-thread request in M6. |
| D-M6-17 | **`WithVSync(false)` prefers MAILBOX, then IMMEDIATE, then FIFO.** `WithVSync(true)` is FIFO. The requested and actual modes are logged. | Low-latency presentation when the driver supports it; FIFO is always the safe fallback. |
| D-M6-18 | **Resize/fullscreen/minimize signalling uses OS-thread control messages plus a render-thread wait.** The OS thread coalesces extent/fullscreen/minimize state and posts messages; the render thread recreates or blocks on its wake event. No polling loop and no `Sleep`. | The OS thread never blocks on the renderer; the render thread does not spin while minimized. |
| D-M6-19 | **Channels: one SPSC control queue (OS→render), an atomic stop flag (tick/host→render), and a one-shot fatal slot (render→host).** | Matches the roadmap's SPSC wording; no MPSC lock on the control path; the host observes the fatal at tick boundaries. |
| D-M6-20 | **A missing validation layer when requested logs a warning and the run continues without validation.** `WithValidation(true)` is honored as a request in every build; it never fails startup because the layer is absent. | Validation is best-effort on developer machines; Release is unaffected; zero validation errors remains an acceptance goal for runs where the layer is present. |
| D-M6-21 | **Validation messages are fatal only when validation is enabled** (Debug default or explicit `WithValidation(true)`): errors fail the run, warnings are logged/counted and are fatal in the `[GpuFact]` tests; with validation off, messages are logged only. | Release without validation keeps running; the validation-enabled gate is strict. Zero errors is the acceptance target. |
| D-M6-22 | **Debug labels cover the instance, device, swapchain images, command buffers, and the clear pass.** No per-frame or per-draw label churn in M6. | Good debugging value without hot-path cost; draw labels are M8. |
| D-M6-23 | **Clear evidence is counters/logs plus a manual visual check; no readback.** No public or internal readback is added in M6. | Keeps the milestone small; the roadmap's "clear reaches the window" is confirmed visually and by frame counters. Internal readback remains future work. |
| D-M6-24 | **No public additions in M6.** Only behavior changes (`WithVSync`, `WithValidation`, `IEngine.Exit` in offscreen, new `Bork2dException` cases) are documented. | `Bork2d.Runtime` still exposes exactly `Bork2dAppBuilder<TLayer>` and `Bork2dApp`; no renderer-info or counters API. |
| D-M6-25 | **The internal `IRenderer`/factory seam lives in `Bork2d.Render`.** Runtime calls it and passes opaque instance/surface handles; all Vulkan types stay in Render. | Keeps Vulkan out of Runtime code and tests inject a fake through the factory seam. |
| D-M6-26 | **The offscreen target prefers `R8G8B8A8_SRGB`, then `B8G8R8A8_SRGB`.** | No window-format constraint offscreen; RGBA is the natural payload order. No sRGB format at all still follows D-M6-04. |
| D-M6-27 | **Renderer cleanup failures during normal shutdown are logged and the run's existing outcome stands.** | Best-effort cleanup; a shutdown bug does not turn a successful run into a thrown exception. Validation-enabled runs still report the error. |

## 4. Inherited constraints (facts, not new decisions)

| Constraint | Source |
| --- | --- |
| Vulkan 1.3, dynamic rendering, no depth, no MSAA/HDR, straight alpha, sRGB attachments. | D-009; [Rendering](../architecture/rendering.md#2-backend-stack-and-device-requirements) |
| Windowed: SDL stays on the OS/window thread; the instance and surface are created there; the render thread owns device/swapchain/offscreen targets and never calls SDL. | [Overview §5](../architecture/overview.md#5-runtime-topology); [Threading §1](../architecture/threading.md#1-threads-by-mode); [Rendering §3](../architecture/rendering.md#3-frame-structure) |
| Headless initializes neither SDL nor Vulkan; offscreen creates no surface/swapchain. | D-017; [Runtime lifecycle §2](../architecture/runtime-lifecycle.md#2-modes); [NativeAOT §6](../architecture/nativeaot.md#6-native-libraries-and-silknet) |
| Windowed follows presentation, offscreen follows GPU completion; no fixed rate, `Sleep`, or spin-pacing in windowed/offscreen. | D-003, D-004; [Threading §4](../architecture/threading.md#4-triple-buffer-handoff), §5.3 |
| The public contract is `Bork2d.Engine` + `Bork2d.Runtime`; `Bork2d.Render` is internal; no SDL/Vulkan types leave the implementation, and game code references no renderer project. | [Projects §3](../architecture/projects.md#3-public-surface-and-assembly-visibility); overview invariants 1–2 |
| `WithVSync` and `WithValidation` are stored/validated today and become effective in M6. `WithOffscreen(w, h)` sizes are validated today. | [Runtime lifecycle §3](../architecture/runtime-lifecycle.md#3-builder-options-and-validation); [API reference §12](../api/reference.md#12-runtime-surface) |
| The frame starts with a safety clear to opaque black; per-camera `Clear.Color`/`Clear.None` are M8. No depth buffer; one window and one app process. | [Rendering §8](../architecture/rendering.md#8-sorting-layers-and-clears); overview invariants 9–10 |
| Runtime projects are NativeAOT/trim-safe; AOT publish/run is a required local gate, not CI. | D-016; [NativeAOT](../architecture/nativeaot.md); AGENTS.md §7 |
| Warnings/style/format/docs are gates; same-change docs; no unjustified suppressions. | [Contributing](../contributing.md) |

**Current baseline at spec time (2026-10-29).** Engine suite: 907 deterministic + 12 opt-in tests
(919); Pong suite: 20. `Bork2d.Render` is an empty scaffold (`AssemblyReferenceAnchor`) wired to
`Bork2d.Runtime`. Windowed runs an uncapped `Setup` → `Tick`… → `Teardown` loop with an SDL window
on an engine-owned OS thread; offscreen is one tick; headless is one tick and never loads SDL. The
builder stores `vsync`/`validation`; the window is created with `SDL_WINDOW_VULKAN` but no GPU object
exists. Local environment verified for this spec: Windows 11 10.0.26300, AMD Radeon 8060S, Vulkan
loader `vulkan-1.dll` present, Vulkan SDK 1.4.363.0 with `VkLayer_khronos_validation`, and
`Silk.NET.Vulkan`/`Silk.NET.Vulkan.Extensions.KHR` 2.23.0 in the NuGet cache. These are environment
facts, not portable requirements.

## 5. Scope

### 5.1 In scope

- `Silk.NET.Vulkan` (and the KHR extension package) pinned in `Bork2d.Render`; AOT/trim analysis of
  the new dependency; dependency-matrix and projects-doc updates.
- Instance creation for windowed (OS thread, SDL extension list) and offscreen (render thread);
  win32 surface creation/destruction for windowed; no surface for offscreen.
- Validation layers + `VK_EXT_debug_utils` messenger when requested; one-shot device/queue/feature
  logging; validation policy wiring.
- Physical-device selection (graphics queue, present support windowed, API 1.3, required features
  and extensions), with the selection and rejection reasons logged.
- Windowed swapchain: format/color space/present mode/extent/image count, acquisition, frames in
  flight, fences/semaphores, dynamic-rendering clear, present, and recreation on resize,
  fullscreen, minimize/restore, `OUT_OF_DATE`/`SUBOPTIMAL`.
- Offscreen: fixed-extent sRGB color target, same clear path, discard, no readback (D-M6-23), and the
  loop until `Exit` (D-M6-07).
- Renderer thread lifecycle: start/stop/join, stop request, control messages, fatal-error channel.
- Runtime composition: internal renderer seam/factory, startup/shutdown ordering, new error
  precedence, `WithVSync`/`WithValidation` effects.
- Counters/logging for frames presented/discarded, recreations, validation messages, and stop
  reason; internal test access.
- Tests: deterministic seam/logic tests and opt-in real-GPU tests (§9).
- AOT gate updates for `Pong.Desktop` (§10).
- Documentation and decision-log updates (§11).

### 5.2 Out of scope (deferred)

- Snapshot capture, the three-slot ring, bounded run-ahead, tick/render overlap, pacing, two-frame
  handoff: **M7**.
- Cameras, projections, sprite instances, pipelines/shaders, bindless texture pages, uploads,
  draws, sorting, layer culling, `Clear.Color`/`Clear.None`, offscreen/windowed image parity: **M8**.
- Public offscreen readback/capture, screenshots, recording: M8/M10 (D-M6-23 keeps M6 readback-free).
- Depth, MSAA, HDR, post-processing, KTX2/compressed formats, mipmaps, compute, indirect draws.
- Device-loss recovery, device override/multi-GPU selection, transfer queue: M10 (device loss stays
  fatal in M6; surface loss gets one recovery attempt per D-M6-16).
- Multi-window, Linux/macOS guarantees, hot reload, editor/preview.
- Reworking the window platform, input, time, assets, or the bake; no TOML/reflection in runtime.
- Any public renderer API (D-M6-24: none in M6).

## 6. Architecture

### 6.1 Modules, ownership, and boundaries

| Piece | Where | Responsibility |
| --- | --- | --- |
| `IRenderer` + factory + `RendererStartInfo`/status | `Bork2d.Render` internal (D-M6-25) | The composition contract used by `AppRuntime`; Runtime passes opaque instance/surface handles; fake for tests. |
| Vulkan instance/surface bootstrap | `Bork2d.Render` internal | Create the instance on the OS thread; enumerate layers/extensions; host the debug messenger; destroy instance/surface per D-M6-09. The windowed surface itself is created/destroyed through the `Bork2d.Runtime` SDL seam. |
| Device/queue/swapchain/offscreen/frames | `Bork2d.Render` internal | Vulkan objects, allocation, barriers, clear pass, present/discard, recreation, counters. |
| SDL Vulkan surface + extension query | `Bork2d.Runtime` internal SDL seam | Minimal additions to `ISdlApi`/`ISdlWindow`; fakes model success/failure. |
| Host ordering, error precedence, proxies | `Bork2d.Runtime` | Start/stop renderer around `Setup`/loop/`Teardown`; surface lifetime; fatal propagation. |

Boundary rules (enforced by the dependency test and review):

- `Bork2d.Render` references `Bork2d.Engine` and `Silk.NET.Vulkan*` only; it never references
  `Bork2d.Runtime`, `Bork2d.Bake`, or `Silk.NET.SDL`, and contains no SDL or windowing type.
- `Bork2d.Runtime` is the only project that sees both SDL and Vulkan handles; those handles are
  internal and never escape the assembly or appear in a public signature.
- `Bork2d.Engine` is untouched: no Vulkan, Silk, logger-provider, or GPU type; no public change.
- No public type is added in M6 (D-M6-24). `Bork2d.Runtime` still exposes exactly
  `Bork2dAppBuilder<TLayer>` and `Bork2dApp`.
- New source folders follow the folder-per-area/IDE0130 rule and update
  [Projects §4](../architecture/projects.md#4-source-layout-and-namespaces) in the same change.
  Proposal (not a decision): `Bork2d.Render/Vulkan/` → `Bork2d.Render.Vulkan`,
  `Bork2d.Render/Diagnostics/` → `Bork2d.Render.Diagnostics`, with the seam at the project root.

### 6.2 Threads and object ownership

| Object | Created on | Destroyed on | Notes |
| --- | --- | --- | --- |
| `ISdlApi`, SDL window | OS/window thread | OS/window thread | Existing M2 ownership; unchanged. |
| `VkInstance` (windowed) | OS/window thread | OS/window thread | Created after the window exists because SDL2's extension query takes the window (D-M6-09). |
| `VkSurfaceKHR` | OS/window thread (SDL) | OS/window thread: on shutdown after the device is destroyed; on D-M6-16 recovery after the renderer drops the swapchain | The renderer never uses a surface after its swapchain is destroyed; the old surface is destroyed after the renderer drops that swapchain. |
| `VkInstance` (offscreen) | Render thread | Render thread | No SDL, no surface extensions. |
| `VkPhysicalDevice`/`VkDevice`/queues | Render thread | Render thread | Selection logged once; no override. |
| Swapchain, image views, offscreen images/memory | Render thread | Render thread | Only the render thread touches swapchain/offscreen state. |
| Command pools/buffers, fences, semaphores | Render thread | Render thread | Per-frame objects. |
| Debug messenger | With the instance (OS thread) | Before the instance (OS thread) | Callbacks may run on the render thread; external synchronization only. |
| Control/fatal channels | Any producer → render | — | See §6.7 (control, stop, fatal, surface-recovery request). |

Rules:

- The tick thread never calls Vulkan or SDL and never blocks on the renderer per frame; its only
  renderer wait is readiness before the first tick (D-M6-06).
- The OS/window thread never creates/uses the device, swapchain, images, or command buffers; after
  surface publication it only reports window/state changes and destroys surface/instance at
  shutdown (D-M6-09).
- The render thread never calls SDL and never reads live scene/entity state (there is no snapshot in
  M6).
- No Vulkan call is made while holding a lock used by another thread; no lock is held across
  acquire/present/fence waits, and no `vkDeviceWaitIdle` except swapchain recreation and shutdown
  (D-M6-15).
- Start and stop are synchronous from the host's point of view: `Run()` returns only after the
  renderer thread has joined; no renderer thread outlives the app, the surface, or the instance.

### 6.3 Startup and shutdown sequence

Host ordering (D-M6-06/D-M6-07; failure behavior in §7):

**Windowed**

1. `assetService.Start()` (unchanged; base pack resolves before any window).
2. `platform.Start()`: SDL init → window (with `SDL_WINDOW_VULKAN`) → instance (validation policy,
   SDL instance extensions) → surface; publish the confirmed window state; start the event pump.
3. Create the renderer and `Start()` it: the render thread begins physical-device selection →
   device/queues → swapchain → frames/semaphores/fences. `Start()` returns **without waiting** for
   readiness (D-M6-06). With D-M6-01 the thread then presents continuously once ready.
4. Attach window/input proxies; `Setup` runs immediately, even while the renderer is still
   initializing; it may call `IEngine.Exit()`.
5. If `Exit`/close was requested during `Setup`, run zero ticks and skip the readiness wait.
6. Otherwise wait for renderer readiness before entering the loop. A readiness failure is handled
   per §7.
7. Uncapped loop: `input.Freeze()` → `time.NewFrame()` → `input.DispatchText()` → `Tick()`.
8. `Teardown`.
9. `renderer.Stop()`/join: stop request, wake, cancel/complete any in-progress initialization, GPU
   idle as required, destroy frames/swapchain/device; the renderer reports its fatal error (if any)
   and counters.
10. `platform.Shutdown()`: the renderer has joined and its device is gone, so the OS thread destroys
    the surface (`vkDestroySurfaceKHR`), then the instance, then the window, quits SDL, and joins the
    OS thread (D-M6-09).
11. `assetService.Stop()`; detach proxies; `Dispose()` detaches root children.

**Offscreen** (D-M6-07: windowed-shaped loop until `Exit`)

1. `assetService.Start()`.
2. Create the renderer and `Start()` it without waiting for readiness (D-M6-06): render thread →
   instance → device/queues → target(s) → frames. Frames clear/discard continuously once ready
   (D-M6-01).
3. `Setup` runs immediately.
4. If `Setup` requested `Exit`, skip the readiness wait and run zero ticks; otherwise wait for
   renderer readiness and loop the windowed tick sequence without input/window
   (`time.NewFrame()` → `Tick()`) until `IEngine.Exit()` or a failure. The tick loop is uncapped;
   render frames are paced only by GPU fences (D-M6-12).
5. `Teardown` (attempted once `Setup` succeeded).
6. `renderer.Stop()`/join; the target(s)/device are destroyed.
7. `assetService.Stop()`.

**Headless** — unchanged M1 one-tick sequence; no renderer, no SDL, no Vulkan; even constructing the
renderer seam or loading `vulkan-1.dll` is forbidden.

### 6.4 Windowed path

**Instance.**

- API version 1.3; application/engine name from the entry assembly (same defaulting style as the
  window title); no engine version claim beyond the assembly version if one exists today.
- Instance extensions are exactly SDL's required list plus `VK_EXT_debug_utils` when validation is
  enabled and the extension is available. `VK_KHR_win32_surface`/`VK_KHR_surface` come from SDL's
  list; do not hard-code platform extensions.
- Layers: `VK_LAYER_KHRONOS_validation` when requested; availability is checked before
  `vkCreateInstance` (D-M6-20: a missing layer logs a warning and continues).
- The instance requests API 1.3; the renderer does not negotiate a lower version. A loader or
  instance that cannot provide 1.3 is a startup failure naming the reported version.

**Surface.** `SDL_Vulkan_CreateSurface` on the OS/window thread; the handle is published to the
renderer as an opaque value. SDL's drawable size (framebuffer pixels) is the authority for the
surface extent, matching D-M2-11 and `IWindow.Size` semantics. `Silk.NET.SDL` 2.23.0 (checked)
exposes `VulkanLoadLibrary`, `VulkanGetInstanceExtensions`, `VulkanCreateSurface`, and
`VulkanGetDrawableSize`; it exposes **no** surface-destroy call (SDL2 has none), so
`vkDestroySurfaceKHR` is the only destruction path; the OS/window thread destroys it after the
renderer has joined and its device is gone (D-M6-09).

**Physical device.** Enumerate all devices; log every candidate at Debug with name, type, API
version, driver version, queue families, and why it is accepted/rejected. Accept a device when it
provides Vulkan 1.3, a graphics queue family (windowed: also present support on that family), the
required extensions (`VK_KHR_swapchain` windowed), and the required features of §6.10. Preference:
discrete GPU, then integrated, then any other suitable device; first suitable within the preference
class; no override in M6. No suitable device is a startup failure naming the reason.

**Device and queues.**

- One graphics queue family that also presents (windowed); one queue from it. No transfer/compute
  queue in M6.
- Enable the required features (D-M6-08) and the extensions M6 uses (dynamic rendering,
  synchronization 2, swapchain, debug utils as applicable); do not enable features the renderer
  does not use.
- Log the chosen device, API/driver version, queue family, enabled features/extensions, and the
  limits relevant to M6 (attachment size, image count, framebuffer extent).

**Swapchain (windowed).**

- Format: `B8G8R8A8_SRGB` first, then `R8G8B8A8_SRGB`; color space
  `VK_COLOR_SPACE_SRGB_NONLINEAR_KHR`. No sRGB format → D-M6-04 startup failure listing the
  available formats.
- Present mode: FIFO when `WithVSync(true)` (default); when false, MAILBOX, then IMMEDIATE, then
  FIFO (D-M6-17). Log the requested and actual mode.
- Image count: `minImageCount + 1` (typically 3), clamped to `maxImageCount` when non-zero
  (D-M6-11).
- Extent: current framebuffer size clamped to `minImageExtent`/`maxImageExtent`; zero extent
  (minimized) skips acquire/present (see below).
- Composite alpha `OPAQUE`, pre-transform equal to `currentTransform` (identity when the surface
  reports identity; no rotation handling in M6), clipped true, usage `COLOR_ATTACHMENT`,
  sharing exclusive, `oldSwapchain` on recreation (D-M6-15).
- Images only need views in M6 (no sampler, no readback).

**Frames.** Two GPU frames are in flight (D-M6-11); each frame owns a command buffer/pool, an
in-flight fence, an image-available semaphore, a render-finished semaphore, and the per-frame state.
Per frame:

1. Wait on the frame fence before rewriting any frame resource.
2. `vkAcquireNextImageKHR` with `imageAvailable`; `OUT_OF_DATE` → recreate and retry without
   presenting; `SUBOPTIMAL` → present the current frame, then recreate on the next iteration
   (D-M6-15).
3. Reset the fence/command buffer; record the synchronization-2 barrier
   `UNDEFINED → COLOR_ATTACHMENT_OPTIMAL` (or `PRESENT_SRC_KHR → COLOR_ATTACHMENT_OPTIMAL` on
   reuse if chosen).
4. `vkCmdBeginRendering` with one sRGB color attachment, `loadOp = CLEAR` to opaque black,
   `storeOp = STORE`, no depth/stencil, no draws; `vkCmdEndRendering`.
5. Barrier `COLOR_ATTACHMENT_OPTIMAL → PRESENT_SRC_KHR`.
6. Submit with synchronization 2, wait `imageAvailable` at `COLOR_ATTACHMENT_OUTPUT`, signal
   `renderFinished`; `vkQueuePresentKHR` waits `renderFinished`; count the presented frame.
7. The fence signals frame reuse.

No per-frame allocations in steady state, no `vkDeviceWaitIdle` in the steady path, and no
present-mode-specific fallback that hides a validation error.

**Resize/fullscreen/minimize.**

- The OS thread already coalesces `IWindow` commands and publishes confirmed state (M2). M6 must
  turn framebuffer-size/fullscreen/minimize changes into swapchain work without blocking the OS
  thread (D-M6-18 control messages).
- Resize: coalesce to the latest extent, recreate once, log at Debug (old/new extent);
  `OUT_OF_DATE`/`SUBOPTIMAL` recreation follows the same path.
- Minimized/zero extent: acquire and present are skipped; no swapchain is created with a zero
  extent; the render thread must not spin while there is nothing to present — it waits on its wake
  event until control/stop (D-M6-18); ticks continue (D-M2-11).
- Fullscreen: the window/surface extent changes; recreate the swapchain with the new extent and
  capabilities; no new mode, no exclusive fullscreen.
- Close/`Exit`: the host stops the tick loop, tears down the game, stops the renderer, then
  destroys surface/window; the renderer must not present after the window is gone.

### 6.5 Offscreen path

- No SDL, no surface, no swapchain, no present. `WithOffscreen(w, h)` sizes are validated by the
  builder today; they are the fixed target extent.
- Instance/device selection is the same policy without surface/present/swapchain requirements.
- Format: `R8G8B8A8_SRGB` first, then `B8G8R8A8_SRGB` (D-M6-26), supported for optimal tiling. No
  sRGB format → D-M6-04 failure.
- Two permanent color images (D-M6-11) with views, `vkAllocateMemory` per image (D-M6-03),
  device-local memory. Layout starts `UNDEFINED`, transitions once to `COLOR_ATTACHMENT_OPTIMAL`
  per image and stays there.
- Same dynamic-rendering clear pass, one attachment, opaque black, `storeOp = STORE` (discarded
  later; `STORE` keeps the pass identical to windowed and future-proof for readback).
- No readback API and no internal readback in M6 (D-M6-23).
- Frames are discarded; the render thread continues per D-M6-01 until stop. Stop waits for in-flight
  work and destroys targets/device.
- Offscreen runs the windowed-shaped loop until `Exit` (D-M6-07): `Setup` → `Tick`… → `Teardown`,
  with `time.NewFrame()` each tick and no input/window. The first tick still waits for renderer
  readiness (D-M6-06); ticks are uncapped and render frames are paced only by GPU fences (D-M6-12),
  with no fixed frame cap or idle wait in M6 (M7 owns handoff/backpressure).

### 6.6 Headless

Unchanged. No window platform, no renderer, no instance/device, no Vulkan loader call, no SDL call,
one tick, no clock advance. M6's evidence must show that a headless run makes no Vulkan/SDL call
(internal seam spy, absence of platform/renderer log lines, and no renderer construction) and, if a
GPU-less machine is available, succeeds there as well.

### 6.7 Control and fatal channels

The roadmap requires an SPSC control queue and a fatal-error channel. M6 uses (D-M6-19):

- **OS thread → render thread (SPSC control queue)**: framebuffer extent changes (with the new pixel
  size), fullscreen changes if they alter capabilities, and minimize/restore; latest value wins per
  property, order preserved across properties (D-M6-18).
- **Tick thread / host → render thread (atomic stop flag)**: stop request (graceful shutdown), so
  shutdown does not depend on a frame boundary.
- **Render thread → host (one-shot fatal slot)**: fatal error (`Bork2dException` with the Vulkan
  result and operation) and counters/status; the host checks it at tick boundaries like the M2
  platform fatal (D-033 pattern) and rethrows after teardown.
- **Render thread → OS thread (surface-loss recovery only)**: a single request for a new surface
  when `VK_ERROR_SURFACE_LOST_KHR` occurs (D-M6-16); the OS thread creates the new surface from the
  same window/instance and destroys the old one. This is the only render→platform request in M6.
- **Wake/wait**: enqueue signals an event (or equivalent) that the render thread waits on when it
  has nothing to present (minimized, zero extent, stopped); no busy loop, no `Thread.Sleep`, no
  timeout-based polling in production. Bounded waits are allowed in tests only.
- The queue is a single-producer/single-consumer structure; the stop flag is atomic; the fatal slot
  is written once. All publication uses `Interlocked`/`Volatile` release/acquire, and no lock is held
  across a Vulkan call.

### 6.8 Frame content in M6

M6 renders a clear only:

- Every frame is one dynamic-rendering pass with one sRGB color attachment, `loadOp = CLEAR` to
  opaque black `(0, 0, 0, 1)`, `storeOp = STORE`, no depth, no draws, then present (windowed) or
  discard (offscreen). This is the M8 safety-clear shape with an empty camera set.
- Cameras, `Clear.Color`, `Clear.None`, `Clear.Depth`, sprites, layers, `ZIndex`, tint, and sorting
  are not read at all in M6. No snapshot exists. `Clear.Depth` logging and camera counters are M8.
- The window is therefore black in M6 (documented); the Pong title/logs carry game state as today.

### 6.9 Validation and debug messenger

- `WithValidation(true)` (Debug default) requests `VK_LAYER_KHRONOS_validation` and
  `VK_EXT_debug_utils`; Release default remains off. The builder flag becomes effective in M6
  (docs update required).
- If the requested layer is not installed, the renderer logs a warning and continues without
  validation (D-M6-20); `WithValidation(true)` is honored as a request in every build but is never a
  startup failure because of a missing layer.
- The messenger is installed when the layer/extension is available and logs each message with
  severity, message id/name, and the Vulkan objects. `VK_EXT_debug_utils` names/labels cover the
  instance, device, swapchain images, command buffers, and the clear pass (D-M6-22); there is no
  per-frame or per-draw label churn.
- Message policy (D-M6-21): when validation is enabled (Debug default or explicit
  `WithValidation(true)`), validation errors fail the run, warnings are logged/counted and are fatal
  in the `[GpuFact]` tests; when validation is off, messages are logged only. Zero validation errors
  is the acceptance target.
- The validation layer is a developer-machine dependency (SDK); it is never shipped, never required
  for Release, and must not be required for the AOT headless gate.

### 6.10 Device and feature requirements

M6 requires at minimum:

| Requirement | Windowed | Offscreen |
| --- | --- | --- |
| Vulkan API 1.3 | yes | yes |
| Graphics queue | yes | yes |
| Present support on the graphics family | yes | n/a |
| `VK_KHR_swapchain` | yes | no |
| `dynamicRendering` | yes | yes |
| `synchronization2` | yes | yes |
| Descriptor-indexing (bindless) set (six features) | yes | yes |
| sRGB color format | yes | yes |
| `VK_EXT_debug_utils` | when validation on and available | same |

The descriptor-indexing set is the rendering target's six features and is a device requirement in M6
(D-M6-08): `runtimeDescriptorArray`, `shaderSampledImageArrayNonUniformIndexing`,
`descriptorBindingPartiallyBound`, `descriptorBindingSampledImageUpdateAfterBind`,
`descriptorBindingUpdateUnusedWhilePending`, and `descriptorBindingVariableDescriptorCount`. They are
enabled and logged; a device missing any of them is rejected with a startup failure naming the
missing feature. `maxPerStageDescriptorUpdateAfterBindSampledImages` is validated and logged for M8
page-capacity planning.

Device loss (`VK_ERROR_DEVICE_LOST`) and any unexpected `VK_ERROR_*` are fatal in M6 (no recovery).
Surface loss (`VK_ERROR_SURFACE_LOST_KHR`) gets exactly one recovery attempt (D-M6-16): the renderer
destroys its swapchain, the OS thread creates a new surface from the same window/instance and destroys
the old one, and the renderer creates a new swapchain on the same device. A second surface loss, a
failed recovery, or a device loss is fatal. The failure path must not hang: record the fatal, wake the
host, join cleanly where possible.

### 6.11 Memory

- D-M6-03: one `vkAllocateMemory` per offscreen image (and any M6 buffer, of which there should be
  none). No allocator abstraction, no VMA, no suballocation; `vkFreeMemory` on destruction.
- Memory type selection: device-local, `COLOR_ATTACHMENT`-capable, no host visibility required in
  M6. Validate the requested extent against `maxImageDimension2D` and fail with a clear message
  rather than a driver error.
- Zero-init is not required for color attachments (the clear pass writes every pixel), but the
  choice must be documented in the implementation.
- M8 will revisit allocation for texture pages/buffers; do not design a general allocator in M6.

### 6.12 Diagnostics, logging, and counters

New logs use the existing `Bork2d.Render` category ([Logging §5](../architecture/logging.md#5-categories)),
one-shot, no per-frame lines:

| Event | Level | Fields |
| --- | --- | --- |
| Instance created | Information | `apiVersion`, `validation` on/off, enabled layers/extensions |
| Validation layer/extension missing | Warning (D-M6-20); the run continues | requested/available |
| Physical devices enumerated | Debug | each name/type/API/driver and accept/reject reason |
| Device selected | Information | name, type, vendor/device id, API/driver version, queue family, enabled features/extensions, relevant limits |
| Swapchain created | Information | format, color space, present mode (requested/actual), extent, image count |
| Swapchain recreated | Debug | reason (`Resize`/`Fullscreen`/`OutOfDate`/`Suboptimal`/`Minimize`), old/new extent |
| Minimized / zero extent | Debug | skipped acquire/present |
| Offscreen target created | Information | extent, format, images, memory bytes |
| Renderer started/stopped | Information | mode, frames presented/discarded, recreations, validation error count, stop reason |
| Validation message | Warning/Error | severity, id/name, message |
| Surface lost and recovered | Warning | `VkResult`, attempt number, new extent/present mode |
| Fatal Vulkan failure | Error | operation, `VkResult`, validation detail; exception carries it |

Counters (internal, exposed to tests and logged once at stop): frames presented, frames discarded,
swapchain recreations, validation errors/warnings, last frame number, last present result. No public
counters API in M6 (D-M6-24).

No per-frame, per-sprite, or per-image logging; no logging inside the acquire/present hot path.

### 6.13 NativeAOT and native deployment

- Pin `Silk.NET.Vulkan` and `Silk.NET.Vulkan.Extensions.KHR` (the swapchain entry points) to the
  existing Silk.NET line, `2.23.0`, to match `Silk.NET.SDL`; document the pins in
  [Projects §1](../architecture/projects.md#1-current-projects).
- `Bork2d.Render` needs `AllowUnsafeBlocks`; keep `IsAotCompatible`, trim/AOT analyzers, and
  warnings-as-errors.
- `vulkan-1.dll` is supplied by the system/driver on Windows and is not copied by publish; a machine
  without a Vulkan 1.3 driver cannot run windowed/offscreen (D-M6-02).
- Silk.NET's native resolver may emit `IL3000`/`IL3002` under ILC, as with SDL. Per D-M6-10 a narrow
  `NoWarn` suppression is acceptable **only if** the publish is proven zero-warning and the
  published app runs; extend the existing `Pong.Desktop` `NoWarn` with a written justification in
  that case. If the suppression cannot make the publish clean, stop and re-ask instead of adding a
  custom resolver ad hoc.
- Validation layers are loaded at runtime via the loader; they are not part of the publish. The
  Release AOT gate runs with validation off; a Debug/GPU run with validation on is the validation
  evidence.
- Headless must not trigger the loader: no `NativeLibrary.Load`, no `VkInstance`, no Vulkan call or
  renderer construction on the headless path.

### 6.14 Runtime surface impact

Expected: **no public API addition** (D-M6-24). Behavior changes, documented in the same milestone:

- `WithVSync` now selects the present mode.
- `WithValidation` now enables layers/messenger.
- Windowed and offscreen `Run()` now fail with `Bork2dException` when Vulkan is unavailable or
  lacks the required device/feature/format.
- Windowed now shows a black cleared window; offscreen now initializes a GPU and discards frames.
- `IEngine.Exit()` now stops the offscreen loop (D-M6-07); offscreen `ITime` advances per tick like
  windowed, while `IWindow` members and the `IInput` getter still throw `NotSupportedException`.
- Renderer failures are reported from `Run()` after `Setup` may have run (D-M6-06), before the first
  tick.

## 7. Runtime lifecycle integration

The lifecycle document ([Runtime lifecycle](../architecture/runtime-lifecycle.md)) must be updated
in the same change. New/changed rows:

| Situation | Where detected | Behavior |
| --- | --- | --- |
| Vulkan loader missing / instance creation fails / no Vulkan 1.3 | Windowed `platform.Start` (OS thread) | `Bork2dException` before the renderer and `Setup`; window/surface/instance cleaned; OS thread joined; assets stopped; no `Teardown`. |
| Renderer setup/readiness fails (no device, missing feature/descriptor set, no sRGB format, swapchain creation) | Renderer thread; observed before the first tick | `Setup` still runs (D-M6-06). `Run()` then attempts `Teardown` (Setup succeeded), joins the renderer, stops the platform/assets, and throws `Bork2dException`; zero ticks run (D-M6-14). If `Setup` itself also failed, the `Setup` exception is rethrown (existing precedence). |
| Renderer setup fails while `Setup` is running | Renderer thread | Same as above: `Setup` is allowed to finish; the failure surfaces after it returns. `Setup` is not interrupted. |
| Exception in `Setup` while the renderer is running or initializing | `Run()` | The renderer is stopped/joined (cancelling in-progress initialization if needed); platform/assets are shut down; the original exception is rethrown; no `Teardown` (unchanged rule). If the renderer also failed, the `Setup` exception wins (D-M6-14). |
| Validation layer requested but missing | Instance creation | Warning; the run continues without validation (D-M6-20). |
| Validation error/warning | Render thread | When validation is enabled: errors fatal, warnings logged/counted and fatal in `[GpuFact]` tests; with validation off: logged only (D-M6-21). |
| Surface lost | Render thread | One recovery attempt: new surface via the OS thread + new swapchain on the same device; a second loss is fatal (D-M6-16). |
| Device loss / unexpected `VK_ERROR_*` | Render thread | Stored fatal; tick loop stops at the next boundary; `Teardown` attempted; renderer joined; platform/assets shut down; fatal rethrown (`AggregateException` with a teardown failure, per the M2 pattern). |
| Renderer stop/cleanup failure during normal shutdown | Renderer `Stop` | Logged; the run's existing outcome stands (D-M6-27). |
| `Dispose()` during `Run` | unchanged | `InvalidOperationException`; renderer is not disturbed. |
| Second `Run`/post-`Dispose` | unchanged | `InvalidOperationException`/`ObjectDisposedException`. |

Shutdown order is §6.3: renderer joined before surface/instance/window destruction, and before the
asset service stops. A missing base pack still fails before any window or GPU object (unchanged).

`ITime`/input/`Exit` semantics: windowed is unchanged; headless is unchanged (one tick, `Exit`
ignored, clock not advanced); offscreen now honors `Exit` and advances `ITime` per tick (D-M6-07)
while keeping `IWindow`/`IInput` unavailable. Ticks are not coupled to frames (D-M6-01); `Exit`/close
stop the loop at the next boundary.

## 8. Invariants added by M6

1. Headless never calls SDL or the Vulkan loader, creates no instance/device/thread, and remains
   one-tick until M7.
2. Windowed creates exactly one instance and one initial surface, both on the OS/window thread; a
   replacement surface exists only through the single D-M6-16 recovery. Offscreen creates exactly one
   instance with no surface/swapchain.
3. The render thread owns the device, swapchain, offscreen targets, frames, and all recording;
   no other thread touches them.
4. The tick thread never blocks on the renderer per frame; the only renderer wait is readiness
   before the first tick (D-M6-06). There is no ring, pacing, or handoff yet.
5. `Setup` runs without waiting for renderer readiness and is not skipped because the renderer
   failed; no `Tick` runs before the renderer is ready.
6. Every frame begins with an opaque-black clear and has no depth; the clear pass shape is the M8
   safety-clear shape with zero cameras.
7. Offscreen uses the windowed loop shape without a window/input, honors `Exit`, advances `ITime`,
   and adds no pacing (D-M6-07).
8. The instance outlives the renderer; the surface is destroyed only after the device is destroyed
   and before the window, except the D-M6-16 recovery where the old surface is destroyed only after
   the renderer drops the swapchain that used it; the app joins the render thread before destroying
   any of them (D-M6-09).
9. Fatal renderer errors are one-shot, stored, and rethrown on the `Run()` caller after teardown.
10. No per-frame allocations, sleeps, busy-waits, or `vkDeviceWaitIdle` in the steady state.
11. Shutdown leaves no Vulkan objects behind (validation-clean) and no renderer thread running.
12. Windowed/offscreen cannot silently degrade to "no renderer": Vulkan failure is a `Bork2dException`
    (D-M6-02).

## 9. Test strategy

### 9.1 Deterministic tests (no GPU)

These must run in CI on every PR and use internal seams, not Vulkan:

- **Renderer seam/host lifecycle**: fake `IRenderer` records `Start`/`Stop` ordering and supports
  delayed readiness, readiness failure, and a post-start fatal. Assert: `Setup` runs before
  readiness and is not skipped by a renderer failure; no `Tick` runs before readiness; readiness
  failure throws `Bork2dException` after `Setup`, attempts `Teardown`, and joins; a fatal after the
  first tick stops the loop, runs `Teardown`, rethrows, and joins; `Dispose` during `Run` is
  unchanged; headless never constructs or starts a renderer; offscreen runs ticks until `Exit`,
  advances `ITime`, and stops the renderer after `Teardown` (D-M6-07).
- **Windowed host with `FakeSdlApi`**: existing windowed loop/input/time/proxy tests inject the fake
  renderer, so they stay fast and deterministic. The fake SDL seam gains the minimal Vulkan surface
  members and models extension-query/surface-creation failure for error-path tests.
- **Pure policy functions**: device acceptance/scoring (API version, queue families, features,
  extensions), sRGB format selection, present-mode selection, extent clamp, and
  resize/minimize/recreate decisions as pure functions over plain descriptors. This is where the
  bulk of M6 logic coverage lives.
- **Control/fatal channel**: SPSC (D-M6-19) ordering, latest-value-wins, coalescing, stop wake,
  one-shot fatal publication, no lost wake-up; bounded joins, no sleeps.
- **No-regression**: the existing 919 engine tests and 20 Pong tests stay green; dependency-matrix
  and public-surface tests are updated for the new Render packages and assert that Render adds no
  public types and Runtime still exposes exactly `Bork2dAppBuilder<T>`/`Bork2dApp`.
- **AOT/trim**: normal builds with analyzers as errors; the publish gate is §10.

### 9.2 Opt-in real-GPU tests (`BORK2D_RUN_GPU_TESTS=1`)

`[GpuFact]`, serialized collection (no parallel GPU tests), skipped by default, bounded joins, no
sleeps, and all Vulkan objects destroyed:

- Instance/device creation with validation on; assert the expected device/feature/enabled-extension
  log data and zero validation errors when the validation layer is available (D-M6-20/D-M6-21).
- Windowed: create a real SDL+Vulkan window, present N frames (e.g. 30), assert counters, then clean
  shutdown; the black clear is confirmed by the manual visual check and counters (D-M6-23), not a
  pixel readback.
- Resize sequence: several extents in a row (including a zero/minimized extent where the platform
  allows it), restore, fullscreen enter/leave; assert no hang, at least one successful recreation,
  and zero validation errors (when the layer is available).
- Minimize/restore: skip acquire/present while minimized, resume on restore, no deadlock.
- Close/stop under load: stop while frames are in flight; renderer joins; no leak reports.
- Surface-loss recovery: if the environment can produce a real loss, exercise exactly one recovery;
  otherwise the recovery policy/state machine and the OS-thread surface replacement are covered with
  fakes (D-M6-16).
- Offscreen: fixed extent, the test game calls `Exit` after N ticks, frames clear/discard (uncapped,
  D-M6-12), no surface/swapchain objects, clean shutdown, zero validation errors when the layer is
  available. Frame count is not asserted for equality (D-M6-01) and there is no pixel readback
  (D-M6-23).
- AOT-published `Pong.Desktop` on a GPU machine: `--headless` (no Vulkan use), `--windowed --ticks
  120`, and `--offscreen --ticks 120` (D-M6-13).

The PR evidence records the machine (GPU, driver, Vulkan SDK/layer version), the commands, and the
observed counters/validation output. CI remains unchanged (D-M6-05).

### 9.3 Existing-suite impact

Tests that currently drive the real `AppRuntime` with `FakeSdlApi` must not require Vulkan: they use
the fake renderer through the internal factory seam (or an internal builder/test hook). Tests that
assert "no GPU work" for headless remain and are strengthened with a renderer-factory spy. The
existing one-tick offscreen tests migrate to the D-M6-07 loop: the test game calls `Exit` after the
ticks it wants, `ITime`/`Setup`/`Tick`/`Teardown` counts and the `Exit`-during-`Setup` path are
re-asserted, and nothing depends on wall-clock pacing. No test uses `Thread.Sleep`; all joins are
bounded; no assertions on frame timing or present order beyond counters.

## 10. Pong example and AOT gate

- `Pong.Desktop --headless` is unchanged: one tick, baked pack assertions, no SDL/Vulkan, must still
  exit 0 and make no Vulkan/SDL call (spy/log evidence; a GPU-less machine run is recorded when
  available).
- `Pong.Desktop --windowed --ticks N` still exits after exactly `N` ticks; `Setup` runs before the
  renderer is ready and the first `Tick` waits for readiness (D-M6-06); the renderer then runs
  concurrently (D-M6-01), so frames presented are not deterministic and must not be asserted for
  equality. The gate requires exit 0, one `Setup`/`Teardown`, a black cleared window, and clean
  validation/shutdown.
- `Pong.Desktop --offscreen --ticks N` is added (D-M6-13): offscreen loops until `Exit` (D-M6-07), so
  the game calls `Exit` after exactly `N` ticks; the renderer's frame count is continuous and not
  asserted. `PongArguments` parsing, usage text, and tests change consistently; the AOT gate runs
  `--offscreen --ticks 120`.
- Publish expectations stay: single-file AOT exe, `assets.pck`, `SDL2.dll`, engine/runtime/game
  PDBs; no `vulkan-1.dll` copy (system loader), no bake/tooling assemblies, zero AOT/trim warnings.
- The Pong game code needs no renderer API (its window stays black until M8). Offscreen reuses
  `PongGame`'s existing tick-limit `Exit()`; only `Pong.Desktop`/`PongArguments` change for the new
  mode.

## 11. Documentation and decision-log changes

| Document | Change |
| --- | --- |
| `docs/decisions.md` | WP-1 records `D-M6-01`…`D-M6-27` (§3). |
| [Rendering](../architecture/rendering.md) | §11 status: instance/device/swapchain/offscreen/clear `Current` (M6); validation/messenger/counters status; remaining M7/M8 markers stay. |
| [Runtime lifecycle](../architecture/runtime-lifecycle.md) | Modes table (§2): windowed/offscreen now run a renderer and offscreen loops until `Exit`; startup order (§5: `Setup` before readiness, first `Tick` after), builder option status (§3: `WithVSync`/`WithValidation` current), `ITime`/`Exit` (§4/§5), error table (§6), shutdown (§6), and the "no GPU" statements. |
| [Threading](../architecture/threading.md) | §1 threads table (render thread `Current` in M6 for device/present; snapshot/ring still M7); §7 shutdown; test plan status. |
| [Overview](../architecture/overview.md) | §5 topology (render thread `Current`; no handoff yet; offscreen loop), invariants, status header. |
| [Projects](../architecture/projects.md) | §1 Render status/description, pins, `AllowUnsafeBlocks`; §2 matrix (Render → Silk.NET.Vulkan; still no Runtime/SDL); §3 internal visibility unchanged; §4 folders. |
| [NativeAOT](../architecture/nativeaot.md) | §1 Render status; §6 Vulkan loader/suppression (D-M6-10); §7 AOT gate commands (windowed requires a GPU machine). |
| [Logging](../architecture/logging.md) | `Bork2d.Render` category becomes `Current` with the §6.12 events. |
| [API reference](../api/reference.md) | `WithVSync`/`WithValidation` status; `IEngine.Exit` offscreen behavior and offscreen `ITime`; new `Bork2dException` cases for Vulkan/device/feature/format failure; no new members. |
| [API guide](../api/guide.md) | Modes note: windowed/offscreen require Vulkan 1.3; offscreen runs until `Exit`; window is black until M8; headless needs no GPU. |
| [Contributing](../contributing.md) | Prerequisites (Vulkan 1.3 GPU for M6+), GPU test command, AOT gate note. |
| [docs/README.md](../README.md) | Documentation map entry for this spec; status header. |
| [Roadmap](../roadmap.md) | Progress log and M6 status only when the milestone lands (not in this spec-only change). |

Same-change rule: any contract, mode, failure, or status change ships with its owning doc in the
same work package. The docs link check runs after any heading/file change.

## 12. Work packages (proposed; owner approval required)

Sequential is the default; any parallel wave needs the AGENTS.md §3.1 dependency/file-overlap check
recorded in the milestone PR. Proposed split:

| WP | Branch | Scope | Acceptance |
| --- | --- | --- | --- |
| WP-1 | `m-6/wp-1` | Record `D-M6-01`…`D-M6-27` in the decision log, keep §16 as the answered register, update the roadmap status when implementation starts, and link this spec. Docs only. | Docs check; decisions consistent; no code. |
| WP-2 | `m-6/wp-2` | `Bork2d.Render` foundation: packages, unsafe flag, instance/validation bootstrap, device selection/features/logging (including the D-M6-08 descriptor set), pure policy functions + deterministic tests, dependency/docs updates. | Build/tests/format; dependency tests; no GPU needed. |
| WP-3 | `m-6/wp-3` | Windowed: SDL Vulkan surface seam, swapchain create/recreate, frames/fences/semaphores, clear pass, present, resize/minimize/fullscreen, counters; opt-in GPU tests. | Windowed clear/present; resize/fullscreen/minimize clean; validation zero errors; GPU tests pass locally. |
| WP-4 | `m-6/wp-4` | Offscreen target + clear/discard; offscreen loop migration (D-M6-07, `Exit`/`ITime`); `Setup`-before-readiness and first-tick gate (D-M6-06); renderer stop/fatal/control channels; Runtime composition, error precedence; deterministic host tests with the fake renderer; docs. | Offscreen loop exits on `Exit`; `Setup` does not wait; headless no Vulkan; error matrix; suite green. |
| WP-5 | `m-6/wp-5` | AOT/Pong gate (`--windowed --ticks`, `--offscreen --ticks` per D-M6-13), publish/run evidence, final docs, full gates, PR. | AOT zero warnings; gate runs exit 0; docs/tests/format; PR with evidence. |

Dependencies: WP-2 → WP-3/WP-4 → WP-5. WP-3 and WP-4 both touch `Bork2d.Render` renderer/frame
types and the Runtime host, so they are sequential unless the owner approves a file-level split.
WP-1 must precede WP-2. No branch starts from an ambiguous base.

## 13. Acceptance criteria

Reviewer-checkable, mapped to the roadmap exit:

1. **Build and static gates.** Debug and Release builds of `src/Bork2d/Bork2d.slnx` are zero-warning;
   `dotnet test` passes; `dotnet format --verify-no-changes` and `pwsh scripts/check-docs.ps1` pass;
   `git diff --check` clean.
2. **Dependency/public surface.** `Bork2d.Render` references only Engine + `Silk.NET.Vulkan*`; no SDL
   or Runtime reference; no new public types in Render/Runtime; dependency test and Projects doc
   agree.
3. **Windowed clear and readiness.** On a Vulkan 1.3 machine, a windowed run (Pong or a test host)
   creates the instance/surface on the OS thread and a device/swapchain on the render thread, runs
   `Setup` before readiness (D-M6-06), starts ticking only after readiness, presents a continuous
   black-cleared window, and logs the expected device/swapchain lines once.
4. **Windowed resilience.** A scripted resize sequence, fullscreen enter/leave, minimize/restore,
   and close/`Exit` under frame load complete without hang, deadlock, or leak; validation reports
   zero errors when the layer is available; the surface-loss recovery policy is covered by a test
   (a GPU-triggered loss is best-effort) and a second loss is fatal (D-M6-16); the renderer joins
   before surface/window destruction.
5. **Offscreen.** An offscreen run creates no surface/swapchain, runs `Setup` → `Tick`… → `Teardown`
   until `Exit` with `ITime` advancing, clears the fixed sRGB target, discards frames, and stops
   cleanly with validation-clean output when validation is enabled; the target extent matches
   `WithOffscreen(w, h)`.
6. **Headless isolation.** A headless run initializes neither SDL nor Vulkan (factory spy + no
   platform/renderer logs) and keeps the one-tick contract (including `Exit` being ignored); a
   GPU-less machine run is recorded when available.
7. **No pacing/backpressure.** No `Sleep`/spin/target rate is added; windowed's tick loop is
   unchanged; offscreen loops per D-M6-07 and is uncapped/GPU-fence paced (D-M6-12), with M7 owning
   pacing; no snapshot/ring types appear; `WithMaxDeltaTime` remains the only clamp.
8. **Failure behavior.** Missing loader/instance failure (before `Setup`) and renderer
   readiness/feature/format failures (after `Setup`, D-M6-06) each produce a `Bork2dException` with
   a clear message; a renderer failure after `Setup` attempts `Teardown` and zero ticks run
   (D-M6-14); a render-thread fatal after the first tick stops at the next boundary, runs
   `Teardown`, joins, and rethrows.
9. **AOT.** `dotnet publish examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Release -r win-x64` is
   zero-warning/zero-trim-diagnostic; published `--headless` exits 0 and provably makes no Vulkan
   call; published `--windowed --ticks 120` exits 0 on a GPU machine; published
   `--offscreen --ticks 120` exits 0 on the same machine (D-M6-13).
10. **Docs.** Every doc in §11 reflects the as-built behavior/status; the decision log contains the
    M6 decisions; status markers are honest.
11. **Tests.** Deterministic suite green in CI; opt-in GPU tests pass locally and are recorded in the
    PR with machine/driver/SDK details and zero validation errors when the layer is available.
12. **Process.** Work packages follow the AGENTS.md loop: one branch each, reviewed diff, wave order
    recorded, no direct `develop` commits, no auto-merge.

## 14. Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Silk.NET.Vulkan AOT/trim warnings or resolver failure | AOT gate fails; risky suppression | WP-2 proves publish early; use the narrow suppression per D-M6-10 and prove the publish/run; re-ask if it cannot be made clean. |
| `Setup` runs while the renderer initializes or has already failed (D-M6-06) | Game setup side effects occur even though the run aborts; longer startup | No `Tick` runs before readiness; a renderer failure is reported after `Setup`, attempts `Teardown`, and is covered by deterministic and GPU tests. |
| Validation layer absence on dev machines | Runs continue without validation; validation evidence is weaker | D-M6-20: log a warning and continue; run the validation gate on a machine with the SDK; document the prerequisite. |
| WSI lifetime bugs (surface/swapchain/instance order) | Crashes/hangs on resize/close; surface-loss recovery is extra moving parts | Strict ownership §6.2, join before destruction, GPU tests for resize/minimize/close, deterministic recovery-policy tests, validation. |
| Minimized/zero-extent busy loop or deadlock | CPU burn or hang | Wait on control/stop events, no polling; dedicated GPU test. |
| Semaphore/fence reuse across 2 frames | Validation errors/artifacts | Per-frame sync objects, fence-gated reuse, validation-clean stress test. |
| Offscreen GPU spin at full load (D-M6-01/D-M6-12, no pacing until M7) | Thermal/perf noise in tests | Short offscreen runs (`--offscreen --ticks 120`); pacing is M7; record the observed behavior. |
| New dependency expands publish/AOT surface | Gate regressions | Pins, dependency tests, publish inspection, no validation layer in publish. |
| No GPU in CI | Real-device regressions slip | Opt-in GPU gate run locally on every renderer PR; CI runner still M8. |
| Docs drift across five architecture docs | Review friction | §11 checklist, docs link check, same-change rule. |

## 15. Deferred / open in later milestones

- Snapshot ring, bounded run-ahead, tick/render coupling, pacing, two-frame handoff: M7.
- Cameras/projection, sprite pipeline, bindless pages, uploads, draws, sorting/clear semantics,
  offscreen/windowed parity, benchmark: M8.
- Public readback/capture, device override, device-loss recovery, transfer queue, multi-window,
  Linux/macOS, hot reload: M8/M10.
- The editor remains out of Stage 1.

## 16. Decision register (all answered)

All 22 questions are answered; the register is kept for traceability. Each row names its decision in
§3, and implementation details are left to the work packages.

### A. Frame drive, startup, and offscreen

| ID | Question | Answer |
| --- | --- | --- |
| Q1 | Startup readiness | **D-M6-06:** `Run()` starts the renderer thread and runs `Setup` immediately; the first `Tick` waits for readiness. A readiness failure is reported after `Setup`, attempts `Teardown`, and runs zero ticks. |
| Q2 | Offscreen M6 host | **D-M6-07:** the windowed-shaped loop runs until `IEngine.Exit()`; `ITime` advances; no window/input. The offscreen smoke uses `--ticks N` (D-M6-13). |
| Q3 | Offscreen pacing | **D-M6-12:** ticks are uncapped; render frames are paced only by GPU fences. No fixed cap or idle wait in M6. |

### B. Device and features

| ID | Question | Answer |
| --- | --- | --- |
| Q4 | Descriptor-indexing features | **D-M6-08:** the full six-feature bindless set is required and enabled; a missing feature rejects the device at startup. |
| Q5 | Frames in flight and image counts | **D-M6-11:** two frames in flight, swapchain `minImageCount + 1` (typically 3, clamped), two offscreen targets. |
| Q6 | Swapchain recreation and `SUBOPTIMAL` | **D-M6-15:** `vkDeviceWaitIdle` + new swapchain (old passed as `oldSwapchain`, then destroyed); `OUT_OF_DATE` recreates and retries without presenting; `SUBOPTIMAL` presents first, then recreates next iteration. |
| Q7 | Device loss and surface loss | **D-M6-16:** surface loss gets exactly one recovery attempt (new surface via the OS thread + new swapchain on the same device); a second loss or any device loss is fatal. |

### C. Presentation and platform

| ID | Question | Answer |
| --- | --- | --- |
| Q8 | Present mode for `WithVSync(false)` | **D-M6-17:** MAILBOX, then IMMEDIATE, then FIFO; `WithVSync(true)` is FIFO. |
| Q9 | Instance/surface lifetime | **D-M6-09 (owner-delegated):** the OS/window thread creates and destroys both, after the renderer joins and its device is destroyed: surface, instance, window (except the single D-M6-16 surface replacement). |
| Q10 | Resize/minimize signalling | **D-M6-18:** the OS thread posts coalesced control messages; the render thread recreates or blocks on its wake event. No polling or spinning. |
| Q11 | Channel shape | **D-M6-19:** one SPSC control queue (OS→render), an atomic stop flag, and a one-shot fatal slot; plus the single surface-loss request (D-M6-16). |

### D. Validation

| ID | Question | Answer |
| --- | --- | --- |
| Q12 | Missing validation layer | **D-M6-20:** log a warning and continue without validation; `WithValidation(true)` is honored as a request in every build but never fails startup because the layer is absent. |
| Q13 | Validation message policy | **D-M6-21:** with validation enabled, errors fail the run and warnings are logged/counted (fatal in `[GpuFact]` tests); with validation off, messages are logged only. |
| Q14 | Debug labels | **D-M6-22:** label the instance, device, swapchain images, command buffers, and the clear pass; no per-frame or per-draw label churn. |

### E. Evidence and public surface

| ID | Question | Answer |
| --- | --- | --- |
| Q15 | Clear evidence | **D-M6-23:** counters/logs plus a manual visual check; no public or internal readback in M6. |
| Q16 | Public surface | **D-M6-24:** no public types or members are added; only behavior changes and new `Bork2dException` cases. |
| Q17 | Pong/AOT gate flags | **D-M6-13:** `Pong.Desktop` gains `--offscreen --ticks N`; the gate runs `--headless`, `--windowed --ticks 120`, and `--offscreen --ticks 120`. |

### F. Implementation boundaries

| ID | Question | Answer |
| --- | --- | --- |
| Q18 | Renderer seam placement | **D-M6-25:** the internal `IRenderer`/factory lives in `Bork2d.Render`; Runtime passes opaque instance/surface handles. |
| Q19 | AOT Vulkan resolver | **D-M6-10:** a narrow `NoWarn` suppression is acceptable if the AOT publish is proven zero-warning and the published app runs; otherwise stop and re-ask. |
| Q20 | Offscreen format preference | **D-M6-26:** `R8G8B8A8_SRGB` first, then `B8G8R8A8_SRGB`; no sRGB format at all follows D-M6-04. |
| Q21 | Renderer cleanup failure | **D-M6-27:** log and keep the run's existing outcome; no exception after a successful run. |

### G. Startup failure details

| ID | Question | Answer |
| --- | --- | --- |
| Q22 | Renderer failure around `Setup` | **D-M6-14:** `Setup` finishes, `Run()` attempts `Teardown`, rethrows `Bork2dException`, runs zero ticks; if `Setup` also failed, the `Setup` exception wins. |

## 17. Related documents

- [Roadmap](../roadmap.md#5-milestone-detail) — M6 definition, sequence, decisions, status.
- [Rendering](../architecture/rendering.md) — Vulkan target, frame structure, presentation,
  validation, current status.
- [Threading](../architecture/threading.md) — thread ownership, shutdown, handoff (M7).
- [Runtime lifecycle](../architecture/runtime-lifecycle.md) — modes, builder, startup/shutdown,
  error precedence.
- [Overview](../architecture/overview.md) — topology and invariants.
- [Projects](../architecture/projects.md) — dependency matrix, public surface, source layout.
- [NativeAOT](../architecture/nativeaot.md) — AOT/trim and native-library constraints.
- [Logging](../architecture/logging.md) — categories and default configuration.
- [API reference](../api/reference.md#12-runtime-surface) — builder/app contract.
- [Decision log](../decisions.md) — accepted decisions; M6 entries are added by WP-1.
- [Pong example spec](pong-example.md#12-as-built-evidence) — the AOT gate and its evidence format.
- [Contributing](../contributing.md) — gates, tests, docs checklist.
