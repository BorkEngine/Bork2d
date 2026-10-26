# API Reference

> **Status:** the implemented M2 contract for the windowed host (M2 WP1–WP5; pending PR review).
> `Current` means implemented in this tree and covered by tests; `Partial` means the declaration
> exists but behavior is stubbed, unwired, or incomplete; `Planned` means an agreed target that is
> not implemented. Windowed mode runs a real SDL window, input, and time; headless/offscreen keep
> the one-tick M1 path. The asset pipeline is M3 and renderer semantics are M8; see the
> [roadmap](../roadmap.md). Code under `src/Bork2d` is authoritative.

## 1. Contract-wide rules

### 1.1 Threads

**No public API requires the process main thread.** Calls may originate on any thread during the
`Setup`/`Tick`/`Teardown` windows. Game workers must join before `Tick` returns and must not write
scene state afterwards.

| Surface | Calling thread | Serialization |
| --- | --- | --- |
| Scene graph (`IEngine`, `ITransform`, `ICamera`, `ISpriteRender`) | Any thread | One engine-owned structural `System.Threading.Lock`; per-object `Lock` for local properties. |
| `IEngine.Exit`, `ITime` reads | Any thread | Three-state exit token / locked snapshot values. |
| `IInput` | Any thread | Tick-start frozen snapshot; the windowed loop publishes it once per tick. |
| `IWindow` | Any thread | Setters enqueue a command for the OS/window thread; getters read the last confirmed immutable snapshot. |
| `IAssets` (M3) | Any thread | Thread-safe requests; asynchronous completion. |
| `IGame` callbacks | Engine tick thread | Invoked by the host; never concurrent. |
| Builder, `Build`, `Run`, `Dispose`, `LoggerFactory` | Any thread | No main-thread requirement; MEL factories/loggers are thread-safe. |
| Value types | Any thread | Copies. |

- No lock is held across an await or blocking wait; there are no thread-affinity assertions.
- Concurrent composite updates to one object are the game's synchronization responsibility.
- Frame snapshots are captured after `Tick` (M7); workers must not write scene state afterwards.

Mode behavior in M2: **windowed** (the default) creates one visible SDL window on an engine-owned
OS/window thread, runs an uncapped `Tick` loop until `Exit()`/close/fatal error, makes
`IWindow`/`IInput` real, and advances `ITime`. **Headless** and **offscreen** create no SDL window,
thread, or input, keep the one-tick `Setup` → `Tick` → `Teardown` path, return a throwing
`IWindow` stub, throw `NotSupportedException` from the `IInput` getter, and keep `ITime` at `0`.

### 1.2 Units and conventions

World space uses world units, +X right, +Y up; rotation is degrees, CCW, normalized to `[0, 360)`;
scale is a multiplier (negative mirrors). Time is monotonic seconds, input is framebuffer pixels
from the top-left, and an integer `ZIndex` sorts higher later. Sprite asset geometry is in
[Assets and tooling](../architecture/assets-and-tooling.md).

### 1.3 Defaults

| Option / object | Default |
| --- | --- |
| Mode / title / validation | Windowed / entry assembly name, else `Bork2d` / on in Debug, off in Release. |
| Window | Visible at `Run()` start; 1280x720 framebuffer pixels; resizable `true`, fullscreen `false`, cursor visible `true`, minimized `false`, VSync `true` (stored). |
| Assets pack | The relative string `"assets.pck"`; not resolved next to the executable in M2. |
| Max delta | `0.25` s gameplay clamp in windowed mode, not a pacing delay. |
| Headless / logging | `60` ticks/s stored (`0` = uncapped; one tick in M2) / ZLogger console, local time ms, no colors or file sink; minimum Debug (Debug) or Information (Release). |
| Camera | `Size = 10` (half-height), `Viewport = (0,0,1,1)`, `CullingMask` = all declared flags, `Clear = Color`, `ClearColor = Black`, `ZIndex = 0`. |
| Sprite renderable | `Sprite.Empty`, `Layer = 0` (**invalid**; skipped by the future renderer), `Tint = White`, `ZIndex = 0`. |

