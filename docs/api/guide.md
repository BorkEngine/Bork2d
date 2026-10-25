# API Guide

> **Status: target guide.** This is how the engine is meant to be used, not a description of a
> finished runtime. Members marked `Planned` in the [API reference](reference.md) do not exist
> in the current scaffold. The startup builder, modes, `IEngine.Input`, `IGame.Teardown`,
> pack API, and asset schema are the main planned additions shown here.

## 1. What you reference

| Project | References | Contains |
| --- | --- | --- |
| `Project.Common` | `Bork2d.Api` only | Gameplay classes, custom data types, source assets. |
| `Project.Desktop` | `Bork2d.Runtime` + `Project.Common` | Entry point that builds and runs the app. |
| `Project.Editor` | `Bork2d.Editor` + `Project.Common` | Thin Avalonia editor host. |

Do not reference `Bork2d.Render`, `Bork2d.Common`, or `Bork2d.Assets` from game code. They are
implementation details. See [Projects and dependencies](../architecture/projects.md).

## 2. Start the engine

Construct the game yourself, then pass the instance to the builder:

```csharp
using Bork2d.Runtime;

var game = new MyGame(logger, network, saveStore);

using var app = new Bork2dAppBuilder<GameLayer>()
    .WithGame(game)
    .WithTitle("My Game")
    .WithSize(1280, 720)
    .WithAssetsPack("assets.pck")
    .WithVSync(true)
    .Build();

app.Run();
```

- `WithGame` takes an **instance**, so constructor injection works for networking, services,
  test doubles, and logging your host owns. The engine factory itself does not exist until
  `Build()`; for engine logging, create the logger from `engine.LoggerFactory` in `Setup`.
  There is no parameterless-constructor/reflection requirement.
- `Build()` validates on the main thread; `Run()` blocks until shutdown; `Dispose()` cleans up.
- The game instance must stay alive through `Run()`.
- Windowed/offscreen have no target tick rate; pacing follows the renderer, with no artificial
  delay. Headless is timer-paced via `WithHeadless(ticksPerSecond)` (default 60, `0` uncapped).
- Logging is configured with `WithLogging`; if omitted, a pretty ZLogger console provider is
  installed, and `IEngine.LoggerFactory` is always available.
- The default pack is `assets.pck`; `WithAssetsPack` can point elsewhere.

### Modes

```csharp
// Windowed (default)
using var windowed = new Bork2dAppBuilder<GameLayer>()
    .WithGame(game)
    .WithTitle("My Game")
    .WithSize(1280, 720)
    .Build();
windowed.Run();

// Windowless Vulkan rendering for tests/benchmarks; frames are discarded in v1.
using var offscreen = new Bork2dAppBuilder<GameLayer>()
    .WithGame(game)
    .WithOffscreen(1920, 1080)
    .Build();
offscreen.Run();

// Dedicated server/data tool: no SDL, no Vulkan, no render thread; paced to 60 ticks/s.
// Use WithHeadless(0) for uncapped.
using var server = new Bork2dAppBuilder<GameLayer>()
    .WithGame(serverGame)
    .WithHeadless()
    .Build();
server.Run();
```

