# Assets, Bake, and Editor Tooling

> **Status:** target contract. The **M3 asset spec is approved (2026-10-26)**
> ([M3 spec](../specs/m3-assets.md)); it supersedes this document's M3-specific statements (base-pack
> timing, per-asset versioning, size caps, byte determinism, the public pack-tooling surface, and
> the M3 `LoadSprite` scope) until WP5 reconciles this document. In the current tree
> `Bork2d.Assets` implements the v1 pack reader/writer and the public
> `PackBuilder`/`PackCompressionMode` tooling surface (M3 WP1), plus canonical paths, the internal
> base/overlay VFS, `LoadPack` mounting, and `Browse`/`Exists` lookup (M3 WP2), the internal loader
> (M3 WP3), and the internal texture-descriptor/sprite models, validation, `LoadSprite` CPU pipeline,
> handle table, and resource cache (M3 WP4). Runtime wiring remains M3 WP5, TOML bake and the
> production bake integration **M4**, and the editor **M5**. `IEngine.Assets` still returns a stub
> whose members throw `NotSupportedException`.

## 1. End-to-end model

```
Project.Common/Assets/  data/**.toml  sprites/**.toml  textures/*
   │ baker (M4, JIT): TOML → MessagePack, textures → raw RGBA8
   ▼
MessagePack assets + raw payloads
   │ pack builder (M3/M4): header + XXH3-64; release Zstd max if smaller
   ▼
assets.pck (+ overlays) → VFS (NativeAOT) → loader threads → resource table → renderer
```

- Runtime reads packs only: no runtime TOML, reflection serializer, or source tree.
- Pack parsing, descriptors, sprites, and custom data use
  [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) generated shapes.
- Game code holds `Sprite` handles; the renderer resolves them and skips anything unready.
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
- The header is always maximum-compression Zstd, in debug and release, for determinism. `dataStart =
  28 + headerCompressedLength`; entry offsets are absolute, at least `dataStart`, and `offset +
  length` must not exceed the file size.
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
    public PackEntry[] Entries { get; init; } = [];
}

