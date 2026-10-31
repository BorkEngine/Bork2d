# Rendering

> **Status:** target design for the renderer. Window creation, input, time, and the tick loops are
> `Current` in `Bork2d.Runtime` (M2/M6/M7); the `Bork2d.Render` foundation, the windowed
> swapchain/clear/present path, and offscreen targets plus the windowed/offscreen host wiring are
> `Current` in M6 (merged 2026-10-31, PR #11). The internal value-only snapshot model, the capture
> pass, and the three-slot ring state machine are `Current` (M7); renderer consumption (one
> presented/discarded frame per consumed snapshot, the slot held to the GPU fence, the suspended
> minimized drain, and the empty per-frame instance resources) is `Current` (M7), and the host
> acquire/capture/publish wiring and the pacing loops are `Current` (M7), so a windowed/offscreen
> run publishes and consumes snapshots and draws one black clear per snapshot. Sprite rendering is
> M8. Everything below about pixels is `Planned` unless [Current status](#11-current-status) says
> otherwise.
## 1. Scope

The renderer turns one immutable [frame snapshot](threading.md#3-frame-snapshot) into pixels:

- one instanced draw per **non-empty** camera;
- sprites are quads resolved from sprite assets (UVs, pivot, topology, pixels-per-unit scale);
- a camera's visible quads share one pipeline, one instance buffer, and one bindless texture set;
- Vulkan 1.3 dynamic rendering: no render pass/framebuffer for the swapchain, no depth buffer;
- explicit ordering by layer and `ZIndex`; windowed and offscreen modes (headless has none).

It knows no game types, layer enum, TOML, or source tree; it consumes flattened snapshot data and
the loaded public asset values (`SpriteAsset`/`TextureDescriptor`).
## 2. Backend stack and device requirements

| Concern | Choice |
| --- | --- |
| Windowing and surface | SDL2 via `Silk.NET.SDL` (windowed only) |
| Vulkan bindings and path | `Silk.NET.Vulkan`, Vulkan 1.3, dynamic rendering |
| Texture access | Descriptor indexing / bindless array |
| Geometry | Instanced shader-generated four-corner quads from sprite assets |
| Shaders | Offline GLSL → SPIR-V; no runtime compilation |

Fail early unless the device provides Vulkan 1.3, `dynamicRendering`, `synchronization2`;
descriptor indexing (`runtimeDescriptorArray`, `shaderSampledImageArrayNonUniformIndexing`,
`descriptorBindingPartiallyBound`, `descriptorBindingSampledImageUpdateAfterBind`,
`descriptorBindingUpdateUnusedWhilePending`, `descriptorBindingVariableDescriptorCount`); a graphics
queue family (present windowed); and an sRGB swapchain or offscreen format.

One queue family covers graphics and present; uploads use the graphics queue on the render thread,
and a transfer queue is later. Prefer a discrete GPU, then the first suitable device (override is
future work); log device, API version, driver, and limits, and validate page capacity against
`maxPerStageDescriptorUpdateAfterBindSampledImages`. Offscreen creates no surface; headless nothing.
## 3. Frame structure
### 3.1 Windowed

1. Wait for a signal and a free frame slot (fence); take the newest `Ready` snapshot, resolve
   sprites, sort/filter, and copy instances; hold the slot until the fence signals (run-ahead bound).
2. Acquire a swapchain image; wait on image-available; reset the command buffer, then transition the
   image to `COLOR_ATTACHMENT_OPTIMAL` and begin **one** dynamic rendering pass: swapchain view,
   `loadOp = CLEAR` to opaque black, no depth, `storeOp = STORE`.
3. Per camera in ascending `ZIndex`: set viewport/scissor; `Clear.Color` clears it; `Clear.Depth`
   warns once and no-ops; skip the draw when nothing is visible; otherwise bind the frame descriptor
   set, push the view-projection, and `vkCmdDraw(4, instanceCount, 0, firstInstance)`.
4. End, transition to `PRESENT_SRC_KHR`, submit, present; the fence frees the snapshot slot,
   command buffer, and instance buffer.

**M6 was the shape without a snapshot.** The render thread waits the frame fence, acquires an
image, transitions it from `UNDEFINED` to `COLOR_ATTACHMENT_OPTIMAL`, begins one dynamic-rendering
pass with `loadOp = CLEAR` to opaque black, ends it, transitions to `PRESENT_SRC_KHR`, submits with
synchronization 2, and presents. **M7 implements step 1's renderer half and the frame
coupling:** the clear pass is unchanged, but the render thread records exactly one frame per
consumed snapshot, holds the snapshot slot until that frame's fence, releases the previous frame's
slot when the frame index is reused, and waits (never re-presents) when no `Ready` snapshot exists.
The Runtime host publishes one snapshot per completed tick (M7), so a windowed run presents one
black clear per snapshot.
Step 3 (cameras, draws, instance filling) is M8; the frame's instance SSBO and descriptor set
exist empty (Q10=J1). The Runtime host starts the renderer before `Setup` (D-M6-06).
### 3.2 Offscreen

Same clear shape as windowed without surface/swapchain/present/acquire/semaphores: two permanent
sRGB color images with one device-local allocation each, created once for the fixed
`WithOffscreen(w, h)` extent. Each image is transitioned once from `UNDEFINED` to
`COLOR_ATTACHMENT_OPTIMAL` and stays there; two frames in flight alternate the images, each gated by
its own fence. **M7:** every frame is recorded for exactly one consumed snapshot — the render
thread waits the frame fence, releases that frame's previous snapshot slot, takes the newest
`Ready`, records one dynamic-rendering pass with `loadOp = CLEAR` to opaque black and
`storeOp = STORE`, submits with synchronization 2, attaches the slot, and counts a discarded frame.
With no `Ready` snapshot it waits instead of discarding; there is no minimized path. There is no
resize, present, readback, or host-visible memory (D-M6-23); pacing is one frame per consumed
snapshot (D-M7-07 revises D-M6-12). The host publishes one snapshot per completed tick. **`Current`
for the M6 clear and the M7 consumption.**

**Headless and draw count.** `WithHeadless` never enters this document. One draw per non-empty
camera: an empty camera clears but does not draw, the whole framebuffer is one pass, cameras are
viewport/scissor regions, and a debug counter enforces at most `C` draws for `C` cameras.
## 4. Snapshot to instances

The render thread owns this work; the tick thread supplies immutable snapshots only. The M7
snapshot is already value-only: each `SpriteSnapshot` carries the loaded `SpriteAsset?`, the
`(Layer, ZIndex)` sort keys, the world matrix, the tint, and the `FlipX`/`FlipY` flags (Q2=B1).
Everything that derives quad size, pivot, UVs, and a texture index from the asset, and everything
that draws, is M8.

1. Take the immutable snapshot of loaded sprite assets.
2. Sort `SpriteSnapshot`s once by `(Layer, ZIndex)` ascending; all cameras share the order, filtering
   preserves it, and ties are unspecified.
3. Per camera: filter `(CullingMask & Layer) != 0`; skip a null `SpriteAsset`; skip the invalid
   layer values that M7 captured as-is (`0`/multi-bit, Q3=C2) with the diagnostic; resolve
   `SpriteAsset.Texture` (`AssetRef<TextureDescriptor>`) and `TextureDescriptor.Payload`
   (`DataRef`) to loaded values/GPU pages; skip until every part and the GPU page are ready; for
   `Quad`, append one instance to the camera's contiguous range.

Instances differ per camera because view-projection and viewport differ. Instance fields:

| Field | Meaning |
| --- | --- |
| `Transform` | 2×3 world transform from the snapshot (parent hierarchy included). |
| `Size` | World-unit quad size: `(uvSpan × textureSize) / scale`. |
| `Pivot` | Normalized pivot, top-left origin. |
| `Uvs[4]` | Normalized UVs, top-left origin; TL/TR/BR/BL order. |
| `Tint` | Linear RGBA multiplied with the sampled texel. |
| `TextureIndex` | Index into the bindless texture page array. |

There is no `Vertices[4]` and no vertex buffer. The vertex shader derives the corners:

```
corners  = { (0,0), (1,0), (1,1), (0,1) }      // TL, TR, BR, BL
c        = corners[gl_VertexIndex]
local    = ((c.x - pivot.x) * size.x, (pivot.y - c.y) * size.y)
position = viewProj * vec4(transform * local, 0, 1)
```

The fragment shader samples `textures[TextureIndex]` and multiplies `Tint`. The asset also carries
`indices` (`ushort[]?`): null for `Quad`, non-null for `TriangleStrip`/`Triangles` indexing the
generated corners. Pack creation preserves those assets; field validation belongs to the M4
baker/editor. v1 draws `Quad` only; the indexed path is M8 work and is never approximated as a quad.
## 5. One-draw contract

One descriptor set per frame in flight:

| Binding | Type | Contents |
| --- | --- | --- |
| 0 | storage buffer | Instance records for the frame. |
| 1 | combined image sampler array | Bindless texture pages, updated after bind. |

**M7 scope (Q10=J1):** M7 creates the frame descriptor set with **binding 0 only** (instance
storage buffer, allocated empty, never updated, bound, or drawn) and a provisional small instance
buffer per GPU frame; binding 1 and the bindless page capacity are deliberately M8 work.

Camera data travels in a 64-byte push constant (view-projection); no per-camera descriptor set.
One graphics pipeline: dynamic rendering, one sRGB attachment, no depth; no vertex input (geometry
from the instance SSBO and `gl_VertexIndex`); triangle strip, 4 vertices/instance; dynamic
viewport/scissor; culling and depth test/write disabled; straight alpha `src*a + dst*(1-a)`.

```c
vkCmdDraw(cmd, 4, instanceCount, 0, firstInstance);
```

`firstInstance` selects the camera's range in the frame instance buffer; `gl_InstanceIndex` addresses
the same record; there is no index buffer. Shaders are GLSL 450/460 compiled offline to SPIR-V
(compiler is M8 work): `sprite.vert` fetches instances and generates corners, `sprite.frag` samples and
tints. `VkPipelineRenderingCreateInfo` carries the color format and no depth/stencil format.
## 6. Texture pages and bindless textures

- A **texture page** is one GPU texture described by a MessagePack descriptor (`textures/<name>`)
  and a raw payload (`textures/<name>-raw`).
- The bindless array holds combined image samplers, one per resident descriptor; `TextureIndex`
  selects the page. The shader sees a runtime-sized, partially bound `uniform sampler2D textures[]`;
  only resident pages have valid descriptors.
- Updates use `VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER` with `UPDATE_AFTER_BIND` and
  `PARTIALLY_BOUND`, so pages can be added or replaced without recreating the set; a replaced image
  stays alive until referencing frames complete.
- Capacity is a fixed, configurable page count (provisional 4096), validated against device limits;
  overflow is a configuration/build error, never a silent extra draw. Sampler state is per
  descriptor (`nearest`/`linear`, `clamp`/`repeat`); RGBA8 sRGB now, KTX2 later, one page per source.
## 7. Coordinates, camera, and projection
### 7.1 World space

+X right, +Y up. Rotation is degrees, CCW around +Z, normalized to `[0, 360)` on assignment;
internal matrix/projection math uses radians. Negative scale mirrors. A world unit is abstract:
source pixels become units by dividing by the sprite's pixels-per-unit `scale`.
### 7.2 Camera

`ICamera<TLayer>` extends `ITransform`; its world transform is the inverse view:
`view = inverse(cameraWorldTransform)`. Camera scale is not zoom in v1; `Size` is, and non-unit
camera scale is unsupported. `Size` is the **half visible height**:

```
visibleHeight = 2 * Size
visibleWidth  = visibleHeight * (viewportPixelWidth / viewportPixelHeight)
```

The viewport is normalized `(x, y, width, height)` with a top-left origin, scaled per component by
the framebuffer size. Full-screen default `(0, 0, 1, 1)`; split screen is `(0,0,0.5,1)` and
`(0.5,0,0.5,1)`. Zero-width/height viewports skip with a debug warning (M8).
### 7.3 Projection and defaults

With view-space `(xv, yv)`, half-height `s = Size`, and aspect `a`:

```
xNdc =  xv / (s * a)
yNdc = -yv / s        // Vulkan NDC is Y-down; world/API stay Y-up
zNdc = 0
```

The negation is the only place the Y-up API meets Vulkan clip space. `ScreenToWorld`/`WorldToScreen`
are `Planned` (not declared in `Bork2d.Engine` yet); camera and sprite property defaults are owned by
[API reference defaults](../api/reference.md#13-defaults) and applied in M8.
## 8. Sorting, layers, and clears
### 8.1 Sort order

Per snapshot: `Layer` ascending, then `ZIndex` ascending; higher values draw later/on top. There is
no entity ID and no stable tie-breaker, so equal `(Layer, ZIndex)` is **unspecified**; use distinct
`ZIndex` values when order matters.

Layers are flags, but a renderable carries exactly one bit: `0` and multi-bit values are invalid and
skipped with a diagnostic; named values are powers of two within 32 bits. A camera mask may combine
bits (`0` sees nothing); filter is `(mask & layer) != 0`. v1 culls by layer only; off-viewport
sprites are still instanced and clipped by the GPU, and future frustum culling must not change
logical visibility.

**Clearing and multiple cameras.** The frame starts with a safety clear to opaque black.
`Clear.Color` clears the camera viewport to `ClearColor`; `Clear.None` preserves earlier output
(safety black if first); `Clear.Depth` is reserved, logs once, and does nothing. Cameras apply in
ascending `ZIndex` (default `0`); ties are unspecified. A later `Clear.Color` wipes its viewport and
`Clear.None` overlays (the HUD pattern). Camera order is D-026, applied in M8.
## 9. Synchronization and presentation

- Windowed: 3 swapchain images (`minImageCount + 1`, clamped) and 2 GPU frames in flight; offscreen:
  2 permanent targets and 2 frames in flight, no present. Each windowed frame owns a command
  buffer/pool, a fence, and an image-available semaphore, and each swapchain image owns its
  render-finished semaphore (M6 WP-3). Each offscreen frame owns a command buffer/pool and a fence
  (M6 WP-4). M7 adds each frame's empty instance SSBO and frame descriptor set
  (binding 0 only, Q10=J1); M8 fills them and adds the bindless binding/capacity.
- **M7 handoff status.** The internal value-only snapshot model, the capture pass, and
  the three-slot ring state machine are `Current` (M7). The renderer consumes snapshots
  (M7): it takes the newest `Ready` for a free GPU frame, records exactly **one frame per
  consumed snapshot**, holds the snapshot slot `Rendering` until that frame's fence is observed,
  releases it when the frame index is reused, and releases every held slot after
  `vkDeviceWaitIdle` (recreation/surface recovery/shutdown). The combined
  `SnapshotControlWait` wakes on a snapshot, a control message, a stop, or a fatal without polling;
  `SnapshotConsumer` tracks the slot-to-frame mapping for the two frames in flight. While
  minimized/zero extent the suspended consumer frees the newest `Ready` without GPU work (the slot
  never enters `Rendering`), counts a suspended consumption, and waits for control or a snapshot;
  restore resumes one frame per snapshot. The host acquire/capture/publish wiring and the pacing
  loops are `Current` (M7): every completed windowed/offscreen tick publishes one snapshot, so the
  renderer records frames. M7 adds no draw.
- **Snapshot slot held to the fence.** A slot is held until the GPU frame that consumed it signals
  its fence, not just until data is copied: this bounds run-ahead to the ring, and the GPU never
  dereferences snapshot memory. `Join` drains every non-`Writing` slot, so every slot ends `Free`
  even after a fatal mid-tick publish.
- Wait on the frame fence before reusing that frame's command buffer/fence/image-available
  semaphore. Windowed: `imageAvailable` waits at `COLOR_ATTACHMENT_OUTPUT`; `renderFinished`
  (per swapchain image) is signalled after submission and awaited by present. Layouts
  `UNDEFINED → COLOR_ATTACHMENT_OPTIMAL` then `→ PRESENT_SRC_KHR`; offscreen stays in
  color-attachment layout. If the image acquire returns `OUT_OF_DATE`/`SURFACE_LOST` after a
  snapshot was taken, that slot is released immediately: it never becomes a GPU frame.
- Uploads are recorded on the render thread before drawing in v1 (M8). `vkDeviceWaitIdle` is only
  for recreation/shutdown; `VK_KHR_synchronization2` is the only submission path.
- No fixed rate or limiter: one frame per consumed snapshot; with no `Ready` snapshot the renderer
  waits and never repeats or re-presents. See
  [Threading](threading.md#4-triple-buffer-handoff).

**Windowed presentation.** Preferred formats `B8G8R8A8_SRGB` then `R8G8B8A8_SRGB`, color space
`VK_COLOR_SPACE_SRGB_NONLINEAR_KHR`. `FIFO` with vsync; `MAILBOX`/`IMMEDIATE` without vsync when
supported, `FIFO` fallback. Extent from the SDL drawable size; zero extent (minimized) skips
acquire/present, drains the newest snapshot without GPU work, and waits on the combined
snapshot/control wake. `OUT_OF_DATE` recreates and retries without presenting; `SUBOPTIMAL`
presents the current frame first and recreates on the next iteration (D-M6-15); both use
`vkDeviceWaitIdle`, release the held snapshot slots, and pass the old handle as `oldSwapchain`.
Device loss is fatal; surface loss gets exactly one replacement-surface recovery (D-M6-16).
Presentation happens once per consumed snapshot (Q7=G1, D-M7-07), not continuously. **`Current`
for the M6 swapchain/clear/present and the M7 renderer consumption**; the Runtime host publishes
one snapshot per completed tick (M7).

**Offscreen.** No surface, swapchain, or present; fixed `WithOffscreen(w, h)` extent; two permanent
sRGB color images (one device allocation each) cleared and discarded with no public readback; one
discarded frame per consumed snapshot (Q7=G1). **`Current` for the M6 clear and the M7
consumption**; the host publishes one snapshot per completed tick (M7).
## 10. Debugging, validation, and performance

- Validation layers in debug and with `WithValidation(true)`, plus a debug messenger; errors are
  fatal in debug/test; `VK_EXT_debug_utils` names the instance, device, swapchain/offscreen images,
  image views, command buffers, the clear pass, and each frame's instance buffer/descriptor set.
  **`Current` in M6 and M7** for both paths; no per-frame or per-draw label churn.
- Counters: draws, instances, skipped sprites, resident pages, snapshot sequence, render CPU, present
  mode (M8). **`Current`:** frames presented (windowed), frames discarded (offscreen), swapchain
  recreations, validation errors/warnings, last frame number, last present result (M6), plus the
  M7 handoff counters — snapshots published/consumed/superseded, backpressure waits, max live
  snapshots, and suspended consumptions (Q8) — logged once at start/stop; the offscreen start line
  also logs extent/format/images/memory bytes, and both start lines log handoff on, ring depth 3,
  one frame per snapshot, and instance resources on.
- Failures: no suitable device/feature or shader failure is fatal; `OUT_OF_DATE`/`SUBOPTIMAL`
  recreate; device loss is fatal; surface loss recovers once; invalid sprite/layer is skipped with a
  diagnostic; bindless-capacity overflow is a configuration error (provisional 4096 pages);
  validation errors fail debug/test. A renderer fatal stops the ring so a blocked producer wakes
  (Q9=I1); renderer cleanup releases every held snapshot slot and `Join` drains the ring. A missing
  loader/instance/surface fails windowed `platform.Start()` before `Setup`; a renderer
  device/feature/format/target failure is reported after `Setup` (D-M6-06). **`Current` in M6** for
  both paths plus the M7 fatal/stop wake; shader/draw failures are M8.

Rules: one `vkCmdDraw` per non-empty camera; no per-sprite descriptors or indirect draws; persistent
instance buffers and zero steady-state allocations; sort once per snapshot, filter per camera, cache
resolution; no GPU→CPU readbacks; update-after-bind pages; never stall the tick on GPU work (ring
backpressure only); no artificial cap or sleep.

Provisional targets (validated in M8, not commitments):

| Metric | Target |
| --- | --- |
| Scene | 50,000 sprites, 1 camera, 1920×1080 |
| Presentation | Follow the display (test machine 165 Hz); no artificial cap |
| Draw calls | 1 for the scene, plus clears |
| Render-thread CPU | instance build + submit under ~2 ms |
| Steady-state allocations | 0 bytes/frame in hot paths |
| Texture pages | at least 256 resident |
| Validation | zero errors with layers |

165 Hz is a measurement goal, not a cap: record the measured ceiling if it cannot be sustained.
## 11. Current status

| Area | Status |
| --- | --- |
| SDL window creation, input, time, and the windowed/offscreen tick loops | `Current` M2/M6/M7 (`Bork2d.Runtime`). The windowed platform creates the SDL window, Vulkan instance, and surface on the OS thread; the host starts the renderer before `Setup` and waits for readiness only before the first tick; ticks are ring-backpressured. |
| `Bork2d.Render` | `Current` M6/M7: internal seam/control channel, Vulkan packages, instance/validation/debug-utils bootstrap, device selection, pure policies, the windowed renderer, the offscreen target/renderer, the handoff consumer/combined wait, and the empty per-frame instance resources. Sprites/uploads are M8. |
| Vulkan instance/validation bootstrap and device/queue/feature logging | `Current` M6 |
| Windowed swapchain, acquire/present, dynamic-rendering clear, resize/minimize/fullscreen recreation, surface-loss recovery, counters | `Current` M6, host-wired in M6/M7 |
| Offscreen targets, dynamic-rendering clear, discard, and the offscreen loop | `Current` M6 |
| Snapshot model/capture and the internal three-slot ring state machine | `Current` M7 (internal; no draw) |
| Renderer snapshot consumption (one presented/discarded frame per consumed snapshot), fence-held slot lifetime, suspended minimized drain, combined wait | `Current` M7 |
| Host acquire/capture/publish wiring, backpressure, pacing | `Current` M7 |
| Empty per-frame instance SSBO and frame descriptor set (binding 0, Q10=J1) | `Current` M7 (allocated, unused) |
| Sprite pipeline, bindless binding/pages, shader build, asset upload, instance writes, flips at draw time | `Planned` M8 |
| Headless | `Current` for the asset/tick path with no SDL/Vulkan/renderer; the renderer is intentionally absent because headless has no GPU work |

## 12. Related documents

- [Threading](threading.md) — snapshot ring, bounded run-ahead, fences.
- [Assets and tooling](assets-and-tooling.md#8-texture-descriptors) — descriptors, sprite assets.
- [Runtime lifecycle](runtime-lifecycle.md#2-modes) — windowed/offscreen/headless modes.
- [API guide](../api/guide.md#5-cameras), [reference](../api/reference.md#13-defaults) — cameras.
- [Roadmap](../roadmap.md), [decision log](../decisions.md) (D-009).
