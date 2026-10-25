# Assets and Pack Files

> **Status:** designed in this pass, not implemented. The pack format, virtual filesystem,
> overlay rules, texture descriptors, and sprite asset model are the v1 target. Code lands in
> roadmap **M3 (assets)** and **M4 (serialization/bake)**; the editor follows in **M5**.

This document replaces the earlier "contract only, pipeline deferred" asset document. The
pipeline is now specified. The code is still an empty `Bork2d.Assets` project, so every
statement below is a target contract unless explicitly called out as `Current`.

## 1. End-to-end model

```
Project.Common/Assets/
  data/**.toml, sprites/**.toml, textures/* + textures/*.toml sidecars
        │
        │  asset baker (M4, JIT; TOML → MessagePack, textures → raw RGBA)
        ▼
  MessagePack assets + raw texture payloads
        │
        │  pack builder (M3/M4; compressed header + XXH3-64, Zstd assets in release)
        ▼
  assets.pck  (or additional *.pck overlays)
        │
        │  runtime virtual filesystem (NativeAOT)
        ▼
  asset loader threads → immutable resource table → render thread
```

Key rules:

- The shipped runtime reads **only** `assets.pck` files. TOML is a source/editor format.
- The runtime is NativeAOT: pack parsing, texture descriptors, sprite assets, and custom data
  use [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) generated shapes,
  not reflection.
- Game code holds `Sprite` handles, not textures or asset objects. The renderer resolves
  handles and skips anything whose resources are not ready.
- A source texture is stored in the pack as raw bytes (RGBA8 now; KTX2 later). Sprite
  definitions are MessagePack assets with UVs, a pivot, a topology, optional indices, and a
  **pixels-per-unit** scale; there is no vertices array.
- `assets.pck` has an always-compressed header with an XXH3-64 checksum; asset payloads have
  no checksum.
- Packs overlay: `assets.pck` is the base; `IAssets.LoadPack(path)` mounts another pack above
  it. Later packs win; browsing shows the merged view.

## 2. Asset kinds

| Kind | `isRaw` | Backing representation | Examples |
| --- | --- | --- | --- |
| **MessagePack asset** | `false` | MessagePack-encoded object | Sprite asset, texture descriptor, custom data |
| **Raw asset** | `true` | Opaque bytes | RGBA8 texture payload, KTX2 texture later |

- MessagePack assets are decoded by Nerdbank.MessagePack generated shapes.
- Raw assets are not decoded by the pack layer. Their consumer (texture uploader, `LoadData`
  caller, future audio decoder) interprets the bytes.
- The same path always has the same kind in a pack; an overlay cannot silently change a
  MessagePack asset into a raw asset or vice versa.
- TOML files are never present in a pack.

## 3. Paths

Canonical paths are the asset keys used everywhere at runtime:

