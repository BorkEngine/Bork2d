# Bork2d

Bork2d is a focused 2D game engine for .NET. It targets **Vulkan 1.3** rendering and **SDL2**
windowing through **Silk.NET**, with gameplay on a tick thread and rendering on a dedicated thread
that consumes immutable frame snapshots. The runtime is **NativeAOT**, assets ship in
**`assets.pck`**, and windowed, offscreen, and headless modes are the target design.

> **Status: pre-alpha.** M0–M4 are merged into `develop`, and the post-M4 asset-processor task
> (D-036/D-037/D-038/D-039/D-040) is merged (2026-10-27, PR #8). Windowed mode opens a real SDL
> window and
> runs an uncapped loop; the runtime loads and browses `assets.pck` through the app-scoped asset
> service. The bake is the game-owned builder flow (D-036/D-037/D-038): a build-time
> `MyGame.Assets` executable registers explicit data types and asset processors, bakes on build, and
> records a schema-3 incremental manifest; PPM and Unit are the acceptance processors. There is still
> no renderer (M8), and headless/offscreen keep the one-tick `Setup` → `Tick` → `Teardown` sequence.
> See the [roadmap](docs/roadmap.md), the
> [file-processors spec](docs/specs/file-processors.md), and the
> [asset-processors architecture](docs/architecture/asset-processors.md).

## Projects

| Project | Role |
| --- | --- |
| [`Bork2d.Api`](src/Bork2d/Bork2d.Api) | Public contracts and value types used by game code. |
| [`Bork2d.Runtime`](src/Bork2d/Bork2d.Runtime) | Public builder/host; logging, validation, SDL window/input/time, windowed loop, one-tick modes. |
| [`Bork2d.Common`](src/Bork2d/Bork2d.Common) | Engine, root-owned unordered scene tree, transforms, time. |
| [`Bork2d.Render`](src/Bork2d/Bork2d.Render) | Vulkan renderer; reference-wired and empty until M6/M8. |
| [`Bork2d.Assets`](src/Bork2d/Bork2d.Assets) | Pack reader/writer, VFS, loader, descriptors, sprite assets; public tooling is `Bork2d.Assets.Pack`. |
| [`Bork2d.Bake`](src/Bork2d/Bork2d.Bake) | Internal JIT bake implementation: TOML binder, processor framework, texture processor, pack composition, schema-3 incremental planner. |
| [`Bork2d.Bake.Builder`](src/Bork2d/Bork2d.Bake.Builder) | Public JIT builder/processor/CLI surface used by game and fixture assets executables. |
| [`Bork2d.BakeFixture`](src/Bork2d/Bork2d.BakeFixture) | AOT-safe game-side fixture data types (`GameConfig`, `Stats`, `StatsRoot`, `Unit`). |
| [`Bork2d.BakeFixture.Assets`](src/Bork2d/Bork2d.BakeFixture.Assets) | Build-only fixture assets executable; owns `Assets/`/`DLC1/`, PPM/Unit processors, and bakes on build. |
| [`Bork2d.Tests`](src/Bork2d/Bork2d.Tests) | xUnit suite and dependency-matrix test (958 tests: 946 deterministic, 12 opt-in/conditional). |
| [`Bork2d.AotSmoke`](src/Bork2d/Bork2d.AotSmoke) | NativeAOT smoke executable: headless one-tick run loading the baked fixture packs (8 main / 9 merged entries) plus `--windowed [--ticks N]` SDL demo; local publish/run gate. |
| `Bork2d.Editor` | Planned Avalonia editor library/app (M5); JIT, not in the solution. |

Game developers consume only `Bork2d.Api` and `Bork2d.Runtime`. `Bork2d.Api` is grouped by area
(`Bork2d.Api.Scene`, `.Rendering`, `.Input`, `.Platform`, `.Assets`); the tooling pack writer lives
in `Bork2d.Assets.Pack` and the builder/processor surface in `Bork2d.Bake.Builder`. A game solution
adds `Project.Common`, `MyGame.Assets`, `Project.Desktop`, and `Project.Editor`; those templates are
M9. `MyGame.Assets` is build-time only and never enters the runtime or AOT publish graph.

## Build

```powershell
dotnet build src/Bork2d/Bork2d.slnx -c Debug
dotnet build src/Bork2d/Bork2d.slnx -c Release
dotnet test src/Bork2d/Bork2d.slnx -c Debug
dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
```

Requires the .NET SDK pinned by `global.json` (`11.0.100-rc.1`, target `net11.0`) on Windows.
Warnings and C# style violations are build errors. The NativeAOT publish/run smoke is a required
local gate, not a CI job:

```powershell
dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64
& src/Bork2d/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe
& src/Bork2d/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe --windowed --ticks 120
```

The windowed run needs a desktop session; `--windowed` without `--ticks` is interactive
(Escape/close). The opt-in real-window tests use the same SDL2 native:

```powershell
$env:BORK2D_RUN_WINDOW_TESTS = '1'
dotnet test src/Bork2d/Bork2d.slnx -c Debug
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
- Target modes: windowed, offscreen (windowless Vulkan from M6/M8), and headless (no GPU/SDL);
  offscreen and headless currently use the one-tick sequence.
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
  smoke are the quality gate.
