# M3 — Assets: Pack, VFS, Descriptors (spec)

> **Status:** **approved by the human owner (2026-10-26).** The decisions in §4 are locked for the
> M3 implementation; implementation has not started. This spec owns M3 scope and
> semantics. It supersedes conflicting statements in
> [Assets and tooling](../architecture/assets-and-tooling.md) and the M3 rows of
> [Roadmap §9](../roadmap.md#9-open-decisions) until those documents are reconciled by WP5.
> [AGENTS.md](../../AGENTS.md) owns the workflow; the [decision log](../decisions.md) owns accepted
> decisions and now records D-M3-01…D-M3-20.
>
> **Outcome:** a runtime that opens `assets.pck`, mounts overlays, browses canonical paths, loads
> raw data and MessagePack assets, and resolves sprite/texture CPU metadata — headless, with no
> SDL, Vulkan, TOML, reflection resolver, or GPU.

## 1. Goal

Replace the M1/M2 `UnsupportedAssets` stub with a real asset pipeline:

- A v1 `assets.pck` reader and writer with the magic/prefix, Zstd header, XXH3-64 checksum, and
  integer-keyed MessagePack header.
- A virtual filesystem mounted from the base pack plus later-wins overlays; no unload.
- Canonical asset paths, `Exists`, `Browse`, and `AssetInfo`.
- One loader thread, a FIFO request queue, a CPU resource table, and asynchronous `LoadData` /
  `LoadAsset<T>` requests.
- Texture descriptors and sprite assets as internal MessagePack models, with M3 validation and
  no GPU work.
- `IAssets.LoadPack`, `Browse`, `Exists`, `LoadData`, `LoadAsset<T>`, and `LoadSprite`; a public
  pack-writer tooling API in `Bork2d.Assets`; a `WithoutAssets()` builder opt-out.
- A base pack that is required at `Run()` startup unless the app opts out, and a NativeAOT smoke
  that builds and loads a pack without reflection or a GPU.

M3 stops before TOML baking (M4), the editor (M5), Vulkan (M6), frame handoff (M7), and sprite
drawing (M8). Sprite payload pixels are not decoded or uploaded in M3.

## 2. Scope

### 2.1 In scope

| Area | M3 deliverable |
| --- | --- |
| Pack format | v1 reader/writer: `BORK2DPK`, 28-byte prefix, Zstd header, XXH3-64 checksum, `uint64` offsets, `uint32` lengths/sizes, integer-keyed `PackHeader`/`PackEntry`. |
| Pack writer | Public tooling `PackBuilder`/`PackCompressionMode` in `Bork2d.Assets`; debug/release payload policy; `Build()` and streaming `Write`. |
| VFS | Base `assets.pck`, `LoadPack` overlays, later wins, no unload, merged `Browse`/`Exists`, `AssetInfo`. |
| Paths | Canonical normalization/validation; filesystem paths relative to the process current directory or absolute. |
| Loading | One dedicated loader thread, FIFO queue, cancellation at shutdown, `LoadData`, `LoadAsset<T>`, `LoadSprite` CPU metadata, resource caching. |
| Descriptors | Internal `TextureDescriptor` model and validation; raw RGBA8 payload metadata; sRGB only. |
| Sprites | Internal `SpriteAsset` model (texture, UVs, scale, pivot, topology, optional indices); M3 validation; no vertices array. |
| Public API | `IAssets` additions, `AssetInfo`, `AssetException`/`AssetErrorKind`, `Bork2dAppBuilder.WithoutAssets()`, tooling pack builder. |
| Runtime | Base pack opened in `Run()` before the window and `Setup`; asset service lifetime; error/lifecycle matrix; logging. |
| Verification | Deterministic pack/VFS/loader/descriptor/sprite tests; test migration; AOT headless pack load/browse; full local gates. |

### 2.2 Out of scope (do not implement in M3)

- TOML models, TOML parsing/writing, the JIT baker, `bork2d.project.toml`, MSBuild bake, release
  bake integration (M4).
- The Avalonia editor and texture slicing (M5).
- KTX2, compressed GPU texture formats, color-space fields, mipmaps, samplers as GPU objects
  (M6/M8). M3 accepts only `rgba8` descriptors.
- GPU texture upload, bindless pages, sprite drawing, draw ordering, camera projection (M6/M8).
- PNG/JPEG decoding, image resizing, atlas generation, mesh generation.
- Loose-file overlays, hot reload, automatic retry, pack unload, pack signing, encryption, or
  payload checksums.
- Audio, animation, tilemaps, materials, scene serialization.
- A public texture/sprite descriptor model. Descriptors and sprite assets stay internal; games use
  `Sprite` handles, `LoadData`, `Browse`, and their own `[GenerateShape]` data types.
- A public `LoadAsset<T>` registration API or a shape-dependency in `Bork2d.Api`.
- Multi-pack unloading, pack replacement while mounted, or editing `.pck` files.
- A pack CLI. The public `PackBuilder` is the M3 tooling surface; M4 owns the bake tool.

## 3. Baseline and reconciliation

The M2 tree is the starting point ([M2 spec](m2-sdl-platform.md)):

- `Bork2d.Assets` is an empty scaffold (`AssemblyReferenceAnchor` only).
- `IAssets` has `LoadAsset<T>`/`LoadSprite`/`LoadData`; `IEngine.Assets` returns
  `UnsupportedAssets` and every member throws `NotSupportedException`.
- `WithAssetsPack` stores a relative path; `Build()` shape-checks it and never touches the
  filesystem.
- `Build()` creates no SDK state, window, GPU resource, thread, or filesystem state; `Run()` has a
  windowed loop and a one-tick headless/offscreen path.
- Tests build apps without any `assets.pck`; 292 tests pass in M2.
- The architecture doc is a target contract with provisional limits and a per-asset schema
  version; those statements are superseded for M3 as recorded below.

M3 changes or removes these statements (all intentional, recorded in §4):

| Current text | M3 change |
| --- | --- |
| `Bork2d.Assets` is an internal implementation project | Still internal by default, but **public for tooling only**: `PackBuilder` and `PackCompressionMode`. Game code still references only `Api`/`Runtime`. |
| A missing base pack throws from `Build()` | `Build()` stays filesystem-free; `Run()` opens/validates the base pack **before** the window and `Setup` and throws `Bork2dException` there. |
| `assets.pck` is copied/found next to the executable | Relative filesystem paths resolve against the **process current directory**; absolute paths are accepted. The app may be launched from a different directory. |
| Every baked asset carries a schema version | **No per-asset version.** The pack prefix `version` is the only version in M3. |
| Provisional limits: 64 MiB header, 1M assets, 512-byte paths | **No policy caps.** Format field widths (`uint32` header lengths, `uint32` entry length/size, `uint64` offset), the file length, and .NET allocation limits are the only bounds. |
| The pack writer is deterministic / byte-reproducible | **No byte determinism guarantee.** Entries are sorted by path for stable structure, but compressed bytes may vary. |
| `LoadSprite` requires sprite + descriptor + payload + GPU page | M3 loads **sprite + descriptor** into CPU metadata; the raw payload is loaded on first texture use in M8. |
| `LoadAsset<T>` is for "built-in MessagePack types" | The loader is shape-generic. M3 tests use engine-internal shapes; game types become usable in M4 when the game-side shape package setup lands. |
| `Bork2d.AotSmoke` references only `Api`, `Common`, `Runtime` | Adds `Bork2d.Assets` to use the public pack writer and load a pack in the default run. |
| `WithAssetsPack` is stored but never used | Opens the base pack at `Run()`; `WithoutAssets()` opts out. |
| `IAssets.LoadPack`/`Browse`/`Exists`, `AssetInfo` are `Planned` | Implemented in M3. |
| `IAssets` has no XML docs (known gap) | XML docs added, with the other `Data.cs` public types. |
| Retry/loose-file/KTX2 are open M3 questions | Resolved: no automatic retry, no loose-file overlay, RGBA8 + sRGB only. |

## 4. Locked decisions

All rows below were answered by the human owner on 2026-10-26. Implementers must not re-litigate
them; if an implementation cannot satisfy one, stop and report (AGENTS.md).

| ID | Decision |
| --- | --- |
| **D-M3-01** | **Base pack opens in `Run()`, before the window and `Setup`.** `Build()` remains filesystem-free. A missing or malformed base pack throws `Bork2dException` from `Run()` before any window, OS platform, `Setup`, or `Teardown`; the partially started asset service is cleaned up. |
| **D-M3-02** | **Base pack required unless the app opts out.** `Bork2dAppBuilder.WithoutAssets()` selects an empty VFS; `LoadPack` may still mount overlays. `WithAssetsPack` and `WithoutAssets` are mutually exclusive (`Build()` throws `Bork2dException` if both are used). |
| **D-M3-03** | **Filesystem paths are current-directory-relative or absolute.** `WithAssetsPack(path)` and `LoadPack(path)` resolve relative paths against the process current directory at the time of the call; absolute paths are accepted. This replaces "next to the executable". |
| **D-M3-04** | **`Bork2d.Assets` is public for tooling only.** It exposes `PackBuilder` and `PackCompressionMode`; the rest stays internal. Game code still references only `Api`/`Runtime`. `Bork2d.AotSmoke` references `Bork2d.Assets`; the dependency matrix, dependency test, and project docs are updated. |
| **D-M3-05** | **Pack v1 schema uses integer MessagePack keys.** `PackHeader`/`PackEntry` use `[Key(n)]`; `PackEntry.Length`/`Size` are `uint32`; `PackEntry.Offset` is `uint64`; the header prefix lengths stay `uint32`. No per-asset version field. |
| **D-M3-06** | **No policy size caps.** Validation uses the format's field widths, the pack file length, range/overlap checks, and .NET allocation limits. Header and MessagePack assets are read through streaming/limiting paths; `LoadData` is bounded by `Array.MaxLength` because it returns `byte[]`. |
| **D-M3-07** | **Compression:** the header is always maximum-quality Zstd (`Quality = ZstandardCompressionOptions.MaxQuality`, `AppendChecksum = false`). Debug payloads are uncompressed; release payloads are maximum-quality Zstd only when strictly smaller. Payloads have **no checksum**. |
| **D-M3-08** | **No byte-level determinism.** The writer sorts entries by path and uses a fixed schema, but `Build()`/`Write()` do not promise identical bytes across runs, .NET versions, or Zstd versions. Tests assert structure and round-trip, not byte equality. |
| **D-M3-09** | **VFS:** base pack at the bottom, `LoadPack` mounts above, later packs win, no unload. A malformed overlay throws `Bork2dException` and is not mounted. `LoadPack` parses synchronously; mounting is atomic and thread-safe. |
| **D-M3-10** | **Canonical paths:** public inputs are normalized (lowercase, `\` → `/`) and then validated; invalid paths throw `ArgumentException` (null throws `ArgumentNullException`). Valid missing paths are "not found", not argument errors. `Browse(null)` means the whole tree; a non-null prefix is matched on a segment boundary. |
| **D-M3-11** | **One loader thread.** The asset service owns one dedicated loader thread and an unbounded FIFO request queue. Requests are processed in order; the thread is cancellable and joined at shutdown. No request is completed on the tick thread. |
| **D-M3-12** | **Caching:** one CPU sprite resource and one descriptor resource per path; repeated `LoadSprite` returns the same handle. `LoadData` returns a fresh decoded `byte[]` per call; `LoadAsset<T>` returns a fresh deserialized instance per call. |
| **D-M3-13** | **`LoadSprite` in M3:** resolve the path and allocate/reuse a handle synchronously; a missing path returns `Sprite.Empty` and logs (no exception); the sprite asset and descriptor load asynchronously; the raw payload is **not** loaded in M3. Load failures log and leave the handle non-renderable; there is no public readiness API. |
| **D-M3-14** | **`IAssets` lifetime:** `IEngine.Assets` returns a real service in every mode (including `WithoutAssets`, as an empty VFS). The getter never throws. After `Run()` returns/throws or the app is disposed, every `IAssets` member throws `ObjectDisposedException`. |
| **D-M3-15** | **Error contract:** pack open/parse/mount failures use `Bork2dException`; asset lookup/decode/IO failures use the new public `AssetException` with `AssetErrorKind` and `AssetPath`. Invalid arguments throw `ArgumentException`/`ArgumentNullException` synchronously. `LoadSprite` missing is not an exception. |
| **D-M3-16** | **`LoadAsset<T>` is generic over compiled shapes.** It requires a Nerdbank/PolyType generated shape on `T`; M3 tests use engine-internal shapes. `Project.Common` stays `Api`-only in M3; game data types become usable in M4. A missing shape faults the `Task` with `AssetException` naming `T`. `PackBuilder.AddMessagePack<T>` constrains `T : IShapeable<T>` so tooling gets a compile-time shape requirement. |
| **D-M3-17** | **Descriptors and sprites stay internal.** They are MessagePack assets with generated shapes; no public `TextureDescriptor`/`SpriteAsset` types. Sprite topology is an integer enum in the pack and a string in TOML (M4). Only `rgba8`/sRGB is accepted. No per-asset version. |
| **D-M3-18** | **No automatic retry; no loose-file overlay; no KTX2/color-space fields.** A failed load logs and stays failed for the process (v1 has no unload). Packs are the runtime source. |
| **D-M3-19** | **AOT smoke always exercises assets.** The default no-argument `Bork2d.AotSmoke` run builds a small temporary pack with the public writer, loads raw data and a generated-shape MessagePack asset, browses/exists, runs one headless tick, and deletes the temp pack. `--windowed` keeps the M2 demo and uses `WithoutAssets()`. |
| **D-M3-20** | **Dependencies:** `Bork2d.Assets` adds `Nerdbank.MessagePack` 1.4.95, `PolyType` 1.4.1 (public `IShapeable<T>` in the tooling API), and `System.IO.Hashing` 11.0.0-rc.1.26425.128 (XXH3-64). Zstd is the .NET 11 built-in `System.IO.Compression` implementation; no third-party codec. |
| **D-M3-21** | **Five sequential work packages** (§13): pack format/writer, VFS/browse, loader/threading, descriptors/sprites, runtime/AOT/docs/acceptance. No parallel wave. |

## 5. Public contract

### 5.1 `IAssets`

`Bork2d.Api` adds the three declared-but-planned members and documents the interface. The existing
three members change from `NotSupportedException` to real behavior.

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
```

| Member | Semantics |
| --- | --- |
| `LoadAsset<T>(string)` | Loads a MessagePack asset with a generated shape on `T`. A fresh instance per call. Raw entries fault with `AssetException` (`RawAsset`); missing paths fault with `NotFound`; decode/shape failures fault with `Decode`. Requires `[GenerateShape]` on `T` (or a compiled shape provider); a missing shape names `T` in the exception. In M3 this is tested with internal shapes; game types are usable in M4. |
| `LoadSprite(string)` | Resolves the path synchronously and returns a session-local handle. Missing path → `Sprite.Empty` + log. Present path → handle immediately; the sprite asset and descriptor load on the loader thread; the raw payload is **not** loaded in M3. Idempotent per path. Load failures log and leave the handle non-renderable; no readiness API in M3. |
| `LoadData(string)` | Decompressed bytes for any asset kind (raw or MessagePack), as a **fresh** `byte[]` per call. Missing → `AssetException` (`NotFound`); a declared size above `Array.MaxLength` → `AssetException` (`InvalidData`). |
| `LoadPack(string)` | Synchronously opens, validates, and mounts a pack **above** the current stack; later mounts shadow earlier paths. Bad/missing pack → `Bork2dException`; the stack is unchanged. Thread-safe and atomic. No unload. |
| `Browse(string? prefix = null)` | Snapshot of visible entries with shadowed paths hidden. `null` = all; non-null prefix is normalized and matched on a `/` segment boundary. Sorted by `Path` ordinal. Empty VFS → empty list. Invalid prefix → `ArgumentException`. |
| `Exists(string assetPath)` | `true` when the merged filesystem contains the canonical path. Invalid path → `ArgumentException`; valid missing → `false`. |

- All members are thread-safe. `LoadPack` performs synchronous file I/O and validation; call it
  before the first tick when possible.
- `LoadData`/`LoadAsset<T>` validate arguments synchronously; missing/corrupt/decode failures fault
  the returned `Task`. They do not throw synchronously for a missing asset.
- No cancellation token in v1. Shutdown cancels engine-owned pending work (§8.7).
- After `Run()`/`Dispose`, the members throw `ObjectDisposedException`; the `IEngine.Assets` getter
  itself never throws.

### 5.2 `AssetInfo`

```csharp
public readonly record struct AssetInfo(
    string Path,
    bool IsRaw,
    bool IsCompressed,
    long Length,
    long Size,
    string PackPath);
```

| Field | Meaning |
| --- | --- |
| `Path` | Canonical asset path. |
| `IsRaw` | `true`: opaque bytes; `false`: MessagePack. |
| `IsCompressed` | Whether the stored payload is Zstd-compressed. |
| `Length` | Stored byte count (compressed length when compressed). |
| `Size` | Byte count after decompression; equals `Length` when uncompressed. |
| `PackPath` | The pack identifier **as supplied** (the `WithAssetsPack` value or the `LoadPack` argument), not the resolved absolute path. |

`Length`/`Size` are `long` in the public value type even though the v1 header stores `uint32`; this
leaves room for a future pack version and avoids unsigned arithmetic in game code. `Browse` returns
a materialized snapshot; callers own the list but not the underlying pack data.

### 5.3 `AssetException` and `AssetErrorKind`

```csharp
public enum AssetErrorKind
{
    NotFound,
    RawAsset,
    Decode,
    Io,
    InvalidData,
}

public sealed class AssetException : Bork2dException
{
    public AssetException(AssetErrorKind kind, string? assetPath, string message);
    public AssetException(
        AssetErrorKind kind, string? assetPath, string message, Exception innerException);

    public AssetErrorKind Kind { get; }
    public string? AssetPath { get; }
}
```

| Kind | Used for |
| --- | --- |
| `NotFound` | A valid canonical path that is not in the merged VFS. |
| `RawAsset` | `LoadAsset<T>` was asked for a raw entry. |
| `Decode` | MessagePack/Zstd decode failure, a missing generated shape (message names `T`), or a null deserialized asset. |
| `Io` | Reading a mounted pack or writing/reading a tooling file failed. |
| `InvalidData` | A valid pack/payload shape is violated at load time (for example a declared size above `Array.MaxLength`, a descriptor whose payload size is inconsistent, or invalid sprite metadata). |

- Pack **open/parse/mount** failures are `Bork2dException` with the resolved path and reason, not
  `AssetException`.
- Argument/programming errors stay BCL: null/empty/invalid path → `ArgumentNullException` /
  `ArgumentException`; builder conflicts → `Bork2dException`.
- `AssetPath` is the canonical path for load failures, or the filesystem path for IO failures.
- Exceptions are not serialized.

### 5.4 `Bork2dAppBuilder.WithoutAssets()`

```csharp
public Bork2dAppBuilder<TLayer> WithoutAssets();
```

- Skips the base pack. The app starts with an empty VFS; `Browse` returns empty, `Exists` is
  `false`, `LoadData`/`LoadAsset<T>` fault with `NotFound`, and `LoadSprite` returns `Sprite.Empty`.
- `LoadPack` is still allowed and mounts overlays on top of the empty VFS.
- Mutually exclusive with `WithAssetsPack`: if both are used, `Build()` throws
  `Bork2dException`. Calling `WithoutAssets()` twice is idempotent.
- Available in windowed, offscreen, and headless modes.
- Without `WithoutAssets`, the default base pack path is `"assets.pck"` and the file must exist at
  `Run()`.

### 5.5 Public pack-writer tooling

`Bork2d.Assets` exposes exactly two public types:

```csharp
using PolyType;

namespace Bork2d.Assets;

public enum PackCompressionMode
{
    Debug,
    Release,
}

public sealed class PackBuilder
{
    public PackBuilder(PackCompressionMode mode);

    public void AddRaw(string path, ReadOnlySpan<byte> payload);
    public void AddMessagePack<T>(string path, T value) where T : IShapeable<T>;

    public byte[] Build();
    public void Write(string path);
    public void Write(Stream destination);
}
```

| Member | Semantics |
| --- | --- |
| `PackBuilder(PackCompressionMode)` | Selects debug (uncompressed payloads) or release (Zstd only when smaller) payload policy. The header is always Zstd-max in both. |
| `AddRaw(path, payload)` | Adds an opaque payload. The path is normalized/validated with the same canonical rules; the bytes are copied. Duplicate path → `Bork2dException`. |
| `AddMessagePack<T>(path, value)` | Serializes `value` with Nerdbank.MessagePack using the generated shape on `T`; requires `T : IShapeable<T>` (the `[GenerateShape]` marker). Duplicate path → `Bork2dException`. A missing shape is a compile-time error because of the constraint. |
| `Build()` | Returns the complete v1 pack as a `byte[]`. Multiple calls are allowed and do not consume the builder. Limited by `Array.MaxLength`. |
| `Write(string path)` | Writes the pack to a file (overwrites), resolving relative paths against the current directory. Streams; does not require the whole pack in one `byte[]`. |
| `Write(Stream destination)` | Writes the pack to the stream and leaves it open. No path resolution. |
| State | Not thread-safe. `Build()`/`Write()` do not consume the builder; adding entries after a build is allowed and affects the next build. A builder is normally built once. |

- `PackBuilder` is tooling-only: game code must not reference `Bork2d.Assets`.
- `PackBuilder` throws `Bork2dException` for duplicate paths, header/payload size overflow,
  serialization failure, and write failure; `ArgumentException`/`ArgumentNullException` for
  caller arguments.
- `Build`/`Write` sort entries by canonical path and write absolute offsets. They do **not**
  promise byte-identical output (D-M3-08).
- `PackCompressionMode.Debug` is the normal test mode; `Release` uses maximum Zstd only when the
  compressed payload is strictly smaller.

### 5.6 Public surface summary

| Assembly | New public surface |
| --- | --- |
| `Bork2d.Api` | `AssetInfo`, `AssetException`, `AssetErrorKind`, three `IAssets` members, XML docs on `IAssets`/`Sprite`/`Color`/`ClearKind`. |
| `Bork2d.Runtime` | `Bork2dAppBuilder.WithoutAssets()`. |
| `Bork2d.Assets` | `PackBuilder`, `PackCompressionMode`; all other types internal. |

No public type is added to `Bork2d.Common`/`Bork2d.Render`. `Bork2d.Api` remains free of PolyType,
Nerdbank, Zstd, and file I/O. `Bork2d.Assets`' tooling API is the only public surface that mentions
`PolyType.IShapeable<T>`.

## 6. Pack format

### 6.1 Binary layout

All fixed-width fields are little-endian. The header is a Zstd-compressed MessagePack object.

| Offset | Size | Field | Notes |
| --- | ---: | --- | --- |
| `0` | 8 | `magic` | ASCII `BORK2DPK` |
| `8` | 4 | `version` | `uint32`, currently `1`; **outside** the compressed header |
| `12` | 4 | `headerCompressedLength` | `uint32`, stored byte length of the compressed header |
| `16` | 4 | `headerUncompressedLength` | `uint32`, expected byte length after decompression |
| `20` | 8 | `headerChecksum` | `uint64`, XXH3-64 (seed `0`) of the uncompressed header |
| `28` | `headerCompressedLength` | `header` | Zstd-compressed MessagePack `PackHeader` |
| `28 + headerCompressedLength` | … | `data` | Asset payloads, addressed by absolute `Offset` |

- `dataStart = 28 + headerCompressedLength`. Entry offsets are absolute, at least `dataStart`, and
  `Offset + Length` must not exceed the file length.
- The header is always maximum-compression Zstd in debug and release.
- `headerChecksum` is computed over the **uncompressed** header. The loader decompresses, checks the
  exact uncompressed length, verifies the checksum, then deserializes; no entry is trusted before
  that.
- Payloads have no checksum; corruption is caught by codec failure, declared sizes, and bounds.

### 6.2 Header schema

The header is a MessagePack **object** with integer keys. Version 1 uses generated shapes:

```csharp
using Nerdbank.MessagePack;
using PolyType;

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
| `PackHeader.Entries` | `PackEntry[]` | Required; may be empty; must not be null. |
| `PackEntry.Path` | `string` | Canonical asset path. |
| `PackEntry.IsRaw` | `bool` | `true`: opaque bytes; `false`: MessagePack. |
| `PackEntry.IsCompressed` | `bool` | Whether stored bytes are Zstd-compressed. |
| `PackEntry.Offset` | `uint64` | Absolute byte offset from the pack start. |
| `PackEntry.Length` | `uint32` | Stored byte count (compressed length when compressed), max 4 GiB − 1. |
| `PackEntry.Size` | `uint32` | Byte count after decompression; equals `Length` when uncompressed. |

- Entry order is insignificant to the reader; the writer sorts by `Path` for stable structure.
- The loader builds a dictionary keyed by canonical path. Duplicate paths are a pack error.
- No per-asset version field. A schema change is a pack-version change; pre-1.0 breaking changes
  are allowed by D-014.
- Required members (`required`) make a missing `Entries`/`Path` a deserialization error rather than
  a silent default. Generated-shape classes must satisfy the Nerdbank analyzers (no silent
  initializer defaults).

### 6.3 Validation

The loader rejects a pack with `Bork2dException` when:

- the file is shorter than 28 bytes, or `magic` is not ASCII `BORK2DPK`;
- `version` is not `1` (checked before decompression);
- `headerCompressedLength` is zero or exceeds the remaining file length;
- `headerUncompressedLength` is zero or the decompression does not produce exactly that many bytes;
- the decompressed header checksum is not XXH3-64 of the uncompressed bytes;
- the header is not a v1 `PackHeader`, `Entries` is null, or an entry is null;
- a path is empty, non-canonical, duplicated, or escapes the root;
- `Offset < dataStart`, `Offset + Length` overflows or exceeds the file length, or two nonzero
  payload ranges overlap;
- an uncompressed entry has `Length != Size`;
- a compressed entry does not decompress to exactly `Size` bytes, or produces extra bytes beyond
  `Size`.

A malformed pack fails with a diagnostic and never causes an out-of-bounds read. A valid `Size`
above `Array.MaxLength` is not a pack error; it is rejected only by `LoadData`, which must return a
single `byte[]` (D-M3-06).

### 6.4 Compression

- **Header:** always Zstd maximum quality (`ZstandardCompressionOptions.Quality =
  ZstandardCompressionOptions.MaxQuality`, `AppendChecksum = false`).
- **Debug payloads:** stored uncompressed, `IsCompressed = false`, `Length == Size`.
- **Release payloads:** maximum-quality Zstd, stored compressed **only when strictly smaller**
  (`compressedLength < rawLength`); otherwise uncompressed. `Size` is the uncompressed length.
- Decompression enforces the declared `Size`; a decoder that produces fewer or more bytes is a pack
  error.
- No payload checksum. Packs are trusted local content; signing and encryption are out of scope.

### 6.5 Bounds and streaming

There are **no policy caps** on pack size, header size, entry count, or path length. The bounds are:

| Bound | Source |
| --- | --- |
| Header compressed/uncompressed lengths | `uint32` prefix fields; also limited by the file length. |
| Per-entry stored/decompressed size | `uint32` `Length`/`Size`; max 4 GiB − 1. |
| Offset | `uint64`; validated against the actual file length and the `long` file API. |
| Pack file | `FileStream`/`long` length; no additional cap. |
| Path | No length cap; canonical validation and the header size bound it. |
| Entry count | No cap; the header size and memory bound it. |
| `LoadData` | `Array.MaxLength` because the public API returns `byte[]`. |
| `LoadAsset<T>` | Streaming: the decompressed stream is passed to the generated-shape deserializer, so a payload need not fit in one `byte[]`. |
| Header | Stream-decompressed through an exact-length/limiting path for hashing and then parsed; no policy cap. |

A pack is trusted local content. A malicious pack can still declare a very large header or payload
and cause memory pressure; M3 accepts that in exchange for no arbitrary caps. The loader must fail
cleanly (not read out of bounds) and must not allocate more than the declared/required sizes.

### 6.6 Determinism

- Entries are sorted by canonical `Path` ordinal when writing; offsets follow that order.
- The schema, keys, and prefix layout are fixed for v1.
- There is **no byte-identical guarantee**. Zstd output, .NET runtime versions, and future writer
  changes may produce different bytes for the same inputs. Tests assert round-trip, entry order,
  and validation, not hash equality.
- Release builds are not promised to be reproducible.

## 7. Paths, VFS, and overlays

### 7.1 Canonical asset paths

Canonical paths are runtime asset keys:

- lowercase ASCII; `/` separators, never `\`;
- no leading/trailing `/`, no empty segments, no `.`/`..` segments;
- no source extension (`textures/bg`, not `textures/bg.png`);
- each segment uses lowercase letters, digits, `_`, and `-` only.

Public inputs are normalized first (`\` → `/`, invariant lowercase) and then validated. A public
path with a dot, whitespace, empty segment, `.`/`..`, leading/trailing slash, or any character
outside the allowed set throws `ArgumentException`; null throws `ArgumentNullException`.
Normalization makes `Textures\\Player` equal to `textures/player`; it does not accept source
extensions. Pack paths are always canonical and are validated on both write and read.

`textures/raw/` is the conventional location for raw texture payloads. A descriptor's payload entry
must be raw; the loader does not require the literal prefix, but the baker/editor uses it.

### 7.2 Filesystem paths

`WithAssetsPack(path)` and `LoadPack(path)` take filesystem paths, not canonical asset paths:

- relative paths resolve against the process current directory at call time;
- absolute paths are accepted;
- null → `ArgumentNullException`; empty/whitespace → `ArgumentException`;
- the base pack path defaults to `"assets.pck"`.

Consequences:

- Running an app from a different working directory changes where `assets.pck` is found. This is
  deliberate (D-M3-03) and must be documented; scripts should set the working directory or pass an
  absolute path.
- A pack file is opened read-only and kept open for the process lifetime (no unload). Replacing a
  mounted file while the process runs is unsupported.

### 7.3 VFS and overlays

1. At `Run()`, the base pack (`WithAssetsPack` or `assets.pck`) is mounted at the bottom unless
   `WithoutAssets()` was used.
2. `LoadPack(path)` parses and validates a pack, then mounts it **above** the current stack.
3. Lookup walks top-down; a later pack shadows an earlier one by canonical path.
4. `Browse` shows only the winning entry for each path; `Exists` uses the same lookup.
5. There is **no unload** in v1. A mounted pack stays for the process lifetime.

- `LoadPack` is synchronous and thread-safe. It parses outside the swap lock and publishes a new
  immutable overlay stack atomically; a failure leaves the old stack untouched.
- A request already enqueued resolves against the overlay snapshot captured at enqueue time; a
  later mount does not change that request. New lookups see the new pack.
- Mounting the same path twice mounts it twice; the later mount shadows the earlier one. There is no
  deduplication.
- `WithoutAssets()` starts with an empty stack; `LoadPack` may mount overlays later (D-M3-02).
- `LoadPack` may be called during `Setup`/`Tick`, but it does synchronous I/O/validation and should
  be done before the first tick when possible. Calling it from `Tick` is not forbidden; it blocks
  that tick.
- A bad overlay fails with `Bork2dException` (resolved path + reason) and is not mounted.

### 7.4 `Browse`, `Exists`, and `AssetInfo`

- `Browse(null)` returns every visible asset, one per canonical path.
- `Browse(prefix)` normalizes/validates the prefix and returns assets whose path equals the prefix or
  starts with `prefix + "/"` (segment boundary; `sprites/pl` does not match `sprites/player`).
  An empty prefix string is invalid; use `null` for all.
- Results are sorted by `Path` ordinal. `AssetInfo.PackPath` is the supplied pack identifier of the
  winning pack.
- `Exists(path)` is an exact canonical lookup, not a prefix match.
- Both methods throw for invalid path arguments; a valid missing path is `false`/absent.
- `Browse` is a materialized snapshot; it is safe to hold after a later `LoadPack`, but it does not
  update. Do not call it in `Tick` as a per-frame operation.

## 8. Loading

### 8.1 Threads and queue

| Thread | Owned by | Does |
| --- | --- | --- |
| Tick | Host (unchanged) | `Setup`/`Tick`/`Teardown`, requests assets, mounts packs, reads `Browse`/`Exists`. |
| Asset loader | Asset service, one per app | Opens/reads packs, decompresses, deserializes, decodes CPU descriptors/sprites, fills the immutable resource table, completes tasks. |
| Game workers | Game | Unchanged; join before `Tick` returns. |

- The loader thread starts at `Run()` when the asset service starts (including the empty VFS) and is
  joined at shutdown. It never touches SDL, Vulkan, or game entities.
- Requests are FIFO in one unbounded queue. There is no priority, no parallelism, and no
  cancellation token in the public API.
- Requests resolve their pack entry at **enqueue** time (VFS snapshot); later overlays do not change
  an in-flight request.
- Task continuations run asynchronously, never inline on the loader thread, so a slow continuation
  cannot stall the queue.
- No callback is raised on the tick thread. The game decides when to await or poll a `Task`.

### 8.2 `LoadData`

1. Normalize/validate the path synchronously; null/invalid throws from the call.
2. If the service is disposed, throw `ObjectDisposedException` from the call.
3. Look up the winning entry in the current VFS snapshot. Missing → fault the `Task` with
   `AssetException(NotFound)`.
4. Enqueue a read. On the loader thread: read the stored bytes, decompress when needed, enforce the
   exact `Size`, and return a **fresh** `byte[]`.
5. A declared `Size > Array.MaxLength` → fault with `AssetException(InvalidData)`.
6. No cache: each call re-reads/re-decompresses and returns a new array.

### 8.3 `LoadAsset<T>`

1. Same synchronous path/disposed checks as `LoadData`.
2. Look up the entry; missing → fault with `NotFound`; `IsRaw == true` → fault with `RawAsset`.
3. Enqueue a read. On the loader thread: decompress into a bounded stream and deserialize with
   Nerdbank.MessagePack using the generated shape on `T`.
4. Return a **fresh** instance per call; no cache.
5. `T` must have a compiled shape (`[GenerateShape]` / `IShapeable<T>`). A missing shape or
   deserialization failure → `AssetException(Decode)` naming `T`; a null result is also `Decode`
   ("deserialized to null").
6. `LoadAsset<T>` for an internal `TextureDescriptor`/`SpriteAsset` is used by tests only; the types
   are not public. Game types become practical in M4 with the game-side shape package setup.

### 8.4 `LoadSprite`

`LoadSprite` is the one mixed sync/async member:

1. Normalize/validate the path synchronously; invalid → `ArgumentException`; null →
   `ArgumentNullException`; disposed → `ObjectDisposedException`.
2. Look up the cached handle for the path; return the same handle for repeats (idempotent).
3. Look up the entry in the VFS snapshot. Missing → log (Information/Debug), return
   `Sprite.Empty`. The empty result is not cached as a handle.
4. Allocate a new opaque `Sprite` handle, store it in the handle table, and enqueue a
   sprite+descriptor load.
5. On the loader thread: deserialize the sprite asset, validate it, resolve its texture descriptor
   path, deserialize/validate the descriptor, check the payload entry exists and its declared
   `Size` matches `width × height × 4` for `rgba8`, and publish a CPU resource. The raw payload is
   **not** read/decompressed in M3.
6. On failure: log with the path/reason and leave the handle non-renderable. The caller still holds
   the handle; M8's renderer will skip it. No exception is thrown to the caller.
7. There is no public readiness query in M3. Tests use internal resource-table state; M8 resolves
   readiness at draw time.

- `Sprite.Empty` (`Id == 0`) is not a path. Handles are session-local and never persisted.
- Missing/failed sprites do not block, retry, or produce a placeholder.
- Descriptor resources are cached per texture path, so multiple sprites sharing a texture share one
  descriptor resource. Sprite resources are cached per sprite path.

### 8.5 Caching and resource table

- The CPU resource table is immutable once published; the loader publishes a new snapshot/entry and
  the renderer (M8) will read it without per-sprite locks.
- Cached: sprite assets, texture descriptors, `LoadSprite` handles.
- Not cached: `LoadData` byte arrays, `LoadAsset<T>` instances.
- A cached sprite/descriptor remains for the process (no unload). A failed load is not retried
  automatically; a repeated `LoadSprite` returns the same handle and does not re-enqueue a
  completed/failed load.
- `LoadData`/`LoadAsset<T>` do not consult the sprite/descriptor cache; they are independent reads.
- No public API exposes the resource table or readiness in M3.

### 8.6 Failures and retry

| Situation | Behavior |
| --- | --- |
| Missing base pack | `Bork2dException` from `Run()` before window/`Setup`; no `Teardown`. |
| Malformed base pack | Same as missing, with the parse reason. |
| Missing overlay / malformed overlay | `Bork2dException` from `LoadPack`; stack unchanged. |
| Missing asset path | `LoadData`/`LoadAsset<T>` fault `NotFound`; `LoadSprite` returns `Sprite.Empty`; `Exists` false. |
| Raw entry requested via `LoadAsset<T>` | `AssetException(RawAsset)`. |
| Decode/serialization/shape failure | `AssetException(Decode)`; `LoadSprite` logs and stays non-renderable. |
| Declared size above `Array.MaxLength` for `LoadData` | `AssetException(InvalidData)`. |
| Descriptor/sprite validation failure | Log + non-renderable resource; `LoadAsset<T>` faults `InvalidData`; no retry. |
| I/O read failure on a mounted pack | `AssetException(Io)`; `LoadSprite` logs and stays non-renderable. |
| Invalid path argument | `ArgumentException` from the call (synchronous). |
| Disposed service | `ObjectDisposedException` from the call. |

There is **no automatic retry** in v1 (D-M3-18). A failed resource stays failed for the process;
the game can detach/recreate renderables or restart the app. No loose-file fallback.

### 8.7 Shutdown and post-shutdown

- At `Run()` shutdown, after `Teardown`, the asset service:
  1. stops accepting new requests;
  2. cancels queued and in-flight `LoadData`/`LoadAsset<T>` tasks with a cancellation token; the
     returned tasks complete as canceled (`TaskCanceledException`/`OperationCanceledException`);
  3. stops the loader thread at a safe point, closes pack handles, and joins the thread;
  4. marks the service disposed.
- `LoadSprite` requests have no task; their handles remain non-renderable and their loads are
  dropped.
- Shutdown is idempotent and does not throw for secondary failures; it logs them.
- After shutdown, `IAssets` members throw `ObjectDisposedException`; `IEngine.Assets` still returns
  the disposed object and never throws by itself.
- `Bork2dApp.Dispose()` before `Run()` disposes the unstarted asset service; after `Run()` it is a
  no-op for assets. `Dispose()` during `Run()` still throws `InvalidOperationException` (M2).

## 9. Texture descriptors and sprite assets

Both are internal generated-shape MessagePack classes. They use Nerdbank's default property-name
keys (not `[Key]`); only the pack header uses integer keys. `Vector2` uses Nerdbank's generated shape
(map with `X`/`Y`). No per-asset version.

### 9.1 `TextureDescriptor`

| Field | Type | Meaning |
| --- | --- | --- |
| `Payload` | `string` | Raw asset path containing pixels (M3 does not read it). |
| `Width` | `uint` | Pixel width, `> 0`. |
| `Height` | `uint` | Pixel height, `> 0`. |
| `Format` | `string` | `rgba8` only in v1. |
| `Filter` | `string` | `nearest` or `linear`. |
| `Wrap` | `string` | `clamp` or `repeat`. |

`rgba8` payload contract: width × height × 4 bytes, row-major, top-left origin, no padding, straight
alpha, 8 bits per channel, treated as sRGB. No color-space field in M3. KTX2 is rejected as an
unsupported format in v1.

### 9.2 `SpriteAsset`

| Field | Type | Meaning |
| --- | --- | --- |
| `Texture` | `string` | Texture descriptor path. |
| `Uvs` | `Vector2[]` | Exactly four normalized UVs, top-left origin, ordered top-left, top-right, bottom-right, bottom-left. |
| `Scale` | `float` | **Pixels per world unit**, `> 0`. |
| `Pivot` | `Vector2` | Normalized origin inside the quad, default `(0.5, 0.5)`. |
| `Topology` | enum | `Quad` (0), `TriangleStrip` (1), `Triangles` (2). Integer in the pack; string in TOML (M4). |
| `Indices` | `ushort[]?` | `null` for `Quad`; required for the other topologies. |

There is no `vertices` array; the vertex shader generates positions from UVs, texture size, scale,
and pivot (M8).

- `pixelWidth = (maxU − minU) × textureWidth`, `pixelHeight = (maxV − minV) × textureHeight`;
  `worldWidth = pixelWidth / scale`; `worldHeight = pixelHeight / scale`;
  `Transform.LocalScale` multiplies after.
- Pivot uses a top-left origin: `(0,0)` top-left, `(1,1)` bottom-right, `(0.5,0.5)` center. The quad
  is positioned around the pivot and converted to world Y-up; UVs stay top-left origin.

### 9.3 M3 validation

The loader validates `TextureDescriptor`:

- `Payload` is a canonical path and exists in the VFS; its entry is raw;
- `Width > 0`, `Height > 0`;
- `(ulong)Width × Height × 4 == payload.Size` (checked with unsigned arithmetic; mismatch → invalid);
- `Format == "rgba8"`; `Filter` is `nearest`/`linear`; `Wrap` is `clamp`/`repeat`;
- the payload is not decompressed in M3.

The loader validates `SpriteAsset`:

- `Texture` is a canonical non-empty path and a descriptor resource can be resolved;
- `Uvs` has exactly four finite `Vector2`s, each component in `[0,1]`, with `maxU > minU` and
  `maxV > minV`;
- `Scale` is finite and `> 0`;
- `Pivot` is finite with both components in `[0,1]`;
- `Topology` is one of `Quad`/`TriangleStrip`/`Triangles`;
- `Quad` has `Indices == null`;
- `TriangleStrip`/`Triangles` have non-null `Indices`, every value in `0..3`, at least 3 entries,
  and `Triangles` length is a multiple of 3;
- no `vertices` field is accepted or required.

Invalid descriptor/sprite data logs and leaves the sprite non-renderable; `LoadAsset<T>` of the
internal type faults with `AssetException(InvalidData)`. M3 does not draw; v1 draws `Quad` only and
preserves non-Quad assets for the future indexed path (M8).

### 9.4 Versioning

- No per-asset schema version (D-M3-05). A shape change is a pack-version change/migration; pre-1.0
  breaking changes are allowed by D-014.
- The pack prefix `version` is the only format version. Unknown pack versions are rejected before
  decompression.

## 10. Runtime integration

### 10.1 `Build()`

`Build()` remains filesystem-free: it validates configuration, creates logging, the engine/clock/
scene root, and an **unstarted** asset service with the pack path or the disabled flag. It does not
resolve paths, open files, start the loader thread, or parse a pack. Windowed builds still create
unattached window/input proxies; headless/offscreen still have the throwing window stub and no input.

Validation adds: `WithAssetsPack` and `WithoutAssets` are mutually exclusive. The existing
non-empty pack path check remains. A logging factory created before a later failure is disposed.

### 10.2 `Run()` order

```
Run() [all modes, on the caller thread]
  assetService.Start()                 // open/validate base pack unless WithoutAssets;
                                       // start the loader thread; Bork2dException here
  windowed: platform.Start()           // visible window after the pack is valid
  windowed: attach window/input proxies
  Setup(engine)                        // IEngine.Assets is live
  loop / one tick
  Teardown()                           // assets and (windowed) window still alive
  windowed: platform.Shutdown()
  assetService.Stop()                  // cancel tasks, close packs, join loader
  windowed: detach proxies
  propagate failures
```

- The base pack is opened **before** the window is created, so a missing/malformed pack produces no
  visible window and no `Setup`/`Teardown`.
- If the asset service fails after partially opening the pack, it cleans up and joins any loader
  thread before `Run()` throws.
- If the window platform fails after the asset service started, the asset service is stopped and
  `Run()` throws before `Setup`; no `Teardown`.
- `Teardown` can still use `IEngine.Assets`; requests are not guaranteed to complete before the
  service is stopped.
- The asset service is stopped on every path, including `Setup`/`Tick` failures, a fatal platform
  error, and `Exit`.
- Headless/offscreen run the same asset start/stop around their one `Setup`/`Tick`/`Teardown`.

### 10.3 Modes

| Mode | M3 asset behavior |
| --- | --- |
| **Windowed** | Base pack/overlays and loads work; no GPU upload/draw; SDL is independent. |
| **Offscreen** | Same assets; no window, no SDL, no GPU in M3. |
| **Headless** | Same assets; no window, no SDL, no Vulkan; suitable for data tools/servers. |
| **`WithoutAssets()`** | Empty VFS; `LoadPack` may mount overlays; no base pack required. |

No mode initializes SDL/Vulkan because of assets. Headless/offscreen remain one-tick until M7.

### 10.4 Shutdown

- Normal: `Teardown` → platform shutdown (windowed) → asset stop → detach proxies → return.
- `Setup` failure: platform shutdown → asset stop → rethrow; no `Teardown`.
- `Tick`/text failure: `Teardown` → platform shutdown → asset stop → rethrow; aggregate if
  `Teardown` also throws.
- Fatal platform error: loop stops at the next boundary → `Teardown` → platform shutdown → asset
  stop → rethrow.
- Asset start failure: no platform/`Setup`/`Teardown`; cleanup then `Bork2dException`.
- `Dispose()` during `Run()`: `InvalidOperationException` (unchanged); the loop/assets are not
  disturbed.
- `Run()` twice / after `Dispose()`: unchanged (`InvalidOperationException` /
  `ObjectDisposedException`).
- Post-shutdown `IAssets`: `ObjectDisposedException`.

### 10.5 Error matrix (asset additions)

| Failure | Where | Behavior |
| --- | --- | --- |
| Base pack missing/malformed | `Run()` → `assetService.Start()` | `Bork2dException` with resolved path/reason; no window, `Setup`, or `Teardown`; service cleaned up. |
| Overlay missing/malformed | `LoadPack` | `Bork2dException`; stack unchanged; caller decides whether the tick/setup fails. |
| Asset missing | `LoadData`/`LoadAsset<T>` | Faulted `Task` with `AssetException(NotFound)`; `LoadSprite` → `Sprite.Empty` + log. |
| Raw asset via `LoadAsset<T>` | `LoadAsset<T>` | Faulted `Task` with `AssetException(RawAsset)`. |
| Missing generated shape | `LoadAsset<T>` | Faulted `Task` with `AssetException(Decode)` naming `T`. |
| Decode/IO/validation failure | loader thread | Faulted `Task` with `AssetException`; `LoadSprite` logs + non-renderable. |
| Invalid path argument | public member | `ArgumentException`/`ArgumentNullException` synchronously. |
| Service disposed | public member | `ObjectDisposedException` synchronously. |
| Shutdown with pending work | `Run()`/`Dispose()` | Pending tasks canceled; loader joined; handles non-renderable. |

### 10.6 Logging

Use the `Bork2d.Assets` category ([logging](../architecture/logging.md#5-categories)); no per-frame,
per-asset, or per-pixel logging.

| Event | Level | Fields |
| --- | --- | --- |
| Asset service start | Information | mode (`Pack`/`Empty`), configured path, resolved path, pack version, entries, bytes |
| Overlay mounted | Information | supplied path, entries |
| Base/overlay validation failure | Error | path, reason |
| Asset load failure | Error | canonical path, kind, reason |
| `LoadSprite` missing path | Information/Debug | path |
| Asset load success | Debug | path, kind (not per tick) |
| Service stop | Information | pending/canceled counts, mounted packs |

The AOT smoke's default logging output must stay trim/AOT-clean.

### 10.7 Existing-test migration

- Update the shared test helpers so apps that do not exercise assets call `.WithoutAssets()`.
- Replace tests that assert `UnsupportedAssets`/`NotSupportedException` with the new empty-VFS or
  real-pack behavior.
- Windowed loop tests and one-tick tests that build without a pack must opt out; otherwise they
  fail at `Run()` by design.
- New asset tests build packs with `PackBuilder` into temp files (no committed binary fixture) and
  clean them up.
- The opt-in real-window tests keep their scope; add `WithoutAssets()` where they build an app.
- Test counts increase; CI still runs deterministic tests only.

## 11. Dependencies, NativeAOT, and deployment

### 11.1 Projects and public boundary

- `Bork2d.Assets` remains the asset implementation project and becomes **public for tooling**:
  `PackBuilder` and `PackCompressionMode` are public; everything else stays internal. It references
  `Bork2d.Api` and `Bork2d.Common` as before.
- `Bork2d.AotSmoke` adds a project reference to `Bork2d.Assets` to use `PackBuilder` and to load a
  pack in the default run. The dependency matrix and `DependencyMatrixTests` are updated; the old
  "AotSmoke must not pull Assets" rule is replaced by "AotSmoke may use the public pack-tooling
  surface for the AOT proof."
- `Bork2d.Runtime` references `Bork2d.Assets` (already) and exposes only `WithoutAssets()` as new
  public surface. No Assets type leaks through `Bork2d.Runtime`.
- `Bork2d.Api` remains free of PolyType, Nerdbank, Zstd, and file I/O.
- Game projects still reference only `Bork2d.Api`/`Bork2d.Runtime`; `Project.Common` stays
  `Api`-only in M3.
- `Bork2d.Editor`/M4 baker may use `PackBuilder` later; no editor project is added in M3.

### 11.2 Packages, Zstd, and hashing

- `Bork2d.Assets` adds exact pins:
  - `Nerdbank.MessagePack` 1.4.95 (generated shapes);
  - `PolyType` 1.4.1 (public `IShapeable<T>` in the tooling API; also a transitive Nerdbank
    dependency);
  - `System.IO.Hashing` 11.0.0-rc.1.26425.128 (`XxHash3` 64-bit; not in the shared framework).
- Zstd is the .NET 11 built-in `System.IO.Compression` implementation:
  `ZstandardStream`, `ZstandardEncoder`/`ZstandardDecoder`, `ZstandardCompressionOptions`. No
  third-party codec and no native codec package.
- The pack reader/writer keep the codec behind internal helpers; no Zstd type appears in a public
  signature.
- `Bork2d.Assets` keeps `IsAotCompatible`, trim/AOT analyzers, and warnings-as-errors.

### 11.3 Serialization and shapes

- `PackHeader`/`PackEntry` are internal `[GenerateShape]` classes with integer keys.
- `TextureDescriptor`/`SpriteAsset` are internal `[GenerateShape]` classes with property-name keys.
- No reflection resolver, runtime TOML, `Activator.CreateInstance`, assembly scanning, or
  `Type.GetType`.
- `LoadAsset<T>` uses the compiled shape on `T`; a missing shape faults with `AssetException`
  naming `T`. `PackBuilder.AddMessagePack<T>` requires `T : IShapeable<T>` at compile time.
- The public `IAssets.LoadAsset<T>` cannot name PolyType (Api boundary). The implementation is
  expected to use a narrow `NBMsgPack051` suppression around the unconstrained deserialize call,
  with a recorded justification: the contract requires `[GenerateShape]` on `T` and tests/AOT prove
  the shape path. `PackBuilder.AddMessagePack<T>` is constrained and does not need it. Review this
  suppression; do not add a broad `NoWarn`.
- Generated-shape classes must satisfy `NBMsgPack110` (use `required`/`[DefaultValue]` rather than
  silent initializer defaults).

### 11.4 NativeAOT smoke

`Bork2d.AotSmoke` default (no arguments):

1. parses arguments (unchanged);
2. creates a temp pack with `PackBuilder(PackCompressionMode.Debug)`, adding:
   - a raw asset (`data/smoke`) with known bytes;
   - a MessagePack asset (`data/smoke-data`) using `MessagePackSmokeData` (a generated shape);
3. writes it to a temp file (absolute path) and builds a headless app with
   `WithAssetsPack(tempPath)`;
4. in `Setup`/`Tick`, loads the raw data and the MessagePack asset, checks `Browse`/`Exists`, and
   waits for the tasks with a bounded timeout (no sleep loop) before asserting the values;
5. runs the unchanged one `Setup`/`Tick`/`Teardown`, checks root cleanup, and deletes the temp pack
   in `finally`;
6. exits `0` only when the M1 scene assertions, the MessagePack shape round-trip, and the asset
   assertions pass.

`--windowed [--ticks N]` keeps the M2 demo and uses `WithoutAssets()`, so windowed acceptance does
not depend on a pack file. The AOT publish must contain the new managed dependencies and report zero
AOT/trim warnings.

### 11.5 Headless isolation

- Asset loading uses file I/O and the loader thread only; it never initializes SDL or Vulkan.
- Headless/offscreen apps never call SDL; the AOT headless run is the proof.
- The loader thread never touches the window, GPU, or game entities.

## 12. Verification

### 12.1 Deterministic tests (CI)

| Area | Required cases |
| --- | --- |
| Pack round-trip | Empty pack; one raw; one MessagePack; mixed; debug and release; compressed and uncompressed payloads; entry order sorted; offsets/sizes correct. |
| Pack validation | Bad magic; unknown version; truncated prefix/file; zero/overlong header lengths; header decompression failure; exact-length mismatch; checksum mismatch; null/empty header; null entries; non-canonical/duplicate paths; offset before data; offset+length overflow/past EOF; overlapping ranges; uncompressed `Length != Size`; compressed size mismatch; extra decompressed bytes; zero-length entries. |
| Compression | Header always Zstd; debug payload uncompressed; release compresses only when smaller; no payload checksum; exact-size decompression; large payload streaming. |
| VFS | Base pack; one/many overlays; later wins; shadowed entries hidden; `Browse` all/prefix/boundary/empty; `Exists`; `LoadPack` bad overlay leaves stack unchanged; duplicate mount; thread-safe mount/lookup. |
| Paths | Lowercase/backslash normalization; invalid characters/segments/extensions/leading/trailing/double slash; null/empty; `Browse(null)` vs empty; `Exists` invalid; filesystem CWD/absolute resolution. |
| `LoadData` | Raw/MessagePack returned as bytes; compressed decompressed; fresh array per call; missing fault; size above `Array.MaxLength`; invalid path sync. |
| `LoadAsset<T>` | Internal descriptor/sprite shapes round-trip; raw entry fault; missing fault; missing shape fault naming `T`; null result fault; fresh instance per call. |
| `LoadSprite` | Missing → `Empty` + log; present → handle immediately; idempotent handle; async sprite+descriptor load; payload not read; invalid sprite/descriptor → log/non-renderable; shared texture descriptor cached; after shutdown non-renderable. |
| Descriptor/sprite validation | Width/height/scale/pivot/UV rules; topology/indices rules; payload size mismatch; format/filter/wrap; KTX2 rejected; non-Quad valid but not drawn. |
| Loader/threading | One loader thread; FIFO order; tasks complete asynchronously; no tick-thread completion; cancellation at shutdown; loader joined; no deadlock; cache behavior. |
| Lifetime | Base pack missing/malformed before window/`Setup`; no `Teardown`; overlay failure; `WithoutAssets` empty VFS + later `LoadPack`; post-shutdown `ObjectDisposedException`; `IEngine.Assets` getter never throws; `Dispose` before/after `Run`; second `Run`. |
| Modes | Headless/offscreen load packs without SDL/Vulkan; windowed loads without GPU; no SDL init in headless. |
| Logging | Start/overlay/failure/stop events; no per-tick/asset-spam. |
| Dependency/AOT analyzers | No public type leaks; Assets public surface exactly `PackBuilder`/`PackCompressionMode`; AotSmoke references Assets; no reflection/AOT warnings. |

All deterministic tests run in CI. Tests use temp files, no committed binary fixtures, and clean up.
GC/thread tests are deterministic: bounded joins/signals, no sleeps; no entity IDs/order assertions.

### 12.2 AOT and acceptance gates (local)

```powershell
# Build/test/format/docs
dotnet build src/Bork2d/Bork2d.slnx -c Debug
dotnet build src/Bork2d/Bork2d.slnx -c Release
dotnet test src/Bork2d/Bork2d.slnx -c Debug --no-build
dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
pwsh scripts/check-docs.ps1

# AOT publish/run (required local gate, not CI)
dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64 --nologo
& src/Bork2d/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe
& src/Bork2d/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe --windowed --ticks 120
```

The default run must exit `0` with the M1 scene assertions, the MessagePack round-trip, and the new
pack/load/browse assertions. The windowed run keeps the M2 evidence. AOT publish must report zero
AOT/trim warnings; `SDL2.dll` remains in the output for the windowed demo.

### 12.3 Acceptance evidence to record

WP5 records the actual commands/results and the observed pack counts/paths in the M3 PR and this
spec (replacing the placeholders):

| Gate | Evidence |
| --- | --- |
| Debug/Release builds | 0 warnings/errors. |
| Deterministic tests | All pass; count reported. |
| Format/docs | Clean; link check passes. |
| AOT publish | 0 AOT/trim warnings; dependencies present. |
| AOT default run | Exit 0; one tick; raw + MessagePack asset loaded; browse/exists pass. |
| AOT windowed run | Exit 0 after exactly 120 ticks; `WithoutAssets` path. |
| Pack/overlay proof | A base pack + overlay shadows correctly; corrupt/missing packs fail safely. |
| Logging | `Bork2d.Assets` start/stop/error lines; no per-frame logging. |

## 13. Work packages

The WPs are **sequential** (one wave each): they share `Bork2d.Assets`, the public contract, and the
same docs. Branch model and review/merge rules are in [AGENTS.md](../../AGENTS.md).

| WP | Branch | Delivers | Depends |
| --- | --- | --- | --- |
| **WP1** Pack format and writer | `m-3/wp-1` | `System.IO.Hashing`/PolyType pins; `PackHeader`/`PackEntry` shapes; reader/writer; Zstd header/payloads; XXH3-64; validation; public `PackBuilder`/`PackCompressionMode`; round-trip/corruption tests. No runtime wiring. | — |
| **WP2** VFS, paths, browse | `m-3/wp-2` | Canonical paths and filesystem resolution; base/overlay stack; `LoadPack`; `Browse`/`Exists`/`AssetInfo`; thread-safe atomic mount; tests. | WP1 |
| **WP3** Loader and threading | `m-3/wp-3` | `IAssets` implementation; one loader thread; FIFO queue; `LoadData`/`LoadAsset<T>`; caching skeleton; task completion; cancellation/join; `AssetException`; post-shutdown; tests. `LoadSprite` may be stubbed until WP4. | WP2 |
| **WP4** Descriptors and sprites | `m-3/wp-4` | Internal `TextureDescriptor`/`SpriteAsset` shapes; validation; `LoadSprite` pipeline (sprite+descriptor, no payload); resource table; tests. | WP3 |
| **WP5** Runtime, AOT, docs, acceptance | `m-3/wp-5` | `WithoutAssets`; asset service wiring in `Run`/`Dispose`; startup/shutdown/error order; test migration; AOT smoke pack; docs/decision/roadmap updates; full gates and acceptance evidence. | WP4 |

Each WP:

- is created from the latest `origin/m-3/common` and merged back only after review;
- runs the §12.2 gates (plus AOT for WP1's writer and WP5);
- updates the owning docs in the same change;
- uses commits `m3(wp-<K>): <imperative summary>`;
- stops and reports instead of guessing when the spec/docs conflict, a public API change is needed,
  a dependency/tool is missing, an unexpected error occurs, or a test is flaky.

## 14. Acceptance checklist (milestone exit)

- [ ] A v1 `assets.pck` with raw data, a MessagePack asset, a texture descriptor, and a sprite asset
      loads through `LoadData`/`LoadAsset<T>`/`LoadSprite`; `Browse`/`Exists` agree.
- [ ] Overlays shadow correctly; a bad overlay is not mounted; there is no unload.
- [ ] Missing/malformed base pack fails with `Bork2dException` from `Run()` before a window/`Setup`;
      no `Teardown`; no partial state.
- [ ] `WithoutAssets()` starts an empty VFS; `LoadPack` can add overlays; all no-asset apps/tests
      use it.
- [ ] Path normalization/validation and current-directory/absolute filesystem resolution match §7.
- [ ] Debug/release pack policies match §6.4; the header is always Zstd-max; payloads have no
      checksum; no per-asset version; `Length`/`Size` are `uint32`, `Offset` `uint64`.
- [ ] No policy caps; streaming/exact-size validation and `Array.MaxLength` behavior match §6.5.
- [ ] Loader is one thread, FIFO, race-free under stress; tasks complete off the tick thread; shutdown
      cancels/joins; post-shutdown members throw.
- [ ] `LoadSprite` loads sprite + descriptor CPU metadata, not the raw payload; missing/failed sprites
      are non-renderable and do not throw.
- [ ] `AssetException`/`AssetErrorKind`/`AssetInfo` and `IAssets` XML docs are complete; `Bork2d.Api`
      stays free of PolyType/Nerdbank/Zstd/file I/O.
- [ ] `Bork2d.Assets` exposes only `PackBuilder`/`PackCompressionMode` publicly; `AotSmoke`
      references it; dependency test/docs updated.
- [ ] AOT default run builds/loads/browses a pack with zero AOT/trim warnings; windowed
      `--ticks 120` still exits `0`.
- [ ] Headless/offscreen initialize neither SDL nor Vulkan; no per-frame logging; no reflection/TOML
      in the runtime.
- [ ] All deterministic tests pass; Debug/Release zero warnings; format/docs/git diff checks pass.
- [ ] Docs/decisions/roadmap updated in the same milestone; no `AGENTS.md` change.

## 15. Documentation updates (same change)

| Document | Required update |
| --- | --- |
| [`docs/api/reference.md`](../api/reference.md) | `IAssets` real members/status; `AssetInfo`; `AssetException`/`AssetErrorKind`; `WithoutAssets`; asset defaults, modes, post-shutdown, errors; remove `UnsupportedAssets`/`Planned` M3 text. |
| [`docs/api/guide.md`](../api/guide.md) | Loading data/assets/sprites; pack mounting/overlays; `WithoutAssets`; path rules; pack-writer tooling example; error handling; no retry/loose files. |
| [`docs/architecture/assets-and-tooling.md`](../architecture/assets-and-tooling.md) | Status to M3 `Current`; no per-asset version; intrinsic/streaming bounds (no policy caps); `uint32` length/size; no byte determinism; base pack at `Run`, CWD resolution; public tooling; `WithoutAssets`; M3 `LoadSprite` scope. |
| [`docs/architecture/runtime-lifecycle.md`](../architecture/runtime-lifecycle.md) | Asset service start before window/`Setup`; builder `WithoutAssets`; startup/shutdown/error order; modes; post-shutdown. |
| [`docs/architecture/threading.md`](../architecture/threading.md) | Asset loader thread `Current`; FIFO/caching; task/cancellation/shutdown; no renderer in M3. |
| [`docs/architecture/nativeaot.md`](../architecture/nativeaot.md) | Assets public tooling/AotSmoke reference; Zstd/`System.IO.Hashing`/PolyType pins; generated shapes; missing shape; AOT smoke pack. |
| [`docs/architecture/logging.md`](../architecture/logging.md) | `Bork2d.Assets` start/overlay/error/stop events; no per-frame. |
| [`docs/architecture/projects.md`](../architecture/projects.md) | `Bork2d.Assets` public tooling surface; AotSmoke → Assets; package pins; dependency matrix/test. |
| [`docs/architecture/overview.md`](../architecture/overview.md) | Asset pipeline status; no per-asset version/limits wording; public tooling exception. |
| [`docs/decisions.md`](../decisions.md) | D-M3-01…D-M3-20 and the D-001 (Assets public tooling exception), D-010 (uint32 sizes, no per-asset version, no policy caps), and D-019 (M3 descriptor/sprite scope) updates recorded with the approval. |
| [`docs/contributing.md`](../contributing.md) | Asset/pack test commands; AOT smoke with assets; no committed pack fixtures; dependency rules. |
| [`docs/roadmap.md`](../roadmap.md) | M3 status/evidence; §9 M3 rows resolved (Zstd, retry, loose files, limits, KTX2); progress log when merged. |
| [`docs/README.md`](../README.md) | Add this spec to the documentation map; status markers. |

`AGENTS.md` is not modified; milestone content stays out of it.

## 16. Risks and stop-and-ask triggers

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Public `Bork2d.Assets` surface leaks internals or grows accidentally | Public contract bloat | Expose exactly `PackBuilder`/`PackCompressionMode`; add a dependency/export test; docs say tooling-only. |
| `IShapeable<T>` in the public tooling API pulls PolyType into tooling consumers | Tooling dependency surface | Pin PolyType 1.4.1; document; game code does not reference Assets. |
| `NBMsgPack051` suppression for unconstrained `LoadAsset<T>` | Hidden AOT shape failure | Narrow `#pragma`, justification, tests for missing shape, AOT smoke; stop if a broader suppression is needed. |
| Zstd API changes in a .NET 11 RC servicing update | Build/runtime break | Use only `ZstandardStream`/`ZstandardEncoder`/options; pin SDK; adapter internal; stop if the API disappears. |
| `System.IO.Hashing` version mismatch | Restore/build failure | Pin 11.0.0-rc.1.26425.128; validate restore/AOT. |
| Current-directory resolution surprises users | `assets.pck` not found when launched elsewhere | Document; allow absolute paths; log the resolved path; scripts set cwd. |
| Unbounded loader queue/memory | A game can flood requests | Document one-thread FIFO; no public cancellation; trusted game code; monitor in M10. |
| `LoadPack`/large load blocks the tick | Frame hitch | Document "mount/load early"; no per-frame `Browse`; M7 pacing later. |
| No per-asset version | Schema evolution is coarse | Pack version/migration; pre-1.0 changes allowed; document. |
| No byte determinism | CI cannot compare pack bytes | Test structure/round-trip; no hash fixtures. |
| `uint32` per-entry size limits a single asset to 4 GiB; `LoadData` to `Array.MaxLength` | Large assets unsupported | Document; use `LoadAsset<T>` streaming or external files later; no policy cap. |
| AOT temp pack file I/O/permissions | Smoke failure | Use `Path.GetTempPath`, absolute path, cleanup in `finally`; report failures. |
| Existing tests silently relying on `UnsupportedAssets` | Migration masks behavior | Explicit `WithoutAssets()`; replace stub assertions; review test diff. |
| Missing/invalid sprite has no public readiness signal | Game sees an invisible sprite | Document log-only behavior; M8 readiness; no placeholder. |
| Pack file kept open | Cannot replace/delete while running | Document no unload; M10 may revisit. |
| Too many small public exceptions/types | Contract churn | Only `AssetException`/`AssetErrorKind`/`AssetInfo`; keep constructors small. |

**Stop and report** if: the public boundary decision conflicts with a hard requirement; Zstd/
`System.IO.Hashing`/PolyType cannot be restored or AOT-published with zero warnings; the shape
constraint cannot be expressed without leaking PolyType into `Bork2d.Api`; a pack/overlay semantic
is ambiguous beyond §17; a test needs real sleeps/retries; or a dependency/tool is missing.

## 17. Derived details (approved with the spec)

These follow mechanically from the locked decisions rather than from a separate question. They were
approved with the spec on 2026-10-26; if implementation contradicts one, stop and report rather
than silently choosing a different behavior.

1. `PackBuilder` is exactly §5.5; `Build()` returns a `byte[]`, `Write` streams, `Write(string)`
   overwrites, and `Write(Stream)` leaves the stream open.
2. `WithoutAssets()` + `WithAssetsPack` is a `Build()` error; `WithoutAssets()` twice is idempotent;
   `LoadPack` on an empty VFS is allowed.
3. `LoadPack` during `Tick` is allowed but synchronous; docs recommend mounting before the first
   tick.
4. Shutdown cancels queued and in-flight load tasks; canceled tasks surface as
   `TaskCanceledException`; the loader is joined; sprite handles never become ready.
5. `Browse` sorts by `Path` ordinal and matches a non-null prefix on a segment boundary; `null`
   means all; an empty prefix is invalid.
6. Descriptor validation checks payload existence/rawness and `Size == width × height × 4` without
   decompressing; sprite UVs/pivot are in `[0,1]`, scale is positive, and non-Quad indices follow
   §9.3.
7. `AssetInfo.PackPath` is the supplied pack identifier, not the resolved absolute path;
   `Length`/`Size` are `long` in the public type.
8. `AssetErrorKind`: missing shape and null deserialization are `Decode`; descriptor/sprite payload
   inconsistency is `InvalidData`; pack open/mount is not `AssetException`.
9. `LoadData`/`LoadAsset<T>` throw argument/disposed errors synchronously and fault the returned
   `Task` for lookup/decode/IO errors.
10. The AOT default pack contains one raw asset and one generated-shape MessagePack asset;
    `--windowed` uses `WithoutAssets()`.
11. The asset service starts its loader thread even with an empty VFS; one thread per app.
12. Base pack validation happens before the window platform starts; a pack failure produces no
    visible window.
13. `LoadSprite` does not cache a missing path as a handle; repeated misses log and return
    `Sprite.Empty`; a present path's handle is cached.
14. `PackHeader`/`PackEntry` use `required`/`[DefaultValue]` to satisfy `NBMsgPack110`; descriptor/
    sprite assets use property-name keys and Nerdbank's `Vector2` shape.
15. `PackBuilder` stores payloads in memory; `Write(Stream)`/`Write(string)` can stream a pack larger
    than `Array.MaxLength` as long as each payload is a `byte[]`; `Build()` cannot.
16. The base/overlay pack files stay open for the process lifetime; replacing a mounted file is
    unsupported.

## 18. Related documents

- [Roadmap](../roadmap.md#5-milestone-detail) — M3 scope/status and [open decisions](../roadmap.md#9-open-decisions).
- [Assets and tooling](../architecture/assets-and-tooling.md) — pack/VFS/descriptor target contract
  (reconciled by this spec in WP5).
- [Runtime lifecycle](../architecture/runtime-lifecycle.md#2-modes) — startup, modes, shutdown.
- [Threading](../architecture/threading.md#6-asset-and-pack-coordination) — loader ownership.
- [NativeAOT](../architecture/nativeaot.md#4-serialization-and-assets) — shapes, AOT gate.
- [API reference](../api/reference.md#7-iassets) — `IAssets` contract; [API guide](../api/guide.md).
- [Decision log](../decisions.md) — D-001, D-010, D-012, D-019, D-M3 entries.
- [M2 spec](m2-sdl-platform.md) — baseline; [M1 spec](m1-foundation.md) — historical baseline.
- [AGENTS.md](../../AGENTS.md) — workflow and the human decision gate.