### 1.4 Public surface and NativeAOT

`Bork2d.Api` declares the interfaces in §2–§10 plus the value types `Sprite`, `Color`,
`ClearKind`, `KeyCode`, `MouseButton`, and `Bork2dException`; `ILoggerFactory` is a
Microsoft.Extensions.Logging type exposed by `IEngine` (not defined in `Bork2d.Api`), and
`AssetInfo` is `Planned` for M3. The runtime is NativeAOT- and trim-safe: no reflection, assembly
scanning, runtime TOML, or dynamic code; `LoadAsset<T>` needs a
[Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) generated shape and the
game is passed as an instance. See [NativeAOT](../architecture/nativeaot.md).

## 2. `IEngine<TLayer>`

```csharp
public interface IEngine<TLayer> where TLayer : struct, Enum
{
    ITime Time { get; }
    IWindow Window { get; }
    IInput Input { get; }
    IAssets Assets { get; }
    ILoggerFactory LoggerFactory { get; }   // Microsoft.Extensions.Logging
    ISceneRoot Root { get; }
    ITransform CreateTransform(ITransform? parent = null);
    ICamera<TLayer> CreateCamera(ITransform parent);
    ISpriteRender<TLayer> CreateSpriteRender(ITransform parent);
    void Exit();
}
```

`ISceneRoot` adds no members to `ITransform`: it is a read-only identity root whose parent/local
setters throw `InvalidOperationException`. `Root.DetachAll()` detaches the scene's direct children,
which stay valid and re-attachable. Use `ISceneRoot`/`ITransform` only; the root is the owner node,
not a concrete transform type.

| Member | Status | Semantics |
| --- | --- | --- |
| `Time` | `Current` (M2) | Monotonic time/delta snapshot. Windowed mode advances it once per tick (first tick both `0`); headless/offscreen never advance it, so both stay `0`. |
| `Window` | `Current` (M2) | The property access never throws. Windowed mode returns the internal proxy attached for the duration of `Run()`; headless/offscreen return an `UnsupportedWindow` whose **members** throw `NotSupportedException`. After a windowed run, retained references throw `ObjectDisposedException` from their members. |
| `Input` | `Current` (M2) | Windowed mode returns the internal input proxy; the getter itself never throws there. Headless/offscreen have no input, so the getter itself throws `NotSupportedException`. After a windowed run, retained references throw `ObjectDisposedException` from their members. |
| `Assets` | `Partial` | Returns `UnsupportedAssets`; `LoadAsset<T>`, `LoadSprite`, and `LoadData` throw `NotSupportedException` (M3). |
| `LoggerFactory` / `Root` | `Current` | App-owned MEL/ZLogger factory (non-null; create per-category loggers; disposed by `Bork2dApp.Dispose()`) / read-only root of the strong scene graph. |
| `CreateTransform` / `CreateCamera` / `CreateSpriteRender` | `Current` | Create a node under `parent`; the two renderable creators require a parent (`null` throws `ArgumentNullException`). A new node keeps default local values and starts at the parent's origin. |
| `Exit()` | `Current` (M2) | Windowed: the current tick finishes and the loop stops before the next freeze. A request during `Setup` skips every tick but still tears down once. Headless/offscreen ignore it (one tick). A no-op after the run or disposal. |

- The engine does not own the loop; `Bork2d.Runtime` does, and `IEngine` has no public
  `Run`/`Update`/`Draw`. `TLayer` must be an `int`-backed `[Flags]` enum with 1–32 named
  single-bit values (`None = 0` allowed, composites rejected), validated by `Build()`.
- **Ownership:** `Root` strongly owns parented objects and unparented objects are collectible;
  there is no registry, weak reference, or entity `Dispose`/`IsAlive`. Detach with
  `Parent = null`; see [Scene ownership](../architecture/scene-ownership.md).

## 3. `IGame<TLayer>`

`Status: Current.` `IGame<TLayer>` declares three `void` members:

