# M4 — Serialization and Bake (spec)

> **Status:** planned; **not implemented**. The owner decisions D-M4-01…D-M4-12 in §2 were
> approved during the M4 spec review on 2026-10-26. The questions in §18 are **open** and block the
> work packages listed against them. This is the first forward-looking milestone spec; the M1–M3
> files under [`docs/specs/`](.) are historical records. When this spec and another document
> disagree, this spec wins for M4 scope until it is implemented; the accepted architecture and API
> documents win for inherited contracts.

## 1. Goal

Turn authoring sources into the runtime artifacts that M3 consumes:

- a project descriptor (`bork2d.project.toml`) that defines the asset root, output pack, and
  path-to-type mapping;
- a generated, reflection-free bake host compiled from the registrations in `Project.Common`;
- custom data TOML → MessagePack, using the same generated shapes the NativeAOT runtime uses;
- texture sources (PNG/JPG/BMP/TGA) plus import sidecars → a `TextureDescriptor` and a raw RGBA8
  payload;
- typed reference validation with `file:line:column` diagnostics;
- structural determinism, a hash-based incremental bake, and an MSBuild target;
- a NativeAOT proof that loads a pack produced by the baker, with no TOML or reflection in the
  runtime;
- **no change to the v1 pack format**; the only runtime change is the D-M4-11 typed-load
  relaxation for struct roots, which does not change the loader's deserialize-only behavior.

M4 deliberately stops before the editor and slicing (M5), the renderer (M6/M8), and scene
serialization (not planned). `SpriteAsset` is not a special pipeline: it is baked exactly like
user-defined custom data (§7.5).

## 2. Owner-approved decisions

These were selected by the owner in the M4 spec review. They are the scope authority for M4; the
formal decision-log entries are added when the corresponding work package lands (the decision log
is not edited by this spec).

| ID | Decision | Owner-approved choice |
| --- | --- | --- |
| D-M4-01 | Bake tooling shape | New JIT `Bork2d.Bake` library plus a `Bork2d.Bake.Cli` console. The M5 editor reuses the library; MSBuild invokes the CLI. |
| D-M4-02 | Type mapping | The manifest is generated from `Project.Common` metadata and compiled into a **generated bake host** that references `Project.Common` and `Bork2d.Bake`. The host calls typed, generated bind/write code. No runtime reflection over game members, and `Project.Common` stays `Bork2d.Api`-only. |
| D-M4-03 | Type registration | **Superseded by D-M4-12.** Registration uses PolyType `[GenerateShape]` directly; no Bork2d attribute or generator. |
| D-M4-04 | TOML library | Tomlyn 2.x (pin **2.10.1**), TOML 1.1.0. The tooling uses Tomlyn's POCO mapping; naming/required/ignore overrides use BCL `System.Text.Json.Serialization` attributes (`[JsonPropertyName]`, `[JsonRequired]`, `[JsonIgnore]`), and Bork2d converters cover references, vectors, colors, and enums. |
| D-M4-05 | Texture sources | PNG, JPG/JPEG, BMP, and TGA via **StbImageSharp 2.30.16**, normalized to straight-alpha RGBA8. |
| D-M4-06 | Sprite scope | `SpriteAsset` is ordinary custom data. M4 has **no** sprite source schema, no slicing, and no UV/pivot/scale/topology/indices validation; those are M5. `SpriteAsset` baking is still tested. |
| D-M4-07 | Determinism | Structural only: stable ordering, entry table, offsets, and identical baked payload bytes for identical inputs. Whole-pack byte identity is not guaranteed (D-M3-08 stays in force). |
| D-M4-08 | Build integration | A `.targets` file runs the CLI on build; unchanged inputs skip as a whole; any change re-bakes; the pack is written atomically. |
| D-M4-09 | AOT proof | The AOT build bakes a fixture pack and the smoke loads it. `Bake`, `Bake.Cli`, and the host generator are not in the AOT publish graph. |
| D-M4-10 | `Color` | **Superseded by D-M4-12.** `Color` uses a marshaler; it does not get `[GenerateShape]` and does not become `partial`. |
| D-M4-11 | Struct roots | A top-level asset root may be a `readonly record struct` (or any struct). M4 relaxes `IAssets.LoadAsset<T>` (both overloads) and `AssetRef<TAsset>`/`AssetRefMarshaler<TAsset>` from `where T : class, IShapeable<T>` to `where T : IShapeable<T>`, adjusts the deserializer (no null return for value types; a MessagePack `nil` is a `Decode` fault), and updates docs/tests. This supersedes the `class` part of D-M3-22 and is a public API change approved under D-014. |
| D-M4-12 | Registration, `partial`, and `Color` | PolyType `[GenerateShape]` is the single shape attribute; the descriptor is the registration list; there is no `Bork2dSerializableAttribute` and no `Bork2d.Bake.Generators`. A `[GenerateShape]` root must be `partial` because PolyType injects `IShapeable<T>` through a partial declaration; this is a PolyType rule already applied to `SpriteAsset`, `TextureDescriptor`, and `MessagePackSmokeData`. `Color` gets a `[TypeShape(Marshaler = ...)]`/`ColorMarshaler` and stays non-`partial`. Supersedes D-M4-03 and D-M4-10. |

