# API Guide

> **Status:** implemented M3 plus the D-035 asset-reference refactor (merged, PR #6); **M4
> serialization and bake is implementation complete on `m-4/common` (final PR pending, not
> merged)**. M1 (foundation), M2 (SDL windowed host), and M3 (pack, VFS, runtime integration) exist
> in `Bork2d.Runtime`/`Bork2d.Assets` and are merged into `develop`. `Build()` validates and `Run()`
> runs the uncapped windowed loop (window, input, time) or the one-tick headless/offscreen sequence,
> opening the base pack (or the `WithoutAssets()` empty filesystem) before the window and `Setup`.
> The renderer is M8. See the [API reference](reference.md#12-runtime-surface).

## 1. What you reference

| Project | References | Contains |
| --- | --- | --- |
| `Project.Common` | `Bork2d.Api` only | Gameplay classes, custom data types, source assets. |
| `Project.Desktop` | `Bork2d.Runtime` + `Project.Common` | Entry point that builds and runs the app. |
| `Project.Editor` (M5) | `Bork2d.Editor` + `Project.Common` | Thin Avalonia editor host. |

Do not reference `Bork2d.Render`, `Bork2d.Common`, or `Bork2d.Assets` from game code; they are
implementation details ([Projects and dependencies](../architecture/projects.md)). **Tooling and
tests** may reference `Bork2d.Assets` for the public `Bork2d.Assets.Pack.PackBuilder`/
`PackCompressionMode` writer (§10); that surface is not part of the game contract. The M4 bake
library/CLI are JIT tooling driven by `build/Bork2d.Bake.targets`; game code never references them.

`Bork2d.Api` is split by area: engine/game in `Bork2d.Api`, scene types in `Bork2d.Api.Scene`,
render value types in `Bork2d.Api.Rendering`, input in `Bork2d.Api.Input`, window/time in
`Bork2d.Api.Platform`, and assets in `Bork2d.Api.Assets` ([reference §1.5](reference.md#15-namespaces)).

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
  There is no GPU work or render thread yet; the base pack is opened by `Run()` (M3), and the
  windowed loop has no frame limiter.
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
using Bork2d.Api.Assets;
using Bork2d.Api.Rendering;
using Bork2d.Api.Scene;
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
    private Task<SpriteAsset>? _playerLoad;

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
        _playerLoad = engine.Assets.LoadAsset<SpriteAsset>("sprites/player/idle");
        _player.Layer = GameLayer.World;
        _player.LocalPosition = new Vector2(0, 1);
    }

    public void Tick()
    {
        if (_player.Sprite is null && _playerLoad is { IsCompletedSuccessfully: true })
        {
            _player.Sprite = _playerLoad.Result;
        }

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

Target label: `LoadAsset<SpriteAsset>` is real (the sprite asset is a normal public MessagePack
asset), and rendering requires M8, so nothing draws yet. The camera/transform code is `Current`;
time is `Current` in windowed mode and stays `0` in the one-tick modes. `Setup` is synchronous, so
the example starts the load and assigns the asset in `Tick` when it completes; blocking startup on
the load is also acceptable when a scene must be complete before the first tick.

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
  `ScreenToWorld`/`WorldToScreen` are `Planned` (not declared in `Bork2d.Api` yet).
- Multiple cameras are supported; each non-empty camera issues one draw (M8). A later camera with
  `Clear.Color` wipes its viewport, while `Clear.None` overlays.

## 6. Sprites and transforms

`ISpriteRender<TLayer>` is a transform plus a loaded sprite asset:

```csharp
SpriteAsset player = await engine.Assets.LoadAsset<SpriteAsset>("sprites/player/idle");

var sprite = engine.CreateSpriteRender(engine.Root);
sprite.Sprite = player;
sprite.Layer = GameLayer.World;
sprite.ZIndex = 10;
sprite.Tint = Color.White;
sprite.LocalPosition = new Vector2(3, 1);
sprite.LocalRotation = 45f; // degrees, CCW
sprite.LocalScale = new Vector2(2, 2);
```

- `Sprite` holds the loaded `SpriteAsset` from `LoadAsset<SpriteAsset>`. The asset supplies UVs,
  pivot, topology, optional `indices`, a texture reference, and a **pixels-per-unit** scale;
  `LocalScale` multiplies it. There is no global PPU and no vertices array. See
  [Assets and tooling](../architecture/assets-and-tooling.md).
- `Sprite = null` hides the entity; a sprite whose texture reference is absent or missing is
  non-drawable until the texture and GPU page are ready (M8). Do not block.
- `ZIndex` orders within a layer, higher later; ties are unspecified. `Tint` multiplies the
  texture, `White` is neutral.
- `FlipX`/`FlipY` are `Planned` (not declared in `Bork2d.Api` yet).
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

public void Setup(IEngine<GameLayer> engine)
{
    _engine = engine;
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
var dt = (float)_engine.Time.DeltaTime; // seconds, clamped by the builder's WithMaxDeltaTime
var now = _engine.Time.Time;            // raw seconds since the first tick
```

- `Status: Current (M2, windowed).` The first tick publishes `Time = 0` and `DeltaTime = 0`;
  afterwards `Time` is raw monotonic seconds (never clamped) and `DeltaTime` is the interval
  between tick starts clamped by the builder's `WithMaxDeltaTime` (default 0.25 s).
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

`Status: Current (M3; asset-reference refactor D-035).` The base pack (or the `WithoutAssets()`
empty filesystem) opens at `Run()` before the window and `Setup`, and an app-scoped loader thread
serves `LoadData`/`LoadAsset<T>`. Packs mount overlays with `LoadPack`, `Browse`/`Exists` read the
merged virtual filesystem, and `AssetException`/`AssetErrorKind` report failures. The loader only
resolves and deserializes; it does not validate asset fields or follow references.

```csharp
using PolyType;

[GenerateShape]
public sealed partial class GameConfig
{
    public required string Name { get; set; }
    public int MaxPlayers { get; set; }
}

[GenerateShape]
public readonly partial record struct LevelTuning(int Gravity, int JumpSpeed);
```

```csharp
// Packs: mount overlays (later wins; no unload).
_engine.Assets.LoadPack("overlays/hd.pck");

// Browse/exists: canonical paths; Browse(null) lists everything.
bool hasConfig = _engine.Assets.Exists("data/config");
IReadOnlyList<AssetInfo> sprites = _engine.Assets.Browse("sprites");

// Typed assets need a compiled shape; raw bytes use LoadData.
GameConfig config = await _engine.Assets.LoadAsset<GameConfig>("data/config");
LevelTuning tuning = await _engine.Assets.LoadAsset<LevelTuning>("data/level-tuning");
byte[] pixels = await _engine.Assets.LoadData("textures/raw/player");

// Sprites are normal public assets; reference links are typed path values.
SpriteAsset player = await _engine.Assets.LoadAsset<SpriteAsset>("sprites/player/idle");
TextureDescriptor texture = await _engine.Assets.LoadAsset(player.Texture);
byte[] texturePixels = await _engine.Assets.LoadData(texture.Payload);
```

- **Base pack and `WithoutAssets`.** By default the app opens `assets.pck` at `Run()`. Use
  `.WithAssetsPack("path/to/game.pck")` for another filesystem path (relative to the process current
  directory, or absolute). If the app has no base pack, call `.WithoutAssets()`: it starts an empty
  virtual filesystem, still starts the loader thread, and still allows `LoadPack` overlays. The two
  are mutually exclusive; a missing or malformed base pack fails `Run()` with `Bork2dException`
  before any window or `Setup`.
- **Typed assets and references.** `LoadAsset<T>` requires a compiled `[GenerateShape]` shape
  (`where T : IShapeable<T>`, D-M3-22/D-M4-11); a missing shape is a compile-time error, not a
  runtime fallback. `T` may be a class or a struct: a `readonly record struct` root loads through the
  same path and can be referenced with `AssetRef<LevelTuning>`. A value-type root has no null value,
  so a MessagePack `nil` payload is an `AssetException(Decode)` fault, never a silent `default`.
  `SpriteAsset.Texture` is an `AssetRef<TextureDescriptor>` and
  `TextureDescriptor.Payload` is a `DataRef`; resolve them with the reference overloads. A `null`
  reference means absent and is not a load request (it throws `ArgumentException`). Both sprite and
  descriptor are public MessagePack assets in `Bork2d.Api.Assets`; the M4 baker validates descriptor
  fields and binding/references, while sprite field validation (UV extents, pivot, topology,
  indices) is M5 (D-M4-06), not the loader. `PackBuilder` validates every non-null reference when it
  builds a pack; the runtime trusts the pack.
- **Colors.** `Color` is linear RGBA and serializes as the float array `[r, g, b, a]` through the
  public `ColorMarshaler` infrastructure, not as an object map; `Color` stays non-`partial`. The
  marshaler is generated into the assembly that declares the containing asset type, so a game
  assembly gets it automatically. A `nil` color surrogate is a `Decode` fault. Re-bake packs that
  contain the older object-map color form; v1 packs have no per-asset schema version.
- **Loading and errors.** A valid missing path, a raw entry passed to `LoadAsset<T>`, and
  decode/size failures fault the returned task instead of throwing:

  ```csharp
  try
  {
      GameConfig config = await _engine.Assets.LoadAsset<GameConfig>("data/config");
  }
  catch (AssetException exception) when (exception.Kind == AssetErrorKind.NotFound)
  {
      // A valid canonical path that is not in the merged VFS.
  }
  ```

  `AssetErrorKind` is `NotFound`, `RawAsset`, `Decode`, `Io`, or `InvalidData`; `AssetPath` names
  the canonical path (or the filesystem path for an IO failure). Null/invalid path arguments still
  throw `ArgumentNullException`/`ArgumentException` from the call. Pack open/parse/mount failures
  are `Bork2dException`, not `AssetException`.
- **Packs, paths, and browsing.** Public asset paths are normalized (`\` → `/`, invariant lowercase)
  then validated: lowercase letters, digits, `_`, `-`, and `/` separators; no leading/trailing
  slash, empty or `.`/`..` segment, or source extension (`textures/bg.png` is invalid). `LoadPack`
  takes a filesystem path, mounts synchronously on top of the stack, and leaves the stack unchanged
  on failure; there is no unload. `Browse(null)` returns every visible path once, sorted by `Path`;
  a canonical prefix matches on a `/` segment boundary. `Exists` is an exact lookup. Results are a
  materialized snapshot, so do not browse per frame. `AssetInfo` reports `Path`, `IsRaw`,
  `IsCompressed`, `Length`, `Size`, and `PackPath` from the header without decompressing. See the
  [reference](reference.md#7-iassets) for the exact member contract.
- **Lifetime and retry.** All members are thread-safe. There is no automatic retry, pack unload, or
  loose-file fallback: a failed lookup stays failed unless a later overlay shadows the path or the
  app restarts. At `Run()` shutdown (after `Teardown`) the service cancels queued and in-flight load
  tasks and joins the loader; after that every `IAssets` member throws `ObjectDisposedException`,
  while the `IEngine.Assets` getter keeps returning the disposed service and never throws.

### Data serialization and bake

`Status: Current (M4) for custom data and sprite-as-data; sprite field validation and slicing are
M5.` Author data as TOML in `Project.Common/Assets`, register it in `bork2d.project.toml`, and let
the M4 tooling bake `assets.pck`. The runtime never sees TOML.

```csharp
using System.Text.Json.Serialization;
using Bork2d.Api.Assets;
using Bork2d.Api.Rendering;
using PolyType;

// The descriptor ([[types]] prefix "data" -> MyGame.Data.GameConfig) registers the type.
// [GenerateShape] + partial is PolyType's rule: it injects IShapeable<T> into the partial type.
[GenerateShape]
public sealed partial class GameConfig
{
    public required string Name { get; set; }
    public int Health { get; set; }

    [JsonPropertyName("max_players")]
    public int MaxPlayers { get; set; }

    [JsonRequired]
    public string? Must { get; set; }

    public Color Tint { get; set; }
    public AssetRef<TextureDescriptor> Texture { get; set; }
}

[GenerateShape]
public readonly partial record struct LevelTuning(int Gravity, int JumpSpeed);
```

```toml
# Assets/data/game.toml -> data/game
name = "campaign"
health = 120
max_players = 4          # [JsonPropertyName("max_players")]
must = "present"         # [JsonRequired]
tint = [1.0, 0.5, 0.25, 1.0]
texture = "textures/player"
```

- **`partial` is PolyType's requirement.** On .NET 8+, `[GenerateShape]` emits
  `IShapeable<T>` through a partial declaration. There is no `[Bork2dSerializable]` attribute and no
  Bork2d source generator; the project descriptor's `[[types]]` list is the registration.
- **Keys and leaves.** Keys are camelCase; `[JsonPropertyName]`, `[JsonIgnore]`, and
  `[JsonRequired]` are honored. Supported leaves are booleans, integers, floats, strings, camelCase
  enums, `Guid`, offset date-times, `Vector2/3/4`, `Color`, `AssetRef<T>`/`DataRef` path strings,
  arrays/lists, `Dictionary<string, T>`, and nested tables. Unknown keys, missing required members,
  type/range errors, and local date-times fail with `file:line:column`. A `readonly record struct` is
  supported as a member and as a root; a `nil` struct-root payload is a `Decode` fault.
- **`Color`.** Stays non-`partial`, serializes as `[r, g, b, a]` through `ColorMarshaler`, and a
  `null` surrogate is a `Decode` fault.
- **References.** `AssetRef<T>`/`DataRef` are canonical path strings; the bake validates every
  present reference against the baked set before writing. An absent key is an absent reference.
- **Commands (tooling only).** `bork2d-bake generate-host`, `bake`, and `validate`; MSBuild normally
  drives them through `build/Bork2d.Bake.targets` (build/publish/clean). Exit codes: `0`
  success/up to date, `1` source diagnostics, `2` usage/descriptor/host error, `3` internal failure.
  The runtime does not reference this tooling. See
  [Assets and tooling §10–§11](../architecture/assets-and-tooling.md#10-toml-source-model) for the
  full mapping/pipeline and the [M4 spec](../specs/m4-serialization-and-bake.md) for scope.

### Building a pack (tooling only)

The pack **writer** is the public tooling surface in `Bork2d.Assets.Pack`; tools, tests, and later
the editor use it to produce the runtime packs the app loads. Game code must not reference
`Bork2d.Assets`.

```csharp
using Bork2d.Assets.Pack;

var builder = new PackBuilder(PackCompressionMode.Release);
builder.AddRaw("textures/raw/player", rgba8Bytes);
builder.AddMessagePack("data/config", new GameConfig { /* [GenerateShape] type */ });
builder.Write("assets.pck"); // overwrites; relative paths resolve against the current directory
```

- `PackCompressionMode.Debug` stores payloads uncompressed; `Release` uses maximum-quality Zstd
  only when strictly smaller. The header is always Zstd with an XXH3-64 checksum; payloads have no
  checksum. There is no byte-determinism guarantee.
- `Build()`/`Write()` validate every non-null `AssetRef<T>`/`DataRef` in added MessagePack assets:
  the path must exist and target the matching entry kind (`AssetRef` → MessagePack, `DataRef` →
  raw). A bad reference throws `Bork2dException` naming the source asset and property path; `null`
  references are allowed.
- `Build()` returns one `byte[]`; `Write(Stream)` streams and leaves the stream open. Builder calls
  are repeatable and do not consume the builder.
- Paths are normalized/validated with the same canonical rules as runtime lookups. Duplicate
  paths, serialization failures, and write failures throw `Bork2dException`; null/invalid path
  arguments throw `ArgumentNullException`/`ArgumentException`.
- See the [pack tooling reference](reference.md#13-pack-tooling-bork2dassetspack-tooling-only) for
  the full contract.

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
- `partial` on a `[GenerateShape]` type is PolyType's rule, not a Bork2d attribute; a missing shape
  is a compile-time error. There is no runtime TOML, reflection serializer, or bake reference.
- The runtime libraries are `IsAotCompatible`; AOT publish/run is a required local gate. The
  default AOT smoke loads the baked fixture pack (class root, struct root through
  `AssetRef<StatsRoot>`, sprite, descriptor, and exact raw payload), browses/exists, and exits 0;
  the windowed demo uses `WithoutAssets()`.

See [NativeAOT](../architecture/nativeaot.md).

## 13. Where to go next

- [API reference](reference.md) — exact members, defaults, exceptions, status.
- [Scene ownership](../architecture/scene-ownership.md) — root lifetime and parent rules.
- [Runtime lifecycle](../architecture/runtime-lifecycle.md) — builder, modes, pacing, shutdown.
- [Assets and tooling](../architecture/assets-and-tooling.md) — pack, descriptors, editor.
- [NativeAOT](../architecture/nativeaot.md) and [Roadmap](../roadmap.md) — constraints and
  milestone ownership of `Planned` behavior.