| Member | Semantics |
| --- | --- |
| `Setup(IEngine<TLayer> engine)` | Once on the engine tick thread before the first tick. Create entities here; do not block on asset loading. |
| `Tick()` | Once per tick on the engine tick thread. Must return; no `async void`; join workers before returning. |
| `Teardown()` | Once on the engine tick thread before shutdown. Release game resources; the app detaches the root afterwards. Asset requests are not guaranteed to complete. |

The callbacks are never concurrent and are invoked by the host, not game code; exceptions propagate
out of `Bork2dApp.Run()` after cleanup (§12). There is no `Run`/`Update`/`Draw` on the game.

## 4. `ITime`

`Status: Current (M2, windowed).` `ITime` declares `double DeltaTime { get; }` and
`double Time { get; }`. The windowed host advances it once per tick, before text dispatch and
`Tick`; headless/offscreen run one tick and never advance it.

| Property | Semantics |
| --- | --- |
| `Time` | `0` on the first windowed tick; afterwards the raw monotonic `Stopwatch` seconds since that tick, never clamped. `0` in the one-tick modes. |
| `DeltaTime` | Exactly `0` on the first tick; afterwards the raw interval between tick starts, clamped to `MaxDeltaTime`. `0` in the one-tick modes. |
| `MaxDeltaTime` | Builder value (`0.25` s default) validated by `Build()`; a gameplay clamp, not a pacing delay. |

- No `TickCount`, fixed timestep, accumulator, or render interpolation. Windowed/offscreen begin
  with no pacing wait at all (M7 adds frame-handoff backpressure); headless pacing is M7.
- The clock is never wall-clock time and does not pause on focus loss or minimization; games can
  pause themselves.
- Reads are thread-safe locked snapshots, so a `double` can never be observed torn. `ITime` keeps
  its last values readable after `Run()` returns or the app is disposed.

## 5. `IWindow`

`Status: Current (M2, windowed).` In windowed mode `IEngine.Window` returns the internal proxy
attached to the live OS/window-thread platform for the duration of `Run()`; the window is created
visible before `Setup` and destroyed after `Teardown`. In headless/offscreen it returns the
existing `UnsupportedWindow`, and every member (including `IsMinimized`) throws
`NotSupportedException`. After a windowed run, retained references' members throw
`ObjectDisposedException` (D-M2-17); the `IEngine.Window` getter itself never throws.

```csharp
public interface IWindow
{
    string Title { get; set; }
    Vector2 Size { get; set; }
    bool Fullscreen { get; set; }
    bool CursorVisible { get; set; }
    bool IsMinimized { get; }     // new in M2
}
```

| Member | Semantics |
| --- | --- |
| `Title` | Non-null; empty and whitespace-only titles are allowed (unlike `WithTitle`). Initial value is the builder title. The setter queues and returns; the getter returns the last confirmed title and may lag one event pump. |
| `Size` | Framebuffer **physical pixels**. Getter: last confirmed non-zero size, including while minimized. Setter: finite and `> 0` components, queued and converted to window points with the current display scale; ignored while fullscreen with a debug log; while minimized it becomes the size restored later. Rounding is an implementation detail; the getter reflects the measured reality. |
| `Fullscreen` | `true` requests borderless desktop fullscreen on the primary monitor; `false` returns to windowed mode at the previous bounds (SDL-managed). Initial value is the builder option. Same-value requests are dropped; the getter returns the last confirmed state. |
| `CursorVisible` | Default `true`. No capture, no relative mode, no automatic hiding, including in fullscreen. Same-value requests are dropped. |
| `IsMinimized` | `true` while the window is minimized. Read-only, readable from any thread; ticks continue while minimized. |

- All members may be called from any thread. Setters enqueue a command for the engine-owned
  OS/window thread and never block on SDL; commands coalesce per property (latest value wins,
  order between different properties preserved). Getters read an immutable confirmed snapshot and
  may lag one pump (D-M2-04).
- In **headless/offscreen** the returned stub's members (including `IsMinimized`) throw
  `NotSupportedException`. After windowed shutdown, retained `IWindow` references throw
  `ObjectDisposedException` (D-M2-17). Commands issued during `Teardown` are best-effort: the
  window is destroyed immediately afterwards.
