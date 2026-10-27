# Asset Processors and `MyGame.Assets`

> **Status:** **Accepted design (D-036/D-037, 2026-10-27); implementation planned on
> `task/file-processors`; the current tree is still M4.** The M4 code hardcodes image extensions,
> has a texture-specific sidecar parser and texture-specific baked-index entries, and reads
> `bork2d.project.toml`. This document defines the replacement: every non-TOML source type is an
> asset processor registered in a game-owned executable (`MyGame.Assets`), the project descriptor is
> removed, and all bake configuration is builder configuration in that executable. Until the task
> lands, the M4 as-built rules in the [M4 spec](../specs/m4-serialization-and-bake.md) and
> [assets and tooling](assets-and-tooling.md) remain the description of the tree; the sections here
> are the target contract. This document supersedes the texture-specific discovery/sidecar wording
> of D-M4-05, D-M4-13, D-M4-15, and the descriptor parts of D-M4-14 and Q-03/Q-10/Q-11; D-036 and
> D-037 record the decision.

## 1. Goal

Make file-type support an extension point, not a set of engine-owned special cases:

- a source type is handled by a registered **asset processor** with an extension set, an optional
  **sidecar shape** (a normal `[GenerateShape]` type), and processing code;
- the engine's PNG/JPG/JPEG/BMP/TGA support is one processor among others;
- an engine user registers a processor for a new format in their game's assets executable without
  editing engine code;
- no file type is hardcoded in discovery, path mapping, the baked index, the pack composer, or the
  incremental input list;
- the project descriptor is gone: packs, roots, output root, host directory, common assembly,
  texture defaults, and processors are configured through a builder in `MyGame.Assets`;
- `MyGame.Assets` is the game's bake CLI and its build bakes the packs automatically.

The design is proved by two test processors: **PPM** (a simple image format reusing
`TextureDescriptor`) and **Unit** (a JSON source that emits a custom `[GenerateShape]` data type with
an optional sidecar).

## 2. Non-goals

- No runtime change: the NativeAOT runtime still reads packs only, with generated shapes and no
  TOML, no reflection resolver, and no processor assembly.
