# Projects and Dependencies

> **Status:** the project graph is `Current` and enforced by the dependency test. M2 (WP1–WP5) is
> merged into `develop` (2026-10-26, PR #2): `Bork2d.Runtime` now hosts the SDL window, input, time,
> and the windowed loop. M3 is in progress: `Bork2d.Assets` implements the v1 pack reader/writer,
> its public tooling surface (`PackBuilder`/`PackCompressionMode`) in WP1, and canonical paths plus
> the internal base/overlay VFS with `LoadPack`/`Browse`/`Exists` in WP2; `Bork2d.Api` now exposes
> `AssetInfo` and the PolyType-constrained `LoadAsset<T>`. The loader, descriptors, and runtime
> wiring remain `Planned` (WP3–WP5). `Bork2d.Render` is still reference-wired with no
> implementation (`Planned`); `Bork2d.Editor` is `Planned` (M5). No project was added in M2, WP1, or
> WP2.

## 1. Current projects

All projects target `net11.0` and live in `src/Bork2d/`, listed in
[`Bork2d.slnx`](../../src/Bork2d/Bork2d.slnx).

| Project | References | Status | Responsibility |
| --- | --- | --- | --- |
| `Bork2d.Api` | none (no project references) | `Partial` | Public contracts and value types, including `AssetInfo` and the PolyType-constrained `LoadAsset<T>` (M3 WP2). |
| `Bork2d.Common` | `Bork2d.Api` | `Partial` | Root-owned unordered scene tree, transforms, time, layer validation, MessagePack generated-shape policy. Snapshot capture is M7. |
| `Bork2d.Runtime` | `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render` | `Current` (M2) | Public builder/app, validation, windowed SDL host (window, input, time, uncapped loop) and one-tick headless/offscreen host, app-owned logging. GPU/rendering is M6/M8. |
| `Bork2d.Assets` | `Bork2d.Api`, `Bork2d.Common` | `Partial` (M3 WP1–WP2) | v1 pack reader/writer, structural validation, Zstd/XXH3-64, canonical paths, public `PackBuilder`/`PackCompressionMode`, and the internal base/overlay VFS with `LoadPack`/`Browse`/`Exists`. Loaders, descriptors, sprites, and runtime wiring are WP3–WP5. |
| `Bork2d.Render` | `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets` | `Empty` (wired) | Vulkan device, swapchain or offscreen targets, pipelines, bindless textures, draws, present. |
| `Bork2d.AotSmoke` | `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Runtime` | `Current` | NativeAOT smoke executable: the default headless one-tick run plus the opt-in `--windowed [--ticks N]` SDL demo; the local publish/run gate. Not a game template project. |
| `Bork2d.Tests` | all five engine projects plus `Bork2d.AotSmoke` | `Current` | 436 xUnit tests (427 deterministic in CI, 9 opt-in real-window tests), including pack-format/writer, VFS/path/browse, asset contract, and dependency-surface tests. The smoke reference copies the app next to the tests; the dependency test loads it by name to check its references. |
| `Bork2d.Editor` | `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets` (+ Avalonia) | `Planned` (M5) | Avalonia editor library/app; JIT. |

`Bork2d.Runtime` references `Microsoft.Extensions.Logging` `11.0.0-rc.1.26425.128` and `ZLogger`
`2.5.10`; it also pins `Silk.NET.SDL` `2.23.0` for the internal windowed platform. That package
carries `Ultz.Native.SDL` `2.32.10`; the win-x64 `SDL2.dll` it ships reports `SDL_GetVersion`
**2.30.8**, which is the runtime version M2 is verified against (the package label is not the
runtime version). No SDL type leaves `Bork2d.Runtime`, `Bork2d.Api` stays SDL-free, and the
project-reference matrix above is unchanged. `Bork2d.Common` references `Nerdbank.MessagePack`
`1.4.95` (generated shapes only, no reflection resolver). `Bork2d.Assets` pins
`Nerdbank.MessagePack` `1.4.95`, `PolyType` `1.4.1` (the public
`PackBuilder.AddMessagePack<T>` shape constraint), and `System.IO.Hashing`
`11.0.0-rc.1.26425.128` (`XxHash3`); Zstd is the .NET 11 built-in
`System.IO.Compression` implementation, so there is no third-party codec package.
`Bork2d.Api` uses `Microsoft.Extensions.Logging.Abstractions` from the .NET 11 shared framework
and pins `PolyType` `1.4.1` for the `IAssets.LoadAsset<T>` `where T : class, IShapeable<T>`
constraint (D-M3-22). That contract reference is the only package in `Bork2d.Api`; it stays free
of Nerdbank.MessagePack, `System.IO.Hashing`, Zstd, and file I/O.
`Bork2d.AotSmoke` is the only executable and the only project that sets `PublishAot`;
libraries do not publish independently.

## 2. Target dependency graph

The graph is acyclic and flows contracts → implementation → composition. `Runtime` is the
composition root and the only project that knows all the others.

| From \ To | Api | Common | Assets | Render | Runtime | Editor |
| --- | :-: | :-: | :-: | :-: | :-: | :-: |
| **Api** | — | ✗ | ✗ | ✗ | ✗ | ✗ |
| **Common** | ✓ | — | ✗ | ✗ | ✗ | ✗ |
| **Assets** | ✓ | ✓ | — | ✗ | ✗ | ✗ |
| **Render** | ✓ | ✓ | ✓ | — | ✗ | ✗ |
| **Runtime** | ✓ | ✓ | ✓ | ✓ | — | ✗ |
| **AotSmoke** | ✓ | ✓ | ✗ | ✗ | ✓ | ✗ |
| **Editor** | ✓ | ✓ | ✓ | ✗ | ✗ | — |

Rules behind the matrix:

- `Api` references the BCL plus `Microsoft.Extensions.Logging.Abstractions` for `ILoggerFactory`
  and `PolyType` `1.4.1` for the `IShapeable<T>` constraint only; no Silk.NET, SDL, Vulkan,
  ZLogger, Avalonia, Nerdbank.MessagePack, `System.IO.Hashing`, Zstd, or file I/O.
- `Common` is platform-agnostic: no SDL, Vulkan, or asset implementation; it consumes `IAssets`
  through the API interface.
- `Assets` implements `IAssets` and owns pack loading/serialization. It may use `Common` internals;
  it must not reference `Render`.
- `Render` consumes internal snapshot and resource-table contracts. It must not reference
  `Runtime` or call SDL; window/surface lifetime is handed to it, offscreen targets are its own.
- `Runtime` is the composition root: builder, app host, SDL window/input (windowed), Vulkan
  bootstrap, mode selection, ZLogger configuration, service wiring.
- `AotSmoke` uses `Runtime` for the host plus `Api`/`Common` for the scene graph; it must not pull
  `Assets`/`Render` types in.
- `Editor` is independent of the renderer: it edits source data, slices textures, and previews
  with Avalonia imaging. It is JIT and not shipped with the game.
- Tests may reference everything and may use internals.

A new arrow requires updating this document and the decision log; the dependency test fails
otherwise.

## 3. Public surface and assembly visibility

### Public assemblies

| Assembly | Public types |
| --- | --- |
| `Bork2d.Api` | `IEngine<TLayer>`, `IGame<TLayer>`, `ITime`, `IWindow`, `IInput`, `IAssets`, `ISceneRoot`, `ITransform`, `ICamera<TLayer>`, `ISpriteRender<TLayer>`, `Sprite`, `Color`, `ClearKind`, `KeyCode`, `MouseButton`, `AssetInfo`, `Bork2dException`. `IAssets.LoadAsset<T>` carries the `where T : class, IShapeable<T>` constraint (M3 WP2). |
| `Bork2d.Runtime` | `Bork2dAppBuilder<TLayer>`, `Bork2dApp`. Nothing else; options, runtime state, and stubs stay internal. |
| `Bork2d.Assets` | **Tooling only:** `PackBuilder`, `PackCompressionMode`. Every implementation type (pack reader/archive, header schema, paths, Zstd helpers) stays internal. Game code must not reference this assembly; it builds packs in tooling/tests and the runtime consumes them. |
| `Bork2d.Editor` | `Bork2dEditorBuilder`, `Bork2dEditorApp`, and the public editor service types needed by `Project.Editor` (`Planned`, M5). |

`ILoggerFactory` is a Microsoft.Extensions.Logging type exposed by `IEngine`; it is not a Bork2d
public type. `Bork2d.Api` takes it from `Microsoft.Extensions.Logging.Abstractions` in the
.NET 11 shared framework.

`Bork2d.Assets`' tooling types are the one exception to the internal-project rule (D-001,
D-M3-04): they exist for pack creation, not for gameplay. The dependency test asserts that the
assembly exports exactly those two types. `Bork2d.AotSmoke` still references only
`Api`/`Common`/`Runtime` in this WP; WP5 adds the `Assets` tooling reference for the AOT pack
proof (D-M3-04).

