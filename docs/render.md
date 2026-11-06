# Render

`Bork2d.Render` is the Vulkan backend. It exposes no public types: the host composes it through the
internal `IRenderer` contract and `RendererStartInfo`. Its only project reference is
`Bork2d.Engine`; it never references SDL or bake tooling.

## Threading and lifecycle

One render thread owns the Vulkan device, swapchain or offscreen target, the per-frame linear scene
images, pipelines, texture pages, and the snapshot ring. `Start()` creates the thread and returns
immediately. The thread selects a device, creates resources, and marks itself ready; the host waits
for readiness before the first tick. `RequestStop()` is a request, and `Join()` guarantees no
renderer thread survives.

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
- `RendererTextures` owns the 2D pipeline for the linear scene format, the
  `VulkanTexturePages` GPU seam, the `TexturePageStore`, and bindless page writes/growth across the
  frames.
- `RenderPassRegistry` owns the ordered render passes and the aggregate frame statistics; both
  target drivers create one during startup and call it from their own frame loop, so a pass is added
  by registration rather than by editing a central plan/record switch.
- `IVulkanFrame`/`VulkanFrameSet<TFrame>` is the small seam that lets the shared services operate on
  both frame types without moving target-specific recording into them.

The two target drivers stay separate: `VulkanRenderer` owns swapchain acquire/present, resize,
recovery, and the minimized drain; `VulkanOffscreenRenderer` owns the fixed UNORM target, its
one-time layout transition, and the discard path. Each driver creates its frames' linear scene
images, the 2D pipeline for the scene format, and the present pipeline for the target format.
There is no renderer inheritance hierarchy.

Useful files: [IRenderer.cs](../src/Bork2d/Bork2d.Render/IRenderer.cs),
[SnapshotRing.cs](../src/Bork2d/Bork2d.Render/Handoff/SnapshotRing.cs),
[RendererLifecycle.cs](../src/Bork2d/Bork2d.Render/Vulkan/RendererLifecycle.cs),
[RendererCaptureService.cs](../src/Bork2d/Bork2d.Render/Vulkan/RendererCaptureService.cs),
[RendererTextures.cs](../src/Bork2d/Bork2d.Render/Vulkan/RendererTextures.cs),
[RenderPassRegistry.cs](../src/Bork2d/Bork2d.Render/Vulkan/RenderPassRegistry.cs),
[TwoDimensionalRenderPass.cs](../src/Bork2d/Bork2d.Render/TwoDimensional/TwoDimensionalRenderPass.cs),
[TwoDimensionalFramePlanner.cs](../src/Bork2d/Bork2d.Render/TwoDimensional/TwoDimensionalFramePlanner.cs),
[TwoDimensionalPipeline.cs](../src/Bork2d/Bork2d.Render/TwoDimensional/TwoDimensionalPipeline.cs),
[PresentRenderPass.cs](../src/Bork2d/Bork2d.Render/Vulkan/PresentRenderPass.cs),
[PresentPipeline.cs](../src/Bork2d/Bork2d.Render/Vulkan/PresentPipeline.cs),
[SceneTarget.cs](../src/Bork2d/Bork2d.Render/Vulkan/SceneTarget.cs),
[SpriteInstance.cs](../src/Bork2d/Bork2d.Render/Sprites/SpriteInstance.cs),
[LineVertex.cs](../src/Bork2d/Bork2d.Render/TwoDimensional/LineVertex.cs),
[GlobalUniformBlock.cs](../src/Bork2d/Bork2d.Render/Sprites/GlobalUniformBlock.cs),
[ShaderStore.cs](../src/Bork2d/Bork2d.Render/Shaders/ShaderStore.cs),
[VulkanFrameSet.cs](../src/Bork2d/Bork2d.Render/Vulkan/VulkanFrameSet.cs),
[VulkanRenderer.cs](../src/Bork2d/Bork2d.Render/Vulkan/VulkanRenderer.cs),
[VulkanOffscreenRenderer.cs](../src/Bork2d/Bork2d.Render/Vulkan/VulkanOffscreenRenderer.cs).

## Device and presentation