- lowercase ASCII;
- `/` separates segments, never `\`;
- no leading or trailing `/`, no empty segments, no `.` or `..` segments;
- **no source extension** (`textures/bg`, not `textures/bg.png`);
- allowed segment characters (proposed): letters, digits, `_`, `-`.

Examples:

```
textures/player
textures/raw/player
sprites/player/idle
data/enemies/boss
```

Rules:

- The loader lowercases the input path and rejects invalid segments. Source files keep their
  normal extensions on disk; the baker strips them when computing the runtime key.
- Paths are case-insensitive by canonicalization: `Textures/BG` and `textures/bg` are the same
  key. Content tools should write canonical paths.
- A duplicate canonical path in one pack is a build error.
- `textures/raw/` is a reserved namespace for raw texture payloads. Sprite authors reference
  the texture descriptor (`textures/player`), not the payload.
- Runtime paths are opaque. They do not imply a file extension or a decoder; the asset type
  or the consuming API determines how the bytes are interpreted.

## 4. `assets.pck` file format

### 4.1 Binary layout

All fixed-width binary fields are little-endian. The header is a Zstd-compressed MessagePack
object; its internal integers use MessagePack's encoding.

| Offset | Size | Field | Notes |
| --- | ---: | --- | --- |
| `0` | 8 | `magic` | ASCII `BORK2DPK` |
| `8` | 4 | `version` | `uint32`, currently `1`; **outside** the compressed header |
| `12` | 4 | `headerCompressedLength` | `uint32`, byte length of the compressed header |
| `16` | 4 | `headerUncompressedLength` | `uint32`, byte length after decompression |
| `20` | 8 | `headerChecksum` | `uint64`, XXH3-64 of the uncompressed header |
| `28` | `headerCompressedLength` | `header` | Zstd-compressed MessagePack `PackHeader` |
| `28 + headerCompressedLength` | … | `data` | Asset payloads, addressed by absolute offsets |

- The 28-byte prefix is fixed.
- `version` is deliberately in the **fixed prefix**, not in `PackHeader`. A future loader can
  read it and choose the v1/v2 parser **before** deserializing; changing `PackHeader` in v2
  cannot break v1 parsing. Only the prefix layout is promised stable across versions.
- The header is **always** compressed, in debug and release builds. Compression level is
  maximum for determinism.
- `headerCompressedLength` is the stored length; `headerUncompressedLength` is the expected
  decompressed length.
- `headerChecksum` is the 64-bit **XXH3** hash (the `XxHash3` variant, seed `0`) of the
  **uncompressed** header, computed with the Microsoft
  [`System.IO.Hashing`](https://www.nuget.org/packages/System.IO.Hashing) package
  ([`XxHash3`](https://learn.microsoft.com/dotnet/api/system.io.hashing.xxhash3)). The loader
  decompresses the header, verifies the hash, then deserializes MessagePack. A mismatch rejects
  the pack before any asset entry is trusted.
- `dataStart = 28 + headerCompressedLength`.
- `offset` in each entry is absolute from the start of the pack file. A valid `offset` is at
  least `dataStart`; `offset + length` must not exceed the file size.
- Individual asset payloads have **no checksum**. Corruption is detected by codec failure,
  declared sizes, and bounds checks; a raw uncompressed payload has no integrity check (by
  design in v1).

### 4.2 Header classes

The header is a MessagePack **object**, not an array. For **version 1**, the header and every
entry are classes serialized with
[Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) generated shapes:

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
| `PackEntry.Path` | string | Canonical asset path. |
| `PackEntry.IsRaw` | bool | `true`: raw bytes; `false`: MessagePack. |
| `PackEntry.IsCompressed` | bool | Whether the stored bytes are Zstd-compressed. |
| `PackEntry.Offset` | uint64 | Absolute byte offset from the start of the pack. |
| `PackEntry.Length` | uint64 | Number of bytes stored at `Offset` (compressed length when compressed). |
| `PackEntry.Size` | uint64 | Number of bytes after decompression. Equal to `Length` when not compressed. |

`PackHeader` is the **v1 shape only**. A future v2 header is a different class/parser selected
from the fixed-prefix `version`; the loader must never assume the v1 shape before checking the
version.

The order of entries is not semantically significant, but the builder sorts by `Path` for
deterministic output. The loader builds a dictionary keyed by canonical path. Because the
header is compressed and checksummed, the loader must not attempt to parse entries until both
checks pass.

Example (conceptual v1 object, serialized and compressed on disk):

```csharp
new PackHeader
{
    Entries =
    [
        new PackEntry { Path = "data/enemies/boss", IsRaw = false, IsCompressed = true,
                        Offset = 4096, Length = 812, Size = 2048 },
        new PackEntry { Path = "sprites/player/idle", IsRaw = false, IsCompressed = false,
                        Offset = 4908, Length = 96, Size = 96 },
        new PackEntry { Path = "textures/player", IsRaw = false, IsCompressed = false,
                        Offset = 5004, Length = 72, Size = 72 },
        new PackEntry { Path = "textures/raw/player", IsRaw = true, IsCompressed = false,
                        Offset = 5076, Length = 4194304, Size = 4194304 },
    ],
}
```

### 4.3 Validation

The loader must reject a pack when any of these fails:

- magic is not `BORK2DPK`;
- `version` is unknown (checked from the fixed prefix before decompressing);
- `headerCompressedLength`/`headerUncompressedLength` exceed configured limits or the file
  size;
- header decompression does not produce exactly `headerUncompressedLength` bytes;
- `headerChecksum` does not match XXH3-64 of the uncompressed header;
- the uncompressed header is not a deserializable v1 `PackHeader`;
- `Entries` is null;
- a path is empty, non-canonical, duplicated, or escapes the virtual root;
- `Offset < dataStart`, `Offset + Length > fileSize`, or entries overlap;
- `IsCompressed == false` and `Length != Size`;
- `IsCompressed == true` and decompression does not produce exactly `Size` bytes;
- the decompressed size exceeds the configured safety limit;
- the same path occurs more than once.

Packs are local files, but they are still parsed defensively: offsets and lengths are
untrusted input, and a corrupt pack must fail with a useful diagnostic, not read out of
bounds. The header checksum protects the index; asset payloads are intentionally not
checksummed in v1.

## 5. Compression

- Codec: **Zstd**, using .NET 11's built-in compression support. No third-party compression
  dependency is planned.
- **Header:** always compressed at maximum compression, in both debug and release builds.
  `headerCompressedLength` and `headerUncompressedLength` describe it; `headerChecksum` is
  XXH3-64 over the uncompressed bytes.
- **Asset payloads, debug builds:** stored uncompressed (`isCompressed = false`,
  `length == size`).
- **Asset payloads, release builds:** compressed at maximum compression and stored compressed
  **only if the compressed bytes are smaller**. Otherwise stored raw with
  `isCompressed = false`.
- `size` is always the decompressed length. `length` is always the stored length.
- Decompression enforces the declared `size` and a maximum safety limit; a stream that
  produces more data is rejected.
- The builder records the Zstd level/settings in the build configuration so that a rebuild of
  the same inputs is deterministic.
- Raw RGBA textures and already-compressed formats often do not shrink; the "store only if
  smaller" rule handles that.
- Verify the .NET 11 built-in Zstd API during M3. If the API differs from the planned shape,
  keep the format ([`isCompressed`, `length`, `size`]) and isolate the codec behind the
  pack reader/writer; do not change the pack layout.

## 6. Virtual filesystem, overlays, and browsing

### 6.1 Mount order

1. `assets.pck` is the **base** pack, mounted at startup.
2. Each `IAssets.LoadPack(path)` mounts a pack **above** the current stack.
3. A lookup walks the stack from top to bottom. Later packs shadow earlier packs with the same
   canonical path.
4. There is **no unload** in v1. A mounted pack stays for the process lifetime.

The default pack path is `assets.pck` next to the executable. The builder may override it:

```csharp
.WithAssetsPack("assets.pck")
```

`IAssets.LoadPack` is intended for DLC, test fixtures, mods, and project-local overlays. It is
thread-safe; the recommended place is `Setup`/before the first tick. Mounting a pack after a
handle has already resolved does not retroactively change that handle; new lookups see the new
pack.

### 6.2 Browsing

The virtual filesystem is browsable:

```csharp
IReadOnlyList<AssetInfo> Browse(string? prefix = null);
bool Exists(string assetPath);
```

- `Browse` returns a merged snapshot of all mounted packs; shadowed entries are hidden.
- `prefix` is a canonical path prefix such as `sprites/player`; `null` means the whole tree.
- `AssetInfo` exposes `Path`, `IsRaw`, `IsCompressed`, `Length`, `Size`, and `PackPath`.
- Browsing does not load or decompress payloads. It is for tools, editor asset browsers, and
  diagnostics, not per-frame gameplay.
- `Exists` applies the same overlay rules as `LoadSprite`/`LoadAsset`.

### 6.3 Missing or malformed pack

This is **accepted**: the game must throw.

- A missing base pack (`assets.pck` or the path passed to `WithAssetsPack`) is a startup error.
  `Build()` throws a `Bork2dException` with the resolved path before window/GPU startup; it
  does not silently fall back to an empty filesystem.
- A malformed base pack is also a startup error; the asset service throws `Bork2dException`
  during startup, identifying the pack path and the failing validation rule.
- `IAssets.LoadPack(path)` throws for a missing or malformed overlay.
- Tests and tools that need no assets use a valid empty pack, not a missing file.

There is no implicit empty-FS mode in v1.

### 6.4 Loose files

Runtime mounts **packs only** in v1. Loose directories, source TOML, and source images belong
to `Project.Common` and the editor. A development-only loose-file overlay is not part of the
runtime contract; adding one requires a decision.

## 7. Public asset API

Target `IAssets` surface:

```csharp
public interface IAssets
{
    Task<T> LoadAsset<T>(string assetPath) where T : class;
    Sprite LoadSprite(string assetPath);
    Task<byte[]> LoadData(string assetPath);

