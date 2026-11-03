# Render

`Bork2d.Render` is the Vulkan backend. It exposes no public types: the host composes it through the
internal `IRenderer` contract and `RendererStartInfo`. Its only project reference is
`Bork2d.Engine`; it never references SDL or bake tooling.

## Threading and lifecycle

One render thread owns the Vulkan device, swapchain or offscreen target, pipelines, texture pages,
and the snapshot ring. `Start()` creates the thread and returns immediately. The thread selects a
device, creates resources, and marks itself ready; the host waits for readiness before the first
tick. `RequestStop()` is a request, and `Join()` guarantees no renderer thread survives.

- Two frames are in flight; each completes from its fence.
- With no `Ready` snapshot the thread blocks on the combined snapshot/control wait and never repeats
  the previous frame.
- A snapshot slot stays held until the consuming frame's fence signals, then returns to the ring.
- While minimized or at zero extent, the windowed renderer frees the newest snapshot without GPU
  work.
- Steady-state frames allocate nothing, log nothing, and never call `vkDeviceWaitIdle`.
  `vkDeviceWaitIdle` is allowed only during recreation and shutdown; texture-page capacity growth
  retires every in-flight frame fence instead.

Windowed and offscreen compose the same internal services instead of duplicating them:

- `RendererLifecycle` owns the render thread and start-once guard, the readiness flag/event, status,
  stop reason, the one-shot fatal, progress signaling, the control channel, the snapshot
  ring/consumer/combined wait, the identical `IRenderer` handoff/counter delegations, the stats
  board, and the post-join ring drain.
- `RendererCaptureService` owns the bounded capture queue, per-frame capture retirement from the
  frame fence, and the stop/fatal close path.
- `RendererTextures` owns the sprite pipeline per output format, the `VulkanTexturePages` GPU seam,
  the `TexturePageStore`, and bindless page writes/growth across the frames.
- `RenderPassRegistry` owns the ordered render passes and the aggregate frame statistics; both
  target drivers create one during startup and call it from their own frame loop, so a pass is added
  by registration rather than by editing a central plan/record switch.
- `IVulkanFrame`/`VulkanFrameSet<TFrame>` is the small seam that lets the shared services operate on
  both frame types without moving target-specific recording into them.

The two target drivers stay separate: `VulkanRenderer` owns swapchain acquire/present, resize,
recovery, and the minimized drain; `VulkanOffscreenRenderer` owns the fixed sRGB target, its
one-time layout transition, and the discard path. There is no renderer inheritance hierarchy.

Useful files: [IRenderer.cs](../src/Bork2d/Bork2d.Render/IRenderer.cs),
[SnapshotRing.cs](../src/Bork2d/Bork2d.Render/Handoff/SnapshotRing.cs),
[RendererLifecycle.cs](../src/Bork2d/Bork2d.Render/Vulkan/RendererLifecycle.cs),
[RendererCaptureService.cs](../src/Bork2d/Bork2d.Render/Vulkan/RendererCaptureService.cs),
[RendererTextures.cs](../src/Bork2d/Bork2d.Render/Vulkan/RendererTextures.cs),
[RenderPassRegistry.cs](../src/Bork2d/Bork2d.Render/Vulkan/RenderPassRegistry.cs),
[SpriteRenderPass.cs](../src/Bork2d/Bork2d.Render/Sprites/SpriteRenderPass.cs),
[SpriteInstance.cs](../src/Bork2d/Bork2d.Render/Sprites/SpriteInstance.cs),
[GlobalUniformBlock.cs](../src/Bork2d/Bork2d.Render/Sprites/GlobalUniformBlock.cs),
[ShaderStore.cs](../src/Bork2d/Bork2d.Render/Shaders/ShaderStore.cs),
[VulkanFrameSet.cs](../src/Bork2d/Bork2d.Render/Vulkan/VulkanFrameSet.cs),
[VulkanRenderer.cs](../src/Bork2d/Bork2d.Render/Vulkan/VulkanRenderer.cs),
[VulkanOffscreenRenderer.cs](../src/Bork2d/Bork2d.Render/Vulkan/VulkanOffscreenRenderer.cs).

## Device and presentation

