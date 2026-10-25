# Editor

> **Status:** planned, not implemented. `Bork2d.Editor` is not in the solution yet. The editor
> is roadmap **M5**, after the pack (M3) and bake (M4) infrastructure. It is an Avalonia
> application/library, not part of the game runtime.

## 1. Scope

The editor is deliberately small:

- browse a game project's source assets;
- slice textures into quads and define sprite assets;
- edit custom serializable TOML classes with a property grid and a raw TOML fallback;
- validate assets and references;
- invoke the asset baker/pack build.

It does **not**:

- edit scenes, place entities, or run the game;
- run the Vulkan renderer or create a GPU device;
- edit built `.pck` files (source assets are the source of truth);
- implement an animation timeline, tilemap editor, or material editor;
- atlas-pack multiple source textures into one renderer page in v1 (one source texture maps
  to one page).

## 2. Project layout

| Project | Role |
| --- | --- |
| `Bork2d.Editor` | Reusable Avalonia editor library/app: shell, asset browser, slicing, TOML property grid, validation, bake/pack integration. |
| `Project.Editor` | Game-specific thin host. References `Bork2d.Editor` and `Project.Common`; registers custom types/windows and the project path. |

`Project.Editor` does not reference `Bork2d.Render` or `Bork2d.Common`. It may reference
`Project.Common` so game-specific serializable types and project metadata are available at
compile time. The game runtime remains Api/Runtime-only.

## 3. Editor builder

The editor has its own builder, following the same pattern as `Bork2d.Runtime`:

```csharp
using Bork2d.Editor;

using var editor = new Bork2dEditorBuilder()
    .WithProject("../../Project.Common")
    .WithAssetType<EnemyDefinition>()
    .WithAssetType<WeaponDefinition>()
    .WithWindow<MyToolWindow>()
    .Build();

editor.Run();
```

Proposed surface:

```csharp
public sealed class Bork2dEditorBuilder
{
    public Bork2dEditorBuilder WithProject(string projectPath);
    public Bork2dEditorBuilder WithAssetType<T>() where T : class;
    public Bork2dEditorBuilder WithWindow<TWindow>() where TWindow : Window, new();
    public Bork2dEditorBuilder WithWindow(Window window);
    public Bork2dEditorApp Build();
}

public sealed class Bork2dEditorApp : IDisposable
{
    public void Run();
    public void Dispose();
}
```

- `WithProject` points at `Project.Common` or at a project descriptor
  (`bork2d.project.toml`, format `Open`). The editor discovers the asset root from it.
- `WithAssetType<T>()` registers a custom serializable class for the property editor. It is a
  **compile-time registration**; no assembly scanning is performed. This keeps startup
  deterministic and leaves room for AOT-compatible tooling later.
- `WithWindow<TWindow>()` registers an extra Avalonia tool window. The instance overload is
  for windows with constructor dependencies. The editor shell owns the window lifetime.
- Registrations are additive; the default asset browser/slicing/data windows are always
  present.
- `Build()` validates the project path and registrations; `Run()` starts the Avalonia UI on
  the main thread; `Dispose()` closes windows and stops background build tasks.

Exact names/signatures are `Proposed` and will be settled in M5.

## 4. Custom asset types

A developer extends the editor for game-specific data by registering the type:

```csharp
public sealed class WeaponDefinition
{
    public string Name { get; set; } = "";
    public float Damage { get; set; }
    public string Sprite { get; set; } = ""; // asset path
}

var editor = new Bork2dEditorBuilder()
    .WithProject("../MyGame.Common")
    .WithAssetType<WeaponDefinition>()
    .Build();
```

The editor:

- finds TOML files whose schema maps to a registered type (project descriptor mapping;
  `Open`);
- presents a property grid generated from public properties/fields;
- offers a raw TOML editor for the same file;
- validates types, required fields, asset references, and ranges where attributes exist;
- writes canonical TOML back to the source tree.

Because the editor is JIT (not NativeAOT), it may use reflection for the property grid. The
registration itself is explicit, so the set of editable types is deterministic and does not
depend on assembly scanning. A source generator can replace reflection later without changing
the builder API.

## 5. Custom windows

`WithWindow<TWindow>()` lets a game add tooling without forking `Bork2d.Editor`:

```csharp
public sealed class BalanceToolWindow : Window
{
    public BalanceToolWindow(EditorServices services)
    {
        // Avalonia controls, data binding, validation, asset access...
    }
}
```

The exact service surface (`EditorServices`, asset browser selection, project events) is
`Open`; it will be small and documented in M5. Windows may read/write source assets through
the editor's project services, but must not touch the game renderer or runtime entities.

## 6. Source assets and slicing

The editor works on `Project.Common/Assets`:

- texture source (an image or raw RGBA) plus its Unity-style sidecar
  (`textures/player.png.toml`) with filter/wrap metadata;
- sprite TOML (`sprites/player/idle.toml`) with texture path, UVs, pixels-per-unit scale,
  pivot, topology, and optional indices;
- custom data TOML (`data/enemies/boss.toml`).

Slicing workflow:

