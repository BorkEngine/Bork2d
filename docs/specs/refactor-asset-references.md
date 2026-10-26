# Refactor — Generic Asset References (record)

> **Status:** approved by the human owner (2026-10-26), implemented, and merged into `develop`
> (PR #6, 2026-10-26). This is the historical record for the refactor; the accepted decision is
> [D-035](../decisions.md). The living contracts are
> [Assets and tooling](../architecture/assets-and-tooling.md#6-public-asset-api),
> the [API reference](../api/reference.md#7-iassets), and the
> [API guide](../api/guide.md#10-assets).

## 1. Goal

Remove the special sprite load path and replace the sprite/texture CPU resource types with generic,
typed asset links:

- delete `IAssets.LoadSprite`, `Sprite`, `SpriteResource`, `TextureResource`, the handle table, and
  the sprite CPU resource pipeline;
- make `SpriteAsset`, `TextureDescriptor`, and `SpriteTopology` public assets loaded through
  `LoadAsset<SpriteAsset>`;
- reference MessagePack assets with `AssetRef<TAsset>` and raw data with `DataRef`;
- make `ISpriteRender<TLayer>.Sprite` a `SpriteAsset?`;
- validate references at pack creation, not at asset load;
- keep the loader a single-threaded, FIFO, deserialize-only pack reader.

## 2. Public asset model

```csharp
public readonly record struct AssetRef<TAsset>(string? Path)
    where TAsset : class, IShapeable<TAsset>;

public readonly record struct DataRef(string? Path);

[GenerateShape] public sealed partial class SpriteAsset { ... }
[GenerateShape] public sealed partial class TextureDescriptor { ... }
public enum SpriteTopology { Quad, TriangleStrip, Triangles }
```

- `AssetRef<TAsset>.Path` and `DataRef.Path` are canonical path strings when present; `null` means
  absent and is valid.
- An empty, whitespace, or non-canonical path is invalid and fails pack creation.
- Both reference types serialize as the path string (a `null` string for an absent reference), so
  M3 sprite/descriptor payload shapes remain readable.
- `AssetRef<TAsset>` constrains `TAsset` to a compiled `IShapeable<TAsset>` shape.
- `SpriteAsset.Texture` is an `AssetRef<TextureDescriptor>`; `TextureDescriptor.Payload` is a
  `DataRef`.
- `ISpriteRender<TLayer>.Sprite` is `SpriteAsset?`; `null` draws nothing.

The reference types are implemented in `Bork2d.Api.Assets` with public PolyType marshalers
(`AssetRefMarshaler<TAsset>`, `DataRefMarshaler`); `Bork2d.Api` grants `Bork2d.Assets` internal access
to the marker used for pack-time traversal.

## 3. Runtime contract

`LoadAsset<T>`:

1. normalize/validate the public path;
2. check disposed state;
3. resolve the winning VFS entry at enqueue time;
4. fault `NotFound`/`RawAsset`/`Decode` as before;
5. deserialize with the generated shape and return a fresh instance.

`assetRef`/`DataRef` overloads unwrap a present path and otherwise behave like the string overload.
An absent reference is an `ArgumentException`, not a load request.

The loader does **not** validate fields, follow references, load descriptors, or cache resolved
assets. A sprite whose texture link is absent or missing still loads; loading the missing texture
later faults `NotFound`. `LoadData` is unchanged.

## 4. Pack-time validation

`PackBuilder.AddMessagePack<T>` walks the asset object graph with its generated PolyType shape and
collects every `AssetRef<T>`/`DataRef` in properties, arrays/lists, dictionary values, nullable
wrappers, and nested objects. `Build`/`Write` validate each non-null reference:

- the path is canonical;
- an entry with that path exists;
- `DataRef` targets a raw entry;
- `AssetRef<T>` targets a MessagePack entry.

A violation throws `Bork2dException` with the source asset path, property path, and reference path.
A failed `Write` does not create or truncate the output. `null` references are skipped.

## 5. Limitations

- The pack header has no CLR type metadata, so a wrong-typed target cannot be rejected by the
  builder.
- `object` and polymorphic members are not traversed in v1.
- Field/schema validation (UVs, scale, pivot, topology, dimensions) belongs to the M4 baker/editor.
- `IAssets.LoadAsset<T>` cannot verify that the referenced CLR type matches the stored asset type.

## 6. Evidence at merge

- Debug and Release builds: 0 warnings, 0 errors.
- `dotnet test`: 519 passed, 9 skipped (real-window), 0 failed. Coverage includes the
  `AssetRef`/`DataRef` contract, pack-time validation (nested objects, collections, dictionaries,
  null/empty/non-canonical/missing/wrong-kind), the deserialize-only sprite path, runtime
  lifecycle/AOT integration, and public-surface checks.
- `dotnet format --verify-no-changes`, `scripts/check-docs.ps1`, and `git diff --check`: clean.
- AOT publish: 0 AOT/trim warnings. The default run exited 0 with a 5-entry temporary pack and
  verified `LoadAsset<SpriteAsset>` plus its `Texture` reference; `--windowed --ticks 120` exited 0.

The current tree has the same suite (519 passed + 9 skipped) and AOT smoke shape.

## 7. Related documents

- [Decision D-035](../decisions.md) — accepted decision and superseded entries.
- [Assets and tooling](../architecture/assets-and-tooling.md#6-public-asset-api) — current asset
  contract.
- [API reference](../api/reference.md#7-iassets) — `IAssets` members and errors.
- [API guide](../api/guide.md#10-assets) — usage examples.
- [M3 record](m3-assets.md) — the milestone this refactor superseded.