A device must support Vulkan 1.3 and the full required feature set: dynamic rendering,
synchronization 2, runtime descriptor arrays, non-uniform sampled-array indexing, partially bound
descriptors, sampled-image update-after-bind, update-unused-while-pending, and variable descriptor
counts. The device must also report a `maxUniformBufferRange` of at least the 144-byte global block;
a smaller value is a device-selection failure with a clear message. The device's
`minUniformBufferOffsetAlignment` sets the per-frame global-block stride. Windowed selection
additionally requires a graphics queue family that presents to the surface. Discrete GPUs are
preferred over integrated, then other types.

Windowed output uses an sRGB surface format (`B8G8R8A8_SRGB`, then `R8G8B8A8_SRGB`) with
`SRGB_NONLINEAR`; offscreen uses an sRGB color-attachment format (`R8G8B8A8_SRGB`, then
`B8G8R8A8_SRGB`). There is no UNORM fallback. Present mode is `FIFO` when vsync is requested;
otherwise `MAILBOX`, then `IMMEDIATE`, then `FIFO`. The swapchain asks for `minImageCount + 1`
images, clamped to the surface maximum, and is recreated on extent/format changes and
out-of-date results. One surface-recovery attempt is available per run.

## Frame planning

The render thread consumes one snapshot and plans it through the registered render-pass list.
`RenderPassRegistry.Plan` resets the aggregate statistics and each pass's per-frame state, then runs
every pass in registration order; recording runs the same passes in the same order, so pass order is
deterministic. The sprite pass is the only registered pass today; a new renderable kind adds a typed
engine producer and one pass, not a central plan/record switch.

The sprite pass plans its data in one allocation-free pass:

1. Sort sprite indices by ascending `(Layer, ZIndex)`.
2. Classify each sprite once from its frozen render data: a null sprite, zero/multi-bit layer,
   non-`Quad` topology, a still-loading main texture page, or a still-loading custom shader is
   skipped; malformed fields are fatal. The custom shader binding resolves through the shader
   catalog: a loading entry skips the sprite, a failed or invalid entry draws with the built-in
   default, and a ready entry supplies the run identity and its frozen declarations. Additional
   textures resolve positionally against that entry's declared slots: a pending extra page skips the
   whole sprite, a failed or absent page uses the bindless placeholder index `0`, references beyond
   four are ignored with a once-per-renderer warning, and paths beyond the declared slot count are
   never loaded. The sprite's quad geometry is resolved from the frozen UVs and per-axis world scale,
   and its camera-independent 176-byte record is built once: the snapshot's four instance-uniform
   lanes (or the default entry's declared default block for a sprite without a custom shader), the
   resolved extra texture indices, and the resolved main texture index. The sorted index is compacted
   into a per-layer range: each single-bit layer maps to one of the 32 mask bits, and each range keeps
   its ascending `ZIndex` order.
3. Sort cameras by `ZIndex` and resolve their projections; invalid cameras are skipped.
4. One camera×sprite visibility pass builds each camera's contiguous visible-index range and groups
   its visible sprites into contiguous shader runs: two adjacent visible sprites whose resolved
   shader entries are equal share a run, and a different entry starts a new run without reordering
   any sprite. Only the layer bits set in the camera's culling mask are visited, and a sprite whose
   known world AABB does not intersect the camera's world AABB is skipped.
5. Validate the frame's accumulated instance total against `int.MaxValue`, ensure the frame's
   dynamic uniform buffer holds one 144-byte global block per run, write each run's block, grow the
   instance buffer once, and copy the precomputed instance records into their per-camera ranges.

Bounds culling is always on and conservative. The camera's world AABB is the AABB of its four
visible world-space corners, so a rotated camera culls with the larger axis-aligned box around its
rotated rectangle; a sprite's world AABB is the AABB of its four drawn quad corners, including the
pivot, flips, and any world-matrix rotation, mirror, shear, or non-uniform scale. There is no
spatial index and no culling toggle. A culled sprite is a normal visibility skip, never a
malformed-field fatal, and it is not counted in `RenderStats.SkippedSprites`; it only reduces the
instance count.

A captured sprite carries the immutable `SpriteRenderData` frozen when its asset was assigned, never
the live `SpriteAsset`, so mutating an asset after assignment cannot change a plan or a published
snapshot. Each camera gets one dynamic-rendering pass. It records exactly one
`vkCmdDraw(4, run.InstanceCount, 0, run.FirstInstance)` per contiguous shader run, so a camera whose
sprites all share `shader/sprite` is exactly one run and one draw, while a camera that alternates
custom and default shaders records one draw per contiguous group and never reorders sprites. A quad
is a four-vertex triangle strip with no vertex buffer: `gl_VertexIndex` selects the corner, and the
vertex shader builds the world position from the instance matrix, pivot, size, and UVs. The quad is
`(maxU - minU) * Scale.X` by `(maxV - minV) * Scale.Y` world units; texture pixel dimensions are
never used.

Every recorded frame starts with one full-target opaque-black clear after the target's initial
layout transition and before any camera pass: linear `(0, 0, 0, 1)`, `LoadOp = Clear`,
`StoreOp = Store`, one dynamic-rendering color attachment, and no depth/stencil. The clear covers the
whole target (the swapchain image extent for windowed, the offscreen target extent for offscreen),
so every pixel has a defined current-frame value. Camera clear semantics are layered on top:
`Clear.Color` clears the camera's pixel rect to `ClearColor`; `Clear.None` and `Clear.Depth` load the
existing content, which the frame clear has already defined. The frame clear is not a camera, not a
draw call, and not a registered pass: the target driver records it before the registry records the
passes, so `RenderStats` and draw-call/instance accounting are unchanged. There is no depth
attachment.

## Textures

Texture pages are keyed by canonical `TextureDescriptor` path within the current asset catalog.
Index `0` is a placeholder page; sprites with an absent texture reference draw with it. The table
starts at the startup capacity: the device's bindless limit clamped to the 256-entry base capacity
(placeholder plus 255 pages) and the 4096-entry startup reservation. A normal run therefore starts
at 4096 entries and does not grow at 256/512. The reservation is an internal
`RendererStartInfo`/`Bork2dAppOptions` seam (default 4096), not a public builder option. When the
table is full the capacity doubles, up to the smaller of the device's per-stage and per-set
update-after-bind sampled-image limits (capped at 1,048,576). Growth waits every in-flight frame
fence before recreating each frame's descriptor pool/set, so the device is never idled for growth
and no descriptor set is recreated while a frame using it is in flight. Reaching the limit is a
fatal `Bork2dException`; pages are never silently dropped.

A page moves through `Pending -> Uploading -> Ready`, or `Failed`:

- Loading is asynchronous through the app's `IAssets`; the render thread never waits on it.
- Pending pages are skipped for that frame. A validated page stays `Pending` while it waits in the
  upload queue.
- The first successful load for a path wins within the current catalog; later descriptors reuse the
  page.
- Failed paths draw the placeholder and are not retried until the catalog changes.

The page store observes an internal catalog-version seam (the asset service's mounted pack count,
which only increases) from `EnsurePage` and `Pump`. When a later overlay mount changes it, the store
retires the current page map, pending loads, queued uploads, and failed-path state. The next
`EnsurePage` for a path starts a fresh load from the new winning entry, so an overlay-mounted path
reloads and a previously failed path is retried. Uploads that were already submitted finish so their
staging and command resources are released, but they never become visible or count as ready; a
retired in-flight upload joins the retirement list when it completes. Retired pages keep their image
in GPU memory but are no longer counted as resident until the residency policy reclaims them.

Completed descriptor/payload loads are content-validated and queued for upload; `Pump` starts at most
the per-frame upload budget (default 128 MiB, `WithTextureUploadBudget`) and charges
`width * height * 4` bytes per page. The rest stay queued for later frames. The first queued upload
of a frame always starts even when it alone exceeds the budget, so one large texture cannot starve
forever; later uploads start only while the frame total fits. Queued pages seen most recently by
`EnsurePage` start first. Because the planner marks visibility during a frame and the next `Pump`
plans the queue, that priority is a next-frame ordering, not a same-frame guarantee; equal markers
fall back to first-sighting order.

The builder's texture residency budget (default 1 GiB, `WithTextureResidencyBudget`) bounds the
declared payload bytes (`width * height * 4`) of the current catalog's ready pages. `Pump` enforces it
before starting uploads: when the total exceeds the budget it evicts ready pages least recently seen
first, with first-sighting order breaking ties, until the total fits. A page is reclaimable only once
its last-seen frame is more than two frame pumps behind the current pump
(`frameSequence - lastSeenFrame > 2`). Two GPU frames are in flight, so a page last seen by the
previous pump or the one before it can still be referenced by a frame the GPU has not finished; the
third-oldest last sighting is the first that is guaranteed complete. If every ready page is that
recent, the over-budget set is kept and eviction is retried on a later pump, so no in-flight frame is
ever left sampling a freed page. The placeholder is never a candidate, and a page whose upload
completes after the check may leave the total over budget until the next pump. An evicted path leaves
the page map, so its next `EnsurePage` starts a fresh load exactly like a path first seen after a
mount.

Reclaiming a page (a budget eviction or a retired catalog page past the safety age) returns its
bindless index to a free list and destroys its view and image; the image-memory range goes back to
the image pool, whose per-block free ranges coalesce and are reused by later uploads. A new upload
takes a freed index before the page table grows, so a churning catalog reuses descriptor slots and
image memory instead of doubling the table. Retired pages use the same safety clock but never count
as resident. `Destroy` frees every resident and retired range and stays idempotent and non-throwing.
Each eviction and retired-page reclamation logs at Debug with the path/index/bytes and the current
resident totals.

## Pipeline and shaders

The sprite pipeline uses descriptor set `0` for the draw inputs and descriptor set `1` for the
per-run global block:

| Set | Binding | Descriptor | Stage | Contents |
| --- | --- | --- | --- | --- |
| `0` | `0` | storage buffer | vertex | per-frame array of 176-byte instance records |
| `0` | `1` | combined image sampler array | fragment | bindless texture pages, partially bound with a variable count |
| `1` | `2` | dynamic uniform buffer | fragment | per-run global block: `vec4 time; vec4 globals[8]`, 144 bytes, std140 |

Set `0` keeps the variable/UAB bindless array as its highest binding, and the dynamic global buffer
is in its own set because Vulkan forbids a dynamic uniform buffer in a set that contains an
update-after-bind binding. The pipeline layout carries descriptor sets `0` and `1` and one 64-byte
view-projection push constant. The graphics pipeline uses sRGB color attachments, dynamic
viewport/scissor, triangle strip, no vertex input, no depth test or write, culling disabled, and
straight-alpha blending. One graphics pipeline exists per `(shader content hash, output format)`:
identical shader content shares its module and its pipelines, a swapchain format change lazily
creates that format's pipeline, and samplers are cached per `(Filter, Wrap)`.

The instance record is 176 bytes and eleven `vec4` values with no padding: `transform0`
(M11, M12, M21, M22), `transform1` (M31, M32, signed sizeX, signed sizeY), `uv0` (top-left,
top-right), `uv1` (bottom-right, bottom-left), `tint` (linear RGBA), `misc` (pivotX, pivotY, main
texture index, reserved flags = 0), `extraTextureIndices` (the four resolved additional bindless
texture indices as floats, with `0` for an absent or failed page), and `instanceParams0` through
`instanceParams3` (the 16-float custom-shader instance uniform block copied from the snapshot; a
sprite without a custom shader uses the default entry's declared default block). There is no vertex
buffer: `gl_VertexIndex` selects the quad corner and `gl_InstanceIndex` selects the record. The
built-in vertex stage writes these interpolants: location `0` `vUv` (`vec2`), `1` `vTint` (`vec4`),
`2` `vTextureIndex` (`flat uint`), `3` `vWorldPosition` (`vec2`), `4` `vLocalPosition` (`vec2`),
`5` `vExtraTextureIndices` (`flat uint[4]`), and `9` `vInstanceParams` (`vec4[4]`). Custom fragment
shaders declare the same interface through the fixed bake prologue, which places the bindless
texture array in set `0` binding `1` and the global block in set `1` binding `2`. The bake prologue
renames the user `main` and appends an engine wrapper that consumes the full vertex interface in a
branch that cannot be reached for valid engine data, so every custom shader's pipeline validation
sees matched inputs and outputs without changing any frame's pixels.

The default sprite shader is asset content, not a compiled-in array. The built-in pack, mounted
below every game pack, serves `shader/sprite` as a `ShaderAsset`, `shader/sprite-spv` as its raw
fragment module, and `shader/vertex-spv` as the engine-owned vertex module. At renderer startup
`RendererTextures.EnsurePipeline` loads the vertex module through `IAssets`; `ShaderStore` then
loads the fragment shader asset, validates a non-null `SpirV` reference, `LayoutVersion` 2, the
`main` entry point, the full declaration set, and a non-empty module whose first little-endian word
is `0x07230203`, freezes the shader declarations, and creates the fragment module. The load is
synchronous on the render thread before the renderer becomes ready. A missing built-in pack or
shader, a malformed asset, or a module without the SPIR-V magic is a fatal startup
`Bork2dException` naming the asset path and reason; there is no fallback shader. The shader catalog
owns every fragment module and the pipeline owns the vertex module and every
`(content hash, format)` pipeline.

There is no build-time C# shader generator and no generated SPIR-V array. The retired
`Bork2d.ShaderCompiler` tool, its targets import, and the checked-in renderer GLSL sources were
removed; the built-in shader sources live only under
[src/Bork2d/BuiltInAssets/Assets](../src/Bork2d/BuiltInAssets/Assets) and are baked into the
embedded pack by the ordinary bake tooling. The runtime does no shader compilation or source I/O; it
only loads the baked SPIR-V from the asset packs.

### Custom shader catalog

A sprite with `SpriteSnapshot.ShaderData == null` uses the default entry. A custom entry is keyed by
its raw SPIR-V `DataRef` path because the sprite already carries the frozen `ShaderRenderData`
metadata; the renderer never loads the `ShaderAsset` object per draw and never parses GLSL or
reflection. The first frozen declaration set seen for a path supplies that entry's instance/global
uniform declarations and texture slots.

On first sighting in the current catalog the store starts one asynchronous `IAssets.LoadData` load for
that path. While the load is outstanding the sprite is skipped and counted in
`RenderStats.SkippedSprites`. A load started while planning a frame never completes that frame: the
store promotes completed loads on the next frame's pump, creates the fragment module, and creates
that frame's format pipeline at plan time. The render thread never blocks on an asset load.

Every custom declaration set is validated: `LayoutVersion` 2, the `main` entry point, a non-empty
SPIR-V payload whose first little-endian word is `0x07230203`, at most four texture slots, identifier
names that are unique within their block, instance-uniform offsets that are aligned, inside the
16-float block, and non-overlapping, global-uniform offsets that are aligned, unique, and inside the
eight-`vec4` slots, and no name declared in both blocks. Global names are engine-wide and track
every live shader that declares them: shaders of the same type may share a name, the name stays
claimed while any owner is live, and a later shader that declares it with a different type is
rejected. A failed load, invalid
metadata, module creation failure, or pipeline creation failure draws with `shader/sprite` and logs
one warning per shader path; a failure of the default shader itself is a fatal startup error.
Modules are cached by shader content hash and pipelines by `(content hash, output format)`, so
identical content shares both. A failed path is not retried until the catalog changes.

### Global block and user values

The frame's global block is written once per shader run, not once per frame: `time.x` is the frame's
`ITime.Time`, the other time components are zero, and each run binds set `1` at that run's block byte
offset (`blockIndex * BlockStride`). The planner ensures the frame's dynamic uniform buffer holds one
block per run before writing. For each declared global uniform the run uses the engine-wide value
captured for its ordinal name when the type matches, or the declaration's own shader default when the
engine has no such name. A type mismatch between an engine value and a custom shader declaration
makes that shader fall back to the default entry with a one-time warning; the bytes are never
reinterpreted. The instance uniform block is the sprite's frozen packed lanes, or the default entry's
declared default block for a sprite without a custom shader.

### Catalog remount and retirement

The catalog observes the asset catalog version once per frame, in the same pump that advances the
texture pages. A later mount retires every custom entry, clears the engine-wide global-name registry,
and starts a replacement default load. The previous ready default entry stays active until the
replacement validates, so the default path never loses its shader; once the replacement is ready the
old entry is retired after the safety age. A previously failed path is retried on its next sighting
in the new catalog. A retired entry's modules and pipelines are destroyed only after every frame that
could reference them has completed: the same two-frames-in-flight rule as texture pages, so a module
or pipeline outlives its last planned frame by more than two pumps. A failed default replacement is
warned once and the previous default stays active.

### Extra textures

`ShaderRenderData.TextureSlots` declares the additional textures a custom shader samples, up to four.
The renderer resolves at most four and at most the declared slot count positionally from
`SpriteSnapshot.ExtraTexture0..3` through the same asynchronous page state machine as the main
texture. A pending extra page skips the whole sprite for the frame; a failed or absent page uses the
bindless placeholder index `0`; paths beyond the declared slot count are never loaded. A sprite that
carries more than four references logs one warning per renderer and the extra references are ignored.
The resolved indices reach the shader through `vExtraTextureIndices` / the `BORK2D_TEXTURE0` through
`BORK2D_TEXTURE3` macros.

## Windowed versus offscreen

Both target drivers compose the same lifecycle coordinator, capture service, and texture services;
only the target driver differs.

| | Windowed | Offscreen |
| --- | --- | --- |
| Target | Swapchain image | Two permanent sRGB images |
| Presentation | Acquire, render, present | Render, discard |
| Input/surface | SDL surface handles supplied by the host | None |
| Minimized behavior | Snapshots drained without GPU work; tick cap in the host | Not applicable |
| Frame capture | Before present | Before restoring the attachment layout |

## Diagnostics and capture

The renderer publishes an immutable `RenderStats` for the last completed frame: sequence,
framebuffer size, draw calls, instances, cameras, non-empty cameras, texture pages/capacity, texture
uploads pending (count and declared bytes), skipped sprites, and cumulative validation
errors/warnings. `DrawCalls` counts the frame's shader runs, one `vkCmdDraw` each, so a default-only
frame records one draw per non-empty camera while a camera with contiguous custom/default groups
records one per run; `NonEmptyCameras` counts the cameras with at least one run.
`TextureUploadsPending` counts content-ready pages the frame's pump left queued by
the upload budget, and `TextureUploadBytesPending` is their declared payload total. `TexturePages` is
the resident page count (the placeholder plus ready pages in the current catalog); an evicted page
leaves it and reappears once its next sighting reloads it. The host installs it as
`IEngine.RenderStats`; reading it never touches Vulkan. `SkippedSprites` counts the null,
invalid-layer, non-`Quad`, and still-loading sprites, including a loading custom shader, main texture,
or declared additional texture; a sprite drawn with the placeholder, the default-shader fallback, or
an ignored fifth additional reference is not skipped. Bounds-culled sprites are not skipped, they
only reduce `Instances`.

`CaptureFrameAsync` queues a one-shot request through the shared `RendererCaptureService`. The copy
is recorded after the last camera pass and before present/discard, so it includes the frame clear and
every camera and excludes window chrome.
The result is a `FrameCaptureResult` whose pixels are row-major, top-left-origin RGBA8, whose
dimensions are the captured frame's actual extent, and whose format is always `TextureFormat.Rgba8`.
The render thread swizzles/copies the staging bytes into one `byte[]` per captured frame, and every
request served by that frame shares the same immutable memory; staging memory itself is pooled per
GPU frame.

A submitted capture completes from its frame's fence, even when the game never publishes another
snapshot. At the top of each render loop, before waiting for a new snapshot or draining a minimized
frame, the capture service waits on every in-flight frame that carries capture requests, marks its
instance resources fence-signaled, and completes those requests. It does not release the snapshot slot early:
the slot stays held until the frame is reused or shutdown. At most eight requests may be outstanding
at once, including requests already taken by an in-flight frame; the ninth and later requests fault
with `Bork2dException` instead of blocking the caller or being dropped silently. Taking a frame's
requests does not free capacity: a slot returns only when its request completes, faults, or is
cancelled. Cancellation is checked before that limit, a cancelled token faults the task, and a stop or
fatal faults all pending requests.

A capture needs a GPU frame, and a GPU frame needs a published snapshot. Waiting for the task
synchronously inside `Setup` or `Tick` before the snapshot that serves the request is published can
block the only producer that can publish it, so request the capture there and await it from a worker
or a later callback.

Renderer failures are one-shot: the host observes `FatalException` at a tick boundary, no further
tick starts, and cleanup is best-effort, idempotent, and non-throwing.