These are separate hosts, not three modes in one process. `WithHeadless(ticksPerSecond = 60)`
and `WithOffscreen()` are mutually exclusive with window options. `0` means uncapped headless.
`IWindow` and `IInput` throw in both windowless modes. See
[Runtime lifecycle](../architecture/runtime-lifecycle.md#2-modes).

### Logging

If you do nothing, the builder installs a pretty ZLogger console provider. You can replace or
extend it:

```csharp
using var app = new Bork2dAppBuilder<GameLayer>()
    .WithGame(game)
    .WithLogging(logging =>
    {
        // Configure Microsoft.Extensions.Logging providers, including ZLogger.
    })
    .Build();
```

The engine exposes the resulting factory:

```csharp
ILogger logger = _engine.LoggerFactory.CreateLogger("MyGame");
logger.LogInformation("Loaded level {Level}", "start");
```

See [Logging](../architecture/logging.md) for defaults, categories, and AOT notes.

## 3. Write a game

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
    Effects    = 1 << 2,
    Ui         = 1 << 3,
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

        _camera = engine.CreateCamera();
        _camera.LocalPosition = Vector2.Zero;
        _camera.Size = 5f;                       // 10 world units tall
        _camera.CullingMask = GameLayer.Background | GameLayer.World | GameLayer.Effects;
        _camera.Clear = ClearKind.Color;
        _camera.ClearColor = Color.Black;

        _player = engine.CreateSpriteRender();
        _player.Sprite = engine.Assets.LoadSprite("sprites/player/idle");
        _player.Layer = GameLayer.World;
        _player.ZIndex = 0;
        _player.LocalPosition = Vector2.Zero;
        _player.Tint = Color.White;
    }

    public void Tick()
    {
        var dt = (float)_engine.Time.DeltaTime;
        var direction = Vector2.Zero;

        if (_engine.Input.IsKeyDown(KeyCode.A)) direction.X -= 1f;
        if (_engine.Input.IsKeyDown(KeyCode.D)) direction.X += 1f;
        if (_engine.Input.IsKeyDown(KeyCode.W)) direction.Y += 1f;
        if (_engine.Input.IsKeyDown(KeyCode.S)) direction.Y -= 1f;

        if (direction != Vector2.Zero)
            _player.LocalPosition += Vector2.Normalize(direction) * 4f * dt;
    }

    // Planned: once on the main thread before shutdown.
    public void Teardown()
    {
        _player.Dispose();
        _camera.Dispose();
    }
}
```

`Setup` runs once on the main thread before the first tick. `Tick` runs on the main thread.
There is no game-side Draw callback: the engine captures the render snapshot after `Tick`.

## 4. Layers

Layers are `[Flags]` bits. A renderable has exactly **one** layer bit; a camera has a mask.

```csharp
[Flags]
public enum GameLayer
{
    None       = 0,
    Background = 1 << 0,
    World      = 1 << 1,
    Effects    = 1 << 2,
    Ui         = 1 << 3,
}

_camera.CullingMask = GameLayer.Background | GameLayer.World | GameLayer.Effects;
_background.Layer   = GameLayer.Background;
_player.Layer       = GameLayer.World;
_particles.Layer    = GameLayer.Effects;
```

- `0` is `None`, not a layer. A renderable with layer `0` is skipped.
- A renderable with more than one bit is skipped. Do not assign `Background | World`.
- Up to 32 layer flags. The builder validates the enum; keep it `int`-backed.
- Declare bits in ascending order. Draw order uses the numeric layer value first.
- A camera mask may combine bits; `0` sees nothing.
- `CreateCamera()` defaults to all declared layers; set the mask explicitly in real code.

## 5. Cameras

```csharp
var camera = engine.CreateCamera();
camera.LocalPosition = new Vector2(0, 0);
camera.Size = 5f;                         // half visible height; 10 units tall
camera.Viewport = new Vector4(0, 0, 1, 1); // normalized, top-left origin
camera.CullingMask = GameLayer.World;
camera.Clear = ClearKind.Color;
camera.ClearColor = Color.Black;
```

- `Size` is the **half** visible height in units. Width follows from viewport aspect.
- World is Y-up, +X right. Rotation is radians, CCW.
- `Viewport` is normalized `(x, y, w, h)`, top-left origin.
- Camera rotation/position come from its transform. Camera scale is not a zoom control.
- Multiple cameras are supported; each non-empty camera issues one instanced draw.
- Camera order is creation order. A later camera with `Clear.Color` wipes its viewport;
  `Clear.None` overlays.

Split screen:

```csharp
var left = engine.CreateCamera();
left.Viewport = new Vector4(0f, 0f, 0.5f, 1f);
left.CullingMask = GameLayer.World;

