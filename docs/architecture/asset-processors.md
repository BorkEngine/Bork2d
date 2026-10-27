# Asset Processors and `MyGame.Assets`

> **Status:** **Accepted design (D-036/D-037/D-038, 2026-10-27); implementation planned on
> `task/file-processors`; the current tree is still M4.** The M4 code hardcodes image extensions,
> has a texture-specific sidecar parser and texture-specific baked-index entries, reads
> `bork2d.project.toml`, and uses a generated host for data-type binding. This document defines the
> replacement: every non-TOML source type is an asset processor registered in a game-owned executable
> (`MyGame.Assets`), the project descriptor is removed, all bake configuration is builder
> configuration in that executable, and data types are registered explicitly with no generated host
> and no reflection. Until the task lands, the M4 as-built rules in the
> [M4 spec](../specs/m4-serialization-and-bake.md) and [assets and tooling](assets-and-tooling.md)
> remain the description of the tree; the sections here are the target contract. This document
> supersedes the texture-specific discovery/sidecar wording of D-M4-05, D-M4-13, D-M4-15, the
> descriptor parts of D-M4-14 and Q-03/Q-10/Q-11, and the generated-host parts of D-M4-02/Q-07/Q-08;
> D-036/D-037/D-038 record the decisions.

## 1. Goal

Make file-type support an extension point, not a set of engine-owned special cases:

- a source type is handled by a registered **asset processor** with an extension set, a **sidecar
  shape** when it requires settings (a normal `[GenerateShape]` type), and processing code;
- the engine's PNG/JPG/JPEG/BMP/TGA support is one processor among others;
- an engine user registers a processor for a new format in their game's assets executable without
  editing engine code;
- no file type is hardcoded in discovery, path mapping, the baked index, the pack composer, or the
  incremental input list;
- the project descriptor is gone: packs, roots, output root, texture defaults, processors, and data
  types are configured through a builder in `MyGame.Assets`;
- data types are registered explicitly through the builder (`RegisterAsset<T>()`) and bound with
  compile-time generic calls; there is no generated host and no reflection;
- `MyGame.Assets` is the game's bake CLI and its build bakes the packs automatically.

The design is proved by two test processors: **PPM** (a simple image format reusing
`TextureDescriptor`) and **Unit** (a JSON source that emits a custom `[GenerateShape]` data type with
a required sidecar).

## 2. Non-goals

- No runtime change: the NativeAOT runtime still reads packs only, with generated shapes and no
  TOML, no reflection resolver, and no processor assembly.
- No multi-input processors (atlas + metadata, font + glyph list) in this task; one source file may
  emit any number of outputs owned by that source. The contract leaves room for a later
  companion-file concept.
- No editor work. M5 consumes the builder and processor contracts later.
- No general TOML writer. The only new write is a missing required sidecar, written once from the
  processor's default shape.
- No automatic migration of existing projects; `bork2d.project.toml` is removed and the API guide
  shows the builder replacement.

## 3. Project model

```text
MyGame/
  src/
    Project.Common/            runtime-safe data types ([GenerateShape]); references Bork2d.Api only
    MyGame.Assets/             executable, JIT, build-time only; owns assets and processors
      Assets/                  main pack root
        data/*.toml            custom data ([bork2d].type)
        art/*.png              texture sources + <source>.toml sidecars
        data/*.unit            Unit processor sources + <source>.toml sidecars
      DLC1/                    optional overlay pack root
      Program.cs               BakeApplicationBuilder configuration
    Project.Desktop/           runtime app; references Bork2d.Runtime + Project.Common
    Project.Editor/            M5 editor host; references Bork2d.Editor + Project.Common
```

- **`Project.Common` remains the runtime data-type assembly.** `MyGame.Assets` references it so a
  processor can emit a runtime type such as `Unit` with its compiled shape. `Project.Common` never
  references the builder or processor assemblies.
- **`MyGame.Assets` owns assets, processors, and bake configuration.** It is an executable and
  build-time only; it is never referenced by `Project.Desktop`'s runtime output and never enters
  the AOT publish graph. `Project.Desktop` references it only as a build dependency so the packs
  exist before the app is copied/published.
