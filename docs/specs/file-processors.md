# File Processors and `MyGame.Assets` — Implementation Spec

> **Status:** **Merged into `develop` (2026-10-27, PR #8, merge commit `a98e0ab`;
> D-036/D-037/D-038/D-039/D-040).** WP1–WP5 were delivered on `task/file-processors`: the public
> `Bork2d.Bake.Builder`
> builder/processor API, explicit `RegisterAsset<T>()`, no project descriptor/generated host/CLI,
> the processor registry and sidecars, the built-in texture processor, the PPM/Unit fixture
> processors, the build-only fixture assets executable, schema-3 incremental bake, the final
> `build/Bork2d.Assets*.targets`, and the local AOT fixture flow. The locked decisions, API text, and
> semantics below remain the implementation authority; WP5 added the incremental manifest, build
> targets, AOT proof, and this as-built record. The post-review D-040 fix (owner-approved) makes
> `TextureDescriptor.Format`/`Filter`/`Wrap` non-null `Bork2d.Api.Assets` enums instead of nullable
> strings; it leaves the rest of the locked task scope intact. Local verification: Debug/Release
> zero-warning builds, 946 deterministic plus 12 opt-in/conditional tests (958 total), `dotnet format`,
> `scripts/check-docs.ps1`, `git diff --check`, and the local AOT publish/run. CI runs only on pull
> requests targeting `develop` and pushes to `develop`, so the task branch is verified by those local
> gates.

## 1. Goal

Implement a game-owned, JIT, build-time asset pipeline with:

- no `bork2d.project.toml` project descriptor;
- no generated bake host and no reflection;
- explicit data-type registration in the builder;
- extension-based asset processors with `[GenerateShape]` sidecars;
- a built-in texture processor, plus PPM and Unit acceptance processors;
- a `MyGame.Assets` executable that configures the bake, acts as the CLI, and bakes when built;
- the existing v1 pack format and runtime `IAssets` contract unchanged.

## 2. Locked decisions

| Decision | Locked behavior |
| --- | --- |
| D-036 | Processors own source extensions, a settings sidecar when required, and processing. Sidecar policies are only `None` and `Required`; `Required` has a separate `CreateDefaultSidecar`. Latest registration overrides an earlier one for one extension. Processor outputs are base-path-derived; `AddAsset<T>` supplies an instance and the engine serializes MessagePack; `AddData` is raw bytes only. |
| D-037 | `MyGame.Assets` owns assets/processors/configuration, is the CLI, and bakes on build. `Project.Common` remains the runtime data-type assembly. `Project.Desktop` takes a build-only reference and asks the assets executable to copy packs. No descriptor. |
| D-038 | The generated host, host generation/loading/ALC/manifest/state/hash, `MetadataLoadContext`, `generate-host`, and `WithCommonAssembly` are removed. Data types are registered with `RegisterAsset<T>()`; `[bork2d].type` remains the per-source selector. No reflection. |
| D-039 | This spec and the final design in [asset processors](../architecture/asset-processors.md) are the locked implementation variant; changes need a new owner decision. |
| D-040 | Owner-approved exception to D-039 (2026-10-27): `TextureDescriptor.Format`/`Filter`/`Wrap` are the non-null `Bork2d.Api.Assets` enums `TextureFormat`/`TextureFilter`/`TextureWrap` (defaults `Rgba8`/`Linear`/`Clamp`), and the builder uses the same enums. |

Default helpers:

- `UseDefaultAssets()` registers engine-provided authoring types. It currently registers
  `Bork2d.Api.Assets.SpriteAsset`.
- `UseDefaultProcessors()` registers the built-in texture processor (PNG/JPG/JPEG/BMP/TGA).
- Custom `.AddProcessor(...)` registrations follow the defaults so a custom processor can override
  an extension. A later registration always wins for the same extension.

## 3. Scope

### 3.1 In scope

- `Bork2d.Bake.Builder` public JIT project and its builder/processor API.
- Builder configuration, validation, commands, and `MyGame.Assets`/fixture assets executables.
- Explicit `RegisterAsset<T>()` front end and internal per-type bind/write registration.
- Processor registry, extension normalization/override, discovery, path mapping, outputs, and
  generic baked-index/composition.
