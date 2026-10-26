# Rendering

> **Status:** target design for the renderer. Window creation, input, time, and the windowed host
> loop are `Current` in `Bork2d.Runtime` (M2); `Bork2d.Render` is still empty: Vulkan bring-up is
> roadmap **M6**, frame handoff **M7**, sprite rendering **M8**. Everything below about pixels is
> `Planned` unless [Current status](#11-current-status) says otherwise.
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
### 3.2 Offscreen

Same as windowed without surface/swapchain/present; one permanent color image per slot (or layout
transitions); fixed `WithOffscreen(w, h)` extent with no resize; frames discarded in v1; fence
pacing unchanged.

**Headless and draw count.** `WithHeadless` never enters this document. One draw per non-empty
camera: an empty camera clears but does not draw, the whole framebuffer is one pass, cameras are
viewport/scissor regions, and a debug counter enforces at most `C` draws for `C` cameras.
## 4. Snapshot to instances

The render thread owns this work; the tick thread supplies immutable snapshots only.

1. Take the immutable snapshot of loaded sprite assets.
2. Sort `SpriteSnapshot`s once by `(Layer, ZIndex)` ascending; all cameras share the order, filtering
   preserves it, and ties are unspecified.
3. Per camera: filter `(CullingMask & Layer) != 0`; skip a null `SpriteAsset`; resolve
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
  and a raw payload (`textures/raw/<name>`).
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
are `Planned` (not declared in `Bork2d.Api` yet); camera and sprite property defaults are owned by
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

- Windowed: at least 3 swapchain images and 2 GPU frames in flight; offscreen: at least 2 targets and
  2 frames, no present. Each slot owns a command buffer, semaphores (or offscreen equivalents),
  fence, persistently mapped instance buffer, and descriptor set.
- A **snapshot slot is held until the GPU frame fence**, not just until instances are copied: this
  bounds run-ahead to the ring, and the GPU never dereferences snapshot memory.
- Wait on the frame fence before reusing that slot's buffers. Windowed: `imageAvailable` at
  `COLOR_ATTACHMENT_OUTPUT`; `renderFinished` signalled after submission and awaited by present.
  Layouts `UNDEFINED → COLOR_ATTACHMENT_OPTIMAL` then `→ PRESENT_SRC_KHR`; offscreen stays in
  color-attachment layout.
- Uploads are recorded on the render thread before drawing in v1. `vkDeviceWaitIdle` is only for
  recreation/shutdown; `VK_KHR_synchronization2` is the only submission path.
- No fixed rate or limiter: windowed follows presentation/GPU completion, offscreen GPU completion.
  See [Threading](threading.md#4-triple-buffer-handoff).

**Windowed presentation.** Preferred formats `B8G8R8A8_SRGB` then `R8G8B8A8_SRGB`, color space
`VK_COLOR_SPACE_SRGB_NONLINEAR_KHR`. `FIFO` with vsync; `MAILBOX`/`IMMEDIATE` without vsync when
supported, `FIFO` fallback. Extent from the SDL framebuffer size; zero extent (minimized) skips
acquire/present. `OUT_OF_DATE`/`SUBOPTIMAL` recreate the swapchain (device idle, coalesced resize);
present on a new snapshot, not every refresh. Device loss is fatal.

**Offscreen.** No surface, swapchain, or present; fixed `WithOffscreen(w, h)` extent; one or two
color images; same pass and draws; frames discarded with no public readback; pacing by GPU
completion.
## 10. Debugging, validation, and performance

- Validation layers in debug and with `WithValidation(true)`, plus a debug messenger; errors are
  fatal in debug/test; `VK_EXT_debug_utils` labels frames, cameras, passes, and pages.
- Counters: draws, instances, skipped sprites, resident pages, snapshot sequence, render CPU, present mode.
- Failures: no suitable device/feature or shader failure is fatal; `OUT_OF_DATE`/`SUBOPTIMAL`
  recreate; device loss is fatal; invalid sprite/layer is skipped with a diagnostic;
  bindless-capacity overflow is a configuration error (provisional 4096 pages); validation errors fail debug/test.

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
| SDL window creation, input, time, and the windowed loop | `Current` M2 (`Bork2d.Runtime`; no Vulkan, surface, or render thread) |
| `Bork2d.Render` | `Empty`: project scaffold |
| Vulkan instance/device/swapchain, offscreen targets, dynamic rendering | `Planned` M6 |
| Snapshot consumption, frame handoff, bounded run-ahead | `Planned` M7 |
| Sprite pipeline, bindless pages, shader build, asset upload | `Planned` M8 |
| Headless | `Current` for the asset/tick path with no SDL/Vulkan; the renderer is intentionally absent because headless has no GPU work |

## 12. Related documents

- [Threading](threading.md) — snapshot ring, bounded run-ahead, fences.
- [Assets and tooling](assets-and-tooling.md#8-texture-descriptors) — descriptors, sprite assets.
- [Runtime lifecycle](runtime-lifecycle.md#2-modes) — windowed/offscreen/headless modes.
- [API guide](../api/guide.md#5-cameras), [reference](../api/reference.md#13-defaults) — cameras.
- [Roadmap](../roadmap.md), [decision log](../decisions.md) (D-009).