A device must support Vulkan 1.3 and the full required feature set: dynamic rendering,
synchronization 2, shader demote-to-helper-invocation, runtime descriptor arrays, non-uniform
sampled-array indexing, partially bound descriptors, sampled-image update-after-bind,
update-unused-while-pending, variable descriptor counts, and `textureCompressionBC`.
`textureCompressionBC` is a desktop Vulkan 1.3 requirement: every baked BC texture must sample
natively, and there is no RGBA fallback or runtime transcoder, so a device without it fails selection
with a message naming the feature and that requirement. `shaderDemoteToHelperInvocation` is required
for fragment shaders that use `discard`/demote: the bake emits the `DemoteToHelperInvocation` SPIR-V
capability for such a source (the Pong court's dashed center line is one), so a device without the
feature fails selection with a message naming the feature and that requirement. The device must also
report a `maxUniformBufferRange` of at least the 144-byte global block; a smaller value is a
device-selection failure with a clear message. The device's `minUniformBufferOffsetAlignment` sets the
per-frame global-block stride. Windowed selection additionally requires a graphics queue family that
presents to the surface. Discrete GPUs are preferred over integrated, then other types.

Windowed output uses a UNORM surface format (`B8G8R8A8_UNORM`, then `R8G8B8A8_UNORM`) with
`SRGB_NONLINEAR`; offscreen uses a UNORM color-attachment format (`R8G8B8A8_UNORM`, then
`B8G8R8A8_UNORM`). There is no sRGB target fallback: the color pipeline is linear end to end, every
2D pass writes linear color into the scene image, and the final present pass applies the sRGB
opto-electronic transfer function when it writes the UNORM target. Targeting an sRGB image instead
would encode twice and is not offered. Present mode is `FIFO` when vsync is requested;
otherwise `MAILBOX`, then `IMMEDIATE`, then `FIFO`. The swapchain asks for `minImageCount + 1`
images, clamped to the surface maximum, and is recreated on extent/format changes and
out-of-date results. One surface-recovery attempt is available per run.

## Frame planning

The render thread consumes one snapshot and plans it through the registered render-pass list.
`RenderPassRegistry.Plan` resets the aggregate statistics and each pass's per-frame state, then runs
every pass in registration order; recording runs the same passes in the same order, so pass order is
deterministic. Both target drivers register the unified 2D pass followed by the present pass. The 2D
pass renders linear color into the frame's scene image; the present pass samples that image and
writes the sRGB-encoded result into the UNORM swapchain or offscreen target. A new renderable kind
adds a typed engine producer and one pass, not a central plan/record switch.

The 2D pass plans sprites, lines, and fonts together in one allocation-free pass:

1. Pre-pass every captured sprite, line, and font once. A sprite is classified from its frozen
   render data: a null sprite, zero/multi-bit layer, non-`Quad` topology, a still-loading main texture
   page, or a still-loading custom shader is skipped; malformed fields are fatal. A line is skipped
   when it has no drawable geometry, an invalid layer, or a still-loading custom shader. Line
   geometry is validated by the engine at capture time, so a non-drawable line arrives as null
   geometry and the renderer never repairs or approximates it. A font is skipped when it has no
   frozen font data, an invalid layer, a still-loading custom shader, or a still-loading atlas page;
   an absent or empty layout (empty text or spaces only) is a normal no-draw and is not a skip. A
   custom shader binding resolves through the shader catalog for the record's kind: a loading entry
   skips the record, a failed or invalid entry draws with that kind's built-in default
   (`shader/sprite`, `shader/line`, or `shader/sdf`), and a ready entry supplies the run identity and
   its frozen declarations. Additional textures resolve positionally against a sprite entry's
   declared slots: a pending extra page skips the whole sprite, a failed or absent page uses the
   bindless placeholder index `0`, references beyond four are ignored with a once-per-renderer
   warning, and paths beyond the declared slot count are never loaded. Lines and fonts have no main
   or additional textures, so a font's extra texture indices stay zero. Each sprite's
   camera-independent 176-byte instance record is built once here, each font expands into one
   176-byte record per drawable glyph quad (through the same sprite vertex stage and instance
   buffer), and each line keeps its immutable local-space geometry plus its seeded 16-float
   instance-uniform block (the snapshot's lanes, or the line default's declared defaults when the
   line has no custom shader). A font with no custom shader uses `shader/sdf` and seeds that entry's
   declared defaults; a custom font shader uses the font's frozen lanes.
2. Sort cameras by `ZIndex` and resolve their projections; invalid cameras are skipped.
3. Draw order is computed per camera. For each camera, build the candidate list from records whose
   layer bit is in the camera's culling mask and whose non-empty world AABB intersects the camera's
   world AABB, project each record's world-space origin (`WorldMatrix.M31/M32`) to that camera's
   framebuffer pixels — or, for a font, project each glyph's world top-left (the instance's
   `transform1.xy`) — and sort the candidates by `(ZIndex, screen Y, screen X)` ascending.
   `ZIndex` is the shared 2D draw order for sprites, lines, and fonts and is not layer-scoped. Screen
   space is the framebuffer with a top-left origin, so a record lower on screen has a larger Y and
   draws later; this is why a 180-degree rotated camera reverses the order of the same records.
   Glyph records of one font with increasing screen X therefore sort left-to-right. `Layer` is only
   a culling bit and never participates in ordering. Ties at equal `ZIndex`/screen Y/screen X are
   explicitly unspecified: there is no capture-order, kind, or record-index tie-break.
