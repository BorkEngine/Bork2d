# API Guide

> **Status:** the builder and host exist in `Bork2d.Runtime`; `Build()` validates and `Run()`
> performs one `Setup` → `Tick` → `Teardown` sequence. Window/input are M2, the asset pipeline is
> M3, and the renderer is M8. In M1 `IWindow` returns a stub whose members throw
> `NotSupportedException`, the `IInput` getter throws, and `IAssets` methods throw. See the
> [API reference](reference.md#12-runtime-surface).

## 1. What you reference

| Project | References | Contains |
| --- | --- | --- |
| `Project.Common` | `Bork2d.Api` only | Gameplay classes, custom data types, source assets. |
| `Project.Desktop` | `Bork2d.Runtime` + `Project.Common` | Entry point that builds and runs the app. |
| `Project.Editor` (M5) | `Bork2d.Editor` + `Project.Common` | Thin Avalonia editor host. |

Do not reference `Bork2d.Render`, `Bork2d.Common`, or `Bork2d.Assets` from game code; they are
implementation details ([Projects and dependencies](../architecture/projects.md)).

## 2. Start the engine

Construct the game yourself, then pass the instance to the builder:

```csharp
using Bork2d.Runtime;

var game = new MyGame(logger, saveStore); // caller-constructed instance

using var app = new Bork2dAppBuilder<GameLayer>()
    .WithGame(game)
    .WithTitle("My Game")
    .WithSize(1280, 720)
    .WithAssetsPack("assets.pck")
    .Build();

app.Run();
```

- `WithGame` takes an **instance**, so constructor injection works for services and test doubles.
  There is no parameterless-constructor or reflection requirement. The game must stay alive through
  `Run()`.
- `Build()` validates the configuration. `Run()` blocks until shutdown in the full design; in M1 it
  is a smoke path that calls `Setup`, exactly one `Tick`, and `Teardown`, then returns. No window,
  GPU, pack, render thread, or pacing loop starts in M1.
- `using`/`Dispose()` detaches the root's direct children and disposes the app-owned logger
  factory. It never calls `Teardown` again.
- The builder, `Build`, `Run`, and `Dispose` have no thread affinity. See
  [`IEngine<TLayer>`](reference.md#2-ienginetlayer) for the engine surface.

### Modes

| Mode | Selection | M1 behavior | Target |
| --- | --- | --- | --- |
| Windowed (default) | `WithTitle`/`WithSize`/`WithVSync` | Validates; one-tick smoke. | SDL window and input (M2); renderer (M8). |
| Offscreen | `WithOffscreen(width, height)` | Same one-tick smoke. | Windowless rendering; frames discarded in v1 (M6/M8). |
| Headless | `WithHeadless(ticksPerSecond = 60)` | Same one-tick smoke. | No SDL/Vulkan/render thread; paced to the rate, `0` = uncapped (M7). |

The modes are mutually exclusive, and window-only options are rejected in headless/offscreen.
They are separate hosts, not three modes in one process. In M1 all three build and validate but run
the same smoke; `IWindow` members and `IInput` throw regardless. See
[Runtime lifecycle](../architecture/runtime-lifecycle.md#2-modes).

### Logging

The host creates an app-owned Microsoft.Extensions.Logging/ZLogger factory at `Build()`. Without
`WithLogging`, the default is a pretty ZLogger console provider with local time in milliseconds,
level, and category. A supplied `WithLogging(logging => { ... })` is invoked exactly once, receives
the `ILoggingBuilder`, owns provider configuration, and the app disposes the factory and providers.

`Build` logs one startup message from the `Bork2d.Runtime` category with the mode,
`validation=passed`, and the layer type. Create game loggers from `engine.LoggerFactory` in
`Setup`; the factory is not available in the game constructor. See
[Logging](../architecture/logging.md).

## 3. Write a game

`MyGame.cs` implements `IGame<GameLayer>`; the `Program.cs` host is in §2.

```csharp
using System.Numerics;
using Bork2d.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

[Flags]
public enum GameLayer
{
    None       = 0,
    Background = 1 << 0,
    World      = 1 << 1,
    Ui         = 1 << 2,
}

public sealed class MyGame : IGame<GameLayer>
{
    private ILogger<MyGame> _logger = NullLogger<MyGame>.Instance;
    private IEngine<GameLayer> _engine = null!;
    private ICamera<GameLayer> _camera = null!;
    private ISpriteRender<GameLayer> _player = null!;

    public void Setup(IEngine<GameLayer> engine)
    {
        _engine = engine;
        _logger = engine.LoggerFactory.CreateLogger<MyGame>();

        _camera = engine.CreateCamera(engine.Root);
        _camera.Size = 5f; // half visible height: 10 world units tall
        _camera.CullingMask = GameLayer.Background | GameLayer.World;
        _camera.Clear = ClearKind.Color;
        _camera.ClearColor = Color.Black;

        _player = engine.CreateSpriteRender(engine.Root);
        _player.Sprite = engine.Assets.LoadSprite("sprites/player/idle");
        _player.Layer = GameLayer.World;
        _player.LocalPosition = new Vector2(0, 1);
    }

    public void Tick()
    {
        var dt = (float)_engine.Time.DeltaTime;
        _player.LocalPosition += new Vector2(4f * dt, 0);
        _camera.LocalPosition = _player.LocalPosition;
    }

    public void Teardown()
    {
        // Root.DetachAll() is handled by Bork2dApp.Dispose().
    }
}
```

Target label: the sprite line requires `IAssets` (M3) and rendering requires M8; in M1 `Setup`
throws at `LoadSprite` because `IEngine.Assets` is a stub. The camera/transform/time code is
`Current`, but `Time.DeltaTime` stays `0` in M1 because the app never advances the clock.

## 4. Layers

Layers are `[Flags]` bits: a renderable has exactly **one** layer bit, and a camera has a mask.

```csharp
_camera.CullingMask = GameLayer.Background | GameLayer.World | GameLayer.Ui;
_player.Layer       = GameLayer.World;
```

- `0` is `None`, not a layer. A renderable with layer `0` or more than one bit is skipped by the
  renderer (M8).
- Up to 32 named single-bit flags; the enum must be `int`-backed. The builder validates it and
  rejects composites.
- A camera mask may combine bits; `0` sees nothing. `CreateCamera(parent)` requires a parent; use
  `engine.Root` for a scene-level camera.

## 5. Cameras

```csharp
var camera = engine.CreateCamera(engine.Root);
camera.LocalPosition = new Vector2(0, 0);
camera.Size = 5f;                          // half visible height; 10 units tall
camera.Viewport = new Vector4(0, 0, 1, 1); // normalized, top-left origin
camera.CullingMask = GameLayer.World;
camera.Clear = ClearKind.Color;
camera.ClearColor = Color.Black;
camera.ZIndex = 0;                         // ascending order; higher applies later
```

- `Size` is the **half** visible height in world units; width follows from the viewport aspect.
  Scale is not a zoom control.
- `Viewport` is normalized `(x, y, width, height)`, top-left origin. `ZIndex` orders cameras
  ascending, higher later, default `0`; ties are unspecified (M8). Split screen uses two cameras
  with viewports `(0, 0, 0.5, 1)` and `(0.5, 0, 0.5, 1)`;
  `ScreenToWorld`/`WorldToScreen` are `Planned`.
- Multiple cameras are supported; each non-empty camera issues one draw (M8). A later camera with
  `Clear.Color` wipes its viewport, while `Clear.None` overlays.

## 6. Sprites and transforms

`ISpriteRender<TLayer>` is a transform plus a sprite handle:

```csharp
var sprite = engine.CreateSpriteRender(engine.Root);
sprite.Sprite = engine.Assets.LoadSprite("sprites/player/idle");
sprite.Layer = GameLayer.World;
sprite.ZIndex = 10;
sprite.Tint = Color.White;
sprite.LocalPosition = new Vector2(3, 1);
sprite.LocalRotation = 45f; // degrees, CCW
sprite.LocalScale = new Vector2(2, 2);
```

- `Sprite` is an opaque handle to a sprite asset. The asset supplies UVs, pivot, topology, optional
  `indices`, texture path, and a **pixels-per-unit** scale; `LocalScale` multiplies it. There is
  no global PPU and no vertices array. See
  [Assets and tooling](../architecture/assets-and-tooling.md).
- `Sprite.Empty` hides the entity; a not-ready asset draws nothing until its texture and GPU page
  are ready (M8). Do not block.
- `ZIndex` orders within a layer, higher later; ties are unspecified. `Tint` multiplies the
  texture, `White` is neutral.
- `FlipX`/`FlipY` are `Planned`.
- Parenting preserves the world transform: assigning `Parent` later keeps the child in place,
  cycles and setting the root's own parent throw `InvalidOperationException`, and a non-engine or
  foreign-engine parent throws `Bork2dException`. `ITransform.DetachAll()` detaches direct children
  without destroying them. See [Scene ownership](../architecture/scene-ownership.md).

## 7. Input

`Status: Current (M2, windowed input).` The windowed platform accumulates physical input, freezes it
once per tick, and dispatches text on the tick thread. The runtime host that attaches it to
`IEngine.Input` and starts the loop is M2 WP4, so in this tree the `IEngine.Input` getter still throws
`NotSupportedException` in every mode.

```csharp
public void Tick()
{
    var input = _engine.Input;
    var dt = (float)_engine.Time.DeltaTime;

    if (input.IsKeyDown(KeyCode.A))
        _player.LocalPosition += new Vector2(-240f * dt, 0);

    if (input.IsKeyPressed(KeyCode.Space))
        Jump();

    if (input.IsMouseDown(MouseButton.Left))
    {
        // Framebuffer pixels, top-left origin: not logical points and not world units.
        Vector2 pixel = input.MousePosition;
        HandleDrag(_player, pixel, input.MouseDelta);
    }

    var scroll = input.MouseWheelDelta; // positive = up/away; reset next tick
}

public void Setup()
{
    _engine.Input.OnTextTyped += character => _name += character;
}
```

- `IsKeyDown`/`IsMouseDown` are held state. `IsKeyPressed`/`IsKeyReleased` (and the mouse
  equivalents) are true for exactly one tick after the transition. A tap between ticks can report
  both edges with `Down == false`; key repeat creates no press edge.
- `KeyCode` is a physical scancode, not a character: use `OnTextTyped` for committed text. A
  surrogate pair arrives as two `char` events, in order, before that tick's `Tick`.
- Input is frozen at tick start, so reads are stable for the whole `Tick`; events that arrive during
  `Tick` appear in the next tick. Reads before the first tick are empty. In windowed mode
  `MousePosition`/`MouseDelta` are framebuffer **physical pixels** (the DPI note in §9 applies).
- Moving between monitors can change the display scale; the platform converts points to pixels at
  each freeze, so do not cache absolute pixel positions across ticks.
- Focus loss synthesizes releases and clears wheel/text; focus regain re-seeds held state and reports
  zero delta. Input is not part of render snapshots. `KeyCode`/`MouseButton` numeric values are
  unstable: never serialize them, and undefined values are safe `false` queries.
- Full semantics are in the [API reference](reference.md#6-iinput).
## 8. Time

```csharp
var dt = (float)_engine.Time.DeltaTime; // seconds
var now = _engine.Time.Time;            // seconds since the first tick
```

- `Status: Partial.` `StopwatchTime` exists, but the M1 app never calls `NewFrame` and never
  clamps, so both values are `0`. Target (M2): monotonic seconds, first-tick `DeltaTime` `0`,
  `DeltaTime` clamped by `MaxDeltaTime` (default 0.25 s).
- Variable timestep; no accumulator, fixed step, or render interpolation. Windowed/offscreen pace
  against the renderer with no artificial delay; headless is timer-paced to
  `ticksPerSecond` (default 60, `0` = uncapped; M7).
- For deterministic simulation, run your own fixed-step accumulator inside `Tick`. See
  [Threading](../architecture/threading.md).

## 9. Window

`Status: target for M2.` In M1 `IEngine.Window` returns an `UnsupportedWindow`; the property
itself does not throw, but every member throws `NotSupportedException`.

```csharp
_engine.Window.Title = "Level 1";
_engine.Window.Size = new Vector2(1920, 1080);
_engine.Window.Fullscreen = true;
```

The target is windowed only (offscreen/headless throw), with calls marshaled from any thread.
`Size` is in framebuffer pixels; do not set it every tick. High-DPI logical scaling is decided in
M2 (see [roadmap](../roadmap.md#9-open-decisions)).

## 10. Assets

`Status: target for M3.` In M1 `IEngine.Assets` returns an `UnsupportedAssets`; `LoadSprite`,
`LoadAsset<T>`, and `LoadData` throw `NotSupportedException`. `Build()` validates only that the
pack path is non-empty.

```csharp
Sprite player = _engine.Assets.LoadSprite("sprites/player/idle");
GameConfig config = await _engine.Assets.LoadAsset<GameConfig>("data/config");
byte[] raw = await _engine.Assets.LoadData("textures/raw/player");
```

- The default pack is the relative string `"assets.pck"` plus later-wins overlays. The call
  returns a handle immediately; the renderer skips it until its sprite asset, texture descriptor,
  raw payload, and GPU page are ready. Missing keys log and return `Sprite.Empty`.
- Paths are canonical lowercase `/` keys without extensions. `LoadSprite` is idempotent per path;
  handles are session-local and never serialized. `LoadAsset<T>` needs a Nerdbank generated shape
  (NativeAOT). `LoadData` returns decompressed bytes for any asset kind.
- `LoadPack`/`Browse`/`Exists` and `AssetInfo` are `Planned` for M3; there is no unload in v1. Do
  not block a tick on I/O or mutate scene state from a continuation. See
  [Assets and tooling](../architecture/assets-and-tooling.md).

## 11. Lifetimes and threading

- `IEngine.Root` owns the scene. Parented objects persist; unparented objects are collectible when
  the game drops the last reference. There is no entity `Dispose()`/`IsAlive`.
- Detach with `Parent = null`; re-attach the same object later with its state intact.
  `Root.DetachAll()` detaches the scene's top-level children, and `ITransform.DetachAll()` works
  on any transform.
- The tree is **unordered**: no entity IDs, ordered discovery, or public child enumeration. Use
  `ZIndex`, not creation order, when draw order matters.
- Create entities inside `Setup`/`Tick`/`Teardown`. Any thread may call the public API there; no
  process-main-thread requirement. Structural attach/detach is serialized by the engine-owned
  structural lock; local properties use per-object locks.
- Workers may mutate **distinct** entities concurrently and must join before `Tick` returns.
  Composite updates to the same entity need game-level synchronization. Frame snapshots are
  captured after `Tick` (M7); workers must not write scene state afterwards. Worker exceptions
  must be observed and propagated by the game.

## 12. NativeAOT

- Pass the game as an instance; no reflection construction.
- Use `LoadAsset<T>` only for types with Nerdbank generated shapes; do not scan assemblies, use
  `Type.GetType`, runtime TOML, or dynamic code.
- TOML is source/editor format only; the pack contains MessagePack/raw assets. The editor is JIT
  and not shipped with the game.
- The runtime libraries are `IsAotCompatible`; AOT publish/run is a required local gate.

See [NativeAOT](../architecture/nativeaot.md).

## 13. Where to go next

- [API reference](reference.md) — exact members, defaults, exceptions, status.
- [Scene ownership](../architecture/scene-ownership.md) — root lifetime and parent rules.
- [Runtime lifecycle](../architecture/runtime-lifecycle.md) — builder, modes, pacing, shutdown.
- [Assets and tooling](../architecture/assets-and-tooling.md) — pack, descriptors, editor.
- [NativeAOT](../architecture/nativeaot.md) and [Roadmap](../roadmap.md) — constraints and
  milestone ownership of `Planned` behavior.