- Defaults: builder title, 1280x720 framebuffer pixels, resizable `true`, fullscreen `false`,
  cursor visible, not minimized. Window position, monitor selection, icons, minimum/maximum size,
  and `IsFocused`/`IsVisible`/DPI-scale members are not part of M2.
- Errors: `Title = null` throws `ArgumentNullException`; a non-positive, NaN, or infinite `Size`
  component throws `ArgumentOutOfRangeException`; both are thrown by the setter before queueing.


## 6. `IInput`

`Status: Current (M2, windowed).` The OS-thread accumulator, one-tick edges, focus handoff, text
queue, and tick-freeze snapshot are wired by the windowed host: each tick freezes input, advances
the clock, dispatches text, then calls `Tick`. In headless/offscreen there is no input at all, so
`IEngine.Input` throws `NotSupportedException` from the getter.

| Member | Semantics |
| --- | --- |
| `event Action<char> OnTextTyped` | UTF-16 `char`s from committed operating-system text input, raised on the tick thread in arrival order before `Tick`. A surrogate pair is two events; malformed encoded text is replaced by U+FFFD. Adding/removing a handler is thread-safe. |
| `bool IsKeyPressed(KeyCode)` / `IsKeyReleased(KeyCode)` | True only during the tick of the up→down / down→up transition. Key repeat creates no press edge. |
| `bool IsKeyDown(KeyCode)` | Held state at tick time. |
| `Vector2 MousePosition` | Framebuffer **physical pixels**, top-left origin; not logical points or world units. Starts at `(0,0)`, keeps its last value while unfocused, and is re-synchronized on focus regain. |
| `Vector2 MouseDelta` | Difference between the current and the previous freeze position in framebuffer pixels. Zero on the first freeze, the focus-loss freeze, the focus-regain freeze, and while unfocused. |
| `float MouseWheelDelta` | Accumulated vertical wheel ticks since the previous freeze; positive means up/away, independent of the OS natural-scrolling setting. Reset after each freeze and cleared on focus loss. |
| `bool IsMousePressed/Released/Down(MouseButton)` | Edge/held semantics for the five buttons `Left`, `Right`, `Middle`, `Back` (X1), and `Forward` (X2). |

- Windowed only. In headless/offscreen the `IEngine.Input` getter itself throws
  `NotSupportedException`; after windowed shutdown the returned object's members throw
  `ObjectDisposedException`. The input proxy is available from `Setup` (empty snapshot) through
  `Teardown` (most recent snapshot).
- The loop is uncapped: reads are taken once per tick, and there is no per-frame input logging.
- Input is frozen at tick start and reads are stable for the whole `Tick`; events that arrive during
  `Tick` belong to the next freeze. Reads during `Setup` return a fully empty snapshot (all queries
  `false`, zero position/delta/wheel), so keys held during `Setup` produce press edges at the first
  tick. Reads during `Teardown` return the most recently frozen snapshot.
- A down and up inside one interval report both edges with `Down == false`; multiple cycles in one
  interval still produce one tick of edges and `Down` reflects the final state.
- Focus loss synthesizes a release for every held key and button, clears held state, wheel, and
  pending text, and reports a zero delta. Focus regain re-seeds `Down` and the mouse position from the
  operating system without press edges and also reports a zero delta. Transitions apply in event
  order, so a loss and regain between two freezes leave the loss's release edges plus the re-seeded
  held state.
- Text input is enabled while the window is focused and stopped on focus loss and before shutdown.
  The engine never synthesizes text from key presses; IME/composition, clipboard, and drag-and-drop
  are out of scope.
- `KeyCode` is the physical scancode set, not a character or layout name; `MouseButton` is the five
  supported buttons. Numeric values are unstable and must never be serialized. An undefined enum
  value is a safe query that returns `false` and throws nothing. Input is never part of a render
  snapshot.

## 7. `IAssets`

`Status: Partial.` Asset behavior is `Planned` for M3. `public interface IAssets` declares
`Task<T> LoadAsset<T>(string) where T : class`, `Sprite LoadSprite(string)`, and
`Task<byte[]> LoadData(string)`. In M2 `IEngine.Assets` returns an `UnsupportedAssets`; all three
methods throw `NotSupportedException`.