### Internal assemblies

`Bork2d.Common`, `Bork2d.Assets`, and `Bork2d.Render` are implementation details. Types are
`internal` by default and exposed only to the assemblies that need them. The only exception is the
tooling-only public surface listed above:

| Project | Internals visible to |
| --- | --- |
| `Bork2d.Common` | `Bork2d.Runtime`, `Bork2d.Render`, `Bork2d.Assets`, `Bork2d.Editor`, `Bork2d.Tests`, `Bork2d.AotSmoke` |
| `Bork2d.Assets` | `Bork2d.Runtime`, `Bork2d.Render`, `Bork2d.Editor`, `Bork2d.Tests` |
| `Bork2d.Render` | `Bork2d.Runtime`, `Bork2d.Tests` |
| `Bork2d.Runtime` | `Bork2d.Tests` |

Consequences:

- There is **no accidental public API** from implementation projects. A public type in `Common`
  is a bug unless it is a contract intentionally re-exported by `Runtime`.
- `Runtime` may reference internals directly; it does not create a parallel public model.
- Trimming/AOT compatibility is **not** deferred: `IsAotCompatible` and the trim/AOT analyzers
  are on from M1 (see [NativeAOT](nativeaot.md)).

## 4. Game solution layout

A game generated for Bork2d uses three projects. The names below are the template's generic
names; a real game uses its own prefix (`MyGame.Common`, `MyGame.Desktop`, …).