- `None`/`Required` sidecar policies, `CreateDefaultSidecar`, and the sidecar TOML writer.
- Built-in texture processor refactor.
- PPM and Unit test processors and their fixture sources.
- Build targets, auto-bake, pack copy/clean, and no host build.
- Incremental manifest changes, diagnostics, tests, docs, and AOT proof.
- Removal of the M4 descriptor, generated host, hardcoded extension lists, and texture-specific
  generic pipeline branches.

### 3.2 Out of scope

- Runtime/API changes beyond removing host/tooling and the D-040 owner exception; `IAssets`,
  `SpriteAsset`, and the pack format are unchanged. D-040 makes
  `TextureDescriptor.Format`/`Filter`/`Wrap` non-null `Bork2d.Api.Assets` enums
  (`TextureFormat`/`TextureFilter`/`TextureWrap`, defaults `Rgba8`/`Linear`/`Clamp`); nullable
  descriptor strings are gone.
- Reflection, assembly scanning, `MetadataLoadContext`, a generated host, or a source generator.
- A TOML editor/writer beyond creating a missing `Required` sidecar.
- Multi-input processors (atlas + metadata, font + glyph list).
- Sidecar `AssetRef`/`DataRef` values.
- Editor work (M5), renderer (M6/M8), KTX2, audio, or new asset kinds.
- A NuGet/template distribution (M9).

## 4. Project and file layout

```text
src/Bork2d/
  Bork2d.Api/                    unchanged runtime contracts
  Bork2d.Assets/                 unchanged pack format/VFS/loader
  Bork2d.Bake/                   internal JIT implementation; no Hosting/, no Project/, no host
  Bork2d.Bake.Builder/           NEW public JIT builder/processor API
  Bork2d.BakeFixture/            runtime data types; add Unit
  Bork2d.BakeFixture.Assets/     NEW build-only fixture executable
  Bork2d.AotSmoke/               build-only reference to BakeFixture.Assets

MyGame.Assets/                   game-owned executable (template pattern, not in engine solution)
  MyGame.Assets.csproj
  Program.cs
  Assets/                        main pack root
  DLC1/                          optional overlay root
```

- `Bork2d.Bake.Builder` references `Bork2d.Bake`, `Bork2d.Api`, and `Bork2d.Assets`; public
  signatures expose no Tomlyn/Stb/Nerdbank/host type.
- `Bork2d.Bake` remains internal and grants `InternalsVisibleTo` to `Bork2d.Bake.Builder`,
  `Bork2d.Editor`, and `Bork2d.Tests`. The host/CLI grants are removed with the host/CLI.
- `Bork2d.BakeFixture.Assets` references `Bork2d.Bake.Builder` and `Bork2d.BakeFixture`; it is
  build-only and never enters the runtime/AOT publish graph.
- `MyGame.Assets` references `Bork2d.Bake.Builder` and `Project.Common`; it is build-time only.

## 5. Public builder API

Names are locked for this variant; exact parameter names may be adjusted without semantic change.

```csharp
namespace Bork2d.Bake.Builder;

public sealed class BakeApplicationBuilder
{
    public BakeApplicationBuilder WithProjectRoot(string path);
    public BakeApplicationBuilder WithPack(string packPath, string root);
    public BakeApplicationBuilder UseDefaultAssets();
    public BakeApplicationBuilder RegisterAsset<T>() where T : IShapeable<T>;
    public BakeApplicationBuilder UseDefaultProcessors();
    public BakeApplicationBuilder WithTextureDefaults(TextureFilter filter, TextureWrap wrap);
    public BakeApplicationBuilder AddProcessor(IAssetProcessor processor);
    public BakeApplication Build();
}

public sealed class BakeApplication
{
    public Task<int> RunAsync(string[] args);
}
```

`TextureFilter` and `TextureWrap` are the `Bork2d.Api.Assets` enums: `TextureFilter` is
`Linear` (default) or `Nearest`, and `TextureWrap` is `Clamp` (default) or `Repeat`.

Builder rules:

- `WithProjectRoot` sets the base for relative roots, pack paths, and diagnostic display paths. The
  build target passes `$(MSBuildProjectDirectory)`; a manual CLI may pass `--project-root`.
- `WithPack(packPath, root)` is ordered; the first pack is main and later packs are DLC/overlay.
  At least one pack is required. Duplicate roots or pack paths are errors. Paths are relative,
  canonical-safe, and cannot escape the project root.
