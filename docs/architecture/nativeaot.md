# NativeAOT

> **Status:** `Current` through M3 (merged into `develop` 2026-10-26, PR #4) and the post-M3
> asset-reference refactor (D-035, merged PR #6); **M4 bake is merged into `develop`
> (2026-10-27, PR #7), including D-M4-13 (plus the committed fixture source PNG) and the
> D-M4-14/D-M4-15 multi-pack revision**. `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render`,
> and `Bork2d.Runtime` compile with `IsAotCompatible` plus the trim and AOT analyzers, and
> `Bork2d.AotSmoke` publishes and runs with zero AOT/trim warnings. The default smoke loads the
> **baked fixture pack** (produced by the imported MSBuild bake targets from `Bork2d.BakeFixture`),
> asserting the class-root custom data, the struct root through `AssetRef<StatsRoot>`, the public
> `SpriteAsset`/`TextureDescriptor` shapes, and the exact raw payload, then browses/exists, runs one
> headless tick, and exits 0. The windowed `--windowed [--ticks N]` SDL demo uses `WithoutAssets()`.
> The smoke also keeps the default logging path, the M1 scene checks, and a MessagePack round trip.
> The editor and build tools stay JIT; Vulkan AOT validation is M6.
>
> **Post-M4 target (D-036/D-037/D-038, Planned on `task/file-processors`).** The build-time
> `MyGame.Assets`/`Bork2d.BakeFixture.Assets` executables, `Bork2d.Bake.Builder`, and processors are
> JIT and never enter the runtime/AOT graph. Runtime data types stay in `Project.Common`/
> `Bork2d.BakeFixture`. The fixture assets executable replaces the M4 MSBuild bake target flow; the
> AOT smoke takes a build-only dependency on it. See [asset processors](asset-processors.md).

## 1. Scope

| Component | NativeAOT required? | Why |
| --- | --- | --- |
| `Bork2d.Api` | Yes | Referenced by the AOT game; must avoid dynamic/reflection traps. |
| `Bork2d.Common` | Yes | Runtime scene, transforms, time, snapshot policy, generated shapes. |
| `Bork2d.Assets` | Yes | Pack parsing, decompression, MessagePack decoding, and pack-time reference validation. |
| `Bork2d.Render` | Yes | Vulkan renderer and GPU uploads. |
| `Bork2d.Runtime` | Yes | Builder, host, and app-owned logging. |
| `Bork2d.AotSmoke` | Yes | One-tick smoke executable published with `PublishAot`; the local gate. It references `Api`/`Common`/`Runtime` plus the game-side `BakeFixture` and loads the baked fixture pack (D-M4-09/Q-22). The game template's `Project.Desktop` takes over this role in M9. |
| `Bork2d.BakeFixture` | Yes | Game-side `Project.Common`-like fixture data types (including the target `Unit` struct) baked and loaded under AOT; it references `Bork2d.Api` only. |
| `Bork2d.Editor` | No (JIT) | Avalonia desktop tool; may reflect over the game model. M5. |
| Asset baker / pack tool (`Bork2d.Bake`, `Bork2d.Bake.Builder`, `Bork2d.Bake.Cli`) | No (JIT) | Build-time tools; use explicit `RegisterAsset<T>()` registrations and TOML. No generated host and no reflection on the target path. M4/D-037/D-038. |
| `MyGame.Assets` / `Bork2d.BakeFixture.Assets` | No (JIT) | Game/fixture build-time executables that own source roots, processors, and builder configuration; they bake packs when built and are never referenced by the runtime/AOT publish. Fixture data types remain in the AOT-safe `Bork2d.BakeFixture`. |
| Tests | Mixed | Unit tests run JIT; `Bork2d.AotSmoke` is the one AOT publish/run smoke. |

## 2. Build settings

```xml
<!-- Api, Common, Assets, Render, Runtime (libraries) -->
<PropertyGroup>
  <IsAotCompatible>true</IsAotCompatible>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <EnableAotAnalyzer>true</EnableAotAnalyzer>
</PropertyGroup>

<!-- Bork2d.AotSmoke (executable) -->
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
- `PublishAot` is set only on `Bork2d.AotSmoke`; libraries do not publish independently.
- `InvariantGlobalization=true` is set for the M1 smoke (human decision); the template chooses its
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
- M1 pins [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) `1.4.95` in
  `Bork2d.Common`; `Bork2d.Assets` adds `PolyType` `1.4.1` and `System.IO.Hashing`
  `11.0.0-rc.1.26425.128`. The full pin list is in [Projects §1](projects.md#1-current-projects).
  An internal `[GenerateShape]` smoke type and round-trip helper prove the generated-shape path;
  `Bork2d.AotSmoke` sees them through the intentional `InternalsVisibleTo`. The v1 pack
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
  data all use generated shapes; TOML exists only in `Project.Common` and the JIT baker/editor.
- The loader body deserializes with the shape from `T.GetTypeShape()` passed explicitly to the
  serializer, so the reflection-shape provider is never consulted. It performs file I/O, Zstd
  decompression, exact-size checking, and generated-shape deserialization; it does not validate
  fields, follow references, or create CPU/GPU resources. `PackBuilder` validates references at pack
  build with a generated-shape traversal, not reflection; the M4 baker also validates binding and
  references with source spans before packing. The default AOT smoke loads the baked fixture pack
  (five entries: a class root with a nested struct/`Color`/texture reference, a `StatsRoot` struct
  root, a sprite, a descriptor, and the raw payload) as the runtime proof.
- `Bork2d.Api` references PolyType `1.4.1` for the `IShapeable<T>` shape contract and the
  `AssetRef`/`DataRef` path marshalers (D-035). It stays free
  of Nerdbank.MessagePack, Zstd/`System.IO.Compression`, `System.IO.Hashing`, and file I/O, so the
  public contract does not pull the pack implementation into game code. `Bork2d.Assets.Pack`
  exposes only the tooling surface (`PackBuilder`, `PackCompressionMode`) publicly and keeps its
  PolyType/Nerdbank/Hashing pins for the pack and tooling.
- Zstd is the .NET 11 built-in codec; the pack header checksum uses the `System.IO.Hashing`
  package's `XxHash3`.
- Asset paths are data, not type names; loading never resolves a type from a string.

See [Assets and tooling](assets-and-tooling.md).

## 5. Editor and tooling boundary

The M4 bake tooling is JIT and **build-time only**. `Bork2d.Bake` may parse TOML with Tomlyn and
decode images with StbImageSharp; `Bork2d.Bake.Cli` is a JIT console. **D-036/D-037/D-038 target:**
the public `Bork2d.Bake.Builder` and the game/fixture `MyGame.Assets` /
`Bork2d.BakeFixture.Assets` executables host the builder, explicit `RegisterAsset<T>()` entries,
processor contract, and processor code (JIT). They may use `System.Text.Json`/image decoders and
reference `Project.Common` types, but they never enter the runtime publish graph. There is no
generated host, no `MetadataLoadContext`, no host ALC, and no reflection over `Project.Common`.
`Bork2d.Editor` (M5) may use Avalonia, reflection, TOML writing, and a collectible load context for
editor reload. None of these dependencies may leak into `Bork2d.Api`, `Bork2d.Common`,
`Bork2d.Assets`, `Bork2d.Render`, `Bork2d.Runtime`, `Bork2d.BakeFixture`, or the published AOT app;
the runtime projects compile with AOT analyzers, and the dependency test proves the absence of
Bake/Builder/Tomlyn/StbImageSharp and the assets executables. The runtime never parses TOML and
never reflects over game members.

Editor registrations are explicit (`WithProject`, `WithAssetType<T>`, `WithWindow<T>`, M5) for
determinism and a possible future AOT editor, not a current AOT requirement.

## 6. Native libraries and Silk.NET

- SDL2 and the Vulkan loader are native dependencies, deployed per target RID.
- `Bork2d.Runtime` pins `Silk.NET.SDL` `2.23.0` for the windowed platform (M2, D-M2-06). No other
  engine project references it, no SDL type appears in `Bork2d.Api`, and Vulkan validation remains
  M6.
- The package brings `Ultz.Native.SDL` `2.32.10` transitively, which supplies
  `runtimes/win-x64/native/SDL2.dll`; the AOT publish output copies it next to the executable. The
  loaded binary reports `SDL_GetVersion` `2.30.8` (its Windows file version is also `2.30.8`), so
  the package version label is ahead of the binary it carries. It exposes every call M2 uses,
  including `SDL_GetWindowSizeInPixels` (2.26+) and the Windows DPI hints (2.24+).
- The SDL adapter uses `Sdl.GetApi()`, Silk.NET's default native-library resolver. That resolver
  probes `Assembly.Location`, `Assembly.CodeBase`, and `DependencyContext` for candidate paths,
  which ILC reports as `IL3000`/`IL3002` single-file trim warnings under NativeAOT. `Bork2d.AotSmoke`
  — the AOT publisher, and the host of the `--windowed` demo — suppresses exactly those two
  codes with a project-level `NoWarn`. No other warning code is suppressed and
  `SuppressTrimAnalysisWarnings` is not used. The suppression is justified: the SDL2 native ships
  from the package and is resolved by the runtime's normal native-asset lookup, so the flagged
  fallback probes are not used. Remove it when Silk.NET stops probing those APIs.
- M2 verified the dependency with an AOT publish/run of a throwaway windowed probe that reaches
  the adapter through the default resolver: with the suppression applied the publish reports no
  warnings (the same graph reports six `IL3000`/`IL3002` warnings when `NoWarn` is cleared); SDL
  video init, window create/destroy, and quit succeed on an engine-created (non-main) thread; and
  `SDL2.dll` is present in the output. M2 verified the real windowed runtime and its AOT demo
  (`--windowed --ticks 120`, and a 5,000,000-tick live-window run) with the same zero-warning
  publish.
- No runtime shader compilation: SPIR-V is built offline.
- The headless server must not initialize SDL or Vulkan; `WithHeadless(...)` must not load or
  call the graphics/windowing paths.

These are validation items, not assumptions. If a Silk.NET path uses reflection or dynamic code
incompatibly with AOT, isolate it behind an adapter and record the workaround.

## 7. Testing and CI

1. `dotnet build` with AOT/trim analyzers and warnings-as-errors, Debug and Release.
2. `dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64`
   with zero AOT/trim warnings; `SDL2.dll` is copied next to the executable.
3. The AOT smoke's build bakes the fixture packs. **Target (D-037/D-038):** it takes a build-only
   dependency on `Bork2d.BakeFixture.Assets`, which owns the fixture source roots, explicit
   `RegisterAsset<T>()` types, and builder configuration; building that executable bakes
   `assets.pck`/`dlc1.pck` and the smoke copies them next to its output/publish. Current M4 runs the
   bake targets (`build/Bork2d.Bake.targets`): it builds the CLI
   out of process, copies the fixture project's committed `Assets/**` and `DLC1/**` trees (including
   the fixture **source asset** `Assets/art/player.png`; no texture generator script runs), and
   bakes the prepared fixture packs through the M4 host. The target has no host. The publish output
   must contain no `Bork2d.Bake`, `Bork2d.Bake.Builder`, `Bork2d.BakeFixture.Assets`,
   `Bork2d.Bake.Cli`, Tomlyn, or StbImageSharp assembly (`Bork2d.BakeFixture` is the expected
   game-side common assembly) and must contain both
   packs and `SDL2.dll`. The NativeAOT output is a single-file executable plus PDBs, so the managed
   assemblies are linked in; `Bork2d.BakeFixture.pdb` is the remaining on-disk proof that the fixture
   was in the publish graph.
4. Run the AOT-published headless smoke locally (no GPU): default logging, one
   `Setup`/`Tick`/`Teardown`, root cleared, generated-shape round trip, the **baked main pack**
   (class root with nested struct/`Color`/texture reference, `StatsRoot` through
   `AssetRef<StatsRoot>`, sprite, `art/player` descriptor, exact `art/player-raw` payload), the
   **overlaid DLC pack** (`LoadPack("dlc1.pck")`, one `data/dlc` entry whose texture reference
   resolves to the main `art/player`), browse/exists (5 main entries, 6 merged), exit `0`.
5. Run the AOT-published windowed demo locally on a desktop session:
   `Bork2d.AotSmoke.exe --windowed --ticks 120` must exit `0` after exactly 120 ticks with one
   `Setup` and one `Teardown`; the demo uses `WithoutAssets()`. The interactive `--windowed` run
   observes the visible window and exits on Escape/close.
6. Run a windowed/offscreen AOT-published app on a GPU machine before release (M8+).
7. The AOT smoke loads the baked pack produced by the same tooling a game uses; the constrained
   `IShapeable<T>` contract makes a missing shape a compile-time error. The fixture's struct root
   proves the D-M4-11 value-type load path under ILC, including the `AssetRef<StatsRoot>` overload.
8. The AOT app logs through the default ZLogger/Microsoft.Extensions.Logging path without trim
   warnings; `IEngine.LoggerFactory` is usable.

CI scope is exactly: pull requests targeting `develop` and pushes to `develop`; it restores,
verifies formatting, builds Debug, and runs Debug tests. There is **no AOT job** in CI, so the
publish/run gate above is mandatory locally.

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
