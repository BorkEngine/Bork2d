# Bork2d

Bork2d is a focused 2D game engine for .NET. It targets **Vulkan 1.3** rendering and **SDL2**
windowing through **Silk.NET**, with gameplay on a tick thread and rendering on a dedicated thread
that consumes immutable frame snapshots. The runtime is **NativeAOT**, assets ship in
**`assets.pck`**, and windowed, offscreen, and headless modes are the target design.

> **Status: pre-alpha.** M0–M4 are merged into `develop`, and the post-M4 asset-processor task
> (D-036/D-037/D-038/D-039/D-040) is merged (2026-10-27, PR #8). Windowed mode opens a real SDL
> window, creates the Vulkan instance/surface on the OS thread, and runs the renderer and an
> uncapped loop; offscreen mode runs the renderer against a fixed sRGB target until `Exit` and
> advances `ITime`; headless keeps the one-tick, no-GPU path. The runtime loads and browses
> `assets.pck` through the app-scoped asset service. **M6 (Vulkan bring-up) is implemented
> (2026-10-30) and awaits the final human-reviewed PR** (not merged): the Render foundation, the
> windowed renderer, the offscreen renderer, and the Runtime host wiring are complete, and the
> AOT/Pong gate proves the published `--headless`, `--windowed --ticks 120`, and
> `--offscreen --ticks 120` runs with a zero-warning publish. The bake is the game-owned builder
> flow
> (D-036/D-037/D-038): a build-time
> `MyGame.Assets` executable registers explicit data types and asset processors, bakes on build, and
> records a schema-3 incremental manifest; Pong's `CircleProcessor` is the worked custom processor
> (the former PPM/Unit fixture processors were removed with the exploratory examples). The window
> shows a black clear until sprites arrive in M8.
> The project-consolidation refactor (D-041/D-042,
> [spec](docs/specs/refactor-projects.md)) is merged (2026-10-28, PR #9) and reduces the engine
> solution to five projects: the public contract and engine implementation live in `Bork2d.Engine`,
> and the bake tooling lives in `Bork2d.Bake`. The Pong example (D-043/D-044,
> [spec](docs/specs/pong-example.md)) is merged into `develop` (2026-10-29, PR #10); it
> replaces the exploratory smoke/fixture projects with `examples/pong` and is the NativeAOT gate.
> See the [roadmap](docs/roadmap.md), the
> [file-processors spec](docs/specs/file-processors.md), and the
> [asset-processors architecture](docs/architecture/asset-processors.md).

## Projects

The engine solution is `src/Bork2d/Bork2d.slnx`; the Pong example is
`examples/pong/Pong.slnx`.

| Project | Role |
| --- | --- |
| [`Bork2d.Engine`](src/Bork2d/Bork2d.Engine) | Public contract (`Bork2d.Engine.*`) plus the engine implementation: root-owned unordered scene tree, transforms, time, pack reader/writer, VFS, loader, and asset service. Public tooling is `Bork2d.Engine.Assets.Pack`. |
| [`Bork2d.Bake`](src/Bork2d/Bork2d.Bake) | Public JIT builder/processor/command surface plus the internal bake implementation (TOML binder, processors, texture pipeline, pack composition, schema-3 incremental planner). |
| [`Bork2d.Render`](src/Bork2d/Bork2d.Render) | Vulkan renderer: instance/device foundation, windowed swapchain/clear/present, and offscreen target/clear/discard; internal only, sprites are M8. |
| [`Bork2d.Runtime`](src/Bork2d/Bork2d.Runtime) | Public builder/host; logging, validation, SDL window/input/time, Vulkan instance/surface and renderer composition, windowed/offscreen loops, headless one-tick path. |
| [`Bork2d.Tests`](src/Bork2d/Bork2d.Tests) | xUnit suite (1028 deterministic + 16 conditional), dependency-matrix/public-surface tests, and the Q-20 metadata guard. |
| [`Pong.Common`](examples/pong/Pong.Common) | Game library: `GameSettings` (struct root), simulation, AI, title, and `PongGame`; `Bork2d.Engine` only. |
| [`Pong.Assets`](examples/pong/Pong.Assets) | Build-only assets executable: `Assets/`, `CircleProcessor`, and the `BakeApplicationBuilder` config; bakes the 7-entry `assets.pck`. |
| [`Pong.Desktop`](examples/pong/Pong.Desktop) | AOT desktop host and the NativeAOT gate: `--headless` smoke, the interactive `--windowed [--ticks N]` example, and `--offscreen --ticks N`. |
| [`Pong.Tests`](examples/pong/Pong.Tests) | 41 game tests: simulation/AI/title, bake/load integration, the headless game, and the linked `PongArgumentsTests`. |
| `Bork2d.Editor` | Planned Avalonia editor library/app (M5); JIT, not in either solution. |

Game developers consume only `Bork2d.Engine` and `Bork2d.Runtime`. `Bork2d.Engine` is grouped by area
(`Bork2d.Engine.Scene`, `.Rendering`, `.Input`, `.Platform`, `.Assets`); the tooling pack writer lives
in `Bork2d.Engine.Assets.Pack` and the builder/processor surface in `Bork2d.Bake`. The Pong example
is the current game solution shape; the M9 template adds `Project.Common`, `MyGame.Assets`,
`Project.Desktop`, and `Project.Editor`. `MyGame.Assets` is build-time only and never enters the
runtime or AOT publish graph.

## Build

```powershell
dotnet build src/Bork2d/Bork2d.slnx -c Debug
dotnet build src/Bork2d/Bork2d.slnx -c Release
dotnet test src/Bork2d/Bork2d.slnx -c Debug
dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
dotnet build examples/pong/Pong.slnx -c Debug
dotnet test examples/pong/Pong.slnx -c Debug
dotnet format examples/pong/Pong.slnx --verify-no-changes --no-restore
```

Requires the .NET SDK pinned by `global.json` (`11.0.100-rc.1`, target `net11.0`) on Windows.
Warnings and C# style violations are build errors. The Pong solution is not a CI job; the local
AOT gate exercises its desktop app. The NativeAOT publish/run gate is required locally:

```powershell
dotnet publish examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Release -r win-x64
$pong = 'examples/pong/Pong.Desktop/bin/Release/net11.0/win-x64/publish/Pong.Desktop.exe'
& $pong --headless                 # baked 7-entry pack + one tick
& $pong --windowed --ticks 120     # windowed gate: exit 0 after exactly 120 ticks
& $pong --offscreen --ticks 120    # offscreen gate: exit 0 after exactly 120 ticks
```

The windowed run needs a desktop session **and a Vulkan 1.3 loader/device**; `--windowed` without
`--ticks` is interactive (Escape/close), and `--offscreen` runs non-interactively and discards
frames. The window shows the M6 black clear; sprites arrive in M8. The three published runs are the
M6 AOT gate. The opt-in real-window and real-GPU tests use the same SDL2/Vulkan runtime:

```powershell
$env:BORK2D_RUN_WINDOW_TESTS = '1'
dotnet test src/Bork2d/Bork2d.slnx -c Debug
$env:BORK2D_RUN_GPU_TESTS = '1'
dotnet test src/Bork2d/Bork2d.slnx -c Debug --filter "Category=Gpu"
```

See [Contributing](docs/contributing.md) for the full gate list.

## Documentation

Start at the [documentation index](docs/README.md).

- [Architecture overview](docs/architecture/overview.md) — goals, invariants, modes.
- [Scene ownership](docs/architecture/scene-ownership.md) — root-owned lifetime model.
- [Runtime lifecycle](docs/architecture/runtime-lifecycle.md) — builder, modes, pacing, shutdown.
- [Threading](docs/architecture/threading.md) — locks and the snapshot handoff.
- [Assets and tooling](docs/architecture/assets-and-tooling.md) — pack format, sprites, TOML bake,
  editor.
- [API guide](docs/api/guide.md) and [API reference](docs/api/reference.md).
- [Roadmap](docs/roadmap.md), [decision log](docs/decisions.md), [contributing](docs/contributing.md).

## Design at a glance

- Target design: tick and render overlap; no fixed tick rate or artificial delay in
  windowed/offscreen. Three snapshot slots bound run-ahead. Headless is timer-paced (default 60,
  `0` = uncapped) from M7; today it runs one tick.
- Target modes: windowed, offscreen (windowless Vulkan target, frames discarded, loop until `Exit`),
  and headless (no GPU/SDL/renderer, one tick). Windowed and offscreen run the real renderer
  (M6) and present/discard an opaque-black clear; frame handoff/pacing is M7.
- GPU tests are opt-in (`BORK2D_RUN_GPU_TESTS=1`, serialized `RealGpu` collection): the windowed
  test creates a real SDL window/instance/surface, and two offscreen tests cover the renderer and
  the app host, all asserting validation-clean runs where the layer is available.
- NativeAOT runtime; Nerdbank.MessagePack generated shapes; game passed to the builder as an
  instance.
- Logging: Microsoft.Extensions.Logging + ZLogger; `IEngine.LoggerFactory`; pretty console default.
- Assets: `assets.pck` with a compressed, XXH3-64-checksummed header, uint64 offsets, later
  overlays win, and debug/release payload compression. A missing base pack is fatal unless the app
  uses `WithoutAssets()`.
- Bake: a build-time `MyGame.Assets` executable configured with `BakeApplicationBuilder` (ordered
  packs, texture defaults, processors, explicit `RegisterAsset<T>()` types) bakes on build and is the
  CLI; source file types are registered processors with `None`/`Required` sidecars. The schema-3
  manifest makes an unchanged second build a no-op. Runtime data types stay in `Project.Common`.
- Textures are MessagePack descriptors plus raw RGBA8 payloads; the descriptor's
  `Format`/`Filter`/`Wrap` are the non-null `TextureFormat`/`TextureFilter`/`TextureWrap` enums
  (defaults `Rgba8`/`Linear`/`Clamp`). Sprites are quads with UVs, pivot, topology, optional
  indices, and pixels-per-unit scale. No vertices array; the shader generates corners.
- One instanced draw per non-empty camera, bindless texture pages, dynamic rendering.
- Scene objects are owned by `IEngine.Root` (read-only). Detach with `Parent = null`; re-attach the
  same object later. No entity `Dispose`/`IsAlive`, IDs, or ordered discovery.
- Layers are `[Flags]` bits; absolute units, Y-up; `Camera.Size` is half the visible height.
- Warnings-as-errors, style-as-errors, `dotnet format`, the docs link check, and the local AOT
  gate are the quality gate.
