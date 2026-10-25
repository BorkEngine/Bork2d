# API Reference

> **Status:** target contract. `Current` means the member exists in `Bork2d.Api` today;
> `Planned` means an agreed addition that is **not** in the scaffold; `Open` means the shape
> or semantics is undecided. The current code is partial; see
> [Projects and dependencies](../architecture/projects.md) and the
> [roadmap](../roadmap.md).

## 1. Contract-wide rules

### 1.1 Thread affinity and modes

**No public API requires the process main thread.** Calls may originate on any thread **during
the `Setup`/`Tick`/`Teardown` windows**. The engine serializes scene structure and per-object
property access; window operations are marshaled to the OS/window thread, and input/time reads
are snapshots.

| Scope | Allowed thread |
| --- | --- |
| `IEngine` / `ITransform` / `ICamera` / `ISpriteRender` scene operations | Any thread; structural + per-object locks. |
| `IEngine.Exit` | Any thread. |
| `ITime`, `IInput` reads | Any thread; snapshot values. |
| `IWindow` operations | Any thread; marshaled to the OS/window thread. |
| `IGame.Setup/Tick/Teardown` | Invoked by the engine on its tick thread; not called by the game. |
| `builder/Build/Run/Dispose` | Any thread; no main-thread requirement. |
| `IEngine.LoggerFactory` | Any thread; Microsoft.Extensions.Logging factories/loggers are thread-safe. |
| `IAssets.LoadSprite` / `LoadAsset<T>` / `LoadData` / `LoadPack` / `Exists` | Any thread; requests are thread-safe. Completion is asynchronous. `Browse` is for tools, not per-frame. |
| Value types (`Sprite`, `AssetInfo`, `Color`, `Vector2`, enums) | Any thread; they are copies. |
| Renderer, swapchain, GPU uploads | Render thread only; no public API. |
| Asset parsing/decompression/resource table | Asset threads only; no public API. |

Modes affect availability:

| Member | Windowed | Offscreen | Headless |
| --- | :-: | :-: | :-: |
| `IWindow` | yes | throws | throws |
| `IInput` | yes | throws | throws |
| `ITime` | yes | yes | yes |
| `IAssets` | yes | yes | data/browse only; no GPU pages |
| Scene creation | yes | yes | yes (never rendered) |

Scene mutation remains logically tick-window scoped (`Setup`/`Tick`/`Teardown`). Game workers may
create and mutate distinct entities during a tick and must join before `Tick` returns. Structural
attach/detach is serialized by the scene-graph lock; individual properties use per-object locks.
A logical multi-property update to the same entity still needs game synchronization if it must be
atomic. There are no thread-affinity exceptions.

### 1.2 Units and conventions

| Quantity | Convention |
| --- | --- |
| Position | World units; +X right, +Y up. |
| Rotation | Radians, positive counter-clockwise around +Z. |
| Scale | Multiplier; negative values mirror. |
| `ICamera.Size` | Half visible height in world units. |
| `ICamera.Viewport` | Normalized `(x, y, width, height)`, top-left origin, `[0,1]`. |
| Sprite asset UVs | Normalized, top-left origin. |
| Sprite asset `scale` | Pixels per world unit; renderer divides by it. |
| Sprite asset pivot | Normalized, top-left origin; default center. |
| Sprite asset `indices` | `ushort[]?`: null for `Quad`; required for `TriangleStrip`/`Triangles`. |
| Input mouse | Framebuffer pixels, top-left origin. |
| Time | Seconds; monotonic; `DeltaTime` clamped. |
| Color | Linear RGBA, components `[0,1]`. |
| Layers | One 32-bit flag per renderable; masks may combine flags. |
| `Sprite` | Opaque session handle; `0` is empty. |
| `ZIndex` | Unitless int; higher sorts later/on top. |

### 1.3 Defaults

