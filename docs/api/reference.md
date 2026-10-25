# API Reference

> **Status:** target contract. `Current` means the member exists in `Bork2d.Api` today;
> `Planned` means an agreed addition that is **not** in the scaffold; `Open` means the shape
> or semantics is undecided. The current code is partial; see
> [Projects and dependencies](../architecture/projects.md) and the
> [roadmap](../roadmap.md).

## 1. Contract-wide rules

### 1.1 Thread affinity and modes

The public API is **not generally thread-safe**. It is safe under the tick-scoped model in
[Threading](../architecture/threading.md):

| Scope | Allowed thread |
| --- | --- |
| `ITime`, `IWindow`, `IInput`, `IGame` callbacks, builder/`Run` | Main/tick thread only. |
| `IEngine` scene creation, `ICamera`/`ISpriteRender`/`ITransform` mutation and `Dispose` | Main thread in `Setup`/`Tick`/`Teardown`; game workers inside a tick for **distinct** entities only. |
| `IEngine.Exit` | Main thread only. |
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

Game workers may create entities and mutate entities they own during a tick. They must not
access an entity concurrently with another thread without synchronization, must not call
main-thread-only APIs, and must be joined before `Tick` returns. The engine serializes
structural state (IDs, registries), not concurrent writes to the same entity/property.
Violations are undefined behaviour; debug builds check tick scope and selected thread
boundaries, not arbitrary data races.

### 1.2 Units and conventions

| Quantity | Convention |
| --- | --- |
| Position | World units; +X right, +Y up. |
| Rotation | Radians, positive counter-clockwise around +Z. |
| Scale | Multiplier; negative values mirror. |
| `ICamera.Size` | Half visible height in world units. |
| `ICamera.Viewport` | Normalized `(x, y, width, height)`, top-left origin, `[0,1]`. |
| Sprite asset vertices | Source pixels, relative to origin, +Y down; renderer flips to world Y-up. |
| Sprite asset `scale` | Per-sprite units per source pixel. |
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
| Tick rate | **None.** Pacing is bounded run-ahead by the renderer, or uncapped in headless. |

### 1.4 NativeAOT

The runtime/game is NativeAOT- and trim-safe. There is no runtime reflection, assembly
scanning, runtime TOML, or dynamic code. `LoadAsset<T>` requires a source-generated MessagePack
formatter. The game is passed as an instance. See
[NativeAOT](../architecture/nativeaot.md).

## 2. `IEngine<TLayer>`

```csharp
public interface IEngine<TLayer> where TLayer : struct, Enum
{
    ITime Time { get; }
    IWindow Window { get; }
    IInput Input { get; }        // Planned
    IAssets Assets { get; }
    ITransform CreateTransform();
    ICamera<TLayer> CreateCamera();
    ISpriteRender<TLayer> CreateSpriteRender();
    void Exit();                 // Planned
}
```

| Member | Status | Semantics |
| --- | --- | --- |
| `Time` | `Current` | Monotonic time/delta. Main thread. |
| `Window` | `Current` | Window controls. Windowed only; throws in offscreen/headless. |
| `Input` | `Planned` | Input state. Windowed only; throws in offscreen/headless. |
| `Assets` | `Current` | Pack/asset requests; thread-safe methods. |
| `CreateTransform()` | `Current` | Standalone transform for math/hierarchy; not rendered. Inside `Setup`/`Tick`; workers may create during a tick. |
| `CreateCamera()` | `Current` | Creates/registers a camera; defaults above. Workers may create during a tick. |
| `CreateSpriteRender()` | `Current` | Creates/registers a sprite renderable; defaults above. Workers may create during a tick. |
| `Exit()` | `Planned` | Graceful shutdown after the current tick. Main thread. |

Notes:

- The engine does not own the loop; `Bork2d.Runtime` does. There is no `Run`/`Update` on
  `IEngine`.
- `TLayer` must be an `int`-backed `[Flags]` enum with 1–32 named single-bit values; the
  builder validates it.
- Entities are strongly retained by the engine until `Dispose`; the current weak-reference
  registry is a scaffold detail and will be replaced.
- The engine has no public `Dispose`; `Bork2dApp` owns shutdown.
- Headless mode still creates cameras/sprites but never renders; `Window`/`Input` throw.

## 3. `IGame<TLayer>`

```csharp
public interface IGame<TLayer> where TLayer : struct, Enum
{
    void Setup(IEngine<TLayer> engine);
    void Tick();
    // void Teardown(); // Planned
}
```

| Member | Status | Semantics |
| --- | --- | --- |
| `Setup(IEngine<TLayer>)` | `Current` | Once on the main thread before the first tick. Create entities, request assets. |
| `Tick()` | `Current` | Once per tick on the main thread. Must return; no `async void`; join workers before returning. |
| `Teardown()` | `Planned` | Once on the main thread before shutdown. Dispose entities/resources; asset requests are not guaranteed to complete. |
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

