# Refactor — Generic asset references (no special sprite path)

> **Status:** approved by the human owner (2026-10-26), implemented in this tree, and open for review
> as PR #6. The original M3 spec remains the historical record for the pack/VFS/loader milestone;
> this spec records the post-M3 public asset-model refactor and supersedes the M3 statements it
> names.

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

## 2. Locked decisions

| ID | Decision |
| --- | --- |
| D-035 | See the [decision log](../decisions.md#d-035-generic-asset-references-replace-the-special-sprite-path). |
| D-M3-12 | Superseded: no sprite/descriptor resource cache or idempotent handle. |
| D-M3-13 | Superseded: no `LoadSprite`, handle, or readiness state. |
| D-M3-17 | Superseded: public sprite/texture asset model. |
| D-M3-14 | Unchanged: `IEngine.Assets` never throws; members throw after shutdown. |
| D-M3-15 | Amended: pack open/parse/mount failures remain `Bork2dException`; load failures remain `AssetException`; reference/field validation is a pack-build concern. |

## 3. Public asset model

```csharp
public readonly record struct AssetRef<TAsset>(string? Path)
    where TAsset : class, IShapeable<TAsset>;

public readonly record struct DataRef(string? Path);

[GenerateShape] public sealed partial class SpriteAsset { ... }
[GenerateShape] public sealed partial class TextureDescriptor { ... }
public enum SpriteTopology { Quad, TriangleStrip, Triangles }
```

- `AssetRef<TAsset>.Path` / `DataRef.Path` are canonical path strings when present.
- `null` means the reference is absent and is valid.
- An empty, whitespace, or non-canonical path is invalid and fails pack creation.
- Both reference types serialize as the path string (a `null` string for an absent reference), so
  M3 sprite/descriptor payload shapes remain readable.
- `AssetRef<TAsset>` constrains `TAsset` to a compiled `IShapeable<TAsset>` shape.
- `SpriteAsset.Texture` is `AssetRef<TextureDescriptor>`; `TextureDescriptor.Payload` is `DataRef`.
- `IAssets` gains `LoadAsset<T>(AssetRef<T>)` and `LoadData(DataRef)`. Loading an absent reference
  is an `ArgumentException`; it is not a load request.
- `ISpriteRender<TLayer>.Sprite` is `SpriteAsset?`; `null` draws nothing.

## 4. Runtime contract

`LoadAsset<T>`:

1. normalize/validate the public path;
2. check disposed state;
3. resolve the winning VFS entry at enqueue time;
4. fault `NotFound` / `RawAsset` / `Decode` as before;
5. deserialize with the generated shape and return a fresh instance.

The loader **does not** validate fields, follow references, load descriptors, or cache resolved
assets. A sprite whose texture link is absent or missing still loads; loading the missing texture
later faults `NotFound`. `LoadData` is unchanged.

## 5. Pack-time validation

`PackBuilder.AddMessagePack<T>` walks the asset object graph with its generated PolyType shape and
collects every `AssetRef<T>`/`DataRef` in properties, arrays/lists, dictionary values, nullable
wrappers, and nested objects. `Build`/`Write` validate each non-null reference:

- the path is canonical;
- an entry with that path exists;
- `DataRef` targets a raw entry;
- `AssetRef<T>` targets a MessagePack entry.

A violation throws `Bork2dException` with the source asset path, property path, and reference path.
A failed `Write` does not create/truncate the output. `null` references are skipped.

Limitations documented in the decision log: the pack header has no CLR type metadata, so a
wrong-typed target cannot be rejected by the builder; `object`/polymorphic members are not traversed
in v1. Field/schema validation (UVs, scale, pivot, topology, dimensions) belongs to the M4
baker/editor.

## 6. Work packages

1. **Public contract** — add `AssetRef`/`DataRef`; move/publicize `SpriteAsset`,
   `TextureDescriptor`, `SpriteTopology`; remove `Sprite`/`LoadSprite`; update `ISpriteRender` and
   `SpriteRender`.
2. **Loader** — remove the sprite pipeline/resource/handle types, hooks, counters, and validation
   type switches; add reference overloads; keep the lifecycle and error contract.
3. **Pack builder** — add the shape-based reference collector and `Build`/`Write` validation; add
   tests for missing, empty, non-canonical, null, wrong-kind, nested, collection, dictionary, and
   write-failure cases.
4. **Tests and AOT** — rewrite sprite tests around `LoadAsset<SpriteAsset>`; extend the AOT smoke
   pack with a public sprite, descriptor, and payload; update scene/runtime/API-contract tests.
5. **Docs and gates** — update the API guide/reference and architecture docs, add D-035, run the
   full local gates.

## 7. Acceptance

- No `LoadSprite`, `Sprite`, `SpriteResource`, or `TextureResource` in current code and current-API
  docs. The historical M3 spec and superseded decision rows retain the old names only as history.
- `Bork2d.Api` exports the public asset model; `Bork2d.Assets` still exports only the pack tooling.
- `LoadAsset<SpriteAsset>` returns the packed asset without semantic validation.
- `PackBuilder` rejects missing/wrong-kind/empty/non-canonical references and allows `null`.
- Debug/Release builds, tests, format, docs check, and local AOT publish/run pass with zero warnings.

## 8. Acceptance evidence (2026-10-26)

- `dotnet build src/Bork2d/Bork2d.slnx -c Debug`: succeeded, 0 warnings, 0 errors.
- `dotnet build src/Bork2d/Bork2d.slnx -c Release`: succeeded, 0 warnings, 0 errors.
- `dotnet test src/Bork2d/Bork2d.slnx -c Debug --no-build`: 519 passed, 9 skipped (real-window),
  0 failed. Coverage includes the new `AssetRef`/`DataRef` contract, pack-time reference validation
  (nested objects, collections, dictionaries, null/empty/non-canonical/missing/wrong-kind), the
  deserialize-only sprite load path, runtime lifecycle/AOT integration, and the public-surface
  checks.
- `dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore` and
  `pwsh scripts/check-docs.ps1`: clean; `git diff --check`: clean.
- `dotnet publish .../Bork2d.AotSmoke.csproj -c Release -r win-x64`: succeeded with zero AOT/trim
  warnings. The default run exited 0 with a 5-entry temporary pack (raw data, generated-shape data,
  public texture descriptor, raw texture payload, public sprite asset) and verified
  `LoadAsset<SpriteAsset>` plus its `Texture` reference. `--windowed --ticks 120` exited 0.