`Planned for M3:` `LoadPack(string packPath)`, `Browse(string? prefix = null)`,
`Exists(string assetPath)`, and `AssetInfo(Path, IsRaw, IsCompressed, Length, Size, PackPath)`.
`LoadPack` mounts above the current stack (later wins, no unload); `Browse` returns the merged
visible snapshot; `AssetInfo` describes one entry without loading a payload.

Target rules (M3):

- Paths are canonical lowercase `/` keys without source extensions.
- `LoadSprite` is idempotent per path; the handle is session-local and never persisted. A sprite
  is skipped until its sprite asset, texture descriptor, raw payload, and GPU page are ready
  (M8); no blocking, no placeholder. A missing key returns `Sprite.Empty` and logs.
- `LoadAsset<T>` loads a MessagePack asset with a Nerdbank generated shape and throws for raw
  assets; use `LoadData` for arbitrary bytes.
- The default pack is the relative string `"assets.pck"`. `Build()` validates only that the path
  is non-empty and never touches the filesystem in M2. A missing or malformed base pack throws
  `Bork2dException` at startup (no empty-filesystem fallback).
- No public pack/asset unload in v1; mount packs during `Setup`/before loading. Missing/failed
  assets log; the retry policy is decided in M3 (see roadmap).

## 8. `ICamera<TLayer>`

`Status: Partial.` M2 properties store values; renderer semantics are `Planned` (M8): no
clamping, zero-size skip, positive-`Size` check, or diagnostic yet. Target semantics (M8):

| Member | Semantics |
| --- | --- |
| `Vector4 Viewport { get; set; }` | Normalized, top-left. Clamped; a zero size skips the camera with a warning. |
| `float Size { get; set; }` | Half visible height in world units; positive. |
| `TLayer CullingMask { get; set; }` | Combination of layer flags; `0` sees nothing. |
| `ClearKind Clear { get; set; }` | `Color` clears the viewport, `None` preserves earlier content, `Depth` is a no-op with a one-time warning. |
| `Color ClearColor { get; set; }` | Linear RGBA for `Clear.Color`. |
| `int ZIndex { get; set; }` | Camera order: ascending, higher applies later, default `0`; ties are unspecified. |
| `ScreenToWorld` / `WorldToScreen` | `Planned`: framebuffer pixels ↔ world units using this camera. |

- Cameras inherit `ITransform`; scale is not zoom, so use `Size` and create with
  `IEngine.CreateCamera(parent)` under `Root` or a rooted transform. A later `Clear.Color` camera
  wipes its viewport and `None` overlays; headless works but never renders. Cameras are not
  `IDisposable` and have no `IsAlive`; detach with `Parent = null` or clear the root.

## 9. `ISpriteRender<TLayer>`

`Status: Partial.` M2 properties store values; renderer semantics are `Planned` (M8): no
validation or diagnostic runs yet. Target semantics (M8):

| Member | Semantics |
| --- | --- |
| `Sprite Sprite { get; set; }` | Opaque sprite asset handle; `Empty` draws nothing; a not-ready handle is skipped. |
| `TLayer Layer { get; set; }` | Exactly one non-zero bit; `0` and composites are invalid and skipped with a diagnostic. |
| `Color Tint { get; set; }` | Linear RGBA multiplied with the texture; `White` is neutral. |
| `int ZIndex { get; set; }` | Sort within a layer; higher draws later; ties are unspecified. |
| `FlipX`, `FlipY` | `Planned`: mirror the sprite without changing the transform. |

- The sprite asset supplies UVs, pivot, topology, optional `indices`, texture path, and a
  **pixels-per-unit** scale; `LocalScale` multiplies it. There is no global PPU or vertices array.
- Sorting is layer numeric value, then `ZIndex`; ties are unspecified. Hide with
  `Sprite = Sprite.Empty` or detach with `Parent = null`. Headless produces no instance, and the
  object is not `IDisposable` and has no `IsAlive`.

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

`Status: Current.`

