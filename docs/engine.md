# Engine

`Bork2d.Engine` is the public contract plus its implementation. Games, the runtime host, the
renderer, and the bake tooling all depend on it; it depends on no windowing, GPU, or logging
provider package.

## Namespaces

| Namespace | Key public types |
| --- | --- |
| `Bork2d.Engine` | `IEngine<TLayer>`, `IGame<TLayer>`, `Bork2dException` |
| `Bork2d.Engine.Scene` | `ISceneRoot`, `ITransform`, `ICamera<TLayer>`, `ISpriteRender<TLayer>` |
| `Bork2d.Engine.Rendering` | `Color`, `RenderStats`, `ClearKind` |
| `Bork2d.Engine.Assets` | `IAssets`, `SpriteAsset`, `TextureDescriptor`, `AssetRef<T>`, `DataRef`, enums |
| `Bork2d.Engine.Assets.Pack` | `PackBuilder`, `PackCompressionMode` (tooling only) |
| `Bork2d.Engine.Input` | `IInput`, `KeyCode`, `MouseButton` |
| `Bork2d.Engine.Platform` | `IWindow`, `ITime` |

`GameEngine`, `Transform`, `Camera`, `SpriteRender`, `AssetService`, `FrameSnapshot`, and the
projection math are internal. `Bork2d.Engine.Assets.Pack` exposes only the pack writer and its
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
| `Window` | Live proxy during the run | Stub; members throw `NotSupportedException` | Stub; members throw `NotSupportedException` |
| `Input` | Live proxy during the run | Getter throws `NotSupportedException` | Getter throws `NotSupportedException` |
| `CaptureFrameAsync` | Supported | Supported | Throws `NotSupportedException` |
| `RenderStats` | Last completed frame | Last completed frame | `default` |

Retained window/input references throw `ObjectDisposedException` after a run. `Exit` may be called
from any thread; the current tick finishes and the next tick does not start.

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
renderer; they require a windowed framebuffer. A camera's world scale must stay `1` (within
`1e-4`), and its matrix must be invertible; otherwise the camera is skipped. Camera defaults are a
full viewport, size `10`, all layers, color clear to black, and `ZIndex` `0`.

Sprite renderables are transforms plus `Sprite`, one-bit `Layer`, linear `Tint`, `ZIndex`, and
`FlipX`/`FlipY`. `SpriteAsset` is a MessagePack quad description: a `Texture` reference, four
normalized UVs (top-left, top-right, bottom-right, bottom-left), a per-axis `Vector2` world `Scale`,
a normalized pivot, a topology, and optional indices. The renderer derives the quad; there is no
vertex array. Quad world size is `(maxU - minU) * Scale.X` by `(maxV - minV) * Scale.Y`, so texture
pixel dimensions never affect geometry.

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

`SnapshotCapture` walks the rooted tree under the engine's structural lock after `IGame.Tick`
returns and writes value-only `CameraSnapshot`/`SpriteSnapshot` records into the current frame slot.
Transforms are traversed, but only cameras and sprites are captured; world matrices include the
parent chain, and the layer enum is flattened to a raw 32-bit mask. Each sprite record carries the
immutable `SpriteRenderData` frozen when the asset was assigned, never the live `SpriteAsset`.
Buffers are slot-private and grow to a high-water mark without shrinking, so a warm capture allocates
nothing.

Camera projection is pure and shared with the renderer: the camera world matrix is the inverse-view
source, the rounded pixel viewport supplies the aspect, and the only Y conversion is the negation
for Vulkan clip space. Invalid cameras (bad framebuffer, size, viewport, clear mode, scale, or
non-finite values) are skipped with a diagnostic instead of being repaired.

## AOT and surface rules

Every runtime project sets `IsAotCompatible`, `EnableTrimAnalyzer`, and `EnableAotAnalyzer`;
warnings are errors. Serialization uses Nerdbank.MessagePack and PolyType generated shapes only.
Public signatures may not expose implementation packages (MessagePack, hashing, Tomlyn,
StbImageSharp). Tests assert the exported namespace/type lists and the dependency matrix, so this
surface is a contract, not a convention.
