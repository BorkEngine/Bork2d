# Bork2d

Bork2d is a small 2D game engine for .NET. Gameplay runs on a tick thread and a dedicated render
thread draws immutable frame snapshots with **Vulkan 1.3**. Windowing and input use **SDL2** through
**Silk.NET**, assets ship in a baked `assets.pck`, and the runtime is **NativeAOT**-ready.

The engine is in early development. The contract, host, Vulkan renderer, sprite renderer, asset
pipeline, and the Pong example are implemented; there is no editor yet.

## What it gives you

- A root-owned scene graph: transforms, cameras, sprite renderers, and `[Flags]` layer masks.
- Windowed, offscreen, and headless modes behind one builder.
- Tick/render decoupling through a three-slot snapshot ring; no fixed tick rate in
  windowed/offscreen mode.
- Instanced sprite draws with bindless texture pages, one draw per non-empty camera.
- Baked packs from TOML data, source processors, and sidecars, with an incremental `bake`.
- MessagePack assets with generated shapes; no reflection or TOML at runtime.
- Warnings-as-errors, style enforcement, AOT/trim analyzers, and a documentation link check in CI.

## Requirements

- Windows.
- The .NET SDK pinned by [`global.json`](global.json): `11.0.100-rc.1`, targeting `net11.0`.
- A Vulkan 1.3 loader and GPU for windowed and offscreen runs. Building needs no Vulkan SDK;
  shaders are compiled during the build.

## Repository layout

| Path | Contents |
| --- | --- |
| [`src/Bork2d`](src/Bork2d) | Engine solution: engine, renderer, runtime host, bake tooling, tests. |
| [`examples/pong`](examples/pong) | Playable Pong, the NativeAOT gate, and a custom processor. |
| [`build`](build) | MSBuild targets for baking, pack copying, and GLSL compilation. |
| [`docs`](docs) | Architecture and project guides. |
| [`scripts/check-docs.ps1`](scripts/check-docs.ps1) | Markdown link and anchor check. |

## Build, test, and format

```powershell
dotnet build src/Bork2d/Bork2d.slnx -c Debug
dotnet test  src/Bork2d/Bork2d.slnx -c Debug
dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
pwsh scripts/check-docs.ps1
```

Tests that need a real window or GPU are skipped by default:

```powershell
$env:BORK2D_RUN_WINDOW_TESTS = '1'
dotnet test src/Bork2d/Bork2d.slnx -c Debug

$env:BORK2D_RUN_GPU_TESTS = '1'
dotnet test src/Bork2d/Bork2d.slnx -c Debug --filter "Category=Gpu"
```

## Run Pong

Building the example bakes its assets and copies the pack next to the app:

```powershell
dotnet build examples/pong/Pong.slnx -c Debug
dotnet run --project examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Debug
```

Controls: **W/S** move the left paddle, **Space** pauses, **R** resets, **Esc** or the close button
exits. A desktop session and a Vulkan 1.3 device are required.

NativeAOT publish:

```powershell
dotnet publish examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Release -r win-x64
```

## Write a game

Implement `IGame<TLayer>`. The game instance is constructed by your code and passed to the builder:

```csharp
[Flags]
public enum Layer { None = 0, World = 1, Hud = 2 }

public sealed class MyGame : IGame<Layer>
{
    private IEngine<Layer>? _engine;

    public void Setup(IEngine<Layer> engine)
    {
        _engine = engine;

        ICamera<Layer> camera = engine.CreateCamera(engine.Root);
        camera.Size = 10f;                       // half the visible height
        camera.CullingMask = Layer.World;
        camera.Clear = ClearKind.Color;

        SpriteAsset sprite = engine.Assets
            .LoadAsset<SpriteAsset>("sprites/hero").GetAwaiter().GetResult();

        ISpriteRender<Layer> renderable = engine.CreateSpriteRender(engine.Root);
        renderable.Sprite = sprite;
        renderable.Layer = Layer.World;
    }

    public void Tick() { }
    public void Teardown() => _engine?.Root.DetachAll();
}
```

```csharp
using var app = new Bork2dAppBuilder<Layer>()
    .WithGame(new MyGame())
    .WithTitle("My game")
    .WithSize(1280, 720)
    .Build();
app.Run();
```

`Setup` may block on asset loads; `Tick` must return, joining any game workers first. Engine members
may be called from any thread during the game callbacks.

## Author assets

An assets executable configures the build-time pipeline:

```csharp
using Bork2d.Bake;
using Bork2d.Engine.Assets;

return await new BakeApplicationBuilder()
    .WithProjectRoot(".")
    .WithPack("assets.pck", "Assets")
    .UseDefaultAssets()
    .RegisterAsset<GameSettings>()       // a [GenerateShape] data type
    .UseDefaultProcessors()
    .WithTextureDefaults(TextureFilter.Linear, TextureWrap.Clamp)
    .Build()
    .RunAsync(args);
```

Data is authored as TOML with an explicit CLR type. A sprite source:

```toml
texture = "art/hero"
uvs = [[0, 0], [1, 0], [1, 1], [0, 1]]
scale = [1.0, 1.0]
pivot = [0.5, 0.5]
topology = "quad"

[bork2d]
type = "Bork2d.Engine.Assets.SpriteAsset"
```

`scale` is the per-axis world size of a full UV quad: this hero draws one world unit square, and
the source texture's pixel dimensions never affect the geometry.

Texture sources (`png`, `jpg`, `jpeg`, `bmp`, `tga`) become a descriptor plus a raw RGBA8 payload.
An optional sidecar overrides sampler state:

```toml
# Assets/art/hero.png.toml
filter = "linear"
wrap = "clamp"
```

See [Bake](docs/bake.md) for processors, multiple packs, and MSBuild integration.

## Documentation

- [Architecture](docs/architecture.md) - projects, threads, frame handoff, and invariants.
- [Engine](docs/engine.md) - the public contract, scene model, and asset service.
- [Render](docs/render.md) - the Vulkan renderer and sprite pipeline.
- [Runtime](docs/runtime.md) - the builder, host loop, window, and input.
- [Bake](docs/bake.md) - the build-time asset pipeline and CLI.
