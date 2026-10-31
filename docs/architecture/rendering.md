# Rendering

> **Status:** target design for the renderer. Window creation, input, time, and the tick loops are
> `Current` in `Bork2d.Runtime` (M2/M6/M7); the `Bork2d.Render` foundation, the windowed
> swapchain/clear/present path, and offscreen targets plus the windowed/offscreen host wiring are
> `Current` in M6 (merged 2026-10-31, PR #11). M7 is merged into `develop` (2026-10-31, PR #12,
> merge `5de5ac3`). The internal value-only snapshot model, the capture
> pass, and the three-slot ring state machine are `Current` (M7); renderer consumption (one
> presented/discarded frame per consumed snapshot, the slot held to the GPU fence, the suspended
> minimized drain, and the empty per-frame instance resources) is `Current` (M7), and the host
> acquire/capture/publish wiring and the pacing loops are `Current` (M7), so a windowed/offscreen
> run publishes and consumes snapshots and clears the target once per snapshot.
> **M8 (sprite renderer) is in progress:** the M8 spec
> ([`docs/specs/m8-sprite-renderer.md`](../specs/m8-sprite-renderer.md)) is owner-approved. WP-1
> reconciled the owning docs; WP-2 added the build-time shader tool/pipeline, the shared camera
> projection/rounding math, the public camera helpers, and the pure sprite planning/quad/96-byte
> instance layout. Texture pages/uploads, per-camera passes, draws, `RenderStats`, and
> `CaptureFrameAsync` are `Planned` (WP-3/WP-4). Everything below about sprite pixels is `Planned`
> unless [Current status](#11-current-status) says otherwise.
## 1. Scope

The renderer turns one immutable [frame snapshot](threading.md#3-frame-snapshot) into pixels:

- one instanced draw per **non-empty** camera, inside **one dynamic-rendering pass per camera**;
- sprites are quads resolved from sprite assets (UVs, pivot, topology, pixels-per-unit scale);
- a camera's visible quads share one pipeline, one instance buffer, and one bindless texture set;
- sprite textures resolve as `SpriteAsset.Texture` → `TextureDescriptor` → `DataRef` payload → a
  resident GPU page through the app's `IAssets` seam (M8 target);
- Vulkan 1.3 dynamic rendering: no render pass/framebuffer for the swapchain, no depth buffer;
- explicit ordering by layer and `ZIndex`; windowed and offscreen modes (headless has none).

It knows no game types, layer enum, TOML, or source tree; it consumes flattened snapshot data and
the loaded public asset values (`SpriteAsset`/`TextureDescriptor`), and loads descriptor/payload
pairs through `IAssets` on the render thread (M8 target).
## 2. Backend stack and device requirements

| Concern | Choice |
| --- | --- |
| Windowing and surface | SDL2 via `Silk.NET.SDL` (windowed only) |
| Vulkan bindings and path | `Silk.NET.Vulkan`, Vulkan 1.3, dynamic rendering |
| Texture access | Descriptor indexing / bindless array |
| Geometry | Instanced shader-generated four-corner quads from sprite assets |
| Instance storage | One host-visible instance buffer per GPU frame, 96-byte std430 records, grown at a free fence |
| Shaders | Checked-in GLSL compiled at build time by the `Bork2d.ShaderCompiler` tool (Silk.NET.Shaderc 2.23.0) into generated SPIR-V linked into `Bork2d.Render`; no runtime compilation and no Vulkan SDK requirement |

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
   sprites, sort/filter, and build instances; hold the slot until the fence signals (run-ahead bound).
2. Acquire a swapchain image; wait on image-available; reset the command buffer, then transition the
   image to `COLOR_ATTACHMENT_OPTIMAL`. There is **no frame-level clear pass** (M8 target): record
   one dynamic-rendering pass per camera in ascending `ZIndex`, each over that camera's rounded
   pixel viewport, with `loadOp = CLEAR` for `Clear.Color` and `loadOp = LOAD` for `Clear.None`/
   `Clear.Depth`.
3. Per camera: set viewport/scissor; if it has at least one instance, bind the frame descriptor set,
   push the view-projection, and `vkCmdDraw(4, instanceCount, 0, firstInstance)`. An empty
   `Clear.Color` camera keeps its clear-only pass; an empty `Clear.None`/`Clear.Depth` camera records
   nothing (§8).
4. End, transition to `PRESENT_SRC_KHR`, submit, present; the fence frees the snapshot slot,
   command buffer, and instance buffer. If a capture is pending, the composited image is copied to a
   host-visible buffer after the last pass and before present (M8 target).

**M6 was the shape without a snapshot.** The render thread waits the frame fence, acquires an
image, transitions it from `UNDEFINED` to `COLOR_ATTACHMENT_OPTIMAL`, begins one dynamic-rendering
pass with `loadOp = CLEAR` to opaque black, ends it, transitions to `PRESENT_SRC_KHR`, submits with
synchronization 2, and presents. **M7 implements step 1's renderer half and the frame
coupling:** the clear pass is unchanged, but the render thread records exactly one frame per
consumed snapshot, holds the snapshot slot until that frame's fence, releases the previous frame's
slot when the frame index is reused, and waits (never re-presents) when no `Ready` snapshot exists.
The Runtime host publishes one snapshot per completed tick (M7), so an M7 windowed run presents one
black clear per snapshot. **M8 supersedes the single clear pass and step 3's M7 scope:** cameras,
draws, instance filling, and the per-camera passes are the approved M8 target, in progress; the M7
frame's instance SSBO and descriptor set exist empty (Q10=J1). The Runtime host starts the renderer
before `Setup` (D-M6-06).
### 3.2 Offscreen

Same clear shape as windowed without surface/swapchain/present/acquire/semaphores: two permanent
sRGB color images with one device-local allocation each, created once for the fixed
`WithOffscreen(w, h)` extent. Each image is transitioned once from `UNDEFINED` to
`COLOR_ATTACHMENT_OPTIMAL` and stays there; two frames in flight alternate the images, each gated by
its own fence. **M8 target:** the same per-camera dynamic-rendering passes and load-op rules as §3.1,
no frame-level clear, and `IEngine.CaptureFrameAsync` copies the composited image when a capture is
requested; otherwise the frame is discarded. **M7 (as built):** every frame is recorded for exactly
one consumed snapshot — the render thread waits the frame fence, releases that frame's previous
snapshot slot, takes the newest `Ready`, records one dynamic-rendering pass with
`loadOp = CLEAR` to opaque black and `storeOp = STORE`, submits with synchronization 2, attaches the
slot, and counts a discarded frame. With no `Ready` snapshot it waits instead of discarding; there is
no minimized path. There is no resize, present, or readback in M7 (D-M6-23; the approved M8 target
adds the public capture); pacing is one frame per consumed snapshot (D-M7-07 revises D-M6-12). The
host publishes one snapshot per completed tick. **`Current` for the M6 clear and the M7
consumption.**

**Headless and draw count.** `WithHeadless` never enters this document. One draw per non-empty
camera, one dynamic-rendering pass per camera over its rounded pixel viewport: an empty
`Clear.Color` camera keeps a clear-only pass and draws nothing, an empty `Clear.None`/`Clear.Depth`
camera records nothing, and areas outside every camera rect are not cleared. There is no frame-level
safety pass (M8 target; §8). A debug counter enforces at most `C` draws for `C` cameras.
## 4. Snapshot to instances

The render thread owns this work; the tick thread supplies immutable snapshots only. The M7
snapshot is already value-only: each `SpriteSnapshot` carries the loaded `SpriteAsset?`, the
`(Layer, ZIndex)` sort keys, the world matrix, the tint, and the `FlipX`/`FlipY` flags (Q2=B1), and
M8 does **not** change it. Everything that derives quad size, pivot, UVs, and a texture index from
the asset, and everything that draws, is the approved M8 target. WP-2 already provides the pure
planner, fatal-field validation, quad/flip math, and 96-byte instance layout (§5); page resolution
(WP-3) and the draw recording (WP-4) remain planned.

1. Take the immutable snapshot of loaded sprite assets; the renderer never reads the scene tree.
2. Sort `SpriteSnapshot`s once by `(Layer, ZIndex)` ascending; all cameras share the order, filtering
   preserves it, and ties are unspecified.
3. Per camera: filter `(CullingMask & Layer) != 0`; skip a null `SpriteAsset`; skip the invalid
   layer values that M7 captured as-is (`0`/multi-bit, Q3=C2) with the diagnostic; resolve
   `SpriteAsset.Texture` (`AssetRef<TextureDescriptor>`) and `TextureDescriptor.Payload`
   (`DataRef`) to loaded values and a resident GPU page through `IAssets` (§6). A page that is still
   loading is skipped this frame; a failed page draws the placeholder; for `Quad`, append one
   instance to the camera's contiguous range. A malformed field is fatal (§6.4 of the
   [M8 spec](../specs/m8-sprite-renderer.md#64-sprites-validity-and-flips)).

The per-frame instance record is a fixed **96-byte std430** record (details in §5); the conceptual
fields are:

| Field | Meaning |
| --- | --- |
| `Transform` | 2×2 linear part + translation from the world matrix (parent hierarchy included). |
| `Size` | World-unit quad size: `(uvSpan × textureSize) / scale`. |
| `Pivot` | Normalized pivot, top-left origin. |
| `Uvs[4]` | Normalized UVs, top-left origin; TL/TR/BR/BL order, arbitrary corners preserved. |
| `Tint` | Linear RGBA multiplied with the sampled texel. |
| `TextureIndex` | Index into the bindless texture page array; `0` is the placeholder. |
| `Flags` | Reserved; zero in M8. |

There is no `Vertices[4]` and no vertex buffer. The vertex shader derives the corners with the
approved pivot/flip formula:

```
corners  = { (0,0), (1,0), (1,1), (0,1) }      // TL, TR, BR, BL
c        = corners[gl_VertexIndex]
sx       = FlipX ? -1 : 1
sy       = FlipY ? -1 : 1
local    = ((c.x - pivot.x) * size.x * sx, (pivot.y - c.y) * size.y * sy)
position = viewProj * vec4(transform * local, 0, 1)
```

`FlipX`/`FlipY` mirror the **geometry** around the pivot in local space; each corner keeps its UV,
the transform is unchanged, `FlipX && FlipY` mirrors both axes, and a negative `LocalScale` composes
with the flip (the two are independent).

The fragment shader samples `textures[TextureIndex]` and multiplies `Tint`. The asset also carries
`indices` (`ushort[]?`): null for `Quad`, non-null for `TriangleStrip`/`Triangles` indexing the
generated corners. Pack creation preserves those assets, and **v1 draws `Quad` only**: a non-`Quad`
asset is skipped with a once-per-asset diagnostic and is never approximated as a quad. The indexed
path is deferred, **not M8 work**. There is no bake/load field validation (M5 is removed): malformed
`SpriteAsset` fields (UVs, `Scale`, `Pivot`, topology/indices) are fatal at draw time and named in
the `Bork2dException`, while a missing/failed texture draws the placeholder page (§6). The
[API reference §9](../api/reference.md#9-ispriterendertlayer) owns the full validity table.
## 5. One-draw contract

One descriptor set per frame in flight:

| Binding | Type | Contents |
| --- | --- | --- |
| 0 | storage buffer | Instance records for the frame. |
| 1 | combined image sampler array | Bindless texture pages, updated after bind. |

**M7 (as built, replaced in M8):** M7 creates the frame descriptor set with **binding 0 only**
(instance storage buffer, allocated empty, never updated, bound, or drawn) and a provisional
small instance buffer per GPU frame. **M8 target:** binding 0 holds the frame's 96-byte instance
records (written, bound, and drawn), and binding 1 is a runtime array of combined image samplers
with a variable descriptor count so page capacity can grow without recreating the pipeline layout.

Camera data travels in a 64-byte push constant (view-projection); no per-camera descriptor set.
One graphics pipeline: dynamic rendering, one sRGB attachment, no depth; no vertex input (geometry
from the instance SSBO and `gl_VertexIndex`); triangle strip, 4 vertices/instance; dynamic
viewport/scissor; culling and depth test/write disabled; straight alpha `src*a + dst*(1-a)`.

The instance record is a fixed 96-byte std430 ABI: `transform0` (M11, M12, M21, M22),
`transform1` (M31, M32, sizeX, sizeY), `uv0` (TL, TR), `uv1` (BR, BL), `tint`, and `misc`
(pivotX, pivotY, textureIndex, flags). The CPU writes the same layout and a unit test asserts the
stride/offsets. `FlipX`/`FlipY` are encoded as the sign of `sizeX`/`sizeY` (algebraically the same as
the `size * sx`/`size * sy` formula in §4), so the reserved `flags` stays zero in M8. WP-2 adds the
internal `SpriteInstance`, the mapped-memory writer, and the pure sort/filter/quad planner; the
growable buffers below remain WP-4. Each GPU frame owns one host-visible coherent instance buffer
that starts at 1024 instances; when a frame needs more and its fence is already signaled, the buffer
is recreated at `max(required, capacity × 2)` (rounded to a power of two) and binding 0 is updated.
There is no CPU frame array, no staging copy, and no growth while the frame is in flight. Allocation
failure or exceeding `maxStorageBufferRange` is fatal.

```c
vkCmdDraw(cmd, 4, instanceCount, 0, firstInstance);
```

`firstInstance` selects the camera's range in the frame instance buffer; `gl_InstanceIndex` addresses
the same record; there is no index buffer. Shaders are checked-in GLSL 450/460
(`Bork2d.Render/Shaders/sprite.vert`, `sprite.frag`) compiled at build time by the
`Bork2d.ShaderCompiler` tool (Silk.NET.Shaderc 2.23.0) into generated SPIR-V C# that is linked into
`Bork2d.Render`; there is no runtime shader I/O or compiler and no Vulkan SDK requirement. WP-2 adds
the sources, the tool/MSBuild targets, and the internal `SpritePipeline` (descriptor-set layout,
64-byte push-constant pipeline layout, one pipeline per sRGB format with dynamic rendering/no depth/no
vertex input/triangle strip/four vertices per instance/dynamic viewport-scissor/no culling/straight
alpha/sample count 1, and a `(filter, wrap)` sampler cache with `maxLod = 0`). Both renderer startups
create it after the color format is known and destroy it in cleanup; creation failure is a clean
renderer startup failure (D-M6-14). No descriptor write, bind, or draw is recorded yet. The vertex
shader fetches instances and generates corners; the fragment shader samples the bindless array and
multiplies the linear tint. `VkPipelineRenderingCreateInfo` carries the color format and no
depth/stencil format.
## 6. Texture pages and bindless textures

- A **texture page** is one GPU texture described by a MessagePack descriptor (`textures/<name>`)
  and a raw payload (`textures/<name>-raw`); page identity is the canonical **descriptor path**.
- The renderer reads descriptors and payloads through the app's `IAssets` seam from the render
  thread (`LoadAsset<TextureDescriptor>`, then `LoadData(descriptor.Payload)`), asynchronously
  through the existing loader thread. There is no snapshot change and no new project dependency.
- The bindless array holds combined image samplers, one per resident page; `TextureIndex` selects
  the page. The shader sees a runtime-sized, partially bound `uniform sampler2D textures[]`; only
  resident pages have valid descriptors.
- One page per descriptor path for the run; the first successful load wins, later descriptor
  instances for the same path reuse it, and there is no eviction or replacement (M8 target).
- **Placeholder:** a generated 8×8 magenta/black checkerboard at index `0`, nearest filter, clamp
  wrap, one mip, from CPU bytes at renderer startup, always resident. A missing `Texture` reference,
  a failed descriptor/payload load, a payload length mismatch, an unsupported format, bad
  dimensions, or dimensions over `maxImageDimension2D` mark that path **failed for the run** and map
  it to the placeholder; failed paths are not retried even if a later overlay would provide them.
  A sprite with a failed/absent texture is drawn with the placeholder and its tint, not skipped.
- **Capacity grows dynamically:** the table starts at 256 entries (index `0` placeholder plus 255
  real pages); when a new real page is needed and the table is full it doubles up to
  `maxPerStageDescriptorUpdateAfterBindSampledImages` and the descriptor-set/pool limits. Growth
  happens on the render thread between frames with all in-flight frames idle
  (`vkDeviceWaitIdle`); the per-frame descriptor sets/pools are recreated with the new variable
  descriptor count and every resident page descriptor is rewritten. Reaching the device limit is a
  fatal `Bork2dException` naming the limit and the requested capacity. No VMA and no hard-coded cap.
- **Memory:** page images and staging buffers use a pool/suballocator — a small number of
  device-local image blocks and a host-visible staging ring — so there is no per-page
  `vkAllocateMemory`; blocks are destroyed after `vkDeviceWaitIdle` at shutdown.
- **Upload path:** a page is `Pending` while its descriptor/payload load runs (sprites skip it),
  then `Uploading` during a one-time command buffer with the staging copy and the
  `TRANSFER_DST → SHADER_READ_ONLY` barrier, submitted on the graphics queue with its own fence.
  When that fence signals at a later frame boundary the page becomes `Ready` and its descriptor is
  written into both per-frame bindless sets; the render thread never blocks on an upload.
- Images are `VK_FORMAT_R8G8B8A8_SRGB`, `SAMPLED | TRANSFER_DST`, optimal tiling, one mip/layer.
  Sampler state comes from the descriptor (`nearest`/`linear`, `clamp`/`repeat`, `maxLod = 0`, no
  anisotropy/compare/mipmaps) and samplers are cached by `(filter, wrap)`. Descriptor writes use
  `COMBINED_IMAGE_SAMPLER` with `PARTIALLY_BOUND | UPDATE_AFTER_BIND` and a variable count. Missing
  content uses the placeholder; Vulkan allocation/queue/device failures stay fatal. KTX2 is later.
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
`(0.5,0,0.5,1)`. Pixel bounds use half-up rounding and clamping to the framebuffer:
`left = clamp(floor(x * W + 0.5), 0, W)` and likewise for top/right/bottom; projection uses the
rounded `pixelWidth / pixelHeight` aspect. A camera whose rounded rect is empty is invalid and is
skipped as a whole, including its clear (throttled diagnostic, M8 target). The other invalid-camera
cases are a non-finite/`<= 0` `Size`, a non-finite or non-positive viewport, an unknown
`ClearKind`, a non-finite `ClearColor`, a singular camera world matrix, and a non-unit camera scale.
There is no validation in the public setters or in M7 capture.
### 7.3 Projection and defaults

With view-space `(xv, yv)`, half-height `s = Size`, and aspect `a`:

```
xNdc =  xv / (s * a)
yNdc = -yv / s        // Vulkan NDC is Y-down; world/API stay Y-up
zNdc = 0
```

The negation is the only place the Y-up API meets Vulkan clip space. `ICamera.WorldToScreen`/
`ScreenToWorld`/`WorldSize` are `Current` (M8 WP-2): they use the same camera world inverse, `Size`,
rounded viewport/aspect, and Y-flip, return framebuffer pixels in global coordinates (the viewport
offset included), and throw `ArgumentOutOfRangeException` for non-finite input and
`InvalidOperationException` for an invalid camera or a missing framebuffer. The shared pure
`Bork2d.Engine.Rendering.CameraProjection` helper is used by both the camera and the renderer's
`SpriteCameraProjection` bridge, so the API and the GPU cannot disagree. `WorldSize` returns
`(2 * Size * aspect, 2 * Size)`. The framebuffer source is the engine window's `IWindow.Size`
(physical pixels) for the helpers and the swapchain/target extent for the renderer. Camera and
sprite property defaults are owned by
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

**Clearing and multiple cameras (approved M8 target, in progress).** There is **no frame-level safety
clear**. Each camera gets its own dynamic-rendering pass over its rounded pixel rect, with
`loadOp = CLEAR` for `Clear.Color` (clearing to `ClearColor`) and `loadOp = LOAD` for `Clear.None`
and `Clear.Depth`. `Clear.Depth` is reserved, logs once, and never reads or writes depth; it behaves
like `Clear.None`. Cameras apply in ascending `ZIndex` (default `0`); ties are unspecified. A later
`Clear.Color` wipes only its own viewport and `Clear.None` overlays (the HUD pattern). Consequences:
a frame with zero cameras records no pass and does not clear the target; a target whose first camera
uses `Clear.None` preserves prior or undefined content; areas outside every camera rect are not
cleared; a full-screen `Clear.Color` camera clears the whole target. Draw count is one draw per
non-empty camera: an empty `Clear.Color` camera produces a clear-only pass, an empty
`Clear.None`/`Clear.Depth` camera produces nothing, and `RenderStats.DrawCalls` counts the
`vkCmdDraw` calls (`NonEmptyCameras` counts cameras with a draw). Camera order is D-026.
## 9. Synchronization and presentation

- Windowed: 3 swapchain images (`minImageCount + 1`, clamped) and 2 GPU frames in flight; offscreen:
  2 permanent targets and 2 frames in flight, no present. Each windowed frame owns a command
  buffer/pool, a fence, and an image-available semaphore, and each swapchain image owns its
  render-finished semaphore (M6 WP-3). Each offscreen frame owns a command buffer/pool and a fence
  (M6 WP-4). M7 adds each frame's empty instance SSBO and frame descriptor set
  (binding 0 only, Q10=J1). The M8 target replaces the empty SSBO with a growable host-visible
  96-byte-record buffer, adds binding 1 (bindless samplers), and fills/writes/binds both.
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
- Texture uploads are recorded on the render thread in a **separate submit with its own fence**
  when a page's content is ready (M8 target); they are not interleaved with the frame's camera
  passes. `vkDeviceWaitIdle` is for recreation/shutdown and page-capacity growth between frames;
  `VK_KHR_synchronization2` is the only submission path. The render thread never blocks on an
  upload.
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
sRGB color images (one device allocation each) cleared and discarded; one discarded frame per
consumed snapshot (Q7=G1). M7 has no public readback; the approved M8 target adds
`IEngine.CaptureFrameAsync`, which copies the composited image (RGBA8, top-left origin) after the
last camera pass and before discard, only when requested. **`Current` for the M6 clear and the M7
consumption**; the host publishes one snapshot per completed tick (M7).
## 10. Debugging, validation, and performance

- Validation layers in debug and with `WithValidation(true)`, plus a debug messenger; errors are
  fatal in debug/test; `VK_EXT_debug_utils` names the instance, device, swapchain/offscreen images,
  image views, command buffers, the clear pass, and each frame's instance buffer/descriptor set.
  **`Current` in M6 and M7** for both paths; no per-frame or per-draw label churn.
- Counters (M8 target): frames completed/sequence, captured/drawn cameras, draw calls/instances,
  captured/skipped sprites, texture pages/capacity, page uploads/failures/placeholder uses,
  page/instance-buffer growths, instance capacity/overflow, and plan/upload CPU. The public subset is
  `IEngine.RenderStats`; the rest are internal and feed logs/tests. **`Current`:** frames presented
  (windowed), frames discarded (offscreen), swapchain
  recreations, validation errors/warnings, last frame number, last present result (M6), plus the
  M7 handoff counters — snapshots published/consumed/superseded, backpressure waits, max live
  snapshots, and suspended consumptions (Q8) — logged once at start/stop; the offscreen start line
  also logs extent/format/images/memory bytes, and both start lines log handoff on, ring depth 3,
  one frame per snapshot, and instance resources on.
- Failures: no suitable device/feature or shader/pipeline failure is fatal; `OUT_OF_DATE`/
  `SUBOPTIMAL` recreate; device loss is fatal; surface loss recovers once; an invalid camera is
  skipped with its clear (throttled diagnostic); a null sprite, invalid layer, non-`Quad` topology,
  or pending page is skipped with a diagnostic; a failed/absent texture draws the placeholder;
  malformed sprite fields (UVs, `Scale`, `Pivot`, topology/indices) are fatal `Bork2dException`s;
  page capacity grows to the device limit and fails explicitly there (no silent drop); validation
  errors fail debug/test. A renderer fatal stops the ring so a blocked producer wakes (Q9=I1);
  renderer cleanup releases every held snapshot slot and `Join` drains the ring. A missing
  loader/instance/surface fails windowed `platform.Start()` before `Setup`; a renderer
  device/feature/format/target failure is reported after `Setup` (D-M6-06). **`Current` in M6** for
  both paths plus the M7 fatal/stop wake; the M8 draw/page/shader failures are `Planned` (in
  progress).

Rules: one `vkCmdDraw` per non-empty camera, inside that camera's single dynamic-rendering pass; no
per-sprite descriptors or indirect draws; per-frame instance buffers grow only at a free fence and
steady-state allocation is zero; sort once per snapshot, filter per camera, cache resolution; no
GPU→CPU readback unless `CaptureFrameAsync` is requested; update-after-bind pages; never stall the
tick on GPU work (ring backpressure only); no artificial cap or sleep.

Approved M8 performance gate (local opt-in benchmark; supersedes the earlier provisional targets):

| Metric | Target |
| --- | --- |
| Scene | 50,000 sprites, 1 camera, 1920×1080 |
| Presentation | Vsync off in the benchmark (offscreen discard); no artificial cap |
| Draw calls | 1 per non-empty camera, plus clear-only passes |
| Render-thread CPU | Measured p50/p95 (10-second window after warm-up); report it, do not gate on a provisional number |
| Steady-state allocations | 0 bytes/frame in hot paths |
| Texture pages | initial capacity 256 (placeholder + 255 real), doubling up to the device limit |
| Validation | zero errors with layers; warnings per D-M6-21 |

Pass criterion: **median ≥165 FPS** (p50 frame time ≤ ~6.06 ms) in the opt-in offscreen benchmark;
p95 is the stability metric. The benchmark and GPU tests are local-only, not CI.
## 11. Current status

| Area | Status |
| --- | --- |
| SDL window creation, input, time, and the windowed/offscreen tick loops | `Current` M2/M6/M7 (`Bork2d.Runtime`). The windowed platform creates the SDL window, Vulkan instance, and surface on the OS thread; the host starts the renderer before `Setup` and waits for readiness only before the first tick; ticks are ring-backpressured. |
| `Bork2d.Render` | `Current` M6/M7 plus M8 WP-2: internal seam/control channel, Vulkan packages, instance/validation/debug-utils bootstrap, device selection, pure policies, the windowed renderer, the offscreen target/renderer, the handoff consumer/combined wait, the empty per-frame instance resources, the internal `SpritePipeline` (descriptor-set layout/pipeline layout/pipeline-per-sRGB-format/sampler cache), and the shared camera-projection bridge. Texture pages/uploads and per-camera draws remain `Planned` (WP-3/WP-4). |
| Vulkan instance/validation bootstrap and device/queue/feature logging | `Current` M6 |
| Windowed swapchain, acquire/present, dynamic-rendering clear, resize/minimize/fullscreen recreation, surface-loss recovery, counters | `Current` M6, host-wired in M6/M7 |
| Offscreen targets, dynamic-rendering clear, discard, and the offscreen loop | `Current` M6 |
| Snapshot model/capture and the internal three-slot ring state machine | `Current` M7 (internal; no draw) |
| Renderer snapshot consumption (one presented/discarded frame per consumed snapshot), fence-held slot lifetime, suspended minimized drain, combined wait | `Current` M7 |
| Host acquire/capture/publish wiring, backpressure, pacing | `Current` M7 |
| Empty per-frame instance SSBO and frame descriptor set (binding 0, Q10=J1) | `Current` M7 (allocated, unused; replaced by the M8 target) |
| Build-time shader tool, GLSL, generated SPIR-V, sprite pipeline/layout/samplers | `Current` M8 WP-2 (internal; created at renderer startup; no draw/bind yet) |
| Camera projection/rounding helper and public `WorldToScreen`/`ScreenToWorld`/`WorldSize` | `Current` M8 WP-2 (shared by the camera and the renderer bridge; deterministic tests) |
| 96-byte instance ABI, mapped-memory writer, pure sprite sort/filter/quad validation | `Current` M8 WP-2 (no GPU instance buffer growth/draw yet) |
| M8 remaining contract (approved 2026-10-31; WP-3/WP-4) | `Planned` M8, **in progress**: per-camera dynamic-rendering passes with no frame-level safety pass and `Clear.Color`/`Clear.None`/`Clear.Depth` load ops; `IAssets` seam, texture pages/pool/staging/dynamic growth/placeholder; growable per-frame instance buffers and ranges; public `RenderStats`/`CaptureFrameAsync`; counters and throttled diagnostics. See the [M8 spec](../specs/m8-sprite-renderer.md). |
| Headless | `Current` for the asset/tick path with no SDL/Vulkan/renderer; the renderer is intentionally absent because headless has no GPU work; `RenderStats` stays default and `CaptureFrameAsync` throws `NotSupportedException` (M8 target) |

## 12. Related documents

- [Threading](threading.md) — snapshot ring, bounded run-ahead, fences.
- [Assets and tooling](assets-and-tooling.md#8-texture-descriptors) — descriptors, sprite assets.
- [Runtime lifecycle](runtime-lifecycle.md#2-modes) — windowed/offscreen/headless modes.
- [API guide](../api/guide.md#5-cameras), [reference](../api/reference.md#13-defaults) — cameras.
- [Roadmap](../roadmap.md), [decision log](../decisions.md) (D-009).
