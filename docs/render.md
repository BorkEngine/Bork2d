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
[VulkanFrameSet.cs](../src/Bork2d/Bork2d.Render/Vulkan/VulkanFrameSet.cs),
[VulkanRenderer.cs](../src/Bork2d/Bork2d.Render/Vulkan/VulkanRenderer.cs),
[VulkanOffscreenRenderer.cs](../src/Bork2d/Bork2d.Render/Vulkan/VulkanOffscreenRenderer.cs).

## Device and presentation

A device must support Vulkan 1.3 and the full required feature set: dynamic rendering,
synchronization 2, runtime descriptor arrays, non-uniform sampled-array indexing, partially bound
descriptors, sampled-image update-after-bind, update-unused-while-pending, and variable descriptor
counts. Windowed selection additionally requires a graphics queue family that presents to the
surface. Discrete GPUs are preferred over integrated, then other types.

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
   non-`Quad` topology, or a still-loading texture page is skipped; malformed fields are fatal.
3. Ensure each sprite's texture page and resolve quad geometry from the frozen UVs and per-axis
   world scale for drawable sprites.
4. Sort cameras by `ZIndex` and resolve their projections; invalid cameras are skipped.
5. Filter by each camera's culling mask, then cull by bounds: a sprite whose known world AABB does
   not intersect the camera's world AABB is skipped for that camera. Empty sprite bounds mean
   "unknown" and are never culled.
6. Assign contiguous per-camera instance ranges, grow the instance buffer once, and write the
   instance records.

Bounds culling is always on and conservative. The camera's world AABB is the AABB of its four
visible world-space corners, so a rotated camera culls with the larger axis-aligned box around its
rotated rectangle; a sprite's world AABB is the AABB of its four drawn quad corners, including the
pivot, flips, and any world-matrix rotation, mirror, shear, or non-uniform scale. There is no
spatial index and no culling toggle. A culled sprite is a normal visibility skip, never a
malformed-field fatal, and it is not counted in `RenderStats.SkippedSprites`; it only reduces the
instance count.

A captured sprite carries the immutable `SpriteRenderData` frozen when its asset was assigned, never
the live `SpriteAsset`, so mutating an asset after assignment cannot change a plan or a published
snapshot. Each non-empty camera gets one dynamic-rendering pass and exactly one
`vkCmdDraw(4, instanceCount, 0, firstInstance)`. A quad is a four-vertex triangle strip with no
vertex buffer: `gl_VertexIndex` selects the corner, and the vertex shader builds the world position
from the instance matrix, pivot, size, and UVs. The quad is `(maxU - minU) * Scale.X` by
`(maxV - minV) * Scale.Y` world units; texture pixel dimensions are never used.

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

The sprite pipeline uses descriptor set `0` with binding `0` (per-frame instance storage buffer)
and binding `1` (bindless combined image sampler array). The layout carries one 64-byte
view-projection push constant. Graphics state: sRGB color attachment, dynamic viewport/scissor,
triangle strip, no vertex input, no depth test or write, culling disabled, and straight-alpha
blending. One graphics pipeline exists per output format; samplers are cached per
`(Filter, Wrap)`.

The GLSL sources are
[sprite.vert](../src/Bork2d/Bork2d.Render/Shaders/sprite.vert) and
[sprite.frag](../src/Bork2d/Bork2d.Render/Shaders/sprite.frag). They are compiled to SPIR-V during
the build by `Bork2d.ShaderCompiler` and embedded as generated C# byte arrays; no Vulkan SDK is
required, and the runtime does no shader I/O or compilation. The generated file lives under the
intermediate output, never in the repository or publish output.

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
errors/warnings. `TextureUploadsPending` counts content-ready pages the frame's pump left queued by
the upload budget, and `TextureUploadBytesPending` is their declared payload total. `TexturePages` is
the resident page count (the placeholder plus ready pages in the current catalog); an evicted page
leaves it and reappears once its next sighting reloads it. The host installs it as
`IEngine.RenderStats`; reading it never touches Vulkan. `SkippedSprites` counts the null,
invalid-layer, non-`Quad`, and still-loading sprites; bounds-culled sprites are not skipped, they
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