var right = engine.CreateCamera();
right.Viewport = new Vector4(0.5f, 0f, 0.5f, 1f);
right.CullingMask = GameLayer.World;
```

HUD overlay:

```csharp
var hud = engine.CreateCamera();
hud.Viewport = new Vector4(0, 0, 1, 1);
hud.Clear = ClearKind.None;      // keep the world behind it
hud.CullingMask = GameLayer.Ui;
```

Screen/world conversion helpers are `Planned`:

```csharp
Vector2 world = camera.ScreenToWorld(engine.Input.MousePosition);
Vector2 pixel = camera.WorldToScreen(player.LocalPosition);
```

## 6. Sprites and transforms

`ISpriteRender<TLayer>` is a transform plus a sprite asset handle:

```csharp
var sprite = engine.CreateSpriteRender();
sprite.Sprite = engine.Assets.LoadSprite("sprites/player/idle");
sprite.Layer = GameLayer.World;
sprite.ZIndex = 10;
sprite.Tint = Color.White;
sprite.LocalPosition = new Vector2(3, 1);
sprite.LocalRotation = MathF.PI / 4f; // radians, CCW
sprite.LocalScale = new Vector2(2, 2);
```

Semantics:

- `Sprite` is an opaque handle to a MessagePack sprite asset. The asset may not be ready; the
  renderer skips it until it is. Do not block.
- `Sprite.Empty` means no sprite.
- `ZIndex` orders within a layer; higher draws later. Equal z is tie-broken by entity ID.
- `Tint` multiplies the texture; `Color.White` is neutral.
- The sprite asset stores UVs, pivot, topology, texture path, and a **pixels-per-unit scale**;
  the renderer divides by that scale. `LocalScale` multiplies it. There is no global PPU and
  no vertices array.
- Pivot/origin is normalized inside the generated quad; default center.
- Planned `FlipX`/`FlipY` mirrors the sprite.
- Dispose a sprite when no longer needed, inside a tick/teardown; a worker may dispose an
  entity it owns.

Parenting:

```csharp
var rig = engine.CreateTransform();
var turret = engine.CreateSpriteRender();
turret.Parent = rig;
turret.LocalPosition = new Vector2(0.5f, 0);
rig.LocalRotation = MathF.PI / 2f;
```

Reparenting is planned to preserve the child's world transform. Cycles are rejected. The
hierarchy is evaluated on the tick thread; the renderer never walks it.

## 7. Input

Input is windowed only. It is read during `Tick`:

```csharp
var input = _engine.Input;

if (input.IsKeyPressed(KeyCode.Space))
    Jump();

if (input.IsKeyDown(KeyCode.LeftShift))
    sprint = true;

if (input.IsMousePressed(MouseButton.Left))
{
    Vector2 pixel = input.MousePosition; // top-left origin, framebuffer pixels
    Attack(pixel);
}

