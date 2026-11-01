# Engine

`Bork2d.Engine` is the public contract plus its implementation. Games, the runtime host, the
renderer, and the bake tooling all depend on it; it depends on no windowing, GPU, or logging
provider package.

## Namespaces

| Namespace | Key public types |
| --- | --- |
| `Bork2d.Engine` | `IEngine<TLayer>`, `IGame<TLayer>`, `Bork2dException` |
| `Bork2d.Engine.Scene` | `ISceneRoot`, `ITransform`, `IRenderable`, `ICamera<TLayer>`, `ISpriteRender<TLayer>` |
| `Bork2d.Engine.Rendering` | `Bounds`, `Color`, `RenderStats`, `ClearKind`, `FrameCaptureResult` |
| `Bork2d.Engine.Assets` | `IAssets`, `SpriteAsset`, `TextureDescriptor`, `AssetRef<T>`, `DataRef`, enums |
| `Bork2d.Engine.Assets.Pack` | `PackBuilder`, `PackCompressionMode` (tooling only) |
| `Bork2d.Engine.Input` | `IInput`, `KeyCode`, `MouseButton` |
| `Bork2d.Engine.Platform` | `IWindow`, `ITime` |

`GameEngine`, `Transform`, `Camera`, `SpriteRender`, `AssetService`, `FrameSnapshot`, the typed
render-data buffers/producers/definitions, `RenderableRegistry`, and the projection and
sprite-bounds math are internal. `Bork2d.Engine.Assets.Pack` exposes only the pack writer and its
compression enum; everything else there is implementation.

## Game and engine

The game is a caller-constructed `IGame<TLayer>`: `Setup` runs once before the first tick, `Tick`
once per tick, and `Teardown` once before shutdown. All three run on the tick thread and are never
concurrent. Exceptions propagate to `Bork2dApp.Run` after cleanup.

`IEngine<TLayer>` provides `Time`, `Window`, `Input`, `Assets`, `LoggerFactory`, `Root`,
`RenderStats`, `CreateTransform`, `CreateCamera`, `CreateSpriteRender`, `CaptureFrameAsync`, and
`Exit`. Access rules differ by mode:

| Member | Windowed | Offscreen | Headless |
| --- | --- | --- | --- |
| `Window` | Live proxy during the run | Functional facade: `Size` is the fixed target extent, setters are inert, and nothing throws | Stub; members throw `NotSupportedException` |
| `Input` | Live proxy during the run | Getter throws `NotSupportedException` | Getter throws `NotSupportedException` |
| `CaptureFrameAsync` | Supported | Supported | Throws `NotSupportedException` |
| `RenderStats` | Last completed frame | Last completed frame | `default` |

Retained window/input references throw `ObjectDisposedException` after a windowed run; the offscreen
window facade stays functional. `Exit` may be called from any thread; the current tick finishes and
the next tick does not start.

### Frame capture

`CaptureFrameAsync` returns a `FrameCaptureResult`: row-major RGBA8 pixels with a top-left origin,
the captured frame's actual `Width`, `Height`, and `Format` (`TextureFormat.Rgba8`). `Pixels` is
exactly `Width * Height * 4` bytes and the result is a shared immutable capture result — every
request served by the same GPU frame observes the same memory, so callers must not mutate it. One
array is allocated per captured frame, not per request.

A request is served by the next frame recorded from a published snapshot, and the renderer completes
it from that frame's fence even when the game never publishes another snapshot. At most eight
requests may be outstanding at once, including requests already taken by an in-flight frame; a
request beyond the limit faults with `Bork2dException` instead of blocking the caller or being
dropped, and a slot returns only when its request completes, faults, or is cancelled. Cancellation is
checked first and faults the task with `OperationCanceledException`; a renderer stop or fatal faults
pending requests. Waiting for the task synchronously inside `Setup` or `Tick` before the snapshot that
serves the request is published can block the producer needed to generate that frame.

## Scene model