- There is **no** `WithCommonAssembly` and **no** host-directory option. The builder derives the
  shape-assembly set from `RegisterAsset<T>()` registrations and the assets executable's non-tooling
  dependency closure.
- `RegisterAsset<T>()` is the only data-type registration. The full name is
  `typeof(T).FullName`; duplicates are errors. `T` must be `IShapeable<T>` (a compiled
  `[GenerateShape]`).
- `UseDefaultAssets()` registers `SpriteAsset`. It is idempotent; a later explicit duplicate of a
  default type is an error.
- `UseDefaultProcessors()` registers the built-in texture processor. It is idempotent.
- `AddProcessor` registers a custom instance; a later registration overrides an earlier one for
  the same extension, with an `Info` diagnostic. Call defaults before custom overrides.
- `WithTextureDefaults` supplies `filter`/`wrap` for the built-in texture processor and its default
  sidecars.
- All configuration is validated before any source is read. Failure is `BK1xxx`, exit code `2`.

## 6. Public processor API

```csharp
public interface IAssetProcessor
{
    string Name { get; }
    IReadOnlyList<string> Extensions { get; }
    SidecarPolicy Policy { get; }
    ITypeShape? SidecarShape { get; }
    void Process(AssetProcessorContext context);
}

public abstract class AssetProcessor<TSidecar> : IAssetProcessor
    where TSidecar : IShapeable<TSidecar>
{
    public ITypeShape SidecarShape => TSidecar.GetTypeShape();
    public SidecarPolicy Policy => SidecarPolicy.Required;

    // Separate from Process; called only when the required sidecar is missing.
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
    public string SourcePath { get; }
    public string RelativePath { get; }
    public string BaseAssetPath { get; }
    public string SourceFileName { get; }
    public string SourceFileNameWithoutExtension { get; }
    public IAssetOutputSink Outputs { get; }
    public IAssetProcessorDiagnostics Diagnostics { get; }
}

public interface IAssetOutputSink
{
    void AddAsset<T>(string assetPath, T value) where T : IShapeable<T>;
    void AddData(string assetPath, ReadOnlySpan<byte> payload);
}

public interface IAssetProcessorDiagnostics
{
    void Error(string code, string message);
    void Warning(string code, string message);
    void Info(string code, string message);
}
```

Semantics:

- `None` processors implement `IAssetProcessor` directly; they have no sidecar.
- `Required` processors use `AssetProcessor<TSidecar>`. `CreateDefaultSidecar` is invoked only when
  `<source>.toml` is missing; the builder writes the returned object, binds it, then calls
  `Process`. It is never called when the file exists.
- `AddAsset<T>` takes an instance; the engine serializes it with the compiled shape. The processor
  never serializes MessagePack, sees MessagePack bytes, or calls `PackBuilder`. `AddData` is only
  for opaque bytes.
- A processor may emit any number of outputs. A processor that reports an error has all of its
  outputs for that source discarded. An exception is caught and reported as `BK7xxx`.
- Processors run sequentially on the bake thread. A processor instance may be reused across
  sources; it must not retain per-source state between calls.

## 7. Data-type registration and `[bork2d].type`

Data TOML keeps the reserved table:

```toml
[bork2d]
type = "MyGame.Data.GameConfig"
```

- The builder stores a dictionary from `typeof(T).FullName` to a typed registration that closes
  over `TomlShapeBinder.Bind<T>` and `PackBuilder.AddMessagePack<T>`.
- `[bork2d].type` selects one registered entry. It is required and must be a full CLR name without
  assembly qualification.
- An unregistered type is a `BK1xxx` error at the `type` value with a `RegisterAsset<T>()` hint.
- There is no reflection, no type lookup by assembly, no generated host, and no per-file path
  mapping. The source remains self-describing while the builder owns the allowed type set.
- A registered internal type needs `InternalsVisibleTo` to the assets executable.
- `UseDefaultAssets()` registers `SpriteAsset`; `TextureDescriptor` is processor output, not
  authoring data.

## 8. Discovery, paths, and outputs

For each pack root, in builder order:

```text
for each file (sorted, reparse points skipped):
    .toml:
        typed [bork2d]                  -> custom-data source
        untyped <registered-source>.toml -> sidecar for that source
        otherwise                       -> missing-type error
    suffix in processor registry         -> processor source
    otherwise                            -> ignored
```

- Extensions are normalized to lowercase without a leading dot. Both `"png"` and `".PNG"` are
  accepted. Matching uses the longest registered compound suffix. Case-insensitive.