- **`Bork2d.Bake.Builder` is the new public tooling project.** It contains the builder, explicit
  data-type registration, processor contracts, sidecar policy, output sink, and CLI command
  handling. It references the internal `Bork2d.Bake` implementation, which keeps
  Tomlyn/StbImageSharp internals hidden.
- `Bork2d.Bake.Cli` is retired as the game-facing CLI. The builder's command handling is the CLI;
  `MyGame.Assets` is the executable that hosts it. The engine fixture gets a build-only
  `Bork2d.BakeFixture.Assets` executable with the same shape.

## 4. Builder configuration (no project descriptor)

`bork2d.project.toml` is deleted. The builder is the single source of truth. Illustrative shape:

```csharp
// MyGame.Assets/Program.cs
using Bork2d.Bake.Builder;

return await new BakeApplicationBuilder()
    .WithProjectRoot(".")                      // MSBuild passes $(MSBuildProjectDirectory)
    .WithPack("assets.pck", "Assets")          // first pack = main
    .WithPack("dlc1.pck", "DLC1")              // later pack = overlay
    .UseDefaultAssets()                        // e.g. SpriteAsset
    .RegisterAsset<GameConfig>()               // explicit data-TOML type
    .UseDefaultProcessors()                    // built-in texture processor
    .WithTextureDefaults(TextureFilter.Linear, TextureWrap.Clamp)
    .AddProcessor(new PpmProcessor())
    .AddProcessor(new UnitProcessor())
    .Build()
    .RunAsync(args);
```

Configuration rules:

- At least one pack is required; the first is the main pack and every later pack is an
  overlay/DLC pack. Each pack has a required root and output path.
- Roots and output paths are relative, canonical-safe, and cannot escape the project root (same
  containment rules as M4, now validated by the builder instead of the descriptor parser).
- There is no common-assembly option. Each `RegisterAsset<T>()` carries `typeof(T).Assembly`; the
  builder derives the shape-assembly set and their dependency closure for incremental hashing. A
  registered type may therefore live in any assembly referenced by `MyGame.Assets`.
- `UseDefaultAssets()` registers the engine-provided authoring asset types. It currently registers
  `SpriteAsset`; texture descriptors are processor output and are not authoring data.
- `RegisterAsset<T>()` explicitly registers a data type that may appear in a data TOML's
  `[bork2d].type`. `T` must satisfy `IShapeable<T>` (the compiled `[GenerateShape]`). Registration
  is a compile-time generic call; there is no type lookup by reflection and no generated host.
- `UseDefaultProcessors()` registers the built-in texture processor. `AddProcessor(...)` registers a
  custom processor; a later registration overrides an earlier one for the same extension, so call
  the defaults before a custom override.
- `WithTextureDefaults` replaces `[textures]`; it configures the built-in texture processor, not
  the pipeline.
- Build/CLI inputs such as the output root, mode, `--force`, `--check`, and `--verbose` remain
  command-line options because they describe the invocation, not the project.
- Builder configuration is validated before any source is read. Failure is a `BK1xxx` diagnostic
  and exit code `2`.

`MyGame.Assets` is the CLI: `bake`, `validate`, `clean`, and the pack-copy command used by
`Project.Desktop`. The commands are thin wrappers over the builder; a game can run them manually.
The build target runs `bake` automatically. There is no `generate-host` command and no generated
host build.

An illustrative project file (engine distribution path is finalized by the M9 template):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Project.Common\Project.Common.csproj" />
    <ProjectReference Include="..\..\engine\src\Bork2d\Bork2d.Bake.Builder\Bork2d.Bake.Builder.csproj" />
  </ItemGroup>
  <Import Project="..\..\engine\build\Bork2d.Assets.targets" />
</Project>
```

## 5. Processor contract

The public contract lives in `Bork2d.Bake.Builder` and is JIT tooling only.

```csharp
public interface IAssetProcessor
{
    string Name { get; }                          // stable; defaults to the CLR full name
    IReadOnlyList<string> Extensions { get; }     // "png" / ".PNG" both accepted
    SidecarPolicy Policy { get; }                 // None | Required
    ITypeShape? SidecarShape { get; }             // null when Policy is None
    void Process(AssetProcessorContext context);
}

