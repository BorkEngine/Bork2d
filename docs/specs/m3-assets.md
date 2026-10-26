# M3 — Assets: Pack, VFS, Descriptors (record)

> **Status:** implemented (WP1–WP5) and merged into `develop` on 2026-10-26 (PR #4). This is a
> historical record, not a live contract. The post-M3 asset-reference refactor ([D-035](../decisions.md))
> superseded the sprite-specific parts described below. The living contracts are
> [Assets and tooling](../architecture/assets-and-tooling.md), the [API reference](../api/reference.md#7-iassets),
> [Threading](../architecture/threading.md#6-asset-and-pack-coordination), and the
> [decision log](../decisions.md#m3-asset-decisions-approved-2026-10-26).

## 1. Goal

Replace the M1/M2 `UnsupportedAssets` stub with a real asset pipeline:

- a v1 `assets.pck` reader/writer with the magic/prefix, Zstd header, XXH3-64 checksum, and
  integer-keyed MessagePack header;
- a virtual filesystem mounted from the base pack plus later-wins overlays, with no unload;
- canonical asset paths, `Exists`, `Browse`, and `AssetInfo`;
- one loader thread, a FIFO request queue, and asynchronous `LoadData`/`LoadAsset<T>`;
- texture descriptors and sprite assets with CPU-side validation and no GPU work;
- a public pack-writer tooling API in `Bork2d.Assets`, plus `WithoutAssets()`;
- a base pack required at `Run()` unless the app opts out, and a NativeAOT smoke that builds and
  loads a pack without reflection or a GPU.

M3 stopped before TOML baking (M4), the editor (M5), Vulkan (M6), frame handoff (M7), and sprite
drawing (M8).

## 2. Outcome

A runtime that opens `assets.pck`, mounts overlays, browses canonical paths, loads raw data and
MessagePack assets, and loads sprite/texture assets — headless, with no SDL, Vulkan, TOML,
reflection resolver, or GPU.

## 3. Scope

**In scope:** pack format/writer; public tooling `PackBuilder`/`PackCompressionMode`; base/overlay
VFS; canonical and filesystem paths; one loader thread with `LoadData`/`LoadAsset<T>`; internal
texture-descriptor and sprite models with validation; `IAssets` additions; base pack lifecycle in
`Run()`; deterministic tests and AOT proof.

**Out of scope:** TOML parsing and the JIT baker (M4); the Avalonia editor and slicing (M5); KTX2,
compressed GPU formats, color-space fields, mipmaps, GPU samplers, texture upload, bindless pages,
sprite drawing, and camera projection (M6/M8); PNG/JPEG decoding, atlases, meshes; loose-file
overlays, hot reload, retry, pack unload/signing/encryption; audio, animation, tilemaps, materials,
scene serialization; a pack CLI.

The detailed approved decisions are D-M3-01…D-M3-22 in the [decision log](../decisions.md); the pack
format, path rules, VFS semantics, and loader lifecycle that survived M3 are owned by
[Assets and tooling](../architecture/assets-and-tooling.md) and [Threading](../architecture/threading.md).
Do not re-derive them from this record.

## 4. Delivered

| WP | Delivered |
| --- | --- |
| WP1 | v1 pack reader/writer: magic/prefix, Zstd header, XXH3-64, integer-keyed header shapes, validation, public pack builder, round-trip/corruption tests. |
| WP2 | Canonical paths and filesystem resolution; base/overlay VFS; `LoadPack`; `Browse`/`Exists`/`AssetInfo`; atomic thread-safe mount. |
| WP3 | `IAssets` implementation, one loader thread, FIFO queue, `LoadData`/`LoadAsset<T>`, `AssetException`, cancellation/join, post-shutdown behavior. |
| WP4 | Internal `TextureDescriptor`/`SpriteAsset` shapes, validation, sprite + descriptor CPU pipeline, resource table. |
| WP5 | `WithoutAssets`, runtime integration, test migration, AOT smoke pack, docs, gates, acceptance evidence. |

## 5. Superseded by D-035

The refactor removed the sprite-specific runtime path. The following M3 statements are **history
only** and must not be treated as the current contract:

- `IAssets.LoadSprite`, the `Sprite` handle, `SpriteResource`, `TextureResource`, the handle table,
  and the sprite CPU resource pipeline are gone.
- `SpriteAsset`, `TextureDescriptor`, and `SpriteTopology` are now public `Bork2d.Api.Assets` assets
  loaded with `LoadAsset<T>`; they are no longer internal.
- Asset links are `AssetRef<TAsset>`/`DataRef` typed canonical paths, not raw string fields.
- `ISpriteRender.Sprite` is a `SpriteAsset?`; there is no handle or readiness state.
- Field/schema validation (UVs, scale, pivot, topology, dimensions) moved to the M4 baker/editor;
  `PackBuilder` validates reference existence and kind at pack build.
- The loader is deserialize-only: it does not validate fields, follow references, cache resolved
  assets, or publish a resource table.

## 6. Evidence

Recorded at the M3 merge (2026-10-26, Windows 11, SDK `11.0.100-rc.1.26425.128`):

| Gate | Result |
| --- | --- |
| Debug/Release build | 0 warnings, 0 errors. |
| Deterministic tests | 587 total: 578 passed, 9 skipped (`[WindowFact]`). |
| Format, docs, `git diff --check` | Clean. |
| AOT publish/run | 0 AOT/trim warnings; default run exit 0 with a **2-entry** temporary pack (raw + generated-shape MessagePack) and one tick; `--windowed --ticks 120` exit 0 with `WithoutAssets()`. |
| Failure matrix | Missing/malformed base packs throw before the window/`Setup`; a bad overlay is not mounted; corrupt headers/sizes/checksums fail with diagnostics. |

**Current tree (after D-035):** the suite is 528 tests (519 passed + 9 skipped), and the default AOT
smoke pack has **5 entries** (raw data, generated-shape data, texture descriptor, raw payload, public
sprite). The current asset contract is documented in
[Assets and tooling](../architecture/assets-and-tooling.md) and the
[refactor record](refactor-asset-references.md); this table is kept only as the M3 historical
evidence.

## 7. Related documents

- [Roadmap](../roadmap.md#5-milestone-detail) — milestone status.
- [Refactor record](refactor-asset-references.md) — the current public asset model.
- [Assets and tooling](../architecture/assets-and-tooling.md) — pack format, VFS, descriptors,
  sprites, and the loader.
- [Threading](../architecture/threading.md#6-asset-and-pack-coordination) — loader ownership.
- [NativeAOT](../architecture/nativeaot.md#4-serialization-and-assets) — shapes and the AOT gate.
- [API reference](../api/reference.md#7-iassets) — `IAssets`.
- [Decision log](../decisions.md#m3-asset-decisions-approved-2026-10-26) — D-M3 decisions.
- [M2 record](m2-sdl-platform.md) — baseline.