1. open a texture;
2. draw a rectangle (manual) or use a grid (planned);
3. set the sprite pivot (normalized, default center);
4. set pixels-per-unit scale; the renderer divides by it;
5. set topology (`Quad` in v1; `indices` is written only for non-Quad);
6. set texture filter/wrap in the sidecar;
7. name the sprite asset (`sprites/player/idle`);
8. preview the quad and its pivot;
9. validate bounds/overlap (proposed) and save the TOML.

The editor generates UVs for a `Quad`; it does not write a vertices array:

```toml
texture  = "textures/player"
uvs      = [ [u0,v0], [u1,v0], [u1,v1], [u0,v1] ] # top-left origin
scale    = 16.0                                    # pixels per world unit
pivot    = [0.5, 0.5]                              # normalized, top-left origin
topology = "quad"
# indices = [0, 1, 2, 0, 2, 3]  # non-Quad only; absent/null for Quad
```

The renderer generates the four corners from the UV span, texture size, scale, and pivot.
The TOML schema is described in [Assets and pack files](assets.md#9-sprite-assets). The bake
step (M4) converts it to a MessagePack sprite asset in `assets.pck`.

One source texture maps to one bindless texture page in v1. Multi-texture atlasing is future
work; the asset format can carry UVs for a packed page when that lands.

## 7. Texture descriptors

For each source texture the editor/baker produces:

- a MessagePack texture descriptor at `textures/<name>` (payload path, width, height, format,
  filter, wrap);
- a raw payload at `textures/raw/<name>` (RGBA8 now, KTX2 later).

The editor edits the source sidecar (`<file>.toml`) and the source image; it does not write the
runtime descriptor directly. The baker generates it. See
[Assets: texture descriptors](assets.md#8-texture-descriptors).

## 8. Bake and pack integration

The editor can invoke the same baker/pack path as the build:

- **Validate:** parse TOML, check references, validate sprite geometry and texture metadata.
- **Bake:** TOML → MessagePack assets.
- **Pack:** MessagePack + raw payloads → `assets.pck` (release compression optional).
- **Report:** file/line diagnostics, duplicate paths, missing textures, invalid UVs/geometry.

The exact build command/API is `Open`; it will reuse M3/M4 code, not reimplement the pack
format. The editor should be able to produce a debug pack (compressed header, uncompressed asset
payloads) for quick runs.

## 9. Runtime and AOT boundary

| Aspect | Decision |
| --- | --- |
| UI framework | Avalonia |
| Runtime | JIT; **not** NativeAOT. AOT is required for the game/runtime, not the editor. |
| Renderer | No Vulkan, no swapchain, no engine render thread. |
| Type discovery | Compile-time builder registrations; reflection is acceptable inside the JIT editor for property metadata. |
| Asset source | TOML and source images under `Project.Common`; built packs are not edited. |
| Threading | Avalonia UI thread; background work for bake/pack only; no game tick/render threads. |

This deliberately differs from the runtime: the editor is a desktop tool, and Avalonia/UI
reflection is not part of the shipped game. The builder registrations keep the tool's behavior
predictable and make a future AOT editor possible without changing user code.

## 10. Non-goals

- scene/entity/prefab editing;
- running the game or a play mode;
- a Vulkan preview of the final renderer (Avalonia imaging is enough to show source textures
  and slices);
- animation timelines;
- tilemap or collision editors;
- editing binary packs;
- multi-project workspaces in v1.

## 11. Open questions

| Topic | Question |
| --- | --- |
| Project descriptor | `bork2d.project.toml` schema: asset root, game assembly, type mappings, default pack path. |
| Type-to-TOML mapping | How a registered type chooses its TOML files: descriptor table, folder convention, or attributes. |
| Custom window services | Exact `EditorServices` surface (project, selection, asset access, validation). |
| Window construction | `new()` only, factory, or dependency-injection container? |
| Comments | Must round-tripping preserve TOML comments and formatting? |
| Build invocation | Editor button, CLI, MSBuild target, or all three? |
| Pack inspection | Should the editor ever open a built pack for read-only inspection? |
| Preview | Static sprite preview versus a lightweight non-Vulkan compositor. |
| Grid slicing | Manual only in v1, or grid/auto-slicing too? |
| Validation rules | Bounds/overlap/pivot/indices rules and whether they are warnings or errors. |
| Multiple projects | One project per editor process in v1? |

## 12. Current status

| Area | Current | Target |
| --- | --- | --- |
| `Bork2d.Editor` | Not in the solution | Avalonia editor library/app (M5) |
| `Project.Editor` | Template folder only; no project | Thin host + builder (M5/M9) |
| Project descriptor | None | `Open`, needed by M4/M5 |
| Slicing | None | Quad slicing + sprite TOML (M5) |
| TOML editor | None | Property grid + raw TOML (M5) |
| Bake/pack invocation | None | Reuses M3/M4 (M5) |
| NativeAOT editor | Not planned | Editor stays JIT |

## 13. Related documents

- [Assets and pack files](assets.md) — texture descriptors, sprite assets, pack format.
- [Data and serialization](data-and-serialization.md) — TOML/MessagePack model and bake.
- [Projects and dependencies](projects.md) — `Bork2d.Editor`/`Project.Editor` references.
- [NativeAOT](nativeaot.md) — why the editor is exempt.
- [Roadmap](../roadmap.md) — M5.