The scene is a strong tree rooted at `IEngine.Root`, a read-only identity transform. Any
`ITransform` can own children. A parent retains its children strongly; unparented objects are
collectible when game code drops them.

- `CreateTransform(parent)` creates a transform, optionally parented.
- `Parent = null` detaches without destroying and preserves the node's world transform; the same
  instance can be re-attached later.
- Re-parenting preserves the world transform. Cycles and parenting the root are rejected.
- `DetachAll()` clears one node's direct children while each direct child keeps its world
  transform; grandchildren keep their parents.
- There is no entity `Dispose`, liveness flag, ID, or ordered child enumeration.

Cameras are transforms plus projection and clear state: normalized `Viewport`, `Size` (half the
visible height), `CullingMask`, `Clear`, `ClearColor`, and `ZIndex`. `WorldSize`, `WorldToScreen`,
and `ScreenToWorld` use the engine window's framebuffer size and the same projection math as the
renderer: the windowed window, or the fixed offscreen target extent. Headless mode has no
framebuffer, so the helpers throw `InvalidOperationException`. A camera's world scale must stay `1`
(within `1e-4`), and its matrix must be invertible; otherwise the camera is skipped. Camera defaults
are a full viewport, size `10`, all layers, color clear to black, and `ZIndex` `0`.

Sprite renderables are transforms plus `Sprite`, one-bit `Layer`, linear `Tint`, `ZIndex`, and
`FlipX`/`FlipY`. `SpriteAsset` is a MessagePack quad description: a `Texture` reference, four
normalized UVs (top-left, top-right, bottom-right, bottom-left), a per-axis `Vector2` world `Scale`,
a normalized pivot, a topology, and optional indices. The renderer derives the quad; there is no
vertex array. Quad world size is `(maxU - minU) * Scale.X` by `(maxV - minV) * Scale.Y`, so texture
pixel dimensions never affect geometry.

Cameras and sprites are `IRenderable`s and expose a read-only world-space `Bounds` AABB for
conservative renderer culling. A camera's bounds contain its transformed visible rectangle; a
sprite's bounds contain the drawn quad, derived from the frozen render data, pivot, flips, and
composed world matrix. Both return `Bounds.Empty` for an invalid renderable, a headless or
shut-down app, or a sprite with no drawable frozen data, and reading them never throws for those
cases. Empty bounds mean "unknown": the renderer must not treat them as "outside".

`SpriteAsset` is a mutable authoring and loading DTO, but rendering reads only an immutable frozen
copy of its draw fields. Assigning an asset to a renderable freezes it once; mutating the asset
afterwards (including its UV and index arrays) does not change the drawn sprite or any published
snapshot, and re-assigning the same asset keeps the first frozen values. Load or create a new asset
instance to change the rendered result.

Layer enums must be `int`-backed `[Flags]` enums with 1-32 named single-bit values; `None = 0` is
allowed. Invalid layer types fail at build/host creation.

## Time

`ITime.DeltaTime` is zero on the first tick. Windowed and offscreen runs then report the real
interval between tick starts, clamped to the builder's maximum delta (default `0.25` s).
`ITime.Time` is monotonic seconds since the first tick and is never clamped. Headless runs use a
synthesized clock: tick `N` reports `N / ticksPerSecond` and `1 / ticksPerSecond`, with no clamp.
There is no fixed timestep, accumulator, or render interpolation.

## Assets

`IAssets` is thread-safe. Loads complete asynchronously on one loader thread; pack mounts are
synchronous.

- `LoadAsset<T>` / `LoadAsset<T>(AssetRef<T>)` deserialize a MessagePack asset. `T` must carry a
  compiled `[GenerateShape]`; reflection-shape fallback is unsupported.
- `LoadData` / `LoadData(DataRef)` return decompressed bytes for raw or MessagePack entries.
- `LoadPack` mounts a pack above the current stack. Later packs shadow earlier ones by canonical
  path; there is no unload.