| Object | Default |
| --- | --- |
| Mode | Windowed. |
| Camera size | `10` units (half-height), so 20 units tall. |
| Camera viewport | `(0, 0, 1, 1)`. |
| Camera culling mask | All declared layer flags. |
| Camera clear | `ClearKind.Color`, `Color.Black`. |
| Sprite handle | `Sprite.Empty`. |
| Sprite layer | `default`/`0` — **invalid**; skipped until set. |
| Sprite tint | `Color.White`. |
| Sprite z-index | `0`. |
| Assets pack | `assets.pck` next to the executable. |
| Max delta | 0.25 s safety clamp; not a pacing delay. |
| VSync | Enabled (`FIFO`) in windowed mode. |
| Logging | Pretty ZLogger console provider by default; configurable via builder. |
| Tick rate | **None** for windowed/offscreen. Headless: configurable `ticksPerSecond` (default 60, `0` = uncapped). |

### 1.4 NativeAOT

The runtime/game is NativeAOT- and trim-safe. There is no runtime reflection, assembly
scanning, runtime TOML, or dynamic code. `LoadAsset<T>` requires a
[Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) generated shape. The
game is passed as an instance. `Bork2d.Api` references
`Microsoft.Extensions.Logging.Abstractions` for `IEngine.LoggerFactory`; no GPU/OS/Silk types
are allowed. See [NativeAOT](../architecture/nativeaot.md).

## 2. `IEngine<TLayer>`

```csharp
public interface IEngine<TLayer> where TLayer : struct, Enum
{
    ITime Time { get; }
    IWindow Window { get; }
    IInput Input { get; }
    IAssets Assets { get; }
    ILoggerFactory LoggerFactory { get; }
    ISceneRoot Root { get; }

    ITransform CreateTransform(ITransform? parent = null);
    ICamera<TLayer> CreateCamera(ITransform parent);
    ISpriteRender<TLayer> CreateSpriteRender(ITransform parent);

    void Exit();
}
```

`ISceneRoot` is a read-only marker over `ITransform`:

```csharp
public interface ISceneRoot : ITransform
{
    // No extra members; root setters throw at runtime.
}
```

`ITransform.DetachAll()` detaches the transform's direct children. `Root.DetachAll()` therefore
detaches the scene's top-level children; the objects remain valid and can be re-attached. The
root is an identity transform: setting parent, position, rotation, or scale throws
`InvalidOperationException`. The app calls `Root.DetachAll()` during shutdown.

| Member | Status | Semantics |
| --- | --- | --- |
| `Time` | `Current` | Monotonic time/delta; snapshot reads from any thread. |
| `Window` | `Current` | Window controls. Windowed only; throws in offscreen/headless. |
| `Input` | `Current` | Input state. Windowed only; throws in offscreen/headless. M1 scaffold throws `NotSupportedException` (windowed input is M2). |
| `Assets` | `Current` | Pack/asset requests; thread-safe methods. |
| `LoggerFactory` | `Current` | Microsoft.Extensions.Logging factory configured by the builder with ZLogger; create per-category loggers. M1 scaffold returns `NullLoggerFactory.Instance` until the logging work wires the real factory. |
| `Root` | `Current` | Read-only root of the strong scene graph; app-owned. `Root.DetachAll()` detaches children. Implemented in the M1 engine (WP3); the public host that creates the engine is WP4. |
| `CreateTransform(parent)` | `Current` | Creates a transform under `parent`, or standalone when `parent` is null. A new node is attached with its default local values, so it starts at the parent's origin. Inside `Setup`/`Tick`; workers may create during a tick. Implemented in WP3. |
| `CreateCamera(parent)` | `Current` | Creates a camera under `parent`; parent is required. Workers may create during a tick. Implemented in WP3. |
| `CreateSpriteRender(parent)` | `Current` | Creates a sprite renderable under `parent`; parent is required. Workers may create during a tick. Implemented in WP3. |
| `Exit()` | `Current` | Graceful shutdown after the current tick; any thread. The M1 scaffold sets the shutdown flag read by the placeholder loop; the real loop is WP4. |

Notes:

- The engine does not own the loop; `Bork2d.Runtime` does. There is no `Run`/`Update` on
  `IEngine`.
- `TLayer` must be an `int`-backed `[Flags]` enum with 1–32 named single-bit values; the
  builder validates it.