| Member | Status | Semantics |
| --- | --- | --- |
| `Parent` | `Current` | Parent or `null`; accepts only engine-owned scene nodes from the same engine. Re-parenting preserves the world matrix; self/cycles and setting the root's own parent throw `InvalidOperationException`; a non-engine parent or another engine's node throws `Bork2dException`. |
| `DetachAll()` | `Current` | Detaches direct children under the structural lock; grandchildren remain attached. Any transform can call it. |
| `LocalPosition` | `Current` | Position relative to the parent, world units; changes only the matrix translation. |
| `LocalRotation` | `Current` | Degrees, CCW, relative to the parent; normalized to `[0, 360)` on assignment and by the getter. Non-finite values throw `ArgumentOutOfRangeException`; matrix math uses radians. |
| `LocalScale` | `Current` | Scale relative to the parent; negative mirrors. |
| `WorldPosition` / `WorldRotation` / `WorldScale` | `Planned` | Not on the public interface; the internal transform evaluates them for re-parenting and tests. |

- **Parent rules.** A parent must be the root or a transform created by the same engine. A node
  from a different engine scene throws `Bork2dException`; a non-engine `ITransform` parent throws
  `Bork2dException`; assigning self, creating a cycle, or setting the root's own parent throws
  `InvalidOperationException`; a singular parent world matrix throws `Bork2dException`. Root
  parent/local setters throw `InvalidOperationException`. `CreateTransform(parent = null)` returns
  a standalone, non-rendered transform that is collectible when the game drops it.
- The internal local matrix is the world source of truth: re-parenting preserves the composed
  world matrix exactly, including shear from a non-uniform scale plus rotation. The position
  setter changes only the translation; rotation/scale setters recompose from the authored
  scalars and discard shear.
- A parent retains children strongly. Detaching keeps the object and state valid for
  re-attachment, and there is no `Dispose()`/`IsAlive`, child enumeration, ordering, or entity
  identity. See [Scene ownership](../architecture/scene-ownership.md).
- Structural attach/detach uses the engine-owned structural `Lock`; local properties use
  per-object `Lock`s. Any thread may call these members during `Setup`/`Tick`/`Teardown`.

## 11. Value types

`Sprite` is `readonly record struct Sprite(int Id)` with `static readonly Sprite Empty = default`.
`Id == 0` is empty; equality is by `Id`; no public `int` conversion; persist asset paths, not IDs.

`Color` is `readonly record struct Color(float R, float G, float B, float A)`: linear RGBA in
`[0,1]` with `Transparent`, `Black`, `White`, `Red`, `Green`, `Blue`, `Yellow`, `Cyan`,
`Magenta`, and `Gray`; linear-space tint, sRGB output; a device without an sRGB format is an M6
startup failure (see roadmap).

`ClearKind` values:

| Value | Meaning |
| --- | --- |
| `None` | Preserve earlier cameras/safety clear. |
| `Depth` | Reserved; no depth attachment; a no-op with a one-time warning when rendering lands (M8). |
| `Color` | Clear the camera viewport to `ClearColor`. |

`KeyCode` maps the physical SDL scancode set: one member per named SDL scancode (247 members,
including `Unknown = 0`), named after the SDL name without the `SDL_SCANCODE_` prefix and split into
readable PascalCase words (`SDL_SCANCODE_PAGEUP` → `PageUp`, `SDL_SCANCODE_CAPSLOCK` → `CapsLock`,
`SDL_SCANCODE_PRINTSCREEN` → `PrintScreen`, `SDL_SCANCODE_AUDIOFASTFORWARD` → `AudioFastForward`).
`KP_` becomes `Keypad` (`SDL_SCANCODE_KP_0` → `Keypad0`), `NONUS` becomes `NonUs`
(`SDL_SCANCODE_NONUSBACKSLASH` → `NonUsBackslash`), and top-row digits become `Digit1`…`Digit0`.
Values equal the SDL scancode integers. `MouseButton` is exactly `Left`, `Right`, `Middle`, `Back`
(X1), and `Forward` (X2), with values equal to SDL's one-based button index (`Left = 1`,
`Middle = 2`, `Right = 3`, `X1 = 4`, `X2 = 5`). Numeric values of both enums are unstable, so do not
serialize them; an undefined value is a safe query that returns `false`. Text uses `OnTextTyped`.

