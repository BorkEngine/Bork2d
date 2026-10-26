# Assets, Bake, and Editor Tooling

> **Status:** M3 is implemented and merged into `develop` (2026-10-26, PR #4); the post-M3
> asset-reference refactor (D-035) is implemented in this tree. `Bork2d.Assets` provides the v1 pack
> reader/writer and the public `Bork2d.Assets.Pack.PackBuilder`/`PackCompressionMode` tooling
> surface, canonical paths, the internal base/overlay VFS with `LoadPack`/`Browse`/`Exists`, one
> loader thread with `LoadData`/`LoadAsset<T>`, generic `AssetRef<T>`/`DataRef` reference validation
> at pack build, and the app-scoped service wired into `Run()`/`Dispose()`. `SpriteAsset`,
> `TextureDescriptor`, and `SpriteTopology` are public `Bork2d.Api` assets; there is no
> `LoadSprite` handle path. The [M3 spec](../specs/m3-assets.md) is the historical record; the
> [refactor spec](../specs/refactor-asset-references.md) owns the public asset-model change. TOML
> bake and the production bake integration are **M4**, and the editor is **M5**. `IEngine.Assets`
> returns the real service in every mode, including the `WithoutAssets()` empty filesystem.

## 1. End-to-end model

```
Project.Common/Assets/  data/**.toml  sprites/**.toml  textures/*
   │ baker (M4, JIT): TOML → MessagePack, textures → raw RGBA8;
   │ field/reference checks happen here, before packing
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

[GenerateShape]
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

```csharp
public interface IAssets
{
    Task<T> LoadAsset<T>(string assetPath) where T : class, IShapeable<T>;
    Task<T> LoadAsset<T>(AssetRef<T> reference) where T : class, IShapeable<T>;
    Task<byte[]> LoadData(string assetPath);
    Task<byte[]> LoadData(DataRef reference);
    void LoadPack(string packPath);
    IReadOnlyList<AssetInfo> Browse(string? prefix = null);
    bool Exists(string assetPath);
}

public readonly record struct AssetRef<TAsset>(string? Path)
    where TAsset : class, IShapeable<TAsset>;

public readonly record struct DataRef(string? Path);
```

| Member | Status | Semantics |
| --- | --- | --- |
| `LoadAsset<T>` | `Current` (M3/D-035) | Deserializes a MessagePack asset with its compiled generated shape; no reflection fallback. Raw assets and missing paths fault. The value is returned as stored: no field or link validation. |
| `LoadAsset<T>(AssetRef<T>)` | `Current` (D-035) | Loads the reference path. A null reference is not a load request; empty/invalid paths throw `ArgumentException`. |
| `LoadData` / `LoadData(DataRef)` | `Current` (M3/D-035) | Decompressed bytes for any asset kind, as a fresh `byte[]` per call. |
| `LoadPack` | `Current` (M3) | Mounts above the stack; later wins; no unload; bad pack throws `Bork2dException`; thread-safe and atomic. |
| `Browse` | `Current` (M3) | Merged snapshot with an optional canonical prefix on a `/` segment boundary; sorted by path. |
| `Exists` | `Current` (M3) | Exact canonical lookup. |
| `AssetInfo` | `Current` (M3) | Value type returned by `Browse`; header fields plus the supplied pack identifier. |
| `AssetException`/`AssetErrorKind` | `Current` (M3/D-035) | Public error contract for lookup/decode/read failures; pack failures stay `Bork2dException`; field/link validation is a pack-build concern. |
| `WithoutAssets()` (builder) | `Current` (M3) | Selects an empty VFS with no base pack; `LoadPack` overlays are still allowed; mutually exclusive with `WithAssetsPack`. |

`IShapeable<T>` is `PolyType.IShapeable<T>`; `Bork2d.Api` references PolyType `1.4.1` for the
shape contract and the `AssetRef`/`DataRef` path marshalers (D-M3-22, D-035), and stays free of
Nerdbank.MessagePack, Zstd, `System.IO.Hashing`, and file I/O. All members are thread-safe.
`LoadPack` is synchronous; loads complete asynchronously. Do not call `Browse` in `Tick`; v1 has no
unload (load-and-keep). `IEngine.Assets` returns the real app-scoped service in every mode and never
throws from the getter; after `Run()`/`Dispose()` its members throw `ObjectDisposedException`
(D-M3-14).

Public `SpriteAsset`, `TextureDescriptor`, and `SpriteTopology` live in `Bork2d.Api.Assets`:

- `SpriteAsset.Texture` is an `AssetRef<TextureDescriptor>`.
- `TextureDescriptor.Payload` is a `DataRef`.
- A `null` reference is absent and valid; empty/non-canonical paths are rejected by `PackBuilder`.
- `ISpriteRender<TLayer>.Sprite` is a `SpriteAsset?`; `null` draws nothing.

## 7. Loading, pack validation, and failure

`Status: loader and pack-build reference validation Current (M3/D-035); GPU readiness/upload
Planned (M8).`

- **One loader thread.** The internal asset service owns one dedicated background thread per app
  (started even for an empty VFS) and an **unbounded FIFO** request queue. There is no priority,
  no parallelism, and no public cancellation token. The loader only reads, decompresses, and
  deserializes; it never touches SDL, Vulkan, game entities, or asset fields.
- **Enqueue-time resolution.** `LoadData`/`LoadAsset<T>` validate the path and the disposed state
  synchronously, capture the current VFS snapshot, and resolve the winning entry at enqueue. A
  later overlay mount does not change an in-flight request; new lookups see the new stack.
- **Exact-size reads.** Packs are validated structurally at open; payload decompression is lazy
  per read. `LoadData` decompresses to a declared-size buffer and returns a **fresh** `byte[]` per
  call. `LoadAsset<T>` streams the decompressed payload through the compiled generated shape; the
  stream is drained so a payload that decompresses to fewer or more bytes than its declared `Size`
  is a decode failure. Neither path caches. There is no post-deserialize validation.
- **Sprite asset loading.** `LoadAsset<SpriteAsset>` is a normal MessagePack read. It returns the
  packed `SpriteAsset` as stored; it does not resolve `Texture`, decode the descriptor, read the
  raw payload, or run UV/scale/pivot/topology checks. The renderer later resolves the asset's
  references and skips a sprite whose texture is absent or missing. There is no handle, readiness
  state, placeholder, or sprite-specific retry.
- **References.** `AssetRef<T>` and `DataRef` are canonical path strings in the pack. A null path
  is absent. `LoadAsset(AssetRef<T>)`/`LoadData(DataRef)` throw `ArgumentException` for an absent
  reference; a present but missing link faults `NotFound` when loaded.
- **Failures.** A missing path, a raw entry requested through `LoadAsset<T>`, a decode/size
  failure, and an IO read failure fault the returned task with `AssetException`
  (`NotFound`/`RawAsset`/`Decode`/`Io`/`InvalidData`). Pack open/parse/mount failures stay
  `Bork2dException`. There is **no automatic retry**, no loose-file fallback, and no
  placeholder/blocking wait (D-M3-18).
- **Pack-time reference validation.** `PackBuilder.AddMessagePack<T>` walks the value graph with
  the generated PolyType shape and collects every `AssetRef<T>`/`DataRef`. `Build`/`Write`
  validate each non-null reference against the final entry table: canonical path, entry exists,
  and the reference kind matches the target kind (`AssetRef` → MessagePack, `DataRef` → raw).
  A violation throws `Bork2dException` naming the source asset and property path. A failed
  `Write` does not create/truncate the output.
- **Field validation.** UV extents, scale, pivot, topology, descriptor dimensions, format,
  filter/wrap, and payload-size consistency are not runtime checks. They belong to the M4
  baker/editor, which validates source before packing (D-035).
- **Shutdown.** Stopping the service rejects new requests, cancels queued and in-flight load tasks
  (they complete as canceled), joins the loader, closes the mounted packs, and marks the service
  disposed. Shutdown is idempotent; secondary failures are logged, not thrown.
- **Logging** uses the `Bork2d.Assets` category: start (`Pack`/`Empty` with the configured and
  resolved path, version, entries, bytes), overlay mounted, load failure (path, kind, reason),
  load success at Debug (path, kind), and stop (pending/canceled counts, mounted packs). Nothing is
  logged per frame.
- **No renderer in M3.** The loader never creates GPU resources. M8 creates GPU pages/uploads on
  the render thread and resolves sprite references at draw time.

A sprite is drawable only when its asset, referenced texture descriptor, and raw payload are
available and the renderer has created the GPU page and bindless descriptor. Any missing stage
emits no instance: no placeholder, checkerboard, or blocking wait.

## 8. Texture descriptors

`Status: public model Current (D-035); field validation M4; GPU upload and KTX2 Planned (M6/M8).`

A texture is a MessagePack **descriptor** plus a **raw payload**: `textures/player` and
`textures/raw/player`.

| Field | Type | Meaning |
| --- | --- | --- |
| `payload` | `DataRef` | Raw asset reference containing RGBA8 pixels (`null` = absent). |
| `width` | `uint32` | Pixel width. |
| `height` | `uint32` | Pixel height. |
| `format` | `string` | `rgba8` in v1; KTX2 is later. |
| `filter` | `string` | `nearest` or `linear`. |
| `wrap` | `string` | `clamp` or `repeat`. |

`rgba8` payload: width × height × 4 bytes, row-major, top-left origin, no padding, straight alpha,
8 bits per channel, treated as sRGB. The source is a sidecar carrying **import settings only**
(`filter`, `wrap`); the baker writes `textures/player` with payload `textures/raw/player`, and
sprite definitions in `sprites/**.toml` reference the descriptor.

- **Public shape.** `TextureDescriptor` is a public `[GenerateShape]` class in
  `Bork2d.Api.Assets` with property-name keys (no `[Key]`) and no per-asset version.
  `System.Numerics.Vector2` uses Nerdbank's generated shape; there is no reflection resolver.
- **No runtime validation.** `LoadAsset<TextureDescriptor>` returns the packed descriptor as
  stored. `PackBuilder` validates that a non-null `Payload` exists and targets a raw entry. The M4
  baker/editor owns canonical-path, dimension, format, filter/wrap, and payload-size checks.
- **No payload read.** The loader never decompresses or uploads the payload; M8 reads it on first
  texture use. There is no CPU texture resource or descriptor cache.

## 9. Sprite assets

`Status: public model Current (D-035); field validation M4; GPU/draw Planned (M8).`

A sprite is a MessagePack asset (for example `sprites/player/idle`) describing a textured quad:

| Field | Type | Meaning |
| --- | --- | --- |
| `texture` | `AssetRef<TextureDescriptor>` | Texture descriptor reference; null = absent. |
| `uvs` | `Vector2[]?` | Normalized UVs, top-left origin; four when present. |
| `scale` | `float` | Pixels per world unit. |
| `pivot` | `Vector2` | Normalized origin inside the quad; default `(0.5, 0.5)`. |
| `topology` | enum | `Quad` (0), `TriangleStrip` (1), or `Triangles` (2); an integer in the pack. |
| `indices` | `ushort[]?` | Optional index buffer; null for `Quad`. |

There is no `vertices` array; the vertex shader generates positions from UVs, texture size, scale,
and pivot.

- Size: `pixelWidth = (maxU - minU) * textureWidth`, `pixelHeight = (maxV - minV) * textureHeight`,
  then `worldWidth = pixelWidth / scale`, likewise for height; `Transform.LocalScale` multiplies
  after. `scale` is pixels per unit, so a 64 px sprite at `scale = 16` is 4 units high.
- `pivot` uses a top-left origin: `(0,0)` is top-left, `(1,1)` bottom-right, `(0.5,0.5)` center. The
  quad is positioned around the pivot and converted to world Y-up; UVs stay top-left origin,
  ordered top-left, top-right, bottom-right, bottom-left.
- `Quad` uses the four generated corners; indexed topologies are preserved for the future indexed
  path. v1 draws `Quad` only; non-Quad assets are never silently treated as quads.
- **Public shape.** `SpriteAsset` is a public `[GenerateShape]` class in `Bork2d.Api.Assets`.
  `PackBuilder` validates a non-null `Texture` reference exists and targets a MessagePack entry.
  The loader does not validate UVs, scale, pivot, topology, or indices; the M4 baker/editor does.
- The editor writes TOML; M4 bakes the MessagePack form. The renderer resolves sprite asset →
  descriptor → payload → GPU page; tint, layer, z-index, and transform stay on the renderable.
  `ISpriteRender.Sprite` holds a loaded `SpriteAsset?` directly.

## 10. TOML source model

Data is authored as TOML for readable, reviewable, diffable git history, then baked to MessagePack.
One model describes both formats: the same C# type is the TOML authoring model and the MessagePack
runtime model; divergent DTOs are avoided unless generated. A typical type is a
`[GenerateShape]`-annotated class with public settable properties such as `Id`, `Health`, an
`AssetRef<SpriteAsset>` field, and a list of referenced types; `[Bork2dSerializable]` is optional
editor/baker metadata.

- `[GenerateShape]` is Nerdbank's source-generation anchor; only the serialized top-level type needs
  it. The JIT editor/baker may reflect over the same model.
- Serializable types are classes/structs with a public parameterless constructor and public settable
  properties/fields, or a documented constructor-binding scheme. Supported leaves: primitives,
  `string`, enums, `Guid`, UTC `DateTime`, `Vector2/3/4`, `Color`, `AssetRef<T>`, `DataRef`,
  nullable variants, and arrays/lists/dictionaries of them. Missing values use the C# default;
  required values are validated by the editor/baker, and comment round-trip preservation is
  deferred (see roadmap §9).
- No cycles, delegates, tasks, threads, engine entities, or loaded `SpriteAsset` instances.
  Polymorphism is explicit or absent in v1; there is no assembly-scanning serializer.
- References are typed canonical paths (`AssetRef<TAsset>`/`DataRef`) serialized as strings,
  validated at bake/pack time, never handles or pack offsets; the editor checks the source tree and
  the baker checks baked assets. `PackBuilder` validates pack-time reference integrity. A missing
  reference is a diagnostic plus a non-drawable renderable, not a crash; save data uses paths.
- There is **no per-asset schema version** (D-M3-05): the pack prefix `version` is the only format
  version. A shape change is a pack-version change/migration; pre-1.0 breaking changes are allowed by
  D-014. The editor preserves unknown fields where practical and runtime errors identify
  path/location. Pack output has no byte-level determinism guarantee (D-M3-08); the runtime never
  parses TOML.

## 11. Build pipeline

Source lives under `Project.Common/Assets`: `data/**.toml`, `sprites/**.toml`, source textures, and
`<file>.toml` import sidecars. TOML is the source of truth; source textures are regular files
(`player.png` or raw RGBA) with a sidecar carrying import settings only, and slices live in
`sprites/**.toml`. The baker normalizes textures to the runtime payload format (RGBA8 now, KTX2 later).

Bake (M4, JIT):

1. parse and validate TOML data with custom classes;
2. parse sprite TOML and validate UVs/scale/pivot/topology/indices/texture references;
3. read texture sidecar and source image/raw RGBA, emit the runtime payload and descriptor;
4. emit MessagePack assets for data, sprites, and descriptors;
5. report errors with file/line context and emit an intermediate tree for the pack builder.

Pack builder: collect baked assets and payloads; establish canonical paths, reject duplicates, sort
entries; serialize `PackHeader`/`PackEntry`, compress the header with Zstd max, compute XXH3-64 over
the uncompressed bytes; compress payloads in release only if smaller; write the 28-byte prefix,
compressed header, and data with absolute offsets; write the pack (no byte-determinism guarantee).

| Mode | Header | Asset payloads |
| --- | --- | --- |
| Debug | Always Zstd-compressed, XXH3-64 checksum | Uncompressed, `IsCompressed=false`, `Length==Size` |
| Release | Always Zstd-compressed, XXH3-64 checksum | Zstd max, compressed only when smaller |

The app opens `assets.pck` at `Run()` (or the `WithAssetsPack` path), resolved against the process
current directory or given as an absolute path (D-M3-03). There is no "next to the executable"
fallback; scripts set the working directory or pass an absolute path. MSBuild vs CLI is undecided
(M4); incremental bake uses timestamps/hashes. The editor shares the validator and baker; CI builds
a debug and a release pack and runs loader validation on both.

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

- `WithProject` points at `Project.Common` or a descriptor (`bork2d.project.toml`, schema M4).
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

For custom data the editor finds TOML mapped to a registered type (project-descriptor mapping is
M4), shows a property grid over public properties/fields plus a raw TOML view, validates required
fields/references/ranges, and writes canonical TOML. It edits texture sidecars and invokes the shared
validator, baker, and pack builder with file/line diagnostics; it never reimplements the pack format.

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
  (D-034); `Bork2d.AotSmoke` references `Bork2d.Assets` to build and load a temporary pack in the
  default AOT run (D-M3-04, D-M3-19).
- Runtime projects set `IsAotCompatible` with trim/AOT analyzers; only the AOT smoke app sets
  `PublishAot`. AOT publish/run is a required local gate, not a CI job.

## 14. Threading

`Status: loader and pack-build reference validation Current (M3/D-035); renderer/GPU integration
Planned (M8).`

- Packs are opened and headers parsed by the asset service (`Start`, on the `Run` caller thread,
  before the platform and `Setup`); path tables are immutable unless a new pack is mounted, and
  `LoadPack` swaps the overlay stack atomically.
- The service owns exactly **one** dedicated loader thread with an unbounded FIFO queue. Requests
  capture the VFS snapshot at enqueue time; the loader thread deserializes and completes
  `TaskCompletionSource`-backed tasks with `RunContinuationsAsynchronously`, and never touches SDL,
  Vulkan, or game entities. Task continuations therefore never run inline on the loader. The
  runtime starts the service before the window/`Setup` and stops it after `Teardown` on every path.
- **No runtime resource table.** The loader is deserialize-only: it does not publish CPU sprite or
  texture resources, caches no resolved assets, and has no handle table. Each `LoadAsset<T>` call
  returns a fresh instance. Shared/cached resolution is M8 renderer work.
- **Pack-time ownership.** `PackBuilder` is the only component that validates asset references
  (`Build`/`Write`); it runs in tooling, never in the runtime. The M4 baker/editor validates asset
  fields before packing.
- At shutdown the service stops accepting requests, cancels queued and in-flight load tasks, joins
  the loader at a safe point, and only then closes the mounted archives.
- M3 has no GPU work: the loader never creates texture pages, and there is no public readiness or
  resource-table API. The future renderer creates GPU texture pages and uploads on the render
  thread, resolving `SpriteAsset.Texture`/`TextureDescriptor.Payload` references at draw time.
  Load-and-keep applies to packs in v1. See [Threading](threading.md#6-asset-and-pack-coordination).

`WithHeadless` supports packs, `LoadData`, `LoadAsset<T>`, `Browse`, and `Exists`; sprites load as
plain assets but no GPU page exists and nothing renders. `WithOffscreen` runs the normal upload/draw
path without a window or swapchain and discards frames in v1. Neither mode has `IWindow`/`IInput`.
See [Runtime lifecycle](runtime-lifecycle.md#2-modes).

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
| Texture descriptors and raw RGBA8 payloads | `Current` (M3/D-035): public `TextureDescriptor` model with a `DataRef` payload; pack-time reference validation. Field validation is M4; payload read/upload and KTX2 are M8/M6 |
| Sprite assets (UVs/scale/pivot/topology/indices) | `Current` (M3/D-035): public `SpriteAsset` model with an `AssetRef<TextureDescriptor>` link; loaded via `LoadAsset<SpriteAsset>`; no runtime field validation. Drawing is M8 |
| CPU resource table (descriptors/sprites) | Removed by D-035; the loader caches no resolved sprite/texture resources |
| Runtime asset lifecycle | `Current` (M3): `WithoutAssets()`, base pack at `Run()` before the window/`Setup`, start/stop/error order, post-shutdown `ObjectDisposedException`, app disposal |
| TOML model, baker, pack builder, deterministic bake | `Planned` M4 (field/reference validation in the baker/editor) |
| Serialization policy | `Current` (M3/D-035): Nerdbank.MessagePack `1.4.95` pinned with generated-shape smoke in Common, tests, and AOT smoke; pack header/entry and public `SpriteAsset`/`TextureDescriptor` shapes are generated; `AssetRef<T>`/`DataRef` use public PolyType path marshalers; `Bork2d.Api` references PolyType `1.4.1`; `LoadAsset<T>` passes the shape from `T.GetTypeShape()` |
| `IAssets` load members | `Current` (M3/D-035): declarations, XML docs, the `IShapeable<T>` constraint, deserialize-only loader bodies, and `AssetRef<T>`/`DataRef` overloads; `IEngine.Assets` returns the real service in every mode |
| `IAssets.LoadPack`/`Browse`/`Exists`, `AssetInfo` | `Current` (M3): `AssetInfo`, the internal VFS/lookup, and public delegation |
| `AssetException`/`AssetErrorKind` | `Current` (M3): public error contract; pack open/parse/mount and reference-validation failures remain `Bork2dException` |
| `Bork2d.Editor` / `Project.Editor` | `Planned` M5; editor absent, host is a template folder |

## 17. Related documents

- [NativeAOT](nativeaot.md) and [Threading](threading.md#6-asset-and-pack-coordination) — runtime
  constraints and loader/upload ownership.
- [Rendering](rendering.md) — texture pages, bindless textures, sprite pipeline.
- [Runtime lifecycle](runtime-lifecycle.md#2-modes) — headless/offscreen.
- [Projects](projects.md) — `Bork2d.Editor`/`Project.Editor` references.
- [API reference](../api/reference.md#7-iassets) — `IAssets` contract.
- [Roadmap](../roadmap.md) and [decision log](../decisions.md) — M3/M4/M5/M8 and D-010–D-012.
