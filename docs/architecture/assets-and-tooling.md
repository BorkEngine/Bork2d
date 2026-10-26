# Assets, Bake, and Editor Tooling

> **Status:** M3 and the post-M3 asset-reference refactor (D-035) are merged into `develop`
> (2026-10-26, PR #4 and PR #6); **M4 serialization and bake is implementation complete on
> `m-4/common` (final PR pending, not merged)**. `Bork2d.Assets` provides the v1 pack
> reader/writer and the public `Bork2d.Assets.Pack.PackBuilder`/`PackCompressionMode` tooling
> surface, canonical paths, the internal base/overlay VFS with `LoadPack`/`Browse`/`Exists`, one
> loader thread with `LoadData`/`LoadAsset<T>`, generic `AssetRef<T>`/`DataRef` reference validation
> at pack build, and the app-scoped service wired into `Run()`/`Dispose()`. `SpriteAsset`,
> `TextureDescriptor`, and `SpriteTopology` are public `Bork2d.Api` assets; there is no
> `LoadSprite` handle path. M4 adds the JIT `Bork2d.Bake` library and `Bork2d.Bake.Cli` (descriptor,
> generated host, shape-driven Tomlyn DOM binder, texture import, reference validation, incremental
> bake, atomic pack write), the MSBuild targets, and the `Bork2d.BakeFixture` game-side fixture; the
> default AOT smoke loads the baked fixture pack. The runtime gained only the approved D-M4-11
> struct-root load relaxation and the D-M4-12 `Color` marshaler. The [M3 record](../specs/m3-assets.md)
> is historical; the [refactor record](../specs/refactor-asset-references.md) records the public
> asset-model change; the [M4 spec](../specs/m4-serialization-and-bake.md) records bake scope and
> as-built evidence, with the [WP0 report](../specs/m4-wp0-spike.md) for the feasibility findings.
> The editor is **M5**.
> `IEngine.Assets` returns the real service in every mode, including the `WithoutAssets()` empty
> filesystem. `IAssets` member contracts and errors are owned by the
> [API reference](../api/reference.md#7-iassets); this document owns the pack/VFS/asset model and
> the bake pipeline.

## 1. End-to-end model

```
Project.Common/Assets/
  data/**.toml  roots/**.toml  sprites/**.toml  textures/* (+ <file>.toml sidecars)
    │ M4 JIT tooling (bork2d-bake): descriptor → generated host → Tomlyn DOM bind →
    │ PolyType generated-shape MessagePack; textures → RGBA8 → TextureDescriptor + raw payload;
    │ binding/reference validation with file:line:column (no sprite field validation in M4)
    ▼
MessagePack assets + raw payloads
    │ pack builder (M3/M4): header + XXH3-64; reference validation; Zstd max if smaller
    ▼
assets.pck (+ overlays) → VFS (NativeAOT) → loader thread → deserialized assets → renderer
```

- Runtime reads packs only: no runtime TOML, reflection serializer, or source tree.
- Pack parsing, public sprite/texture assets, and custom data use
  [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) generated shapes.
- Game code loads `SpriteAsset` like any other asset and assigns it to an `ISpriteRender`; there is
  no sprite handle, global registry, or readiness state.
- Asset-to-asset links are typed path values: `AssetRef<TAsset>` for MessagePack assets and
  `DataRef` for raw data. `null` means absent.
- Textures are raw bytes (RGBA8 now, KTX2 later); sprites carry UVs, pivot, topology, optional
  indices, and a pixels-per-unit scale, with no vertices array.

## 2. Asset kinds

| Kind | `IsRaw` | Backing representation | Examples |
| --- | --- | --- | --- |
| MessagePack asset | `false` | MessagePack object | Sprite, texture descriptor, custom data |
| Raw asset | `true` | Opaque bytes | RGBA8 payload, KTX2 later |

- Generated shapes decode MessagePack assets; raw assets are not decoded by the pack layer, and their
  consumer (texture uploader, `LoadData`, future audio) interprets the bytes. A path has one kind per
  pack, and TOML never appears in a pack.

## 3. Paths

Canonical paths are the runtime asset keys: lowercase ASCII; `/` separators, never `\`; no
leading/trailing `/`, empty segments, or `.`/`..` segments; **no source extension**
(`textures/bg`, not `textures/bg.png`); segments use lowercase letters, digits, `_`, and `-`.

```
textures/player
textures/raw/player
sprites/player/idle
```

- The loader lowercases input and rejects invalid segments. The baker strips source extensions and
  the `.toml` suffix (`textures/player.png` + `textures/player.png.toml` → `textures/player`).
- Duplicate paths in one pack are build errors. `textures/raw/` is reserved for raw payloads; sprites
  reference the descriptor, never the payload. Paths imply no extension or decoder; the asset type or
  consuming API decides how bytes are interpreted.

## 4. Pack binary format

### 4.1 Binary layout

All fixed-width fields are little-endian; the header is a Zstd-compressed MessagePack object.

| Offset | Size | Field | Notes |
| --- | ---: | --- | --- |
| `0` | 8 | `magic` | ASCII `BORK2DPK` |
| `8` | 4 | `version` | `uint32`, currently `1`; **outside** the compressed header |
| `12` | 4 | `headerCompressedLength` | `uint32`, stored byte length of the compressed header |
| `16` | 4 | `headerUncompressedLength` | `uint32`, expected byte length after decompression |
| `20` | 8 | `headerChecksum` | `uint64`, XXH3-64 of the uncompressed header |
| `28` | `headerCompressedLength` | `header` | Zstd-compressed MessagePack `PackHeader` |
| `28 + headerCompressedLength` | … | `data` | Asset payloads, addressed by absolute offsets |

- The 28-byte prefix is fixed; only its layout is stable across versions. `version` sits outside the
  compressed header so a loader picks a v1/v2 parser before deserializing; `PackHeader` is v1 only.
- The header is always maximum-compression Zstd, in debug and release, for a stable structure.
  `dataStart = 28 + headerCompressedLength`; entry offsets are absolute, at least `dataStart`, and
  `offset + length` must not exceed the file size.
- `headerChecksum` is XXH3-64 (`XxHash3`, seed `0`) of the **uncompressed** header via
  [`System.IO.Hashing`](https://www.nuget.org/packages/System.IO.Hashing)
  ([`XxHash3`](https://learn.microsoft.com/dotnet/api/system.io.hashing.xxhash3)); the loader
  decompresses, verifies, then deserializes, rejecting the pack before any entry is trusted.
- Payloads have **no checksum**; corruption is caught by codec failure, declared sizes, and bounds.

### 4.2 Header classes

The header is a MessagePack **object**, not an array. Version 1 uses generated-shape classes:

```csharp
[GenerateShape]
internal sealed partial class PackHeader
{
    [Key(0)]
    public required PackEntry[] Entries { get; init; }
}

internal sealed partial class PackEntry
{
    [Key(0)]
    public required string Path { get; init; }

    [Key(1)]
    public bool IsRaw { get; init; }

    [Key(2)]
    public bool IsCompressed { get; init; }

    [Key(3)]
    public ulong Offset { get; init; }

    [Key(4)]
    public uint Length { get; init; }

    [Key(5)]
    public uint Size { get; init; }
}
```

Only `PackHeader` carries `[GenerateShape]`; `PackEntry`'s shape is generated transitively, which
keeps the pack schema free of a reflection resolver.

| Field | Type | Meaning |
| --- | --- | --- |
| `PackHeader.Entries` | `PackEntry[]` | Asset entries; required, may be empty, never null. |
| `PackEntry.Path` | `string` | Canonical asset path; required. |
| `PackEntry.IsRaw` | `bool` | `true`: raw bytes; `false`: MessagePack. |
| `PackEntry.IsCompressed` | `bool` | Whether stored bytes are Zstd-compressed. |
| `PackEntry.Offset` | `uint64` | Absolute byte offset from the pack start. |
| `PackEntry.Length` | `uint32` | Stored byte count (compressed length when compressed). |
| `PackEntry.Size` | `uint32` | Byte count after decompression; equals `Length` when uncompressed. |

Entry order is insignificant, but the builder sorts by `Path` for a stable structure and the loader
builds a dictionary keyed by canonical path. No entry is parsed until the header checks pass.

### 4.3 Validation and compression

The loader rejects a pack when: magic is not `BORK2DPK`; the version is unknown (checked before
decompression); header lengths are zero or exceed the file; decompression does not produce exactly
`headerUncompressedLength` bytes; the checksum does not match XXH3-64 of the uncompressed header; the
header is not a v1 `PackHeader` or `Entries` is null; a path is empty, non-canonical, duplicated, or
escapes the root; `Offset < dataStart`, `Offset + Length` overflows or exceeds the file length, or
ranges overlap; uncompressed `Length != Size`; or compressed data does not produce exactly `Size`
bytes. There are **no policy size caps**: the format field widths (`uint32` lengths/sizes, `uint64`
offset), the pack file length, range/overlap checks, and .NET allocation limits are the only bounds.
A corrupt pack fails with a diagnostic, never an out-of-bounds read.

**Compression.** Zstd (.NET 11 built-in; no third-party dependency). The header is always maximum
Zstd; payloads are uncompressed in debug and maximum Zstd in release **only when strictly smaller**.
`Size` is the decompressed length and `Length` the stored length; decompression enforces the declared
`Size`. Payloads have **no checksum**. Keep the format behind the reader/writer.

**Determinism.** The writer sorts entries by canonical path and uses a fixed schema, but there is
**no byte-identical guarantee**: Zstd output and runtime versions may produce different bytes. Tests
assert structure, round-trip, entry order, and validation, not hashes.

## 5. Virtual filesystem, overlays, and browsing

1. `assets.pck` is the base pack, mounted at startup.
2. `IAssets.LoadPack(path)` mounts a pack **above** the current stack.
3. Lookup walks top-down; later packs shadow earlier ones by canonical path.
4. There is **no unload** in v1: a mounted pack stays for the process lifetime.

- The default pack is `assets.pck`; `.WithAssetsPack(path)` overrides it. The base pack is opened at
  `Run()` before the window/`Setup` (D-M3-01), so `Build()` stays filesystem-free. A missing or
  malformed base pack throws `Bork2dException` there. `.WithoutAssets()` opts out: the app starts
  with an empty VFS and needs no base pack, while `LoadPack` may still mount overlays. `LoadPack`
  throws for a bad overlay; the stack is unchanged.
- **Status: `Current` (M3).** The internal `AssetFileSystem` implements the immutable base/overlay
  stack, canonical path normalization, filesystem path resolution, `LoadPack` mounting,
  `Browse`/`Exists` lookup, and `AssetInfo` projection. Mounting parses outside the swap lock and
  publishes a new snapshot atomically; a failed mount leaves the stack unchanged; there is no
  unload.
- **Status: `Current` (M3).** The internal `AssetService` implements `LoadPack`/`Browse`/`Exists`
  delegation, `LoadData`, and `LoadAsset<T>`, with one loader thread, an unbounded FIFO queue,
  enqueue-time entry resolution, cancellation/join at shutdown, and the public
  `AssetException`/`AssetErrorKind` contract. It no longer owns sprite handles, CPU sprite
  resources, or descriptor caches; runtime loading is deserialize-only. The runtime starts it
  before the platform/`Setup` and stops it after `Teardown`; `Bork2dApp.Dispose()` disposes an
  unstarted or stopped service.
- `LoadPack` is thread-safe; mount during `Setup`/before the first tick. A later mount shadows
  earlier paths for new lookups; an in-flight request keeps the entry resolved at enqueue time.
- `Browse(prefix)` returns a merged snapshot with shadowed entries hidden (`prefix` is canonical or
  `null` for the whole tree); `AssetInfo` exposes `Path`, `IsRaw`, `IsCompressed`, `Length`, `Size`,
  and `PackPath`. `Exists` applies the same rules. Runtime mounts packs only; loose files, source TOML,
  and source images belong to `Project.Common` and the editor.

## 6. Public asset API

`IAssets` members, defaults, and the error contract are owned by the
[API reference §7](../api/reference.md#7-iassets). This section records the asset-model types and
the pack-specific rules.

Public `SpriteAsset`, `TextureDescriptor`, and `SpriteTopology` live in `Bork2d.Api.Assets`:

- `SpriteAsset.Texture` is an `AssetRef<TextureDescriptor>`.
- `TextureDescriptor.Payload` is a `DataRef`.
- A `null` reference is absent and valid; empty/non-canonical paths are rejected by `PackBuilder`.
- `ISpriteRender<TLayer>.Sprite` is a `SpriteAsset?`; `null` draws nothing.

`AssetRef<TAsset>`/`DataRef` are typed canonical-path values serialized as strings. `Bork2d.Api`
references PolyType `1.4.1` for `IShapeable<T>` and the path marshalers (D-M3-22, D-035), and stays
free of Nerdbank.MessagePack, Zstd, `System.IO.Hashing`, and file I/O. The public marshalers
(`AssetRefMarshaler<TAsset>`, `DataRefMarshaler`) are infrastructure for consumer assemblies, not
game-facing API.

`WithoutAssets()` selects an empty VFS with no base pack and still allows `LoadPack` overlays; it is
mutually exclusive with `WithAssetsPack`. All members are thread-safe; `LoadPack` is synchronous and
loads complete asynchronously. Do not call `Browse` in `Tick`; v1 has no unload. After
`Run()`/`Dispose()` every member throws `ObjectDisposedException` while `IEngine.Assets` never throws
(D-M3-14).

## 7. Loading, pack validation, and failure

`Status: loader and pack-build reference validation Current (M3/D-035); GPU readiness/upload
Planned (M8).`

Thread ownership, enqueue-time snapshotting, task completion, and shutdown are owned by
[Threading §6](threading.md#6-asset-and-pack-coordination); `IAssets` error kinds are owned by the
[API reference §7.1](../api/reference.md#71-assetexception-and-asseterrorkind). Pack-specific rules:

- **Deserialize-only.** Packs are validated structurally at open; payload decompression is lazy per
  read. `LoadData` returns a fresh `byte[]`; `LoadAsset<T>` streams through the compiled shape and
  drains the stream, so a declared-size mismatch is a decode failure. Neither path caches, and there
  is no post-deserialize validation.
- **Sprite asset loading.** `LoadAsset<SpriteAsset>` is a normal MessagePack read. It does not
  resolve `Texture`, decode the descriptor, read the raw payload, or run UV/scale/pivot/topology
  checks. The renderer later resolves references and skips a sprite whose texture is absent or
  missing. There is no handle, readiness state, placeholder, or sprite-specific retry.
- **References.** `AssetRef<T>`/`DataRef` hold canonical paths; `null` is absent. The reference
  overloads throw `ArgumentException` for an absent reference, and a present but missing link faults
  `NotFound` when loaded.
- **Pack-time reference validation.** `PackBuilder.AddMessagePack<T>` walks the value graph with the
  generated PolyType shape and collects every reference. `Build`/`Write` validate each non-null
  reference against the final entry table: canonical path, entry exists, and kind matches
  (`AssetRef` → MessagePack, `DataRef` → raw). A violation throws `Bork2dException` naming the
  source asset and property path; a failed `Write` does not create or truncate the output.
- **Field validation.** UV extents, scale, pivot, topology, dimensions, format, filter/wrap, and
  payload-size consistency are not runtime checks. The M4 baker validates descriptor dimensions,
  format/filter/wrap, and payload size, and validates binding and references for all custom data; it
  does **not** validate sprite UVs, scale, pivot, topology, or indices (D-M4-06). Sprite field
  validation and slicing belong to M5 (D-035's broader sentence is narrowed by D-M4-06).
- **No retry and no renderer.** There is no automatic retry, loose-file fallback, or blocking wait
  (D-M3-18). The loader never creates GPU resources; M8 creates GPU pages/uploads on the render
  thread and resolves sprite references at draw time. A sprite is drawable only when its asset,
  descriptor, raw payload, and GPU page are available; a missing stage emits no instance.
- **Logging** is owned by [Logging §5.2](logging.md#52-asset-service-m3): start, overlay mounted,
  load failure, load success at Debug, and stop; nothing per frame.

## 8. Texture descriptors

`Status: public model Current (D-035); texture import and descriptor field validation Current (M4);
GPU upload and KTX2 Planned (M6/M8).`

A texture is a MessagePack **descriptor** plus a **raw payload**: `textures/player` and
`textures/raw/player`.

| Field | Type | Meaning |
| --- | --- | --- |
| `Payload` | `DataRef` | Raw asset reference containing RGBA8 pixels (`null` = absent). |
| `Width` | `uint` | Pixel width. |
| `Height` | `uint` | Pixel height. |
| `Format` | `string?` | `rgba8` in v1; KTX2 is later. |
| `Filter` | `string?` | `nearest` or `linear`. |
| `Wrap` | `string?` | `clamp` or `repeat`. |

These are the CLR property names; Nerdbank writes them verbatim as the MessagePack map keys, so the
pack stores `Payload`, `Width`, and so on (not lowercase field names).

`rgba8` payload: width × height × 4 bytes, row-major, top-left origin, no padding, straight alpha,
8 bits per channel, treated as sRGB. The source is a sidecar carrying **import settings only**
(`filter`, `wrap`); the baker writes `textures/player` with payload `textures/raw/player`, and
sprite definitions in `sprites/**.toml` reference the descriptor.

- **Public shape.** `TextureDescriptor` is a public `[GenerateShape]` class in
  `Bork2d.Api.Assets` with PascalCase property-name keys (no `[Key]`) and no per-asset version.
  `System.Numerics.Vector2` uses Nerdbank's generated shape; there is no reflection resolver.
- **No runtime validation.** `LoadAsset<TextureDescriptor>` returns the packed descriptor as
  stored. `PackBuilder` validates that a non-null `Payload` exists and targets a raw entry. The M4
  texture pipeline owns canonical-path, dimension, format, filter/wrap, and payload-size checks; the
  M5 editor reuses them.
- **No payload read.** The loader never decompresses or uploads the payload; M8 reads it on first
  texture use. There is no CPU texture resource or descriptor cache.

## 9. Sprite assets

`Status: public model Current (D-035); M4 bakes SpriteAsset as ordinary custom data (no sprite field
validation); field validation and slicing are M5 (D-M4-06); GPU/draw Planned (M8).`

A sprite is a MessagePack asset (for example `sprites/player/idle`) describing a textured quad:

| Field | Type | Meaning |
| --- | --- | --- |
| `Texture` | `AssetRef<TextureDescriptor>` | Texture descriptor reference; null = absent. |
| `Uvs` | `Vector2[]?` | Normalized UVs, top-left origin; four when present. |
| `Scale` | `float` | Pixels per world unit. |
| `Pivot` | `Vector2` | Normalized origin inside the quad; default `(0.5, 0.5)`. |
| `Topology` | `SpriteTopology` | `Quad` (0), `TriangleStrip` (1), or `Triangles` (2); an integer in the pack. |
| `Indices` | `ushort[]?` | Optional index buffer; null for `Quad`. |

The pack keys are these property names (`Texture`, `Uvs`, …), not lowercase.

There is no `vertices` array; the vertex shader generates positions from UVs, texture size, scale,
and pivot.

- Size: `pixelWidth = (maxU - minU) * textureWidth`, `pixelHeight = (maxV - minV) * textureHeight`,
  then `worldWidth = pixelWidth / Scale`, likewise for height; `Transform.LocalScale` multiplies
  after. `Scale` is pixels per unit, so a 64 px sprite at `Scale = 16` is 4 units high.
- `Pivot` uses a top-left origin: `(0,0)` is top-left, `(1,1)` bottom-right, `(0.5,0.5)` center. The
  quad is positioned around the pivot and converted to world Y-up; UVs stay top-left origin,
  ordered top-left, top-right, bottom-right, bottom-left.
- `Quad` uses the four generated corners; indexed topologies are preserved for the future indexed
  path. v1 draws `Quad` only; non-Quad assets are never silently treated as quads.
- **Public shape.** `SpriteAsset` is a public `[GenerateShape]` class in `Bork2d.Api.Assets`.
  `PackBuilder` validates a non-null `Texture` reference exists and targets a MessagePack entry.
  The loader does not validate UVs, scale, pivot, topology, or indices, and neither does the M4
  baker: a `sprites/` prefix is mapped to `SpriteAsset` and baked as ordinary custom data, so an
  invalid sprite value can bake in M4. M5 adds sprite field validation and slicing (D-M4-06).
- M4 bakes the MessagePack form from TOML; the M5 editor writes TOML and adds slicing. The renderer
  resolves sprite asset → descriptor → payload → GPU page; tint, layer, z-index, and transform stay
  on the renderable. `ISpriteRender.Sprite` holds a loaded `SpriteAsset?` directly.

## 10. TOML source model

`Status: Current (M4) for custom data and sprite-as-data. The M4 baker implements binding,
required/unknown/type/range checks, and reference validation. Sprite field validation and slicing
are M5 (D-M4-06).`

Data is authored as TOML for readable, reviewable, diffable git history, then baked to MessagePack.
One model describes both formats: the same `Bork2d.Api`-referencing C# type is the TOML authoring
model and the MessagePack runtime model. There is no second DTO and no hand-written TOML schema.

### 10.1 Registration and generated shapes

- A custom-data root carries PolyType `[GenerateShape]` and is `partial`. **`partial` is PolyType's
  requirement, not a Bork2d attribute or generator requirement**: on .NET 8+ `[GenerateShape]` injects
  `IShapeable<T>` through a partial declaration. `SpriteAsset`, `TextureDescriptor`, and the M4
  fixture types already follow that pattern. There is no `[Bork2dSerializable]Attribute`, no
  `Bork2d.Bake.Generators` project, and no assembly scanning. `GenerateShapeAttribute` is
  `[Conditional("NEVER")]`, so the host generator validates that the compiled type implements
  `IShapeable<T>` instead of reading the attribute from metadata.
- The **project descriptor is the registration list**: each `[[types]]` row maps a canonical source
  prefix to a CLR full name (§11.1). Type resolution searches `Project.Common`, the assemblies it
  references, then `Bork2d.Api` (Q-04); a mapped `internal` type needs the fixed-name IVT grant. A
  mapped type without a compiled shape is a `BK1xxx` generate-time diagnostic. Registration is the
  descriptor entry itself; there is no Bork2d marker.
- `Color` is the exception (D-M4-12): `[TypeShape(Marshaler = typeof(ColorMarshaler))]`, non-
  `partial`, serialized as the linear float surrogate `[r, g, b, a]`. A `null` surrogate is a
  `Decode` fault.
- A root may be a class or any struct, including a `readonly record struct` (D-M4-11); structs work
  as members and as roots, and a `nil` root payload is a `Decode` fault rather than a silent default.

### 10.2 Mapping, requiredness, and diagnostics

The generated host implements the mapping with a **generic shape-driven DOM binder** over Tomlyn's
`TomlTable` plus a `SyntaxParser` value-span index (Q-06/Q-07). It does **not** use Tomlyn's
POCO/source-gen mapper: that path cannot reject unknown keys or expose value spans. The authoring
attributes remain the BCL `System.Text.Json.Serialization` ones, read from the compiled shape's
attribute provider.

- Keys are camelCase from the member name. `[JsonPropertyName]` overrides the key and
  `[JsonIgnore]` excludes a member; `[JsonRequired]`/`[TomlRequired]` and the C# `required` keyword
  mark mandatory values.
- Supported leaves: `bool`; `sbyte`…`ulong`/`nint`/`nuint` (range-checked, integer→integer only);
  `float`/`double` (integer→float is allowed); `string`; enums as exact camelCase names (integer
  values are rejected); `Guid`; offset `DateTime`/`DateTimeOffset` (local date-times are errors);
  `Vector2/3/4` as float arrays; `Color` as `[r, g, b, a]`; `AssetRef<T>`/`DataRef` as canonical
  path strings; arrays/lists (including TOML table arrays) and `Dictionary<string, T>`; nested
  tables; nullable/absent values (TOML has no null, so absence means null/default). `char`, sets,
  immutable collections, `object`, polymorphism, delegates, tasks, and engine entities are
  unsupported and produce a diagnostic.
- A missing `required`/`[JsonRequired]` member is an error; a non-nullable reference member must be
  non-null after binding; value types and collection initializers keep their C# defaults when the key
  is absent.
- **Unknown keys are errors** with `file:line:column`; reads are case-insensitive, but a key must
  still match the mapped member (Q-05).
- **References** are collected during binding with the exact value span. Every present
  `AssetRef<T>`/`DataRef` is validated against the complete baked set before the pack is written:
  canonical path, entry exists, kind matches, and the target type matches when known (§9 of the
  [M4 spec](../specs/m4-serialization-and-bake.md#9-references)). An absent key is a valid absent
  reference.
- **Diagnostics** are MSBuild-canonical (`path(line,column): error BKxxxx: message`), sorted by
  file, line, column, code, then message. Codes: `BK1xxx` descriptor/type/host, `BK2xxx` TOML parse
  and binding, `BK3xxx` references, `BK4xxx` textures, `BK5xxx` pack/incremental, `BK6xxx`
  MSBuild/host-build integration. Source diagnostics are errors by default.

- No cycles, delegates, tasks, threads, engine entities, or loaded `SpriteAsset` instances.
  Polymorphism is explicit or absent in v1; there is no assembly-scanning serializer.
- References are typed canonical paths (`AssetRef<TAsset>`/`DataRef`) serialized as strings,
  validated at bake/pack time, never handles or pack offsets; the editor checks the source tree and
  the baker checks baked assets. `PackBuilder` validates pack-time reference integrity as the last
  line of defense. A missing reference is a bake error; save data uses paths.
- There is **no per-asset schema version** (D-M3-05): the pack prefix `version` is the only format
  version. A shape change is a pack-version change/migration; pre-1.0 breaking changes are allowed by
  D-014. Pack output has no byte-level determinism guarantee (D-M3-08); structural determinism is
  defined in §11.5, and the runtime never parses TOML.

## 11. Build pipeline

`Status: Current (M4).` Source lives under `Project.Common/Assets`: mapped custom-data TOML
(`data/`, `roots/`, and `sprites/` when mapped), source textures, and `<file>.toml` import sidecars.
TOML is the source of truth; source textures are regular files with a sidecar carrying import
settings only. M4 has no sprite source schema and no slicing; slices arrive with M5.

### 11.1 Project descriptor

`bork2d.project.toml` is committed at the game root and parsed by the JIT tooling (never packed):

```toml
schema = 1
name = "MyGame"

[assets]
root = "Assets"                            # required; relative to this descriptor

[bake]
pack = "assets.pck"                        # literal default; --output wins
host = "obj/bork2d/bakehost"               # generated host directory

[textures]
filter = "linear"                          # nearest | linear
wrap = "clamp"                             # clamp | repeat

[[types]]
prefix = "data"
type = "MyGame.Data.GameConfig"
```

`schema` must be `1`; `[assets].root` is required and must exist; every relative path resolves
against the descriptor directory. `[[types]]` maps a canonical source prefix (matched on a `/`
segment boundary, longest prefix wins) to a CLR full name (§10.1). Duplicate exact prefixes,
overlapping prefixes without a deterministic winner, unknown keys, an unmapped `.toml`, and an
unknown/ambiguous/missing-shape type are `BK1xxx`/`BK2xxx` diagnostics with descriptor line/column.
The texture defaults apply when a sidecar omits `filter`/`wrap`.

### 11.2 Generated host

1. The mapped game assembly builds. Each root carries `[GenerateShape]` on a `partial` declaration;
   PolyType's generator has emitted `IShapeable<T>` into that assembly.
2. `bork2d-bake generate-host --project … --common <Project.Common.dll> --output
   obj/bork2d/bakehost --configuration <Debug|Release>` reads the descriptor and the game assembly's
   metadata (a `MetadataLoadContext`; it does not execute game code), resolves the mapped types,
   validates `IShapeable<T>`, and writes `BakeHost.csproj` plus `BakeManifest.g.cs` with the static
   type manifest and per-type `Bind`/`AddToPack` delegates. Generation is content-hash gated
   (`obj/bork2d/bake/host-generation.json`).
3. MSBuild builds `BakeHost.csproj` in the current configuration; the generated host references the
   mapped assemblies by absolute `HintPath` and is **not** a solution project.
4. `bork2d-bake bake --project … --host <Bork2d.BakeHost.dll>` loads the host in a non-collectible
   `AssemblyLoadContext` named `Bork2d.BakeHost` that shares the already-loaded `Bork2d.*`, `Tomlyn`,
   `PolyType`, `Nerdbank.*`, `System.*`, and `Microsoft.*` assemblies from the default context, and
   loads only the host and the game assembly from the host directory. This keeps `PackBuilder`,
   `IBakeManifest`, `SpriteAsset`, and PolyType type identities shared.

### 11.3 Custom data, textures, and references

- **Custom data.** The host's generic DOM binder (`Bork2d.Bake.Toml`) reads `TomlTable` values
  through the compiled PolyType shape, assigns constructor/property/field members, records reference
  sites with their exact value spans, and rejects unknown keys. Baked values are added to one
  `PackBuilder` with the generated per-type delegate (`builder.AddMessagePack<T>(path, (T)value)`);
  the binder never reflects over game members. Sprite TOML is baked exactly like any other mapped
  type (no `SpritePipeline`).
- **Textures.** Discovery accepts `.png`, `.jpg`/`.jpeg`, `.bmp`, and `.tga` (case-insensitive);
  `textures/<relative path>` becomes the descriptor and `textures/raw/<relative path>` the payload.
  StbImageSharp decodes to straight-alpha RGBA8 (`ColorComponents.RedGreenBlueAlpha`), with no
  premultiplication, gamma, ICC, flipping, or resampling. A missing sidecar is valid; a sidecar
  without a source, a source under `textures/raw/`, and canonical collisions are `BK4xxx` errors.
  The descriptor is created in code (authors do not hand-write `TextureDescriptor` TOML) and the
  payload must be exactly `width × height × 4`.
- **References.** The bake validates every present `AssetRef<T>`/`DataRef` against the complete
  baked set before writing: canonical path, entry exists, kind matches, and type matches when known.
  `PackBuilder` re-validates at pack build as a backstop; a user-visible failure comes from the
  baker with `file:line:column` and the member path.

### 11.4 Pack write and atomic replacement

One `PackBuilder` collects every entry. The compression mode follows the MSBuild configuration
(`Debug` → `PackCompressionMode.Debug`, `Release` → `.Release`) or `--mode`. Entries are sorted by
the builder; discovery sorts paths ordinal first. The pack is written to a temporary file in the
destination directory and atomically replaces the target only after a fully successful bake. A
failed bake leaves the previous pack and manifest unchanged and fails the build. An empty project
produces a valid empty pack; a texture-only project is valid. The v1 header is always
Zstd-compressed and XXH3-64-checksummed; M4 changes no format bytes.

### 11.5 Structural determinism

For identical descriptor, sources, game assembly, tool version, and mode, a second bake produces
the same canonical path/kind set, the same sorted entry table (path, `IsRaw`, `IsCompressed`,
`Length`, `Size`, with `Offset` derived from the layout), byte-identical MessagePack payloads,
byte-identical texture payloads and descriptor values, and identical diagnostics. Whole-pack byte
equality is **not** guaranteed (D-M3-08/D-M4-07): Zstd output may vary across runtime versions, so
tests compare the entry table and decompressed payloads, never a pack hash.

### 11.6 Incremental bake and MSBuild

- The internal, non-committed manifest is `<descriptor directory>/obj/bork2d/bake/manifest.json`:
  JSON schema 1, XXH3-128 uppercase hex, explicit field framing, deterministic field order. It
  records the descriptor hash, the sorted tracked inputs (mapped TOML, texture sources, sidecars),
  the game-side assembly closure next to the host, the host assembly hash, tool version, mode,
  output path/hash, and the asset/texture/reference counts.
- `bake` skips before loading the host only when every recorded field matches and the pack exists
  with the recorded hash. A missing/corrupt manifest, any changed/deleted source, a changed
  common/host/tool/mode, a missing pack, or an externally modified pack forces a full bake. A
  successful full bake writes the manifest atomically; a manifest write failure after a valid pack
  is a warning. `--force` bypasses both skips; `--check` never skips and writes nothing.
- `build/Bork2d.Bake.targets` is imported by the runnable app that owns the pack. After `Build` it
  builds the CLI out of process, runs `generate-host`, builds the host with the MSBuild task, and
  runs `bake`; `Publish` copies the pack to `$(PublishDir)`; `Clean` removes the generated host,
  prepared fixture copy, incremental state, and pack. Properties:
  `Bork2dProjectDescriptor`, `Bork2dCommonAssembly`/`Bork2dBakeCommonAssemblyName`,
  `Bork2dBakeHostDirectory`, `Bork2dBakeOutput`, `Bork2dBakeMode`, `Bork2dBakeVerbose`,
  `Bork2dBakeForce`, `Bork2dBakeEnabled`, and the fixture-preparation properties used by
  `Bork2d.AotSmoke`.
- Builds with no imported targets are unaffected; CI stays restore/format/build/test, and the bake
  tests run in the normal Debug pass. The local AOT gate adds the publish/run step.

### 11.7 CLI

```text
bork2d-bake generate-host --project <bork2d.project.toml> --common <Project.Common.dll>
                          --output <dir> [--configuration Debug|Release] [--force]
bork2d-bake bake          --project <bork2d.project.toml> --host <BakeHost.dll>
                          [--output <assets.pck>] [--mode debug|release]
                          [--check] [--force] [--verbose|--quiet]
bork2d-bake validate      --project <bork2d.project.toml> [--host <BakeHost.dll>]
bork2d-bake --help | --version
```

`bake` runs the full pipeline and atomically writes the pack; `--check` validates and writes
nothing; `validate` without `--host` checks the descriptor, texture sources, and TOML syntax, and
with `--host` runs the full binding/reference validation without writing a pack. Exit codes: `0`
success/up to date, `1` one or more source diagnostics, `2` usage/descriptor/host configuration
error, `3` unexpected internal failure. Diagnostics go to stderr in the MSBuild form; the summary
goes to stdout.

### 11.8 Outputs

| Artifact | Location | Committed? |
| --- | --- | --- |
| Project descriptor | game root (`bork2d.project.toml`) | yes (source) |
| Generated host project/sources | `<project>/obj/bork2d/bakehost/` | no |
| Host-generation state | `<descriptor dir>/obj/bork2d/bake/host-generation.json` | no |
| Incremental manifest | `<descriptor dir>/obj/bork2d/bake/manifest.json` | no |
| Runtime pack | default `$(TargetDir)assets.pck`, copied to `$(PublishDir)`; overridable | no |

The app opens `assets.pck` at `Run()` (or the `WithAssetsPack` path), resolved against the process
current directory or given as an absolute path (D-M3-03/Q-10). There is no "next to the executable"
fallback in the runtime; the AOT smoke passes `AppContext.BaseDirectory/assets.pck` explicitly. The
M5 editor reuses the library and shares the validator/baker; the local gates build debug and
release packs and run loader validation on both (CI builds Debug only).

## 12. Editor

`Bork2d.Editor` is an Avalonia library/app working on the source tree, not `assets.pck`; it is JIT and
not part of the game runtime. `Project.Editor` is a thin game-specific host referencing
`Bork2d.Editor` + `Project.Common`; it does not reference `Bork2d.Render`/`Bork2d.Common`, so the
runtime stays Api/Runtime-only.

```csharp
using var editor = new Bork2dEditorBuilder()
    .WithProject("../../Project.Common")
    .WithAssetType<EnemyDefinition>()
    .WithWindow<MyToolWindow>()
    .Build();
editor.Run();
```

- `WithProject` points at `Project.Common` or a descriptor (`bork2d.project.toml`; the descriptor
  and mapping format are implemented in M4).
- `WithAssetType<T>()` registers a serializable class for the property editor as a **compile-time
  registration** (no assembly scanning); the JIT editor may reflect for property metadata, and a
  source generator can replace that later without changing the API.
- `WithWindow<TWindow>()` registers an extra tool window (instance overload for constructor
  dependencies); the shell owns window lifetime, registrations are additive, and default
  browser/slicing/data windows always exist. `Build()` validates the project path and registrations;
  `Run()` starts Avalonia on the main thread; `Dispose()` closes windows and stops background work.

Slicing: open a texture and sidecar, draw a rect (manual; grid planned), set normalized pivot,
pixels-per-unit scale, and topology (`Quad` in v1; `indices` only for non-Quad), name the sprite,
preview the quad, validate bounds/overlap (proposed), and save TOML. UVs are generated (no vertices
array, see §9), and one source texture maps to one bindless page in v1.

For custom data the editor finds TOML mapped by the M4 project descriptor, shows a property grid over
public properties/fields plus a raw TOML view, validates required fields/references/ranges, and
writes canonical TOML. It edits texture sidecars and invokes the shared validator, baker, and pack
builder with file/line diagnostics; it never reimplements the pack format.

Boundary: Avalonia UI, JIT (**not** NativeAOT), no Vulkan/swapchain/engine render thread, compile-time
type registrations with reflection only for JIT property metadata, TOML/source images from
`Project.Common` (built packs never edited), work on the Avalonia UI thread with background bake/pack
only. The editor does **not** edit scenes, place entities, run the game, edit `.pck` files, implement
animation timelines, tilemaps, or materials, atlas multiple textures into one page, or support
multi-project workspaces in v1.

## 13. NativeAOT

- Runtime pack/descriptor/sprite/custom-data shapes are Nerdbank generated; `LoadAsset<T>` requires a
  shape, with no reflection resolver. No runtime reflection, `Type.GetType`, assembly scanning, runtime
  TOML, `Activator.CreateInstance`, or dynamic code in pack/asset loading.
- Zstd is the .NET 11 built-in implementation, so no extra native codec is needed; pack paths and
  asset keys are data, not types. `WithGame(instance)` avoids reflection-based game construction.
- The public tooling surface is `PackBuilder`/`PackCompressionMode` in `Bork2d.Assets.Pack`
  (D-034, M3). The M4 bake tooling (`Bork2d.Bake`, `Bork2d.Bake.Cli`, Tomlyn, StbImageSharp) is
  JIT-only and internal; no runtime project references it, and the generated host is not a solution
  project. Build-time reflection over `Project.Common` metadata is confined to host generation.
- The default AOT run loads the baked fixture pack from `AppContext.BaseDirectory/assets.pck`; it
  does not build a temporary pack. Its publish contains `assets.pck` and `SDL2.dll`, the game-side
  `Bork2d.BakeFixture` assembly (linked into the single-file executable; its PDB is present), and no
  `Bork2d.Bake`/`Bork2d.Bake.Cli`/`Bork2d.BakeHost`/Tomlyn/StbImageSharp assembly.
- Runtime projects set `IsAotCompatible` with trim/AOT analyzers; only the AOT smoke app sets
  `PublishAot`. AOT publish/run is a required local gate, not a CI job.

## 14. Threading

`Status: loader and pack-build reference validation Current (M3/D-035); renderer/GPU integration
Planned (M8).`

Loader ownership, queueing, enqueue-time resolution, task completion, and shutdown are owned by
[Threading §6](threading.md#6-asset-and-pack-coordination). Pack-specific points:

- Packs are opened and parsed by the asset service on the `Run` caller thread before the platform and
  `Setup`; the overlay stack is immutable except for the atomic `LoadPack` swap.
- The loader is deserialize-only: it caches no resolved assets and publishes no CPU resource table;
  each `LoadAsset<T>` returns a fresh instance.
- `PackBuilder` validates asset references at pack build and runs in tooling, never in the runtime.
  The M4 baker also validates binding and references before packing (with source spans), so a
  correct bake never reaches `PackBuilder`'s backstop. The M4 baker does not validate sprite fields;
  that is M5 (D-M4-06).
- The runtime has no GPU work yet. M8 creates GPU texture pages/uploads on the render thread and
  resolves `SpriteAsset.Texture`/`TextureDescriptor.Payload` at draw time; load-and-keep applies to
  packs in v1.

`WithHeadless` supports packs, `LoadData`, `LoadAsset<T>`, `Browse`, and `Exists`; sprites load as
plain assets but no GPU page exists and nothing renders. `WithOffscreen` currently keeps the
one-tick path with no GPU; windowless rendering is M6/M8. Neither mode has `IWindow`/`IInput`. See
[Runtime lifecycle](runtime-lifecycle.md#2-modes).

## 15. Limits and security

There are **no policy size caps** (D-M3-06). The bounds are intrinsic:

| Bound | Source |
| --- | --- |
| Header compressed/uncompressed lengths | `uint32` prefix fields and the file length. |
| Per-entry stored/decompressed size | `uint32` `Length`/`Size` (max 4 GiB − 1). |
| Offset | `uint64`, validated against the file length and the `long` file API. |
| Pack file | `FileStream`/`long` length; no additional cap. |
| Path and entry count | No cap; canonical validation and the header size bound them. |
| `LoadData` | `Array.MaxLength` because the API returns a single `byte[]`. |
| `LoadAsset<T>` | Streaming: the decompressed stream goes to the generated-shape deserializer. |

The **header** has an XXH3-64 checksum verified before parsing; individual **payloads have no
checksum** in v1, so a raw payload has no integrity check beyond bounds/length validation. Packs are
trusted local content: signing is out of scope and the parser is defensive against malformed input.
A malicious pack can still declare a large header/payload and cause memory pressure; M3 accepts
that in exchange for no arbitrary caps, and fails cleanly rather than reading out of bounds.

## 16. Current status

| Area | Status |
| --- | --- |
| `Bork2d.Assets` | `Current` (M3/D-035): v1 pack reader/writer, structural validation, canonical paths, public `Bork2d.Assets.Pack.PackBuilder`/`PackCompressionMode`, the internal base/overlay VFS with `LoadPack`/`Browse`/`Exists`, the app-scoped service (one thread, FIFO queue, `LoadData`/`LoadAsset<T>`, cancellation/join, `AssetException`), and generic pack-time reference validation |
| `assets.pck` reader/writer | `Current` (M3): v1 format reader and writer; the VFS, overlays, `Browse`/`Exists`, and the loader are wired through `IEngine.Assets` |
| Texture descriptors and raw RGBA8 payloads | `Current` (M3/D-035; texture import M4): public `TextureDescriptor` model with a `DataRef` payload; the M4 texture pipeline decodes PNG/JPG/BMP/TGA to straight-alpha RGBA8, emits the descriptor + raw payload, and validates dimensions, format/filter/wrap, and payload size. Payload read/upload and KTX2 are M8/M6 |
| Sprite assets (UVs/scale/pivot/topology/indices) | `Current` (M3/D-035; data bake M4): public `SpriteAsset` model with an `AssetRef<TextureDescriptor>` link, loaded via `LoadAsset<SpriteAsset>` and baked as ordinary custom data. No runtime or bake-time field validation; UV/pivot/scale/topology/indices validation and slicing are M5 (D-M4-06). Drawing is M8 |
| CPU resource table (descriptors/sprites) | Removed by D-035; the loader caches no resolved sprite/texture resources |
| Runtime asset lifecycle | `Current` (M3): `WithoutAssets()`, base pack at `Run()` before the window/`Setup`, start/stop/error order, post-shutdown `ObjectDisposedException`, app disposal |
| TOML model, baker, deterministic bake | `Current` (M4) on `m-4/common` (PR pending): `bork2d.project.toml`, generated host, shape-driven Tomlyn DOM binder, texture import, typed reference validation, structural determinism, incremental manifest/host state, atomic pack write, CLI, MSBuild targets. Sprite field validation/slicing are M5 |
| `Bork2d.Bake` / `Bork2d.Bake.Cli` | `Current` (M4): internal JIT tooling; no public types; `generate-host`, `bake`, `validate` with exit codes `0`–`3` |
| `build/Bork2d.Bake.targets` | `Current` (M4): build/publish/clean integration, fixture preparation, property overrides |
| `Bork2d.BakeFixture` | `Current` (M4, Q-16): game-side `Project.Common`-like fixture with `[GenerateShape] partial` types and an `Assets/` tree; referenced by tests and the AOT smoke, never by the runtime |
| Serialization policy | `Current` (M3/D-035): Nerdbank.MessagePack `1.4.95` pinned with generated-shape smoke in Common, tests, and AOT smoke; pack header/entry and public `SpriteAsset`/`TextureDescriptor` shapes are generated; `AssetRef<T>`/`DataRef` use public PolyType path marshalers; `Bork2d.Api` references PolyType `1.4.1`; `LoadAsset<T>` passes the shape from `T.GetTypeShape()` |
| `IAssets` load members | `Current` (M3/D-035): declarations, XML docs, the `IShapeable<T>` constraint, deserialize-only loader bodies, and `AssetRef<T>`/`DataRef` overloads; `IEngine.Assets` returns the real service in every mode |
| `IAssets.LoadPack`/`Browse`/`Exists`, `AssetInfo` | `Current` (M3): `AssetInfo`, the internal VFS/lookup, and public delegation |
| `AssetException`/`AssetErrorKind` | `Current` (M3): public error contract; pack open/parse/mount and reference-validation failures remain `Bork2dException` |
| `Bork2d.Editor` / `Project.Editor` | `Planned` M5; editor and host are absent |

## 17. Related documents

- [NativeAOT](nativeaot.md) and [Threading](threading.md#6-asset-and-pack-coordination) — runtime
  constraints and loader/upload ownership.
- [Rendering](rendering.md) — texture pages, bindless textures, sprite pipeline.
- [Runtime lifecycle](runtime-lifecycle.md#2-modes) — headless/offscreen.
- [Projects](projects.md) — `Bork2d.Bake`/`Bork2d.BakeFixture` rows and `Bork2d.Editor` references.
- [API reference](../api/reference.md#7-iassets) — `IAssets` contract.
- [M4 spec](../specs/m4-serialization-and-bake.md) and [WP0 report](../specs/m4-wp0-spike.md) —
  bake scope, feasibility findings, and as-built evidence.
- [Roadmap](../roadmap.md) and [decision log](../decisions.md) — M3/M4/M5/M8 and D-010–D-012,
  D-M4-01…D-M4-12.