_aim += input.MouseDelta;
_zoom -= input.MouseWheelDelta;
```

- `IsKeyDown`/`IsMouseDown` are held state.
- `IsKeyPressed`/`IsMousePressed` are true for exactly one tick after the down transition.
- `IsKeyReleased`/`IsMouseReleased` are true for exactly one tick after the up transition.
- A fast tap between ticks can produce both pressed and released in the same tick with
  `IsKeyDown == false`.
- `MousePosition` is framebuffer pixels; use a camera helper for world.
- `MouseDelta` is pixels since the previous tick; `MouseWheelDelta` is accumulated.
- `KeyCode` uses physical scancodes; text is separate:

```csharp
_engine.Input.OnTextTyped += c =>
{
    if (char.IsControl(c)) return;
    _name += c;
};
```

`OnTextTyped` fires on the main thread during event pumping, before `Tick`. In offscreen or
headless mode, `IInput` throws; do not call it from mode-independent code.

## 8. Time

```csharp
var dt = (float)_engine.Time.DeltaTime; // seconds, clamped
var now = _engine.Time.Time;            // seconds since first tick
```

- Variable timestep. Windowed/offscreen have **no target tick rate** and no artificial pacing
  delay; headless is timer-paced to its configured tick rate.
- `DeltaTime` is clamped by `MaxDeltaTime` (default 0.25 s) to survive stalls.
- The first tick's `DeltaTime` is `0`.
- Windowed/offscreen pacing is bounded run-ahead: the tick runs until the snapshot ring is full,
  then waits for a GPU frame. The rate follows the renderer/frame rate.
- Headless pacing uses the configured tick rate (`WithHeadless(60)` default, `0` uncapped).
- No render interpolation. If you need deterministic simulation, run your own fixed-step
  accumulator inside `Tick`.

## 9. Window

```csharp
_engine.Window.Title = "Level 1";
_engine.Window.Size = new Vector2(1920, 1080);
_engine.Window.Fullscreen = true;
_engine.Window.CursorVisible = false;
```

Window properties are main-thread, windowed only. In offscreen/headless they throw. `Size` is
framebuffer pixels; the renderer recreates the swapchain asynchronously. Do not set it every
tick. High-DPI logical scaling is `Open` in v1; treat sizes as physical pixels.

## 10. Assets

The runtime reads packed assets (`assets.pck` plus overlays):

```csharp
Sprite playerSprite = _engine.Assets.LoadSprite("sprites/player/idle");
_player.Sprite = playerSprite;
```

- The call returns a handle immediately; loading happens in the background.
- The renderer skips the sprite until its sprite asset, texture descriptor, raw texture, and
  GPU page are ready. No exception, no placeholder.
- The same key returns the same handle during a session.
- Paths are canonical lowercase `/` keys without extensions. See
  [Assets: paths](../architecture/assets.md#3-paths).
- A missing key returns `Sprite.Empty` and logs.
- The handle is not serializable; store asset paths in game data.

Data and raw bytes:

```csharp
GameConfig config = await _engine.Assets.LoadAsset<GameConfig>("data/config");
byte[] raw = await _engine.Assets.LoadData("textures/raw/player");
```

- `LoadAsset<T>` reads MessagePack assets and requires a Nerdbank generated shape (NativeAOT).
- `LoadData` returns decompressed bytes for any asset kind.
- Both complete on a background thread; do not mutate scene state from the continuation. Marshal
  to a tick.
- Prefer loading data before the first frame; never block a tick on I/O.

Overlays and browsing:

```csharp
_engine.Assets.LoadPack("mods/chapter2.pck"); // mounted above assets.pck; later wins
bool hasBoss = _engine.Assets.Exists("sprites/enemies/boss");
IReadOnlyList<AssetInfo> sprites = _engine.Assets.Browse("sprites");
```

`LoadPack` has no unload in v1. Mount packs during `Setup`/before loading. `Browse`/`Exists` are
for tools and diagnostics, not per-frame gameplay.

## 11. Lifetimes and threading

- Create cameras/sprites/transforms inside the `Setup`/`Tick` window. Workers may create
  entities during a tick if they are joined before it ends.
- Dispose inside `Tick`/`Teardown`. Workers may dispose entities they own; do not dispose an
  entity another thread is using.
- Engine-owned entities are retained until disposed; dropping your own reference does not
  remove them. This is a planned change from the current weak-reference registry.
- Workers may mutate **distinct** entities concurrently. They must join before `Tick` returns.
  Two workers touching the same entity/hierarchy need game-level synchronization.

```csharp
public void Tick()
{
    float dt = (float)_engine.Time.DeltaTime;

    Parallel.For(0, _sprites.Count, i =>
    {
        // Each worker owns one distinct sprite.
        _sprites[i].LocalPosition += _velocities[i] * dt;
    });
}
```

- Workers must not read `ITime`, `IInput`, `IWindow`, or call `IEngine.Exit`; pass values in.
- Asset requests are thread-safe; readiness is asynchronous.
- The renderer never exposes live entities or GPU resources.
- Worker exceptions must be observed and propagated by the game.

## 12. NativeAOT

The game/runtime is NativeAOT- and trim-safe. Practical rules:

- Pass the game as an instance; no reflection construction.
- Use `LoadAsset<T>` only for types with Nerdbank generated shapes.
- Do not scan assemblies, use `Type.GetType`, runtime TOML, or dynamic code in game code.
- TOML is source/editor format only; the pack contains MessagePack/raw assets.
- The editor is JIT and not shipped with the game.

See [NativeAOT](../architecture/nativeaot.md).

## 13. Common mistakes

| Mistake | Result | Correct approach |
| --- | --- | --- |
| Treating `Camera.Size` as full height/pixels | Wrong zoom | It is half the visible height in units. |
| Assigning `Layer = 0` or a combined mask to a sprite | Invisible, debug diagnostic | One non-zero layer bit. |
| Using a sprite before its asset loads | Invisible | Expected; keep the handle; do not block. |
| Blocking `Tick` waiting for `LoadAsset<T>` | Hitch/deadlock risk | Load early; complete asynchronously; marshal to a tick. |
| Persisting `Sprite.Id` | Broken references | Persist asset paths. |
| Touching entities from the render thread | Corrupt snapshots | Renderer only reads snapshots. |
| Two workers mutating one entity without sync | Lost updates/data races | One owner per entity or external sync. |
| Assuming equal `ZIndex` keeps insertion order | Reordering surprises | Use distinct z; entity ID is the stable tie-break. |
| Creating/disposing outside a tick | Undefined ordering | Use `Setup`/`Tick`/`Teardown`. |
| Choosing a global pixels-per-unit | Inconsistent sprite sizes | Use the sprite asset's scale and transform scale. |
| Relying on a fixed tick rate | Wrong on 165 Hz displays | Use `DeltaTime`; pacing is frame-based. |
| Calling `IWindow`/`IInput` in headless/offscreen | Throws | Branch on mode or use separate host code. |
| Using TOML/reflection at runtime | NativeAOT failure | TOML is baked; use Nerdbank generated shapes. |
| Setting `Window.Size` every tick | Swapchain churn | Change on user action. |

## 14. A complete example

`Program.cs`:

```csharp
using Bork2d.Runtime;