- `Browse(prefix)` returns a sorted, materialized snapshot; `Exists(path)` is an exact lookup.

Asset paths are canonical runtime keys: lowercase ASCII, `/` separators, no leading/trailing slash,
no empty or `.`/`..` segments, and segments limited to lowercase letters, digits, `_`, and `-`.
Public input is normalized (backslashes become `/`, lowercased) before validation. Source
extensions therefore never appear in an asset path.

`AssetRef<T>` and `DataRef` store canonical path strings; a null path is an absent reference. The
runtime loader treats a pack as trusted content: it does not validate asset fields or follow
references. Pack creation validates reference integrity and the bake tooling validates fields.

### Pack format

A pack is a v1 `BORK2DPK` file: a 28-byte prefix, a maximum-quality Zstd-compressed MessagePack
header carrying entries and uint64 payload offsets, and stored payloads. The header is checksummed
with XXH3-64. `PackCompressionMode.Debug` stores payloads uncompressed;
`PackCompressionMode.Release` compresses each payload only when that is strictly smaller. Packs are
sorted by canonical path and written atomically.

## Rendering data

`SnapshotCapture` walks the rooted tree once under the engine's structural lock after `IGame.Tick`
returns and writes value-only records into the current frame slot. The walk is iterative and
parent-first: an explicit reusable stack carries each node's evaluated parent world matrix, so
every node's world matrix is composed exactly once as its local matrix times the parent world
matrix, and a deep hierarchy cannot exhaust the call stack. Each node is read under one acquisition
of its per-object lock, and world-matrix composition happens outside that lock.

Capture is driven by registered typed producers, not a central type switch. The camera and sprite
renderables are `CameraRenderableDefinition<TLayer>`/`SpriteRenderableDefinition<TLayer>`; each is
both the renderable factory and its `IRenderDataProducer`. `RenderableRegistry` assigns every
producer a render-data slot and keeps the ordered producer list, and `SnapshotCapture` offers each
traversed node to those producers in registration order: the first match captures the node and
returns its world matrix, and a node that matches no producer uses the plain transform path so its
children still compose. The camera and sprite producers own the reserved slots `0` and `1`; a custom
renderable producer owns a slot at or above `2`. Transforms are traversed, but only renderables
produce records; the layer enum is flattened to a raw 32-bit mask. Each sprite record carries the
immutable `SpriteRenderData` frozen when the asset was assigned, never the live `SpriteAsset`, and
the world-space AABB computed from that data when the capture composed the world matrix. The
renderer culls with those bounds without reading the scene.

Frame buffers are compact typed pools (`RenderDataBuffer<T>`) owned by the snapshot through a
`RenderDataBufferSet`. They start empty, grow to the observed high-water mark, and never shrink;
resetting a slot clears every buffer's used range (not only cameras and sprites), releasing obsolete
references without shrinking the buffers, so a warm capture allocates nothing. Adding a renderable
kind registers one definition/producer, plus the renderer pass for its data; it adds no capture
switch case and no engine creation method.

Camera projection is pure and shared with the renderer: the camera world matrix is the inverse-view
source, the rounded pixel viewport supplies the aspect, and the only Y conversion is the negation
for Vulkan clip space. The projection result also yields the world-space AABB of the camera's
visible rectangle, which the renderer uses for bounds culling. Invalid cameras (bad framebuffer,
size, viewport, clear mode, scale, or non-finite values) are skipped with a diagnostic instead of
being repaired.

## AOT and surface rules

Every runtime project sets `IsAotCompatible`, `EnableTrimAnalyzer`, and `EnableAotAnalyzer`;
warnings are errors. Serialization uses Nerdbank.MessagePack and PolyType generated shapes only.
Public signatures may not expose implementation packages (MessagePack, hashing, Tomlyn,
StbImageSharp). Tests assert the exported namespace/type lists and the dependency matrix, so this
surface is a contract, not a convention.