[GenerateShape]
internal sealed partial class PackEntry
{
    public string Path { get; init; } = "";
    public bool IsRaw { get; init; }
    public bool IsCompressed { get; init; }
    public ulong Offset { get; init; }
    public ulong Length { get; init; }
    public ulong Size { get; init; }
}
```

| Field | Type | Meaning |
| --- | --- | --- |
| `PackHeader.Entries` | `PackEntry[]` | Asset entries; must not be null. |
| `PackEntry.Path` | `string` | Canonical asset path. |
| `PackEntry.IsRaw` | `bool` | `true`: raw bytes; `false`: MessagePack. |
| `PackEntry.IsCompressed` | `bool` | Whether stored bytes are Zstd-compressed. |
| `PackEntry.Offset` | `uint64` | Absolute byte offset from the pack start. |
| `PackEntry.Length` | `uint64` | Stored byte count (compressed length when compressed). |
| `PackEntry.Size` | `uint64` | Byte count after decompression; equals `Length` when uncompressed. |

Entry order is insignificant, but the builder sorts by `Path` for deterministic output and the loader
builds a dictionary keyed by canonical path. No entry is parsed until the header checks pass.

### 4.3 Validation and compression

The loader rejects a pack when: magic is not `BORK2DPK`; the version is unknown (checked before
decompression); header lengths exceed limits or the file size; decompression does not produce exactly
`headerUncompressedLength` bytes; the checksum does not match XXH3-64 of the uncompressed header; the
header is not a v1 `PackHeader` or `Entries` is null; a path is empty, non-canonical, duplicated, or
escapes the root; `Offset < dataStart`, `Offset + Length > fileSize`, or ranges overlap; uncompressed
`Length != Size`; compressed data does not produce exactly `Size` bytes; or a decompressed size
exceeds the safety limit. A corrupt pack fails with a diagnostic, never an out-of-bounds read.

**Compression.** Zstd (.NET 11 built-in; no third-party dependency). The header is always maximum
Zstd; payloads are uncompressed in debug and maximum Zstd in release **only when smaller**. `Size` is
decompressed length and `Length` stored length; decompression enforces `Size` plus a global cap. The
builder records its settings for deterministic rebuilds; keep the format behind the reader/writer.

## 5. Virtual filesystem, overlays, and browsing

1. `assets.pck` is the base pack, mounted at startup.
2. `IAssets.LoadPack(path)` mounts a pack **above** the current stack.
3. Lookup walks top-down; later packs shadow earlier ones by canonical path.
4. There is **no unload** in v1: a mounted pack stays for the process lifetime.

- The default pack is `assets.pck`; `.WithAssetsPack(path)` overrides it. A missing or malformed base
  pack throws `Bork2dException` from `Run()` before the window/`Setup` (D-M3-01); `Build()` stays
  filesystem-free. `LoadPack` throws for a bad overlay; tests and tools use a valid empty pack.
- **M3 WP2 status:** the internal `AssetFileSystem` implements the immutable base/overlay stack,
  canonical path normalization, filesystem path resolution, `LoadPack` mounting, `Browse`/`Exists`
  lookup, and `AssetInfo` projection. Mounting parses outside the swap lock and publishes a new
  snapshot atomically; a failed mount leaves the stack unchanged; there is no unload.
- **M3 WP3 status:** the internal `AssetService` implements `LoadPack`/`Browse`/`Exists`
  delegation, `LoadData`, and `LoadAsset<T>`, with one loader thread, an unbounded FIFO queue,
  enqueue-time entry resolution, cancellation/join at shutdown, and the public
  `AssetException`/`AssetErrorKind` contract. Public runtime wiring is WP5.
- **M3 WP4 status:** the internal `AssetService` adds the `LoadSprite` handle table and CPU
  pipeline, `TextureDescriptor`/`SpriteAsset` generated shapes and validation, the per-texture
  descriptor cache, the per-sprite resource cache, and enqueue-snapshot capture for sprite,
  descriptor, and payload entries. Validation is also applied to `LoadAsset<T>` for the internal
  types. No public type, readiness API, payload read, or GPU work is added; public runtime wiring is
  WP5.
- `LoadPack` is thread-safe; mount during `Setup`/before the first tick. Mounting after a handle
  resolved does not change that handle; new lookups see the new pack.
- `Browse(prefix)` returns a merged snapshot with shadowed entries hidden (`prefix` is canonical or
  `null` for the whole tree); `AssetInfo` exposes `Path`, `IsRaw`, `IsCompressed`, `Length`, `Size`,
  and `PackPath`. `Exists` applies the same rules. Runtime mounts packs only; loose files, source TOML,
  and source images belong to `Project.Common` and the editor.

## 6. Public asset API

```csharp
public interface IAssets
{
    Task<T> LoadAsset<T>(string assetPath) where T : class, IShapeable<T>;
    Sprite LoadSprite(string assetPath);
    Task<byte[]> LoadData(string assetPath);
    void LoadPack(string packPath);
    IReadOnlyList<AssetInfo> Browse(string? prefix = null);
    bool Exists(string assetPath);
}

public readonly record struct AssetInfo(
    string Path, bool IsRaw, bool IsCompressed, long Length, long Size, string PackPath);