public abstract class AssetProcessor<TSidecar> : IAssetProcessor
    where TSidecar : IShapeable<TSidecar>
{
    public ITypeShape SidecarShape => TSidecar.GetTypeShape();
    public SidecarPolicy Policy => SidecarPolicy.Required;

    // Called only when the Required sidecar file is missing; the builder writes the returned
    // object to <source>.toml, then binds it and calls Process.
    protected abstract TSidecar CreateDefaultSidecar(AssetProcessorContext context);

    protected abstract void Process(AssetProcessorContext context, TSidecar sidecar);
}

public enum SidecarPolicy
{
    None,
    Required,
}

public sealed class AssetProcessorContext
{
    public string SourcePath { get; }          // absolute source path
    public string RelativePath { get; }        // pack-root relative, '/'
    public string BaseAssetPath { get; }       // canonical path, matched extension removed
    public string SourceFileName { get; }      // "archer.unit"
    public string SourceFileNameWithoutExtension { get; } // "archer"
    public IAssetOutputSink Outputs { get; }
    public IAssetProcessorDiagnostics Diagnostics { get; }
}

public interface IAssetOutputSink
{
    // The processor supplies a typed instance; the engine serializes it as MessagePack.
    void AddAsset<T>(string assetPath, T value) where T : IShapeable<T>;

    // Raw bytes are only for opaque data assets (texture payloads, future audio, etc.).
    void AddData(string assetPath, ReadOnlySpan<byte> payload);
}

