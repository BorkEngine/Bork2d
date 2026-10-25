# NativeAOT

> **Status:** target constraint from M1. The runtime and game desktop host are
> NativeAOT-compatible; the editor and build tools stay JIT. This is not implemented or
> validated yet.

## 1. Scope

| Component | NativeAOT required? | Why |
| --- | --- | --- |
| `Bork2d.Api` | Yes | It is referenced by the AOT game and must avoid dynamic/reflection traps. |
| `Bork2d.Common` | Yes | Runtime scene, snapshots, transforms, time. |
| `Bork2d.Assets` | Yes | Pack parsing, decompression, MessagePack decoding, resource tables. |
| `Bork2d.Render` | Yes | Vulkan renderer and uploads. |
| `Bork2d.Runtime` | Yes | Builder, SDL platform, host. |
| `Project.Desktop` | Yes | The shipped game/headless server is published with `PublishAot`. |
| `Bork2d.Editor` | No (JIT) | Avalonia desktop tool; may reflect over the game model. |
| Asset baker / pack tool | No (JIT) | Build-time tool; may reflect over game assemblies. |
| Tests | Mixed | Unit tests JIT; at least one AOT publish smoke test. |

The editor being JIT is deliberate: it keeps a rich Avalonia UI and reflection-based property
grid out of the runtime's AOT/trimming constraints. The compile-time editor builder still
avoids assembly scanning, so the boundary is explicit.

## 2. Build settings

Target settings for runtime/game projects:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <IsAotCompatible>true</IsAotCompatible>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <EnableAotAnalyzer>true</EnableAotAnalyzer>
  <InvariantGlobalization>false</InvariantGlobalization> <!-- decide at M1 -->
</PropertyGroup>
```

Notes:

- `IsAotCompatible`/`EnableAotAnalyzer` on engine libraries catch reflection and dynamic-code
  warnings during normal builds, not only at publish.
- `PublishAot` is set on `Project.Desktop` (the executable). Libraries do not publish
  independently.
- Native assets (SDL2, Vulkan loader) must be deployed for the target RID. The exact
  `RuntimeIdentifier` and native-copy story are part of M1/M6.
- AOT publish is a CI job, not only a release step. A JIT-clean build can still fail AOT.
- Trimming warnings are treated as errors for all projects; game/template projects enable the
  same AOT/trim analyzers as the engine.

## 3. Runtime constraints

The runtime must not depend on:

- `Activator.CreateInstance`, `Type.GetType`, `Assembly.Load`, `AssemblyLoadContext`, or
  assembly scanning;
- `Reflection.Emit`, expression compilation, or dynamically generated types;
- runtime TOML parsing or reflection-based serializers;
- convention-based discovery of game types, assets, or editor registrations;
- `MakeGenericType`/`MethodInfo.Invoke` on unknown types;
- unregistered Nerdbank.MessagePack shapes;
- `dynamic` and runtime code generation in hot paths.

It may use:

- static generics and `typeof(T)` metadata that the AOT compiler can see;
- source-generated Nerdbank.MessagePack shapes, including `PackHeader`/`PackEntry`;
- explicit builder registrations (`WithGame(instance)`, `WithAssetType<T>()`,
  `WithWindow<T>()`);
- bounded validation around the generated pack header/entry shapes and known asset kinds;
- `DllImport`/Silk.NET bindings for SDL2/Vulkan.

### Game startup

The game is constructed by the developer and passed in:

```csharp
var game = new MyGame(logger, network, saveStore);

using var app = new Bork2dAppBuilder<GameLayer>()
    .WithGame(game)
    .WithAssetsPack("assets.pck")
    .Build();

app.Run();
```

There is no `new()` constraint and no reflection-based construction. This is required for
AOT and also allows arbitrary constructor injection.

## 4. Serialization and assets

- Runtime data assets are MessagePack, decoded by
  [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) **source-generated
  shapes**.
- `LoadAsset<T>` works only for types with a generated shape (for example `[GenerateShape]` on
  the root type). A missing shape is a build/startup error; there is no reflection fallback.
- Pack header/entry classes, texture descriptors, and sprite assets use Nerdbank generated
  shapes; they are part of the engine, not user types.
- Custom game data uses the Nerdbank generated-shape path; TOML exists only in
  `Project.Common` and is consumed by the JIT baker/editor.
- Zstd uses the .NET 11 built-in implementation; the pack header checksum uses the
  `System.IO.Hashing` package's `XxHash3`.
- Asset paths are data, not type names. Loading never resolves a type from a string.

See [Assets and pack files](assets.md) and [Data and serialization](data-and-serialization.md).

## 5. Editor and tooling boundary

`Bork2d.Editor` and the asset baker may use:

- reflection over `Project.Common` types for the property grid;
- TOML parsing/writing with reflection;
- `AssemblyLoadContext` if the project descriptor loads a game assembly;
- Avalonia and other desktop-tool dependencies.

They must not leak these dependencies into `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets`, or
`Bork2d.Render`. The runtime projects compile with AOT analyzers and no reference to the
editor.

The editor builder still uses explicit registrations:

```csharp
new Bork2dEditorBuilder()
    .WithProject("../MyGame.Common")
    .WithAssetType<EnemyDefinition>()
    .WithWindow<MyToolWindow>()
    .Build()
    .Run();
