# NativeAOT

> **Status:** `Current` through M4, the merged asset-processor task (D-036/D-037/D-038, 2026-10-27,
> PR #8), the Pong example (D-043/D-044; merged 2026-10-29, PR #10), M6 (Vulkan bring-up;
> merged 2026-10-31, PR #11), and M7 (frame handoff and pacing; implemented on `m-7/common`, the
> ready-for-review PR is open/awaiting human review and is not merged).
> The project-consolidation refactor (D-041/D-042, merged 2026-10-28, PR #9) merged Api/Common/Assets
> into `Bork2d.Engine` and Bake.Builder into `Bork2d.Bake`; `Bork2d.Engine`, `Bork2d.Render`,
> `Bork2d.Runtime`, and `Pong.Common` compile with `IsAotCompatible` plus the trim and AOT analyzers,
> and `Pong.Desktop` publishes and runs with zero AOT/trim warnings. The default headless run loads
> the **baked Pong pack** produced by the build-only `Pong.Assets` executable: the
> `Pong.Common.GameSettings` struct root with its `AssetRef<SpriteAsset>` links, the paddle and ball
> sprites, the paddle PNG and `CircleProcessor` ball descriptors with their exact raw payloads, and
> the 7-entry browse; it runs **120 headless ticks by default** (`--headless --ticks N` overrides)
> and exits 0. The windowed `--windowed [--ticks N]` run and the offscreen `--offscreen --ticks N`
> run use the same pack, run the real Vulkan renderer, and keep the default logging path. All three
> are the AOT gate. The editor and build tools stay
> JIT; M6 pins the Vulkan bindings and wires the windowed/offscreen renderer into the Runtime host,
> so the publish graph reaches the Vulkan bindings while the headless path never loads Vulkan.
>
> The build-time `MyGame.Assets`/`Pong.Assets` executables, `Bork2d.Bake`, and processors are JIT and
> never enter the runtime/AOT graph. Runtime data types stay in `Project.Common`/`Pong.Common`. The
> Pong desktop app builds the assets executable out of process through
> `build/Bork2d.Assets.Copy.targets`; see [asset processors](asset-processors.md).

## 1. Scope

| Component | NativeAOT required? | Why |
| --- | --- | --- |
| `Bork2d.Engine` | Yes | Public contract plus the runtime scene, transforms, time, snapshot policy, generated shapes, pack parsing, decompression, MessagePack decoding, and pack-time reference validation. |
| `Bork2d.Render` | Yes | Vulkan renderer and GPU uploads. M6 adds the instance/validation/device-selection foundation, the windowed swapchain/clear/present renderer, and the offscreen target/renderer with the Runtime host wiring. Sprites/uploads are M8. The renderer is never constructed on the headless path. |
| `Bork2d.Runtime` | Yes | Builder, host, and app-owned logging. |
| `Pong.Desktop` | Yes | The AOT desktop example and the local gate, published with `PublishAot`. It references `Bork2d.Engine`/`Bork2d.Runtime` plus the game-side `Pong.Common` and loads the baked 7-entry Pong pack for `--headless` (120 ticks by default; `--headless --ticks N` overrides) or runs the same pack windowed (`--windowed [--ticks N]`) or offscreen (`--offscreen --ticks N`). |
| `Pong.Common` | Yes | Game-side runtime library: `GameSettings` (a `[GenerateShape]` struct root with sprite references), `PongGame`, `PongSimulation`, `PongAi`, and `PongTitle`; it references `Bork2d.Engine` only. |
| `Bork2d.Editor` | No (JIT) | Avalonia desktop tool; may reflect over the game model. M5. |
| Asset baker / pack tool (`Bork2d.Bake`) | No (JIT) | Build-time tools; use explicit `RegisterAsset<T>()` registrations and TOML. No generated host, no reflection, and no `Bork2d.Bake.Cli` or `generate-host`. |
| `MyGame.Assets` / `Pong.Assets` | No (JIT) | Game/build-time executables that own source roots, processors, and builder configuration; they bake packs when built and are never referenced by the runtime/AOT publish. Runtime data types remain in the AOT-safe `Pong.Common`. |
| Tests | Mixed | Unit tests run JIT; `Pong.Desktop` is the one AOT publish/run gate. |

## 2. Build settings

```xml
<!-- Engine, Render, Runtime, Pong.Common (libraries) -->
<PropertyGroup>
  <IsAotCompatible>true</IsAotCompatible>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <EnableAotAnalyzer>true</EnableAotAnalyzer>
</PropertyGroup>

<!-- Pong.Desktop (executable) -->
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
  <!-- Narrow Silk.NET resolver suppression; see §6. -->
  <NoWarn>$(NoWarn);IL3000;IL3002</NoWarn>
</PropertyGroup>
```

- `IsAotCompatible`/`EnableAotAnalyzer` catch reflection and dynamic-code warnings during normal
  builds, not only at publish. Warnings-as-errors makes every AOT/trim diagnostic an error; a
  narrow suppression needs a recorded justification.
- `PublishAot` is set only on `Pong.Desktop`; libraries do not publish independently.
- `InvariantGlobalization=true` is set for the AOT gate (human decision); the template chooses its
  globalization mode in M9.
- AOT publish is a **required local gate**, not a CI job (human decision 2026-10-25); see §7.

## 3. Runtime constraints

The runtime must not depend on:

- `Activator.CreateInstance`, `Type.GetType`, `Assembly.Load`, `AssemblyLoadContext`, or
  assembly scanning;
- `Reflection.Emit`, expression compilation, or dynamically generated types;
- runtime TOML parsing or reflection-based serializers;
- convention-based discovery of game types, assets, or editor registrations;
- `MakeGenericType`/`MethodInfo.Invoke` on unknown types;
- unregistered Nerdbank.MessagePack shapes;
- `dynamic` or runtime code generation in hot paths.

It may use:

- static generics and `typeof(T)` metadata visible to the AOT compiler;
- source-generated Nerdbank.MessagePack shapes, including `PackHeader`/`PackEntry` (M3);
- explicit registrations: `WithGame(instance)` now, editor `WithAssetType<T>()`/
  `WithWindow<T>()` in M5;
- bounded validation around generated pack header/entry shapes and known asset kinds.

The game is a caller-constructed instance: `WithGame(instance)` has no `new()` constraint and no
reflection-based construction, which also allows constructor injection. See the
[API guide](../api/guide.md).

## 4. Serialization and assets

- Runtime data assets are MessagePack (M3/M4), decoded by
  [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) **source-generated
  shapes**; no reflection resolver is configured.
- `Bork2d.Engine` pins [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack)
  `1.4.95` for generated shapes, plus `PolyType` `1.4.1` and `System.IO.Hashing`
  `11.0.0-rc.1.26425.128`. The full pin list is in [Projects §1](projects.md#1-current-projects).
  `Pong.Common` owns the `[GenerateShape]` `GameSettings` struct root; the tests use a tests-local
  equivalent. The v1 pack
  header/entry shapes and the public `TextureDescriptor`/`SpriteAsset` shapes are `Current`.
  Descriptors and sprites use PascalCase property-name keys (not `[Key]`), an integer
  `SpriteTopology`, no per-asset version, and Nerdbank's generated `System.Numerics.Vector2` shape
  (a map with `X`/`Y`); there is no `vertices` field. `AssetRef<T>`/`DataRef` use public PolyType
  path marshalers so a consumer assembly can generate a closed shape for a reference property; the
  marshalers are infrastructure types, not game-facing API.
- `LoadAsset<T>` is declared as `Task<T> LoadAsset<T>(string assetPath) where T : IShapeable<T>`
  (D-M3-22, relaxed from `class` by D-M4-11). `T` must carry a compiled generated shape (for example
  `[GenerateShape]` on the root type), so a missing shape is a **compile-time** error. There is no
  unconstrained call, reflection-shape fallback, runtime "missing shape" fault, or `NBMsgPack051`
  suppression. A struct root uses the same path; a MessagePack `nil` root is a `Decode` fault, never
  a silent default. Pack header/entry classes, texture descriptors, sprite assets, and custom game
  data all use generated shapes; TOML exists only in the build-time asset/bake projects, never in
  the runtime.
- The loader body deserializes with the shape from `T.GetTypeShape()` passed explicitly to the
  serializer, so the reflection-shape provider is never consulted. It performs file I/O, Zstd
  decompression, exact-size checking, and generated-shape deserialization; it does not validate
  fields, follow references, or create CPU/GPU resources. `PackBuilder` validates references at pack
  build with a generated-shape traversal, not reflection; the bake also validates binding and
  references with source spans before packing. The default `Pong.Desktop` headless run loads the
  baked 7-entry Pong pack (the `GameSettings` struct root with its two `AssetRef<SpriteAsset>` links,
  the paddle and ball sprites, the `art/paddle` and `CircleProcessor` `textures/ball` descriptors,
  and their exact raw payloads) as the runtime proof.
- `Bork2d.Engine` references PolyType `1.4.1` for the `IShapeable<T>` shape contract and the
  `AssetRef`/`DataRef` path marshalers (D-035). Because the contract and the pack implementation
  share one assembly, Engine also references Nerdbank.MessagePack and `System.IO.Hashing`; the public
  signatures must not expose them (the dependency test enforces this). `Bork2d.Engine.Assets.Pack`
  exposes only the tooling surface (`PackBuilder`, `PackCompressionMode`) publicly.
- Zstd is the .NET 11 built-in codec; the pack header checksum uses the `System.IO.Hashing`
  package's `XxHash3`.
- Asset paths are data, not type names; loading never resolves a type from a string.

See [Assets and tooling](assets-and-tooling.md).

## 5. Editor and tooling boundary

The bake tooling is JIT and **build-time only**. `Bork2d.Bake` contains both the internal
implementation (Tomlyn parsing, StbImageSharp decoding, processors) and the public JIT
builder/processor surface, and
the game `MyGame.Assets` / `Pong.Assets` executables host the builder,
explicit `RegisterAsset<T>()` entries, and processor code (JIT). They may use
`System.Text.Json`/image decoders and reference `Project.Common` types, but they never enter the
runtime publish graph. There is no generated host, no `MetadataLoadContext`, no host ALC, and no
reflection over `Project.Common`.
`Bork2d.Editor` (M5) may use Avalonia, reflection, TOML writing, and a collectible load context for
editor reload. None of these dependencies may leak into `Bork2d.Engine`, `Bork2d.Render`,
`Bork2d.Runtime`, `Pong.Common`, or the published AOT app;
the runtime projects compile with AOT analyzers, and the dependency test proves the absence of
Bake/Builder/Tomlyn/StbImageSharp and the assets executables. The runtime never parses TOML and
never reflects over game members.

Editor registrations are explicit (`WithProject`, `WithAssetType<T>`, `WithWindow<T>`, M5) for
determinism and a possible future AOT editor, not a current AOT requirement.

## 6. Native libraries and Silk.NET

- SDL2 and the Vulkan loader are native dependencies, deployed per target RID.
- `Bork2d.Runtime` pins `Silk.NET.SDL` `2.23.0` for the windowed platform (M2, D-M2-06). No other
  engine project references it, no SDL type appears in `Bork2d.Engine`, and Vulkan validation remains
  M6.
- The package brings `Ultz.Native.SDL` `2.32.10` transitively, which supplies
  `runtimes/win-x64/native/SDL2.dll`; the AOT publish output copies it next to the executable. The
  loaded binary reports `SDL_GetVersion` `2.30.8` (its Windows file version is also `2.30.8`), so
  the package version label is ahead of the binary it carries. It exposes every call M2 uses,
  including `SDL_GetWindowSizeInPixels` (2.26+) and the Windows DPI hints (2.24+).
- The SDL adapter and the Vulkan instance/device entry points use Silk.NET's default
  native-library resolver (`Sdl.GetApi()` and `Vk.GetApi()`). That resolver probes
  `Assembly.Location`, `Assembly.CodeBase`, and `DependencyContext` for candidate paths, which ILC
  reports as `IL3000`/`IL3002` single-file trim warnings under NativeAOT. `Pong.Desktop` — the AOT
  publisher and the host of the windowed/offscreen examples — suppresses exactly those two codes
  with a project-level `NoWarn`. No other warning code is suppressed and
  `SuppressTrimAnalysisWarnings` is not used. The suppression is justified for both native
  dependencies: `SDL2.dll` ships from the package and `vulkan-1.dll` is the system loader, so both
  are resolved by the runtime's normal native-asset lookup and the flagged fallback probes are not
  used. A diagnostic publish with `NoWarn` cleared reports only those two codes. Remove the
  suppression when Silk.NET stops probing those APIs.
- M2 verified the dependency with an AOT publish/run of a throwaway windowed probe that reaches
  the adapter through the default resolver: with the suppression applied the publish reports no
  warnings (the same graph reports six `IL3000`/`IL3002` warnings when `NoWarn` is cleared); SDL
  video init, window create/destroy, and quit succeed on an engine-created (non-main) thread; and
  `SDL2.dll` is present in the output. M2 verified the real windowed runtime and its AOT demo
  (`--windowed --ticks 120`, and a 5,000,000-tick live-window run) with the same zero-warning
  publish.
- `Bork2d.Render` pins `Silk.NET.Vulkan` `2.23.0`, `Silk.NET.Vulkan.Extensions.KHR` `2.23.0`, and
  `Silk.NET.Vulkan.Extensions.EXT` `2.23.0` (the debug-utils messenger/labels). Vulkan is a system
  dependency: `vulkan-1.dll` comes from the driver/loader, is never copied by publish, and is never
  needed by the headless AOT gate. `Bork2d.Render` exposes no public types. M6 wires the
  production windowed/offscreen factory into `Bork2d.Runtime`, so the `Pong.Desktop` compile and
  publish graph reaches the Silk.NET Vulkan bindings even though the headless run never loads the
  loader. The M7 gate proves the zero-warning publish and the published `--headless` (120
  ticks by default), `--headless --ticks N`, `--windowed --ticks 120`, and `--offscreen --ticks
  120` runs; the existing two-code suppression
  covers the shared resolver and no custom resolver or broader suppression was needed.
- No runtime shader compilation: SPIR-V is built offline.
- The headless server must not initialize SDL or Vulkan; `WithHeadless(...)` must not load or
  call the graphics/windowing paths.

These are validation items, not assumptions. If a Silk.NET path uses reflection or dynamic code
incompatibly with AOT, isolate it behind an adapter and record the workaround.

## 7. Testing and CI

1. `dotnet build` with AOT/trim analyzers and warnings-as-errors, Debug and Release.
2. `dotnet publish examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Release -r win-x64`
   with zero AOT/trim warnings; `SDL2.dll` is copied next to the executable.
3. `Pong.Desktop`'s build bakes the pack. It imports `build/Bork2d.Assets.Copy.targets` and sets
   `Bork2dAssetsProjectFile`, so the copy target builds `Pong.Assets` out of process; that executable
   owns the source tree, `GameSettings` registration, `CircleProcessor`, and builder configuration,
   bakes `assets.pck` (7 entries), and the copy target places it next to the desktop output/publish.
   There is no generated host and no host build step. The publish output must contain no
   `Pong.Assets`, `Bork2d.Bake`, Tomlyn, or StbImageSharp assembly (`Pong.Common` is the expected
   game-side assembly), must contain `assets.pck` and `SDL2.dll`, and must not contain
   `vulkan-1.dll` (the system loader). The NativeAOT output is a single-file executable plus PDBs,
   so the managed assemblies are linked in; `Pong.Common.pdb` is the remaining on-disk proof that
   the game assembly was in the publish graph.
4. Run the AOT-published headless example locally (no GPU): default logging, one `Setup`, **120
   `Tick`s by default** (`--headless --ticks N` runs exactly `N`), one `Teardown`, the **baked
   7-entry pack** (the `GameSettings` struct root with its
   `AssetRef<SpriteAsset>` links, the `sprites/paddle` and `sprites/ball` sprites, the `art/paddle`
   descriptor and exact raw payload, the `CircleProcessor` `textures/ball` descriptor and exact raw
   payload, the red/blue tints), detached root, exit `0`. The success line reports the tick count
   and `tickLimit`.
5. Run the AOT-published windowed and offscreen examples locally on a desktop session with a
   Vulkan 1.3 device: `Pong.Desktop.exe --windowed --ticks 120` must exit `0` after exactly 120
   ticks with one `Setup` and one `Teardown`, loading the same pack, consuming one black-clear
   frame per snapshot (the short run consumes the first and supersedes the rest), and joining the
   renderer; `Pong.Desktop.exe --offscreen --ticks 120` must exit `0` after exactly 120 ticks with
   one `Setup`/`Teardown`, a 1280×720 sRGB offscreen target, and a joined renderer (frame count not
   asserted, D-M6-13/Q22=V1). The interactive `--windowed` run observes the visible black-cleared
   window, reads W/S/Space/R/Escape, and exits on Escape/close (sprites arrive in M8;
   the title/logs carry the score and state).
6. The **three-mode AOT gate** runs the AOT-published app on a GPU machine: `--headless` (120 ticks
   by default, or `--headless --ticks N`) stays GPU-free, `--windowed --ticks 120` creates the SDL
   window/instance/surface and presents a black clear per consumed snapshot, and `--offscreen
   --ticks 120` runs the offscreen renderer until `Exit` (D-M6-13). All four runs exit 0; the
   publish is zero-warning; and the published app runs with the narrow two-code Silk.NET
   suppression.
7. `Pong.Desktop` loads the baked pack produced by the same tooling a game uses; the constrained
   `IShapeable<T>` contract makes a missing shape a compile-time error. `GameSettings` is a
   `readonly record struct` root with `AssetRef<SpriteAsset>` members, so it proves the D-M4-11
   value-type load path under ILC.
8. The AOT app logs through the default ZLogger/Microsoft.Extensions.Logging path without trim
   warnings; `IEngine.LoggerFactory` is usable.

CI scope is exactly: pull requests targeting `develop` and pushes to `develop`; it restores,
verifies formatting, builds Debug, and runs Debug tests. It does not build
`examples/pong/Pong.slnx`, so build that locally when the example changes.
There is **no AOT job** in CI, so the publish/run gate above is mandatory locally.

## 8. Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Silk.NET or a transitive dependency uses reflection/dynamic code | AOT publish or runtime failure | Validate in M2/M6; isolate behind adapters; pin versions. |
| MessagePack source generation misses a custom type | Runtime load failure | Build-time validation and registration checks; tests. |
| TOML/reflection leaks into runtime | AOT warnings or failure | Project boundary, analyzers, TOML only in baker/editor. |
| Native SDL2/Vulkan deployment | `DllNotFound` at runtime | RID-specific packaging and clean-machine tests. |
| Headless pulls in SDL/Vulkan initialization | Unwanted window/GPU dependency | `WithHeadless` never initializes graphics. |

## 9. Related documents

- [Projects and dependencies](projects.md#6-build-packaging-and-versioning) — project graph and
  build settings.
- [Assets and tooling](assets-and-tooling.md) — pack format, descriptors, sprites, bake.
- [Logging](logging.md) — ZLogger/MEL AOT notes.
- [Decision log](../decisions.md) — AOT and serialization decisions.
- [Roadmap](../roadmap.md) — M1 baseline and status.
