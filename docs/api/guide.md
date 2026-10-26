# API Guide

> **Status:** implemented M2 (WP1–WP5; merged into `develop` 2026-10-26, PR #2), plus the M3 WP1
> tooling-only pack writer and the M3 WP2 asset contract/VFS. The builder and host exist in
> `Bork2d.Runtime`; `Build()` validates and `Run()` runs the uncapped windowed loop (window, input,
> time) or the one-tick headless/offscreen sequence. The asset contract, canonical paths, and
> internal VFS are M3 WP2; the loader/runtime service is WP3–WP5 and the renderer is M8;
> `IEngine.Assets` remains a throwing stub. See the
> [API reference](reference.md#12-runtime-surface).

## 1. What you reference

| Project | References | Contains |
| --- | --- | --- |
| `Project.Common` | `Bork2d.Api` only | Gameplay classes, custom data types, source assets. |
| `Project.Desktop` | `Bork2d.Runtime` + `Project.Common` | Entry point that builds and runs the app. |
| `Project.Editor` (M5) | `Bork2d.Editor` + `Project.Common` | Thin Avalonia editor host. |

Do not reference `Bork2d.Render`, `Bork2d.Common`, or `Bork2d.Assets` from game code; they are
implementation details ([Projects and dependencies](../architecture/projects.md)). **Tooling and
tests** may reference `Bork2d.Assets` for the public `PackBuilder`/`PackCompressionMode` writer
(§10); that surface is not part of the game contract.

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
- `Build()` validates the configuration. `Run()` blocks until shutdown. Windowed mode creates one
  visible window and runs an uncapped `Tick` loop until `IEngine.Exit()`, the close button, or a
  fatal error; headless/offscreen call `Setup`, exactly one `Tick`, and `Teardown`, then return.
  There is no GPU work, pack loading, render thread, or frame limiter in M2.
- `using`/`Dispose()` detaches the root's direct children and the window/input proxies and disposes
  the app-owned logger factory. It never calls `Teardown` again. `Dispose()` while `Run()` is active
  throws `InvalidOperationException`; call `IEngine.Exit()` or close the window first.
- The builder, `Build`, `Run`, and `Dispose` have no thread affinity. See
  [`IEngine<TLayer>`](reference.md#2-ienginetlayer) for the engine surface.

### Modes

| Mode | Selection | M2 behavior | Target |
| --- | --- | --- | --- |
| Windowed (default) | `WithTitle`/`WithSize`/`WithVSync` | Visible SDL window, input, time, and an uncapped loop until exit/close. | Renderer (M8). |
| Offscreen | `WithOffscreen(width, height)` | One-tick `Setup`/`Tick`/`Teardown`; no window, input, SDL, or clock advance. | Windowless rendering (M6/M8). |
| Headless | `WithHeadless(ticksPerSecond = 60)` | One-tick `Setup`/`Tick`/`Teardown`; no window, input, SDL, or clock advance. | Paced loop at the rate, `0` = uncapped (M7). |

The modes are mutually exclusive, and window-only options are rejected in headless/offscreen.
They are separate hosts, not three modes in one process. In headless/offscreen `IWindow` members
and the `IInput` getter throw `NotSupportedException` and `ITime` stays `0`. See
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

### Windowed AOT demo

`Bork2d.AotSmoke` is the reference windowed host. With no arguments it keeps the M1 headless
one-tick smoke; the demo is opt-in and NativeAOT-published:

```powershell
dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64 --nologo
& src/Bork2d/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe --windowed
& src/Bork2d/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe --windowed --ticks 120
```

The interactive demo creates a camera/transform/sprite tree, moves the transform with WASD or the
arrow keys using `ITime.DeltaTime`, logs input transitions and text rather than every frame, and
exits on Escape (`IEngine.Exit()`) or the window close button. `--ticks N` exits with code 0 after
exactly `N` completed ticks and is the automated acceptance run; `--ticks` requires `--windowed`
and `N >= 1`. Invalid or unknown arguments print usage and exit with code 2.

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

Target label: the sprite line requires `IAssets` (M3) and rendering requires M8; in M2 `Setup`
throws at `LoadSprite` because `IEngine.Assets` is a stub. The camera/transform code is `Current`;
time is `Current` in windowed mode and stays `0` in the one-tick modes.

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

`Status: Current (M2, windowed).` The windowed host freezes physical input once per tick, advances
the clock, dispatches queued text, then calls `Tick`. Headless/offscreen have no input: the
`IEngine.Input` getter throws `NotSupportedException`.

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
var dt = (float)_engine.Time.DeltaTime; // seconds, clamped to MaxDeltaTime
var now = _engine.Time.Time;            // raw seconds since the first tick
```

- `Status: Current (M2, windowed).` The first tick publishes `Time = 0` and `DeltaTime = 0`;
  afterwards `Time` is raw monotonic seconds (never clamped) and `DeltaTime` is the interval
  between tick starts clamped by `MaxDeltaTime` (default 0.25 s).
- In headless/offscreen the one-tick host never advances the clock, so both values stay `0` until
  M7. Do not assume a non-zero delta or a tick count.
- Variable timestep; no accumulator, fixed step, or render interpolation. The windowed loop is
  uncapped in M2 (M7 adds frame-handoff backpressure); headless pacing is M7.
- For deterministic simulation, run your own fixed-step accumulator inside `Tick`. See
  [Threading](../architecture/threading.md).

## 9. Window

`Status: Current (M2, windowed).` In windowed mode the window is created visible before `Setup` and
destroyed after `Teardown`. `IEngine.Window` always returns an object; in headless/offscreen its
members throw `NotSupportedException`, and after a windowed run retained references throw
`ObjectDisposedException`.

```csharp
_engine.Window.Title = "Level 1";
_engine.Window.Size = new Vector2(1920, 1080);
_engine.Window.Fullscreen = true;
```

- `Size` is **framebuffer physical pixels**, not logical points. At 150% Windows scaling, a
  1280x720 request becomes an 853x480 window whose framebuffer still reports 1280x720; mouse
  coordinates use the same framebuffer-pixel unit. Do not set `Size` every tick.
- Setters validate and enqueue; they never block on SDL. Getters return the last confirmed state
  and may lag one event pump. `Size` is ignored while fullscreen (debug log); while minimized it
  becomes the restored size, and `IsMinimized` is readable from any thread.
- Escape is **game-handled**: the engine never binds it. Exit from the game, and the close button
  always requests a graceful exit:

```csharp
public void Tick()
{
    if (_engine.Input.IsKeyPressed(KeyCode.Escape))
    {
        _engine.Exit(); // finishes this tick, then Run() tears down and returns
    }
}
```

## 10. Assets

`Status: Partial (M3 WP2).` The public contract and the internal path/VFS/browse stack are
implemented, but `IEngine.Assets` still returns the throwing `UnsupportedAssets` stub in this
tree: every member throws `NotSupportedException` until WP3/WP5 wire the asset service. The
examples below compile against the contract and describe the target runtime behavior.

```csharp
using PolyType;

[GenerateShape]
public sealed partial class GameConfig
{
    public required string Name { get; set; }
    public int MaxPlayers { get; set; }
}
```

```csharp
// Packs: mount overlays (later wins; no unload).
_engine.Assets.LoadPack("overlays/hd.pck");

// Browse/exists: canonical paths; Browse(null) lists everything.
bool hasConfig = _engine.Assets.Exists("data/config");
IReadOnlyList<AssetInfo> sprites = _engine.Assets.Browse("sprites");

// Typed assets need a compiled shape; raw bytes use LoadData.
GameConfig config = await _engine.Assets.LoadAsset<GameConfig>("data/config");
byte[] pixels = await _engine.Assets.LoadData("textures/raw/player");

// Sprites return a session-local handle immediately.
Sprite player = _engine.Assets.LoadSprite("sprites/player/idle");
```

- **Typed assets.** `LoadAsset<T>` is constrained to `where T : class, IShapeable<T>` (D-M3-22).
  `T` must carry a compiled `[GenerateShape]` shape (PolyType); a missing shape is a compile-time
  error, not a runtime fallback. There is no reflection resolver. Use `LoadData` for arbitrary
  bytes, including raw texture payloads.
- **Paths.** Public inputs are normalized (`\` → `/`, invariant lowercase) and then validated:
  lowercase letters, digits, `_`, `-`, and `/` separators; no leading/trailing slash, empty
  segment, `.`/`..` segment, or source extension. `textures/bg.png` is invalid; use
  `textures/bg`. Null throws `ArgumentNullException`, other invalid paths throw
  `ArgumentException`, and a valid missing path is simply absent.
- **`LoadPack`.** Takes a filesystem path (relative to the process current directory or absolute),
  validates and mounts it synchronously. Later mounts shadow earlier paths; mounting the same file
  twice is allowed. A bad/missing pack throws `Bork2dException` with the resolved path and leaves
  the stack unchanged. Mount before the first tick when possible; there is no unload, replacement,
  loose-file fallback, or automatic retry in v1.
- **`Browse`/`Exists`.** `Browse(null)` returns every visible path once (shadowed entries hidden),
  sorted by `Path` ordinal; a non-null prefix must be canonical and matches on a `/` segment
  boundary (`sprites/pl` matches `sprites/pl` and `sprites/pl/idle`, not `sprites/player`). An
  empty prefix is invalid. `Exists` is an exact lookup, not a prefix match. Results are a
  materialized snapshot; do not browse per frame.
- **`AssetInfo`.** Reports `Path`, `IsRaw`, `IsCompressed`, `Length`, `Size`, and `PackPath` from
  the header without decompressing. `PackPath` is the supplied pack identifier, not the resolved
  absolute path.
- **Sprites.** `LoadSprite` is idempotent per path; handles are session-local and never serialized.
  Missing paths log and return `Sprite.Empty`. A handle is skipped by the renderer until its CPU
  metadata (and, in later milestones, its GPU page) is ready; M3 has no readiness query and no
  placeholder.
- **Lifetime.** All members are thread-safe. After the runtime service stops, members throw
  `ObjectDisposedException`; the `IEngine.Assets` getter itself never throws. `AssetException`/
  `AssetErrorKind` arrive with the loader in WP3.

### Building a pack (tooling only)

The pack **writer** is available now from a tool, test, or editor host. The runtime loader is
WP3–WP5; game code must not reference `Bork2d.Assets`.

```csharp
using Bork2d.Assets;

var builder = new PackBuilder(PackCompressionMode.Release);
builder.AddRaw("textures/raw/player", rgba8Bytes);
builder.AddMessagePack("data/config", new GameConfig { /* [GenerateShape] type */ });
builder.Write("assets.pck"); // overwrites; relative paths resolve against the current directory
```

- `PackCompressionMode.Debug` stores payloads uncompressed; `Release` uses maximum-quality Zstd
  only when strictly smaller. The header is always Zstd with an XXH3-64 checksum; payloads have no
  checksum. There is no byte-determinism guarantee.
- `Build()` returns one `byte[]`; `Write(Stream)` streams and leaves the stream open. Builder calls
  are repeatable and do not consume the builder.
- Paths are normalized/validated with the same canonical rules as runtime lookups. Duplicate
  paths, serialization failures, and write failures throw `Bork2dException`; null/invalid path
  arguments throw `ArgumentNullException`/`ArgumentException`.
- See the [pack tooling reference](reference.md#13-pack-tooling-bork2dassets-tooling-only) for the
  full contract.

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
- `Run()` runs until `IEngine.Exit()` or the close button. `Dispose()` during an active `Run()`
  throws `InvalidOperationException`; dispose after `Run()` returns. A failed `Run()` still counts
  as the app's one run and still detaches the root's children.
- Workers may mutate **distinct** entities concurrently and must join before `Tick` returns.
  Composite updates to the same entity need game-level synchronization. Frame snapshots are
  captured after `Tick` (M7); workers must not write scene state afterwards. Worker exceptions
  must be observed and propagated by the game.

## 12. NativeAOT

- Pass the game as an instance; no reflection construction.
- Use `LoadAsset<T>` only with types that satisfy the `IShapeable<T>` constraint (a compiled
  Nerdbank/PolyType shape); do not scan assemblies, use `Type.GetType`, runtime TOML, or dynamic
  code.
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