    void LoadPack(string packPath);

    IReadOnlyList<AssetInfo> Browse(string? prefix = null);
    bool Exists(string assetPath);
}

public readonly record struct AssetInfo(
    string Path,
    bool IsRaw,
    bool IsCompressed,
    long Length,
    long Size,
    string PackPath);
```

| Member | Semantics |
| --- | --- |
| `LoadAsset<T>` | Finds a MessagePack asset, decompresses, deserializes with its Nerdbank generated shape. Throws for raw assets or missing paths. |
| `LoadSprite` | Finds a MessagePack **sprite asset** by path and returns a handle immediately; loading is asynchronous. Missing/invalid path returns `Sprite.Empty` and logs. |
| `LoadData` | Returns the decompressed bytes for any asset kind. Useful for raw payloads and tooling. |
| `LoadPack` | Mounts a pack above the current stack. Later wins; no unload. |
| `Browse` | Merged snapshot of visible assets. Prefix filter optional. |
| `Exists` | Whether the merged filesystem contains the canonical path. |

Rules:

- All methods are thread-safe for requests; completion is asynchronous.
- `LoadAsset<T>` requires a Nerdbank generated shape for `T`; a missing shape is a
  configuration/build error, not a runtime reflection fallback.
- `LoadData` returns a copy/decoded buffer; it does not expose pack-backed memory directly.
- Game code should not use `Browse` in `Tick`; it is a tooling/diagnostic API.
- `Sprite` handles remain session-local and are never persisted. Asset files reference paths.
- No public pack unload or asset unload exists in v1. Residency is load-and-keep; the API can
  grow unloading later without changing paths.

## 8. Texture descriptors

A texture is represented by a **MessagePack descriptor** plus a **raw payload**.

Descriptor path example: `textures/player`. Payload path example: `textures/raw/player`.

Logical descriptor fields:

| Field | Type | Meaning |
| --- | --- | --- |
| `payload` | string | Raw asset path containing the pixels/KTX2 bytes. |
| `width` | uint32 | Pixel width. |
| `height` | uint32 | Pixel height. |
| `format` | string | `rgba8` now; `ktx2` later. |
| `filter` | string | `nearest` or `linear`. |
| `wrap` | string | `clamp` or `repeat`. |

Raw payload format `rgba8`:

- width × height × 4 bytes;
- row-major, top-left origin, no row padding;
- straight alpha, 8 bits per channel;
- interpreted as sRGB color data by the renderer unless a later color-space field says
  otherwise.

Later `ktx2`:

- the payload is a KTX2 container and carries its own dimensions/format;
- the descriptor still supplies sampler state and validates dimensions;
- the descriptor fields may become redundant and can be relaxed without changing the pack
  format.

Source TOML for a texture is a Unity-style sidecar next to the source file:

```toml
# textures/player.png.toml
[texture]
filter = "nearest"
wrap = "clamp"
```

The sidecar carries **import settings only**. Sprite definitions live in separate
`sprites/**.toml` files. The baker reads the sidecar, writes the runtime descriptor at
`textures/player` with the generated `payload` path (`textures/raw/player`), and drops the
`.png.toml` name from the runtime key. Sprite assets reference the descriptor path, not the
payload path.

## 9. Sprite assets

A sprite is a **MessagePack asset** at a path such as `sprites/player/idle`. It describes a
textured quad using UVs, a per-sprite pixels-per-unit scale, a pivot, a topology, and optional
indices.

Logical fields:

| Field | Type | Meaning |
| --- | --- | --- |
| `texture` | string | Texture descriptor path (`textures/player`). |
| `uvs` | Vector2[] | Normalized UVs, top-left origin. Exactly four. |
| `scale` | float | **Pixels per world unit** for this sprite. |
| `pivot` | Vector2 | Normalized origin inside the quad; default `(0.5, 0.5)` (center). |
| `topology` | enum | `Quad`, `TriangleStrip`, or `Triangles`. |
| `indices` | `ushort[]?` | `null` for `Quad`; required for `TriangleStrip`/`Triangles`. |

There is no `vertices` array. Vertex positions are generated in the vertex shader from the
UVs, texture size, scale, and pivot.

v1 rules:

- The four corners are derived from the UV span and the texture descriptor:
  - `pixelWidth = (maxU - minU) * textureWidth`;
  - `pixelHeight = (maxV - minV) * textureHeight`;
  - `worldWidth = pixelWidth / scale`, `worldHeight = pixelHeight / scale`.
- `scale` is **pixels per unit**, so the renderer divides by it. Example: a 64 px high sprite
  with `scale = 16` is 4 world units high. `Transform.LocalScale` multiplies afterward.
- `pivot` is normalized with a top-left origin: `(0,0)` is the quad's top-left, `(1,1)` the
  bottom-right. `(0.5,0.5)` is the default center. The generated quad is positioned around
  the pivot and converted to world Y-up; UVs remain top-left origin.
- The four UVs are ordered top-left, top-right, bottom-right, bottom-left.
- `topology = Quad`: `indices` must be **null**; the quad is drawn as a four-corner triangle
  strip.
- `topology = TriangleStrip`: `indices` is required; each value is `0..3` and selects one of
  the four generated corners.
- `topology = Triangles`: `indices` is required; each value is `0..3`, and the length must be a
  multiple of 3.
- The v1 renderer draws `Quad`. Non-Quad assets are valid and validated, but are not silently
  treated as quads; the indexed draw path lands with M8.
- `indices` is never generated by the Quad editor path; it is absent in TOML for a Quad.

Source TOML example:

```toml
# sprites/player/idle.toml
texture = "textures/player"
scale = 16.0          # 16 source pixels = 1 world unit
pivot = [0.5, 0.5]    # normalized, top-left origin
topology = "quad"     # indices must be absent/null for Quad

uvs = [
  [0.0, 0.0],         # top-left
  [0.25, 0.0],        # top-right
  [0.25, 0.25],       # bottom-right
  [0.0, 0.25],        # bottom-left
]

# Non-Quad only, for example:
# topology = "triangles"
# indices = [0, 1, 2, 0, 2, 3]
```

The editor writes this TOML; M4 bakes it to a MessagePack sprite asset. The pack contains the
MessagePack form, not the TOML.

`LoadSprite("sprites/player/idle")` returns a handle. The renderer resolves the sprite asset,
then its texture descriptor, then the raw payload, and skips the sprite until all are ready.
Tint, layer, z-index, and the world transform stay on the renderable, not in the sprite asset.

## 10. Loading, readiness, and failure

A sprite is renderable only when the whole chain is ready:

1. the sprite MessagePack asset is loaded and deserialized;
2. the texture descriptor is loaded;
3. the raw payload is resident and decompressed;
4. the renderer has created the GPU texture page and updated the bindless descriptor.

Any missing/not-ready stage means the renderer emits no instance for that sprite. There is no
placeholder triangle, checkerboard, or blocking wait.

- `LoadSprite` returns a handle immediately and is idempotent per canonical path.
- A missing path returns `Sprite.Empty` and logs a diagnostic.
- A failed decode/upload logs and leaves the handle non-renderable. Retry is not automatic in
  v1.
- Readiness can change between the tick that produced a snapshot and the renderer's draw; the
  renderer resolves at draw time. A sprite can appear one frame earlier than a snapshot-time
  check would suggest.
- `Sprite.Empty` (`Id == 0`) means "no sprite" and is not an asset path.
- Handles are session-local and are not persisted. Data/save files store asset paths.
- v1 is load-and-keep: handles and loaded resources remain for the process lifetime. Pack
  unloading and asset eviction are future work, but the public API is shaped so unloading can
  be added without changing paths.

## 11. Build pipeline

### 11.1 Source layout

Proposed:

```
Project.Common/
  Assets/
    data/enemies/boss.toml
    sprites/player/idle.toml
    textures/player.png
    textures/player.png.toml     # Unity-style sidecar: import settings only
```

- TOML is the git-friendly source of truth for data and sprite definitions.
- Source textures are regular files (`player.png` or raw RGBA). Each source texture has a
  sidecar `<file>.toml` with import settings (filter/wrap, etc.), Unity-style. The sidecar does
  **not** contain sprite slices; those live in `sprites/**.toml`.
- The source texture may be a normal image or raw RGBA; the baker normalizes it to the runtime
  payload format (RGBA8 now, KTX2 later).
- Source extensions and the `.toml` sidecar suffix are stripped when computing runtime keys:
  `textures/player.png` + `textures/player.png.toml` → descriptor `textures/player`;
  `sprites/player/idle.toml` → `sprites/player/idle`.

### 11.2 Bake

The asset baker (M4) runs on the JIT build tool:

1. parse and validate TOML data (custom classes);
2. parse sprite TOML and validate UVs/scale/pivot/topology/indices/texture references;
3. read the texture sidecar and source image/raw RGBA, produce the runtime RGBA8 payload (or
   KTX2 later), and write the texture descriptor;
4. emit MessagePack assets for data, sprites, and texture descriptors;
5. report all errors with file/line context;
6. emit an intermediate asset tree for the pack builder.

The runtime never runs TOML parsing or reflection-based serialization.

### 11.3 Pack

The pack builder:

1. collects the baked MessagePack assets and raw payloads;
2. establishes canonical paths, rejects duplicates, sorts entries;
3. serializes the `PackHeader`/`PackEntry` objects with Nerdbank.MessagePack, compresses the
   header with Zstd maximum compression, and computes XXH3-64 over the uncompressed header
   bytes;
4. compresses asset payloads in release builds (Zstd max, only if smaller) or stores them
   uncompressed in debug;
5. writes the 28-byte prefix (including the fixed-prefix version), compressed header, and data
   with absolute offsets;
6. writes `assets.pck` deterministically.

Build modes:

| Mode | Header | Asset payloads |
| --- | --- | --- |
| Debug | Always Zstd-compressed, XXH3-64 checksum | Uncompressed, `isCompressed=false`, `length==size` |
| Release | Always Zstd-compressed, XXH3-64 checksum | Zstd maximum, `isCompressed=true` only when smaller |

`assets.pck` is copied next to the game executable (or to the path passed to
`WithAssetsPack`).

### 11.4 Build integration

- MSBuild target or an explicit baker/pack CLI (decision `Open`).
- Incremental bake based on source file timestamps/hashes.
- The editor uses the same validator and baker; it never reads TOML in the runtime path.
- CI builds both a debug pack (compressed header, uncompressed assets) and a release pack
  (compressed header, compressed assets where smaller) and runs the loader validation tests
  against both.

## 12. Editor integration

`Bork2d.Editor` (Avalonia, JIT) works on the source tree, not on `assets.pck`:

- browse source textures/data under `Project.Common/Assets`;
- define sprite slices as quads: rectangle, pivot, pixels-per-unit scale, topology, texture;
- generate the sprite TOML (`texture`, `uvs`, `scale`, `pivot`, `topology`, optional `indices`);
- edit texture sidecars (`<file>.toml` filter/wrap/import settings);
- edit custom serializable classes in TOML with a property grid and raw TOML fallback;
- validate asset references and slice rectangles;
- invoke the baker/pack build (or leave it to the normal build).

The editor does not edit the binary pack and does not run the Vulkan renderer. See
[Editor](editor.md) and [Data and serialization](data-and-serialization.md).

## 13. NativeAOT

The runtime/game is NativeAOT (editor and baker are JIT; see [NativeAOT](nativeaot.md)):

- pack header/entry classes, texture descriptors, sprite assets, and custom data use
  [Nerdbank.MessagePack](https://github.com/AArnott/Nerdbank.MessagePack) generated shapes;
- `LoadAsset<T>` requires a generated shape; there is no reflection resolver at runtime;
- no `Type.GetType`, assembly scanning, or dynamic code in pack/asset loading;
- Zstd is the .NET 11 built-in implementation, so no extra native codec is needed;
- the pack path and asset keys are data, not types; no assembly discovery is required;
- `WithGame(instance)` is used instead of constructing the game via reflection.

## 14. Threading

- Pack files are opened and their headers parsed at startup (main thread / asset service).
  The resulting path tables are immutable unless a new pack is mounted.
- `LoadPack` updates the overlay stack atomically for lookups; it is thread-safe and should be
  called during `Setup` or before loading.
- Asset loader threads decompress/deserialize/upload-request; they never touch Vulkan.
- The render thread creates GPU texture pages and updates bindless descriptors.
- The renderer reads an immutable resource table; it does not lock per sprite.
- GPU texture lifetime is retired by the render thread after frames that reference it have
  completed.

See [Threading and frame handoff](threading.md#8-asset-and-pack-coordination).

## 15. Headless and offscreen modes

- **No-render headless (`WithHeadless(ticksPerSecond = 60)`):** packs, `LoadData`,
  `LoadAsset<T>`, `Browse`, and `Exists` work. Sprite handles can be resolved to CPU metadata,
  but there is no GPU page and no rendering. Useful for dedicated servers and data-only tools.
- **Offscreen (`WithOffscreen(w, h)`):** the normal asset/upload/draw path runs, but there is
  no window or swapchain. Frames are discarded in v1; the target exists for validation and
  benchmarks.
- Neither mode has `IWindow`/`IInput`; accessing those throws.

See [Runtime lifecycle](runtime-lifecycle.md#10-headless-and-offscreen-modes).

## 16. Limits and security

Proposed v1 limits (final values `Open`):

- maximum header size: 64 MiB;
- maximum assets per pack: 1,000,000;
- maximum path length: 512 bytes;
- maximum decompressed asset size: configured per project, with a sane default;
- no overlapping payload ranges;
- no path traversal (`..`), no absolute paths, no drive letters;
- unknown pack version is rejected;
- decompression is bounded by the header's `size` and a global cap;
- malformed packs fail with `Bork2dException` (or the loader's specific exception) and a
  diagnostic, never an out-of-bounds read.

- The **header** has an XXH3-64 checksum; the loader verifies it before parsing.
- Individual **asset payloads have no checksum** in v1. A raw uncompressed payload therefore
  has no integrity check beyond bounds/length validation; packs are trusted local content.
- Authentication/signing is out of scope; the parser is defensive against malformed offsets,
  lengths, and compressed streams.

## 17. Open questions

| Topic | Question | Proposed default |
| --- | --- | --- |
| Texture descriptor path | Descriptor at `textures/<name>`, payload at `textures/raw/<name>`? | Yes; raw namespace is reserved. |
| Sprite geometry | Quad corner generation from UVs + texture size + pivot? | Four corners, `scale` in pixels/unit, pivot default center. |
| Indexed path | When does the renderer consume `indices` for `TriangleStrip`/`Triangles`? | Schema carries indices now; v1 renderer is `Quad` only; indexed path in M8. |
| KTX2 migration | Which descriptor fields become optional? | Width/height/format validation relaxed; sampler fields remain. |
| Memory mapping | Map packs or read into memory? | Read header, stream/decompress payloads; no full-file requirement. |
| Overlay after load | Mounting a pack after handles resolved? | New lookups see it; existing handles unchanged. |
| Unload | Pack/asset unload in v1? | No; load-and-keep. |
| Editor pack preview | Should the editor inspect built packs? | Source-only first; preview later. |
| Zstd API | Exact .NET 11 built-in API surface? | Validate in M3; keep format stable behind adapter. |
| Limits | Header/asset/file size caps? | Values above are proposals. |
| Colorspace | Add `colorSpace` to descriptors? | v1 assumes sRGB color textures. |
| Path charset | Forbid dots/spaces? | Lowercase letters, digits, `_`, `-`, `/`. |

## 18. Current status

| Area | Current | Target |
| --- | --- | --- |
| `Bork2d.Assets` | Empty project | Pack parser/writer, VFS, loaders (M3) |
| `assets.pck` | Does not exist | Format above (M3) |
| Overlays / browse | No API | `LoadPack`, `Browse`, `Exists` (M3) |
| Texture descriptor | Not implemented | MessagePack descriptor + raw payload (M3/M4) |
| Sprite asset | `Sprite` handle only | MessagePack quad with UVs/scale/pivot/topology/indices (M3/M4) |
| TOML → MessagePack bake | None | M4 |
| Editor slicing | None | M5 |
| Runtime rendering of assets | None | M8 |
| NativeAOT pack path | Not implemented | Nerdbank.MessagePack generated shapes (M3/M4) |

## 19. Related documents

- [Editor](editor.md) — source authoring and slicing.
- [Data and serialization](data-and-serialization.md) — TOML/MessagePack/custom classes.
- [NativeAOT](nativeaot.md) — runtime constraints and source generation.
- [Threading and frame handoff](threading.md#8-asset-and-pack-coordination) — loader/upload
  ownership.
- [Rendering](rendering.md#12-asset-upload-and-residency) — texture upload and bindless pages.
- [Runtime lifecycle](runtime-lifecycle.md#10-headless-and-offscreen-modes) — headless modes.
- [Roadmap](../roadmap.md) — M3 assets, M4 bake, M5 editor, M8 renderer.