**Scope delta approved with D-M4-06.** The roadmap's M4 bullet currently includes sprite TOML,
slicing-adjacent validation, and sprite field validation. M4 bakes `SpriteAsset` as data only. The
roadmap bullet, the [assets and tooling](../architecture/assets-and-tooling.md#9-sprite-assets)
wording, and the D-035 sentence "field validation belongs to the M4 baker/editor" are updated in
the M4 documentation work package to say that sprite field validation and slicing are M5.

**Public API delta approved with D-M4-11.** M4 is otherwise a tooling milestone, but struct roots
require relaxing the `class` constraint on the typed load path. The change is limited to removing
`class` (no new members, no new types), adding value-type semantics, and updating docs/tests. It
does not change the pack format, the loader's deserialize-only behavior, or `IAssets`' other
members.

**Registration delta approved with D-M4-12.** The Bork2d-specific attribute and Roslyn generator
are dropped. Data types use PolyType's `[GenerateShape]` directly and stay `partial`; the
`partial` keyword is required by PolyType to inject `IShapeable<T>` and is already used by
`SpriteAsset`, `TextureDescriptor`, and `MessagePackSmokeData`. The descriptor is the only
registration list. `Color` uses a marshaler and stays non-`partial`.

## 3. Inherited constraints (facts, not new decisions)

- **Pack format.** v1 is unchanged: 28-byte prefix, always-Zstd MessagePack header with XXH3-64,
  `uint64` offsets, `uint32` lengths/sizes, later-wins overlays, no unload, no policy size caps.
  `PackBuilder` remains the only writer. See
  [assets and tooling §4](../architecture/assets-and-tooling.md#4-pack-binary-format).
- **Runtime loader.** `LoadAsset<T>` stays deserialize-only; it does not validate fields, follow
  references, or cache. See [reference §7](../api/reference.md#7-iassets). D-M4-11 removes only the
  `class` constraint so value-type roots can use the same path; a `nil` payload for a value type is
  a `Decode` fault, never a silent default.
- **Typed load contract.** `IAssets.LoadAsset<T>` (both overloads), `AssetRef<TAsset>`, and
  `AssetRefMarshaler<TAsset>` become `where T : IShapeable<T>`; `PackBuilder.AddMessagePack<T>` and
  the loader body already use that shape-only constraint. Existing class-based call sites are
  unaffected.
- **Shape generation.** PolyType's `[GenerateShape]` is the only shape source. Because it injects
  `IShapeable<T>` through a partial declaration on .NET 8+, every registered root is `partial`.
  There is no Bork2d source generator, no runtime reflection resolver, and no second attribute.
- **Paths.** Canonical lowercase `/` keys without source extensions; the baker strips source
  extensions and the `.toml` sidecar suffix. The same rules apply to every new discovery pass; do
  not add a second path grammar.
- **References.** `AssetRef<T>`/`DataRef` stay typed canonical paths serialized as strings; `null`
  is absent. `PackBuilder` validates references at pack build. The baker is expected to produce
  source-level diagnostics; the pack builder is a backstop, not the user-facing validator.
- **AOT.** Runtime projects are NativeAOT/trim-safe and contain no TOML, reflection resolver, or
  dynamic code. Build-time tools may reflect; see
  [NativeAOT §5](../architecture/nativeaot.md#5-editor-and-tooling-boundary).
- **Quality gates.** Warnings-as-errors, style-as-errors, `dotnet format`, the docs link check, and
  the local AOT publish/run gate apply. Tests must not commit binary fixtures; pack/source fixtures
  are generated in temp directories (see [Contributing §6](../contributing.md#6-tests-and-ci)).
- **Environment.** SDK `11.0.100-rc.1`, `net11.0`, Windows x64; tooling is JIT and does not ship
  with the game.

## 4. Scope

### 4.1 In scope

- New JIT tooling projects, the generated host contract, and the host generator.
- `bork2d.project.toml` parsing and validation.
- PolyType `[GenerateShape]` as the single shape attribute (the descriptor is the registration
  list; no Bork2d attribute or source generator); a `Color` marshaler that keeps `Color`
  non-`partial`.
- The typed-load constraint relaxation for struct roots (D-M4-11), including value-type decode
  semantics and the API docs/guide/tests that describe it.
- Custom data TOML → MessagePack: mapping rules, required/unknown handling, references, file/line
  diagnostics, and `SpriteAsset` as data.
- Texture import: discovery, sidecars, decoding (PNG/JPG/BMP/TGA), RGBA8 normalization, descriptor
  and raw payload emission, validation.
- Typed reference validation across the baked asset set.
- Structural determinism and repeatable ordering.
- Whole-project hash-based incremental bake, atomic pack replacement, and an MSBuild target.
- CLI commands, exit codes, and canonical diagnostic rendering.
- An AOT proof that loads a baked fixture pack.
- Tests, docs, dependency-matrix updates, and the AOT-gate documentation change.

### 4.2 Out of scope

- Sprite slicing, the sprite source schema, pixel rects, UV/pivot/scale/topology/indices
  validation, and any sprite-specific pipeline (M5, D-M4-06).
- The Avalonia editor, property grid, preview, and TOML writing/comment preservation (M5).
- Renderer, GPU texture upload, KTX2, color-space management, mipmaps, atlases, animation,
  tilemaps, materials, audio, UI, physics (later milestones or never for v1).
- Scene/prefab serialization, save games, hot reload, file watching, and live re-bake.
- Pack format changes, pack unload, multiple packs in one bake, or overlay generation.
- Multi-project workspaces, per-asset incremental caching, parallel baking, and NuGet/dotnet-tool
  packaging (M9).
- Public runtime asset API changes beyond the D-M4-11 typed-load relaxation and the `Color`
  marshaler.
- Linux/macOS guarantees and a public bake API for game code.

## 5. Architecture

### 5.1 End-to-end pipeline

```text
Project.Common/Assets/
  data/**.toml            custom data (registered types)
  sprites/**.toml         SpriteAsset as ordinary custom data (no slicing)
  textures/*.png|jpg|bmp|tga (+ <file>.toml sidecar)
        │
        │  1. PolyType generator: [GenerateShape] + partial → IShapeable<T>
        │  2. generate-host: descriptor + Project.Common metadata → generated host source
        │  3. MSBuild builds the host (references Project.Common + Bork2d.Bake)
        │  4. bake: Tomlyn → typed objects → generated-shape MessagePack
        │           textures → RGBA8 → TextureDescriptor + raw payload
        │           reference validation (file:line:col)
        ▼
  PackBuilder (unchanged v1 writer) → assets.pck
        │
        ▼
  IAssets / LoadAsset<T> on the NativeAOT runtime (unchanged)
```

### 5.2 Components and boundary

| Component | Kind | Responsibility | References |
| --- | --- | --- | --- |
| `Bork2d.Api` | runtime contract | No new registration attribute; `Color` gets a marshaler; existing asset types unchanged. | BCL, Logging.Abstractions, PolyType |
| `Bork2d.Bake` | JIT library (tooling) | Descriptor model/parser, source discovery, Tomlyn infrastructure and converters, texture pipeline, reference validation, diagnostics, incremental manifest, pack orchestration, generated-host contract and loader, host source generation. | `Api`, `Assets`, Tomlyn, StbImageSharp |
| `Bork2d.Bake.Cli` | JIT console (tooling) | `generate-host`, `bake`, `validate`; argument validation, exit codes, rendering. | `Bake` |
| Generated bake host | generated library, **not** a solution project | Static manifest: type registrations, TOML binders, pack-add delegates, reference key paths. Loaded by the CLI. | `Project.Common`, `Api`, `Assets`, `Bake`, Tomlyn |
| `Bork2d.Bake.targets` | MSBuild | Host generation, host build, bake invocation, clean; forwards configuration and properties. | — |
| `Bork2d.BakeFixture` (proposed, Q-16) | test/AOT fixture | A minimal `Project.Common`-like library with registered types and an `Assets/` tree, used by integration tests and the AOT bake proof. | `Api` |

Rules:

- No runtime project references `Bake`, `Bake.Cli`, Tomlyn, StbImageSharp, or Nerdbank tooling.
  The dependency test proves it.
- `Project.Common` references `Bork2d.Api` only; its data types carry PolyType `[GenerateShape]`
  and are `partial`, so the shape is compiled into `Project.Common` by PolyType's generator. There
  is no Bork2d analyzer project. PolyType is already a transitive dependency of `Bork2d.Api`; if
  the source generator does not flow transitively, `Project.Common` adds a direct, exactly pinned
  PolyType `1.4.1` package reference (WP1 verifies; no version drift).
- The generated host is the only assembly that knows the concrete registered types. It is produced
  in `obj/bork2d/bakehost/` and is deleted by clean.
- The pack writer remains `Bork2d.Assets.Pack.PackBuilder`; the baker does not reimplement the
  format.

### 5.3 Generated-host flow

1. `Project.Common` builds. Every mapped type carries `[GenerateShape]` and is `partial`; PolyType's
   generator has emitted the `IShapeable<T>` implementation into `Project.Common`.
2. The MSBuild target runs `bork2d-bake generate-host --project … --common <Project.Common.dll>
   --output obj/bork2d/bakehost --configuration <Debug|Release>`.
3. The generator parses the descriptor, reads `Project.Common` (and referenced `Bork2d.Api`)
   metadata at build time, validates every mapped type implements `IShapeable<T>` (the
   `[GenerateShape]` attribute itself is `[Conditional("NEVER")]` and is not in metadata), and
   writes:
   - `BakeHost.csproj` — `net11.0`, library, `AssemblyName=Bork2d.BakeHost`, references
     `Project.Common`, `Bork2d.Api`, `Bork2d.Assets`, `Bork2d.Bake`, and Tomlyn (transitively);
   - `BakeManifest.g.cs` — the static manifest;
   - `BakeTomlContext.g.cs` — the Tomlyn registrations, if that mechanism is selected (Q-07).
4. MSBuild builds `BakeHost.csproj` into the host output directory.
5. The MSBuild target runs `bork2d-bake bake --project … --host <Bork2d.BakeHost.dll>`.
6. The CLI loads the host, uses the manifest, bakes, validates, and writes the pack.

The manifest contract (visibility is Q-02) must expose at least:

```csharp
// Tooling-only; exact names are decided with Q-02.
internal interface IBakeManifest
{
    IReadOnlyList<BakeTypeRegistration> Types { get; }
    object Bind(string typeName, string toml, out IReadOnlyList<BakeReferenceSite> references);
    void AddToPack(PackBuilder builder, string typeName, string assetPath, object value);
}

internal readonly record struct BakeTypeRegistration(
    string TypeName,
    string SourcePrefix,
    BakeAssetKind Kind); // MessagePack | Raw

internal readonly record struct BakeReferenceSite(
    string MemberPath,   // e.g. "texture", "items[2].icon"
    string ReferencePath, // canonical path from TOML
    bool IsData);
```

`Bind` returns the typed instance; `references` lists every present `AssetRef`/`DataRef` in the
bound object graph with the key path needed for source spans. `AddToPack` is generated per type
(`builder.AddMessagePack<T>(path, (T)value)`) and therefore never reflects over members.

**Host isolation (proposed).** The CLI loads the host in a non-collectible
`AssemblyLoadContext` whose resolver returns the already-loaded `Bork2d.*`, `Tomlyn`, `PolyType`,
`Nerdbank.MessagePack`, and `System.*` assemblies from the default context, and loads the host and
`Project.Common` from the host directory. This keeps `PackBuilder`, `IBakeManifest`, `SpriteAsset`,
and `PolyType` type identities shared. A collectible context for editor reload is an M5 concern.
The exact mechanism is Q-08.

### 5.4 Artifact layout (proposed)

| Artifact | Location | Committed? |
| --- | --- | --- |
| Project descriptor | game root (`bork2d.project.toml`) | yes (source) |
| Generated host project and sources | `<project>/obj/bork2d/bakehost/` | no |
| Incremental manifest | `<project>/obj/bork2d/bake/manifest.json` | no |
| Runtime pack | default `$(TargetDir)assets.pck`, overridable | no |
| Diagnostics | stdout/stderr; structured objects in the library | no |

The runtime resolves the pack against the process current directory or an absolute path. The
default output location and the `dotnet run` working-directory story are Q-10.

## 6. Project descriptor — `bork2d.project.toml`

Proposed schema; Q-03/Q-04 decide the exact fields and requiredness. The descriptor is TOML and is
parsed with Tomlyn by `Bork2d.Bake`; it is never packed.

```toml
schema = 1
name = "MyGame"

[assets]
root = "src/Project.Common/Assets"          # relative to this descriptor file

[bake]
# A literal default; `--output` (used by the targets) wins over this value.
pack = "assets.pck"
host = "obj/bork2d/bakehost"

[textures]
filter = "linear"                            # nearest | linear
wrap = "clamp"                               # clamp | repeat

# One entry per path prefix. Longest matching prefix wins.
[[types]]
prefix = "data"
type = "MyGame.Data.GameConfig"

[[types]]
prefix = "sprites"
type = "Bork2d.Api.Assets.SpriteAsset"
```

Rules (proposed):

- `schema` is required and must be `1`. An unknown schema is a fatal descriptor diagnostic.
- Every relative path resolves against the descriptor's directory. CLI overrides (`--output`) win
  over `[bake].pack`.
- `[assets].root` is required. It must exist, be a directory, and contain the source tree.
- `[[types]]` entries map a **canonical source prefix** (relative to the asset root) to a CLR type.
  The longest prefix that matches a TOML file's asset path decides the type; the prefix must match
  on a `/` segment boundary. Duplicate exact prefixes are errors. Overlapping prefixes with no
  deterministic winner are errors.
- Type names are full names resolved in `Project.Common` first, then `Bork2d.Api`; an ambiguous
  name is an error. Assembly-qualified names and types from other assemblies are Q-04.
- A mapped `Project.Common` type must implement `IShapeable<T>` (that is, carry `[GenerateShape]`
  on a `partial` type). Built-in shapeable types such as `Bork2d.Api.Assets.SpriteAsset` are
  allowed without any Bork2d-specific marker; registration is the descriptor entry itself.
- `[textures]` defaults apply when a sidecar omits `filter`/`wrap`; both fields are optional.
- Unknown descriptor keys are errors (Q-05). Include/exclude patterns and a raw-RGBA source policy
  are Q-12.
- The descriptor is validated before any source file is read; errors carry descriptor line/column.

## 7. Custom data

### 7.1 Registration and the generated shape

There is **no Bork2d registration attribute and no Bork2d source generator** (D-M4-12). A game
data type is registered by the descriptor (`[[types]]`) and shaped by PolyType's `[GenerateShape]`:

```csharp
[GenerateShape]
public sealed partial class EnemyDefinition
{
    public required string Name { get; set; }
    public int Health { get; set; }
}

[GenerateShape]
public readonly partial record struct EnemyStats(int Attack, int Defense);
```

`partial` is **required**, but it is a PolyType requirement, not a Bork2d one: on .NET 8+,
`[GenerateShape]` emits a partial declaration implementing `IShapeable<T>`, which is what
`LoadAsset<T>` requires and what the bake host uses to serialize. The repository's own
`SpriteAsset`, `TextureDescriptor`, and `MessagePackSmokeData` already follow this pattern (all are
`partial` with `[GenerateShape]`). The keyword is one word; `[GenerateShape]` is the single
attribute.

The descriptor is the registration list. There is no attribute scan and no `[Bork2dSerializable]`
marker. The host generator validates that each mapped type implements `IShapeable<T>` and reports a
clear diagnostic when it does not; a missing `[GenerateShape]` is a compile-time problem in the game
assembly (no shape is generated), not a bake-time discovery.

**Structs (D-M4-11).** A `readonly record struct` (or any struct) is supported both as a **member**
and as a **top-level asset root**. As a member it is covered by the root's generated shape,
serialized by Nerdbank.MessagePack, and mapped by Tomlyn like any nested POCO; `Color`, `Vector2`,
`AssetRef<T>`, and `DataRef` are value-type members already. As a root it must be a top-level
`partial` struct (a `readonly record struct` with a primary constructor/`init` members is the
recommended form); it is loaded with `LoadAsset<T>` and referenced with `AssetRef<T>` after the
D-M4-11 constraint relaxation. A value-type root has no null value: a MessagePack `nil` payload is
a `Decode` fault, and the loader never returns a silent default. Tomlyn must construct the struct
(constructor binding); the WP0 spike and the WP2 tests must prove `readonly record struct` binding,
including nested struct members and collections of structs.

Compiler/PolyType diagnostics cover a non-`partial` type (CS0260), an abstract/static/generic type,
a nested private type, and an unsupported shape. The bake host adds its own diagnostics for a
mapped type that is not found, is ambiguous, does not implement `IShapeable<T>`, or has an
unsupported shape kind. Models must have a public parameterless constructor (or a Tomlyn-supported
constructor), public settable properties/fields, and only supported leaves (§7.3).

**`Color` (D-M4-12).** `Color` does not get `[GenerateShape]` and stays non-`partial`. It is
configured with a PolyType marshaler (proposed
`[TypeShape(Marshaler = typeof(ColorMarshaler))]`, surrogate `float[4]`) so the containing asset's
generated shape knows how to serialize it without augmenting `Color` itself. The TOML form remains
`[r, g, b, a]`. `ColorMarshaler` is public infrastructure (like `AssetRefMarshaler`) because the
shape generated in a game assembly references it. WP0 verifies the marshaler path against PolyType
1.4.1; if it cannot be used, the fallback is a `Color` converter registered in the **runtime**
`PackSerialization` serializer so bake and runtime agree — not a tooling-only converter. If neither
works, `Color` becomes `partial` as a last resort and D-M4-12 is revisited.

### 7.2 Generated host and binders

The host generator emits a static manifest and a per-type binder. The binder produces the typed
object and the reference sites. The proposed mechanisms (Q-06/Q-07) are:

1. **Tomlyn source-gen + DOM span pass.** The generator emits a `TomlSerializerContext` covering
   every mapped type plus the Bork2d converters; the manifest binds with the context. A second
   Tomlyn DOM/metadata pass maps the manifest's member paths to source spans for reference
   diagnostics.
2. **Generated DOM binder.** If the source-gen path cannot express the required converters, spans,
   or unknown-key detection, the generator emits explicit code that reads the Tomlyn DOM and
   assigns members, recording spans directly.

A WP0 spike chooses between them; if neither satisfies §16.2/§16.3, stop and ask the owner.

### 7.3 TOML mapping (proposed)

| CLR leaf | TOML form | Notes |
| --- | --- | --- |
| `bool` | boolean | |
| `sbyte`…`ulong`, `nint`/`nuint` | integer | Range-checked; integer→integer only. |
| `float`, `double` (plus `decimal`/`Half` if Q-31 approves) | float or integer | Integer→float is allowed; float→integer is not. |
| `string` (`char` is Q-31) | string | UTF-8; TOML escapes. |
| Enums | string name | Proposed camelCase (`quad`, `triangleStrip`, `triangles`); exact casing is Q-33. |
| `Guid` | string | `Guid.Parse` rules, invariant. |
| `DateTime`, `DateTimeOffset` | TOML offset date-time | Converted to UTC for `DateTime`; a local date-time without an offset is an error (Q-05). |
| `Vector2`, `Vector3`, `Vector4` | `[x, y]`, `[x, y, z]`, `[x, y, z, w]` | Floats. |
| `Color` | `[r, g, b, a]` | Linear floats in `[0,1]` (D-M4-12 marshaler); no sRGB conversion. |
| `AssetRef<T>` | string | Canonical asset path; absent key = absent reference. |
| `DataRef` | string | Canonical raw path; absent key = absent reference. |
| Arrays/lists | TOML array | `T[]`, `List<T>`, `IReadOnlyList<T>`; element type follows this table. |
| `Dictionary<string, T>` | TOML table | Other key types are unsupported in v1 (Q-28). |
| Nested registered type | TOML table | Recurses with the same rules. |
| Struct member or root (`readonly record struct`, etc.) | TOML table | Supported; root structs use constructor binding and D-M4-11 value semantics. |
| Nullable `T?` | absent key or `null`-less TOML | TOML has no null; absence means null. |
| `object`, polymorphism, delegates, tasks, engine entities | — | Unsupported; diagnostic. |

Keys are camelCase by default (`PropertyNamingPolicy = JsonNamingPolicy.CamelCase`).
`[JsonPropertyName("…")]` overrides a key; `[JsonIgnore]` excludes a member. Case sensitivity,
unknown keys, and required semantics are Q-05.

Missing values:

- `required` C# members and `[JsonRequired]` members must be present in TOML.
- A non-nullable reference member must be non-null after binding.
- Value types and initializers keep their C# defaults when the key is absent.
- A collection member that is absent stays `null` unless an initializer supplies a value.

Unknown keys are errors by default (recommended), with `file:line:column`; this catches typos. An
opt-out is Q-05.

### 7.4 Diagnostics and validation split

M4 validates **binding and references**, not type-specific field semantics:

- Parse errors, type mismatches, missing required members, unknown keys, unsupported leaves, and
  numeric range errors are bake errors with source spans.
- `AssetRef<T>`/`DataRef` validation is §9.
- `SpriteAsset`'s UVs, scale, pivot, topology, and indices are **not** validated in M4 (D-M4-06).
  An invalid sprite value can bake; M5 adds field validation and slicing.
- No runtime field validation is added (D-035).

### 7.5 `SpriteAsset` as custom data

There is no `SpritePipeline` and no `sprites/`-specific code. A project maps a prefix to
`Bork2d.Api.Assets.SpriteAsset`:

```toml
[[types]]
prefix = "sprites"
type = "Bork2d.Api.Assets.SpriteAsset"
```

A source file `sprites/player/idle.toml` becomes the asset `sprites/player/idle` and uses the
public property names, camelCased:

```toml
texture = "textures/player"
uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]
scale = 16.0
pivot = [0.5, 0.5]
topology = "quad"
# indices = [0, 1, 2, 0, 2, 3]   # non-Quad only; not validated in M4
```

The baker validates that `texture` exists and is a `TextureDescriptor` MessagePack entry. It does
not validate UV extents, winding, scale, pivot, topology/indices consistency, or texture dimensions.
An M4 test bakes this file and loads it with `LoadAsset<SpriteAsset>`, asserting the values and the
reference.

## 8. Textures

### 8.1 Source layout and sidecars

```text
Assets/textures/player.png
Assets/textures/player.png.toml     # import sidecar (optional)
Assets/textures/ui/button.jpg
Assets/textures/ui/button.jpg.toml
```

- Accepted source extensions: `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tga` (D-M4-05). Case-insensitive
  discovery; canonical-path collisions after lowercasing and extension stripping are errors.
- The asset path is `textures/<relative path without the source extension>`; the raw payload path
  is `textures/raw/<same suffix>`. `Assets/textures/ui/button.jpg` → `textures/ui/button` and
  `textures/raw/ui/button`.
- The sidecar is `<source file>.toml`. It carries **import settings only** (proposed; Q-12):

```toml
filter = "nearest"   # optional: nearest | linear
wrap = "repeat"      # optional: clamp | repeat
```

- A missing sidecar is valid: the `[textures]` descriptor defaults apply.
- A `.toml` file under `textures/` that is not `<source>.toml` for an existing source is an error.
  A sidecar without a source is an error. Source files under `textures/raw/` are reserved/forbidden
  (Q-12).
- Raw RGBA sources and their sidecar schema are Q-12.

### 8.2 Decode and normalize

- Decode with StbImageSharp's `ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha)`;
  the result is RGBA8 with straight alpha.
- JPG has no alpha; the decoder supplies 255.
- Palette and grayscale sources are converted to RGBA by the decoder.
- No premultiplication, gamma correction, ICC handling, flipping, or resampling. PNG/BMP/TGA
  sources are top-left origin, matching the runtime descriptor contract.
- 16-bit and other high-bit-depth handling is Q-12 (StbImageSharp returns 8-bit; the proposed
  default is to accept the conversion and document it, or reject when the source header declares
  16-bit).
- Decode failure, unsupported format, zero width/height, or a size that overflows the descriptor/
  payload bounds is a diagnostic naming the source file. No partial descriptor/payload is emitted.

### 8.3 Emitted assets

For `textures/ui/button`:

| Asset | Kind | Value |
| --- | --- | --- |
| `textures/ui/button` | MessagePack | `TextureDescriptor { Payload = "textures/raw/ui/button", Width, Height, Format = "rgba8", Filter, Wrap }` |
| `textures/raw/ui/button` | Raw | `width × height × 4` bytes, row-major, top-left origin, straight alpha, sRGB values unchanged |

The descriptor is created in code by the texture pipeline and added with
`PackBuilder.AddMessagePack<TextureDescriptor>`; the payload is added with `AddRaw`. Authors do not
hand-write texture descriptors in M4 (Q-29).

### 8.4 Validation

- Sidecar `filter` is `nearest` or `linear`; `wrap` is `clamp` or `repeat`; unknown keys and values
  are errors.
- Dimensions are positive; the payload size is exactly `width × height × 4`; the size fits the
  pack's `uint32` length and the in-memory array limit.
- The descriptor's `Payload` is present, canonical, and targets the raw entry.
- No policy cap is added beyond the format field widths (D-M3-06); a decoder/OOM guard is Q-12.
- Duplicate canonical paths, collisions with custom data, and reserved `textures/raw/` names are
  errors.

## 9. References

Every non-null `AssetRef<T>` and `DataRef` in a baked custom-data asset is validated against the
complete baked asset set **before** the pack is written:

1. The path is canonical (lowercase, `/`, no extension, no empty/`.`/`..` segments).
2. An entry with that path is produced by this bake.
3. The kind matches: `AssetRef` targets a MessagePack entry; `DataRef` targets a raw entry.
4. When the target's type is known, it matches `T`: a `SpriteAsset.Texture` reference must target a
   `TextureDescriptor`; a `data/` reference must target the type mapped for that path; a
   `AssetRef<Struct>` target must be the same struct type (D-M4-11).
5. A `DataRef` targets a raw payload (in M4, only `textures/raw/**` exists).
6. An absent reference (omitted key) is valid and skipped.

A violation is a bake error naming the source file, line, column, member path, and reference path.
The error does not wait for `PackBuilder`; if `PackBuilder` later reports a reference failure, that
is an internal baker bug (the bake should have failed first).

`PackBuilder`'s pack-time validation remains in force and is the last line of defense. It reports a
`Bork2dException` without source spans; that path is not user-facing in a correct bake.

## 10. Pack, determinism, and incremental bake

### 10.1 Pack integration

- All baked entries are added to one `PackBuilder`; the compression mode comes from the MSBuild
  configuration (`Debug` → `PackCompressionMode.Debug`, `Release` → `.Release`), overridable by
  `--mode`.
- Entry paths are sorted by `PackBuilder`; the baker must not depend on filesystem enumeration
  order. Source discovery sorts paths ordinal before processing.
- The pack is written to a temporary file in the destination directory and atomically replaces the
  target only after a fully successful bake. A failed bake leaves the previous pack and manifest
  unchanged and fails the build.
- Empty projects (no custom data, no textures) produce a valid empty pack; texture-only projects
  are supported (Q-23).
- The v1 header is always Zstd-compressed and checksummed by the existing writer; M4 adds no format
  changes.

### 10.2 Determinism (D-M4-07)

For identical descriptor, sources, `Project.Common` assembly, bake-tool version, and mode, a second
bake must produce:

- the same set of canonical paths and kinds;
- the same sorted entry table (path, `IsRaw`, `IsCompressed`, `Size`; `Offset` follows from the
  layout);
- byte-identical custom-data MessagePack payloads (generated-shape serialization);
- byte-identical texture payloads and descriptor values;
- identical diagnostics for the same invalid input (same order and text).

Whole-pack byte equality is **not** guaranteed (D-M3-08), because Zstd output may vary across
runtime versions. Tests assert the contract above, not a pack hash. Q-19 pins the exact assertions.

### 10.3 Incremental bake (D-M4-08)

- A manifest under `obj/bork2d/bake/` records: descriptor hash, canonical input list with SHA-256
  per source file, `Project.Common` hash, tool version, mode, output pack hash, and host hash.
- On each invocation the CLI recomputes the input digest. If it matches the manifest and the
  output pack exists with the recorded hash, the bake is skipped (reported as up to date).
- Any change re-bakes the entire project (no per-asset cache in M4). Host generation is skipped the
  same way when its inputs (descriptor, `Project.Common`, generator version) are unchanged.
- `--force` bypasses both skips. A missing/corrupt/unreadable manifest forces a full bake. An
  externally modified pack is detected by the output hash and re-baked.
- Deleted sources disappear on the next full bake; stale outputs are never merged.
- The manifest is internal, versioned, and not committed. The exact format and hash algorithm are
  Q-09 (recommendation: JSON + SHA-256).
- The bake is single-threaded in M4; parallel decoding is Q-25.

## 11. CLI

Proposed surface (Q-15); names may change before implementation.

```text
bork2d-bake generate-host --project <bork2d.project.toml> --common <Project.Common.dll>
                          --output <dir> [--configuration Debug|Release] [--force]
bork2d-bake bake          --project <bork2d.project.toml> --host <BakeHost.dll>
                          [--output <assets.pck>] [--mode debug|release]
                          [--check] [--force] [--verbose|--quiet]
bork2d-bake validate      --project <bork2d.project.toml> [--host <BakeHost.dll>]
bork2d-bake --help | --version
```

- `generate-host` is used by MSBuild; it writes the generated host sources/project and returns 0
  when up to date.
- `bake` runs the full pipeline. `--check` validates without writing the pack or manifest.
- `validate` validates the descriptor and reachable sources without requiring a host; when a host is
  supplied it also binds and checks references.
- `--project` is required; relative CLI paths resolve against the process current directory.
  Descriptor-internal paths resolve against the descriptor.
- Exit codes: `0` success/up to date; `1` one or more bake diagnostics; `2` usage/descriptor
  configuration error; `3` unexpected internal failure (with a stack trace only under `--verbose`).
- Diagnostics go to stderr in the MSBuild-canonical form (§13); the summary goes to stdout.
  `--json` is deferred unless Q-15 asks for it; the library returns structured diagnostics for the
  M5 editor.

## 12. MSBuild integration (D-M4-08)

Proposed target structure (Q-11); no new CI job.

- A targets file shipped with the tooling (`build/Bork2d.Bake.targets`, path to be confirmed) is
  imported by the runnable app project (proposed `Project.Desktop`) — the project that owns the
  runtime pack. `Project.Common` carries only the `[GenerateShape]` data types.
- Properties (proposed): `Bork2dProjectDescriptor`, `Bork2dCommonAssembly`,
  `Bork2dBakeHostDirectory`, `Bork2dBakeOutput`, `Bork2dBakeMode`, `Bork2dBakeVerbose`.
- Targets (proposed):
  1. `Bork2dGenerateBakeHost` — runs `generate-host` after `Project.Common` is built; skips when
     the host hash is unchanged.
  2. `Bork2dBuildBakeHost` — builds the generated `BakeHost.csproj` with the current
     configuration (MSBuild task, not a shell).
  3. `Bork2dBake` — runs `bake`; the CLI's hash gate decides skip/re-bake; diagnostics fail the
     build; the pack is copied to the output/publish location.
  4. `Bork2dCleanBake` — removes the generated host, manifest, and pack on `Clean`.
- Hook order: the bake needs `Project.Common`'s output, so it runs after the project references
  are built. Hooking `Build` vs `Publish`, and whether the pack is copied to `$(OutDir)` or
  `$(PublishDir)`, is Q-11.
- `dotnet run` resolves the pack against the working directory; the fixture/test sets the working
  directory or passes an absolute path. The M9 template will own the final ergonomics.
- A failed bake must not leave a truncated pack; MSBuild reports the canonical error lines and
  stops. Builds with no descriptor opted in are unaffected.
- CI stays as it is (restore/format/build/test); bake tests run inside the test suite. The local AOT
  gate gains the bake step (§14).

## 13. Diagnostics

### 13.1 Model

```csharp
// Tooling-only; exact visibility with Q-02.
public enum BakeSeverity { Error, Warning, Info }

public readonly record struct BakeDiagnostic(
    BakeSeverity Severity,
    string Code,
    string Message,
    string? FilePath,
    int? Line,
    int? Column,
    string? AssetPath,
    string? MemberPath);
```

- `FilePath`/`Line`/`Column` are 1-based; `FilePath` is project-relative when under the asset root,
  otherwise absolute.
- Diagnostics are collected where safe and sorted by file, line, column, code, then message, so
  repeated runs produce the same order.
- Host-generation, host-load, and unexpected IO failures may be fatal/fail-fast; source diagnostics
  are aggregated.
- Tomlyn's `TomlException`/`DiagnosticsBag` line/column values are adapted; parse errors keep their
  span.

### 13.2 Rendering and codes

MSBuild-canonical line:

```text
Assets/data/enemies/grunt.toml(12,17): error BK2301: 'hp' must be an integer; got a string.
```

Proposed code ranges (final table in the owning docs with WP6):

| Range | Area |
| --- | --- |
| `BK1xxx` | Descriptor, projects, type registration, generated host, host loading |
| `BK2xxx` | TOML parse/bind: syntax, type mismatch, required/unknown/missing member, range |
| `BK3xxx` | Custom-data references and asset-graph validation |
| `BK4xxx` | Texture discovery, sidecar, decoding, dimensions, payload |
| `BK5xxx` | Pack layout/write, incremental manifest, up-to-date/force |
| `BK6xxx` | MSBuild/target/host-build integration |

Warnings are used only for recoverable, intentional conditions (for example a deprecated import
setting); the default bake treats all validation failures as errors. The full code table is part of
the documentation work package; codes are stable once released.

## 14. NativeAOT

- `Bake`, `Bake.Cli`, Tomlyn, StbImageSharp, and the host generator are **not** referenced by
  `Bork2d.Api`, `Common`, `Assets`, `Render`, `Runtime`, or the published AOT app. The dependency
  test proves the absence.
- `[GenerateShape]` on `partial` types keeps runtime deserialization on the existing
  generated-shape path; no reflection resolver is introduced. Struct roots use the same generated
  shape and the D-M4-11 load path; a `nil` payload is a `Decode` fault.
- `Color`'s marshaler is AOT-safe and uses only PolyType, which `Bork2d.Api` already references;
  `Color` does not become `partial`.
- The AOT build bakes a fixture pack (D-M4-09):
  1. the fixture's `Project.Common` builds with PolyType's generator;
  2. the MSBuild target generates and builds the host and bakes `assets.pck`;
  3. the pack is copied next to the published executable;
  4. the default smoke loads the baked pack, not a `PackBuilder`-built temporary pack, and asserts
     custom data, a baked `SpriteAsset`, its `TextureDescriptor`, and the raw payload;
  5. `--windowed --ticks N` keeps `WithoutAssets()` and is unchanged.
- The smoke assembly references the fixture library (Q-16) but not the bake tooling. The publish
  output contains no Tomlyn/StbImageSharp/Bake assemblies; the publish reports zero AOT/trim
  warnings and `SDL2.dll` remains present.
- The local AOT gate documentation and commands in
  [Contributing §2](../contributing.md#2-build-test-and-local-gates) and
  [NativeAOT §7](../architecture/nativeaot.md#7-testing-and-ci) are updated in WP6.

## 15. Work packages

Sequence is dependency-ordered. WP2 and WP3 may run in parallel only if the WP1 split keeps their
files/areas disjoint; otherwise run them sequentially and record why in the milestone PR.

| WP | Scope | Depends | Wave |
| --- | --- | --- | --- |
| WP0 | Feasibility spike: PolyType `[GenerateShape]` for class and struct roots (including `readonly record struct`); `Color` marshaler; `readonly record struct` TOML binding (primary constructor/`init`, member and root); host generation/compile/load; Tomlyn mapping + converters + spans; `PackBuilder` calls from generated code; AOT shape proof. Report; if a contract-level fallback is needed, stop and ask. | — | 0 |
| WP1 | Foundations: new projects/solution/dependency test/docs skeletons; `[GenerateShape]` registration validation (no Bork2d attribute/generator); `Color` marshaler; the D-M4-11 typed-load relaxation (`IAssets`, `AssetRef`, marshaler, deserializer, loader tests); descriptor model/parser; diagnostics model/codes; CLI skeleton; path/traversal safety; incremental/host contracts. | WP0 | 1 |
| WP2 | Generated host + custom data: host generator, generated csproj/manifest, CLI load/ALC, Tomlyn binders/converters, references, `SpriteAsset`-as-data, file/line diagnostics, tests. | WP1 | 2 |
| WP3 | Texture pipeline: discovery, sidecars/defaults, decoder seam + StbImageSharp, RGBA8, descriptor/payload, validation, collisions; tests. | WP1 | 2 |
| WP4 | References + pack + determinism: registry with spans, full reference validation, `PackBuilder` integration, atomic write, structural determinism, `IAssets` load tests. | WP2, WP3 | 3 |
| WP5 | Incremental + MSBuild: hash manifest, skip/force/clean, `.targets`, host build invocation, fixture project, `dotnet build` integration test, output/publish layout. | WP4 | 4 |
| WP6 | AOT proof + docs + gates: AOT bake step, smoke loads baked pack, docs (assets/tooling, projects, nativeaot, API guide/reference, README, roadmap, decisions, contributing), full gates, acceptance evidence. | WP5 | 5 |

WP0 is a gate, not a deliverable: it may produce throwaway code. Its report records which TOML
mapping/span mechanism is viable and any deviation; the orchestrator asks the owner before a
contract-level change.

## 16. Acceptance criteria

M4 is done when all of the following are demonstrated (not merely coded):

1. **Bake and load.** A source tree with a descriptor, one registered custom-data type, one
   `SpriteAsset` TOML, one PNG (and the other accepted formats covered by tests), and sidecars
   bakes to one `assets.pck`; the runtime loads the custom asset, `SpriteAsset`,
   `TextureDescriptor`, and raw payload with the expected values and references. A registered
   class root containing a `readonly record struct` member (including `Color`/`Vector2`) round-trips
   unchanged. A `readonly record struct` **root** bakes, loads with `LoadAsset<T>`, and is
   referenced with `AssetRef<T>`; a `nil` payload for that root faults `Decode`; no silent default
   is returned.
2. **Invalid TOML diagnostics.** Syntax, missing required member, unknown key, wrong type, and
   numeric range failures each fail the bake with a stable code and `file:line:column`; no pack is
   replaced.
3. **Reference diagnostics.** Missing, wrong-kind, wrong-type, and non-canonical references each
   fail with the source file/line/column and member path; an absent reference is valid.
4. **Textures.** For a generated source image the descriptor and payload are exact
   (`width × height × 4`, byte-for-byte expected pixels, expected filter/wrap/defaults); corrupt or
   unsupported sources and invalid sidecars fail with file/line diagnostics; collisions and
   reserved paths fail.
5. **Sprite-as-data.** A `SpriteAsset` TOML bakes and loads as ordinary custom data; no UV/pivot/
   scale/topology/indices validation exists in M4, and this is documented as M5 work.
6. **Determinism.** Two bakes of identical inputs produce identical asset payloads, entry table,
   and offsets; the documented structural contract passes in a separate-process test.
7. **Incremental.** A second bake with no changes skips; one source change re-bakes; deleting a
   source removes it; a corrupt/missing manifest re-bakes; `--force` re-bakes; a failed bake leaves
   the previous pack intact.
8. **MSBuild.** A fixture project builds through the targets, produces the pack at the documented
   location, fails the build with canonical errors on invalid source, and cleans its generated
   host/manifest/pack.
9. **AOT.** The AOT publish is zero-warning, contains no bake/TOML/image-tooling assemblies, and the
   smoke loads the baked fixture pack (custom data + sprite + descriptor + raw) and exits 0; the
   windowed demo still exits 0 with `WithoutAssets()`.
10. **Boundaries.** Runtime behavior is unchanged except the approved `Bork2d.Api` additions and the
    D-M4-11 typed-load relaxation; dependency/public-surface tests prove the tooling boundary; the
    generated host is not in the solution.
11. **Docs and decisions.** The owning docs, roadmap delta, D-M4 entries, and AOT-gate docs are
    updated; status markers are honest. The docs state plainly that `partial` is PolyType's
    `[GenerateShape]` requirement, not a Bork2d attribute or generator requirement.
12. **Gates.** Debug and Release builds with zero warnings; full deterministic tests; format; docs
    check; AOT publish/run locally.

## 17. Test plan

All tests live in `Bork2d.Tests` unless a fixture project is added; no binary fixture files are
committed.

| Area | Cases |
| --- | --- |
| Descriptor | valid schema; missing `schema`/`root`; unknown schema/key; relative/absolute path resolution; duplicate/overlapping prefixes; unmapped TOML; type not found/ambiguous; type without `[GenerateShape]`/`IShapeable<T>`; built-in allow-list. |
| Shape/registration | a mapped type with `[GenerateShape]` bakes; a non-`partial` type fails to compile (CS0260); a mapped type without a shape fails with a clear BK1xxx diagnostic; class and struct roots; `Color` round-trips through its marshaler and stays non-`partial`. |
| TOML binding | every leaf in §7.3; nested types; `readonly record struct` members and roots (primary constructor/`init`, `Color`, `Vector2`, collections of structs); arrays/lists/dictionaries; nullables; missing/required; unknown key; type mismatch; numeric range; camelCase and `[JsonPropertyName]`; `[JsonIgnore]`; UTF-8. |
| Custom data | bake + `LoadAsset<T>` round-trip for class and struct roots; `AssetRef<Struct>`; `SpriteAsset`-as-data + texture reference; nested references; absent references; source spans for nested/collection references. |
| API contract | `LoadAsset<T>`/`AssetRef<T>` compile for a `readonly record struct`; a `nil` root payload faults `Decode`; class-based call sites and `FakeAssets` still compile and behave unchanged; API docs match the relaxed constraint. |
| References | canonical/missing/wrong-kind/wrong-type/duplicate target; `DataRef` to raw; errors carry file/line/member path; `PackBuilder` backstop not triggered. |
| Textures | PNG (RGBA/RGB/palette/grayscale), BMP, TGA, JPG (decoder seam plus a tiny in-code sample); sidecar defaults/overrides; 16-bit behavior; corrupt/truncated/unsupported; zero/huge dimensions; duplicate paths; `textures/raw/` reservation. |
| Determinism | same-process and separate-process double bake; entry table/payload comparison; discovery order independence. |
| Incremental | skip; change one TOML; change one image; delete a source; corrupt manifest; missing pack; externally modified pack; `--force`; failed bake keeps the old pack. |
| Host | generated project contents; manifest type map; binder delegates; ALC/type-identity; missing/stale host diagnostic; host rebuild after descriptor change. |
| MSBuild | fixture `dotnet build`; pack path; error propagation; clean; publish copy. |
| AOT | publish/run gate with the baked fixture pack; dependency proof that Bake/Tomlyn/Stb are absent. |
| Boundaries | dependency matrix rows; public surface (only the approved Api additions); no `Bake` types in runtime assemblies; docs links. |

Determinism rules: temp directories, no sleeps, no retries, bounded joins, ordinal comparisons, and
cleanup. A host-build integration test may be marked as a slower collection but must run in the
normal debug test pass (CI). Filter examples:
`--filter "FullyQualifiedName~Bake|FullyQualifiedName~Assets"`.

## 18. Open questions (decision register)

**Do not implement past a blocked question.** Each row lists a suggested default; the default is
not a decision until the owner approves it. IDs are stable for review.

Q-34 (struct roots) was resolved by D-M4-11. Q-01 (attribute namespace), Q-17 (Roslyn analyzer
packaging), and Q-27 (non-`partial` diagnostic) were resolved by D-M4-12: there is no Bork2d
attribute or generator, PolyType owns the shape and `partial` rule, and CS0260 is the compile-time
behavior. They are no longer open; the remaining rows are.

### 18.1 Public surface and registration

| ID | Question | Suggested default | Blocks |
| --- | --- | --- | --- |
| Q-02 | Is the bake tooling surface public (like `PackBuilder`) or internal with `InternalsVisibleTo` to a fixed `Bork2d.BakeHost`, `Bake.Cli`, `Tests`, and `Editor`? | Internal + IVT; no new public tooling API until a consumer needs one. | WP1/WP2 |
| Q-03 | Exact descriptor schema: required/optional fields, `[bake].host`, include/exclude, empty `[[types]]`, default pack name/location. | Schema in §6; allow empty type list; pack defaults to `assets.pck` at the target output. | WP1 |
| Q-04 | Type naming: simple name in `Project.Common` then Api vs assembly-qualified; may a mapped type live outside `Project.Common`? | Simple name, Project.Common then Api; other assemblies deferred. | WP1/WP2 |
| Q-30 | Does a mapped `[GenerateShape]` type need to be public, or is `internal` enough (both are visible to the generated host via `InternalsVisibleTo`)? | `internal` allowed; the host is a friend. | WP1 |

### 18.2 TOML semantics

| ID | Question | Suggested default | Blocks |
| --- | --- | --- | --- |
| Q-05 | Unknown keys: error, warning, or ignore? Case sensitivity? Are local (`DateTime` without offset) values accepted? Are numeric strings accepted? | Error on unknown keys and local date-times; exact camelCase names with case-insensitive read. | WP2 |
| Q-06 | How are source spans for reference diagnostics obtained? (Tomlyn metadata store vs a generated DOM binder vs a second DOM pass keyed by member paths.) | Generated DOM/metadata span pass per §7.2; WP0 decides. | WP2/WP4 |
| Q-07 | TOML mapping mechanism in the generated host: Tomlyn source-gen context + converters vs a generated DOM binder. | Source-gen if it supports the required converters/spans/unknown-key checks; otherwise generated DOM binder. WP0 decides. | WP2 |
| Q-08 | Host generation/load details: MSBuild task vs CLI-generated project; default ALC vs shared-name custom ALC; non-collectible vs collectible; assembly name/IVT. | CLI `generate-host` + MSBuild `Build`; non-collectible custom ALC sharing engine assemblies; fixed `Bork2d.BakeHost` name. | WP2/WP5 |
| Q-21 | Is a TOML writer or comment preservation needed in M4? | No; read-only in M4, M5 owns writing/trivia. | WP2 |
| Q-28 | Is `Dictionary<string,T>` supported in v1, and are sets/immutable collections supported? | Dictionary yes; sets/immutable deferred. | WP2 |
| Q-31 | Is `char` a supported leaf? Is `decimal`/`Half` supported? | `char` no; `decimal`/`Half` only if Tomlyn supports them cleanly. | WP2 |
| Q-33 | Enum TOML form: member name (`Quad`) vs camelCase (`quad`); case-insensitive read; are integer values accepted? | camelCase strings, case-sensitive, integers rejected; use one converter for game enums and `SpriteTopology`. | WP2 |

### 18.3 Textures

| ID | Question | Suggested default | Blocks |
| --- | --- | --- | --- |
| Q-12 | Texture edge policy: 16-bit sources (accept/downsample vs reject); raw RGBA sources in M4; sidecar required/optional; source dir reservation; symlinks; max dimension/OOM guard. | Accept 8-bit and decoder-normalized sources, reject explicit 16-bit PNGs; raw RGBA deferred; sidecars optional; forbid `textures/raw/` sources; skip reparse points; fail cleanly on OOM without a new policy cap. | WP3 |
| Q-24 | Are texture sidecars allowed to override `format` or declare source-specific fields? | Only `filter`/`wrap` in v1; `format` fixed `rgba8`. | WP3 |
| Q-29 | May authors hand-write `TextureDescriptor` TOML, or is it generated only? | Generated only; no descriptor TOML path in M4. | WP3 |

### 18.4 Paths, discovery, and diagnostics

| ID | Question | Suggested default | Blocks |
| --- | --- | --- | --- |
| Q-13 | Discovery: should unknown files under the asset root be ignored or errors? Must all TOML be mapped? How are `sprites/` files mapped? Longest-prefix tie-breaks? | Ignore non-asset files; error on unmapped `.toml`; map `sprites` explicitly; longest prefix, exact duplicates error. | WP2/WP3 |
| Q-14 | `SpriteAsset` test scope: which fields/values are baked and asserted, given no field validation? | One TOML with all public fields and a texture reference; assert round-trip only. | WP2 |
| Q-15 | CLI shape: final command names/options, `--json`, `validate` command, warnings policy, log verbosity. | Commands/options in §11; human output + structured library API; `--json` deferred. | WP1/WP5 |
| Q-19 | Determinism assertions: exactly which bytes/tables are compared, and is a pack-hash check informational only? | Entry table + per-asset payload bytes + diagnostics; pack hash informational. | WP4 |
| Q-20 | Does `Bork2d.Bake` need `Bork2d.Common`? | No; only Api/Assets. | WP1 |
| Q-25 | Sequential vs parallel bake; progress output; cancellation on first error. | Sequential, sorted, aggregate source errors. | WP3/WP4 |

### 18.5 Build, incremental, and AOT

| ID | Question | Suggested default | Blocks |
| --- | --- | --- | --- |
| Q-09 | Incremental manifest format/location, SHA-256 vs XXH3, host skip, corrupt-manifest behavior, `--force`. | JSON under `obj/bork2d/bake/`, SHA-256, skip when unchanged, corrupt → full bake. | WP5 |
| Q-10 | Default pack output location; publish copy; `dotnet run` CWD; whether the app must pass an absolute path. | `$(TargetDir)assets.pck`, copied to publish; document CWD and `WithAssetsPack`. | WP5 |
| Q-11 | Which project imports the targets; hook (`Build`/`Publish`); property names; clean behavior. | Runnable app project (`Project.Desktop`), `AfterTargets=Build`, publish copy, clean removes generated artifacts. | WP5 |
| Q-16 | AOT/fixture project: built-in `SpriteAsset` only vs a `Bork2d.BakeFixture` custom type; where the fixture lives; whether it joins the solution and the AOT reference graph. | Add `Bork2d.BakeFixture` (library + `Assets/`) to the solution, referenced by the AOT smoke and tests. | WP5/WP6 |
| Q-18 | Public API docs for `[GenerateShape]`/`partial`, descriptor registration, and `Color`: XML docs, API reference section, guide example, status markers. | Add an API-reference "data serialization" subsection and a guide "data and bake" section; document that `partial` is PolyType's requirement. | WP6 |
| Q-22 | AOT smoke default-run changes: replace the `PackBuilder` temp pack with the baked fixture pack; keep the windowed demo `WithoutAssets()`. | Yes; the default run loads the baked pack; windowed unchanged. | WP6 |
| Q-23 | Empty/no-custom-type and texture-only projects; empty pack behavior. | Supported; empty pack valid. | WP4/WP5 |
| Q-26 | Cross-platform CLI behavior (path separators, case-sensitive FS) in M4. | Windows-first; use platform APIs; no guarantees beyond v1 Windows. | WP1 |
| Q-32 | Docs ownership: extend `assets-and-tooling.md` or add a dedicated `architecture/bake.md`. | Extend `assets-and-tooling.md` for M4; split only if it becomes unmanageable. | WP6 |

## 19. Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Public API relaxation (`class` removal) | Null/decode semantics for value types, doc drift, missed generic call sites | Small, explicit D-M4-11 change; value-type tests (nil, default, `AssetRef<Struct>`); update every `where T : class` occurrence and the API reference/guide; no new members. |
| Generated host / assembly-load identity conflicts | Host fails to load, `PackBuilder` type mismatch, or wrong `T` | WP0 spike; shared-name ALC; dependency and type-identity tests; fail with a clear BK1xxx diagnostic. |
| PolyType `partial` requirement | Developer friction: every data root must be `partial` | Document it as PolyType's requirement (already true for `SpriteAsset`/`TextureDescriptor`); one attribute, one keyword; removing it would require the witness/load-API redesign, not a Bork2d generator. |
| Tomlyn source-gen cannot express `AssetRef<>`/spans/unknown-key checks | File/line diagnostics or reference binding incomplete | WP0 fallback to a generated DOM binder; do not silently downgrade the diagnostic contract. |
| MSBuild nested host build / incremental correctness | Stale host, wrong pack, slow builds, CI flakes | Hash-gated generate/build/bake; clean target; fixture integration test; no reliance on timestamps. |
| Reference spans across nested/collection members | Exit criterion 3 fails | Manifest reference sites + DOM span pass; dedicated tests; if impossible, stop and ask (do not ship file-only diagnostics). |
| Texture decoder edge cases (16-bit, alpha, malformed images) | Wrong pixels or crashes on bad input | Decoder seam + fakes; generated test images; explicit rejection/diagnostic policy; clean failure. |
| Bake tooling leaks into runtime/AOT | AOT warnings, publish failure, boundary violation | Project graph + dependency test + AOT publish inspection; no Bork2d generator; host generator is tooling-only. |
| Scope creep from sprites/slicing | M4 slips into M5 | D-M4-06 and the out-of-scope list; no `sprites/`-specific code. |
| Determinism expectations | Tests assert pack bytes and fail on a runtime upgrade | Define structural determinism (D-M4-07); never assert a pack hash. |
| Docs drift (roadmap, D-035 wording) | Scope ambiguity | WP6 updates the owning docs and the roadmap delta; check-docs gate. |
| Path traversal/symlinks/untrusted images | Reads outside the asset root or resource exhaustion | Resolve/validate every source under the root; skip reparse points; clean OOM diagnostics; no new policy cap without a decision. |

## 20. Docs, dependencies, and CI

### 20.1 Documentation changes (WP6)

- This spec stays as the milestone record; add the as-built evidence when merged.
- [`assets-and-tooling.md`](../architecture/assets-and-tooling.md): §10 (TOML model,
  `[GenerateShape]`/`partial` registration, leaves, Tomlyn), §11 (host generation, texture import,
  incremental, MSBuild, outputs), §9 (sprite scope note), §13 (AOT boundary), §16 (status).
- [`projects.md`](../architecture/projects.md): new project rows, dependency matrix, public surface,
  namespaces, package pins, game layout (`bork2d.project.toml`), build directives; no Bork2d
  analyzer project, and `Project.Common` references only `Bork2d.Api`/PolyType.
- [`nativeaot.md`](../architecture/nativeaot.md): JIT/tooling table, build-time reflection boundary,
  AOT smoke baked-pack step, dependency proof.
- [`api/reference.md`](../api/reference.md) and [`api/guide.md`](../api/guide.md):
  `[GenerateShape]` + `partial` data authoring, descriptor registration, TOML authoring, bake
  commands (tooling-only), the `ColorMarshaler` infrastructure type and `Color`'s serialized form,
  and the relaxed `where T : IShapeable<T>` load contract with a struct-root example and its `nil`
  decode rule.
- [`projects.md`](../architecture/projects.md), [`overview.md`](../architecture/overview.md),
  [`nativeaot.md`](../architecture/nativeaot.md), and
  [`assets-and-tooling.md`](../architecture/assets-and-tooling.md): replace every
  `where T : class, IShapeable<T>` statement with the D-M4-11 contract; update the AOT note that a
  missing shape is still a compile-time error.
- [docs index](../README.md): doc map/glossary (`bake host`, `project descriptor`), status markers,
  and ownership if a bake document is split (Q-32).
- [`roadmap.md`](../roadmap.md): M4 bullet/exit criteria updated for D-M4-06 and the approved
  decisions; progress log after merge.
- [`decisions.md`](../decisions.md): D-M4-01…D-M4-12 plus any accepted Q decisions; D-M4-11
  supersedes the `class` part of D-M3-22 (D-M3-22's shape contract itself remains), and D-M4-12
  supersedes D-M4-03 and D-M4-10.
- [`contributing.md`](../contributing.md): bake test filter, AOT bake step, tooling prerequisites.

### 20.2 New dependencies (proposed, exact pins at implementation)

| Project | Package | Version | License / note |
| --- | --- | --- | --- |
| `Bork2d.Bake` | Tomlyn | 2.10.1 | BSD-2; TOML 1.1.0 |
| `Bork2d.Bake` | StbImageSharp | 2.30.16 | public domain/MIT; pure managed |

There is **no** Bork2d source-generator project and no `Microsoft.CodeAnalysis` package: PolyType's
generator ships with the PolyType package that `Bork2d.Api` already references. The
`System.Reflection.MetadataLoadContext`/`System.Reflection.Metadata` choice for the build-time host
generator remains Q-08/WP1. No new runtime package is added to
`Api`/`Common`/`Assets`/`Render`/`Runtime`; `Color` uses the existing PolyType reference with a
marshaler.

### 20.3 CI and local gates

- CI stays: docs check, restore, format, Debug build, Debug tests. Bake unit/integration tests run
  in the Debug test pass (the host-build integration may be a slower collection but still runs).
- Local AOT gate: build the fixture via the targets, publish the smoke, run the headless smoke
  against the baked pack, then `--windowed --ticks 120`; zero warnings; inspect the publish output
  for the absence of Bake/Tomlyn/Stb assemblies.
- No new workflow job; no branch-push CI. The milestone PR records the exact commands/results.

## 21. Related documents

- [Roadmap](../roadmap.md#5-milestone-detail) — sequence and M4 status.
- [Decision log](../decisions.md) — accepted decisions; D-M4 entries are added with the work.
- [Assets and tooling](../architecture/assets-and-tooling.md#10-toml-source-model) — current TOML
  source model and [build pipeline](../architecture/assets-and-tooling.md#11-build-pipeline).
- [Projects and dependencies](../architecture/projects.md#2-target-dependency-graph) — project graph.
- [NativeAOT](../architecture/nativeaot.md#5-editor-and-tooling-boundary) — tooling boundary and
  [publish gate](../architecture/nativeaot.md#7-testing-and-ci).
- [API reference §7](../api/reference.md#7-iassets) — `IAssets`; [API guide §10](../api/guide.md#10-assets)
  — asset usage.
- [Pack tooling §13](../api/reference.md#13-pack-tooling-bork2dassetspack-tooling-only) — `PackBuilder`.
- [Contributing](../contributing.md#2-build-test-and-local-gates) — gates and tests.
- [M3 record](m3-assets.md) and [asset-reference refactor](refactor-asset-references.md) — inherited
  asset model; D-035's field-validation sentence is adjusted by D-M4-06.