- No multi-file processors (atlas + metadata, font + glyph list) in this task; one source file maps
  to zero or more outputs owned by that source. The contract leaves room for a later companion-file
  concept.
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
        art/*.png              texture sources + optional <source>.toml sidecars
        data/*.unit            Unit processor sources
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
- **`Bork2d.Bake.Builder` is the new public tooling project.** It contains the builder, processor
  contracts, sidecar policy, output sink, and CLI command handling. It references the internal
  `Bork2d.Bake` implementation, which keeps Tomlyn/StbImageSharp/host generation hidden.
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
    .WithHostDirectory("obj/bork2d/bakehost")
    .WithCommonAssembly(typeof(GameConfig).Assembly)
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
- `WithCommonAssembly` supplies the data/shape assembly the generated host references. It replaces
  the descriptor's common-assembly input.
- `WithHostDirectory` replaces `[bake].host`.
- `WithTextureDefaults` replaces `[textures]`; it configures the built-in texture processor, not
  the pipeline.
- `AddProcessor` registers custom processors. The built-in texture processor is pre-registered.
- Build/CLI inputs such as the output root, mode, `--force`, `--check`, and `--verbose` remain
  command-line options because they describe the invocation, not the project.
- Builder configuration is validated before any source is read. Failure is a `BK1xxx` diagnostic
  and exit code `2`.

`MyGame.Assets` is the CLI: `bake`, `generate-host`, `validate`, `clean`, and the pack-copy command
used by `Project.Desktop`. The commands are thin wrappers over the builder; a game can run them
manually. The build target runs `bake` automatically. `generate-host` remains available for
diagnosing a stale generated host.

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
    SidecarPolicy SidecarPolicy { get; }          // None | Optional | Required
    ITypeShape? SidecarShape { get; }             // null when SidecarPolicy is None
    void Process(AssetProcessorContext context);
}

public abstract class AssetProcessor<TSidecar> : IAssetProcessor
    where TSidecar : IShapeable<TSidecar>
{
    public ITypeShape SidecarShape => TSidecar.GetTypeShape();
    public abstract SidecarPolicy SidecarPolicy { get; }   // Optional or Required
    protected abstract void Process(AssetProcessorContext context, TSidecar sidecar);
    protected virtual TSidecar CreateDefaultSidecar(AssetProcessorContext context)
        => throw new NotSupportedException("This processor has no default sidecar.");
}

public enum SidecarPolicy
{
    None,
    Optional,
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
    void AddMessagePack<T>(string assetPath, T value) where T : IShapeable<T>;
    void AddRaw(string assetPath, ReadOnlySpan<byte> payload);
}

public interface IAssetProcessorDiagnostics
{
    void Error(string code, string message);
    void Warning(string code, string message);
    void Info(string code, string message);
}
```

Key properties:

- A processor with no sidecar (`SidecarPolicy.None`) implements `IAssetProcessor` directly (or a
  non-generic base); `AssetProcessor<TSidecar>` is for `Optional`/`Required` processors and always
  exposes a sidecar shape.
- The sidecar is a normal `[GenerateShape]`/`partial` type. The framework binds it through the
  existing shape-driven `TomlShapeBinder`, using a non-generic `Bind(ITypeShape, …)` entry point.
  There is no hand-written sidecar parser, no per-processor generated binder, and no reflection
  over sidecar members.
- `AddMessagePack<T>` uses the compiled shape `T.GetTypeShape()` and the existing `PackBuilder`
  path. The output type can be a public runtime type (`TextureDescriptor`, `Unit`) or any other
  shapeable type; it does **not** need to be registered with the generated host. The host only maps
  custom-data TOML types.
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
  generated host. Processor outputs do not participate in `[bork2d]` registration.

## 8. Sidecars

### 8.1 Policies

| Policy | Missing sidecar | Existing sidecar | Default creation |
| --- | --- | --- | --- |
| `None` | Fine; no sidecar lookup | Not a sidecar; ordinary TOML rules | Never |
| `Optional` | Bind an empty document; C# defaults apply | Bind and pass to the processor | Never |
| `Required` | Ask the processor for its default sidecar, write `<source>.toml`, then bind | Bind and pass | Once, only when missing |

- `AssetProcessor<TSidecar>` exposes a protected `CreateDefaultSidecar(AssetProcessorContext)`
  hook. `Required` processors must return a valid default; the base implementation for a
  parameterless sidecar shape binds an empty document.
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
- A missing optional sidecar is not an error and produces no diagnostic.
- A sidecar with a syntax error uses the shared `BK2001` TOML syntax code.

## 9. Outputs and asset paths

- `BaseAssetPath` is the source path relative to its pack root, lowercased, with the matched
  extension removed. `art/Player.PNG` → `art/player`.
- A processor may emit zero or more outputs. Every output path must be **base-path-derived**:
  `BaseAssetPath` itself or `BaseAssetPath + "-" + suffix`, where `suffix` is one canonical segment
  of `[a-z0-9_-]+`. This prevents a processor from writing outside its source's directory or
  inventing unrelated paths. Multiple outputs are allowed (`art/player` and `art/player-raw` for
  the texture processor).
- A MessagePack output is added with `AddMessagePack<T>`; a raw output with `AddRaw`. The path must
  be canonical; the name must be base-path-derived; duplicate paths from the same source are a
  `BK7xxx` error.
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
- `TextureSidecar` is a `[GenerateShape]` type with optional `filter` and `wrap` members; defaults
  come from `WithTextureDefaults`. A missing sidecar is allowed (`Optional`).
- The processor keeps the existing decoding rules: StbImageSharp to straight-alpha RGBA8, no
  premultiplication/gamma/ICC/flipping/resampling; explicit 16-bit PNG rejection, truncation
  probing, dimension/payload checks, and `BK4xxx` diagnostics.
- It emits `TextureDescriptor` at `BaseAssetPath` and the raw RGBA8 payload at
  `BaseAssetPath + "-raw"`; `Payload` points at the raw path. This preserves the M3/M4 runtime
  contract.
- The descriptor's `filter`/`wrap` values come from the sidecar or the builder defaults. There is
  no `[textures]` table and no descriptor parser.
- `TexturePipeline`, `TextureSidecarParser`, `BakedTexture`, `TextureSourceEntry`, and the
  texture-specific `BakedAssetEntry`/`PackComposer` branches are removed or become private
  implementation details of the processor.

## 11. Test processors

### 11.1 PPM (image extension point)

- `PpmProcessor : AssetProcessor<PpmSidecar>` with extension `ppm`; optional sidecar with
  `filter`/`wrap`; simple P6 (binary) support with header comments/whitespace, max value 255,
  dimension validation, RGB→RGBA8 conversion (alpha 255).
- Emits `TextureDescriptor` + raw payload, reusing the public runtime descriptor so the test proves
  a user processor produces a loadable asset.
- Tests: extension discovery, sidecar binding/unknown key, exact RGBA bytes, descriptor/payload
  paths, collisions, unregistered extension ignored, full bake → `LoadAsset<TextureDescriptor>`.

### 11.2 Unit (custom data extension point)

- `UnitProcessor : AssetProcessor<UnitSidecar>` with extension `unit`; sidecar optional with an
  optional `name` member; JSON payload shape:

```json
{
    "Health": 50,
    "Attack": 20
}
```

- The source is `Assets/data/archer.unit`; the sidecar is `Assets/data/archer.unit.toml`. The name
  is the sidecar `name` when present, otherwise `SourceFileNameWithoutExtension` (`archer`).
- The output type is a runtime data type in the fixture common assembly:

```csharp
[GenerateShape]
public readonly partial record struct Unit(string Name, int Health, int Attack);
```

- The processor emits `AddMessagePack<Unit>(context.BaseAssetPath, new Unit(name, health, attack))`
  at `data/archer`. It uses `System.Text.Json` in the JIT assets executable; JSON parsing is
  processor-owned, not an engine format.
- Tests: default name from filename, sidecar override, JSON parse/validation, struct round-trip
  through `LoadAsset<Unit>`, and the sidecar-optional path.
- This test proves a custom processor can emit a custom `[GenerateShape]` type, not only a texture
  descriptor, and that no host registration is needed for processor outputs.

## 12. Generated host, incremental bake, and MSBuild

### 12.1 Generated host

- The generated host still exists for **custom-data TOML** types. The builder knows the common
  assembly and scans every pack root's data TOMLs for `[bork2d].type`.
- The host contains data bind/write code only. Processors are compiled into `MyGame.Assets`; their
  sidecars are bound by the tooling binder and their outputs use `PackBuilder` directly. The host
  does not know processors.
- `bake` (or `generate-host` + a host build) generates `BakeHost.csproj`/`BakeManifest.g.cs` under
  the configured host directory, builds the host, then bakes. The executable orchestrates the host
  build so the game build target runs one command. The generated host project is independent, so
  this is not a recursive build of `MyGame.Assets`.
- The host-generation state is keyed by the builder configuration hash, the common assembly hash,
  the generated-content hash, processor-registration hash (extensions/sidecar policy/sidecar type),
  and the tool version. There is no descriptor hash.

### 12.2 Incremental inputs

The manifest (`<project root>/obj/bork2d/bake/manifest.json`, schema bumped) records:

- builder configuration hash: packs/roots/output paths, host directory, common assembly, texture
  defaults, processor identities, and processor assembly hash;
- every tracked input: every `.toml` under every pack root plus every file whose suffix is in the
  registered extension set (so `.ppm`/`.unit` are tracked automatically);
- the game-side assembly closure next to the generated host;
- the host assembly hash, tool version, mode, every output pack path/hash, and aggregate counts.

The bake skips only when all fields match and every output exists with its recorded hash. Adding a
processor extension, changing a sidecar policy, changing processor code, or moving a pack
invalidates the state. `--force` bypasses both skips; `--check` validates without writing.

### 12.3 Build integration

- `MyGame.Assets` imports `build/Bork2d.Assets.targets` (name to be finalized). After `Build`, the
  target runs the freshly built executable with `bake`, passing the project root, configuration
  mode, and output root. The build fails with the canonical `BKxxxx` lines on bake errors.
- `MyGame.Assets` is the CLI, so it also supports `generate-host`, `validate`, `clean`, and a
  pack-copy command.
- `Project.Desktop` references `MyGame.Assets` **for build order only** (`ReferenceOutputAssembly`
  false) and imports a thin copy target: after `Build` it asks `MyGame.Assets` to copy its packs to
  `$(TargetDir)`; after `Publish` to `$(PublishDir)`; on `Clean` it asks the assets executable to
  clean. MSBuild never needs a pack-path list; the builder config owns the paths.
- There is no `bork2d.project.toml`, no `Bork2dBakePackPaths`, no `[bake].host`, and no
  `[textures]` table. The old `build/Bork2d.Bake.targets` descriptor properties are removed.
- The engine fixture uses `Bork2d.BakeFixture.Assets` (same pattern) so bake tests, the MSBuild
  integration test, and the AOT smoke exercise the real game flow. `Bork2d.AotSmoke` takes a
  build-only reference to it; the AOT publish still contains only the fixture data types and the
  baked packs, never the assets executable or builder.

## 13. Diagnostics

| Range | Owner |
| --- | --- |
| `BK1xxx` | Builder configuration, generated host, host loading |
| `BK2xxx` | TOML syntax/binding for data and sidecars |
| `BK3xxx` | References and baked-set validation |
| `BK4xxx` | Built-in texture decode/probe/dimensions (and PPM if it chooses to reuse them) |
| `BK5xxx` | Pack composition/write, incremental state |
| `BK6xxx` | MSBuild/host build integration |
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
- Builder, processors, `MyGame.Assets`, and the generated host are JIT tooling. They are not in the
  runtime or AOT publish graph. Game data types stay in `Project.Common` and remain AOT-safe.
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

1. **Builder / no descriptor.** Add `Bork2d.Bake.Builder` with `BakeApplicationBuilder`/`BakeApplication`,
   configuration validation, commands, and output/copy targets. Remove `ProjectDescriptor` and its
   parser from the pipeline; migrate tests and fixtures. Add `Bork2d.BakeFixture.Assets`.
2. **Processor framework.** Add the processor contract, registry/override rules, base-path output
   validation, sidecar policy, sidecar binder entry point, `BK7xxx`, and generic baked-index entries.
   Remove hardcoded extension lists from discovery/path/incremental.
3. **Texture processor.** Move the existing texture pipeline behind `TextureProcessor`, keep all
   behavior/tests, retire `TextureSidecarParser` and texture-specific index/composer branches.
4. **PPM and Unit.** Add the two test processors, fixture sources, and end-to-end tests.
5. **Build/AOT/docs.** Wire `MyGame.Assets`-style build targets, fixture assets executable, AOT
   fixture flow, and finalize these docs.

## 16. Acceptance criteria

- A game-owned `MyGame.Assets` executable configures packs, roots, host, common assembly, texture
  defaults, and processors through the builder; no `bork2d.project.toml` is read or required.
- Building `MyGame.Assets` bakes its packs automatically; `Project.Desktop` gets the packs without
  a pack-path list.
- The engine's PNG/JPG/JPEG/BMP/TGA support is a registered processor; discovery has no image
  extension list, and no texture-specific branches remain in the generic index/composer.
- A test registers a `.ppm` processor and bakes/loads a `TextureDescriptor` + exact RGBA payload.
- A test registers a `.unit` processor, reads JSON, defaults the name to the file name, honors a
  sidecar override, and loads a `Unit` struct.
- Required sidecars are created once when missing; optional sidecars do not require a file.
- A later processor registration overrides an earlier one for the same extension.
- Processor errors are `BK7xxx`; sidecar binding errors are `BK2xxx`; texture decode errors stay
  `BK4xxx`.
- Debug/Release builds, tests, format, docs link check, and the local AOT publish/run gate pass.

## 17. Implementation choices to confirm

These are naming/mechanical choices made in this design; they do not change the approved semantics
and are settled during WP1/WP2.

- Final project/API names: `Bork2d.Bake.Builder`, `BakeApplicationBuilder`, `BakeApplication`,
  `IAssetProcessor`/`AssetProcessor<TSidecar>`, `SidecarPolicy`, and `IAssetOutputSink` are proposed.
  The `MyGame.Assets` and `Bork2d.BakeFixture.Assets` names are from D-037.
- MSBuild file name/properties: `build/Bork2d.Assets.targets` and the copy target are proposed; the
  assets executable owns the pack paths, so no pack-path property is required.
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
- [Decision log](../decisions.md) — D-036/D-037 and the superseded M4 decisions.
- [NativeAOT](nativeaot.md), [Contributing](../contributing.md) — tooling boundary and gates.
