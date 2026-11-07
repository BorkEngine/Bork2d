# Render Passes — 0.3 Milestones

[Back to the 0.3 roadmap](../roadmap.md)

Render-pass workstream for Bork2d 0.3: the public pass model, built-in clients, bindless split,
custom renderables and vertex ABI, tests, and the R1-R5 milestones.

Owning docs: [render.md](../render.md) (Vulkan renderer and pipelines), [engine.md](../engine.md)
(snapshot and public scene contracts), and [architecture.md](../architecture.md) (threads and
invariants).

## Goals

- The renderer has one pass model. The 2D pass and the present pass are `IRenderPass` implementations
  registered in the same ordered list as user passes. There is no frame-loop branch on pass type or
  renderable kind and no private recording path for built-in content. "Custom" is not a second
  interface for users; it is the only pass interface the engine has.
- A pass plans value-only data into frame-owned buffers and records bulk draw commands through an
  engine-owned context. A pass never sees a Vulkan handle, a descriptor set, a command buffer, or a
  live scene object.
- A game can author a custom renderable with its own record, vertex/instance layout, vertex and
  fragment shaders, and a custom pass that draws it.
- The 0.2 2D behavior is preserved: sprite/line/font order, culling, shader fallback, capture,
  draw-run accounting, one draw per camera, and zero steady-state allocations.

## Current rigidity

The existing internal pass list is a good skeleton, but it is not a public extension point:

- `IRenderPass` is internal and its plan signature uses the internal `FrameSnapshot`, instance and
  line sinks, global-uniform sink, and Vulkan `Format`. A pass cannot be written outside
  `Bork2d.Render`.
- Pass registration is two literal constructor calls in the windowed and offscreen drivers. There is
  no builder option, renderer start field, or runtime registration seam.
- The record context exposes the final target, descriptor sets, and command buffer; a custom pass
  would either need Vulkan or a second parallel API.
- The 2D planner hardcodes `DrawKind.Sprite`, `DrawKind.Line`, and `DrawKind.Font` in the run
  record, the planner loops, the pipeline switch, the three vertex modules, the shader catalog, and
  the statistics.
- A game cannot derive a scene node: `Transform`, `ISceneNode`, `IRenderableDefinition`,
  `IRenderDataProducer`, and every snapshot record are internal.
- The instance and line buffers assume exactly one planner. Multiple passes writing the same buffer
  need a reserve-then-write protocol.
- Passes have no dispose or resize notification; a pass owning a pipeline or target leaks or goes
  stale.

## Options considered

| Option | What a user can draw | API risk | Built-in pass rewrite | Verdict |
| --- | --- | --- | --- | --- |
| A. One public pass contract, built-ins included | Anything the record context expresses: indexed meshes, instances, fullscreen overlays | The draw vocabulary is public and must be large enough for the 2D and present passes | Yes: 2D and present become clients of the same contract | Selected |
| B. Public pass registration with Vulkan friend access | Anything Vulkan | Leaks Vulkan and internal ABI into game code; breaks the dependency rules and AOT | Small | Rejected |
| C. Asset-driven `.pass` state only | A fixed pipeline over a renderable's records | No per-frame CPU logic; still needs a draw source | Medium | Not sufficient alone |
| D. Full render graph | Anything, correctly synchronized | Transient resources, barriers, aliasing, and allocation-free graph construction | Very large | Deferred |
| E. Keep built-ins direct, add a small public pass beside them | Custom overlays | Two pass models and a privileged built-in path | Small | Rejected by the no-special-stuff rule |

Option A is selected. It is deliberately not a render graph: there are no intermediate targets, no
resource aliasing, and no automatic barrier planner in 0.3. The pass list is ordered by phases, and
the renderer owns the scene image and final target layouts. This is the smallest model that lets the
2D and present passes be ordinary passes and lets a user pass issue its own draws.

## Pass and draw model

Every frame is planned and recorded through the same ordered pass list:

1. Scene clear. The target driver owns the full-target opaque black clear into the scene image. It is
   a phase boundary, not a pass, so it never changes draw-call accounting.
2. Camera passes. For each valid camera in the existing order, every registered camera pass runs in
   registration order. The built-in 2D pass is one camera pass. A user camera pass runs after it and
   sees the same camera projection, viewport, layer mask, and world bounds.
3. Frame passes. After all cameras, every registered frame pass runs in registration order. Used for
   overlays and custom full-screen work that does not need a camera.