```

| Member | Status | Semantics |
| --- | --- | --- |
| `LoadAsset<T>` | `Partial` | Declaration and `IShapeable<T>` constraint are `Current` (WP2); the loader body is `Current` (WP3) in the internal service, and public runtime wiring is WP5. Deserializes a MessagePack asset with its compiled generated shape; no reflection fallback. Raw assets and missing paths fault. A deserialized internal `TextureDescriptor`/`SpriteAsset` is validated against the enqueue-time snapshot and faults `InvalidData` when invalid. |
| `LoadSprite` | `Partial` | Declaration and path validation are `Current`; the WP4 internal service resolves a session-local handle synchronously and loads sprite + descriptor CPU metadata asynchronously. It never reads the raw payload, has no public readiness API, and public runtime wiring is WP5. |
| `LoadData` | `Partial` | Declaration is `Current`; the loader body is `Current` (WP3) in the internal service, and public runtime wiring is WP5. Decompressed bytes for any asset kind. |
| `LoadPack` | `Partial` | Internal VFS mount is `Current` (WP2); service delegation is `Current` (WP3); public runtime wiring is WP5. Mounts above the stack; later wins; no unload; bad pack throws `Bork2dException`. |
| `Browse` | `Partial` | Internal merged snapshot is `Current` (WP2); service delegation is `Current` (WP3); public runtime wiring is WP5. Optional canonical prefix on a `/` segment boundary; sorted by path. |
| `Exists` | `Partial` | Internal exact lookup is `Current` (WP2); service delegation is `Current` (WP3); public runtime wiring is WP5. |
| `AssetInfo` | `Current` (M3 WP2) | Value type returned by `Browse`; header fields plus the supplied pack identifier. |
| `AssetException`/`AssetErrorKind` | `Current` (M3 WP3) | Public error contract for lookup/decode/read/payload-validation failures; pack failures stay `Bork2dException`. |

`IShapeable<T>` is `PolyType.IShapeable<T>`; `Bork2d.Api` references PolyType `1.4.1` for the
constraint only and stays free of Nerdbank.MessagePack, Zstd, `System.IO.Hashing`, and file I/O
(D-M3-22). All members are thread-safe. `LoadPack` is synchronous; loads complete asynchronously.
Do not call `Browse` in `Tick`; handles are session-local, and v1 has no unload (load-and-keep).
`IEngine.Assets` still returns the throwing `UnsupportedAssets` stub in this tree; WP5 wires the
internal service into `Run()`/`Dispose()`.

## 7. Loading, readiness, and failure

`Status: loader Current (M3 WP3); sprite/descriptor CPU pipeline Current (M3 WP4); GPU
readiness/upload Planned (M8).`

- **One loader thread.** The internal asset service owns one dedicated background thread per app
  (started even for an empty VFS) and an **unbounded FIFO** request queue. There is no priority,
  no parallelism, and no public cancellation token. The loader only reads, decompresses, and
  deserializes; it never touches SDL, Vulkan, or game entities.
- **Enqueue-time resolution.** `LoadData`/`LoadAsset<T>`/`LoadSprite` validate the path and the
  disposed state synchronously, capture the current VFS snapshot, and resolve every entry for the
  request against that snapshot. A later overlay mount does not change an in-flight request; new
  lookups see the new stack. `LoadData`/`LoadAsset<T>` resolve the winning entry at enqueue;
  `LoadSprite` uses its captured snapshot on the loader thread for the sprite entry, its texture
  descriptor, and that descriptor's payload.
- **Exact-size reads.** Packs are validated structurally at open; payload decompression is lazy
  per read. `LoadData` decompresses to a declared-size buffer and returns a **fresh** `byte[]` per
  call. `LoadAsset<T>` streams the decompressed payload through the compiled generated shape; the
  stream is drained so a payload that decompresses to fewer or more bytes than its declared `Size`
  is a decode failure. Neither path caches. A deserialized internal `TextureDescriptor`/
  `SpriteAsset` is validated against the same snapshot; invalid data faults `InvalidData`.
- **Sprite loading.** `LoadSprite` normalizes/validates the path and checks the disposed state
  synchronously. A cached handle is returned immediately, whether its load is pending, ready, or
  failed. A missing path logs at Information and returns `Sprite.Empty` without caching a handle;
  repeated misses log again. A present path allocates an opaque handle (ids start at 1;
  `Sprite.Empty`'s `Id == 0` is never a path), caches it, enqueues a sprite + descriptor load on the
  existing loader thread, and returns without waiting. The loader deserializes and validates the
  sprite asset, resolves and validates its texture descriptor (cached per texture path), checks
  that the payload entry exists, is raw, and declares `width × height × 4` bytes, then publishes a
  CPU sprite resource linked to the descriptor resource. The raw payload is **not** read or
  decompressed in M3.
- **Caches.** One CPU descriptor resource per texture path and one sprite resource per sprite path,
  valid or failed; multiple sprites sharing a texture share one descriptor resource. A repeated
  `LoadSprite` returns the same handle and never re-enqueues. `LoadData`/`LoadAsset<T>` never
  consult these caches. A failed descriptor or sprite stays failed for the process (D-M3-18).
- **Failures.** A missing path, a raw entry requested through `LoadAsset<T>`, a decode/size
  failure, and an IO read failure fault the returned task with `AssetException`
  (`NotFound`/`RawAsset`/`Decode`/`Io`/`InvalidData`). A descriptor/sprite validation failure faults
  `InvalidData`; `LoadSprite` instead logs and leaves the handle non-renderable. Pack
  open/parse/mount failures stay `Bork2dException`. There is **no automatic retry**, no loose-file
  fallback, and no placeholder/blocking wait (D-M3-18).
- **Shutdown.** Stopping the service rejects new requests, cancels queued and in-flight load tasks
  (they complete as canceled), joins the loader, closes the mounted packs, and marks the service
  disposed. Sprite loads have no caller task: their handles remain non-renderable and the loads are
  dropped. Shutdown is idempotent; secondary failures are logged, not thrown.
- **Logging** uses the `Bork2d.Assets` category: start (`Pack`/`Empty` with the configured and
  resolved path, version, entries, bytes), overlay mounted, load failure (path, kind, reason),
  `LoadSprite` misses, sprite load success at Debug (path, kind), descriptor cache hits at Debug,
  and stop (pending/canceled counts, mounted packs). Nothing is logged per frame.
- **No renderer in M3.** The loader never creates GPU resources. The CPU resource table is internal
  and immutable after publication; M8 creates GPU pages/uploads on the render thread. There is no
  public readiness query and no GPU work in M3.

A sprite is drawable only when its sprite asset, texture descriptor, and raw payload are loaded and
decompressed and the renderer has created the GPU page and bindless descriptor. M3 publishes the CPU
metadata only: no GPU page exists, nothing renders, and the handle is resolved by the M8 renderer at
draw time. Any missing stage emits no instance: no placeholder, checkerboard, or blocking wait.

- `LoadSprite` returns immediately and is idempotent per path; a missing path returns `Sprite.Empty`
  and logs. A failed decode/validation logs and stays non-renderable; retry is not automatic in v1.
- Readiness can change between the snapshot tick and the draw, so the future renderer resolves at
  draw time; a sprite can appear one frame earlier than a snapshot-time check. `Sprite.Empty`
  (`Id == 0`) is not a path; handles are session-local, save data stores paths, and v1 keeps
  resources for the process.

## 8. Texture descriptors

`Status: internal model and M3 validation Current (M3 WP4); GPU upload and KTX2 Planned (M6/M8).`

A texture is a MessagePack **descriptor** plus a **raw payload**: `textures/player` and
`textures/raw/player`.

| Field | Type | Meaning |
| --- | --- | --- |
| `payload` | `string` | Raw asset path containing RGBA8 pixels (M3 never reads it). |
| `width` | `uint32` | Pixel width, `> 0`. |
| `height` | `uint32` | Pixel height, `> 0`. |
| `format` | `string` | `rgba8` only in v1; `ktx2` is rejected. |
| `filter` | `string` | `nearest` or `linear`. |
| `wrap` | `string` | `clamp` or `repeat`. |

`rgba8` payload: width × height × 4 bytes, row-major, top-left origin, no padding, straight alpha,
8 bits per channel, treated as sRGB. The source is a Unity-style `<file>.toml` sidecar carrying
**import settings only** (`filter`, `wrap`); the baker writes `textures/player` with payload
`textures/raw/player`, and sprite definitions in `sprites/**.toml` reference the descriptor.

- **Internal shape.** `TextureDescriptor` is an internal `[GenerateShape]` class with property-name
  keys (no `[Key]`) and no per-asset version. `System.Numerics.Vector2` uses Nerdbank's generated
  shape in sprite assets; there is no reflection resolver.
- **M3 validation.** The payload path must be canonical and exist in the enqueue-time snapshot, the
  payload entry must be raw, `Width > 0`, `Height > 0`, and `(ulong)Width × Height × 4` must equal
  the payload's declared `Size` (unsigned, overflow-safe). `Format` must be `rgba8`, `Filter` one of
  `nearest`/`linear`, and `Wrap` one of `clamp`/`repeat`.
- **No payload read.** Validation uses header metadata only; the payload is not decompressed or
  uploaded in M3. `LoadAsset<TextureDescriptor>` faults `InvalidData` for a bad descriptor;
  `LoadSprite` logs and leaves the sprite non-renderable.
- **Resource caching.** A validated descriptor becomes one immutable CPU texture resource per
  texture path; a failed descriptor is cached as failed and shared by referencing sprites, with no
  retry. `LoadAsset<TextureDescriptor>` does not use the cache.

## 9. Sprite assets

`Status: internal model, M3 validation, and CPU sprite resources Current (M3 WP4); GPU/draw Planned
(M8).`

A sprite is a MessagePack asset (for example `sprites/player/idle`) describing a textured quad:

| Field | Type | Meaning |
| --- | --- | --- |
| `texture` | `string` | Texture descriptor path (`textures/player`). |
| `uvs` | `Vector2[]` | Normalized UVs, top-left origin. Exactly four. |
| `scale` | `float` | **Pixels per world unit**, `> 0`. |
| `pivot` | `Vector2` | Normalized origin inside the quad; default `(0.5, 0.5)`. |
| `topology` | enum | `Quad` (0), `TriangleStrip` (1), or `Triangles` (2); an integer in the pack. |
| `indices` | `ushort[]?` | `null` for `Quad`; required for the other topologies. |

There is no `vertices` array; the vertex shader generates positions from UVs, texture size, scale, and
pivot.

- Size: `pixelWidth = (maxU - minU) * textureWidth`, `pixelHeight = (maxV - minV) * textureHeight`,
  then `worldWidth = pixelWidth / scale`, likewise for height; `Transform.LocalScale` multiplies after.
  `scale` is pixels per unit, so a 64 px sprite at `scale = 16` is 4 units high.
- `pivot` uses a top-left origin: `(0,0)` is top-left, `(1,1)` bottom-right, `(0.5,0.5)` center. The
  quad is positioned around the pivot and converted to world Y-up; UVs stay top-left origin, ordered
  top-left, top-right, bottom-right, bottom-left.
- `Quad` requires `indices` to be **null** and draws as a four-corner triangle strip. `TriangleStrip`
  requires indices in `0..3`; `Triangles` requires indices in `0..3`, length a multiple of 3.
- v1 draws `Quad` only. Non-Quad assets are valid, validated, and cached as CPU resources but never
  silently treated as quads; the indexed path lands with M8.

**M3 validation.** `SpriteAsset` is an internal `[GenerateShape]` class with property-name keys, no
per-asset version, and no vertices field. The loader validates: `Texture` is a canonical non-empty
path whose descriptor resolves and validates; `Uvs` is exactly four finite `Vector2` values with
every component in `[0,1]`, `maxU > minU`, and `maxV > minV`; `Scale` is finite and `> 0`; `Pivot`
is finite with both components in `[0,1]`; `Topology` is one of the three values; `Quad` has
`Indices == null`; `TriangleStrip`/`Triangles` have non-null indices, every value in `0..3`, at least
three entries, and `Triangles` length a multiple of 3. Invalid data faults `InvalidData` from
`LoadAsset<SpriteAsset>`; `LoadSprite` logs and leaves the handle non-renderable.

The editor writes this as TOML; M4 bakes the MessagePack form. The renderer resolves sprite asset →
descriptor → payload → GPU page; tint, layer, z-index, and transform stay on the renderable. In M3 the
loader stops at the CPU sprite resource linked to its descriptor resource.

## 10. TOML source model

Data is authored as TOML for readable, reviewable, diffable git history, then baked to MessagePack.
One model describes both formats: the same C# type is the TOML authoring model and the MessagePack
runtime model; divergent DTOs are avoided unless generated. A typical type is a
`[GenerateShape]`-annotated class with public settable properties such as `Id`, `Health`, a `Sprite`
asset path, and a list of referenced types; `[Bork2dSerializable]` is optional editor/baker metadata.

- `[GenerateShape]` is Nerdbank's source-generation anchor; only the serialized top-level type needs
  it. The JIT editor/baker may reflect over the same model.
- Serializable types are classes/structs with a public parameterless constructor and public settable
  properties/fields, or a documented constructor-binding scheme. Supported leaves: primitives,
  `string`, enums, `Guid`, UTC `DateTime`, `Vector2/3/4`, `Color`, nullable variants, and
  arrays/lists/dictionaries of them. Missing values use the C# default; required values are validated
  by the editor/baker, and comment round-trip preservation is deferred (see roadmap §9).
- No cycles, delegates, tasks, threads, engine entities, or runtime `Sprite` handles. Polymorphism is
  explicit or absent in v1; there is no assembly-scanning serializer.
- References are canonical paths validated at bake time, never `Sprite.Id` or pack offsets; the editor
  checks the source tree and the baker checks baked assets. A missing reference is a diagnostic plus
  invisible renderable, not a crash; save data uses paths.
- Every baked asset carries a schema version; additive changes with defaults are allowed, while
  renames/removals need a migration or bump. The editor preserves unknown fields where practical,
  runtime errors identify path/version/location, and pack versioning is separately validated. Output
  is deterministic; the runtime never parses TOML.

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
compressed header, and data with absolute offsets; write `assets.pck` deterministically.

| Mode | Header | Asset payloads |
| --- | --- | --- |
| Debug | Always Zstd-compressed, XXH3-64 checksum | Uncompressed, `IsCompressed=false`, `Length==Size` |
| Release | Always Zstd-compressed, XXH3-64 checksum | Zstd max, compressed only when smaller |

`assets.pck` is copied next to the executable (or the `WithAssetsPack` path) when the loader lands
(M3). MSBuild vs CLI is undecided (M4); incremental bake uses timestamps/hashes. The editor shares the validator and baker; CI builds
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
- Runtime projects set `IsAotCompatible` with trim/AOT analyzers; only the AOT smoke app sets
  `PublishAot`. AOT publish/run is a required local gate, not a CI job.

## 14. Threading

`Status: loader Current (M3 WP3); CPU resource table and sprite/descriptor caches Current (M3 WP4);
renderer/GPU integration Planned (M8).`

- Packs are opened and headers parsed by the asset service (`Start`, on the `Run` caller thread);
  path tables are immutable unless a new pack is mounted, and `LoadPack` swaps the overlay stack
  atomically.
- The service owns exactly **one** dedicated loader thread with an unbounded FIFO queue. Requests
  capture the VFS snapshot at enqueue time; the loader thread deserializes and publishes CPU
  resources, completes `TaskCompletionSource`-backed tasks with `RunContinuationsAsynchronously`,
  and never touches SDL, Vulkan, or game entities. Task continuations therefore never run inline on
  the loader.
- **Resource-table ownership.** The loader thread is the only writer of descriptor and sprite
  resources. A published resource is immutable; the table holds one descriptor resource per texture
  path and one sprite resource per sprite path, including cached failures. Handle allocation and
  lookup happen on the calling thread under a short lock; the loader never blocks on a caller. Reads
  take the same short lock; no lock is held across a load, an await, or a blocking wait.
- **Caches.** A repeated `LoadSprite` returns the cached handle without enqueueing; descriptor
  resources are shared by every sprite that references the same texture; `LoadData`/`LoadAsset<T>`
  bypass the resource table entirely. There are no weak references and no unload in v1.
- At shutdown the service stops accepting requests, cancels queued and in-flight load tasks, joins
  the loader at a safe point, and only then closes the mounted archives. Handle completion signals
  remain internal; sprite loads are dropped.
- M3 has no GPU work: the loader never creates texture pages, and there is no public readiness or
  resource-table API. The future renderer creates GPU texture pages and uploads on the render
  thread, reading the immutable CPU resources without per-sprite locks; GPU lifetime is retired
  after referencing frames complete. Load-and-keep applies in v1. See
  [Threading](threading.md#6-asset-and-pack-coordination).

`WithHeadless` supports packs, `LoadData`, `LoadAsset<T>`, `Browse`, and `Exists`; sprite handles
resolve to CPU metadata, but no GPU page exists and nothing renders. `WithOffscreen` runs the normal
upload/draw path without a window or swapchain and discards frames in v1. Neither mode has
`IWindow`/`IInput`. See [Runtime lifecycle](runtime-lifecycle.md#2-modes).

## 15. Limits and security

Provisional v1 limits (final values are M3 decisions): maximum header 64 MiB; maximum assets per pack
1,000,000; maximum path length 512 bytes; maximum decompressed asset size configured per project; no
overlapping payload ranges, path traversal (`..`), absolute paths, or drive letters; unknown pack
versions rejected; decompression bounded by declared `Size` and a global cap; malformed packs fail
with `Bork2dException` (or a loader-specific exception) and a diagnostic, never an out-of-bounds read.

The **header** has an XXH3-64 checksum verified before parsing; individual **payloads have no
checksum** in v1, so a raw payload has no integrity check beyond bounds/length validation. Packs are
trusted local content: signing is out of scope and the parser is defensive against malformed input.

## 16. Current status

| Area | Status |
| --- | --- |
| `Bork2d.Assets` | `Partial` (M3 WP1–WP4): v1 pack reader/writer, structural validation, canonical paths, public `PackBuilder`/`PackCompressionMode`, the internal base/overlay VFS with `LoadPack`/`Browse`/`Exists`, the internal loader service (one thread, FIFO queue, `LoadData`/`LoadAsset<T>`, cancellation/join, `AssetException`), and the internal texture-descriptor/sprite models, validation, `LoadSprite` CPU pipeline, handle table, and resource cache; runtime wiring is WP5 |
| `assets.pck` reader/writer | `Partial` (M3 WP1): v1 format reader and writer implemented; the VFS, overlays, `Browse`/`Exists`, and the internal loader are implemented (WP2–WP3) and wired publicly in WP5 |
| Texture descriptors and raw RGBA8 payloads | `Partial` (M3 WP4): internal `TextureDescriptor` model, M3 validation, and CPU descriptor resources; the raw payload is metadata only. Payload read/upload and KTX2 are M8/M6 |
| Sprite assets (UVs/scale/pivot/topology/indices) | `Partial` (M3 WP4): internal `SpriteAsset` model, M3 validation, CPU sprite resources, and the idempotent session-local handle; `LoadSprite` resolves sprite + descriptor without reading the payload. Drawing is M8 |
| CPU resource table (descriptors/sprites) | `Current` (M3 WP4, internal): one descriptor resource per texture path, one sprite resource per sprite path, failed entries cached, no weak references, no public access or readiness API |
| TOML model, baker, pack builder, deterministic bake | `Planned` M4 |
| Serialization policy | `Partial`: Nerdbank.MessagePack `1.4.95` pinned with generated-shape smoke in Common, tests, and AOT smoke; pack header/entry and internal descriptor/sprite shapes are `Current` (WP1/WP4); `Bork2d.Api` references PolyType `1.4.1` for the `IShapeable<T>` contract only; `LoadAsset<T>` deserialization passes the shape from `T.GetTypeShape()` (WP3) |
| `IAssets` load members | `Partial`: declarations, XML docs, and the `IShapeable<T>` constraint are `Current` (WP2); the internal loader bodies are `Current` (WP3); `LoadSprite` CPU metadata is `Current` (WP4); `IEngine.Assets` still returns the throwing stub until WP5 |
| `IAssets.LoadPack`/`Browse`/`Exists`, `AssetInfo` | `Partial` (M3 WP2–WP3): `AssetInfo`, the internal VFS/lookup, and service delegation are `Current`; public runtime wiring is WP5 |
| `AssetException`/`AssetErrorKind` | `Current` (M3 WP3): public error contract; pack open/parse/mount failures remain `Bork2dException` |
| `Bork2d.Editor` / `Project.Editor` | `Planned` M5; editor absent, host is a template folder |

## 17. Related documents

- [NativeAOT](nativeaot.md) and [Threading](threading.md#6-asset-and-pack-coordination) — runtime
  constraints and loader/upload ownership.
- [Rendering](rendering.md) — texture pages, bindless textures, sprite pipeline.
- [Runtime lifecycle](runtime-lifecycle.md#2-modes) — headless/offscreen.
- [Projects](projects.md) — `Bork2d.Editor`/`Project.Editor` references.
- [API reference](../api/reference.md#7-iassets) — `IAssets` contract.
- [Roadmap](../roadmap.md) and [decision log](../decisions.md) — M3/M4/M5/M8 and D-010–D-012.