```

That is a deliberate choice for determinism and a possible future AOT editor, not a current
AOT requirement.

## 6. Native libraries and Silk.NET

- SDL2 and the Vulkan loader are native dependencies; they are deployed per target RID.
- Silk.NET bindings are used instead of dynamic P/Invoke discovery. Validate the chosen
  Silk.NET versions under AOT in M1 (SDL) and M6 (Vulkan).
- No runtime shader compilation: SPIR-V is built offline.
- The headless server should not initialize SDL or Vulkan; `WithHeadless(...)` must not load or
  call into the graphics/windowing paths.

These are validation items, not assumptions. If a Silk.NET path uses reflection/dynamic code
incompatibly with AOT, isolate it behind an adapter and record the workaround.

## 7. Testing and CI

Required checks:

1. `dotnet build` with AOT/trim analyzers and warnings-as-errors.
2. `dotnet publish -c Release -r win-x64 -p:PublishAot=true` for a sample Desktop app.
3. Run the AOT-published **headless** app in CI (no GPU required): load a pack, tick, browse,
   exit.
4. Run the AOT-published windowed/offscreen app on a GPU runner before release.
5. Trim/AOT warning budget: zero warnings for every project; style and compiler warnings are
   errors. AOT/trim analyzers are errors on runtime/game projects.
6. A smoke test that no `LoadAsset<T>` type lacks a Nerdbank generated shape.
7. AOT-published app logs through the default ZLogger/Microsoft.Extensions.Logging path without
   trim warnings; `IEngine.LoggerFactory` is usable.

A JIT unit-test pass is necessary but not sufficient; AOT failures often appear only at
publish/runtime.

## 8. Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Silk.NET or a dependency uses reflection/dynamic code | AOT publish/runtime failure | Validate in M1/M6; isolate adapters; pin versions. |
| ZLogger/MEL provider not AOT-safe | Trim warnings or startup failure | Validate in M1; isolate provider behind `ILoggerFactory`; AOT-safe fallback. |
| MessagePack source generation misses a custom type | Runtime load failure | Build-time validation; `LoadAsset<T>` registration checks; tests. |
| TOML/reflection leaks into runtime | AOT warnings/failure | Project boundary and analyzers; TOML only in baker/editor. |
| Trimming removes needed code | Subtle runtime failures | `IsAotCompatible`, analyzers, AOT smoke tests. |
| Native SDL2/Vulkan deployment | Missing DllNotFound at runtime | RID-specific packaging and clean-machine tests. |
| Server pulls in SDL/Vulkan initialization | Unwanted GPU/window dependency | `WithHeadless` path that never initializes graphics. |
| Editor keeps reflection while runtime forbids it | Boundary confusion | Separate assemblies/projects; editor JIT; runtime analyzers. |

## 9. Open decisions

| Topic | Question | Proposed direction |
| --- | --- | --- |
| MessagePack generation | Nerdbank shape/analyzer setup and root-type annotations. | `[GenerateShape]` roots; reflection resolver disabled. |
| Globalization | `InvariantGlobalization` true or false? | Decide in M1 based on text/culture needs. |
| AOT target RIDs | Windows x64 first; Linux later? | win-x64 first; Linux when platform support is added. |
| Trimming game code | Are game projects required to be fully trim-clean? | All projects warnings-as-errors; trim warnings are errors unless narrowly suppressed with justification. |
| Editor AOT | Ever? | Not required; keep registrations AOT-friendly anyway. |
| Native library packaging | How SDL2/Vulkan loaders are copied/deployed. | MSBuild targets + documented redistributables (M1/M6). |
| AOT diagnostics | Startup check for missing Nerdbank shapes? | Yes: fail fast with the type name. |

## 10. Current status

| Area | Current | Target |
| --- | --- | --- |
| AOT analyzers/settings | None | M1 |
| `WithGame(instance)` | Builder absent | M1 |
| Runtime source-generated assets | No asset pipeline | M3/M4 |
| AOT headless smoke | None | M1 publish/build; full pack/tick/exit in M7; release gate M9 |
| Silk.NET AOT validation | Not started | M1 (SDL), M6 (Vulkan) |
| Editor JIT boundary | Editor absent | M5 |
| AOT publish CI | None | M1 baseline, M9 release gate |

## 11. Related documents

- [Projects and dependencies](projects.md#6-build-packaging-and-versioning) — project graph and
  build settings.
- [Assets and pack files](assets.md#13-nativeaot) — runtime pack constraints.
- [Data and serialization](data-and-serialization.md) — Nerdbank.MessagePack source generation.
- [Logging](logging.md) — ZLogger/MEL AOT notes.
- [Editor](editor.md#9-runtime-and-aot-boundary) — JIT editor boundary.
- [Roadmap](../roadmap.md) — M1 baseline, M9 release gate.