```
MyGame/
  MyGame.sln
  src/
    Project.Common/
      Project.Common.csproj      -> Bork2d.Api
      Game/                      game code: IGame implementation, systems, data types
      Assets/                    textures, TOML data, sprite metadata
    Project.Desktop/
      Project.Desktop.csproj     -> Bork2d.Runtime, Project.Common
      Program.cs                 builder startup
    Project.Editor/
      Project.Editor.csproj      -> Bork2d.Editor, Project.Common
      Program.cs                 editor host (thin)
  bork2d.project.toml            project descriptor for editor/tooling (Planned)
```

| Project | May reference | Must not reference |
| --- | --- | --- |
| `Project.Common` | `Bork2d.Api` | `Bork2d.Runtime`, `Bork2d.Common`, `Bork2d.Render`, `Bork2d.Assets` |
| `Project.Desktop` | `Bork2d.Runtime`, `Project.Common` | `Bork2d.Render`, `Bork2d.Common`, `Bork2d.Assets` |
| `Project.Editor` | `Bork2d.Editor`, `Project.Common` | `Bork2d.Render`, `Bork2d.Common`, `Bork2d.Assets` |

A developer therefore writes against exactly two engine assemblies: `Bork2d.Api` in
`Project.Common`, and `Bork2d.Runtime` in `Project.Desktop`. `Bork2d.Editor` is distributed by the
engine; the game's `Project.Editor` is a thin host that adds game-specific types and windows
through the editor builder. The generated solution references `Project.Common` directly and
registers editable types at compile time; assembly scanning is not used. The project descriptor
format is a M4 decision (see [roadmap](../roadmap.md#9-open-decisions)).

## 5. Build, packaging, and versioning

- `Directory.Build.props` sets `net11.0`, nullable, implicit usings, warnings-as-errors,
  `EnforceCodeStyleInBuild`, deterministic builds, and .NET analyzers for every project.
  `.editorconfig` and `.gitattributes` apply as well.
- The SDK is `11.0.100-rc.1`, pinned by `global.json` with `rollForward: latestPatch` and
  `allowPrerelease: true`.
- `Api`, `Common`, `Assets`, `Render`, and `Runtime` set `IsAotCompatible`,
  `EnableTrimAnalyzer`, and `EnableAotAnalyzer`; they do not set `PublishAot`.
  `Bork2d.AotSmoke` sets `PublishAot` and `InvariantGlobalization`.
- Build: `dotnet build src/Bork2d/Bork2d.slnx`. Local AOT gate:
  `dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64`.
  AOT publish/run is a local gate, not a CI job. See [NativeAOT](nativeaot.md#7-testing-and-ci).
- CI restores, verifies formatting, builds Debug, and runs Debug tests on pull requests to
  `develop` and on pushes to `develop`.
- Distribution starts as source/project references; NuGet packaging is M9 (see roadmap). The game
  template's `Project.Desktop`/`Project.Common` take over the smoke app's role in M9.
- Versioning: pre-1.0 assemblies may break. A public API change updates the API guide, reference,
  and decision log in the same change.

## 6. Related documents

- [Architecture overview](overview.md) — invariants and context.
- [Runtime lifecycle](runtime-lifecycle.md) — modes, builder ownership, startup.
- [Assets and tooling](assets-and-tooling.md) — pack VFS, editor, and `Project.Editor`.
- [NativeAOT](nativeaot.md) — runtime build constraints.
- [Roadmap](../roadmap.md) — milestone sequence and progress.