- **Ownership:** `Root` strongly owns parented objects. Unparented objects are not retained by
  the engine and are collectible when the game drops its reference. There is no global
  camera/sprite registry, no weak entity registry, and no entity `Dispose`/`IsAlive`. Detach with
  `Parent = null`; re-attach the same object later. `Root.DetachAll()` detaches the scene. See
  [Scene ownership](../architecture/scene-ownership.md).
- Any thread may call the public API; no process-main-thread requirement. Scene mutation remains
  logically tick-window scoped; locks serialize concurrent calls.
- The engine has no public `Dispose`; `Bork2dApp` owns shutdown and clears `Root`.
- Headless mode still creates cameras/sprites but never renders; `Window`/`Input` throw.
- `LoggerFactory` is created/owned by the app; `WithLogging` configures its providers. See
  [Logging](../architecture/logging.md).

## 3. `IGame<TLayer>`

```csharp
public interface IGame<TLayer> where TLayer : struct, Enum
{
    void Setup(IEngine<TLayer> engine);
    void Tick();
    void Teardown();
}
```

| Member | Status | Semantics |
| --- | --- | --- |
| `Setup(IEngine<TLayer>)` | `Current` | Once on the engine tick thread before the first tick. Create entities, request assets. |
| `Tick()` | `Current` | Once per tick on the engine tick thread. Must return; no `async void`; join workers before returning. |
| `Teardown()` | `Current` | Once on the engine tick thread before shutdown. Release game resources; the app clears `Root`. Asset requests are not guaranteed to complete. |
| `Run`/`Update`/`Draw` | — | Not part of the contract. The host owns the loop; the snapshot is built after `Tick`. |

`Setup`, `Tick`, and `Teardown` are not concurrent. Exceptions propagate out of
`Bork2dApp.Run()` after graceful cleanup. `Setup` may create entities; it must not block on
asset loading.

## 4. `ITime`

```csharp
public interface ITime
{
    double DeltaTime { get; }
    double Time { get; }
}
```

| Member | Status | Semantics |
| --- | --- | --- |
| `DeltaTime` | `Current` | Seconds since the previous tick start, clamped by `MaxDeltaTime`. First tick is `0`. |
| `Time` | `Current` | Monotonic gameplay time in seconds, starting at `0` on the first tick. |

- Read-only; safe from any thread (snapshot values).
- Variable timestep. Windowed/offscreen have **no target tick rate**; pacing is bounded
  run-ahead against the renderer. Headless is timer-paced to `ticksPerSecond` (default 60,
  `0` = uncapped). Windowed/offscreen use no `Sleep`/deadline pacing; headless pacing is the
  intentional exception.
- No accumulator, fixed step, or render interpolation.
- Monotonic `Stopwatch`, not wall-clock time. Not paused on focus loss/minimize.
- `TickCount` is not in v1.

## 5. `IWindow`

```csharp
public interface IWindow
{
    string Title { get; set; }
    Vector2 Size { get; set; }
    bool Fullscreen { get; set; }
    bool CursorVisible { get; set; }
}
```

| Member | Status | Semantics |
| --- | --- | --- |
| `Title` | `Current` | Window title. Any thread; marshaled, windowed only. |
| `Size` | `Current` | Client/framebuffer size in physical pixels; request resize. Windowed only. |
| `Fullscreen` | `Current` | Toggle fullscreen/borderless; exact mode `Open`. Windowed only. |
| `CursorVisible` | `Current` | OS cursor visibility. No capture/relative mode in v1. Windowed only. |

- Offscreen/headless: **throws** (`Bork2dException`/`NotSupportedException`). Do not use window
  APIs in code that runs in all modes.
- `Size` returns the latest known framebuffer size; the swapchain may lag one frame.
- Setting size/fullscreen every tick causes swapchain churn.
- High-DPI logical scaling is not handled in v1; sizes are physical pixels. `Open` for M2.
- `IsMinimized` is under consideration.

## 6. `IInput`

