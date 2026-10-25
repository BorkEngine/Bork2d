# Data and Serialization

> **Status:** designed direction; implementation is roadmap **M4**, after the pack/FS layer
> (M3) and before the editor (M5). The TOML schema details are still partly `Open`, but the
> source → bake → pack split is fixed.

## 1. Scope

Game data in `Project.Common` is authored as **TOML** so it is readable, reviewable, and
diffable in git. At build time it is baked to **MessagePack** and packed into `assets.pck`.
The shipped runtime is NativeAOT and reads MessagePack only; TOML parsing and reflection live
in the editor and build tool.

This covers:

- custom serializable classes and their TOML files;
- sprite and texture descriptor sources;
- the baker and its relationship to the pack builder;
- versioning, validation, and asset references.

The binary pack format and runtime filesystem are specified in
[Assets and pack files](assets.md).

## 2. Source → bake → pack → runtime

```
Project.Common/Assets/
  data/**.toml          custom data
  sprites/**.toml       sprite definitions (texture, UVs, scale, pivot, topology, indices)
  textures/*            source image or raw RGBA
  textures/*.toml       Unity-style sidecar import settings
        │
        │  baker (JIT): TOML → MessagePack, textures → raw RGBA8 (KTX2 later)
        ▼
  MessagePack data assets + MessagePack sprite/texture descriptors + raw payloads
        │
        │  pack builder: compressed header + XXH3-64, Zstd assets in release
        ▼
  assets.pck
        │
        │  runtime (NativeAOT): Nerdbank.MessagePack generated shapes
        ▼
  IAssets.LoadAsset<T> / LoadSprite / LoadData
```

Rules:

- TOML is never present in the pack and is never parsed by the shipped runtime.
- The editor writes TOML and reads it back; it does not edit the binary pack.
- The baker is a build-time JIT tool; it may use reflection over `Project.Common`.
- The runtime uses [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack)
  source-generated shapes; no reflection resolver.
- One model describes both formats: the same C# type is the TOML authoring model and the
  MessagePack runtime model. Divergent DTOs are avoided unless generated.

## 3. TOML model

Proposed model (names finalised in M4):

```csharp
[GenerateShape]
[Bork2dSerializable] // optional editor/baker metadata
public partial class EnemyDefinition
{
    public string Id { get; set; } = "";
    public float Health { get; set; }
    public string Sprite { get; set; } = "";   // asset path, not Sprite.Id
    public List<AttackDefinition> Attacks { get; set; } = new();
}
```

`[GenerateShape]` is Nerdbank.MessagePack's source-generation anchor. Only the top-level type
that is serialized needs it; referenced types get shapes generated. The editor/baker may use
reflection over the same model because they are JIT; the runtime uses the generated shapes.

Constraints:

- Serializable types are classes/structs with a public parameterless constructor and public
  settable properties/fields, or a documented constructor-binding scheme.
- Supported leaves: primitives, `string`, enums, `Guid`, UTC `DateTime`, `Vector2/3/4`,
  `Color`, nullable variants, and arrays/lists/dictionaries of supported leaves. The final
  list is fixed with the serializer.
- No cycles, delegates, tasks, threads, engine entities, or runtime `Sprite` handles.
- Polymorphism is explicit (type discriminator) or absent in v1; no assembly-scanning
  serializer.
- Missing TOML values use the C# default; required values are validated by the editor/baker.
- Asset references are strings (canonical paths), validated at bake time.
- The model must be NativeAOT-friendly. Source generation is the runtime path; the editor may
  use reflection for the property grid because it is JIT.

TOML files may contain comments, but whether round-tripping preserves them is `Open`
(Section 10). The build output is deterministic regardless of formatting.

## 4. Sprite and texture sources

Sprite and texture TOML are first-class asset sources, not custom game classes:

- `textures/<file>.toml` is a Unity-style sidecar for the source texture; it carries import
  settings (filter/wrap), not sprite slices. The baker writes a MessagePack descriptor at
  `textures/<name>` and a raw payload at `textures/raw/<name>`.
- `sprites/<...>.toml` describes a quad: texture path, UVs, pixels-per-unit scale, pivot,
  topology, and optional indices; the baker writes a MessagePack sprite asset at
  `sprites/<...>`.
- Both are generated/edited by `Bork2d.Editor` and validated by the same baker.