## 12. Runtime surface

`Status: Current (M2).` The builder, windowed loop and one-tick app, validation, app-owned logging,
window/input proxies, and time are implemented. GPU rendering, pack loading, and renderer-paced
pacing remain later milestones.

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

- `WithGame` takes a **caller-constructed instance** (no `new()`/reflection), must be called
  exactly once, and the caller keeps it alive through `Run()`. Configuration and `Build()` may run
  on any thread; `Build()` validates once, and defaults are in §1.3.
- Modes are windowed (default), `WithHeadless(ticksPerSecond)`, or `WithOffscreen(w, h)`. The
  windowless modes are mutually exclusive; window-only options (`WithTitle`, `WithSize`,
  `WithResizable`, `WithFullscreen`, `WithVSync`) are rejected in them. `WithHeadless` defaults
  to 60 and `0` means uncapped; in M2 the rate is stored and validated but the mode still runs one
  tick (pacing is M7).
- `WithLogging(Action<ILoggingBuilder>)` runs once at `Build` after the minimum level is applied;
  it owns provider configuration, otherwise a pretty ZLogger console provider is installed. The
  app disposes the factory ([Logging](../architecture/logging.md)).
- `WithAssetsPack` accepts a non-empty path; the default is the relative string `"assets.pck"`,
  and the file need not exist in M2. `WithMaxDeltaTime` is a safety clamp for windowed
  `ITime.DeltaTime`, not a pacing delay; `WithValidation` is stored for M6 GPU validation (M2 has
  no GPU).
- `Build()` validates: game present exactly once; windowless modes mutually exclusive; `TLayer`
  valid; positive window/offscreen sizes; window options only in windowed mode; max delta > 0 and
  not NaN (positive infinity passes); headless rate >= 0; non-empty title and pack path. It then
  creates the app-owned logging factory, logs one startup message from the `Bork2d.Runtime`
  category (mode, `validation=passed`, layer type), and creates the options, clock, internal
  engine, and app runtime. Windowed builds get unattached window/input proxies; headless/offscreen
  get the throwing window stub and no input. It creates no SDL state, window, GPU resources,
  threads, or filesystem state; a logging factory created before a later failure is disposed.

### `Bork2dApp`

`public sealed class Bork2dApp : IDisposable` exposes `void Run()` and `void Dispose()`.

- **Windowed `Run()`** creates the SDL window on the engine-owned OS/window thread (visible before
  `Setup`), attaches the window/input proxies, runs an uncapped `Setup` → `Tick`… → `Teardown` loop
  until `IEngine.Exit()`, the window close button/`SDL_QUIT`, or a fatal platform error, then
  destroys the window, quits SDL, and joins the OS thread. It never sleeps, spins, or paces.
- **Headless/offscreen `Run()`** keeps the M1 one-tick path: `Setup`, exactly one `Tick`, then
  `Teardown`, on the caller thread, with no platform and no clock advance.
- `Run()` may run on any thread and only once (a second call throws `InvalidOperationException`;
  after `Dispose()` it throws `ObjectDisposedException`), and a failed run still counts as the one
  run. Exit during `Setup` finishes `Setup`, runs zero ticks, tears down once, and returns.
- If `Setup` throws, the exception propagates and `Teardown` is not called; in windowed mode the
  platform is still shut down. If `Tick` (or a text handler) throws, `Teardown` is still attempted
  and the original exception is rethrown; if both throw, `Run()` throws an `AggregateException`
  holding both. A window start failure throws `Bork2dException` (with the SDL error text) before
  `Setup`, with no `Teardown`. A fatal platform error after startup stops the loop at the next
  boundary, attempts `Teardown`, and rethrows the fatal `Bork2dException`. Every windowed path
  destroys the window and joins the OS thread. A failed run detaches the root's direct children.