```csharp
public interface IInput
{
    event Action<char> OnTextTyped;

    bool IsKeyPressed(KeyCode key);
    bool IsKeyReleased(KeyCode key);
    bool IsKeyDown(KeyCode key);

    Vector2 MousePosition { get; }
    Vector2 MouseDelta { get; }
    float MouseWheelDelta { get; }

    bool IsMousePressed(MouseButton button);
    bool IsMouseReleased(MouseButton button);
    bool IsMouseDown(MouseButton button);
}
```

Access via `IEngine.Input` (declaration `Current`; windowed implementation is M2). **Windowed
only**; offscreen/headless throw.

| Member | Status | Semantics |
| --- | --- | --- |
| `OnTextTyped` | `Current` | UTF-16 `char` events from SDL text input; dispatched on the engine tick thread before `Tick`. |
| `IsKeyPressed` | `Current` | True only during the tick of the up→down transition. |
| `IsKeyReleased` | `Current` | True only during the tick of the down→up transition. |
| `IsKeyDown` | `Current` | Held state at tick time. |
| `MousePosition` | `Current` | Framebuffer pixels, top-left. Not world units. |
| `MouseDelta` | `Current` | Pixel delta since the previous tick; zero when unfocused. |
| `MouseWheelDelta` | `Current` | Accumulated wheel ticks; positive means up/away. |
| `IsMousePressed/Released/Down` | `Current` | Edge/held semantics for mouse buttons. |

- Input is frozen at tick start; read anywhere in `Tick`.
- Fast down/up between ticks: both pressed/released true, `Down == false`.
- Edges reset after the tick.
- Focus loss synthesizes releases; wheel/text cleared.
- `KeyCode` maps physical SDL scancodes; text is separate.
- Full key/button set is `Planned`; numeric values are not stable and must not be serialized.
- Not readable from the render thread and not part of snapshots.

## 7. `IAssets`

```csharp
public interface IAssets
{
    Task<T> LoadAsset<T>(string assetPath) where T : class;
    Sprite LoadSprite(string assetPath);
    Task<byte[]> LoadData(string assetPath);

    void LoadPack(string packPath);
    IReadOnlyList<AssetInfo> Browse(string? prefix = null);
    bool Exists(string assetPath);
}

public readonly record struct AssetInfo(
    string Path,
    bool IsRaw,
    bool IsCompressed,
    long Length,
    long Size,
    string PackPath);
```

| Member | Status | Semantics |
| --- | --- | --- |
| `LoadAsset<T>` | `Current` declaration; semantics `Planned` | Loads/decompresses a MessagePack asset with a Nerdbank generated shape. Throws for raw assets/missing paths. |
| `LoadSprite` | `Current` declaration; semantics `Planned` | Resolves a sprite asset path and returns a handle immediately; loading is async. Missing returns `Sprite.Empty`. |
| `LoadData` | `Current` declaration | Returns decompressed bytes for any asset kind. |
| `LoadPack` | `Planned` | Mounts a pack above the current stack; later wins; no unload in v1. |
| `Browse` | `Planned` | Merged snapshot of visible assets; prefix filter; for tools, not per-frame. |
| `Exists` | `Planned` | Whether the merged filesystem contains the canonical path. |

Rules:

- Paths are canonical lowercase `/` keys without source extensions (see
  [Assets: paths](../architecture/assets.md#3-paths)).
- `LoadSprite` is idempotent per path; a handle is session-local and never persisted.
- A sprite is skipped until its sprite asset, texture descriptor, raw payload, and GPU page are
  ready. No blocking, no placeholder.
- Missing/failed assets log; handles stay non-renderable. Retry policy is `Open`.
- `LoadAsset<T>` requires a Nerdbank generated shape. No reflection fallback (NativeAOT).
- `LoadPack`/`Browse`/`Exists` are thread-safe; mount packs during `Setup`/before loading.
- No public pack/asset unload in v1; load-and-keep.
- Default pack is `assets.pck`; a missing or malformed base pack throws at startup. There is no
  empty-FS fallback.

## 8. `ICamera<TLayer>`

```csharp
public interface ICamera<TLayer> : ITransform
    where TLayer : struct, Enum
{
    Vector4 Viewport { get; set; }
    float Size { get; set; }
    TLayer CullingMask { get; set; }
    ClearKind Clear { get; set; }
    Color ClearColor { get; set; }
}
```

| Member | Status | Semantics |
| --- | --- | --- |
| `Viewport` | `Current` | Normalized `(x, y, w, h)`, top-left. Clamped; zero size skips with a warning. |
| `Size` | `Current` | Half visible height in world units; positive. |
| `CullingMask` | `Current` | Combination of layer flags; `0` sees nothing. |
| `Clear` | `Current` | `Color` clears viewport, `None` preserves earlier content, `Depth` is a no-op/one-time warning. |
| `ClearColor` | `Current` | Linear RGBA for `Clear.Color`. |
| `ScreenToWorld(Vector2)` | `Planned` | Framebuffer pixels → world units using this camera. |
| `WorldToScreen(Vector2)` | `Planned` | Inverse. |

- Cameras inherit `ITransform`; position/rotation/parent come from the transform. Scale is not
  zoom; use `Size`. Create with `IEngine.CreateCamera(parent)`; use `Root` or a rooted transform.
- Camera order is creation order (entity ID). Later camera with `Clear.Color` wipes its
  viewport; `None` overlays.
- Defaults above. `Clear.Depth` is accepted for forward compatibility.
- Works in headless/offscreen, but headless never renders.
- Cameras are not `IDisposable` and have no `IsAlive` state; detach with `Parent = null` or
  clear the root. The engine camera type is internal and has no destruction lifecycle.

## 9. `ISpriteRender<TLayer>`

```csharp
public interface ISpriteRender<TLayer> : ITransform
    where TLayer : struct, Enum
{
    Sprite Sprite { get; set; }
    TLayer Layer { get; set; }
    Color Tint { get; set; }
    int ZIndex { get; set; }
}
```

| Member | Status | Semantics |
| --- | --- | --- |
| `Sprite` | `Current` | Opaque sprite asset handle. `Empty` = no draw. Not-ready handles skipped. |
| `Layer` | `Current` | Exactly one non-zero bit. `0`/composite invalid and skipped with a diagnostic. |
| `Tint` | `Current` | Linear RGBA multiplied with the texture; `White` neutral. |
| `ZIndex` | `Current` | Sort within layer; higher draws later. Ties use entity ID. |
| `FlipX`, `FlipY` | `Planned` | Mirrors the sprite without changing the transform. |

- The sprite asset supplies UVs, pivot, topology, optional indices, texture path, and a
  **pixels-per-unit** scale. `LocalScale` multiplies. There is no global PPU and no vertices
  array.
- Hide with `Sprite = Sprite.Empty` (keep entity) or detach with `Parent = null`.
- Sorting: layer numeric → `ZIndex` → entity ID. A not-ready asset yields no instance.
- Headless: entities can exist but never produce an instance.
- Create with `IEngine.CreateSpriteRender(parent)`; use `Root` or a rooted transform.
- Sprite renderables are not `IDisposable` and have no `IsAlive` state; detach with
  `Parent = null` or clear the root. The engine sprite type is internal and has no destruction
  lifecycle.

## 10. `ITransform`

```csharp
public interface ITransform
{
    ITransform? Parent { get; set; }
    Vector2 LocalPosition { get; set; }
    float LocalRotation { get; set; }
    Vector2 LocalScale { get; set; }

    void DetachAll(); // detaches direct children
}
```

| Member | Status | Semantics |
| --- | --- | --- |
| `Parent` | `Current` | Parent or `null`. Re-parenting preserves world transform; self/cycles rejected; `Root.Parent` cannot be set. |
| `DetachAll()` | `Current` | Detaches direct children; grandchildren remain attached. Any transform can call it, not just root. Implemented in WP3 under the structural lock. |
| `LocalPosition` | `Current` | Position relative to parent, world units. |
| `LocalRotation` | `Current` | Radians, CCW, relative to parent. |
| `LocalScale` | `Current` | Scale relative to parent; negative mirrors. |
| `WorldPosition` | `Planned` | Read-only evaluated world position. |
| `WorldRotation` | `Planned` | Read-only evaluated world rotation (radians). |
| `WorldScale` | `Planned` | Read-only evaluated world scale. |

- World transforms are evaluated/captured on the tick thread; the renderer never walks the
  hierarchy.
- World position/rotation/scale stay planned on the public interface; the internal transform
  evaluates them for world-preserving re-parenting and tests. The renderer will consume the
  captured world matrix.
- The internal local matrix is the world source of truth: re-parenting preserves the composed
  world matrix exactly (including shear from a non-uniform scale combined with rotation), and
  `LocalPosition` changes only the translation. `LocalPosition`/`LocalRotation`/`LocalScale` are
  the authored scalar view: getters return the authored value, so negative scale axes and angles
  outside `(-π, π]` round-trip, and the rotation/scale setters recompose the local matrix (so
  writing either discards shear). A re-parent refreshes the scalar view from the exact matrix and
  keeps the authored mirror axis when possible; only then may an authored angle outside
  `(-π, π]` normalize.
- `IEngine.CreateTransform(parent = null)` returns a standalone transform when unparented; it
  is not rendered and is collectible when the game drops it.
- `Parent` attaches to a strong tree. A parented object remains alive even if game code loses
  its reference; detach with `Parent = null` or `DetachAll()` on the parent to release it. The
  same object can be re-attached later. See [Scene ownership](../architecture/scene-ownership.md).
- There is no `Dispose()`/`IsAlive` on transforms. Detaching removes the object from the scene;
  it remains valid.
- Structural attach/detach uses a scene-graph `System.Threading.Lock`; per-object property
  access uses `Lock`, with lock-free/`Interlocked` paths where practical. Concurrent composite
  updates to the same entity still require game synchronization.
- There is no thread-affinity requirement; any thread may call these members during Setup/Tick/
  Teardown.

## 11. Value types

### `Sprite`

```csharp
public readonly record struct Sprite(int Id)
{
    public static readonly Sprite Empty = default;
}
```

- Opaque runtime handle; `Id == 0` is empty.
- Equality by `Id`; no public implicit `int` conversion.
- Not serializable; data/save files use asset paths.

### `AssetInfo`

`AssetInfo(Path, IsRaw, IsCompressed, Length, Size, PackPath)` describes one visible asset in
the merged pack filesystem. `Length` is stored bytes; `Size` is decompressed bytes;
`PackPath` is the pack that supplies the visible entry. No payload is loaded.

### `Color`

```csharp
public readonly record struct Color(float R, float G, float B, float A);
```

- Linear RGBA, `[0,1]`; constants `Transparent`, `Black`, `White`, `Red`, `Green`, `Blue`,
  `Yellow`, `Cyan`, `Magenta`, `Gray`.
- Textures are sRGB, tinted in linear space, output to sRGB. No-sRGB fallback is `Open`.
- No HSV/hex helpers in v1.

### `ClearKind`

| Value | Meaning |
| --- | --- |
| `None` | Preserve earlier cameras/safety clear. |
| `Depth` | Reserved; no depth attachment; no-op with a one-time warning. |
| `Color` | Clear the camera viewport to `ClearColor`. |

CLR default is `None`; `CreateCamera(parent)` explicitly sets `Color`.

### `KeyCode` / `MouseButton`

- `KeyCode` maps physical SDL scancodes; final set `Planned`. Scaffold contains sample members
  only (`A`; `Left`).
- `MouseButton` will cover left/right/middle/back/forward; scaffold contains `Left`.
- Numeric values are unstable; do not serialize them.
- Text uses `OnTextTyped`, not `KeyCode`.

### `SpriteFlags` (planned)

Planned flags for `FlipX`/`FlipY`; the planned snapshot reserves the field.

## 12. Runtime surface

> **Status: `Planned`.** `Bork2d.Runtime` is empty today. Names/signatures may change in M1.

### `Bork2dAppBuilder<TLayer>`

```csharp
public sealed class Bork2dAppBuilder<TLayer> where TLayer : struct, Enum
{
    public Bork2dAppBuilder<TLayer> WithGame(IGame<TLayer> game);

    public Bork2dAppBuilder<TLayer> WithAssetsPack(string path);
    public Bork2dAppBuilder<TLayer> WithLogging(Action<ILoggingBuilder> configure);

    public Bork2dAppBuilder<TLayer> WithTitle(string title);
    public Bork2dAppBuilder<TLayer> WithSize(int width, int height);
    public Bork2dAppBuilder<TLayer> WithResizable(bool resizable);
    public Bork2dAppBuilder<TLayer> WithFullscreen(bool fullscreen);
    public Bork2dAppBuilder<TLayer> WithVSync(bool enabled);

    public Bork2dAppBuilder<TLayer> WithHeadless(int ticksPerSecond = 60);
    public Bork2dAppBuilder<TLayer> WithOffscreen(int width, int height);

    public Bork2dAppBuilder<TLayer> WithMaxDeltaTime(double seconds);
    public Bork2dAppBuilder<TLayer> WithValidation(bool enabled);

    public Bork2dApp Build();
}
```

- Created/configured on any thread; `Build()` validates once.
- `WithGame` takes a **caller-constructed instance**; no `new()`/reflection. The caller keeps
  the instance alive through `Run()`.
- Mode is windowed by default, or mutually exclusive `WithHeadless(ticksPerSecond)`/
  `WithOffscreen(w,h)`.
- `WithHeadless` defaults to 60 ticks/second; `0` means uncapped. This is the only mode with an
  intentional pacing timer.
- Window options apply only to windowed mode and are validated.
- `WithAssetsPack` default is `assets.pck`.
- `WithLogging` configures Microsoft.Extensions.Logging with ZLogger. If omitted, the app adds
  a pretty ZLogger console provider by default. See [Logging](../architecture/logging.md).
- `WithMaxDeltaTime` is a safety clamp, not a pacing delay.
- `WithValidation` defaults to on in debug.
- There is **no** `WithTickRate`; windowed/offscreen pacing is bounded run-ahead, headless uses
  the `WithHeadless` tick rate.

### `Bork2dApp`

```csharp
public sealed class Bork2dApp : IDisposable
{
    public void Run();
    public void Dispose();
}
```

- `Run()` starts/blocks in the full design. In **M1** it is a smoke path: `Setup` → one `Tick` →
  `Teardown`, then returns. M2/M7 replace it with the real loop.
- `Run()` may be called from any thread; no main-thread requirement.
- `Dispose()` is idempotent and safe before/after `Run`; it clears the root and disposes the
  logger. It is app lifetime, not an entity lifetime API.
- Stop with `IEngine.Exit()` or the window close button (windowed).
- Exceptions propagate after cleanup.
- `Run` return shape is `Open`; v1 uses `void` + exceptions.

## 13. Threading matrix

| Operation | Calling thread | Notes |
| --- | --- | --- |
| Scene creation/mutation, `Root.DetachAll` | Any thread | Structural + per-object locks; logical tick window. |
| `IEngine.Exit` | Any thread | Requests shutdown. |
| `ITime` / `IInput` reads | Any thread | Snapshot values. |
| `IWindow` operations | Any thread | Marshaled to the OS/window thread. |
| `IAssets` requests/browse | Any thread | Thread-safe; completion asynchronous. |
| `IGame.Setup/Tick/Teardown` | Engine tick thread | Invoked by the engine; not called by game code. |
| Builder / `Build` / `Run` / app `Dispose` | Any thread | No main-thread requirement. |
| Renderer / asset internals | Internal threads | No public API. |
| Value types | Any thread | Copies. |

The public contract permits any caller. Scene mutation is still logically tick-window scoped,
and same-object composite updates still require game synchronization where atomicity matters.

## 14. Errors and exceptions

| Situation | Contract |
| --- | --- |
| Invalid builder config / layer enum / conflicting modes | `Build()` throws `ArgumentException`/`Bork2dException`; nothing starts. |
| Missing game instance | `Build()` throws. |
| No Vulkan 1.3 device (windowed/offscreen) | `Run()` throws after cleanup. |
| Headless tries GPU/SDL | Configuration error; headless must not initialize them. |
| Missing/corrupt default pack | Startup error: `Bork2dException`; `LoadPack` throws. |
| Missing/invalid asset path | Log + `Sprite.Empty` (proposed); not fatal. |
| Asset not ready | No exception; renderer skips. |
| Raw asset passed to `LoadAsset<T>` | Throws; use `LoadData`. |
| Missing Nerdbank shape (AOT) | Build/startup error naming the type; no reflection fallback. |
| Same-entity concurrent composite update | Not atomic unless the game synchronizes; individual property access is locked. |
| Invalid layer (`0`/composite) | Debug diagnostic; renderer skips. |
| Game exception | Propagates out of `Run()` after shutdown. |
| Renderer fatal / device lost | Propagates out of `Run()` after shutdown; no auto recovery. |

A public `Bork2dException` in `Bork2d.Api` represents engine-originated failures. It exposes the
standard constructors (parameterless, message, message + inner exception). BCL exceptions remain
for argument/programming errors raised directly by callers.

## 15. Status summary: current gaps

| Contract | Current state |
| --- | --- |
| `ISceneRoot` / `IEngine.Root` | Declarations present. Implemented in the internal M1 engine (WP3): read-only identity root that owns the scene. The public host/builder that creates the engine is WP4. |
| Parent-required creators | Implemented (WP3): attached to the supplied parent; a `null` parent throws `ArgumentNullException`. |
| `ITransform.DetachAll` | Implemented (WP3): detaches direct children under the structural lock on any transform. |
| `IEngine.Input` | Declaration present. Scaffold throws `NotSupportedException`; windowed input is M2. |
| `IEngine.Exit` | Declaration present. Scaffold sets a shutdown flag read by the placeholder `Run`; the real loop is WP4. |
| `IEngine.LoggerFactory` | Declaration present. Scaffold returns `NullLoggerFactory.Instance`; the real factory is wired by the logging work. |
| `IGame.Teardown` | Declaration present. The M1 host invokes it (WP4). |
| `Bork2dException` | Present in `Bork2d.Api` with standard constructors. |
| Scene entity `IDisposable` | Removed from `ICamera`/`ISpriteRender` and from the internal Common types (WP3). |
| `ITransform` world reads | Not on the public interface; the internal transform evaluates them for re-parenting and tests. |
| `ICamera` screen/world | Missing. |
| `ISpriteRender` flip | Missing; planned snapshot flag. |
| `IAssets` pack/browse/exists | Missing. |
| `IAssets` unload/readiness | Missing; load-and-keep deferred. |
| Builder/app | `Bork2d.Runtime` empty; `WithGame` currently would require a new API. |
| Threading | Single-threaded placeholder; target any-thread API with locks. |
| Headless/offscreen | Missing. |
| NativeAOT | No analyzers/publish. |
| Camera defaults | Implemented (WP3): size 10, full viewport, all declared layers, black clear. |
| Entity ownership | Root-owned strong tree (WP3); no weak registry, no entity `Dispose`/`IsAlive`. |
| Layer semantics | Undefined; target one bit/mask with validation. |
| Sprite sizing | No assets; target UVs/pivot/topology/optional indices/pixels-per-unit scale. |
| Assets/PCK | `Bork2d.Assets` empty. |

## 16. Related documents

- [API guide](guide.md) — usage examples.
- [Scene ownership](../architecture/scene-ownership.md) — root lifetime model.
- [M1 spec](../specs/m1-foundation.md) — implementation plan.
- [Assets and pack files](../architecture/assets.md) — pack, descriptors, sprites.
- [Logging](../architecture/logging.md) — ZLogger and `IEngine.LoggerFactory`.
- [Editor](../architecture/editor.md) — editor builder (not game API).
- [NativeAOT](../architecture/nativeaot.md) — runtime constraints.
- [Rendering](../architecture/rendering.md) — camera/projection/sorting.
- [Threading](../architecture/threading.md) — threading rules.
- [Roadmap](../roadmap.md) — when `Planned` becomes `Current`.