4. Present. Exactly one present pass runs last. The registry validates that the last pass is the only
   `Present` pass and that there is exactly one; a missing or misplaced present is a startup error.

A pass implements:

```csharp
public interface IRenderPass
{
    string Name { get; }
    RenderPassPhase Phase { get; }
    void Reset();
    void Plan(in RenderPassPlanContext context);
    void Record(in RenderPassRecordContext context);
}

public enum RenderPassPhase
{
    Cameras,
    Frame,
    Present,
}
```

Planning and recording remain separate so every pass can reserve buffer space before any pass writes
into it. `Reset` clears the pass's per-frame state. `Plan` resolves pipelines and textures, reserves
vertex/instance/index/global ranges, queries the frame's cameras, writes the pass's frozen data into
its reserved ranges, and builds the run list it will draw. `Record` issues those runs through the
record context; it creates no Vulkan object, grows no buffer, and allocates nothing.

The plan context is the only way a pass reaches frame data:

- Frame extent, frame sequence, and time.
- The scene texture handle for a `Present` pass only. Camera and frame passes write the scene image
  and cannot sample it, so 0.3 cannot accidentally create a feedback loop without a render target.
- The cameras for this frame, with projection, viewport, culling mask, and world bounds.
- Typed value-only records the pass registered for. The built-in 2D pass registers the built-in
  sprite/line/font record set; a user pass registers its own record type. The record set is a value
  passed by `ref`, not a live scene object.
- `TryResolvePipeline(PipelineAsset)`, returning a handle that is ready, pending, or failed.
- `TryResolveTexture(AssetRef<TextureDescriptor>)`, returning a bindless page index and applying the
  same pending/failed/placeholder rules as sprites.
- `ReserveVertices<T>`, `ReserveInstances<T>`, `ReserveIndices`, and `ReserveGlobalBlocks` for
  frame-owned ranges.

The record context is the only way a pass issues draws. It exposes indexed and instanced draws and a
fullscreen draw; each draw names a pipeline, a camera or frame scope, a vertex/instance/index range,
a global block, and the fixed blend mode when the pipeline does not fix it. There is no per-record
callback, so a pass cannot accidentally allocate or virtualize per sprite. The context deliberately
does not expose the final target, descriptor sets, image views, or samplers for camera and frame
passes. The present pass receives the final target because its phase is `Present`, not because the
frame loop names it.

A pass instance is created at renderer startup, lives for the run, and is render-thread-only. It
cannot read `IEngine`, `IAssets`, `IWindow`, or a scene node. Per-frame data travels through records;
asset resolution goes through the plan context. Registration happens before the run; there is no
runtime pass add/remove in 0.3.

## Built-in passes

`TwoDimensionalPass` is an ordinary camera pass. It consumes the frozen sprite, line, and font
records, applies camera layer and AABB culling, sorts by the existing `(ZIndex, screen Y, screen X)`
key, expands font glyphs, writes sprite/font instances and line vertices/indices into reserved ranges,
and emits the same contiguous draw runs as 0.2. It uses the built-in sprite, line, and font pipelines
and the same bindless pages. It does not bypass the pass contract.

`PresentPass` is an ordinary present pass. It receives the scene texture and the final target under
its phase, draws one fullscreen triangle with the present pipeline, and applies the sRGB transfer
function. It is the only pass that writes the final target.

The scene clear remains driver-owned. The driver also owns the scene-image transition into
attachment layout and the target transition after present. A pass declares whether it writes the
scene image; the renderer tracks the scene-image layout centrally, so present no longer assumes a
fixed layout after an arbitrary pass.

The built-in passes live in `Bork2d.Render` and may use engine-internal snapshot record types,
because the record shapes are engine content. The mechanism they use is the same public pass and
draw contract a user pass uses. They have no capability a user pass lacks: they register a record
type, resolve pipelines and textures, reserve ranges, and record draws through the same contexts.
Their record types are internal only because sprites, lines, and fonts are engine content. The frame
loop contains no reference to either pass type.

Frame capture remains a copy of the final UNORM target after present. A camera or frame pass that
draws into the scene image is captured through present; a pass cannot render to a separate target and
expect capture. There is no intermediate target in 0.3, so the 0.2 `CaptureFrameAsync` contract is
unchanged.

## Custom renderables and vertex data

A custom renderable is a game content object; the engine owns the scene node. This is the 0.3 form
of "your own `IRenderable`": the game supplies the record and draw data, the engine supplies the
scene node, transform, layer, and z-index, and the pass supplies the draws. `IRenderable<TLayer>`
stays the built-in 2D contract for sprites, lines, and fonts.