- A later processor registration overrides an earlier one for the same extension; report `Info`.
- Unregistered files are ignored.
- A sidecar is `<source file>.toml`. Filename decides classification; a `[bork2d]` table inside a
  sidecar is an unknown sidecar key (`BK2xxx`), not data.
- `BaseAssetPath` is the source path relative to its pack root, lowercased, with the matched
  extension removed. Outputs must be `BaseAssetPath` or `BaseAssetPath + "-" + suffix` where suffix
  is one canonical `[a-z0-9_-]+` segment.
- A processor may emit any number of outputs. Duplicate paths from one source are `BK7xxx`;
  collisions across sources/data/processors are `BK3005`.
- Paths are added to the pack whose root contains the source. DLC rules are unchanged: main is
  strict, DLC may reference local or main, DLC→DLC is an error, cross-pack duplicates are overlay
  legal.

## 9. Sidecars

| Policy | Missing sidecar | Existing sidecar |
| --- | --- | --- |
| `None` | No sidecar; `<source>.toml` follows ordinary TOML rules | Not a sidecar |
| `Required` | `CreateDefaultSidecar`, write `<source>.toml`, bind, process | Bind and process |

- No `Optional` policy. A processor that needs settings uses `Required`; a processor with no
  settings uses `None`.
- Default sidecar writing is deterministic, canonical TOML (camelCase keys, LF, UTF-8 without BOM),
  never overwrites an existing file, and reports `Info` with the path. Failure is `BK7xxx`.
- Sidecars are settings-only; `AssetRef`/`DataRef` members are unsupported in this task.
- Sidecar binding uses the shared shape-driven TOML binder and `BK2xxx` codes: camelCase,
  `[JsonPropertyName]`, `[JsonRequired]`, unknown-key errors, enum/date/range rules, and value
  spans. A sidecar syntax error is `BK2001`.

## 10. Built-in texture processor

- `TextureProcessor : AssetProcessor<TextureSidecar>`, extensions `png`, `jpg`, `jpeg`, `bmp`,
  `tga`.
- `TextureSidecar` is a `[GenerateShape]` type with `filter`/`wrap` members typed as the
  `Bork2d.Api.Assets` `TextureFilter`/`TextureWrap` enums.
- `CreateDefaultSidecar` returns the builder's `WithTextureDefaults` values. A texture without a
  sidecar gets one created on the first bake.
- Decode with StbImageSharp to straight-alpha RGBA8; no premultiplication/gamma/ICC/flipping/
  resampling. Keep explicit 16-bit PNG rejection, truncation probing, dimension/payload checks, and
  `BK4xxx` diagnostics.
- Emit a `TextureDescriptor` instance at `BaseAssetPath` and raw RGBA8 at
  `BaseAssetPath + "-raw"`. `Payload` points at the raw path.
- Remove `TexturePipeline` as a separate pipeline, `TextureSidecarParser`, `BakedTexture`,
  `TextureSourceEntry`, and texture-specific `BakedAssetEntry`/`PackComposer` branches. Their
  logic moves behind the processor and generic entries.

## 11. Acceptance processors

### 11.1 PPM

- `PpmProcessor : AssetProcessor<PpmSidecar>`, extension `ppm`, `Required` sidecar with
  `filter`/`wrap`; default sidecar from texture defaults.
- Simple P6 binary support: header comments/whitespace, max value 255, positive dimensions,
  exact payload length, RGB→RGBA8 (alpha 255); invalid input is `BK7xxx`.
- Emit `TextureDescriptor` + raw payload; reuse the public runtime descriptor.
- Tests cover discovery, default sidecar creation, sidecar binding/unknown key, exact bytes,
  descriptor/payload paths, collisions, unregistered extension ignored, and
  `LoadAsset<TextureDescriptor>`.

### 11.2 Unit

- `UnitProcessor : AssetProcessor<UnitSidecar>`, extension `unit`, `Required` sidecar with an
  optional `name` member.
- Default sidecar: `new UnitSidecar { Name = context.SourceFileNameWithoutExtension }`.
- JSON source:

```json
{
    "Health": 50,
    "Attack": 20
}
```