Schemas are in [Assets: texture descriptors](assets.md#8-texture-descriptors) and
[Assets: sprite assets](assets.md#9-sprite-assets).

## 5. Asset references

- Files reference **canonical asset paths**, never `Sprite.Id` or pack offsets.
- `Sprite` is a runtime handle; it is session-local and is never serialized.
- The editor validates that referenced paths exist in the source tree.
- The baker validates that references resolve to a baked asset of the right kind.
- The loader resolves paths through the overlay stack at runtime. A missing reference is a
  content error: diagnostic + invisible renderable, not a crash.
- Save data (future) uses the same path references, not handles.

## 6. Editor

`Bork2d.Editor` (Avalonia, JIT) provides:

- a property grid generated from registered serializable types;
- a raw TOML view for the same file;
- validation of required fields, ranges (if attributes exist), asset references, and cycles;
- texture slicing that writes sprite TOML and texture sidecar import settings;
- invocation of the baker/pack build.

The editor uses compile-time registrations:

```csharp
new Bork2dEditorBuilder()
    .WithProject("../MyGame.Common")
    .WithAssetType<EnemyDefinition>()
    .Build()
    .Run();
```

No assembly scanning is required for the editable type set. The editor may use reflection for
property metadata because it is JIT. See [Editor](editor.md).

## 7. Build pipeline

1. **Parse/validate:** the baker parses every TOML file into its model, validates required
   fields, asset references, and sprite geometry.
2. **Import textures:** read source images/raw RGBA and emit raw RGBA8 payloads (KTX2 later)
   plus MessagePack texture descriptors.
3. **Bake data:** serialize custom data and sprite assets to MessagePack with Nerdbank
   source-generated shapes.
4. **Pack:** the pack builder collects MessagePack assets and raw payloads, computes offsets,
   compresses the header and release payloads, and writes `assets.pck` (see
   [Assets: build pipeline](assets.md#11-build-pipeline)).
5. **Report:** all errors carry file/line context and are surfaced by the editor and CI.

Build modes:

- Debug: header compressed with XXH3-64 checksum; asset payloads uncompressed.
- Release: header compressed; payloads Zstd-max, per-asset only if smaller; deterministic.

The baker/pack tool and the editor share validation code. The runtime never invokes them.

## 8. NativeAOT serialization

- Runtime custom data uses
  [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) source-generated
  shapes (`[GenerateShape]` on the top-level type).
- `LoadAsset<T>` requires a generated shape. Missing shapes fail at build or startup with the
  type name; there is no reflection fallback.
- Built-in asset types (pack header, texture descriptor, sprite asset) also use generated or
  hand-written readers.
- No TOML parser, reflection serializer, assembly scanning, or dynamic code in the runtime.
- The baker/editor may use reflection over the game assembly; they are JIT and not shipped.

See [NativeAOT](nativeaot.md#4-serialization-and-assets).

## 9. Versioning and evolution

- Every baked data asset carries a schema/format version.
- Additive changes with defaults are allowed within a version; renames/removals need a
  migration or a version bump.
- The editor preserves unknown TOML fields where practical so older editors do not destroy
  newer data. Comment preservation is verified separately.
- Runtime errors identify the asset path, schema version, and a JSON-path-like location.
- Save-game migration is out of scope.
- Pack format versioning is separate from data schema versioning; both are validated.

## 10. Open decisions

| Topic | Question | Proposed default |
| --- | --- | --- |
| TOML library | Tomlyn or another parser; comment/trivia preservation. | Tomlyn; preserve comments if practical. |
| MessagePack setup | Nerdbank.MessagePack shape generation and analyzer policy. | `[GenerateShape]` on serialized roots; reflection resolver disabled. |
| Attributes | Names, location (`Bork2d.Api` vs separate), required or optional. | Small public attributes in Api. |
| Polymorphism | Type discriminator or unsupported in v1. | Unsupported unless needed. |
| Collections | Dictionary key restrictions and deterministic ordering. | String/enum keys; stable sort. |
| Type-to-file mapping | How a registered type maps to its TOML files. | Project descriptor mapping; exact schema open. |
| Project descriptor | `bork2d.project.toml` fields for assets, types, game assembly. | Define in M4/M5. |
| Comments | Must the editor preserve comments and formatting? | Strong preference; verify. |
| Debug TOML | Read TOML directly in debug runtime? | No; runtime is MessagePack-only. |
| Build tool | MSBuild target, CLI, or both. | CLI + MSBuild wrapper in M4. |
| Localization | Text tables as data assets in v1? | Not in v1. |

## 11. Non-goals

- scene/prefab serialization and entity placement;
- save-game format and migrations;
- hot reload of TOML or MessagePack;
- arbitrary C# object graphs (`TypeNameHandling`-style);
- runtime TOML parsing;
- reflection-based runtime serialization;
- editor scene editing.

## 12. Current status

| Area | Current | Target |
| --- | --- | --- |
| TOML model/attributes | None | M4 |
| TOML parser/writer | None | M4 |
| MessagePack source generation | None | Nerdbank.MessagePack shapes (M3/M4) |
| Baker | None | M4 |
| Sprite/texture sources | None | Sprite TOML + texture sidecars (M4/M5) |
| Runtime data loading | `LoadAsset<T>` declaration only | M3/M4 |
| Editor property grid | None | M5 |
| Pack builder | None | M3/M4 |
| NativeAOT runtime | None | M1 baseline, M4 data |

## 13. Related documents

- [Assets and pack files](assets.md) — pack format, overlays, texture/sprite assets.
- [Editor](editor.md) — authoring and validation.
- [NativeAOT](nativeaot.md) — runtime serialization constraints.
- [API guide](../api/guide.md#10-assets) — how gameplay requests assets.
- [Roadmap](../roadmap.md) — M4, M5.