- **Accepted fatal-path exception (D-033).** After an unrecoverable post-startup SDL error, the OS
  thread runs its cleanup immediately and may destroy the window before the tick thread reaches
  `Teardown`; the window/input proxies stay attached so `Teardown` can still attempt best-effort
  access (getters generally work, setters may be dropped). Normal, `Exit`, and close paths keep the
  window alive through `Teardown` and destroy it only afterwards.
- `Dispose()` is idempotent and safe before or after `Run`; it detaches the root's direct children,
  detaches/marks the window and input proxies disposed, disposes the app-owned logging factory and
  providers, and never calls `IGame.Teardown`. During an active `Run()` it throws
  `InvalidOperationException` from any thread without disturbing the loop (D-M2-18); stop with
  `IEngine.Exit()` or the window close button first.
- After `Run` returns or the app is disposed, retained `IWindow`/`IInput` references remain valid
  objects but their members throw `ObjectDisposedException`; `ITime` keeps its last values
  readable, and `IEngine.Exit()` is a no-op. `Run` returns `void`; failure is reported by
  exceptions.

## 13. Errors and exceptions

| Situation | Contract |
| --- | --- |
| Invalid builder config (missing/duplicate game, conflicting modes, invalid layer enum, non-positive size/delta, negative headless rate) | `Build()` throws `Bork2dException`; nothing starts. |
| Null builder argument or null `CreateCamera`/`CreateSpriteRender` parent | `ArgumentNullException`; a blank string throws `ArgumentException` at the call. |
| Non-engine or other-engine parent; singular parent world matrix | `Bork2dException`. |
| Self/cycle parent; setting the root's own parent or a root local value | `InvalidOperationException`. |
| SDL init / window creation / platform-thread startup failure | Windowed `Run()` throws `Bork2dException` containing `SDL_GetError()` before `Setup`; no `Teardown`; the window and OS thread were already cleaned up. |
| Game callback exceptions | `Setup` propagates without `Teardown`; a `Tick` or text-handler failure still tears down and rethrows the original; `AggregateException` when `Tick` and `Teardown` both throw. The window and OS thread are cleaned up in every path. |
| Fatal platform error after windowed startup | Loop stops at the next boundary; `Teardown` is attempted; the fatal `Bork2dException` is rethrown (`AggregateException` if `Teardown` also throws). The OS thread's cleanup may already have destroyed the window before `Teardown`; the proxies stay attached for best-effort access (D-033). |
| `Bork2dApp.Dispose()` during an active `Run()` | `InvalidOperationException` from any thread; the loop is unaffected. |
| Second `Run()`, or `Run()` after `Dispose()` | `InvalidOperationException` / `ObjectDisposedException`. |
| `IWindow`/`IInput` member after windowed shutdown | `ObjectDisposedException` (D-M2-17). Headless/offscreen `IWindow` members and the `IInput` getter throw `NotSupportedException`. |
| Undefined `KeyCode`/`MouseButton` query | `false`; never throws. |
| Input text handler throws | Treated as a `Tick` failure: no further text dispatch, loop stops, `Teardown` attempted, the original exception rethrown. |
| `IWindow.Size` non-positive/NaN/infinite, or `IWindow.Title = null` | `ArgumentOutOfRangeException` / `ArgumentNullException` from the setter, before enqueueing. |
| Missing/corrupt default pack, or missing asset (M3) | Pack: `Bork2dException` at startup; asset: log plus `Sprite.Empty`, not fatal. |

`Bork2dException` lives in `Bork2d.Api` with the standard constructors; BCL exceptions remain for caller argument errors.

## 14. Related documents

- [API guide](guide.md) — usage examples; [Roadmap](../roadmap.md) — milestone ownership.
- [Scene ownership](../architecture/scene-ownership.md) — root lifetime and parent rules;
  [Threading](../architecture/threading.md) — any-thread API, locks, snapshots.
- [Runtime lifecycle](../architecture/runtime-lifecycle.md) — builder, modes; [Logging](../architecture/logging.md) — ZLogger and `IEngine.LoggerFactory`.
- [Assets and tooling](../architecture/assets-and-tooling.md) — pack, VFS, descriptors, sprites;
  [Rendering](../architecture/rendering.md) — camera/projection/sorting.