- Parse `Health`/`Attack` as required integers. Invalid JSON/shape is `BK7xxx`.
- Name is the sidecar `name` when non-empty, otherwise `SourceFileNameWithoutExtension`.
- Emit `context.Outputs.AddAsset(context.BaseAssetPath, new Unit(name, health, attack))`.
- `Unit` is a runtime type in `Bork2d.BakeFixture`/game common assembly:

```csharp
[GenerateShape]
public readonly partial record struct Unit(string Name, int Health, int Attack);
```

- Tests cover default-sidecar creation (`name = "archer"` from `archer.unit`), sidecar override,
  JSON validation, and `LoadAsset<Unit>`.

## 12. Baked index, composition, and references

- `BakedAssetEntry` becomes generic over `CustomData`, `ProcessorAsset`, and `ProcessorData`.
- A processor asset entry stores the engine write delegate
  (`builder.AddMessagePack<T>(path, value)`); a data entry stores bytes.
- `PackComposer` has no texture-specific branch. Custom data still uses its registered typed
  writer; processor assets use their registration delegate; raw entries use `AddRaw`.
- References in processor output are collected by the existing shape walk. Data references come
  from binding. Validation, `BK3xxx` codes, and `PackBuilder` backstop semantics are unchanged.
- Cross-pack and collision rules are unchanged: `BK3005` inside one pack; main/DLC reference rules
  as D-M4-14.

## 13. Build, CLI, and MSBuild

- `MyGame.Assets` imports `build/Bork2d.Assets.targets` (final name may change). `AfterTargets=Build`
  runs the built executable's `bake` with project root, configuration/mode, and output root. Bake
  errors fail the build with canonical `BKxxxx` lines.
- CLI commands:

```text
MyGame.Assets bake          [--project-root <dir>] [--output-root <dir>]
                            [--mode debug|release] [--check] [--force] [--verbose|--quiet]
MyGame.Assets validate      [--project-root <dir>]
MyGame.Assets clean         [--project-root <dir>]
MyGame.Assets copy-packs    --output-root <dir>
MyGame.Assets --help | --version
```

- No `generate-host` command. No host project, host build, or host state.
- Exit codes: `0` success/up to date, `1` source diagnostics, `2` usage/configuration error,
  `3` unexpected internal failure.
- `Project.Desktop` takes a build-only reference to `MyGame.Assets` and asks it to copy packs to
  `$(TargetDir)` after `Build` and `$(PublishDir)` after `Publish`; `Clean` delegates. MSBuild has
  no pack-path property.
- `Bork2d.BakeFixture.Assets` follows the same pattern for tests and the AOT smoke.
- `Bork2d.Bake.Cli` is deleted/retired; no game-facing engine CLI remains.

## 14. Incremental bake

- Manifest: `<project root>/obj/bork2d/bake/manifest.json`, schema 3 (bump from M4 schema 2).
- Records:
  - builder configuration hash: project root, every pack (index, pack path, relative root, absolute
    root), texture defaults, sorted registered type names with their assembly simple names, and
    processor identities (registration order, name, CLR type, policy, sorted normalized extensions);
  - sorted tracked inputs: every `.toml` plus every file whose suffix is in the registered
    extension set, with a content hash;
  - derived shape-assembly closure hash (registered data-type and processor assemblies plus their
    non-tooling, non-framework referenced assemblies);
  - tool version, mode, every output path/hash in pack order, and aggregate counts.
- Skip only when every field matches and every output exists with its recorded hash. No per-pack
  cache. `--force` bypasses; `--check` never skips and writes nothing.
- A full bake re-collects the tracked inputs after processing so a `Required` sidecar created during
  that bake is recorded immediately; the next bake skips instead of running one extra full bake.
- A changed registration, processor, extension set, sidecar policy, source, pack path, common
  assembly, tool, or mode invalidates the bake. No host-generation state exists.

## 15. Diagnostics

| Range | Owner |
| --- | --- |
| `BK1xxx` | Builder configuration and explicit type registration |
| `BK2xxx` | TOML syntax/binding for data and sidecars |
| `BK3xxx` | References and baked-set validation |
| `BK4xxx` | Built-in texture decode/probe/dimensions |
| `BK5xxx` | Pack composition/write, incremental state |
| `BK6xxx` | MSBuild/build integration |
| `BK7xxx` | Processor registration/processing |