public interface IAssetProcessorDiagnostics
{
    void Error(string code, string message);
    void Warning(string code, string message);
    void Info(string code, string message);
}
```

Key properties:

- There are exactly two policies: `None` (no sidecar) and `Required` (a sidecar is always used). A
  processor with no sidecar implements `IAssetProcessor` directly; `AssetProcessor<TSidecar>` is the
  `Required` base and always exposes a sidecar shape.
- `AssetProcessor<TSidecar>` has a **separate** abstract `CreateDefaultSidecar(context)` function.
  The builder calls it only when a `Required` sidecar file is missing, writes the returned object as
  `<source>.toml`, then binds that file and calls `Process`. It never calls it when the file exists.
- The sidecar is a normal `[GenerateShape]`/`partial` type. The framework binds it through the
  existing shape-driven `TomlShapeBinder`, using a non-generic `Bind(ITypeShape, …)` entry point.
  There is no hand-written sidecar parser, no per-processor generated binder, and no reflection
  over sidecar members.
- **MessagePack is the engine's responsibility.** `AddAsset<T>` takes a typed instance; the engine
  serializes it with `T.GetTypeShape()` through the existing `PackBuilder.AddMessagePack<T>` path.
  A processor never serializes MessagePack, never sees MessagePack bytes, and never calls
  `PackBuilder`. The output type can be a public runtime type (`TextureDescriptor`, `Unit`) or any
  other shapeable type; it does **not** need a `RegisterAsset<T>()` entry, because `AddAsset<T>`
  carries the compiled shape at compile time.
- `AddData` is only for opaque raw payloads. A `[GenerateShape]` asset must come through
  `AddAsset<T>` as an instance; a processor cannot emit pre-serialized bytes for a shaped asset.
- A processor may emit any number of assets and data entries for one source; there is no fixed
  output count. The texture processor uses two: a `TextureDescriptor` asset and its raw payload.
- References in processor output are collected automatically by the existing
  `AssetReferenceCollector` shape walk; a processor does not declare references by hand.
- A processor is synchronous and runs on the single bake thread. It must not start work that
  outlives the call. There is no per-source parallelism in this task.
- A processor that reports an error has all of its outputs for that source discarded. An exception
  is caught, reported as a `BK7xxx` error naming the processor and source, and the bake continues
  with other sources.

## 6. Registration and override

- `AddProcessor(IAssetProcessor)` registers an instance; constructor dependencies are allowed.
  Registration is compile-time code, not assembly scanning.
- The built-in texture processor is registered first by the builder.
- Extensions are normalized to lowercase without a leading dot. Both `"png"` and `".PNG"` mean
  `png`.
- Matching is case-insensitive and uses the **longest registered compound suffix** (so a future
  `tar.gz` registration wins over `gz`). A normal one-extension source is the common case.
- Unregistered files are ignored, matching M4's "non-asset files are not sources" behavior.
- **A later registration overrides an earlier one for the same extension.** There is no duplicate
  extension error. The builder reports an `Info` diagnostic naming both processors. This is how a
  game replaces the built-in `png` processor if it needs a different decoder.
- The registry is immutable after `Build()`. Processor order is stable for diagnostics and hashing;
  output entries are always sorted by canonical path.

### 6.1 Data type registration

Custom-data TOML still declares its type in the reserved table:

```toml
[bork2d]
type = "MyGame.Data.GameConfig"
```

That string is a **selector**, not a registration. The builder must contain a compile-time
registration for every type a source may select:

```csharp
.RegisterAsset<GameConfig>()
.RegisterAsset<EnemyDefinition>()
```

```csharp
public BakeApplicationBuilder RegisterAsset<T>() where T : IShapeable<T>;
public BakeApplicationBuilder UseDefaultAssets();
public BakeApplicationBuilder UseDefaultProcessors();
```

- `RegisterAsset<T>()` is constrained to `T : IShapeable<T>`, so `T` must be a `[GenerateShape]`
  type with a compiled shape. It creates a typed registration object that closes over
  `TomlShapeBinder.Bind<T>` and `PackBuilder.AddMessagePack<T>`; the builder stores it in a
  dictionary keyed by `typeof(T).FullName`.
- There is no reflection and no generated host. The generic call is the binding; the dictionary
  lookup is the type resolution.
- A `[bork2d].type` that is not registered is a `BK1xxx` error that names the type and points at
  the source line, with a hint to call `RegisterAsset<T>()`. Duplicate registrations of the same
  full name are an error.
- `[bork2d].type` is kept intentionally: registration says which types are available, and the
  source says which one it is. Removing it would require a path-to-type mapping in code and would
  duplicate every source path; the selected design keeps sources self-describing.
- `UseDefaultAssets()` registers engine-provided authoring types (`SpriteAsset` today). It does not
  register a game's own types. `UseDefaultProcessors()` is separate and registers the built-in
  texture processor.
- A registered data type must be accessible to `MyGame.Assets`: public, or internal with an
  `InternalsVisibleTo` grant to the assets assembly. There is no host assembly to grant access to.
- The builder derives the shape-assembly set from the registered types (`typeof(T).Assembly`) and
  the assets executable's non-tooling dependency closure. There is no common-assembly option and no
  type lookup; the registration dictionary is the resolution.

## 7. Discovery and file classification

There is no per-type special case. Discovery is:

```text
for each file under each pack root (sorted, reparse points skipped):
    if the file ends in .toml:
        typed ([bork2d].type present)              -> custom-data source
        else if it is <source>.toml for a
                 registered source whose
                 processor has a sidecar           -> sidecar (owned by that source)
        else                                       -> missing-type error
    else if a processor matches the file suffix    -> processor source
    else                                           -> ignored
