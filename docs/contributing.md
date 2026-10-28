# Contributing

> **Status:** current through M4, the merged D-036/D-037/D-038 asset-processor task
> (2026-10-27, PR #8), and the Pong example (D-043/D-044; implemented on `task/pong-example`,
> merged pending). The warning/style/format/AOT gates and the docs link check are
> active. CI runs the docs link check, build, tests, and format verification on PRs to `develop` and
> pushes to `develop`; the NativeAOT publish/run gate (`Pong.Desktop --headless` loading the baked
> Pong pack, and `--windowed --ticks 120`) is a required local gate.
>
> The docs and gates cover `Bork2d.Bake`, the build-time `MyGame.Assets`/`Pong.Assets` executables,
> explicit `RegisterAsset<T>()` registrations, Pong's `CircleProcessor`, and the schema-3 incremental
> bake. There is no project descriptor, generated host, or `Bork2d.Bake.Cli`. The local AOT gate
> loads the pack baked by the Pong assets executable; no builder/processor assembly enters the
> publish. Implementation scope: [file-processors spec](specs/file-processors.md). The
> project-consolidation refactor (D-041/D-042, [spec](specs/refactor-projects.md)) is merged
> (2026-10-28, PR #9); it moved the engine contract/implementation into `Bork2d.Engine`, merged the
> bake tooling into `Bork2d.Bake`, and replaced the exploratory smoke/fixture projects with
> `examples/pong`; CI does not build the Pong solution.

## 1. Prerequisites

- .NET SDK pinned by `global.json` (`11.0.100-rc.1`, target `net11.0`).
- Windows for windowed/Vulkan work; headless builds/tests should stay cross-platform where
  practical.
- A desktop session for the windowed demo and the opt-in real-window tests.
- SDL2: **no separate install**. `Bork2d.Runtime` pins `Silk.NET.SDL` 2.23.0, whose
  `Ultz.Native.SDL` 2.32.10 dependency supplies `runtimes/win-x64/native/SDL2.dll`. The loaded
  binary reports `SDL_GetVersion` 2.30.8, and `Pong.Desktop`'s publish output includes
  `SDL2.dll` next to the executable. Deployment is therefore a normal `dotnet publish -r win-x64`;
  a clean machine needs the Windows desktop session and whatever runtime the SDL2 build requires
  (full clean-machine validation remains M10).
- Vulkan 1.3 GPU/driver for windowed/offscreen checks from M6 (not needed before M6).
- NativeAOT toolchain (MSVC) for the local publish gate.

## 2. Build, test, and local gates

```powershell
dotnet build src/Bork2d/Bork2d.slnx -c Debug
dotnet build src/Bork2d/Bork2d.slnx -c Release
dotnet test src/Bork2d/Bork2d.slnx -c Debug
dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
dotnet build examples/pong/Pong.slnx -c Debug
dotnet build examples/pong/Pong.slnx -c Release
dotnet test examples/pong/Pong.slnx -c Debug
dotnet format examples/pong/Pong.slnx --verify-no-changes --no-restore
pwsh scripts/check-docs.ps1
```

The Pong solution is not a CI job; build, test, and format it locally when the example changes.
A `Pong.slnx` Release build still builds the external engine project references in Debug (the
`.slnx` has no configuration mapping outside `examples/pong`); the direct AOT publish is the Release
proof.

The engine suite is **919 tests**: 907 deterministic tests run in CI; 12 opt-in/conditional tests are
skipped unless the environment variable or capability is present (9 `[WindowFact]` real-window tests
and 3 reparse-point capability tests). The deterministic set includes the pack-format/writer tests,
the path/VFS/browse/contract tests, the loader/threading and error-contract tests, generic pack-time
reference validation, the public asset-reference/pack-build contracts, the runtime asset
lifecycle/error-matrix tests, the builder/no-descriptor/no-host tests, explicit/default
`RegisterAsset<T>()` tests, processor registration/override and sidecar policies, the shape-driven
TOML binder, the processor/bake suites, and schema-3 incremental tests. They build packs in memory or
in temp files, generate their own test images, and use **no committed binary test/pack fixtures**.
`Pong.Tests` adds 20 game tests (simulation/AI/title, bake/load integration, and the headless game).
The committed game sources (`examples/pong/Pong.Assets/Assets/art/paddle.png`,
`Assets/textures/ball.circle`, and the TOMLs) are **source assets** that the Pong executable bakes;
they are not test packs or generated outputs. Run only the asset/pack tests with:

```powershell
dotnet test src/Bork2d/Bork2d.slnx -c Debug --filter "FullyQualifiedName~Pack|FullyQualifiedName~Asset"
```

Run the bake tests (bake pipeline, incremental, explicit registration, CLI, and the processor
contract) with:

```powershell
dotnet test src/Bork2d/Bork2d.slnx -c Debug --filter "FullyQualifiedName~Bake"
```

On a desktop session run the whole engine suite (including the opt-in window tests) with:

```powershell
$env:BORK2D_RUN_WINDOW_TESTS = '1'
dotnet test src/Bork2d/Bork2d.slnx -c Debug
Remove-Item Env:BORK2D_RUN_WINDOW_TESTS
```

NativeAOT publish/run is required for runtime-affecting changes (both runs are local, never CI):

```powershell
dotnet publish examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Release -r win-x64
$pong = 'examples/pong/Pong.Desktop/bin/Release/net11.0/win-x64/publish/Pong.Desktop.exe'
& $pong --headless                        # headless: baked 7-entry pack load/assertions
& $pong --windowed --ticks 120            # windowed gate: exit 0 after exactly 120 ticks
```

The publish must report zero AOT/trim warnings and contain `assets.pck` and `SDL2.dll`.
`Pong.Desktop` imports `build/Bork2d.Assets.Copy.targets` and sets `Bork2dAssetsProjectFile`, so its
build restores and builds the build-only `Pong.Assets` executable out of process; that executable
owns the `Assets/` root, the `GameSettings` registration, `CircleProcessor`, and the builder
configuration, bakes the pack, and the copy target places it next to the built and published
executable. The restore step makes the direct publish work from a clean clone; the app's own publish
never references the assets project. There is no
generated host, and the AOT publish never contains the builder/processor assemblies or the assets
executable. The headless run mounts the pack from `AppContext.BaseDirectory` (not the process CWD)
and exits `0` only when the scene, the `GameSettings` struct root, the `sprites/paddle` and
`sprites/ball` sprites, the `art/paddle` descriptor and exact 6144-byte raw payload, the
`CircleProcessor` `textures/ball` descriptor and exact 16384-byte raw payload, the red/blue tints, the
7-entry browse, and the detached world all pass. The `--windowed --ticks 120` run loads the same pack
and asserts one `Setup`, exactly `N` ticks, and one `Teardown`. The interactive `--windowed` run is
the manual visibility/input check (the window is black until the M8 renderer; the title carries the
score/state). Inspect the publish directory: it is a NativeAOT single-file output plus PDBs, so the
managed assemblies are linked in. It must contain `Pong.Desktop.exe`, `assets.pck`, `SDL2.dll`, and
the engine/runtime/`Pong.Common` PDBs (`Pong.Common.pdb` is the on-disk proof that the game assembly
is in the graph), and must not contain a `Pong.Assets`, `Bork2d.Bake`, `Tomlyn`, or `StbImageSharp`
assembly.
`dotnet clean src/Bork2d/Bork2d.slnx` and `dotnet clean examples/pong/Pong.slnx` remove the packs and
the incremental state under the assets project's `obj/bork2d/bake`.

## 3. Build, style, and warnings

- `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, Microsoft/.NET-standard
  `.editorconfig`, enforced IDE rules as errors.
- AOT/trim analyzers as errors on runtime projects (`IsAotCompatible`, `EnableAotAnalyzer`,
  `EnableTrimAnalyzer`).
- `dotnet format --verify-no-changes` is a CI gate.
- Shared settings live in `Directory.Build.props` and the root `.editorconfig`; do not override
  them per project without a decision-log entry.
- Suppressions must be the narrowest `#pragma`/`[SuppressMessage]`, justified, and scoped. `NoWarn`
  is a review flag with an owner/expiry.

## 4. C# conventions

File-scoped namespaces; nullable enabled; explicit accessibility; `var` only when the type is
obvious; braces for control flow; one type per file; a file's namespace is the project namespace
plus its folder path (`Bork2d.Engine/Assets/Loading/AssetService.cs` → `Bork2d.Engine.Assets.Loading`; IDE0130
is an error, D-034); `_camelCase` private fields; `PascalCase` constants; `I`-prefixed interfaces;
`Async` suffix and no `async void` outside handlers; guard clauses; no regions except generated
code; no commented-out code; XML docs on public API.

M3 closed the former XML-doc gap: `IAssets`, `AssetInfo`, `AssetException`, `AssetErrorKind`,
`AssetRef<T>`, `DataRef`, `SpriteAsset`, `TextureDescriptor`, `SpriteTopology`, `TextureFormat`,
`TextureFilter`, `TextureWrap`, `Color`, `ClearKind`, and `Bork2dAppBuilder.WithoutAssets()` are
documented. The rule applies to every new public API; do not remove it.

## 5. Architecture boundaries

- Runtime game code references `Bork2d.Engine`/`Bork2d.Runtime` only; implementation types are
  internal. The public tooling exceptions are the pack writer in `Bork2d.Engine.Assets.Pack` and the
  builder/processor surface in `Bork2d.Bake` for the build-time `MyGame.Assets` executable. Data
  types are registered explicitly with `RegisterAsset<T>()`; there is no generated host or
  reflection. The builder is never referenced by the runtime app or AOT publish.
- The example `Pong.Desktop` takes a build-only dependency on `Pong.Assets` (through
  `Bork2dAssetsProjectFile`, not a `ProjectReference`); it loads the baked pack through the public
  `IAssets` API and does not reference the pack tooling, builder, processor assemblies, or the assets
  executable. `Pong.Tests` references `Pong.Common`/`Pong.Assets`; `Bork2d.Tests` references no
  example project. Update the dependency test/docs when the graph changes.
- Editor/tooling stays JIT; no Avalonia, TOML, or reflection in runtime projects.
- Runtime stays NativeAOT/trim-safe: no runtime reflection, assembly scanning, runtime TOML,
  dynamic code, or unregistered Nerdbank shapes.
- `Bork2d.Engine` references the BCL plus `Microsoft.Extensions.Logging.Abstractions`, PolyType
  `1.4.1` (the `IShapeable<T>` contract and public shapes), `Nerdbank.MessagePack` `1.4.95`
  (generated shapes only), and `System.IO.Hashing` `11.0.0-rc.1.26425.128` (pack checksums). The
  public contract must not expose MessagePack or hashing types; no Vulkan/SDL types either.
- Pack format changes are versioned and documented in
  [Assets and tooling](architecture/assets-and-tooling.md).

## 6. Tests and CI

- Add unit tests with behavior changes; pack/format tests for asset changes; threading tests for
  handoff changes; GPU/visual tests for renderer changes. Asset/pack tests build packs in memory or
  in temp files (`PackBuilder`/`TempPackDirectory`) and never commit binary **test/pack** fixtures;
  the bake tests generate their own source trees and textures in temp directories, and the Pong AOT
  gate loads the game-baked pack. The committed game sources are
  `examples/pong/Pong.Assets/Assets/art/paddle.png`, `Assets/textures/ball.circle`, and the TOMLs
  (plus sidecars) — authoring inputs, not test packs or generated outputs. Processor tests must use
  the public builder/processor path (Pong's `CircleProcessor` and the self-contained processor
  contract tests), cover registration/override and sidecar policies, and assert exact baked payloads.
  Asset-loader tests are deterministic: they use a narrow
  internal hook plus bounded signals/joins (no sleeps, retries, or timing assertions) to prove FIFO
  order, asynchronous completion, and shutdown cancellation.
- Real-window tests use `[WindowFact]`, are skipped without `BORK2D_RUN_WINDOW_TESTS=1`, share the
  serialized `RealSdlWindow` collection, and may use only signals/bounded waits (no sleeps). Run
  them locally before a window/runtime PR.
- The dependency test and docs link check run in CI. The AOT publish gate (`Pong.Desktop` headless
  and windowed) is a required local gate, not a CI job.
- GPU/validation checks need a Vulkan 1.3 runner from M8; headless/data tests must run without a
  GPU.
- Do not merge with disabled tests, flaky retries without a tracking entry, or a lowered warning
  level.

## 7. Documentation

- Update the owning document in the same change as a contract change; see the ownership table in
  [docs/README.md](README.md#ownership).
- Keep status markers accurate: `Current` only for implemented behavior; use `Partial`/`Planned`/
  `Open` honestly.
- Add a decision entry for public-contract or format changes.
- Update the roadmap progress log when a milestone lands.
- Run `pwsh scripts/check-docs.ps1` after moving headings or files.

## 8. Pull-request checklist

- [ ] `dotnet build` Debug and Release: zero warnings.
- [ ] `dotnet test` passes; the opt-in real-window tests pass locally with
      `BORK2D_RUN_WINDOW_TESTS=1` for window/runtime changes.
- [ ] `dotnet format --verify-no-changes` and `scripts/check-docs.ps1` pass.
- [ ] The Pong solution builds, tests, and formats when the example changed.
- [ ] Dependency matrix respected; no unintended public types.
- [ ] Files sit in the area folder and the namespace matches it (IDE0130).
- [ ] No runtime reflection/TOML/dynamic code.
- [ ] `Pong.Desktop` publishes/runs when runtime-affecting, including `--windowed --ticks 120` for
      window/runtime changes.
- [ ] Pack format/version updated if assets changed.
- [ ] Owning docs/API/decision log updated; status markers honest.
- [ ] No unjustified suppressions.

## 9. Related documents

- [Projects and dependencies](architecture/projects.md) — boundaries.
- [NativeAOT](architecture/nativeaot.md) — AOT constraints.
- [Logging](architecture/logging.md) — ZLogger/MEL usage.
- [Assets and tooling](architecture/assets-and-tooling.md) — formats and bake.
- [Roadmap](roadmap.md) — milestone sequence.
