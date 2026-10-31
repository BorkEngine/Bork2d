# Assets, Bake, and Editor Tooling

> **Status:** **Current; implemented through M4 and the asset-processor task merged into `develop`
> (2026-10-27, PR #8; D-036/D-037/D-038/D-039/D-040).** `Bork2d.Engine` provides the v1 pack
> reader/writer and the public
> `Bork2d.Engine.Assets.Pack.PackBuilder`/`PackCompressionMode` tooling surface, canonical paths, the
> internal base/overlay VFS with `LoadPack`/`Browse`/`Exists`, one loader thread with
> `LoadData`/`LoadAsset<T>`, generic `AssetRef<T>`/`DataRef` reference validation at pack build, and
> the app-scoped service wired into `Run()`/`Dispose()`. `SpriteAsset`, `TextureDescriptor`,
> `SpriteTopology`, and the `TextureFormat`/`TextureFilter`/`TextureWrap` enums are public
> `Bork2d.Engine` assets; there is no `LoadSprite` handle path.
>
> The bake is the game-owned builder flow: a build-time `MyGame.Assets`/`Pong.Assets` executable
> configures ordered packs, roots, texture defaults, processors, and explicit `RegisterAsset<T>()`
> data types through the public JIT `Bork2d.Bake`, bakes on build, is the CLI
> (`bake`/`validate`/`clean`/`copy-packs`), and records a schema-3 incremental manifest. Source file
> types are registered asset processors; the built-in texture importer is one processor, and the
> Pong example's `CircleProcessor` is the worked custom processor. The project descriptor, generated
> bake host, host state/ALC/manifest, `MetadataLoadContext`, `Bork2d.Bake.Cli`, and `generate-host` are
> removed. The AOT gate (`Pong.Desktop`) loads the baked 7-entry Pong pack and its publish contains no
> Bake/assets-executable/Tomlyn/StbImageSharp assembly. The project-consolidation refactor
> (D-041/D-042, merged 2026-10-28, PR #9) merged the pack implementation into `Bork2d.Engine` and the
> builder into `Bork2d.Bake`; the Pong example (D-043/D-044, merged 2026-10-29, PR #10)
> replaced the exploratory examples. The M3 record and M4 spec are
> historical; the [file-processors spec](../specs/file-processors.md) and
> [asset processors](asset-processors.md) own the current bake design.
>
> The editor is **M5**. `IEngine.Assets` returns the real service in every mode, including the
> `WithoutAssets()` empty filesystem. `IAssets` member contracts and errors are owned by the
> [API reference](../api/reference.md#7-iassets); this document owns the pack/VFS/asset model and
> the bake pipeline.

## 1. End-to-end model

```
MyGame.Assets/Assets/ (main) + DLC1/ (overlay)
  data/**.toml                 custom data (typed [bork2d]); bound by the registered type entry
  art/**.png|jpg|...           built-in texture processor + <source>.toml sidecar
  textures/**.circle            custom processor sources (e.g. Pong's CircleProcessor; no sidecar)
    │ MyGame.Assets executable:
    │   BakeApplicationBuilder (packs, roots, texture defaults, processors, RegisterAsset<T> entries)
    │   → processor discovery + sidecar binding → registered data binder
    │   → PolyType generated-shape MessagePack (data + processor AddAsset instances) / AddData
    │   → binding/reference validation with file:line:column
    ▼
MessagePack assets + raw payloads
    │ pack builder (M3/M4): header + XXH3-64; reference validation; Zstd max if smaller
    ▼
assets.pck (+ overlays) → VFS (NativeAOT) → loader thread → deserialized assets → renderer
```

- Runtime reads packs only: no runtime TOML, reflection serializer, or source tree.
- There is no project descriptor or generated host. `MyGame.Assets` configures the bake in code;
  building it bakes; the engine's image support is a registered processor, not a special pipeline.
  The M4 descriptor and hand-written texture-sidecar parser are history.
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
art/player
art/player-raw
sprites/player/idle
```

- The loader lowercases input and rejects invalid segments. The baker strips the **matched
  processor extension** and the `.toml` suffix (`art/paddle.png` + `art/paddle.png.toml` →
  `art/paddle`; `textures/ball.circle` → `textures/ball`). There is no hardcoded extension list and no
  per-format path rule; the processor registry owns the suffix.
- Duplicate paths inside one pack are build errors. The same canonical path may exist in different
  packs: the runtime overlays packs (later mounts win, D-M3-09), so a DLC pack can replace a base
  path deliberately (D-M4-14). Processor outputs are base-path-derived: `BaseAssetPath` or
  `BaseAssetPath + "-" + suffix` (the built-in texture processor uses `art/player` and
  `art/player-raw`). A processor cannot write an unrelated path. Sprites reference the descriptor,
  never the payload. Paths imply no extension or decoder; the asset type or consuming API decides
  how bytes are interpreted.
- `.toml` is the one metadata format (D-036): typed TOML is data; `<source>.toml` for a registered
  source whose processor has a sidecar is that source's sidecar. Source extensions are processor
  registrations, not engine knowledge.

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

Public `SpriteAsset`, `TextureDescriptor`, and `SpriteTopology` live in `Bork2d.Engine.Assets`:

- `SpriteAsset.Texture` is an `AssetRef<TextureDescriptor>`.
- `TextureDescriptor.Payload` is a `DataRef`.
- A `null` reference is absent and valid; empty/non-canonical paths are rejected by `PackBuilder`.
- `ISpriteRender<TLayer>.Sprite` is a `SpriteAsset?`; `null` draws nothing.

`AssetRef<TAsset>`/`DataRef` are typed canonical-path values serialized as strings. `Bork2d.Engine`
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
  payload-size consistency are not runtime checks. The built-in texture processor validates
  descriptor dimensions, format/filter/wrap, and payload size; the bake validates binding,
  processor outputs, and references for all custom data; it does **not** validate sprite UVs, scale,
  pivot, topology, or indices (D-M4-06). Sprite field validation and slicing belong to M5 (D-035's
  broader sentence is narrowed by D-M4-06).
- **No retry and no renderer.** There is no automatic retry, loose-file fallback, or blocking wait
  (D-M3-18). The loader never creates GPU resources; M8 creates GPU pages/uploads on the render
  thread and resolves sprite references at draw time. A sprite is drawable only when its asset,
  descriptor, raw payload, and GPU page are available; a missing stage emits no instance.
- **Logging** is owned by [Logging §5.2](logging.md#52-asset-service-m3): start, overlay mounted,
  load failure, load success at Debug, and stop; nothing per frame.

## 8. Texture descriptors

`Status: public model Current (D-035); texture import and descriptor field validation Current (the
built-in texture processor, D-036); GPU upload and KTX2 Planned (M6/M8).`

A texture is a MessagePack **descriptor** plus a **raw payload**: `art/player` and
`art/player-raw` (the descriptor path plus `-raw`).

| Field | Type | Meaning |
| --- | --- | --- |
| `Payload` | `DataRef` | Raw asset reference containing RGBA8 pixels (`null` = absent). |
| `Width` | `uint` | Pixel width. |
| `Height` | `uint` | Pixel height. |
| `Format` | `TextureFormat` | `Rgba8` in v1 (the default); KTX2 is later. |
| `Filter` | `TextureFilter` | `Linear` (the default) or `Nearest`; sampler state. |
| `Wrap` | `TextureWrap` | `Clamp` (the default) or `Repeat`; sampler state. |

The three descriptor settings are non-null `Bork2d.Engine.Assets` enums (D-040), not strings, and are
stored as their underlying integer values. These are the CLR property names; Nerdbank writes them
verbatim as the MessagePack map keys, so the pack stores `Payload`, `Width`, and so on (not lowercase
field names).

`rgba8` payload: width × height × 4 bytes, row-major, top-left origin, no padding, straight alpha,
8 bits per channel, treated as sRGB. The source is a `<source>.toml` sidecar carrying **import
settings only** (`filter`, `wrap`); the built-in texture processor writes `art/player` with payload
`art/player-raw`, and sprite definitions in `sprites/**.toml` reference the descriptor. The sidecar
is a normal `[GenerateShape]` type bound by the shared TOML binder, not a hand-written parser;
missing sidecars are valid and take the builder's texture defaults
([asset processors](asset-processors.md#10-built-in-texture-processor)).

- **Public shape.** `TextureDescriptor` is a public `[GenerateShape]` class in
  `Bork2d.Engine.Assets` with PascalCase property-name keys (no `[Key]`) and no per-asset version.
  `Format`, `Filter`, and `Wrap` are the non-null `TextureFormat`/`TextureFilter`/`TextureWrap` enums
  (D-040). `System.Numerics.Vector2` uses Nerdbank's generated shape; there is no reflection resolver.
- **No runtime validation.** `LoadAsset<TextureDescriptor>` returns the packed descriptor as
  stored. `PackBuilder` validates that a non-null `Payload` exists and targets a raw entry. The
  built-in texture processor owns canonical-path, dimension, format, filter/wrap, and payload-size
  checks; the M5 editor reuses them. The processor is a normal registered processor, so a game can
  replace it for an extension with another implementation.
- **No payload read.** The loader never decompresses or uploads the payload; M8 reads it on first
  texture use. There is no CPU texture resource or descriptor cache.

## 9. Sprite assets

`Status: public model Current (D-035); the bake emits SpriteAsset as ordinary custom data (no sprite
field validation); field validation and slicing are M5 (D-M4-06); GPU/draw Planned (M8).`

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
- **Public shape.** `SpriteAsset` is a public `[GenerateShape]` class in `Bork2d.Engine.Assets`.
  `PackBuilder` validates a non-null `Texture` reference exists and targets a MessagePack entry.
  The loader does not validate UVs, scale, pivot, topology, or indices, and the bake tooling does
  not either: a data TOML declaring `type = "Bork2d.Engine.Assets.SpriteAsset"` in `[bork2d]` is baked
  as ordinary custom data, so an invalid sprite value can bake. M5 adds sprite field validation and
  slicing (D-M4-06).
- The bake tooling bakes the MessagePack form from TOML; the M5 editor writes TOML and adds slicing.
  The renderer resolves sprite asset → descriptor → payload → GPU page; tint, layer, z-index, and
  transform stay on the renderable. `ISpriteRender.Sprite` holds a loaded `SpriteAsset?` directly.

## 10. TOML source model

`Status: Current for custom data, sprites-as-data, and processor sources (D-036). The bake
implements binding, required/unknown/type/range checks, processor/sidecar handling, and reference
validation. Sprite field validation and slicing are M5 (D-M4-06).`

Data is authored as TOML for readable, reviewable, diffable git history, then baked to MessagePack.
One model describes both formats: the same `Bork2d.Engine`-referencing C# type is the TOML authoring
model and the MessagePack runtime model. There is no second DTO and no hand-written TOML schema.

### 10.1 Explicit registration and shapes

- A custom-data root carries PolyType `[GenerateShape]` and is `partial`. **`partial` is PolyType's
  requirement, not a Bork2d attribute or generator requirement**: on .NET 8+ `[GenerateShape]` injects
  `IShapeable<T>` through a partial declaration. `SpriteAsset`, `TextureDescriptor`, and the game's
  `GameSettings` types already follow that pattern. There is no `[Bork2dSerializable]Attribute`, no
  `Bork2d.Bake.Generators` project, and no assembly scanning.
- **The builder registers every authorable data type explicitly** (D-038):
  `RegisterAsset<T>() where T : IShapeable<T>`. The generic call compiles the binder/writer for `T`;
  `UseDefaultAssets()` supplies the engine authoring types (`SpriteAsset` today). There is no
  generated host, no reflection, and no `MetadataLoadContext`.
- **Type is selected in the data TOML** (D-M4-13): a reserved top-level table

  ```toml
  [bork2d]
  type = "MyGame.Data.GameConfig"
  ```

  `type` is required, non-empty, and a full CLR name without assembly qualification. Only `type` is
  allowed in `[bork2d]` in v1; missing, unknown, or mistyped keys are diagnostics at the source
  line/column. It must match a type registered with `RegisterAsset<T>()` or
  `UseDefaultAssets()`; an unregistered type is a `BK1xxx` error with the registration hint. A
  registered `internal` type needs an `InternalsVisibleTo` grant to the assets executable. There is
  no descriptor and no prefix mapping.
- The root `[bork2d]` table is metadata and is ignored by the binder: it is not an unknown key and it
  cannot bind a root `Bork2d` member. Nested tables named `bork2d` are ordinary member tables.
- A source may live anywhere under the asset root, including under `textures/`; its data asset path is
  the source path minus `.toml`. A data TOML name containing an extension or dot (`foo.png.toml`) is
  an invalid asset path.
- `Color` is the exception (D-M4-12): `[TypeShape(Marshaler = typeof(ColorMarshaler))]`, non-
  `partial`, serialized as the linear float surrogate `[r, g, b, a]`. A `null` surrogate is a
  `Decode` fault.
- A root may be a class or any struct, including a `readonly record struct` (D-M4-11); structs work
  as members and as roots, and a `nil` root payload is a `Decode` fault rather than a silent default.

### 10.2 Mapping, requiredness, and diagnostics

The builder's registered type entry implements the mapping with a **generic shape-driven DOM binder** over Tomlyn's
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
  file, line, column, code, then message. Codes: `BK1xxx` builder configuration and explicit type
  registration, `BK2xxx` TOML parse and binding (data and sidecars), `BK3xxx` references, `BK4xxx`
  built-in texture decode/probe, `BK5xxx` pack/incremental, `BK6xxx` MSBuild/build integration,
  `BK7xxx` processor registration/processing. Source diagnostics are errors by default.

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

### 10.3 Processors and sidecars

`Status: Current (D-036). Processors are the only non-TOML source mechanism; the built-in texture
importer is one of them.`

Every non-TOML source extension is handled by an `IAssetProcessor` registered in the
`MyGame.Assets` builder. A processor declares its extension set, a sidecar policy, a
`[GenerateShape]` sidecar type when settings are required, and processing code. The built-in texture importer is one processor;
users add `.circle` or any other format without touching engine code; Pong's `CircleProcessor` is
the current example. The processor contract,
registration/override rules, sidecar policies, output path rules, and diagnostics are owned by
[asset processors](asset-processors.md).

- A sidecar is `<source file>.toml`; the shared TOML binder binds it from the processor's sidecar
  shape. Sidecar binding errors are `BK2xxx`; processor errors are `BK7xxx`.
- Sidecar policies are only `None` and `Required`; `Required` sidecars are created once through the
  processor's separate `CreateDefaultSidecar` function when missing, and `None` has no sidecar.
- A later registration overrides an earlier one for the same extension. Extensions are
  case-insensitive, accept `"png"`/`".png"`, and support compound suffixes.
- A processor may emit any number of outputs at `BaseAssetPath` or `BaseAssetPath + "-<suffix>"`.
  A `[GenerateShape]` output is an instance (the engine serializes MessagePack); raw bytes are only
  for opaque data. They flow through the same baked index, reference validation, and pack composer
  as custom data.
- There is no hardcoded source-extension list, no texture-specific sidecar parser, and no
  texture-specific entry kind in the generic index/composer.

## 11. Build pipeline

`Status: Current (D-036/D-037/D-038).` The pipeline is configured and driven by `MyGame.Assets`; the
[asset processors](asset-processors.md) design doc owns the processor contract. The M4 descriptor,
generated host, and texture-specific steps described in the M4 spec are historical and are replaced
as follows.

### 11.1 Builder configuration (no descriptor)

`bork2d.project.toml` is removed. The game-owned `MyGame.Assets` executable configures the bake in
code:

```csharp
using Bork2d.Bake;
using Bork2d.Engine.Assets;
using Pong.Assets.Processors;
using Pong.Common;

return await new BakeApplicationBuilder()
    .WithProjectRoot(".")
    .WithPack("assets.pck", "Assets")
    .UseDefaultAssets()
    .RegisterAsset<GameSettings>()
    .UseDefaultProcessors()
    .WithTextureDefaults(TextureFilter.Linear, TextureWrap.Clamp)
    .AddProcessor(new CircleProcessor())
    .Build()
    .RunAsync(args);
```

At least one pack is required; the first is main and later packs are DLC/overlay. Roots and pack
paths are relative, canonical-safe, and contained by the project root. There is no common-assembly
option: each `RegisterAsset<T>()` carries its own assembly, and the builder derives the tracked
shape-assembly closure. `UseDefaultAssets()` registers `SpriteAsset`;
`RegisterAsset<T>()` explicitly registers a type that a data TOML may select with
`[bork2d].type`. `UseDefaultProcessors()` registers the built-in texture processor;
`WithTextureDefaults` replaces `[textures]`; `AddProcessor` adds custom processors, and a later
registration overrides an earlier one for an extension. There is no generated host. Output root,
mode, `--force`, `--check`, and verbosity are invocation options, not project configuration. A
builder configuration failure is a `BK1xxx` diagnostic and exit code `2`.

### 11.2 `MyGame.Assets` and explicit data binding

- `MyGame.Assets` is an executable project that references `Bork2d.Bake` and
  `Project.Common`. It contains `Assets/`, optional `DLC1/`, and `Program.cs`. It is JIT,
  build-time-only, and never enters the runtime/AOT publish graph.
- Building `MyGame.Assets` runs its build target, which invokes the freshly built executable with
  `bake` after passing the project root, configuration, and output root. The builder discovers
  sources, runs processors, binds data TOML through the registered type entries, validates
  references, and atomically writes every pack. `validate`, `clean`, and a pack-copy command remain
  available for manual use.
- **There is no generated host.** Each `RegisterAsset<T>()` creates a compile-time registration
  object that closes over `TomlShapeBinder.Bind<T>` and `PackBuilder.AddMessagePack<T>`; the
  builder stores it by `typeof(T).FullName`. A data TOML's `[bork2d].type` selects one entry. An
  unregistered type is a `BK1xxx` error naming the type and the `RegisterAsset<T>()` fix.
- Processor outputs use `AddAsset<T>` and need no type registration: the compiled shape travels
  with the generic call. The engine serializes the instance; the processor never does.
- There is no `MetadataLoadContext`, host project, host ALC, host manifest, host-generation state,
  `generate-host`, or `obj/bork2d/bakehost` directory. Data-type changes are captured by the assets
  assembly/config hash and the tracked TOML inputs.

### 11.3 Discovery, processors, and sidecars

- Each pack root is discovered in builder pack order. Discovery walks the
  tree in ordinal path order, skips reparse points, and classifies files by the processor registry:
  a registered source suffix is a processor source, a typed `.toml` is custom data, an untyped
  `<source>.toml` for a processor with a sidecar is that source's sidecar, and anything else is
  ignored or diagnosed as before.
- There is no hardcoded source-extension list. Data and texture sources may live anywhere under a
  pack root. The canonical base path is the source path minus the matched processor extension;
  processor outputs are `BaseAssetPath` or `BaseAssetPath + "-<suffix>"`.
- The built-in texture processor registers PNG/JPG/JPEG/BMP/TGA, keeps the StbImageSharp
  normalization and `BK4xxx` decode/probe rules, and emits a `TextureDescriptor` instance + raw
  RGBA8 payload. It is a `Required` sidecar processor for `filter`/`wrap`; a missing sidecar is
  created from the builder's `WithTextureDefaults` values before processing.
- Custom-data TOML is bound by the selected `RegisterAsset<T>()` entry: the shape-driven DOM binder
  reads it, records reference spans, rejects unknown keys, and adds the value with the typed
  per-type writer. Sprite TOML is ordinary custom data.
- Processor sidecars are bound by the same shape-driven binder from the processor's
  `[GenerateShape]` type. There are only `None` and `Required` policies; `Required` sidecars are
  created once by the processor's separate `CreateDefaultSidecar` function when missing. Processor
  errors are `BK7xxx`; sidecar binding errors are `BK2xxx`.
- Processors emit shaped asset instances through `AddAsset<T>` and opaque data through `AddData`.
  The engine serializes MessagePack; the processor does not. References are collected by the shape
  walk, and outputs join the same baked index as custom data, so collision and reference rules are
  identical. The generic index/composer has no texture-specific entry kind.
- **Per-pack rules are unchanged.** The main pack is strict; a DLC may reference locally or in main;
  DLC→DLC is an error; duplicate canonical paths are allowed across packs and forbidden inside one
  pack (`BK3005`). `PackBuilder` still receives the external-main paths/kinds through its internal
  tooling seam.

### 11.4 Pack write and atomic replacement

One `PackBuilder` per pack collects that pack's entries. The compression mode follows the build
configuration (`Debug` → `PackCompressionMode.Debug`, `Release` → `.Release`) or `--mode`. Entries
are sorted by the builder; discovery sorts paths ordinal first. All packs validate and compose in
memory before the first write, and each pack is written to a temporary file in its destination
directory and atomically replaces the target only after a fully successful bake. A failed pack
leaves every previous pack and the manifest unchanged and fails the build; a failure while writing a
later pack cannot leave a half-validated pack set. An empty project produces a valid empty pack; a
texture-only or processor-only project is valid. The v1 header is always Zstd-compressed and
XXH3-64-checksummed; the format bytes are unchanged.

### 11.5 Structural determinism

For identical builder configuration, sources, registered type/shape assemblies, processor set/code,
tool version, and mode, a second bake produces the same canonical path/kind set per pack, the same sorted entry table
(path, `IsRaw`, `IsCompressed`, `Length`, `Size`, with `Offset` derived from the layout),
byte-identical MessagePack payloads, byte-identical processor payloads and descriptor values, and
identical diagnostics. Whole-pack byte equality is **not** guaranteed (D-M3-08/D-M4-07): Zstd output
may vary across runtime versions, so tests compare the entry table and decompressed payloads, never
a pack hash. Processor registration order is stable; a later override for one extension is
deterministic.

### 11.6 Incremental bake and MSBuild

- The internal, non-committed manifest is `<project root>/obj/bork2d/bake/manifest.json`: JSON
  schema 3, XXH3-128 uppercase hex, explicit field framing, deterministic field order. It records
  the builder configuration hash (packs/roots/output paths, texture defaults, registered type
  names, processor identities and assembly hash), the sorted tracked inputs across every pack root
  (every `.toml` plus every file whose suffix is in the registered extension set), the derived
  shape-assembly closure hash, tool version, mode, every pack output path/hash in builder
  order, and aggregate counts.
- `bake` skips only when every recorded field matches and every output pack exists with its recorded
  hash. The decision is all-or-nothing; there is no per-pack cache. A missing/corrupt manifest, any
  changed/deleted source or output, a changed common/tool/mode, a changed registration or processor,
  or an externally modified pack forces a full bake. A successful full bake writes the manifest
  atomically; a manifest write failure after valid packs is a warning. `--force` bypasses the skip;
  `--check` never skips and writes nothing.
- A full bake **re-collects the tracked inputs after processing**, so a `Required` sidecar created by
  `CreateDefaultSidecar` is hashed and recorded by the same bake. Without that, the next invocation
  would see a newly created `.toml` input and run one unnecessary extra full bake.
- `build/Bork2d.Assets.targets` is imported by `MyGame.Assets`. After `Build` it runs the built
  executable's `bake` command with the project root and configuration mode; the executable's own
  directory is the default output root. A second build with unchanged inputs/outputs/config is a
  no-op through the schema-3 manifest. `Clean` removes the incremental state and packs. There are no
  descriptor properties, no `Bork2dBakePackPaths`, no host build, and no assets-copy step: the
  assets executable owns its roots and outputs.
- `build/Bork2d.Assets.Copy.targets` is imported by a consuming desktop/AOT app. A desktop app may
  reference the assets project with `ReferenceOutputAssembly=false` for build order only; a
  self-contained AOT app cannot and sets `Bork2dAssetsProjectFile`, so the target builds the assets
  executable out of process. After `Build` it asks the executable to copy packs to `$(TargetDir)`;
  after `Publish` to `$(PublishDir)`. Builds with no assets executable are unaffected; CI stays
  restore/format/build/test, and bake tests run in the normal Debug pass. The local AOT gate adds the
  publish/run step.

### 11.7 CLI and build integration

`MyGame.Assets` is the game's CLI. The builder provides the command surface:

```text
MyGame.Assets bake          [--project-root <dir>] [--output-root <dir>]
                            [--mode debug|release] [--check] [--force]
                            [--verbose|--quiet]
MyGame.Assets validate      [--project-root <dir>]
MyGame.Assets clean         [--project-root <dir>]
MyGame.Assets copy-packs    --output-root <dir>
MyGame.Assets --help | --version
```

`bake` processes every builder pack and atomically writes each under the output root. `--check`
validates every pack and writes nothing. `validate` runs discovery, processor validation, registered
type binding, and reference validation without writing. `copy-packs` is the build-only command used
by `Project.Desktop`; it copies the
configured pack outputs without exposing a pack-path list to MSBuild. Summaries report the pack count
and paths. Exit codes: `0` success/up to date, `1` one or more source diagnostics, `2`
usage/configuration error, `3` unexpected internal failure. Diagnostics go to stderr in the
MSBuild form; the summary goes to stdout. The engine's `Bork2d.Bake.Cli` is retired as a game-facing
tool; the Pong example uses `Pong.Assets`.

### 11.8 Outputs

| Artifact | Location | Committed? |
| --- | --- | --- |
| Assets executable and builder config | `MyGame.Assets/Program.cs` and project file | yes (source) |
| Asset roots | `MyGame.Assets/Assets/`, optional `DLC1/` | yes (source) |
| Pong source assets | `examples/pong/Pong.Assets/Assets/art/paddle.png`, `Assets/textures/ball.circle`, and the data/sprite/settings TOMLs | yes (game **source assets**, not test/pack fixtures) |
| Incremental manifest | `<project root>/obj/bork2d/bake/manifest.json` | no |
| Runtime packs | one file per builder pack under the output root (build output/publish) | no |

The Pong assets executable bakes `Assets/` → `assets.pck` (7 entries: `art/paddle`, `art/paddle-raw`,
`game_settings`, `sprites/ball`, `sprites/paddle`, `textures/ball`, `textures/ball-raw`). A game may
also configure overlay/DLC packs; the app opens the main pack at `Run()` (or the `WithAssetsPack`
path) and mounts DLC packs with `IAssets.LoadPack`. Paths resolve against the
process current directory or are given as absolute paths (D-M3-03/Q-10). There is no "next to the
executable" fallback in the runtime; `Pong.Desktop` passes `AppContext.BaseDirectory/assets.pck`. The
M5 editor reuses the builder and shares the validator/baker; the local gates build debug and release
packs and run loader validation on both (CI builds Debug only).

## 12. Editor

`Bork2d.Editor` is an Avalonia library/app working on the source tree, not `assets.pck`; it is JIT and
not part of the game runtime. `Project.Editor` is a thin game-specific host referencing
`Bork2d.Editor` + `Project.Common`; it does not reference `Bork2d.Render` and does not call runtime
APIs, so the runtime stays `Bork2d.Engine`/`Bork2d.Runtime`-only. `Bork2d.Bake` may be referenced for
the shared builder configuration.

```csharp
using var editor = new Bork2dEditorBuilder()
    .WithProject("../../MyGame.Assets")        // builder configuration lives here
    .WithAssetType<EnemyDefinition>()
    .WithWindow<MyToolWindow>()
    .Build();
editor.Run();
```

- `WithProject` points at the game's `MyGame.Assets` project; D-037 removes the M4 descriptor, and
  the editor will consume the same builder configuration (M5 refinement).
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

For custom data the editor finds TOML by walking the builder's asset roots and reading each source's
`[bork2d].type` (there is no descriptor path mapping), shows a property grid over public
properties/fields plus a raw TOML view, validates required fields/references/ranges, and writes
canonical TOML. It edits processor sidecars through their `[GenerateShape]` schema and invokes the
shared validator, builder, and pack builder with file/line diagnostics; it never reimplements the
pack format.

Boundary: Avalonia UI, JIT (**not** NativeAOT), no Vulkan/swapchain/engine render thread, compile-time
type registrations with reflection only for JIT property metadata, TOML/source images from
`MyGame.Assets` (built packs never edited), work on the Avalonia UI thread with background bake/pack
only. The editor does **not** edit scenes, place entities, run the game, edit `.pck` files, implement
animation timelines, tilemaps, or materials, atlas multiple textures into one page, or support
multi-project workspaces in v1.

## 13. NativeAOT

- Runtime pack/descriptor/sprite/custom-data shapes are Nerdbank generated; `LoadAsset<T>` requires a
  shape, with no reflection resolver. No runtime reflection, `Type.GetType`, assembly scanning, runtime
  TOML, `Activator.CreateInstance`, or dynamic code in pack/asset loading.
- Zstd is the .NET 11 built-in implementation, so no extra native codec is needed; pack paths and
  asset keys are data, not types. `WithGame(instance)` avoids reflection-based game construction.
- The public tooling surface is `PackBuilder`/`PackCompressionMode` in `Bork2d.Engine.Assets.Pack`
  (D-034, M3) plus the `Bork2d.Bake` builder/processor API (D-036/D-037). The bake implementation
  (same assembly, Tomlyn, StbImageSharp) is JIT-only and internal; no runtime project references it.
  There is no generated host and no reflection over `Project.Common` metadata.
- The default `Pong.Desktop` AOT run loads the baked `AppContext.BaseDirectory/assets.pck`; it does
  not build a temporary pack. Its publish contains `assets.pck` and `SDL2.dll`, the game-side
  `Pong.Common` assembly (linked into the single-file executable; `Pong.Common.pdb` is present), and
  no `Bork2d.Bake`/`Pong.Assets`/Tomlyn/StbImageSharp assembly. The default headless run asserts the
  7 entries: `art/paddle`, `art/paddle-raw`, `game_settings`, `sprites/ball`, `sprites/paddle`,
  `textures/ball`, and `textures/ball-raw`.
- `MyGame.Assets`, `Bork2d.Bake`, and processors are JIT build-time tooling. They are never
  referenced by the runtime app's compile or publish graph; the assets executable is a build-only
  dependency (a project reference is fine for a desktop app; the Pong AOT app builds it out of
  process through `Bork2dAssetsProjectFile`). Runtime data types stay in `Project.Common`.
- Runtime projects set `IsAotCompatible` with trim/AOT analyzers; only the AOT desktop app
  (`Pong.Desktop`) sets `PublishAot`. AOT publish/run is a required local gate, not a CI job.

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
  The bake pipeline also validates binding, processor outputs, and references before packing (with
  source spans where available), so a correct bake never reaches `PackBuilder`'s backstop. Sprite
  field validation remains M5 (D-M4-06). Processors run sequentially on the bake thread and are not
  part of the runtime.
- The runtime has no GPU work yet. M8 creates GPU texture pages/uploads on the render thread and
  resolves `SpriteAsset.Texture`/`TextureDescriptor.Payload` at draw time; load-and-keep applies to
  packs in v1.

`WithHeadless` supports packs, `LoadData`, `LoadAsset<T>`, `Browse`, and `Exists`; sprites load as
plain assets but no GPU page exists and nothing renders. `WithOffscreen` runs the M6 Vulkan renderer
against a fixed target (`Setup` → `Tick`… → `Teardown` until `Exit`, `ITime` advances, frames are
discarded); sprites and readback are M8/M10. Neither mode has `IWindow`/`IInput`. See
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
| `Bork2d.Engine` | `Current` (M3/D-035): v1 pack reader/writer, structural validation, canonical paths, public `Bork2d.Engine.Assets.Pack.PackBuilder`/`PackCompressionMode`, the internal base/overlay VFS with `LoadPack`/`Browse`/`Exists`, the app-scoped service (one thread, FIFO queue, `LoadData`/`LoadAsset<T>`, cancellation/join, `AssetException`), and generic pack-time reference validation |
| `assets.pck` reader/writer | `Current` (M3): v1 format reader and writer; the VFS, overlays, `Browse`/`Exists`, and the loader are wired through `IEngine.Assets` |
| Texture descriptors and raw RGBA8 payloads | `Current` (M3/D-035; texture import D-036): public `TextureDescriptor` model with a `DataRef` payload; the built-in texture processor (`Required` sidecar, `png`/`jpg`/`jpeg`/`bmp`/`tga`) decodes from anywhere under the asset root to straight-alpha RGBA8, emits the descriptor + `<descriptor>-raw` payload, and validates dimensions, format/filter/wrap, and payload size. Payload read/upload and KTX2 are M8/M6 |
| Sprite assets (UVs/scale/pivot/topology/indices) | `Current` (M3/D-035; data bake D-036): public `SpriteAsset` model with an `AssetRef<TextureDescriptor>` link, loaded via `LoadAsset<SpriteAsset>` and baked as ordinary custom data when a TOML declares its type in `[bork2d]`. No runtime or bake-time field validation; UV/pivot/scale/topology/indices validation and slicing are M5 (D-M4-06). Drawing is M8 |
| CPU resource table (descriptors/sprites) | Removed by D-035; the loader caches no resolved sprite/texture resources |
| Runtime asset lifecycle | `Current` (M3): `WithoutAssets()`, base pack at `Run()` before the window/`Setup`, start/stop/error order, post-shutdown `ObjectDisposedException`, app disposal |
| TOML model, baker, deterministic bake | `Current` (D-036/D-037/D-038): builder-configured ordered packs, per-source `[bork2d].type`, explicit `RegisterAsset<T>()` registrations, processor discovery/sidecars, shape-driven Tomlyn DOM binder, built-in texture processor, local + main cross-pack reference validation, structural determinism, schema-3 incremental manifest, atomic per-pack writes, CLI, and MSBuild targets. No project descriptor or generated host. Sprite field validation/slicing are M5 |
| `Bork2d.Bake` | `Current` (D-036/D-037/D-038): internal JIT implementation plus the **public** `Bork2d.Bake` builder/processor surface; `bake` is incremental (schema 3), `validate` is check-mode, `clean` removes packs + manifest state, and `copy-packs` serves consuming apps; exit codes `0`–`3`. `Bork2d.Bake.Cli` and `generate-host` are removed |
| `build/Bork2d.Assets.targets` / `Bork2d.Assets.Copy.targets` | `Current`: the assets executable bakes after `Build` and cleans before `CoreClean`; the copy target builds the assets executable out of process when `Bork2dAssetsProjectFile` is set and copies packs to `$(TargetDir)`/`$(PublishDir)`. No descriptor properties, no pack-path list, no host build |
| `Pong.Common` / `Pong.Assets` | `Current` (D-043/D-044): `Pong.Common` is the AOT-safe game assembly (`GameSettings` struct root, simulation, AI, title); `Pong.Assets` is the build-only executable owning `Assets/` and `CircleProcessor`, and bakes the 7-entry `assets.pck`. Neither the assets executable nor the builder enters the runtime/AOT publish |
| Texture sidecars | `Current` (D-036): `<source>.toml` is a `[GenerateShape]` settings type bound by the shared TOML binder; policies are `None`/`Required`, and a missing `Required` sidecar is created once by `CreateDefaultSidecar`. No hand-written parser and no `Optional` policy |
| Asset processors and `MyGame.Assets` | `Current` (D-036/D-037/D-038; Pong example D-043/D-044): `Bork2d.Bake`, explicit `RegisterAsset<T>()`/`UseDefaultAssets()`/`UseDefaultProcessors()`, no generated host, `IAssetProcessor`/`AssetProcessor<TSidecar>`, sidecar policies, base-path outputs, extension override, engine-side MessagePack serialization from `AddAsset<T>`, built-in texture processor, `Pong.Assets` + `CircleProcessor` as the worked example, and auto-bake. Design: [asset processors](asset-processors.md) |
| Serialization policy | `Current` (M3/D-035): Nerdbank.MessagePack `1.4.95` pinned with the generated-shape proof in the Pong AOT app and tests; pack header/entry and public `SpriteAsset`/`TextureDescriptor` shapes are generated; `AssetRef<T>`/`DataRef` use public PolyType path marshalers; `Bork2d.Engine` references PolyType `1.4.1`, Nerdbank.MessagePack, and `System.IO.Hashing`; `LoadAsset<T>` passes the shape from `T.GetTypeShape()` |
| `IAssets` load members | `Current` (M3/D-035): declarations, XML docs, the `IShapeable<T>` constraint, deserialize-only loader bodies, and `AssetRef<T>`/`DataRef` overloads; `IEngine.Assets` returns the real service in every mode |
| `IAssets.LoadPack`/`Browse`/`Exists`, `AssetInfo` | `Current` (M3): `AssetInfo`, the internal VFS/lookup, and public delegation |
| `AssetException`/`AssetErrorKind` | `Current` (M3): public error contract; pack open/parse/mount and reference-validation failures remain `Bork2dException` |
| `Bork2d.Editor` / `Project.Editor` | `Planned` M5; editor and host are absent |

## 17. Related documents

- [NativeAOT](nativeaot.md) and [Threading](threading.md#6-asset-and-pack-coordination) — runtime
  constraints and loader/upload ownership.
- [Rendering](rendering.md) — texture pages, bindless textures, sprite pipeline.
- [Runtime lifecycle](runtime-lifecycle.md#2-modes) — headless/offscreen.
- [Asset processors and `MyGame.Assets`](asset-processors.md) — processor contract, sidecars,
  builder configuration, and Pong's `CircleProcessor` example.
- [Projects](projects.md) — `Bork2d.Engine`/`Bork2d.Bake`/`Pong.Common` rows and
  `Bork2d.Editor` references.
- [API reference](../api/reference.md#7-iassets) — `IAssets` contract.
- [M4 spec](../specs/m4-serialization-and-bake.md) and [WP0 report](../specs/m4-wp0-spike.md) —
  historical bake scope, feasibility findings, and as-built evidence.
- [Roadmap](../roadmap.md) and [decision log](../decisions.md) — M3/M4/M5/M8 and D-010–D-012,
  D-M4-01…D-M4-15, D-036/D-037/D-038.