- Processor framework codes (WP2): `BK7001` source read/parse failure, `BK7002` invalid processor
  input, `BK7003` processor exception, `BK7004` invalid output path, `BK7005` duplicate output,
  `BK7006` required-sidecar creation failure, `BK7007` invalid processor registration. User
  processors use `BK7xxx` and include their processor name; tests assert the range and message
  context, not per-processor custom codes beyond these framework ones.
- Sidecar binding errors are `BK2xxx` with spans. Texture decode/probe remains `BK4xxx`.
- Sorted deterministically by file, line, column, code, message.

## 16. Determinism, threading, AOT, security

- Deterministic discovery, registry order (latest override), output path ordering, sidecar default
  TOML, diagnostics, and entry table.
- No whole-pack byte identity guarantee (D-M3-08/D-M4-07); compare entry tables and decompressed
  payloads.
- Single-threaded bake; processors are synchronous and reusable but not concurrent. No per-source
  parallelism in this task.
- Builder/processors/assets executable are JIT and outside runtime/AOT. No generated host, no
  reflection, no `MetadataLoadContext`. Runtime data types stay in the game common assembly.
- Registered data types must be public or IVT to the assets executable.
- Processors are trusted build-time code; output paths are validated against `BaseAssetPath`.
  Reparse points are skipped. Sidecar creation writes only next to the source and only when absent.

## 17. Migration and removals

Remove/retire:

- `bork2d.project.toml`, `ProjectDescriptor`, descriptor parser/model, descriptor diagnostics;
- `Bork2d.Bake/Hosting/`, `Project/`, host generator/loader/ALC/manifest/state, host package
  `System.Reflection.MetadataLoadContext`;
- `Bork2d.Bake.Cli` and `generate-host`;
- hardcoded image extension lists in discovery/path/incremental;
- `TexturePipeline`, `TextureSidecarParser`, `BakedTexture`, `TextureSourceEntry`,
  `TextureImportSettings` as a separate model, and texture-specific index/composer branches;
- `WithCommonAssembly`, `WithHostDirectory`, `[bake].host`, `[textures]`, `Bork2dBakePackPaths`,
  and descriptor MSBuild properties;
- `Optional` sidecar policy.

Add/replace:

- `Bork2d.Bake.Builder` public project and builder/processor API;
- explicit type registration and default helpers;
- processor registry, `CreateDefaultSidecar`, sidecar TOML writer;
- generic baked-index/composition;
- `MyGame.Assets`/`Bork2d.BakeFixture.Assets` pattern and build targets;
- PPM/Unit test processors and fixture sources; `Unit` in the fixture common assembly;
- new incremental manifest schema 3.

## 18. Work packages