4. Group the sorted records into contiguous draw runs: a run changes only when the renderable kind,
   the resolved shader entry, or (for a font) the font asset changes, so a sprite, line, or font run
   never merges across that boundary even when two records share a shader entry, and two adjacent
   glyph runs from different font assets stay distinct. Within a camera, sprite instances, font glyph
   instances, and line vertices/indices are written into contiguous frame-buffer ranges in draw
   order; sprite and glyph records share one instance buffer, and a record visible to two cameras is
   written twice.
5. Validate the frame's accumulated instance, glyph, line-record, line-vertex, and line-index totals
   against `int.MaxValue`, ensure the shared instance and line buffers once, write one 144-byte global
   block per run, and write the precomputed sprite/glyph instance records and world-transformed line
   vertices/indices in draw order.

Bounds culling is always on and conservative. The camera's world AABB is the AABB of its four
visible world-space corners, so a rotated camera culls with the larger axis-aligned box around its
rotated rectangle; a sprite's world AABB is the AABB of its four drawn quad corners, including the
pivot, flips, and any world-matrix rotation, mirror, shear, or non-uniform scale; a line's world AABB
is the AABB of the four transformed corners of its cached local-space geometry bounds, which already
include half the width and every miter or bevel extent; a font's world AABB is the tight AABB of its
laid-out glyph quads. A font is tested once per font record, not once per glyph. Empty bounds mean
"unknown" and never cull. There is no spatial index and no culling toggle. A culled record is a
normal visibility skip, never a malformed-field fatal, and it is not counted in
`RenderStats.SkippedSprites`, `RenderStats.SkippedFonts`, or `RenderStats.SkippedLines`; it only
reduces the instance/glyph/line counts. A font whose layer matches at least one planned camera but
whose bounds reject every camera counts once in the internal culled-font total.

A captured sprite carries the immutable `SpriteRenderData` frozen when its asset was assigned, a
captured line carries only its immutable `LineGeometryData`, and a captured font carries the frozen
`FontRenderData` and its cached `FontLayoutData`; mutating an asset after capture cannot change a plan
or a published snapshot. Each camera gets one dynamic-rendering pass. Sprite and font runs record
exactly one `vkCmdDraw(4, run.Count, 0, run.First)` per contiguous group from the shared instance
buffer; line runs bind the frame's line vertex/index buffers once per camera and record one
`vkCmdDrawIndexed(run.Count, 1, run.First, 0, 0)` per contiguous group, where the indices are absolute
in the frame's line index buffer. A camera whose sprites all share `shader/sprite` and whose lines all
share `shader/line` records one run per kind plus the frame's present draw, while a camera that
alternates shaders or font assets records one per contiguous group and never reorders records. The
quad is a four-vertex triangle strip with no vertex buffer: `gl_VertexIndex` selects the corner, and
the vertex shader builds the world position from the instance matrix, pivot, size, and UVs. The quad
is `(maxU - minU) * Scale.X` by `(maxV - minV) * Scale.Y` world units; texture pixel dimensions are
never used. A glyph instance uses the quad's world top-left as a `(0, 0)` pivot and its atlas UV
rectangle, so a font adds no glyph buffer, descriptor binding, or vertex shader asset. A line is a
CPU-expanded triangle list; the renderer writes each triangle vertex with the world matrix applied, so
a transform change never rebuilds the cached local geometry.

Every recorded frame starts with one full-target opaque-black clear into the frame's linear scene
image after the scene image's initial layout transition and before any camera pass: linear
`(0, 0, 0, 1)`, `LoadOp = Clear`, `StoreOp = Store`, one dynamic-rendering color attachment, and no
depth/stencil. The scene image is one device-local `R8G8B8A8_UNORM` image per GPU frame in flight with
`COLOR_ATTACHMENT | SAMPLED` usage; its transition discards the previous frame's contents, so the
clear defines every scene pixel. `Clear.Color` clears the camera's pixel rect to `ClearColor` in the
scene image; `Clear.None` and `Clear.Depth` load the existing scene content, which the frame clear has
already defined. The frame clear is not a camera, not a draw call, and not a registered pass: the
target driver records it before the registry records the passes, so the frame clear never changes
`RenderStats` draw-call/instance accounting. There is no depth attachment.

