# Rendering

> **Status:** target design. `Bork2d.Render` is an empty project today. The windowed renderer
> lands in roadmap **M6–M8**; the offscreen mode shares the same device/pipeline path but has
> no surface or swapchain. Headless mode has no renderer at all.

## 1. Scope

The renderer receives one immutable [frame snapshot](threading.md#3-frame-snapshot) at a time
and turns it into pixels:

- one instanced draw call per non-empty camera;
- sprites are quads resolved from **sprite assets** (UVs, pivot, topology, pixels-per-unit
  scale), not a hard-coded unit quad;
- all visible quads in a camera share one pipeline, one instance buffer, and one bindless
  texture set;
- Vulkan 1.3 dynamic rendering, no `VkRenderPass`/`VkFramebuffer` for the swapchain;
- no depth buffer; ordering is explicit;
- windowed (swapchain/present) and offscreen (windowless target, frames discarded) modes.

The renderer does not know game types, the generic layer enum, TOML, or the source asset tree.
It consumes flattened snapshot data and an immutable resource table.

## 2. Backend stack and device requirements

| Concern | Choice | Status |
| --- | --- | --- |
| Windowing and surface | SDL2 via `Silk.NET.SDL` (windowed only) | `Planned` |
| Vulkan bindings | `Silk.NET.Vulkan` (+ KHR extensions as needed) | `Planned` |
| Graphics API | Vulkan 1.3 | `Planned` |
| Rendering path | Dynamic rendering (core 1.3) | `Planned` |
| Texture access | Descriptor indexing / bindless array | `Planned` |
| Sprite geometry | Instanced shader-generated 4-corner quads from sprite assets | `Planned` |
| Shader compilation | Offline GLSL → SPIR-V (compiler `Open`) | `Planned` |
| Memory allocation | VMA or a minimal suballocator (`Open`) | `Open` |
| Presentation | Windowed: FIFO default. Offscreen: none | `Planned` |

### Required device features

Fail early with a clear diagnostic if the device cannot provide:

- Vulkan API 1.3;
- `dynamicRendering`;
- `synchronization2`;
- descriptor indexing:
  - `runtimeDescriptorArray`;
  - `shaderSampledImageArrayNonUniformIndexing`;
  - `descriptorBindingPartiallyBound`;
  - `descriptorBindingSampledImageUpdateAfterBind`;
  - `descriptorBindingUpdateUnusedWhilePending`;
  - `descriptorBindingVariableDescriptorCount`;
- a graphics queue family (and present support in windowed mode);
- a supported sRGB swapchain format (windowed) or sRGB offscreen format.

Not required: geometry/tessellation shaders, depth/stencil, MSAA, HDR, anisotropic sampling,
sparse resources, timeline semaphores (reserved for future async uploads).

### Queues and device selection

- One queue family with graphics (and present, windowed) in v1. Uploads use the graphics queue
  on the render thread; a dedicated transfer queue is a later optimization.
- Prefer a discrete GPU; fall back to the first suitable device. An override is `Open`.
- Log device, API version, driver, and bindless limits.
- Validate the configured texture-page capacity against
  `maxPerStageDescriptorUpdateAfterBindSampledImages` and related limits. Fail or lower the
  capacity explicitly; never silently create an invalid layout.
- Offscreen mode creates no surface and no swapchain; the instance/device/queue setup is
  otherwise the same. Headless mode creates nothing.

## 3. Frame structure

### 3.1 Windowed

1. Wait for a snapshot/control signal and a free GPU frame slot (fence). Take the newest
   `Ready` snapshot, resolve sprite assets, sort/filter, copy instances into the frame's
   instance buffer. The snapshot slot is held until the frame fence signals (bounded
   run-ahead).
2. Acquire a swapchain image and wait for the image-available semaphore.
3. Reset the command buffer.
4. Transition the image to `COLOR_ATTACHMENT_OPTIMAL`.
5. Begin **one** dynamic rendering pass for the whole framebuffer:
   - color attachment = swapchain image view;
   - `loadOp = CLEAR` with opaque black as a deterministic safety clear;
   - no depth attachment; `storeOp = STORE`.
6. For each camera in ascending `EntityId` order:
   - set viewport/scissor to its normalized viewport in pixels;
   - if `Clear == Color`, clear that viewport to `ClearColor`;
   - if `Clear == Depth`, one-time debug warning and no-op;
   - if the camera has no visible sprite, do **not** issue a draw;
   - otherwise bind the frame descriptor set, push the camera view-projection, and issue
     `vkCmdDraw(4, instanceCount, 0, firstInstance)`.
7. End dynamic rendering.
8. Transition to `PRESENT_SRC_KHR`, submit, present.
9. The frame fence frees the snapshot slot and the frame's instance buffer.

### 3.2 Offscreen

Same as windowed except:

- no `VkSurfaceKHR`, swapchain, acquire, or present;
- one permanent offscreen color image per frame slot (or one image with layout transitions);
- the framebuffer extent is the `WithOffscreen(width, height)` size; there is no resize;
- frames are discarded in v1 (internal target only);
- the frame fence frees the snapshot slot and frame resources, providing the same bounded
  run-ahead pacing.

### 3.3 Headless

No renderer, no device, no frames. Headless mode (`WithHeadless`) never enters this document's
code paths.

### 3.4 Draw-count invariant

- **One draw per non-empty camera**, not one per texture page and not one per sprite.
- A camera with zero visible sprites performs its clear (if requested) but issues no draw.
- A frame with one camera and any number of visible sprites has exactly one draw call.
- The whole framebuffer is one dynamic rendering pass; cameras are viewport/scissor regions.

## 4. From snapshot to instances

The render thread owns all of this; the tick thread only supplies immutable snapshot data.

### 4.1 Resolve

1. Take the current immutable sprite resource table.
2. Sort `SpriteSnapshot`s once by `(Layer, ZIndex, EntityId)` ascending. Shared by all cameras;
   filtering preserves order.
3. For each camera:
   - filter by `(camera.CullingMask & sprite.Layer) != 0`;
   - skip `Sprite.Empty`;
   - resolve the `Sprite` handle to a sprite asset (texture descriptor, UVs, pixels-per-unit
     scale, pivot, topology, optional indices);
   - skip if the sprite/texture/payload/GPU page is not ready;
   - for `Quad`, compute the world-unit quad size and append an instance (UVs, pivot, size,
     transform, tint, texture index); non-Quad assets are not drawn by the v1 renderer;
   - append one instance to the camera's contiguous range.

Multiple cameras may see the same sprite. Resource lookup and instance data can be cached per
sprite for the frame; each camera gets its own instance because the view-projection and
viewport differ.

### 4.2 Instance data

Logical per-instance fields (exact layout implementation detail):

| Field | Meaning |
| --- | --- |
| `Transform` | 2×3 world transform from the snapshot (includes parent hierarchy). |
| `Size` | World-unit quad size: `(uvSpan × textureSize) / scale`. |
| `Pivot` | Normalized pivot, top-left origin. |
| `Uvs[4]` | Normalized UVs, top-left origin, in top-left/top-right/bottom-right/bottom-left order. |
| `Tint` | Linear RGBA multiplied with the sampled texel. |
| `TextureIndex` | Index into the bindless texture page array. |

There is no `Vertices[4]` field. The vertex shader derives the four local corners from `Size`,
`Pivot`, and `gl_VertexIndex`; the asset stores no vertex positions.

### 4.3 Quads now, indexed topologies later

v1 draws `topology = Quad` as a four-corner triangle strip. It is still **instanced**, with
`gl_VertexIndex` selecting one generated corner.

The sprite asset also carries `indices` (`ushort[]?`): null for `Quad`, non-null for
`TriangleStrip`/`Triangles`, indexing the four generated corners. The v1 renderer validates and
preserves those assets but does not draw them yet. The indexed draw path is a later M8
extension; it is not silently approximated as a quad, and there is no separate mesh asset.

### 4.4 Shader

- Four corners are generated in the vertex shader; no vertex buffer.
- `gl_VertexIndex` 0..3 selects the corner (top-left, top-right, bottom-right, bottom-left) and
  its UV from the instance.
- Vertex shader:
  - `corner = corners[gl_VertexIndex]` where `corners = { (0,0), (1,0), (1,1), (0,1) }`
    (top-left, top-right, bottom-right, bottom-left);
  - `local = ((corner.x - pivot.x) * size.x, (pivot.y - corner.y) * size.y)`;
  - `gl_Position = viewProj * vec4(instance.Transform * local, 0, 1)`.
- Fragment shader: sample `textures[TextureIndex]`, multiply by `Tint`, output.
- No vertex buffer, no index buffer, no per-sprite descriptor updates in v1.
- The future indexed path consumes the asset's `indices`; it does not add a vertex array.

## 5. The one-draw contract

### 5.1 Descriptors

One descriptor set per frame in flight:

| Binding | Type | Contents |
| --- | --- | --- |
| 0 | storage buffer | Instance records for the frame, written by the render thread. |
| 1 | combined image sampler array | Bindless texture pages, updated after bind. |

Camera data travels through a 64-byte push constant (view-projection), so there is no
per-camera descriptor set or buffer. A later feature that needs more camera data can use a
small UBO without changing the draw model.

### 5.2 Pipeline

One graphics pipeline for sprites:

- dynamic rendering, one sRGB color attachment, no depth attachment;
- no vertex input state; geometry comes from the instance SSBO and `gl_VertexIndex`;
- triangle strip, 4 vertices per instance;
- dynamic viewport/scissor;
- culling disabled (mirrored quads);
- depth test/write disabled;
- straight-alpha blending:
  `srcColor * srcAlpha + dstColor * (1 - srcAlpha)`;
- no per-sprite specialization or pipeline.

The draw:

```c
vkCmdDraw(cmd, 4, instanceCount, 0, firstInstance);
```

`firstInstance` points into the frame's instance buffer for that camera; `gl_InstanceIndex`
addresses the same record. There is no index buffer.

### 5.3 Why this satisfies the requirement

- **Instancing:** every sprite of a camera is one instance of a generated four-corner quad;
  per-sprite corners/UVs live in the instance.
- **Bundled textures:** texture pages live in a bindless array, so changing texture does not
  break the draw.
- **Dynamic rendering:** no render pass/framebuffer setup per camera.
- **One draw:** one instance range + one `vkCmdDraw` per camera.

The debug counter enforces at most `C` draws for `C` cameras. A feature that needs a second
draw per camera (custom material, a different pipeline) must be designed, documented, and
excluded from the one-draw promise until it is.

## 6. Texture pages and bindless textures

- A **texture page** is one GPU texture described by a MessagePack texture descriptor
  (`textures/<name>`) with a raw payload (`textures/raw/<name>`).
- The bindless array holds combined image samplers, one element per resident descriptor.
  The instance's `TextureIndex` selects the page.
- The shader sees a runtime-sized array (`uniform sampler2D textures[]`) with descriptor
  indexing. It is partially bound; only resident pages have valid descriptors.
- Updates use `VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER` with
  `UPDATE_AFTER_BIND` / `PARTIALLY_BOUND`, so pages can be added/replaced without recreating
  the set. A replaced image remains alive until frames that referenced it complete.
- Capacity is a fixed, configurable page count (provisional 4096), validated against device
  limits. Overflow is a configuration/build error, not a silent extra draw.
- Sampler state is per descriptor (`nearest`/`linear`, `clamp`/`repeat`). Pixel-art pages
  default to nearest + clamp.
- Texture formats: RGBA8 sRGB now; KTX2 later. Compressed formats, mips, and array textures
  are later work.
- One source texture maps to one page in v1. Multi-texture atlas packing is future work; the
  sprite asset's UVs already support packed pages when it lands.

## 7. Coordinates, camera, and projection

### 7.1 World space

- +X right, +Y up.
- Rotation in radians, positive counter-clockwise around +Z.
- Scale is a multiplier; negative scale mirrors.
- One world unit is abstract. A sprite's source pixels become units by dividing by its
  pixels-per-unit `scale`; `Camera.Size` and positions use units.

### 7.2 Camera

`ICamera<TLayer>` extends `ITransform`. The camera's world transform is the inverse view
transform:

```
view = inverse(cameraWorldTransform)
```

Camera scale is not a zoom control in v1; `Size` is. A non-unit camera scale is unsupported.

`Camera.Size` is the **half visible height** in world units:

```
visibleHeight = 2 * Size
visibleWidth  = visibleHeight * aspect
aspect        = viewportPixelWidth / viewportPixelHeight
```

The viewport is normalized `(x, y, width, height)` with a top-left origin:

```
pixelX = x * framebufferWidth
pixelY = y * framebufferHeight
pixelW = width * framebufferWidth
pixelH = height * framebufferHeight
```

Full-screen default: `(0, 0, 1, 1)`. Split screen:

```csharp
left.Viewport  = new Vector4(0f,   0f, 0.5f, 1f);
right.Viewport = new Vector4(0.5f, 0f, 0.5f, 1f);
```

Zero-width/height viewports are skipped with a debug warning.

### 7.3 Projection

With view-space point `(xv, yv)` and half-height `s = Size`, aspect `a`:

```
xNdc = xv / (s * a)
yNdc = -yv / s        // Vulkan NDC is Y-down; world/API stay Y-up
zNdc = 0
```

The Y negation is the only place the Y-up API meets Vulkan's Y-down clip space. Sprite quad
corners are generated in Y-up world-local space by the vertex shader from UVs, texture size,
scale, and pivot; the asset has no vertices array.

### 7.4 Screen ↔ world (planned)

```
ScreenToWorld(camera, pixel)
WorldToScreen(camera, world)
```

The conversion accounts for viewport, `Size`, aspect, and rotation. Input coordinates are
never treated as world units.

### 7.5 Defaults

`CreateCamera(parent)` is planned to apply:

| Property | Default |
| --- | --- |
| `Size` | `10` (20 units tall) |
| `Viewport` | `(0, 0, 1, 1)` |
| `CullingMask` | all declared layer flags |
| `Clear` | `ClearKind.Color` |
| `ClearColor` | `Color.Black` |
| Transform | identity at origin |

The current implementation uses `Cyan` and a zero mask; both are placeholders.

## 8. Sorting, layers, and clears

### 8.1 Sort order

The renderer sorts each snapshot's sprites once:

```
Layer (numeric, ascending)
  → ZIndex (ascending)
    → EntityId (ascending)
```

Higher layer/`ZIndex` draw later/on top. Entity ID is the stable tie-breaker.

Layers are flags, but a renderable must have exactly one bit:

- `0` (no layer) is invalid and skipped with a debug diagnostic;
- two or more bits are invalid and skipped;
- named layer values must be powers of two and fit in 32 bits;
- declare layers in ascending numeric order to define draw order.

A camera's `CullingMask` may combine any bits; `0` sees nothing. Filter:
`(mask & layer) != 0`.

### 8.2 Culling

v1 culls only by layer. A sprite outside the camera viewport is still instanced and clipped by
the GPU. Frustum/AABB culling is a planned optimization; it must not change logical visibility.

### 8.3 Clearing and multiple cameras

- The frame starts with a safety clear to opaque black.
- `Clear.Color` clears the camera's viewport to `ClearColor` before drawing.
- `Clear.None` preserves what earlier cameras drew; if first, the result is safety black.
- `Clear.Depth` is reserved: no depth attachment, so it logs once and does nothing.
- Camera order is ascending `EntityId` (creation order). A later camera with `Clear.Color`
  wipes its viewport; with `Clear.None` it overlays.
- A full-screen HUD camera with `Clear.None` and a later ID is the overlay pattern.

Camera order may become an explicit property later; that is `Open` and does not change
one-draw-per-camera.

## 9. Synchronization and frames in flight

- Windowed: swapchain images at least 3; GPU frames in flight 2.
- Offscreen: offscreen targets at least 2; GPU frames in flight 2; no present.
- Each frame slot owns:
  - command buffer;
  - image-available/render-finished semaphores (windowed) or offscreen equivalents;
  - fence;
  - instance buffer (persistently mapped);
  - descriptor set.
- A **snapshot slot is held until the GPU frame fence**, not just until the CPU copies
  instances. This is the bounded run-ahead pacing: the tick can be at most a ring ahead and
  then waits. The GPU does not read snapshot memory; the hold is for lifetime/pacing.
- The CPU waits on the frame fence before reusing its command buffer/instance buffer.
- Windowed acquire/present:
  - wait `imageAvailable` at `COLOR_ATTACHMENT_OUTPUT`;
  - signal `renderFinished` after submission;
  - present waits on `renderFinished`.
- Layout transitions:
  `UNDEFINED → COLOR_ATTACHMENT_OPTIMAL` at frame start,
  `COLOR_ATTACHMENT_OPTIMAL → PRESENT_SRC_KHR` at the end (windowed); offscreen remains in
  `COLOR_ATTACHMENT_OPTIMAL` or transitions to a readback layout later.
- Uploads are recorded on the render thread before drawing (v1); a dedicated transfer queue
  and timeline semaphores are later.
- `vkDeviceWaitIdle` is acceptable for swapchain/offscreen recreation and shutdown; it is not
  used per frame.
- `VK_KHR_synchronization2` queue submission is the default; no `vkQueueSubmit` fallback.

There is **no fixed frame rate or artificial frame limiter**. Windowed pacing follows
presentation/GPU completion; offscreen follows GPU completion. Headless has no frames.

## 10. Windowed presentation and offscreen targets

### 10.1 Windowed

- Format preference: `B8G8R8A8_SRGB` → `R8G8B8A8_SRGB`; color space
  `VK_COLOR_SPACE_SRGB_NONLINEAR_KHR`.
- Present modes: `FIFO` when vsync is on; `MAILBOX`/`IMMEDIATE` when off and supported;
  `FIFO` fallback.
- Extent from the SDL framebuffer size. Zero extent = minimized: skip acquire/present.
- `OUT_OF_DATE`/`SUBOPTIMAL` trigger recreation; resize is coalesced.
- Recreate waits for device idle in v1; recreate image views/swapchain; request a redraw.
- Present when a new snapshot arrives or a control message requires redraw. Do not re-present
  an unchanged snapshot every refresh in v1.
- Device loss is fatal: stop render thread, propagate, shut down. No automatic re-creation.

### 10.2 Offscreen

- No surface/swapchain/present; fixed extent from `WithOffscreen(w,h)`.
- One or two offscreen color images; render the same pass and draws.
- Frames are discarded in v1; no public readback/capture.
- No resize/minimize path.
- Pacing is GPU frame completion; there is no vsync and no artificial delay.
- The internal target is for validation, benchmarks, and future screenshots.

### 10.3 Headless

No renderer. Headless mode (`WithHeadless`) does not initialize Vulkan or allocate GPU
resources.

## 11. Shaders and pipeline creation

- GLSL 450/460, compiled offline to SPIR-V. Compiler choice is `Open`; runtime does not
  compile GLSL.
- Shaders:
  - `sprite.vert` — instance SSBO fetch, corner/UV generation by `gl_VertexIndex`, world
    transform, push-constant view-projection;
  - `sprite.frag` — bindless sample, tint multiply.
- Pipeline creation uses `VkPipelineRenderingCreateInfo` with the color format and no
  depth/stencil format.
- SPIR-V is embedded or copied next to the assembly; the build step guarantees freshness.
- Pipeline cache, shader hot reload, and specialization constants are later.

## 12. Asset upload and residency

1. The asset service decodes the texture descriptor and raw payload (RGBA8 now, KTX2 later).
2. The render thread creates/maps a staging buffer, copies pixels, transitions to
   `SHADER_READ_ONLY_OPTIMAL`, creates the sampler, and updates the bindless descriptor.
3. The resource table exposes the sprite asset only when the GPU page is ready.
4. A sprite whose texture/payload/page is not ready is skipped; no placeholder.
5. Unload retires the resource from the table, then waits for frames that may reference it
   before destroying image/sampler and clearing the descriptor.

v1 is load-and-keep; pack/asset unload is future work. The threading rule is fixed: CPU
lifetime belongs to the asset side; GPU lifetime belongs to the render thread.

See [Assets and pack files](assets.md#8-texture-descriptors) for descriptor/payload schemas.

## 13. Debugging, validation, and failure handling

- Validation layers in debug and with `WithValidation(true)`; debug messenger; errors are
  fatal in debug/test configurations.
- `VK_EXT_debug_utils` labels for frames, cameras, passes, and texture pages; object names for
  captures.
- Per-frame counters: draw calls, instances, skipped sprites (not ready/invalid), resident
  pages, snapshot sequence, render CPU time, present mode (windowed).
- RenderDoc/Nsight capture works without special build flags.
- Failure policy:
  - no suitable device/missing feature/shader failure → fatal;
  - `OUT_OF_DATE`/`SUBOPTIMAL` → recreate;
  - device lost → fatal;
  - invalid sprite/layer → skip + debug diagnostic;
  - bindless capacity exceeded → configuration error (`Open`);
  - validation error → fail debug/test runs.
- Headless mode never reaches this code. Offscreen mode has no present/swapchain failures but
  still validates device/pipeline/offscreen targets.

## 14. Performance rules and targets

Rules:

- One `vkCmdDraw` per non-empty camera; no per-sprite descriptors.
- Persistent instance buffers; no steady-state per-frame allocations.
- Sort once per snapshot; filter per camera; cache sprite asset resolution.
- Contiguous instance ranges per camera; no per-sprite indirect draws.
- No GPU→CPU readbacks in steady state.
- Update-after-bind texture pages; no descriptor-set recreation on page add.
- Never stall the tick on GPU work; bounded snapshot-ring backpressure is the only coupling.
- No artificial frame cap or sleep; the display/GPU and ring define pacing.

Provisional targets (validate in M8, not commitments):

| Metric | Target |
| --- | --- |
| Scene | 50,000 sprites, 1 camera, 1920×1080 |
| Presentation | Follow the display (test machine: 165 Hz); no artificial cap |
| Draw calls | 1 for the scene (plus clears) |
| Render-thread CPU | instance build + submit under ~2 ms for the target scene |
| Steady-state allocations | 0 bytes/frame in snapshot/render hot paths |
| Texture pages | at least 256 resident pages in the test project |
| Validation | zero errors with validation layers |

The 165 Hz target is a measurement goal, not a cap: if the machine cannot sustain it, record
the measured ceiling and optimize. The architectural requirements are one draw per camera and
linear instance packing with zero steady-state allocations.

## 15. Open decisions

| Topic | Question |
| --- | --- |
| Allocator | VMA bindings vs a small custom suballocator. |
| Shader compiler | glslangValidator, Shaderc, or an MSBuild task; SPIR-V location. |
| Bindless overflow | Hard startup failure, lower capacity, or a documented fallback that adds draws. |
| Sampler defaults | Per-descriptor point/linear and wrap defaults; editor overrides. |
| Mipmaps/compression | v1 raw RGBA8 only? Mips/KTX2 timeline. |
| Camera order | Entity ID vs explicit `Order`. |
| Frustum culling | v1 layer mask only; AABB later. |
| Offscreen readback | Internal discard now; public capture later. |
| MSAA | None in v1; confirm no 2D need. |
| Present policy | Never re-present unchanged snapshots (plan) vs present each refresh. |
| Device selection | Override policy for multi-GPU. |
| Transfer queue | Uploads stay on graphics queue in v1? |
| Validation policy | Fail on warnings or only errors in CI. |
| Indexed path | How `indices` for `TriangleStrip`/`Triangles` will be uploaded/drawn. |

## 16. Current status

| Area | Current | Needed |
| --- | --- | --- |
| `Bork2d.Render` | Empty | Full renderer |
| Vulkan instance/surface | None | M6 |
| Device/swapchain | None | M6 |
| Offscreen mode | None | M6 |
| Dynamic rendering | None | M6 |
| Snapshot consumption | None | M7 |
| Sprite pipeline/assets | None | M8 |
| Bindless textures | None | M8 |
| Shader build | None | M8 |
| Asset upload | None | M8 |
| Headless | None | M6/M7 |

## 17. Related documents

- [Threading and frame handoff](threading.md) — snapshot ring, bounded run-ahead, fences.
- [Assets and pack files](assets.md) — texture descriptors and sprite assets.
- [Runtime lifecycle](runtime-lifecycle.md#2-modes) — windowed/offscreen/headless modes.
- [API guide](../api/guide.md#5-cameras) — camera sizes/viewports.
- [Roadmap](../roadmap.md) — M6 (bring-up), M7 (handoff), M8 (sprites).
- [Decision log](../decisions.md#d-009-vulkan-13-instancing-bindless-dynamic-rendering) —
  rendering decisions.
