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
  `vkDeviceWaitIdle` is allowed only during recreation and shutdown.

Useful files: [IRenderer.cs](../src/Bork2d/Bork2d.Render/IRenderer.cs),
[SnapshotRing.cs](../src/Bork2d/Bork2d.Render/Handoff/SnapshotRing.cs),
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

The render thread consumes one snapshot and plans it in one allocation-free pass:

1. Sort sprite indices by ascending `(Layer, ZIndex)`.
2. Classify each sprite once from its frozen render data: a null sprite, zero/multi-bit layer,
   non-`Quad` topology, or a still-loading texture page is skipped; malformed fields are fatal.
3. Ensure each sprite's texture page and resolve quad geometry from the frozen UVs and per-axis
   world scale for drawable sprites.
4. Sort cameras by `ZIndex` and resolve their projections; invalid cameras are skipped.
5. Filter by each camera's culling mask, assign contiguous per-camera instance ranges, grow the
   instance buffer once, and write the instance records.

A captured sprite carries the immutable `SpriteRenderData` frozen when its asset was assigned, never
the live `SpriteAsset`, so mutating an asset after assignment cannot change a plan or a published
snapshot. Each non-empty camera gets one dynamic-rendering pass and exactly one
`vkCmdDraw(4, instanceCount, 0, firstInstance)`. A quad is a four-vertex triangle strip with no
vertex buffer: `gl_VertexIndex` selects the corner, and the vertex shader builds the world position
from the instance matrix, pivot, size, and UVs. The quad is `(maxU - minU) * Scale.X` by
`(maxV - minV) * Scale.Y` world units; texture pixel dimensions are never used. Clear semantics are
per camera: `Clear.Color` clears the camera's pixel rect to `ClearColor`; `Clear.None` and
`Clear.Depth` load the existing content. There is no frame-level safety clear and no depth
attachment.

## Textures

Texture pages are identity-mapped by canonical `TextureDescriptor` path. Index `0` is a placeholder
page; sprites with an absent texture reference draw with it. The table starts at 256 entries
(placeholder plus 255 pages) and doubles when full, up to the smaller of the device's per-stage and
per-set update-after-bind sampled-image limits (capped at 1,048,576). Reaching the limit is a fatal
`Bork2dException`; pages are never silently dropped.

A page moves through `Pending -> Uploading -> Ready`, or `Failed`:

- Loading is asynchronous through the app's `IAssets`; the render thread never waits on it.
- Pending pages are skipped for that frame.
- The first successful load for a path wins for the run; later descriptors reuse the page.
- Failed paths draw the placeholder for the run and are not retried.

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

| | Windowed | Offscreen |
| --- | --- | --- |
| Target | Swapchain image | Two permanent sRGB images |
| Presentation | Acquire, render, present | Render, discard |
| Input/surface | SDL surface handles supplied by the host | None |
| Minimized behavior | Snapshots drained without GPU work; tick cap in the host | Not applicable |
| Frame capture | Before present | Before restoring the attachment layout |

## Diagnostics and capture

The renderer publishes an immutable `RenderStats` for the last completed frame: sequence,
framebuffer size, draw calls, instances, cameras, non-empty cameras, texture pages/capacity,
skipped sprites, and cumulative validation errors/warnings. The host installs it as
`IEngine.RenderStats`; reading it never touches Vulkan.

`CaptureFrameAsync` queues a one-shot request. The copy is recorded after the last camera pass and
before present/discard, so it includes every camera and excludes window chrome. The result is a new
RGBA8, row-major, top-left-origin byte array; staging memory is pooled per GPU frame. Cancellation
faults the task; a stop or fatal faults all pending requests.

Renderer failures are one-shot: the host observes `FatalException` at a tick boundary, no further
tick starts, and cleanup is best-effort, idempotent, and non-throwing.