var game = new MyGame();

using var app = new Bork2dAppBuilder<GameLayer>()
    .WithGame(game)
    .WithTitle("Bork2d Sample")
    .WithSize(1280, 720)
    .WithAssetsPack("assets.pck")
    .WithVSync(true)
    .Build();

app.Run();
```

`MyGame.cs`:

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
    private ISpriteRender<GameLayer> _background = null!;
    private ISpriteRender<GameLayer> _player = null!;
    private Vector2 _velocity;

    public void Setup(IEngine<GameLayer> engine)
    {
        _engine = engine;
        _logger = engine.LoggerFactory.CreateLogger<MyGame>();

        _camera = engine.CreateCamera();
        _camera.Size = 5f;
        _camera.CullingMask = GameLayer.Background | GameLayer.World;
        _camera.Clear = ClearKind.Color;
        _camera.ClearColor = Color.Black;

        _background = engine.CreateSpriteRender();
        _background.Sprite = engine.Assets.LoadSprite("sprites/background");
        _background.Layer = GameLayer.Background;
        _background.ZIndex = -100;
        _background.LocalScale = new Vector2(4, 4);

        _player = engine.CreateSpriteRender();
        _player.Sprite = engine.Assets.LoadSprite("sprites/player/idle");
        _player.Layer = GameLayer.World;
        _player.ZIndex = 0;
        _player.LocalPosition = Vector2.Zero;
    }

    public void Tick()
    {
        float dt = (float)_engine.Time.DeltaTime;
        var input = _engine.Input;

        var direction = Vector2.Zero;
        if (input.IsKeyDown(KeyCode.A)) direction.X -= 1f;
        if (input.IsKeyDown(KeyCode.D)) direction.X += 1f;
        if (input.IsKeyDown(KeyCode.W)) direction.Y += 1f;
        if (input.IsKeyDown(KeyCode.S)) direction.Y -= 1f;

        if (direction != Vector2.Zero)
            direction = Vector2.Normalize(direction);

        _velocity = Vector2.Lerp(_velocity, direction * 4f, 12f * dt);
        _player.LocalPosition += _velocity * dt;

        _camera.LocalPosition = Vector2.Lerp(_camera.LocalPosition, _player.LocalPosition, 8f * dt);

        if (input.IsKeyPressed(KeyCode.Escape))
            _engine.Exit();
    }

    public void Teardown()
    {
        _player.Dispose();
        _background.Dispose();
        _camera.Dispose();
    }
}
```

### Headless server host

```csharp
var server = new ServerGame(logger);

using var app = new Bork2dAppBuilder<GameLayer>()
    .WithGame(server)
    .WithHeadless()
    .WithAssetsPack("assets.pck")
    .Build();

app.Run();
```

No window, SDL, Vulkan, or render thread is initialized. `IWindow`/`IInput` are unavailable.
The tick loop is paced at 60 Hz by default; pass `WithHeadless(0)` for uncapped.

## 15. Where to go next

- [API reference](reference.md) — exact members, defaults, threading, status.
- [Assets and pack files](../architecture/assets.md) — pack format, descriptors, sprites.
- [Editor](../architecture/editor.md) — source authoring and slicing.
- [NativeAOT](../architecture/nativeaot.md) — runtime constraints.
- [Roadmap](../roadmap.md) — when planned members become real.
