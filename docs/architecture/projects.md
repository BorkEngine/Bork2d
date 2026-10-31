# Projects and Dependencies

> **Status:** `Current`, merged into `develop` (2026-10-28, PR #9; D-041/D-042) and enforced by the
> dependency test. The engine solution has five projects; the Pong example (`Pong.Common`,
> `Pong.Assets`, `Pong.Desktop`, `Pong.Tests`) lives in `examples/pong/Pong.slnx` and replaces the
> exploratory smoke/fixture projects (D-043/D-044; merged 2026-10-29, PR #10).
> `Bork2d.Engine` carries the `Bork2d.Engine.*` public contract plus the internal engine
> implementation, and `Bork2d.Bake` carries the public bake-tooling surface plus the internal bake
> implementation. M6 (Vulkan bring-up) is merged (2026-10-31, PR #11, merge `5de725c`):
> `Bork2d.Render` holds the renderer foundation, the windowed and offscreen renderers, and the
> internal seam; `Bork2d.Runtime` hosts both renderers and still exposes only the builder/app. The
> windowed/offscreen AOT gate is proven (zero-warning publish and three published runs exit 0).
> M7 (frame handoff and pacing) is merged into `develop` (2026-10-31, PR #12, merge `5de5ac3`): the
> Render handoff ring/consumer and the Runtime
> acquire/publish/headless host add **no** project reference, package, or public type beyond the
> already-approved `FlipX`/`FlipY` (D-M7-02).
> **M8 (sprite renderer) is in progress** (WP-1 is documentation reconciliation; no M8 code is
> written yet). The approved target adds only a **build-only** `Bork2d.ShaderCompiler` tool
> (Silk.NET.Shaderc/Silk.NET.Shaderc.Native 2.23.0): no new runtime project, package, or public
> Render type, and the dependency matrix in §2 is unchanged.

## 1. Current projects

All engine projects target `net11.0` and live in `src/Bork2d/`; the Pong example lives in
`examples/pong/`. They are listed in [`Bork2d.slnx`](../../src/Bork2d/Bork2d.slnx) and
[`Pong.slnx`](../../examples/pong/Pong.slnx).

| Project | References | Status | Responsibility |
| --- | --- | --- | --- |
| `Bork2d.Engine` | none (Bork2d) | `Current` | Public contract (`Bork2d.Engine`, `.Scene`, `.Rendering`, `.Input`, `.Platform`, `.Assets`) plus the internal root-owned scene tree, transforms, time, MessagePack shape policy, v1 pack reader/writer, VFS, loader, and asset service. The tooling-only `Bork2d.Engine.Assets.Pack.PackBuilder`/`PackCompressionMode` stay public for pack creation. |
| `Bork2d.Bake` | `Bork2d.Engine` | `Current` | Public JIT tooling surface (`BakeApplicationBuilder`, `BakeApplication`, `RegisterAsset<T>()`, `UseDefaultAssets()`, `UseDefaultProcessors()`, processor contracts, sidecar policy, output sink, commands) plus the internal implementation (Tomlyn binder, processor registry, texture processor, pack composition, schema-3 incremental planner). |
| `Bork2d.Render` | `Bork2d.Engine` + `Silk.NET.Vulkan*` | `Current` (M6/M7) | Vulkan instance/validation/debug-utils bootstrap, physical and logical device selection (features, extensions, limits), the internal renderer seam and control channel, the pure surface-format/present-mode/swapchain policies, the windowed renderer (swapchain create/recreate, two frames in flight, dynamic-rendering clear, present, resize/minimize/fullscreen, surface-loss recovery, counters, validation observation), the offscreen renderer (two permanent sRGB targets, one allocation per image, clear/discard), and the M7 handoff (three-slot ring, snapshot consumer, combined wait, fence-held slot lifetime, suspended minimized drain, empty per-frame instance resources). Sprites/uploads and instance writes are M8. |
| `Bork2d.Runtime` | `Bork2d.Engine`, `Bork2d.Render` | `Current` (M6/M7) | Public builder/app, validation, windowed SDL + Vulkan instance/surface host, renderer composition and startup/shutdown ordering, windowed/offscreen snapshot acquire/capture/publish with ring backpressure, the paced headless host, app-owned logging, and the app-scoped asset-service lifecycle. |
| `Bork2d.Tests` | `Bork2d.Engine`, `Bork2d.Bake`, `Bork2d.Render`, `Bork2d.Runtime` | `Current` | Deterministic xUnit suite, dependency-matrix/public-surface tests, and the Q-20 metadata guard. It references no example project. |
| `Pong.Common` (examples) | `Bork2d.Engine` | `Current` (D-043/D-044; AOT-compatible) | The game-side runtime library: `GameLayer`, the `[GenerateShape]` `GameSettings` struct root, `PongSide`, the engine-free deterministic `PongSimulation`, `PongAi`, `PongTitle`, and `PongGame` (the `IGame<GameLayer>` implementation, loaded sprite/descriptor chain, title, score logging, resize). |
| `Pong.Assets` (examples) | `Bork2d.Bake`, `Pong.Common` | `Current` (D-043/D-044; build-time only) | Build-time executable for the game: bakes `Assets/` → `assets.pck` (7 entries), configures texture defaults, registers `GameSettings`, and registers `CircleProcessor` (JSON `.circle` source, `SidecarPolicy.None`). It owns the paddle PNG, the TOML sources, and the generated ball texture, and is never in the runtime/AOT publish graph. |
| `Pong.Desktop` (examples) | `Bork2d.Runtime`, `Pong.Common` (+ build-only `Pong.Assets` through the copy target) | `Current` (D-043/D-044; M6/M7 three-mode gate) | NativeAOT host and the AOT gate: `--headless` 120-tick acceptance run (or `--headless --ticks N`), the interactive `--windowed [--ticks N]` example, and the non-interactive `--offscreen --ticks N` acceptance run. It imports `build/Bork2d.Assets.Copy.targets`; the assets executable is built out of process and its pack copied next to the app. |
| `Pong.Tests` (examples) | `Pong.Common`, `Pong.Assets`, `Bork2d.Engine`, `Bork2d.Bake`, `Bork2d.Runtime` | `Current` | 46 deterministic xUnit tests: simulation/serve/bounce/scoring/pause/reset, AI steering, title formatting, the bake/load chain (real `Pong.Assets/Assets` copied to a temp project), invalid `.circle` diagnostics, the full headless `PongGame` run with an explicit tick limit, and the linked `PongArgumentsTests` mode/tick parsing cases (including the 120-tick headless default and the override). |
| `Bork2d.Editor` | `Bork2d.Engine`, `Bork2d.Bake` (+ Avalonia) | `Out of scope` | Avalonia editor library/app; JIT; consumes the public `Bork2d.Bake` builder/processor surface. M5 was removed from Stage 1. |
| `Bork2d.ShaderCompiler` (build-only) | `Silk.NET.Shaderc`, `Silk.NET.Shaderc.Native` `2.23.0` | `Planned` (M8, in progress) | Build-time tool at `build/Bork2d.ShaderCompiler`: compiles the checked-in GLSL under `Bork2d.Render/Shaders/` to SPIR-V (Vulkan 1.3 target) and emits generated C# with the SPIR-V byte arrays into `Bork2d.Render`'s compilation. JIT, build-only, and never in the runtime/AOT publish graph; no runtime project references it. No Vulkan SDK requirement. |

`Bork2d.Engine` references `PolyType` `1.4.1` (the `IShapeable<T>` contract and the public
generated shapes), `Nerdbank.MessagePack` `1.4.95` (generated-shape serialization; no reflection
resolver), and `System.IO.Hashing` `11.0.0-rc.1.26425.128` (`XxHash3`/`XxHash128`). Zstd is the
.NET 11 built-in `System.IO.Compression` implementation. `Bork2d.Runtime` references
`Microsoft.Extensions.Logging` `11.0.0-rc.1.26425.128`, `ZLogger` `2.5.10`, and `Silk.NET.SDL`
`2.23.0` (which carries `Ultz.Native.SDL` `2.32.10`; the shipped win-x64 `SDL2.dll` reports
`SDL_GetVersion` **2.30.8**, the runtime version M2 is verified against). No SDL type leaves
`Bork2d.Runtime`, and `Bork2d.Engine` stays free of GPU/OS/Silk/ZLogger/Avalonia.

`Bork2d.Bake` references `Bork2d.Engine` plus `Tomlyn` `2.10.1`,
`PolyType` `1.4.1`, `StbImageSharp` `2.30.16`, and `System.IO.Hashing`
`11.0.0-rc.1.26425.128`; it is JIT and sets neither `IsAotCompatible` nor `PublishAot`. The public
surface hides Tomlyn, StbImageSharp, the internal bake implementation, and `PackBuilder`.

`Bork2d.Render` pins `Silk.NET.Vulkan` `2.23.0`,
`Silk.NET.Vulkan.Extensions.KHR` `2.23.0` (the surface/swapchain entry points), and
`Silk.NET.Vulkan.Extensions.EXT` `2.23.0` (the `VK_EXT_debug_utils` messenger and labels). It sets
`AllowUnsafeBlocks` for the opaque native handles and keeps `IsAotCompatible` with the trim and AOT
analyzers. It references no `Bork2d.Runtime`, `Bork2d.Bake`, or `Silk.NET.SDL`; the dependency test
enforces that and the exact package pins. The SDL Vulkan surface seam
(`VulkanLoadLibrary`/`GetVulkanInstanceExtensions`/`CreateVulkanSurface`/`GetVulkanDrawableSize`)
stays in `Bork2d.Runtime/Sdl` and passes only opaque `nint` handles across the Render boundary.

`Pong.Desktop` is the only project that sets `PublishAot`; libraries do not publish
independently, and no runtime project references the bake tooling or its packages. The
`Bork2d.Bake`/`Pong.Assets` tooling assemblies are built and executed out of process by the imported
targets, so they never enter the desktop app's output or publish graph (the game-side `Pong.Common`
assembly is expected there).

`Bork2d.ShaderCompiler` is a **build-only** JIT tool (not a runtime project) that pins
`Silk.NET.Shaderc` and `Silk.NET.Shaderc.Native` `2.23.0` (M8 target, in progress). The MSBuild
shader target builds it, compiles the checked-in GLSL to SPIR-V, and emits generated C# with the
SPIR-V byte arrays into `Bork2d.Render`'s compilation. The tool and its packages are never referenced
by a runtime project and never appear in the AOT publish; no Vulkan SDK or `glslangValidator` build
dependency is required.

## 2. Target dependency graph

The graph is acyclic and flows contracts → implementation → composition. `Runtime` is the
composition root and the only project that knows both the engine and the renderer.

| From \ To | Engine | Render | Runtime | Bake | Pong.Common | Pong.Assets | Pong.Desktop |
| --- | :-: | :-: | :-: | :-: | :-: | :-: | :-: |
| **Engine** | — | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ |
| **Render** | ✓ | — | ✗ | ✗ | ✗ | ✗ | ✗ |
| **Runtime** | ✓ | ✓ | — | ✗ | ✗ | ✗ | ✗ |
| **Bake** | ✓ | ✗ | ✗ | — | ✗ | ✗ | ✗ |
| **Pong.Common** | ✓ | ✗ | ✗ | ✗ | — | ✗ | ✗ |
| **Pong.Assets** | ✗ | ✗ | ✗ | ✓ | ✓ | — | ✗ |
| **Pong.Desktop** | ✓ | ✗ | ✓ | ✗ | ✓ | ✗ | — |
| **Bork2d.Tests** | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ |
| **Pong.Tests** | ✓ | ✗ | ✓ | ✓ | ✓ | ✓ | ✗ |

Rules behind the matrix:

- `Engine` references no Bork2d assembly and only the BCL, `Microsoft.Extensions.Logging.Abstractions`,
  `PolyType`, `Nerdbank.MessagePack`, and `System.IO.Hashing`. The public contract must not expose
  MessagePack or hashing types; the dependency test enforces both the reference set and the public
  signatures.
- `Render` consumes the engine contract and internal snapshot contracts; it must not reference
  `Runtime`, `Bake`, or SDL. It references only `Silk.NET.Vulkan*` for the GPU path. Window/surface
  lifetime is handed to it; offscreen targets are its own. The window/platform side sees only the
  internal renderer seam and opaque instance/surface handles (D-M6-25).
- `Runtime` is the composition root: builder, app host, SDL window/input (windowed), Vulkan
  bootstrap, mode selection, ZLogger configuration, service wiring.
- `Bake` is JIT tooling: it references `Engine` only and never `Runtime`, `Render`, or a game project.
  Q-20 (no engine implementation dependency) is no longer assembly-enforced because the contract and
  implementation share `Bork2d.Engine`; the dependency test enforces it at metadata level: Bake may
  only reference the `Bork2d.Engine`, `.Rendering`, `.Assets`, `.Assets.Pack`, and `.Assets.Vfs`
  namespaces (the last two are the pack-writer and canonical-path seams).
- `Pong.Common` is the game-side AOT-safe library: it references `Bork2d.Engine` only and never
  `Runtime`, `Render`, or `Bake`. It carries the game loop, simulation, settings, and title logic.
- `Pong.Assets` is the build-time executable that owns the source tree, `CircleProcessor`, and the
  `BakeApplicationBuilder` configuration; it references `Bork2d.Bake` and `Pong.Common` and is never
  in the runtime/AOT publish graph.
- `Pong.Desktop` uses `Runtime` for the host and `Pong.Common` for the game (it also sees `Engine`
  transitively through both). It imports `build/Bork2d.Assets.Copy.targets`, which builds
  `Pong.Assets` out of process and copies the baked pack next to the app and the publish. It must not
  pull `Render` types or any bake-tooling/assets-executable assembly in.
- `Bork2d.Tests` may reference the engine/runtime/bake assemblies and use their internals, but no
  Pong project; it asserts the runtime assemblies never reference the bake tooling and that the
  engine dependency graph matches this matrix. `Pong.Tests` references `Pong.Common`, `Pong.Assets`,
  `Bork2d.Engine`, `Bork2d.Bake`, and `Bork2d.Runtime`; it does not reference `Pong.Desktop` or
  `Bork2d.Render` directly.
- `Editor` is independent of the renderer: it edits source data, slices textures, and previews with
  Avalonia imaging; it consumes the public `Bork2d.Bake` surface.

A new arrow requires updating this document and the dependency test; the dependency test fails
otherwise. The M8 build-only `Bork2d.ShaderCompiler` tool is deliberately **not** a row in this
runtime matrix: no runtime or example project references it, it does not ship, and the matrix is
unchanged by M8.

## 3. Public surface and assembly visibility

### Public assemblies

| Assembly | Public types |
| --- | --- |
| `Bork2d.Engine` | `IEngine<TLayer>`, `IGame<TLayer>`, `ITime`, `IWindow`, `IInput`, `IAssets`, `ISceneRoot`, `ITransform`, `ICamera<TLayer>`, `ISpriteRender<TLayer>`, `Color`, `ClearKind`, `KeyCode`, `MouseButton`, `AssetInfo`, `AssetRef<TAsset>`, `DataRef`, `SpriteAsset`, `TextureDescriptor`, `SpriteTopology`, `TextureFormat`, `TextureFilter`, `TextureWrap`, `AssetException`, `AssetErrorKind`, `Bork2dException`, the `ColorMarshaler`/`AssetRefMarshaler<TAsset>`/`DataRefMarshaler` serialization infrastructure, and the tooling-only `Bork2d.Engine.Assets.Pack.PackBuilder`/`PackCompressionMode`. |
| `Bork2d.Runtime` | `Bork2dAppBuilder<TLayer>`, `Bork2dApp`. Nothing else. |
| `Bork2d.Bake` | `BakeApplicationBuilder`, `BakeApplication`, `RegisterAsset<T>()`, `UseDefaultAssets()`, `UseDefaultProcessors()`, `IAssetProcessor`, `AssetProcessor<TSidecar>`, `AssetProcessorContext`, `IAssetOutputSink`, `IAssetProcessorDiagnostics`, `SidecarPolicy`, and the command surface. Tomlyn/Stb/pack internals stay hidden. |
| `Pong.Common` | The game library (`GameLayer`, `GameSettings`, `PongSide`, `PongSimulation`, `PongAi`, `PongTitle`, `PongGame`); a normal game surface, not engine API. |
| `Pong.Assets` | **None.** `Program` and `CircleProcessor` are internal (visible to `Pong.Tests` through `InternalsVisibleTo`). |
| `Pong.Desktop` | **None** (entry point only). |
| `Bork2d.Editor` | `Bork2dEditorBuilder`, `Bork2dEditorApp`, and the public editor service types (`Out of scope`, M5 removed). |

`ILoggerFactory` is a Microsoft.Extensions.Logging type exposed by `IEngine`; it is not a Bork2d
public type. `Bork2d.Engine` takes it from the .NET 11 shared framework.

The approved M8 public additions are `ICamera<TLayer>.WorldToScreen`/`ScreenToWorld`, the public
`Bork2d.Engine.Rendering.RenderStats` record struct, and `IEngine<TLayer>.RenderStats`/
`CaptureFrameAsync` (§6.9 of the [M8 spec](../specs/m8-sprite-renderer.md#69-public-api)). They are
`Planned` and not declared yet; `Bork2d.Render` keeps zero public types.

The pack writer is the one documented exception to the "implementation types stay internal" rule
(D-001, D-M3-04): `PackBuilder`/`PackCompressionMode` exist for pack creation, not gameplay. The
dependency test asserts exactly those two public types from the asset implementation. Game code
consumes assets through `IAssets`; it must not call the pack writer.

### Internal assemblies

`Bork2d.Engine`'s scene/time/asset implementation, `Bork2d.Render`, and `Bork2d.Bake`'s
implementation are implementation details. Types are `internal` by default and exposed only to the
assemblies that need them. `Bork2d.Render` exposes **zero** public types: the Vulkan instance/device
abstraction, the pure policies, and the internal `IRenderer`/factory seam are all internal and
visible to `Bork2d.Runtime` and `Bork2d.Tests` through the existing `InternalsVisibleTo` grants.

| Project | Internals visible to |
| --- | --- |
| `Bork2d.Engine` | `Bork2d.Render`, `Bork2d.Runtime`, `Bork2d.Bake`, `Bork2d.Editor`, `Bork2d.Tests` |
| `Bork2d.Bake` | `Bork2d.Editor`, `Bork2d.Tests` |
| `Bork2d.Render` | `Bork2d.Runtime`, `Bork2d.Tests` |
| `Bork2d.Runtime` | `Bork2d.Tests` |
| `Pong.Assets` | `Pong.Tests` |

The `Bork2d.Engine` → `Bork2d.Bake` grant is intentional: the baker validates canonical asset paths
with the engine's one path grammar. The metadata guard in the dependency test restricts Bake to the
allowed engine namespaces so the grant does not become a general implementation dependency.

Consequences:

- There is **no accidental public API** from the engine implementation: a public type outside the
  documented contract and pack tooling is a bug.
- `Runtime` may reference engine internals directly; it does not create a parallel public model.
- Trimming/AOT compatibility is **not** deferred: `IsAotCompatible` and the trim/AOT analyzers are
  on from M1 (see [NativeAOT](nativeaot.md)).

## 4. Source layout and namespaces

Every project uses one folder per area and a namespace derived from the project and folder path.
Folder names are PascalCase and match the namespace segment exactly; a file in
`Bork2d.Engine/Assets/Loading/AssetService.cs` declares
`namespace Bork2d.Engine.Assets.Loading;`. IDE0130 is an error (D-034).

| Project | Folders (namespace) | Root-namespace files |
| --- | --- | --- |
| `Bork2d.Engine` | `Scene/` → `.Scene`; `Rendering/` → `.Rendering`; `Input/` → `.Input`; `Platform/` → `.Platform`; `Time/` → `.Time`; `Assets/` → `.Assets`, with `Pack/`, `Vfs/`, `Loading/` | `IEngine`, `IGame`, `Bork2dException` |
| `Bork2d.Bake` | `Baking/`, `Commands/`, `Diagnostics/`, `Incremental/`, `Paths/`, `Processors/`, `Sources/`, `Textures/`, `Toml/` | `BakeApplicationBuilder`, `BakeApplication`, the processor/sidecar contracts, `SidecarPolicy` |
| `Bork2d.Runtime` | `Hosting/`, `Input/`, `Sdl/`, `Window/` | `Bork2dApp`, `Bork2dAppBuilder`, `AssemblyReferenceAnchor` |
| `Bork2d.Tests` | `Architecture/`, `Scene/`, `Runtime/`, `Assets/{Pack,Vfs,Service,Sprites}/`, `Bake/{Baking,Builder,Cli,Diagnostics,Incremental,Paths,Processors,Sources,Textures,Toml}/`, `Platform/{Sdl,Window}/`, `Input/`, `TestDoubles/`, `TestSupport/` | none |
| `Pong.Common` (examples) | — (game library) | `GameLayer`, `GameSettings`, `PongSide`, `PongSimulation`, `PongAi`, `PongTitle`, `PongGame` |
| `Pong.Assets` (examples) | `Processors/`; `Assets/` (TOML data/sprites/settings, the paddle PNG, and the `.circle` source) | `Program` (top-level statements) |
| `Pong.Desktop` (examples) | — | `Program`, `PongArguments` |
| `Bork2d.Render` | `Vulkan/` → `.Vulkan` (instance, validation, device selection, pure policies, windowed renderer/factory, swapchain, frames, per-frame instance resources, offscreen target/renderer); `Handoff/` → `.Handoff` (internal three-slot snapshot ring, slot state, counters, snapshot consumer, combined snapshot/control wait); `Diagnostics/` → `.Diagnostics` (renderer status/counters/log category) | `IRenderer`, `IRendererFactory`, `RendererStartInfo`, `RendererMode`, `WindowExtent`, `RendererControlChannel`, `RendererControlMessage`, `AssemblyReferenceAnchor` |

`Bork2d.Engine` keeps `IEngine`, `IGame`, and `Bork2dException` in the root namespace. The rest of
the public contract is grouped by area:

| Namespace | Public types |
| --- | --- |
| `Bork2d.Engine` | `IEngine<TLayer>`, `IGame<TLayer>`, `Bork2dException` |
| `Bork2d.Engine.Scene` | `ISceneRoot`, `ITransform`, `ICamera<TLayer>`, `ISpriteRender<TLayer>` |
| `Bork2d.Engine.Rendering` | `Color`, `ColorMarshaler`, `ClearKind` |
| `Bork2d.Engine.Input` | `IInput`, `KeyCode`, `MouseButton` |
| `Bork2d.Engine.Platform` | `IWindow`, `ITime` |
| `Bork2d.Engine.Assets` | `IAssets`, `AssetInfo`, `AssetException`, `AssetErrorKind`, `AssetRef<TAsset>`, `DataRef`, `SpriteAsset`, `TextureDescriptor`, `SpriteTopology`, `TextureFormat`, `TextureFilter`, `TextureWrap`, `AssetRefMarshaler<TAsset>`, `DataRefMarshaler` |
| `Bork2d.Engine.Assets.Pack` | `PackBuilder`, `PackCompressionMode` (tooling only) |

The tooling pack writer is `Bork2d.Engine.Assets.Pack.PackBuilder`/`PackCompressionMode`; it is not
part of the game contract (D-001, D-M3-04, D-034). A namespace change is a public-contract change:
update this section, the API guide/reference, and the decision log in the same change.

Internal implementation namespaces: `Bork2d.Engine.Time` (frame clock),
`Bork2d.Engine.Assets.Vfs` (pack stack and canonical paths),
`Bork2d.Engine.Assets.Loading` (asset service/loader), `Bork2d.Engine.Assets.Pack` (reader/writer),
the internal scene types inside `Bork2d.Engine.Scene` (`Transform`, `SceneRoot`, `GameEngine`,
`Camera`, `SpriteRender`, `ISceneNode`, `LayerValidation`), the internal snapshot model and capture
in `Bork2d.Engine.Rendering` (`FrameSnapshot`, `CameraSnapshot`, `SpriteSnapshot`,
`SnapshotCapture<TLayer>`), and the internal handoff ring in `Bork2d.Render.Handoff`
(`SnapshotRing`, `SnapshotSlot`, `SnapshotSlotState`, `SnapshotRingCounters`).

## 5. Game solution layout

The Pong example is the current worked instance of the game layout; the M9 template will use the same
shape with generic `Project.Common`/`MyGame.Assets`/`Project.Desktop` names and a game prefix.

```
examples/pong/
  Pong.slnx
  Pong.Common/                  -> Bork2d.Engine; AOT-compatible
    Pong.Common.csproj
    GameLayer.cs                layers
    GameSettings.cs             [GenerateShape] readonly record struct root
    PongSide.cs
    PongSimulation.cs           engine-free deterministic simulation
    PongAi.cs                   right-paddle policy
    PongTitle.cs                score/prompt formatting
    PongGame.cs                 IGame implementation: assets, camera, entities, input, title, logs
  Pong.Assets/                  -> Bork2d.Bake, Pong.Common; Exe; JIT; build-time only
    Pong.Assets.csproj          imports build/Bork2d.Assets.targets
    Assets/                     main pack root: art/paddle.png, art/paddle.png.toml,
                                textures/ball.circle, sprites/{paddle,ball}.toml,
                                game_settings.toml
    Processors/CircleProcessor.cs
    Program.cs                  BakeApplicationBuilder config + CLI entry point
  Pong.Desktop/                 -> Bork2d.Runtime, Pong.Common; AOT
    Pong.Desktop.csproj         build-only Pong.Assets via Bork2d.Assets.Copy.targets
    Program.cs                  builder startup + headless/windowed/offscreen acceptance
    PongArguments.cs
  Pong.Tests/                   -> Pong.Common, Pong.Assets, Bork2d.Engine/Bake/Runtime; xUnit
    Pong.Tests.csproj
```

There is no `bork2d.project.toml` (D-037). `Pong.Assets` configures the single `assets.pck` pack, its
root, texture defaults, `CircleProcessor`, and the explicit `RegisterAsset<GameSettings>()` type in
`Program.cs`. Building it runs its imported target, invokes the executable's `bake` command, and
writes the pack. `Pong.Desktop` takes **no** project reference to `Pong.Assets`; it imports the thin
copy target and sets `Bork2dAssetsProjectFile`, so the AOT app never references the builder or
processor assemblies in its compile or publish graph.

A `dotnet run` still resolves `WithAssetsPack("assets.pck")` against the process current directory
(Q-10); Pong passes an absolute `AppContext.BaseDirectory` path. The M9 template owns the final
ergonomics.

| Project | May reference | Must not reference |
| --- | --- | --- |
| `Pong.Common` | `Bork2d.Engine` | `Bork2d.Runtime`, `Bork2d.Render`, `Bork2d.Bake` |
| `Pong.Assets` | `Bork2d.Bake`, `Pong.Common` | `Bork2d.Runtime`, `Bork2d.Render` |
| `Pong.Desktop` | `Bork2d.Runtime`, `Pong.Common`; build-only `Pong.Assets` (out of process) | `Bork2d.Render`, `Bork2d.Bake` (compile/publish) |
| `Project.Editor` (out of scope) | `Bork2d.Editor`, `Project.Common`; `Bork2d.Bake` | `Bork2d.Render` |

A developer's **runtime** code writes against two engine assemblies: `Bork2d.Engine` in
`Pong.Common` and `Bork2d.Runtime` in `Pong.Desktop`. The build-time `Pong.Assets` project adds
`Bork2d.Bake`; it is JIT, build-only, and never shipped. A data type may live in an assembly that
`Pong.Common` references; resolution is the builder's explicit `RegisterAsset<T>()` dictionary keyed
by full type name; there is no reflection or common-assembly lookup. The builder and processor model
are owned by [asset processors](asset-processors.md#3-project-model).

## 6. Build, packaging, and versioning

- `Directory.Build.props` sets `net11.0`, nullable, implicit usings, warnings-as-errors,
  `EnforceCodeStyleInBuild`, deterministic builds, and .NET analyzers for every project.
- The SDK is `11.0.100-rc.1`, pinned by `global.json` with `rollForward: latestPatch` and
  `allowPrerelease: true`.
- `Engine`, `Render`, `Runtime`, and the game-side `Pong.Common` set `IsAotCompatible`,
  `EnableTrimAnalyzer`, and `EnableAotAnalyzer`; they do not set `PublishAot`. `Render` additionally
  sets `AllowUnsafeBlocks` for the Vulkan handles. `Pong.Desktop` sets `PublishAot`,
  `InvariantGlobalization`, and the narrow `IL3000;IL3002` Silk.NET suppression. `Bork2d.Bake` and
  `Pong.Assets` are JIT tooling and set neither AOT-compatibility nor `PublishAot`.
- Build: `dotnet build src/Bork2d/Bork2d.slnx`. The Pong example solution is
  `dotnet build examples/pong/Pong.slnx`; it is not built by CI (the local AOT gate exercises
  `Pong.Desktop`). A `Pong.slnx` Release build still builds the external engine project references in
  Debug, because the `.slnx` has no configuration mapping for projects outside `examples/pong`; the
  direct Release publish below is the Release proof.
- **M8 shader build (approved target, in progress):** an MSBuild target builds the build-only
  `Bork2d.ShaderCompiler` tool and compiles the checked-in GLSL to SPIR-V, emitting generated C#
  with the byte arrays into `Bork2d.Render`'s compilation (Vulkan 1.3 target; debug info in Debug,
  optimized in Release). It requires no Vulkan SDK, `glslangValidator`, or `glslc`, and there is no
  runtime shader I/O or compiler. The generated code is linked into the executable and the publish
  contains no shaderc assemblies or loose `.spv` files.
- Local AOT gate:
  `dotnet publish examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Release -r win-x64`, then run
  the published exe from its publish directory: `--headless` (120 ticks by default; mounts the baked
  7-entry pack, asserts the settings/sprite/descriptor/raw chain, exactly `N` completed ticks, and
  the detached root, exits 0), `--headless --ticks N` (exactly `N` ticks),
  `--windowed --ticks 120` (exit 0 after exactly 120 ticks; creates the window/instance/surface and
  presents a black clear per consumed snapshot), and `--offscreen --ticks 120` (exit 0 after exactly
  120 ticks through the offscreen renderer; frame count not asserted). The publish must contain
  `Pong.Desktop.exe`,
  `assets.pck`, `SDL2.dll`, and the `Pong.Common`/engine PDBs (NativeAOT single-file links the managed
  assemblies; `Pong.Common.pdb` proves the game assembly is in the graph), must not contain
  `vulkan-1.dll` (the system loader), and must not contain a `Pong.Assets`, `Bork2d.Bake`, Tomlyn, or
  StbImageSharp assembly.
- CI restores, verifies formatting, builds Debug, and runs Debug tests on pull requests to
  `develop` and on pushes to `develop`. CI does not build the Pong solution.
- Distribution starts as source/project references; NuGet packaging is M9.
- Versioning: pre-1.0 assemblies may break. A public API change updates the API guide, reference,
  and decision log in the same change.

## 7. Related documents

- [Architecture overview](overview.md) — invariants and context.
- [Runtime lifecycle](runtime-lifecycle.md) — modes, builder ownership, startup.
- [Assets and tooling](assets-and-tooling.md) — pack VFS, editor, and `Project.Editor`.
- [Asset processors and `MyGame.Assets`](asset-processors.md) — builder/processor model and layout.
- [Pong example](../specs/pong-example.md) — the living game example and its as-built evidence.
- [NativeAOT](nativeaot.md) — runtime build constraints.
- [Project consolidation](../specs/refactor-projects.md) — the earlier refactor's scope and as-built
  record.
- [Roadmap](../roadmap.md) — milestone sequence and progress.