```

- `.toml` remains the one metadata/source format (D-036): custom data is typed TOML, sidecars are
  type-less TOML. Source extensions are processor-owned.
- A sidecar is `<source file>.toml` next to the source (`art/player.png.toml`,
  `data/archer.unit.toml`). It is associated by canonical base path, so casing differences between
  the source and sidecar resolve as they do in M4.
- **Filename decides sidecar classification.** If `<source>.toml` exists for a registered source
  whose processor has a sidecar, it is that source's sidecar. A `[bork2d]` table inside it is an
  unknown sidecar key (`BK2xxx`), not a data declaration. This deliberately removes the M4 content
  sniffing that read every sidecar-shaped TOML to see whether it declared a type; with processors
  the filename and registration are authoritative.
- A type-less `.toml` that is neither a data source nor a sidecar remains a missing-type error. An
  orphaned sidecar (source missing) is an error.
- A processor with `SidecarPolicy.None` has no sidecar; a `<source>.toml` next to it is treated as
  ordinary TOML (data if typed, otherwise missing-type).
- Data TOML still declares `[bork2d] type = "Full.Type.Name"` (D-M4-13) and is bound by the
  builder's registered type entry (§6.1). Processor outputs do not participate in `[bork2d]`
  registration.

## 8. Sidecars

### 8.1 Policies

| Policy | Missing sidecar | Existing sidecar | Default creation |
| --- | --- | --- | --- |
| `None` | Fine; no sidecar lookup | Not a sidecar; ordinary TOML rules | Never |
| `Required` | Call `CreateDefaultSidecar`, write `<source>.toml`, then bind it | Bind and pass to the processor | Once, only when missing |

- `AssetProcessor<TSidecar>` is the `Required` base. Its abstract
  `CreateDefaultSidecar(AssetProcessorContext)` is a **separate function** from `Process`; the
  builder calls it only when the sidecar file is missing. The processor returns the default object,
  the builder writes it, then the normal binder reads it back. There is no “optional, use C#
  defaults without a file” policy.
- A required sidecar that cannot be created is a `BK7xxx` error; the source is not baked.
- Default creation is deterministic, writes a canonical TOML file, never overwrites an existing
  file, and reports `Info` with the path. This is the only bake-time source-tree write; it is
  intended for authoring convenience when a new source file is added.
- Sidecars are settings only in this task. `AssetRef`/`DataRef` members in a sidecar shape are not
  supported; the processor should model settings, and references belong in data TOML.

### 8.2 Binding and diagnostics

- Sidecars use the same shape-driven TOML binder and attribute rules as custom data:
  camelCase keys, `[JsonPropertyName]`, `[JsonRequired]`, unknown-key errors, enum names, numeric
  ranges, and value spans where the binder has them.
- Sidecar binding uses `BK2xxx` codes (the M4 hand-written texture sidecar codes `BK4011`/`BK4012`
  are retired). The built-in texture processor keeps its decode/probe/dimension errors in `BK4xxx`.
- A missing `Required` sidecar is created and reports `Info`; it is not an error unless creation
  fails.
- A sidecar with a syntax error uses the shared `BK2001` TOML syntax code.

## 9. Outputs and asset paths

- `BaseAssetPath` is the source path relative to its pack root, lowercased, with the matched
  extension removed. `art/Player.PNG` → `art/player`.
- A processor may emit **any number of outputs**. Every output path must be
  **base-path-derived**: `BaseAssetPath` itself or `BaseAssetPath + "-" + suffix`, where `suffix` is
  one canonical segment of `[a-z0-9_-]+`. This prevents a processor from writing outside its
  source's directory or inventing unrelated paths. Multiple outputs are expected: the texture
  processor emits `textures/image` and `textures/image-raw` for `textures/image.png`.
- A shaped asset output is added with `AddAsset<T>` as an instance; the engine serializes it. A raw
  payload is added with `AddData`. The path must be canonical; the name must be base-path-derived;
  duplicate paths from the same source are a `BK7xxx` error.
- Outputs from different sources, data TOML, and other processors share the same baked index.
  Cross-processor collisions are `BK3005`, exactly as data/texture collisions are today.
- Outputs are added to the pack whose root contains the source. Overlay/DLC rules are unchanged:
  the main pack is strict; a DLC may reference local or main; DLC→DLC is an error.
- Processor output references are validated against the complete baked set before the pack is
  written, using the collected `AssetRef`/`DataRef` sites.
- A processor output type must be shapeable (`IShapeable<T>`) and its shape must be compiled into
  an assembly available to the runtime when the asset is loaded. `Unit` therefore lives in
  `Project.Common`, not in `MyGame.Assets`.

## 10. Built-in texture processor

The existing texture behavior moves behind the same contract; it does not keep a privileged path.

- `TextureProcessor : AssetProcessor<TextureSidecar>` registers `png`, `jpg`, `jpeg`, `bmp`, `tga`.
- `TextureSidecar` is a `[GenerateShape]` type with `filter` and `wrap` members. The processor is
  `Required`; when a texture has no sidecar, `CreateDefaultSidecar` returns the builder's
  `WithTextureDefaults` values and the builder writes `<source>.toml` before processing.
- The processor keeps the existing decoding rules: StbImageSharp to straight-alpha RGBA8, no
  premultiplication/gamma/ICC/flipping/resampling; explicit 16-bit PNG rejection, truncation
  probing, dimension/payload checks, and `BK4xxx` diagnostics.
- It emits a `TextureDescriptor` instance at `BaseAssetPath` and the raw RGBA8 payload at
  `BaseAssetPath + "-raw"`; `Payload` points at the raw path. The engine serializes the descriptor;
  this preserves the M3/M4 runtime contract.
- The descriptor's `filter`/`wrap` values come from the sidecar or the builder defaults. There is
  no `[textures]` table and no descriptor parser.
- `TexturePipeline`, `TextureSidecarParser`, `BakedTexture`, `TextureSourceEntry`, and the
  texture-specific `BakedAssetEntry`/`PackComposer` branches are removed or become private
  implementation details of the processor.

## 11. Test processors

### 11.1 PPM (image extension point)

- `PpmProcessor : AssetProcessor<PpmSidecar>` with extension `ppm`; required sidecar with
  `filter`/`wrap` (default values created when missing); simple P6 (binary) support with header
  comments/whitespace, max value 255, dimension validation, RGB→RGBA8 conversion (alpha 255).
- Emits a `TextureDescriptor` instance (engine-serialized) + raw payload, reusing the public runtime
  descriptor so the test proves a user processor produces a loadable asset.
- Tests: extension discovery, sidecar binding/unknown key, exact RGBA bytes, descriptor/payload
  paths, collisions, unregistered extension ignored, full bake → `LoadAsset<TextureDescriptor>`.

### 11.2 Unit (custom data extension point)

- `UnitProcessor : AssetProcessor<UnitSidecar>` with extension `unit`; required sidecar with an
  optional `name` member. When missing, `CreateDefaultSidecar` returns
  `new UnitSidecar { Name = context.SourceFileNameWithoutExtension }`; JSON payload shape:

```json
{
    "Health": 50,
    "Attack": 20
}
```

- The source is `Assets/data/archer.unit`; the sidecar is `Assets/data/archer.unit.toml`. The name
  is the sidecar `name` when present, otherwise `SourceFileNameWithoutExtension` (`archer`). A
  missing sidecar is created with `name = "archer"` before binding.
- The output type is a runtime data type in the fixture common assembly:

```csharp
[GenerateShape]
public readonly partial record struct Unit(string Name, int Health, int Attack);
```

- The processor emits `context.Outputs.AddAsset(context.BaseAssetPath, new Unit(name, health, attack))`
  at `data/archer`. It never serializes MessagePack. It uses `System.Text.Json` in the JIT assets
  executable; JSON parsing is processor-owned, not an engine format.
- Tests: default-sidecar creation with the filename name, sidecar override, JSON parse/validation,
  and struct round-trip through `LoadAsset<Unit>`.
- This test proves a custom processor can emit a custom `[GenerateShape]` type, not only a texture
  descriptor, and that processor outputs need no `RegisterAsset<T>()` entry.

## 12. Data binding, incremental bake, and MSBuild

### 12.1 No generated host

- Data TOML is bound by the builder's explicit registrations (§6.1). `[bork2d].type` selects one
  registered entry; the entry calls `TomlShapeBinder.Bind<T>` and the per-type pack writer closes
  over `PackBuilder.AddMessagePack<T>`. There is no generated project, host assembly, host manifest,
  `MetadataLoadContext`, host `AssemblyLoadContext`, host-staleness check, or host-generation state.
- `MyGame.Assets` compiles the registrations into the assets executable. It resolves names from an
  in-memory dictionary, not from reflection. A type that is not registered is a `BK1xxx` error with
  a `RegisterAsset<T>()` hint.
- `bake` is a single command: build `MyGame.Assets` (already done by MSBuild), run `bake`, bind,
  validate, and write. There is no nested `dotnet build` of a generated host.
- `validate` uses the same builder registry and runs discovery, processor/sidecar validation,
  binding, and reference validation without writing a pack.

### 12.2 Incremental inputs

The manifest (`<project root>/obj/bork2d/bake/manifest.json`, schema bumped) records:

- builder configuration hash: packs/roots/output paths, texture defaults, registered type names,
  processor identities, and processor assembly hash;
- every tracked input: every `.toml` under every pack root plus every file whose suffix is in the
  registered extension set (so `.ppm`/`.unit` are tracked automatically);
- the game-side assembly closure hash (the registered data/output types and their dependencies);
- the tool version, mode, every output pack path/hash, and aggregate counts.

The bake skips only when all fields match and every output exists with its recorded hash. Changing
a registration, a processor extension, a sidecar policy, processor code, or a source/pack path
invalidates the state. A changed `[bork2d].type` is already a changed TOML input. `--force` bypasses
the skip; `--check` validates without writing.

### 12.3 Build integration

- `MyGame.Assets` imports `build/Bork2d.Assets.targets` (name to be finalized). After `Build`, the
  target runs the freshly built executable with `bake`, passing the project root, configuration
  mode, and output root. The build fails with the canonical `BKxxxx` lines on bake errors.
- `MyGame.Assets` is the CLI, so it also supports `validate`, `clean`, and a pack-copy command.
  There is no `generate-host` command.
- `Project.Desktop` references `MyGame.Assets` **for build order only** (`ReferenceOutputAssembly`
  false) and imports a thin copy target: after `Build` it asks `MyGame.Assets` to copy its packs to
  `$(TargetDir)`; after `Publish` to `$(PublishDir)`; on `Clean` it asks the assets executable to
  clean. MSBuild never needs a pack-path list; the builder config owns the paths.
- There is no `bork2d.project.toml`, no `Bork2dBakePackPaths`, no `[bake].host`, no `[textures]`
  table, and no `obj/bork2d/bakehost` directory. The old `build/Bork2d.Bake.targets` descriptor
  properties are removed.
- The engine fixture uses `Bork2d.BakeFixture.Assets` (same pattern) so bake tests, the MSBuild
  integration test, and the AOT smoke exercise the real game flow. `Bork2d.AotSmoke` takes a
  build-only reference to it; the AOT publish still contains only the fixture data types and the
  baked packs, never the assets executable or builder.

## 13. Diagnostics

| Range | Owner |
| --- | --- |
| `BK1xxx` | Builder configuration and explicit asset-type registration |
| `BK2xxx` | TOML syntax/binding for data and sidecars |
| `BK3xxx` | References and baked-set validation |
| `BK4xxx` | Built-in texture decode/probe/dimensions (and PPM if it chooses to reuse them) |
| `BK5xxx` | Pack composition/write, incremental state |
| `BK6xxx` | MSBuild/build integration |
| `BK7xxx` | Processor registration and processing |

- Processor code uses `BK7xxx` only. The message includes the processor name and source path; a
  source line/column is optional and normally absent for decode/processing errors. Sidecar binding
  errors come from the binder with `BK2xxx` and spans.
- Warnings and infos are allowed. Expected infos: later processor overrides an extension, required
  sidecar created, bake skipped as up to date.
- A processor output outside the base-path rule, a duplicate output, a processor exception, or a
  failed required-sidecar write is an error and prevents the pack write.
- Unregistered files are ignored without a diagnostic.

## 14. Determinism, threading, AOT, and security

- Registration order is deterministic; later registrations override earlier ones for one extension;
  processors are hashed in a stable order. Outputs and diagnostics use the existing sorted order.
- The bake remains single-threaded. Processors must be safe for one sequential call per source; the
  builder may reuse a processor instance across sources, so processors must not keep per-source
  state between calls.
- Builder, processors, and `MyGame.Assets` are JIT tooling. They are not in the runtime or AOT
  publish graph. Game data types stay in `Project.Common` and remain AOT-safe.
- Processor output types need `[GenerateShape]` and must be loadable by the runtime; a processor
  that emits a type living only in `MyGame.Assets` would bake but fail to load at runtime, so the
  test processors put `Unit` in the fixture common assembly.
- The processor API is trusted build-time code. It may read the source file and use normal .NET
  libraries; it cannot escape the pack-root path rules because output paths are validated against
  `BaseAssetPath`.
- Reparse points are still skipped by discovery. Required-sidecar creation writes only next to the
  source and only when absent.

## 15. Migration and work packages

Planned sequence on `task/file-processors`:

1. **Builder / no descriptor / no host.** Add `Bork2d.Bake.Builder` with
   `BakeApplicationBuilder`/`BakeApplication`, explicit `RegisterAsset<T>()`,
   `UseDefaultAssets()`/`UseDefaultProcessors()`, configuration validation, commands, and
   output/copy targets. Remove `ProjectDescriptor`, the generated host, host generation/loading, and
   `MetadataLoadContext`; migrate tests/fixtures. Add `Bork2d.BakeFixture.Assets`.
2. **Processor framework.** Add the processor contract, explicit data-type registry, registry override
   rules, base-path output validation, sidecar policy, sidecar binder entry point, `BK7xxx`, and
   generic baked-index entries. Remove hardcoded extension lists from discovery/path/incremental.
3. **Texture processor.** Move the existing texture pipeline behind `TextureProcessor`, keep all
   behavior/tests, retire `TextureSidecarParser` and texture-specific index/composer branches.
4. **PPM and Unit.** Add the two test processors, fixture sources, and end-to-end tests.
5. **Build/AOT/docs.** Wire `MyGame.Assets`-style build targets, fixture assets executable, AOT
   fixture flow, and finalize these docs.

## 16. Acceptance criteria

- A game-owned `MyGame.Assets` executable configures packs, roots, texture defaults, processors,
  and explicit data types through the builder; no `bork2d.project.toml` is read or required.
- Data TOML keeps `[bork2d].type`; each such type is registered with `RegisterAsset<T>()` and
  bound with compile-time generic calls. No generated host and no reflection are used.
- `UseDefaultAssets()` and `UseDefaultProcessors()` register the engine defaults; a game can add
  its own types/processors without touching engine code.
- Building `MyGame.Assets` bakes its packs automatically; `Project.Desktop` gets the packs without
  a pack-path list.
- The engine's PNG/JPG/JPEG/BMP/TGA support is a registered processor; discovery has no image
  extension list, and no texture-specific branches remain in the generic index/composer.
- A test registers a `.ppm` processor and bakes/loads a `TextureDescriptor` + exact RGBA payload.
- A test registers a `.unit` processor, reads JSON, defaults the name to the file name, honors a
  sidecar override, and loads a `Unit` struct.
- `Required` sidecars are created once when missing through `CreateDefaultSidecar`; `None` has no
  sidecar file.
- A later processor registration overrides an earlier one for the same extension.
- Processor errors are `BK7xxx`; sidecar binding errors are `BK2xxx`; texture decode errors stay
  `BK4xxx`.
- Debug/Release builds, tests, format, docs link check, and the local AOT publish/run gate pass.

## 17. Implementation choices to confirm

These are naming/mechanical choices made in this design; they do not change the approved semantics
and are settled during WP1/WP2.

- Final project/API names: `Bork2d.Bake.Builder`, `BakeApplicationBuilder`, `BakeApplication`,
  `RegisterAsset<T>`, `UseDefaultAssets`, `UseDefaultProcessors`, `IAssetProcessor`/
  `AssetProcessor<TSidecar>`, `SidecarPolicy`, and `IAssetOutputSink` are proposed. The
  `MyGame.Assets` and `Bork2d.BakeFixture.Assets` names are from D-037.
- MSBuild file name/properties: `build/Bork2d.Assets.targets` and the copy target are proposed; the
  assets executable owns the pack paths, so no pack-path property is required and there is no host
  build step.
- Default-sidecar TOML writer: the builder may use a small canonical writer or Tomlyn serialization;
  it must be deterministic and must not overwrite an existing file.
- Sidecar reference values: this task treats sidecars as settings-only. If a processor needs an
  `AssetRef`/`DataRef` in its sidecar, that is a follow-up decision; data TOML remains the place for
  references.
- The exact `AssetProcessorContext` helper names (e.g. `SourceFileNameWithoutExtension`) are
  illustrative.

## 18. Related documents

- [Assets and tooling](assets-and-tooling.md) — pack, VFS, asset model, current M4 bake text.
- [Projects and dependencies](projects.md) — project graph and game layout.
- [API guide](../api/guide.md) and [API reference](../api/reference.md) — builder/processor usage.
- [M4 spec](../specs/m4-serialization-and-bake.md) — historical M4 record and as-built evidence.
- [Decision log](../decisions.md) — D-036/D-037/D-038 and the superseded M4 decisions.
- [NativeAOT](nativeaot.md), [Contributing](../contributing.md) — tooling boundary and gates.