Recording order per frame is: transition the scene image and the final target to
`COLOR_ATTACHMENT_OPTIMAL`; record the full-target scene clear; record the 2D pass into the scene
image; the present pass barriers the scene image to `SHADER_READ_ONLY_OPTIMAL` and records its
fullscreen draw into the final UNORM target; the optional capture copy reads the final target; the
target then transitions to `PRESENT_SRC_KHR` (windowed) or stays a color attachment (offscreen).

## Textures

Texture pages are keyed by canonical `TextureDescriptor` path within the current asset catalog.
Index `0` is the built-in placeholder page; sprites with an absent texture reference draw with it. The
placeholder is ordinary content baked into the embedded pack: a `TextureDescriptor` at
`textures/placeholder` and an uncompressed RGBA8 KTX2 payload at `textures/placeholder-ktx2` with a
full mip chain, nearest filtering, and clamped wrapping. Renderer startup loads both synchronously
from `IAssets`, validates the payload, uploads it, and records the descriptor's dimensions for absent
and failed lookups; a missing or invalid built-in placeholder is a fatal `Bork2dException`, and no
sprite ever samples an incomplete page. The table
starts at the startup capacity: the device's bindless limit clamped to the 256-entry base capacity
(placeholder plus 255 pages) and the 4096-entry startup reservation. A normal run therefore starts
at 4096 entries and does not grow at 256/512. The reservation is an internal
`RendererStartInfo`/`Bork2dAppOptions` seam (default 4096), not a public builder option. When the
table is full the capacity doubles, up to the smaller of the device's per-stage and per-set
update-after-bind sampled-image limits (capped at 1,048,576). Growth waits every in-flight frame
fence before recreating each frame's descriptor pool/set, so the device is never idled for growth
and no descriptor set is recreated while a frame using it is in flight. Reaching the limit is a
fatal `Bork2dException`; pages are never silently dropped.

Every page payload is a complete KTX2 2.0 file. The renderer's internal reader accepts only the narrow
profile the bake writes: the KTX2 identifier, one 2D image with `pixelDepth = 0`, `layerCount = 0`,
`faceCount = 1`, `supercompressionScheme = 0`, absent key/value and supercompression-global-data
sections, `typeSize = 1`, a complete mip chain with the base level first in the level index, and no
alignment or bounds violation. It cross-checks the header `vkFormat` against the descriptor's format
mapping (RGBA8 UNORM `37`, RG8 UNORM `16`, R8 UNORM `9`, BC7 UNORM `145`, BC1 RGB UNORM
`131`, BC5 UNORM `141`, BC4 UNORM `139`), the base dimensions, and the mip count. The DFD must be
inside the payload with `dfdTotalSize == dfdByteLength`, every level entry must be inside the payload
with `byteLength == uncompressedByteLength`, each level's length must equal the format's computed
level size, and each level offset must be aligned to `lcm(texelBlockSize, 4)`. A malformed or
mismatched payload is a content failure for that path (the sprite draws the placeholder); the
built-in placeholder is the only payload whose failure is fatal, because it is required startup
content. Parsing happens on load/upload only, never per frame.

Upload creates one image with the descriptor's concrete Vulkan format and its full mip count, then
copies every KTX2 level from one staging buffer at that level's payload offset. Compressed levels set
`BufferRowLength`/`BufferImageHeight` to the level extent rounded up to the 4x4 block (in texels) and
use the actual level extent as `ImageExtent`; uncompressed levels are tightly packed. One barrier range
covers every mip for `UNDEFINED -> TRANSFER_DST_OPTIMAL` and `TRANSFER_DST_OPTIMAL ->
SHADER_READ_ONLY_OPTIMAL`, and the image view covers all mips. Before creating the image, the renderer
queries the device's format properties: a format without `VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT` on
optimal tiling is a fatal `Bork2dException` naming the format and the `textureCompressionBC`
requirement. There is no fallback format, decode, or silent placeholder for a requested texture the
device cannot sample.

Samplers come from one cache keyed by `(Filter, Wrap)`: the descriptor filter selects nearest or
linear magnification/minification, mip filtering is always linear with `MinLod = 0` and
`MaxLod = VK_LOD_CLAMP_NONE` (1000), and anisotropy is disabled. Every page has a complete chain, so
the maximum LOD clamps to that chain and the key does not need the mip count.

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
the per-frame upload budget (default 128 MiB, `WithTextureUploadBudget`) and charges the validated
KTX2 payload byte length per page. The rest stay queued for later frames. The first queued upload
of a frame always starts even when it alone exceeds the budget, so one large texture cannot starve
forever; later uploads start only while the frame total fits. Queued pages seen most recently by
`EnsurePage` start first. Because the planner marks visibility during a frame and the next `Pump`
plans the queue, that priority is a next-frame ordering, not a same-frame guarantee; equal markers
fall back to first-sighting order.

