# Bork2d

Bork2d is a focused 2D game engine for .NET. It targets **Vulkan 1.3** rendering and **SDL2**
windowing through **Silk.NET**, with gameplay on a tick thread and rendering on a dedicated thread
that consumes immutable frame snapshots. The runtime is **NativeAOT**, assets ship in
**`assets.pck`**, and windowed, offscreen, and headless modes are the target design.

> **Status: pre-alpha.** M0 is complete and M1 (foundation + NativeAOT baseline) is merged into
> `develop` (2026-10-25, PR #1). The engine is not runnable as a game yet: there is no window,
> renderer, pack, or asset system, and `Bork2dApp.Run()` performs one `Setup` → `Tick` → `Teardown`
> smoke, then returns. See the [roadmap](docs/roadmap.md) and [M1 record](docs/specs/m1-foundation.md).

## Projects

| Project | Role |
| --- | --- |
| [`Bork2d.Api`](src/Bork2d/Bork2d.Api) | Public contracts and value types used by game code. |
| [`Bork2d.Runtime`](src/Bork2d/Bork2d.Runtime) | Public builder/host; logging, validation, one-tick `Run`, modes. |
| [`Bork2d.Common`](src/Bork2d/Bork2d.Common) | Engine, root-owned unordered scene tree, transforms, time. |
| [`Bork2d.Render`](src/Bork2d/Bork2d.Render) | Vulkan renderer; reference-wired and empty until M6/M8. |
| [`Bork2d.Assets`](src/Bork2d/Bork2d.Assets) | Pack VFS, loading, descriptors, sprite assets, bake support; empty until M3/M4. |
| [`Bork2d.Tests`](src/Bork2d/Bork2d.Tests) | xUnit suite and dependency-matrix test (135 tests). |
| [`Bork2d.AotSmoke`](src/Bork2d/Bork2d.AotSmoke) | NativeAOT one-tick smoke executable; local publish/run gate. |
| `Bork2d.Editor` | Planned Avalonia editor library/app (M5); JIT, not in the solution. |

Game developers consume only `Bork2d.Api` and `Bork2d.Runtime`. A game solution adds
`Project.Common`, `Project.Desktop`, and `Project.Editor`; those templates are M9.

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

- Tick and render overlap; no fixed tick rate or artificial delay in windowed/offscreen. Three
  snapshot slots bound run-ahead. Headless is timer-paced (default 60, `0` = uncapped).
- Windowed, offscreen (windowless Vulkan), and headless (no GPU/SDL) modes.
- NativeAOT runtime; Nerdbank.MessagePack generated shapes; game passed to the builder as an
  instance.
- Logging: Microsoft.Extensions.Logging + ZLogger; `IEngine.LoggerFactory`; pretty console default.
- Assets: `assets.pck` with a compressed, XXH3-64-checksummed header, uint64 offsets, later
  overlays win, and debug/release payload compression. A missing base pack is fatal.
- Textures are MessagePack descriptors plus raw RGBA8 payloads; sprites are quads with UVs, pivot,
  topology, optional indices, and pixels-per-unit scale. No vertices array; the shader generates
  corners.
- One instanced draw per non-empty camera, bindless texture pages, dynamic rendering.
- Scene objects are owned by `IEngine.Root` (read-only). Detach with `Parent = null`; re-attach the
  same object later. No entity `Dispose`/`IsAlive`, IDs, or ordered discovery.
- Layers are `[Flags]` bits; absolute units, Y-up; `Camera.Size` is half the visible height.
- Warnings-as-errors, style-as-errors, `dotnet format`, the docs link check, and the local AOT
  smoke are the quality gate.