Implementation was delivered on `task/file-processors`; each WP was a reviewable commit series. The
task is merged into `develop` (PR #8, merge commit `a98e0ab`). WP boundaries:

| WP | Scope | Depends | Acceptance |
| --- | --- | --- | --- |
| WP1 | Builder/no descriptor/no host/public surface: add `Bork2d.Bake.Builder`, config validation, commands, explicit `RegisterAsset<T>()`, `UseDefaultAssets`/`UseDefaultProcessors`, remove descriptor/host/MetadataLoadContext/CLI, migrate tests/fixtures. | — | Builder config tests pass; no descriptor/host code remains in the target; solution builds. |
| WP2 | Processor framework: contracts, registry/override, sidecar policies/default writer, discovery/path, generic index/composition, `BK7xxx`; remove hardcoded extensions and texture-specific generic branches. | WP1 | Processor unit tests pass; discovery/override/sidecar/output tests pass. |
| WP3 | Texture processor refactor: move decode/probe/descriptor/raw behind `TextureProcessor`; delete the old pipeline/parser; keep texture tests green. | WP2 | Existing texture behavior + sidecar tests pass; no texture-specific generic branch. |
| WP4 | Fixture assets + PPM/Unit: `Bork2d.BakeFixture.Assets`, PPM/Unit processors, fixture sources, `Unit` type, end-to-end bake/load tests. | WP2, WP3 | PPM/Unit acceptance tests pass through builder/executable, including default sidecars. |
| WP5 | Build/incremental/AOT/docs: assets targets, auto-bake/copy/clean, manifest schema 3, AOT fixture flow, docs/decision updates, final gates. | WP1–WP4 | Debug/Release/test/format/docs/AOT gates pass; no host/builder/processor assembly in AOT publish. |

## 19. Test plan

Required tests:

- Builder: valid config; missing pack; duplicate root/pack; escaping paths; duplicate asset type;
  unregistered `[bork2d].type`; default asset/processor registration; no `WithCommonAssembly`.
- Processor registry: `"png"` vs `".PNG"`, compound suffix, case-insensitive, latest override,
  unregistered file ignored, invalid registration.
- Discovery/paths: typed data, sidecar filename classification, `[bork2d]` in sidecar error,
  missing type, orphan sidecar, canonical base path, base-path-derived outputs.
- Sidecars: `None`, `Required` existing, `Required` created deterministically, no overwrite,
  unknown key/type/value, syntax error, settings-only.
- Processor outputs: any number, `AddAsset<T>` instance + engine serialization, `AddData` raw,
  no `PackBuilder` in processor, invalid/duplicate path, exception/error discards outputs.
- Texture: existing decode/probe/dimension/payload matrix, default sidecar creation, descriptor +
  raw paths, collision.
- PPM: P6 valid/invalid, default sidecar, exact RGBA bytes, descriptor load.
- Unit: default name, sidecar override, JSON invalid, `Unit` load.
- Baked index/references: data + processor + raw, `BK3005`, main/DLC scopes, missing/kind/type
  references, DLC→DLC error.
- Incremental: skip, changed registration, changed processor/extension, changed source/pack,
  missing output, `--force`, `--check`, schema 3, no host state.
- Build: `MyGame.Assets`-style build auto-bake, `Project.Desktop` copy, publish/clean, no
  descriptor/host properties.
- AOT: fixture packs load; publish contains no Bake/Builder/Tomlyn/Stb/host/assets executable.
- Docs: link check, no stale descriptor/host instructions.

Local gates (per AGENTS §7):

```powershell
dotnet build src/Bork2d/Bork2d.slnx -c Debug
dotnet build src/Bork2d/Bork2d.slnx -c Release
dotnet test src/Bork2d/Bork2d.slnx -c Debug --no-build
dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
pwsh scripts/check-docs.ps1
git diff --check
# AOT publish/run gate when runtime-affecting (fixture flow)
```

## 20. Acceptance criteria

- No `bork2d.project.toml`, `ProjectDescriptor`, generated host, host state/ALC/manifest,
  `MetadataLoadContext`, `generate-host`, `WithCommonAssembly`, or `WithHostDirectory` remains in
  the target flow.
- `MyGame.Assets` builds and bakes automatically; `Project.Desktop` copies packs without a
  pack-path list.
- Data types are explicitly registered; `[bork2d].type` selects a registered type; unregistered
  types fail with `BK1xxx`; no reflection.
- Engine texture support is a `Required`-sidecar processor; PPM and Unit pass end-to-end.
- `AddAsset<T>` is instance-only and engine-serialized; `AddData` is raw only; any number of
  outputs is supported.
- `None`/`Required` sidecars only; `CreateDefaultSidecar` runs only when missing; no `Optional`.
- No hardcoded source-extension list remains; override/latest and compound/case rules are tested.
- Incremental manifest schema 3 and skip/force behavior are correct without host state.
- Debug/Release builds, tests, format, docs, and local AOT gate pass.

## 21. Risks and open implementation details

- `Bork2d.Bake.Builder` public surface names are locked semantically; parameter names and result
  types may be adjusted during WP1 without changing behavior.
- MSBuild target file/property names are not locked; the assets executable owns pack paths.
- The default-sidecar TOML writer may be a small canonical writer or Tomlyn-based; it must be
  deterministic and must not overwrite.
- `Unit` lives in `Bork2d.BakeFixture`/game common assembly; a processor that emits a type living
  only in the assets executable would bake but fail runtime load.
- Removing the host simplifies the build but requires public/IVT data types; this is intentional.
- If a later need pulls in arbitrary path outputs or sidecar references, it needs a new decision.

## 22. Related documents

- [Asset processors and `MyGame.Assets`](../architecture/asset-processors.md) — architecture design.
- [Assets and tooling](../architecture/assets-and-tooling.md) — pack/VFS and target pipeline.
- [Projects and dependencies](../architecture/projects.md) — project graph and `MyGame.Assets`.
- [API guide](../api/guide.md), [API reference](../api/reference.md) — builder/processor examples.
- [Decision log](../decisions.md) — D-036/D-037/D-038/D-039/D-040.
- [M4 spec](m4-serialization-and-bake.md) — historical M4 record.