The builder's texture residency budget (default 1 GiB, `WithTextureResidencyBudget`) bounds the
validated KTX2 payload bytes of the current catalog's ready pages, so a BC page consumes less upload
and residency budget than the equivalent RGBA8 page. `Pump` enforces it
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

The 2D pipeline uses descriptor set `0` for the draw inputs and descriptor set `1` for the
per-run global block:

| Set | Binding | Descriptor | Stage | Contents |
| --- | --- | --- | --- | --- |
| `0` | `0` | storage buffer | vertex | per-frame array of 176-byte sprite and font glyph instance records |
| `0` | `1` | combined image sampler array | fragment | bindless texture pages, partially bound with a variable count |
| `1` | `2` | dynamic uniform buffer | fragment | per-run global block: `vec4 time; vec4 globals[8]`, 144 bytes, std140 |

Set `0` keeps the variable/UAB bindless array as its highest binding, and the dynamic global buffer
is in its own set because Vulkan forbids a dynamic uniform buffer in a set that contains an
update-after-bind binding. The pipeline layout carries descriptor sets `0` and `1` and one 64-byte
view-projection push constant shared by every kind. The pipeline owner owns three engine vertex
modules: `shader/vertex-spv` for the sprite stage, a second module created from those same
`shader/vertex-spv` bytes for the font glyph stage, and `shader/line-vertex-spv` for lines. The font
module is a separate Vulkan handle with identical code and instance ABI; there is no dedicated glyph
buffer, descriptor binding, or engine-owned font vertex asset. All graphics pipelines use the linear
`R8G8B8A8_UNORM` scene color attachment, dynamic viewport/scissor, no depth test or write, culling
disabled, and straight-alpha blending. The sprite and font pipelines have no vertex input and use
triangle-strip topology with four generated quad corners per instance. The line pipeline uses
triangle-list topology and a 112-byte per-vertex stride with attribute locations `0` through `7`
(position, line UV, color, misc, and the four instance-uniform lanes). One graphics pipeline exists
per `(pipeline kind, shader content hash, scene format)`: the scene format is fixed for the whole
run, identical fragment content shares its module but caches separate sprite, line, and font
pipelines, a swapchain format change never creates a new scene-format variant, and samplers are
cached per `(Filter, Wrap)` with linear mip filtering over the full chain and anisotropy disabled.