```csharp
public interface ICustomRenderable<TRecord>
    where TRecord : struct
{
    Bounds Bounds { get; }          // Local-space; the engine composes the world AABB. Empty = unknown.
    TRecord Capture();              // Called once per snapshot under the node's lock.
}
```

The engine wraps it in an internal node that owns `ITransform`, parenting, world-matrix composition,
the layer bit, the z-index, and the captured record. The public node view is:

```csharp
public interface ICustomRenderableNode<TLayer> : ITransform
    where TLayer : struct, Enum
{
    TLayer Layer { get; set; }   // One camera culling bit.
    int ZIndex { get; set; }     // Shared 2D order when the pass honors it.
}
```

The game creates the node with
`IEngine.CreateCustomRenderable(ITransform parent, ICustomRenderable<TRecord> content)` after
registering the kind with the builder. The registration supplies the record type, the pipeline asset,
the draw source, and the pass association; it is generic, so there is no reflection or runtime type
lookup.

Capture is a frozen record. It may carry an immutable mesh reference, like the existing line
geometry, but no live scene object, asset service, task, or delegate that outlives the frame. A
renderable that changes its mesh every tick allocates a new immutable mesh; a static mesh is built
once and shared.

The per-kind vertex/instance ABI is declared by a pipeline asset:

```csharp
public sealed partial class PipelineAsset
{
    public DataRef VertexSpirV { get; set; }
    public DataRef FragmentSpirV { get; set; }
    public VertexLayout Layout { get; set; }
    public PrimitiveTopology Topology { get; set; }
    public BlendMode Blend { get; set; }
    public uint LayoutVersion { get; set; }
}
```

`PipelineAsset` is the full 0.3 pipeline form. The existing fragment-only `ShaderAsset` remains the
sprite, line, and font custom-fragment path; assigning one resolves to a pipeline that uses that
kind's engine-provided vertex stage. A pipeline asset without a vertex stage behaves the same way; a
pipeline asset with a vertex stage requires an explicit layout and topology.

`VertexLayout` declares attributes by location, format, offset, stride, and whether the element is
per-vertex or per-instance. The bake validates offsets, overlaps, formats, and the total stride. A
pipeline asset without a vertex stage is a fragment-only pipeline and uses the engine-provided
vertex stage for its kind; this keeps 0.2 fragment-only shaders valid where the kind has an engine
stage.

User vertex shaders are baked from `.vert` sources by the shader processor; fragment shaders remain
`.frag`. A pipeline is assembled by a data asset that references both stages and declares the layout
and blend mode. The exact wire shape is settled in the implementation task, but the capability is
fixed: a user kind can have a custom vertex stage, custom per-vertex and per-instance layouts,
32-bit indices, triangle lists or strips, and one of the fixed blend modes.

The built-in sprite, line, and font pipelines become built-in pipeline assets in the built-in pack,
with engine-shipped vertex stages; present becomes a built-in fullscreen pipeline with no vertex
input. This is what makes "no special stuff" true: the engine's own passes use pipeline assets and
the same record context, not compiled-in pipeline branches.

The record context supports:

- Instanced quad-style draws with no vertex buffer, for sprites and font glyphs.
- Indexed triangle draws with a vertex buffer and a custom layout, for lines and custom meshes.
- A fullscreen draw with no vertex input, for present and fullscreen overlays.
- Per-camera draws with the camera's view-projection push constant and viewport/scissor.
- Frame draws with the full target viewport and no camera push constant.
- Straight-alpha, additive, and opaque blends. No depth, stencil, cull, or raster state.
- Bindless texture indices resolved at plan time and written into the user's vertex or instance data
  by the pass.
- Per-run global uniform blocks, including the engine time slot; a pass reserves and writes the
  blocks it needs.

A custom pass is the same `IRenderPass`; it registers for a custom record type and draws that type's
mesh. It is not a special "custom" interface with different rules.

## Ordering, culling, and statistics

- Camera order is the existing camera sort: `ZIndex` ascending, ties unspecified.
- Within a camera, camera passes run in registration order. The built-in 2D pass is registered
  before user camera passes by default. A user camera pass that must draw before the 2D pass can be
  registered before it through the builder.
