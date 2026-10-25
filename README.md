# Bork2d

Bork2d is a focused 2D game engine for .NET. It will use **Vulkan 1.3** for rendering and
**SDL2** for windowing through **Silk.NET**. Gameplay ticks on the engine tick thread while a
dedicated render thread presents the previous frame; each tick publishes an immutable frame
snapshot. The runtime is **NativeAOT** and assets are packed into **`assets.pck`**, referenced
by canonical asset paths. Windowed, offscreen, and headless modes are the target design.

> **Status: pre-alpha. M0 complete; M1 foundation implemented (WP1–WP8), final PR pending.**
> The solution builds and the M1 tests pass, but the engine is not runnable yet: there is no
> window, renderer, pack, or asset system, and `Bork2dApp.Run()` performs one
> `Setup` → `Tick` → `Teardown` smoke, then returns. See the
> [M1 implementation spec](docs/specs/m1-foundation.md) and the [roadmap](docs/roadmap.md).

## Projects

| Project | Role |
| --- | --- |
| [`Bork2d.Api`](src/Bork2d/Bork2d.Api) | Public contracts and value types used by game code. |
| [`Bork2d.Runtime`](src/Bork2d/Bork2d.Runtime) | Public startup builder/host; logging, validation, one-tick `Run`, modes. |
| [`Bork2d.Common`](src/Bork2d/Bork2d.Common) | Engine, root-owned unordered scene tree, transforms, time (snapshots are M7). |
| [`Bork2d.Render`](src/Bork2d/Bork2d.Render) | Vulkan renderer; reference-wired and empty until M6/M8. |
| [`Bork2d.Assets`](src/Bork2d/Bork2d.Assets) | Pack VFS, loading, texture descriptors, sprite assets, bake support; empty until M3/M4. |
| [`Bork2d.Tests`](src/Bork2d/Bork2d.Tests) | xUnit suite and dependency-matrix test (135 tests). |
| [`Bork2d.AotSmoke`](src/Bork2d/Bork2d.AotSmoke) | NativeAOT one-tick smoke executable; local publish/run gate. |
| `Bork2d.Editor` | Planned Avalonia editor library/app; JIT, not in the solution yet. See [Editor docs](docs/architecture/editor.md). |

Game developers only consume `Bork2d.Api` and `Bork2d.Runtime`; everything else is an
implementation detail. A game solution will be split into `Project.Common` (game + source
assets), `Project.Desktop` (entry point), and `Project.Editor` (thin editor host); those template
projects are M9 and are not in this repository yet.

## Build

```powershell
dotnet build src/Bork2d/Bork2d.slnx -c Debug
dotnet build src/Bork2d/Bork2d.slnx -c Release
dotnet test src/Bork2d/Bork2d.slnx -c Debug
dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
```

Current requirements: .NET SDK `11.0.100-rc.1` (target `net11.0`), Windows. The finished
engine additionally needs a Vulkan 1.3-capable GPU/driver and the SDL2 native runtime. The
runtime/game is intended to publish with NativeAOT; the editor and build tools stay JIT.
Warnings and C# style violations are build errors; see [Contributing](docs/contributing.md).

NativeAOT is a **required local gate, not a CI job** (CI runs build/test/format on pushes to
`develop`):

```powershell
dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64
& src/Bork2d/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe
```

## Documentation

Start at the [documentation index](docs/README.md).

- [Architecture overview](docs/architecture/overview.md) — goals, invariants, modes.
- [Scene ownership](docs/architecture/scene-ownership.md) — root-owned lifetime model.
- [Projects and dependencies](docs/architecture/projects.md) — public boundary and game layout.
- [Runtime lifecycle](docs/architecture/runtime-lifecycle.md) — builder, modes, pacing, shutdown.
- [Threading and frame handoff](docs/architecture/threading.md) — bounded run-ahead snapshots.
- [Assets and pack files](docs/architecture/assets.md) — `assets.pck`, compressed header/checksum, overlays, descriptors, sprites.
- [Logging](docs/architecture/logging.md) — ZLogger and `IEngine.LoggerFactory`.
- [Editor](docs/architecture/editor.md) — Avalonia builder and asset/data authoring.
- [Data and serialization](docs/architecture/data-and-serialization.md) — TOML → Nerdbank.MessagePack.
- [NativeAOT](docs/architecture/nativeaot.md) — runtime constraints and the local AOT gate.
- [Rendering](docs/architecture/rendering.md) — Vulkan 1.3, one instanced draw per camera.
- [API guide](docs/api/guide.md) and [API reference](docs/api/reference.md).
- [Implementation roadmap](docs/roadmap.md) and [M1 implementation spec](docs/specs/m1-foundation.md).
- [Decision log](docs/decisions.md) and [contributing](docs/contributing.md).

## Design at a glance

- Tick and render overlap; no fixed tick rate, no artificial delay in windowed/offscreen.
  Three snapshot slots bound run-ahead. Headless is timer-paced (default 60, `0` = uncapped).
- Windowed, offscreen (windowless Vulkan), and headless (no GPU/SDL) modes.
- NativeAOT runtime; Nerdbank.MessagePack generated shapes; game passed to the builder as an instance.
- Logging: Microsoft.Extensions.Logging + ZLogger; `IEngine.LoggerFactory`; pretty console default.
- Assets: `assets.pck` with a compressed, XXH3-64-checksummed header (`PackHeader`/`PackEntry`
  MessagePack classes) and uint64 offsets; later pack overlays win; debug assets uncompressed,
  release Zstd when smaller. A missing base pack throws at startup.
- Textures: MessagePack descriptors + raw RGBA8 payloads. Sprites: MessagePack quads with UVs,
  pivot, topology, optional indices, and pixels-per-unit scale; TOML source, baked. No vertices
  array; the shader generates quad corners.
- One instanced draw per non-empty camera, generated sprite quads, bindless texture pages, dynamic rendering.
- Scene objects are owned by `IEngine.Root` (read-only). Parented objects persist, unparented
  objects are collectible. Detach to remove and re-attach the same object later; there is no
  entity `Dispose`/`IsAlive`.
- Layers are `[Flags]` bits (one per renderable, mask per camera).
- Abstract world units, Y-up; `Camera.Size` is half the visible height; no global PPU.
- Editor is an Avalonia JIT tool with compile-time builder registrations; no scene editing.
- Warnings-as-errors, C# style rules as errors, and `dotnet format` are the M1 quality gate.
  CI runs build/test/format on `develop` only; the NativeAOT publish/run is a required local
  gate.