The instance record is 176 bytes and eleven `vec4` values with no padding: `transform0`
(M11, M12, M21, M22), `transform1` (M31, M32, signed sizeX, signed sizeY), `uv0` (top-left,
top-right), `uv1` (bottom-right, bottom-left), `tint` (linear RGBA), `misc` (pivotX, pivotY, main
texture index, reserved flags = 0), `extraTextureIndices` (the four resolved additional bindless
texture indices as floats, with `0` for an absent or failed page), and `instanceParams0` through
`instanceParams3` (the 16-float custom-shader instance uniform block copied from the snapshot; a
sprite without a custom shader uses the default entry's declared default block). A font glyph uses
the same record: `transform1` is the glyph quad's world top-left and signed quad size, `misc.xy` is
the top-left pivot `(0, 0)`, `misc.z` is the atlas page index, and `uv0`/`uv1` carry the glyph atlas
rectangle in the same corner order. There is no vertex buffer: `gl_VertexIndex` selects the quad
corner and `gl_InstanceIndex` selects the record. The built-in sprite vertex stage writes these
interpolants: location `0` `vUv` (`vec2`), `1` `vTint` (`vec4`), `2` `vTextureIndex` (`flat uint`),
`3` `vWorldPosition` (`vec2`), `4` `vLocalPosition` (`vec2`), `5` `vExtraTextureIndices`
(`flat uint[4]`), `9` `vInstanceParams` (`vec4[4]`), and the line-only `13` `vLineUv` (`vec2`), `14`
`vColor` (`vec4`), and `15` `vLineFlags` (`flat float`), which it leaves at zero. The engine-owned line vertex stage (`shader/line-vertex-spv`) reads a
112-byte vertex record through attribute locations `0` through `7` and writes the complete shared
interface, including neutral sprite values (`vUv` and `vLocalPosition` from the line coordinate,
`vTint` from the line color, texture index `0`, and zero extra indices). Custom fragment
shaders declare the same interface through the fixed bake prologue, which places the bindless
texture array in set `0` binding `1` and the global block in set `1` binding `2`. The bake prologue
renames the user `main` and appends an engine wrapper that consumes the full vertex interface in a
branch that cannot be reached for valid engine data, so every custom shader's pipeline validation
sees matched inputs and outputs without changing any frame's pixels.

The line vertex record is 112 bytes with no padding: `position` (world-space `vec2`), `lineUv`
(`vec2`: `x` is `0` on one width edge and `1` on the other, `y` is the accumulated local distance
along the line), `color` (linear RGBA `vec4`), `misc` (`vec4`; `x` is the anti-alias flag `0`/`1` and
the rest are zero), and `params0` through `params3` (the 16-float instance uniform block). The 2D
planner writes one record per expanded triangle vertex into the frame's host-visible line vertex
buffer with the snapshot world matrix applied, and writes absolute 32-bit vertex indices into the
frame's line index buffer. The per-frame line buffers start at 4,096 vertex records and 6,144
indices; when a frame needs more, each grows independently to
`roundUpToPowerOfTwo(max(required, capacity * 2))` after the frame fence is observed, is checked
against the device's buffer limit, logs the growth, and never truncates. A normal frame does not
grow.

The default sprite shader, the built-in line shader, the built-in font SDF shader, and the bindless
placeholder are asset content, not compiled-in arrays. The built-in pack, mounted below every game
pack, serves `shader/sprite`, `shader/line`, and `shader/sdf` as `ShaderAsset`s, `shader/sprite-spv`,
`shader/line-spv`, and `shader/sdf-spv` as their raw fragment modules, `shader/vertex-spv` and
`shader/line-vertex-spv` as the engine-owned sprite and line vertex modules, `present/vertex-spv`
plus `present/fragment-spv` as the engine-owned present modules, and `textures/placeholder` plus
`textures/placeholder-ktx2` as the bindless placeholder descriptor and KTX2 payload. At renderer
startup `RendererTextures.EnsurePipeline` loads only the sprite and line vertex modules through
`IAssets` and creates the pipeline owner, which derives the font vertex module from the same sprite
vertex bytes; `ShaderStore` then loads the three default fragment shader assets (`shader/sprite`,
`shader/line`, and `shader/sdf`), validates each non-null `SpirV` reference, `LayoutVersion` 3, the
`main` entry point, the full declaration set, and a non-empty module whose first little-endian word
is `0x07230203`, freezes the declarations, and creates all three default fragment modules. The
sprite default is entry `0`, the line default entry `1`, and the font SDF default entry `2`; custom
entries start at `3`. The SDF default is required startup content even when a game has no text: a
missing, malformed, or pipeline-failing `shader/sdf` default is a fatal startup failure like the
other two defaults. `EnsureDefaultPipelines` creates all three default pipelines for the scene
format. The present pipeline loads its two raw modules the same way and validates their SPIR-V
magic. The placeholder descriptor and payload are loaded synchronously and validated before the
renderer becomes ready. A missing built-in pack, default shader, vertex module, or placeholder, a
malformed asset, a module without the SPIR-V magic, or an invalid placeholder payload is a fatal
startup `Bork2dException` naming the asset path and reason; there is no fallback shader. The shader
catalog owns every fragment module and the pipeline owns the three vertex modules and every
`(kind, content hash, scene format)` pipeline.

There is no build-time C# shader generator and no generated SPIR-V array. The retired
`Bork2d.ShaderCompiler` tool, its targets import, and the checked-in renderer GLSL sources were
removed; the built-in shader sources live only under
[src/Bork2d/BuiltInAssets/Assets](../src/Bork2d/BuiltInAssets/Assets) and are baked into the
embedded pack by the ordinary bake tooling. The runtime does no shader compilation or source I/O; it
only loads the baked SPIR-V from the asset packs.

### Present pipeline

The final present pass has its own minimal pipeline, layout, and sampler; it never reuses or changes
the 2D bindless set:

| Set | Binding | Descriptor | Stage | Contents |
| --- | --- | --- | --- | --- |
| `0` | `0` | combined image sampler | fragment | the frame's linear scene color image |

The pipeline layout carries that one set and no push constant. The graphics pipeline targets the
UNORM swapchain or offscreen format, uses no vertex input, triangle-list topology, dynamic
viewport/scissor, no depth test or write, culling disabled, and no blending; it records one
`vkCmdDraw(3, 1, 0, 0)` for a fullscreen triangle. Its sampler is nearest with clamped edges, which
samples the scene image exactly one texel per target pixel. The fragment stage applies the standard
sRGB opto-electronic transfer function to RGB
(`linear <= 0.0031308 ? 12.92 * c : 1.055 * pow(c, 1/2.4) - 0.055`) and passes alpha through, so the
UNORM target receives sRGB-encoded bytes.

The present vertex and fragment stages are engine-owned sources baked into the built-in pack as raw
SPIR-V (`present/vertex-spv`, `present/fragment-spv`) by the build-time built-in-assets tool; they are
not user `.frag` assets. The runtime loads both modules from the built-in pack at startup and creates
the pipeline owner. Pipelines are cached per final target format, so a windowed swapchain format
change creates only that format's present pipeline. Each GPU frame owns a dedicated present
descriptor set (in its frame instance resources) that binds the scene image view with the present
sampler; the frame rewrites it when it creates or recreates its scene image. Steady-state recording
allocates nothing and creates no Vulkan object.

### Custom shader catalog

A sprite, line, or font with a null `ShaderData` uses its kind's default entry (`shader/sprite` for a
sprite, `shader/line` for a line, `shader/sdf` for a font). A custom entry is keyed by its raw SPIR-V
`DataRef` path because the record already carries the frozen `ShaderRenderData` metadata; the
renderer never loads the `ShaderAsset` object per draw and never parses GLSL or reflection. The first
frozen declaration set seen for a path supplies that entry's instance/global uniform declarations and
texture slots.