- Read-only, main-thread.
- Variable timestep. **No target tick rate.** Windowed/offscreen pacing is bounded run-ahead
  against the renderer; headless is uncapped. No `Sleep`/deadline pacing.
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
| `Title` | `Current` | Window title. Main thread, windowed only. |
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

Access via `IEngine.Input` (`Planned`). **Windowed only**; offscreen/headless throw.

| Member | Status | Semantics |
| --- | --- | --- |
| `OnTextTyped` | `Current` | UTF-16 `char` events from SDL text input; main thread, before `Tick`. |
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
| `LoadAsset<T>` | `Current` declaration; semantics `Planned` | Loads/decompresses a MessagePack asset with a source-generated formatter. Throws for raw assets/missing paths. |
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
- `LoadAsset<T>` requires a generated formatter. No reflection fallback (NativeAOT).
- `LoadPack`/`Browse`/`Exists` are thread-safe; mount packs during `Setup`/before loading.
- No public pack/asset unload in v1; load-and-keep.
- Default pack is `assets.pck`; missing/malformed behaviour is `Open` (proposed startup error).

## 8. `ICamera<TLayer>`

```csharp
public interface ICamera<TLayer> : ITransform, IDisposable
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
| `Dispose()` | `Current` | Unregisters; inside `Tick`/`Teardown` (main or owning worker; same entity sync required). |
| `ScreenToWorld(Vector2)` | `Planned` | Framebuffer pixels → world units using this camera. |
| `WorldToScreen(Vector2)` | `Planned` | Inverse. |

- Cameras inherit `ITransform`; position/rotation/parent come from the transform. Scale is not
  zoom; use `Size`.
- Camera order is creation order (entity ID). Later camera with `Clear.Color` wipes its
  viewport; `None` overlays.
- Defaults above. `Clear.Depth` is accepted for forward compatibility.
- Works in headless/offscreen, but headless never renders.

## 9. `ISpriteRender<TLayer>`

```csharp
public interface ISpriteRender<TLayer> : ITransform, IDisposable
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
| `Dispose()` | `Current` | Unregisters; inside `Tick`/`Teardown` (main or owning worker; same entity sync). Disappears from the next snapshot. |

- The sprite asset supplies vertices, UVs, texture path, and units-per-pixel scale.
  `LocalScale` multiplies. There is no global PPU and no per-renderable pivot.
- Hide with `Sprite = Sprite.Empty` (keep entity) or `Dispose()`.
- Sorting: layer numeric → `ZIndex` → entity ID. A not-ready asset yields no instance.
- Headless: entities can exist but never produce an instance.

## 10. `ITransform`

```csharp
public interface ITransform
{
    ITransform? Parent { get; set; }
    Vector2 LocalPosition { get; set; }
    float LocalRotation { get; set; }
    Vector2 LocalScale { get; set; }
}
```

| Member | Status | Semantics |
| --- | --- | --- |
| `Parent` | `Current` | Parent or `null`. Planned: re-parenting preserves world transform; cycles rejected. |
| `LocalPosition` | `Current` | Position relative to parent, world units. |
| `LocalRotation` | `Current` | Radians, CCW, relative to parent. |
| `LocalScale` | `Current` | Scale relative to parent; negative mirrors. |
| `WorldPosition` | `Planned` | Read-only evaluated world position. |
| `WorldRotation` | `Planned` | Read-only evaluated world rotation (radians). |
| `WorldScale` | `Planned` | Read-only evaluated world scale. |

- World transforms are evaluated/captured on the tick thread; the renderer never walks the
  hierarchy.
- The scaffold declares global properties on `Transform` but does not compute them; treat them
  as stubs.
- `IEngine.CreateTransform()` returns a standalone transform (not rendered, no `Dispose`).
- Parenting across cameras/sprites is allowed; disposing a parent while children remain is a
  game bug. Same-entity hierarchy mutations need external sync across workers.

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