- Frame passes run in registration order after every camera.
- `Layer` remains a camera culling bit and never participates in draw order. A custom record carries
  a layer bit and bounds; the pass applies the camera mask and conservative world-AABB test with the
  shared helpers. Empty bounds mean unknown and never cull.
- `ZIndex` remains the shared 2D order for built-in sprites, lines, and fonts. A custom record that
  wants to interleave with them by `ZIndex` must be drawn by the 2D pass or must implement equivalent
  sorting in its own pass; the pass contract does not impose a cross-pass order beyond phase and
  registration.
- `RenderStats` keeps its shape. `DrawCalls` includes every pass draw plus present. `Instances`,
  `Glyphs`, and `Lines` remain the built-in counters. A custom pass owns any additional counters and
  exposes them to the game directly; they are not added to the public struct.
- A pass exception is a one-shot renderer fatal, consistent with the renderer's existing failure
  containment. A pending pipeline or texture skips the affected record and is counted according to
  the pass's own rules. A failed pipeline falls back only where the existing shader rules define a
  fallback.

## Resources and parity

Frame resources remain renderer-owned: the scene image, final target, one shared vertex buffer, one
index buffer, the dynamic global uniform buffer, the bindless descriptor set, and the per-frame
fences. Passes own no Vulkan object. Pipelines are cached by pipeline asset content hash, vertex
layout, topology, blend mode, and target format. The pipeline cache and descriptor sets are
render-thread-owned and retired with the existing two-frames-in-flight safety rule.

The rewrite is proved by parity, not by inspection:

- Byte-exact planner tests for a mixed scene (sprites, lines, fonts, a custom shader, two cameras,
  one rotated, one `Clear.None`) assert instance records, line vertices/indices, global blocks, and
  run boundaries.
- Offscreen GPU tests assert `RenderStats` and probe pixels for the existing sprite, line, font,
  SDF, KTX2, and present behavior.
- The Release sprite benchmark keeps one draw per camera for the default path, 50,000 instances,
  zero allocated bytes, and its existing frame-rate floor.
- A custom indexed mesh and a user pass are captured in a GPU test, including ordering, culling,
  additive blend, and a pending pipeline skip.

## Risks specific to this workstream

The main risk is that the public draw contract becomes either too narrow for the built-in passes or
so wide that it is a Vulkan clone. The mitigation is to keep it bulk and value-based, to implement
the 2D and present passes first as contract clients, and to refuse any capability that neither the
built-in passes nor the custom vertex/instance requirement needs for 0.3. A second risk is
performance: the renderer must keep the 2D planner's allocation-free sort and run building, and the
record path must not virtualize per record. The benchmark and allocation tests are release gates,
not optional measurements.

## Milestones

R1 - Pass framework and built-in clients. Public pass, plan/record contexts, bulk draw commands,
pipeline asset skeleton, frame-owned reservations, phases, pass dispose and target-change
notifications, central scene-image layout tracking, per-pass debug labels, and 2D/present converted
to `IRenderPass`. Exit: no frame-loop branch on pass type; 0.2 parity tests, capture, stats, and the
sprite benchmark pass; a test pass can draw a fullscreen overlay and be captured.

R2 - Bindless split. Replace the combined image-sampler array with a sampled-image array plus one
sampler, bump the shader layout version, update the prologue and every built-in pipeline, and make
texture-page capacity device-driven with a documented minimum. Exit: all platforms still render the
0.2 content; MoltenVK's 16-sampler limit is no longer a startup blocker; a device that reports fewer
pages than the built-in content needs fails with a clear message; the page growth and residency tests
pass.

R3 - Custom kinds and user passes. `ICustomRenderable<TRecord>`, engine node wrapper, builder
registration, custom record types, custom pass registration, and a custom pass that draws its own
record. Exit: a user-authored pass draws and is captured in both windowed and offscreen modes;
culling, layer masks, per-camera scope, and additive blend work; pending pipeline/texture skips are
counted; no steady-state allocation.

R4 - Custom vertex/instance ABI and vertex shaders. Pipeline asset with vertex stage, layout,
topology, and blend; `.vert` bake processor; data pipeline asset; custom indexed mesh and instanced
kind; pipeline cache keys. Exit: a custom indexed mesh with a custom vertex and fragment shader draws
at the expected pixels; a custom instanced kind works; a malformed layout fails at bake or pipeline
creation with a clear diagnostic; AOT publishes with zero warnings.

R5 - Hardening and as-built docs. Performance, parity, error containment, migration note, and
renderer documentation. Exit: all release gates pass and no unfinished marker remains.