On first sighting in the current catalog the store starts one asynchronous `IAssets.LoadData` load for
that path. While the load is outstanding the record is skipped and counted in
`RenderStats.SkippedSprites`, `RenderStats.SkippedFonts`, or `RenderStats.SkippedLines`. A load
started while planning a frame never completes that frame: the store promotes completed loads on the
next frame's pump, creates the fragment module, and creates that frame's kind and format pipeline at
plan time. The render thread never blocks on an asset load.

Every custom declaration set is validated: `LayoutVersion` 3, the `main` entry point, a non-empty
SPIR-V payload whose first little-endian word is `0x07230203`, at most four texture slots, identifier
names that are unique within their block, instance-uniform offsets that are aligned, inside the
16-float block, and non-overlapping, global-uniform offsets that are aligned, unique, and inside the
eight-`vec4` slots, and no name declared in both blocks. Global names are engine-wide and track
every live shader that declares them: shaders of the same type may share a name, the name stays
claimed while any owner is live, and a later shader that declares it with a different type is
rejected. A failed load, invalid metadata, module creation failure, or pipeline creation failure
draws with the requesting kind's default (`shader/sprite`, `shader/line`, or `shader/sdf`) and logs
one warning per shader path; a failure of a default shader itself is a fatal startup error. When a
font's custom shader falls back, its glyphs draw with `shader/sdf` and the font is not counted as
skipped. Modules are cached by shader content hash and pipelines by
`(kind, content hash, scene format)`, so identical content shares its module while the sprite, line,
and font pipelines stay distinct; a shader assigned to several kinds therefore caches one pipeline
for each. A failed path is not retried until the catalog changes.

### Global block and user values

The frame's global block is written once per draw run, not once per frame: `time.x` is the frame's
`ITime.Time`, the other time components are zero, and each run binds set `1` at that run's block byte
offset (`blockIndex * BlockStride`). The planner ensures the frame's dynamic uniform buffer holds one
block per run before writing. For each declared global uniform the run uses the engine-wide value
captured for its ordinal name when the type matches, or the declaration's own shader default when the
engine has no such name. A type mismatch between an engine value and a custom shader declaration
makes that shader fall back to its kind's default entry with a one-time warning; the bytes are never
reinterpreted. The instance uniform block is the sprite's frozen packed lanes, the font snapshot's
packed lanes for a custom font shader, the line snapshot's packed lanes for a custom line shader, or
the kind default's declared default block when the record has no custom shader.

### Catalog remount and retirement

The catalog observes the asset catalog version once per frame, in the same pump that advances the
texture pages. A later mount retires every custom entry, clears the engine-wide global-name registry,
and starts a replacement load for each built-in default. A previous ready default entry stays active
until its replacement validates, so a default path never loses its shader; once the replacement is
ready the old entry is retired after the safety age. A previously failed path is retried on its next
sighting in the new catalog. A retired entry's modules and pipelines are destroyed only after every
frame that could reference them has completed: the same two-frames-in-flight rule as texture pages,
so a module or pipeline outlives its last planned frame by more than two pumps. A failed default
replacement is warned once and the previous default stays active.

### Extra textures

`ShaderRenderData.TextureSlots` declares the additional textures a custom shader samples, up to four.
The renderer resolves at most four and at most the declared slot count positionally from
`SpriteSnapshot.ExtraTexture0..3` through the same asynchronous page state machine as the main
texture. A pending extra page skips the whole sprite for the frame; a failed or absent page uses the
bindless placeholder index `0`; paths beyond the declared slot count are never loaded. A sprite that
carries more than four references logs one warning per renderer and the extra references are ignored.
The resolved indices reach the shader through `vExtraTextureIndices` / the `BORK2D_TEXTURE0` through
`BORK2D_TEXTURE3` macros.

### SDF shader