CLR default is `None`; `CreateCamera()` explicitly sets `Color`.

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

    public Bork2dAppBuilder<TLayer> WithTitle(string title);
    public Bork2dAppBuilder<TLayer> WithSize(int width, int height);
    public Bork2dAppBuilder<TLayer> WithResizable(bool resizable);
    public Bork2dAppBuilder<TLayer> WithFullscreen(bool fullscreen);
    public Bork2dAppBuilder<TLayer> WithVSync(bool enabled);

    public Bork2dAppBuilder<TLayer> WithHeadless();
    public Bork2dAppBuilder<TLayer> WithOffscreen(int width, int height);

    public Bork2dAppBuilder<TLayer> WithMaxDeltaTime(double seconds);
    public Bork2dAppBuilder<TLayer> WithValidation(bool enabled);

    public Bork2dApp Build();
}
```

- Created/configured on the main thread; `Build()` validates once.
- `WithGame` takes a **caller-constructed instance**; no `new()`/reflection. The caller keeps
  the instance alive through `Run()`.
- Mode is windowed by default, or mutually exclusive `WithHeadless()`/`WithOffscreen(w,h)`.
- Window options apply only to windowed mode and are validated.
- `WithAssetsPack` default is `assets.pck`.
- `WithMaxDeltaTime` is a safety clamp, not a pacing delay.
- `WithValidation` defaults to on in debug.
- There is **no** `WithTickRate`; pacing is bounded run-ahead or uncapped headless.

### `Bork2dApp`

```csharp
public sealed class Bork2dApp : IDisposable
{
    public void Run();
    public void Dispose();
}
```

- `Run()` starts/blocks; call once on the main thread.
- `Dispose()` is idempotent and safe before/after `Run`.
- Stop with `IEngine.Exit()` or the window close button (windowed).
- Exceptions propagate after cleanup.
- `Run` return shape is `Open`; v1 uses `void` + exceptions.

## 13. Threading matrix

| Member / operation | Setup/Tick/Teardown (main) | Worker inside tick | Render thread | Asset thread |
| --- | :-: | :-: | :-: | :-: |
| `IEngine` create methods | ✓ | ✓ (distinct entities) | ✗ | ✗ |
| `IEngine.Exit` | ✓ | ✗ | ✗ | ✗ |
| `ITime` reads | ✓ | ✗ | ✗ | ✗ |
| `IWindow`/`IInput` | ✓ (windowed) | ✗ | ✗ | ✗ |
| `IAssets.LoadSprite` / `LoadAsset` / `LoadData` / `LoadPack` / `Exists` | ✓ | ✓ (thread-safe) | ✗ | internal |
| `IAssets.Browse` | ✓ | tooling only | ✗ | internal |
| Camera/sprite/transform mutation | ✓ | ✓ (distinct; sync shared) | ✗ | ✗ |
| `Dispose` on entities | ✓ | ✓ (distinct; sync shared) | ✗ | ✗ |
| `IGame.Setup/Tick/Teardown` | ✓ | ✗ | ✗ | ✗ |
| Builder / `Run` | main only, before loop | ✗ | ✗ | ✗ |
| Value types | ✓ | ✓ | ✓ | ✓ |

`✓` means allowed by the contract; tick-scope and same-entity synchronization still apply.
Asset completion callbacks are not on the tick thread; marshal scene changes to a tick.

## 14. Errors and exceptions

| Situation | Contract |
| --- | --- |
| Invalid builder config / layer enum / conflicting modes | `Build()` throws `ArgumentException`/`Bork2dException`; nothing starts. |
| Missing game instance | `Build()` throws. |
| No Vulkan 1.3 device (windowed/offscreen) | `Run()` throws after cleanup. |
| Headless tries GPU/SDL | Configuration error; headless must not initialize them. |
| Missing/corrupt default pack | `Open`: proposed startup error. `LoadPack` throws. |
| Missing/invalid asset path | Log + `Sprite.Empty` (proposed); not fatal. |
| Asset not ready | No exception; renderer skips. |
| Raw asset passed to `LoadAsset<T>` | Throws; use `LoadData`. |
| Missing MessagePack formatter (AOT) | Build/startup error naming the type; no reflection fallback. |
| Cross-thread main-only access | Debug: throw/assert; release: undefined; same-entity races are game responsibility. |
| Use after `Dispose` | Debug exception where cheap; otherwise undefined. |
| Invalid layer (`0`/composite) | Debug diagnostic; renderer skips. |
| Game exception | Propagates out of `Run()` after shutdown. |
| Renderer fatal / device lost | Propagates out of `Run()` after shutdown; no auto recovery. |

A public `Bork2dException` is planned in `Bork2d.Api` for engine-originated failures. BCL
exceptions remain for argument/programming errors.

## 15. Status summary: current gaps

| Contract | Current state |
| --- | --- |
| `IEngine.Input` / `Exit` | Missing. |
| `IGame.Teardown` | Missing. |
| `ITransform` world reads | Uncomputed class stubs; not in the interface. |
| `ICamera` screen/world | Missing. |
| `ISpriteRender` flip | Missing; planned snapshot flag. |
| `IAssets` pack/browse/exists | Missing. |
| `IAssets` unload/readiness | Missing; load-and-keep deferred. |
| Builder/app | `Bork2d.Runtime` empty; `WithGame` currently would require a new API. |
| Headless/offscreen | Missing. |
| NativeAOT | No analyzers/publish. |
| Camera defaults | Scaffold uses cyan/zero mask; target black/all layers. |
| Entity ownership | Weak references; target strong until `Dispose`. |
| Layer semantics | Undefined; target one bit/mask with validation. |
| Sprite sizing | No assets; target vertices/UVs/scale. |
| Assets/PCK | `Bork2d.Assets` empty. |
| Threading | Single-threaded placeholder; target bounded ring. |

## 16. Related documents

- [API guide](guide.md) — usage examples.
- [Assets and pack files](../architecture/assets.md) — pack, descriptors, sprites.
- [Editor](../architecture/editor.md) — editor builder (not game API).
- [NativeAOT](../architecture/nativeaot.md) — runtime constraints.
- [Rendering](../architecture/rendering.md) — camera/projection/sorting.
- [Threading](../architecture/threading.md) — threading rules.
- [Roadmap](../roadmap.md) — when `Planned` becomes `Current`.