`shader/sdf` is the built-in font SDF fragment shader and the third renderer startup default (entry
`2`), loaded synchronously with `shader/sprite` and `shader/line`. It samples the sprite's main
texture `.rgb` — the SVG bake's linear `Rgb8`, its opt-in BC7 `CompRgba8`, or a font's MSDF atlas —
reconstructs the median of the three MSDF channels, derives a screen-space anti-aliasing width from
`fwidth`, and writes `vTint.rgb` with `vTint.a` multiplied by the coverage. It declares two instance
uniforms, `uThreshold` (default `0.5`) and `uSoftness` (default `1.0`), which a sprite or font may
override with `SetUniform`; it declares no global uniforms and no additional texture slots. A font
with no custom shader resolves to this entry and seeds its declared defaults; a custom font shader
that fails falls back to it. A sprite may still assign `shader/sdf` as ordinary custom content, which
shares the same asset and SDF pipeline kind as the font default. The SDF shader uses the normal
pipeline, mips, and bindless page path; `shader/sdf` is no longer loaded lazily on first sighting.

The SVG processor emits a normal `SpriteAsset` that references its `-sdf` texture with full UVs and a
sprite `Scale` that makes the visible artboard one world unit high. The asset carries no shader
choice, so a game loads `shader/sdf` and assigns it to that sprite; there is no SVG renderable and no
extra pass.

## Windowed versus offscreen

Both target drivers compose the same lifecycle coordinator, capture service, and texture services;
only the target driver differs.

| | Windowed | Offscreen |
| --- | --- | --- |
| Target | UNORM swapchain image | Two permanent UNORM images |
| Presentation | Acquire, render, present | Render, discard |
| Input/surface | SDL surface handles supplied by the host | None |
| Minimized behavior | Snapshots drained without GPU work; tick cap in the host | Not applicable |
| Frame capture | After the present pass, before present | After the present pass, before restoring the attachment layout |

## Diagnostics and capture

The renderer publishes an immutable `RenderStats` for the last completed frame: sequence,
framebuffer size, draw calls, instances, glyphs, lines, cameras, non-empty cameras, texture
pages/capacity, texture uploads pending (count and declared bytes), skipped sprites, skipped fonts,
skipped lines, and cumulative validation errors/warnings. `DrawCalls` counts the frame's draw runs
plus the final fullscreen present draw: every sprite run and every font run records one `vkCmdDraw`
and every line run one `vkCmdDrawIndexed`, so a default-only camera records one sprite run, one line
run when it has lines, one font run when it has text, and the present draw; a camera with contiguous
kind/shader/font groups records one per run plus the present, and a frame with no camera runs still
records the present draw. `NonEmptyCameras` counts the cameras with at least one run. `Instances`
counts sprite instance records, `Glyphs` counts submitted glyph instance records, and `Lines` counts
line records submitted by the frame, all across cameras; glyphs share the instance buffer but are
never added to `Instances`.
`TextureUploadsPending` counts content-ready pages the frame's pump left queued by
the upload budget, and `TextureUploadBytesPending` is their validated KTX2 payload total. `TexturePages` is
the resident page count (the placeholder plus ready pages in the current catalog); an evicted page
leaves it and reappears once its next sighting reloads it. The host installs it as
`IEngine.RenderStats`; reading it never touches Vulkan. `SkippedSprites` counts the null,
invalid-layer, non-`Quad`, and still-loading sprites, including a loading custom shader, main texture,
or declared additional texture; a sprite drawn with the placeholder, the default-shader fallback, or
an ignored fifth additional reference is not skipped. `SkippedFonts` counts the null-font-data,
invalid-layer, and still-loading fonts (a loading custom shader or atlas page); a font with an empty
layout, a font drawn with the placeholder atlas, and a font drawn with the SDF fallback are not
skipped. `SkippedLines` counts the null/invalid-geometry, invalid-layer, and still-loading-custom-shader
lines; a line drawn with the built-in `shader/line` or the line-default fallback is not skipped.
Bounds-culled records are not skipped, they only reduce `Instances`/`Glyphs`/`Lines`.

`CaptureFrameAsync` queues a one-shot request through the shared `RendererCaptureService`. The copy
is recorded after the present pass and before present/discard, so it captures the final UNORM target:
the sRGB-encoded result of the frame clear, every camera, and the present pass, and never window
chrome. The result is a `FrameCaptureResult` whose pixels are row-major, top-left-origin RGBA8, whose
dimensions are the captured frame's actual extent, and whose format is always `TextureFormat.Rgba8`.
Because the captured bytes come from the final UNORM target, they are sRGB-encoded RGBA8, the same
visual result as the prior sRGB attachment.
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
