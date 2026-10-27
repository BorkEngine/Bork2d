# Contributing

> **Status:** current through M3 plus the D-035 asset-reference refactor (merged, PR #6) and M4
> serialization/bake (merged into `develop` 2026-10-27, PR #7, including the D-M4-13 revision and
> the D-M4-14/D-M4-15 multi-pack revision). The
> warning/style/format/AOT gates and the docs link check are active. CI runs docs link check, build,
> tests, and format verification on PRs to `develop` and pushes to `develop`; the NativeAOT
> publish/run smoke (headless loading the **baked fixture pack**, and windowed) is a required local
> gate.
>
> **Post-M4 target (D-036/D-037/D-038, Planned):** the docs and gates add `Bork2d.Bake.Builder`, the
> build-time `MyGame.Assets`/`Bork2d.BakeFixture.Assets` executables, explicit
> `RegisterAsset<T>()` registrations, and PPM/Unit processor tests. The generated host is removed.
> The local AOT gate loads packs baked by the fixture assets executable; no builder/processor
> assembly enters the publish. Implementation scope:
> [file-processors spec](specs/file-processors.md).

## 1. Prerequisites

- .NET SDK pinned by `global.json` (`11.0.100-rc.1`, target `net11.0`).
- Windows for windowed/Vulkan work; headless builds/tests should stay cross-platform where
  practical.
- A desktop session for the windowed demo and the opt-in real-window tests.
- SDL2: **no separate install**. `Bork2d.Runtime` pins `Silk.NET.SDL` 2.23.0, whose
  `Ultz.Native.SDL` 2.32.10 dependency supplies `runtimes/win-x64/native/SDL2.dll`. The loaded
  binary reports `SDL_GetVersion` 2.30.8, and `Bork2d.AotSmoke`'s publish output includes
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
pwsh scripts/check-docs.ps1
```

The suite is **923 tests**: 911 deterministic tests run in CI; 12 opt-in/conditional tests are
skipped unless the environment variable or capability is present (9 `[WindowFact]` real-window tests
and 3 reparse-point capability tests). The deterministic set includes the M3 pack-format/writer
tests, the path/VFS/browse/contract tests, the loader/threading and error-contract tests, generic
pack-time reference validation, the public asset-reference/pack-build contracts, the runtime asset
lifecycle/error-matrix tests, the M4 bake pipeline (descriptor, host generation/load, TOML binder,
textures, references, pack composition, determinism), and the M4 incremental/MSBuild tests. The
post-M4 target (D-036/D-037/D-038) adds builder/no-descriptor/no-host tests, explicit
`RegisterAsset<T>()` and default-registration tests, processor registration/override, sidecar
policies, PPM/Unit end-to-end bakes, and the `MyGame.Assets` build auto-bake flow. They
build packs in memory or in temp files, generate their own test images, and use **no committed
binary test/pack fixtures**. The only committed image is the bake fixture's
`Bork2d.BakeFixture/Assets/art/player.png` source asset, which the MSBuild targets copy and bake and
which is not a test pack or generated output. Run only the
asset/pack tests with:

```powershell
dotnet test src/Bork2d/Bork2d.slnx -c Debug --filter "FullyQualifiedName~Pack|FullyQualifiedName~Asset"
```

Run the bake tests (bake pipeline, incremental, explicit registration, CLI, and the
`dotnet build`/publish/clean MSBuild integration) with:

```powershell
dotnet test src/Bork2d/Bork2d.slnx -c Debug --filter "FullyQualifiedName~Bake"
```

The M4 MSBuild integration test writes a temporary app project (two descriptor packs, sources, and a
generated PNG in test code), references `Bork2d.BakeFixture`, imports
[`build/Bork2d.Bake.targets`](../build/Bork2d.Bake.targets), and runs `dotnet build`, `dotnet publish`,
and `dotnet clean` as child processes. **Target (D-037/D-038):** the test uses a temporary
`MyGame.Assets`-style executable with builder configuration, explicit `RegisterAsset<T>()` entries,
and the assets build target; the descriptor, host build, and pack-path properties are removed. It
therefore needs a warmed repository build and adds a few seconds to the Debug pass; it is not marked
slower and runs in the normal suite.

On a desktop session run the whole suite (including the opt-in window tests) with:

```powershell
$env:BORK2D_RUN_WINDOW_TESTS = '1'
dotnet test src/Bork2d/Bork2d.slnx -c Debug
Remove-Item Env:BORK2D_RUN_WINDOW_TESTS
```

NativeAOT publish/run is required for runtime-affecting changes (both runs are local, never CI):

```powershell
dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64
$smoke = 'src/Bork2d/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe'
& $smoke                                  # headless: scene + baked fixture pack load/assertions
& $smoke --windowed --ticks 120           # windowed gate: exit 0 after exactly 120 ticks
```

The publish must report zero AOT/trim warnings and contain `assets.pck`, `dlc1.pck`, and `SDL2.dll`.
Current M4: the AOT smoke imports [`build/Bork2d.Bake.targets`](../build/Bork2d.Bake.targets), so its
build copies the committed `Bork2d.BakeFixture` `Assets/**` and `DLC1/**` trees (including the
fixture source PNG `Assets/art/player.png`), bakes the prepared fixture packs to `$(TargetDir)`, and
copies both next to the published executable. **Target (D-037/D-038):** the smoke takes a build-only
dependency on `Bork2d.BakeFixture.Assets`, which owns the fixture roots, explicit type registrations,
and builder configuration, bakes them, and copies the packs; there is no generated host, and the AOT
publish never contains the builder/processor assemblies or the assets executable. The default headless run
mounts the main pack from `AppContext.BaseDirectory` (not the process CWD), overlays `dlc1.pck` with
`LoadPack`, and exits `0` only when the scene, the baked class root (nested struct/`Color`/texture
reference), the `StatsRoot` through `AssetRef<StatsRoot>`, the sprite referencing `art/player`, the
`art/player` descriptor, the exact `art/player-raw` payload, the DLC `data/dlc` (one entry) and its
DLC → main texture reference, browse/exists (5 main, 6 merged), and exit 0 all pass. The
windowed run uses `WithoutAssets()`, so it needs a desktop session but no pack; it asserts one
`Setup`, exactly `N` ticks, and one `Teardown`. The interactive `--windowed` run is the manual
visibility/input check. Inspect the publish directory: it is a NativeAOT single-file output plus
PDBs, so the managed assemblies are linked in. It must contain `assets.pck`, `dlc1.pck`, `SDL2.dll`,
and the game-side `Bork2d.BakeFixture` PDB, and must not contain a `Bork2d.Bake.dll`,
`Bork2d.Bake.Builder.dll`, `Bork2d.BakeFixture.Assets*`, `Bork2d.Bake.Cli*`, `Bork2d.BakeHost*`
(M4 only), `Tomlyn*`, or `StbImageSharp*` file. `dotnet clean src/Bork2d/Bork2d.slnx` removes the
prepared fixture assets, the incremental state under the assets project's `obj/bork2d/bake`, and
every configured pack.

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
plus its folder path (`Bork2d.Assets/Loading/AssetService.cs` → `Bork2d.Assets.Loading`; IDE0130
is an error, D-034); `_camelCase` private fields; `PascalCase` constants; `I`-prefixed interfaces;
`Async` suffix and no `async void` outside handlers; guard clauses; no regions except generated
code; no commented-out code; XML docs on public API.

M3 closed the former XML-doc gap: `IAssets`, `AssetInfo`, `AssetException`, `AssetErrorKind`,
`AssetRef<T>`, `DataRef`, `SpriteAsset`, `TextureDescriptor`, `SpriteTopology`, `Color`,
`ClearKind`, and `Bork2dAppBuilder.WithoutAssets()` are documented. The rule applies to every new
public API; do not remove it.

## 5. Architecture boundaries

- Runtime game code references `Bork2d.Api`/`Bork2d.Runtime` only; implementation projects are
  internal. The public tooling exceptions are the pack writer in `Bork2d.Assets` and (target
  D-037/D-038) `Bork2d.Bake.Builder` for the build-time `MyGame.Assets` executable. Data types are
  registered explicitly with `RegisterAsset<T>()`; there is no generated host or reflection. The
  builder is never referenced by the runtime app or AOT publish.
- `Bork2d.AotSmoke` takes a build-only dependency on the fixture assets executable (current M4:
  imports the bake targets and references `Bork2d.BakeFixture`); it loads the baked pack through the
  public `IAssets` API and does not reference the pack tooling, builder, processor assemblies, or
  `Bork2d.Assets` directly (Assets remains in the publish through `Bork2d.Runtime`). It updates the
  dependency test/docs when the graph changes.
- Editor/tooling stays JIT; no Avalonia, TOML, or reflection in runtime projects.
- Runtime stays NativeAOT/trim-safe: no runtime reflection, assembly scanning, runtime TOML,
  dynamic code, or unregistered Nerdbank shapes.
- `Bork2d.Api` references the BCL plus `Microsoft.Extensions.Logging.Abstractions` and PolyType
  `1.4.1` for the `IShapeable<T>` constraint only. It must not reference Nerdbank.MessagePack,
  Zstd/`System.IO.Compression`, `System.IO.Hashing`, or file I/O; no Vulkan/SDL types either.
- Pack format changes are versioned and documented in
  [Assets and tooling](architecture/assets-and-tooling.md).

## 6. Tests and CI

- Add unit tests with behavior changes; pack/format tests for asset changes; threading tests for
  handoff changes; GPU/visual tests for renderer changes. Asset/pack tests build packs in memory or
  in temp files (`PackBuilder`/`TempPackDirectory`) and never commit binary **test/pack** fixtures;
  the bake tests generate their own source trees and textures in temp directories, and the default
  AOT smoke loads the fixture-baked packs. The one committed image is the bake fixture's
  `Assets/art/player.png` source asset (an authoring input, not a test pack or generated output).
  Processor tests must use the public builder/processor path (the PPM and Unit test processors),
  cover registration/override and sidecar policies, and assert exact baked payloads.
  Asset-loader tests are deterministic: they use a narrow
  internal hook plus bounded signals/joins (no sleeps, retries, or timing assertions) to prove FIFO
  order, asynchronous completion, and shutdown cancellation.
- Real-window tests use `[WindowFact]`, are skipped without `BORK2D_RUN_WINDOW_TESTS=1`, share the
  serialized `RealSdlWindow` collection, and may use only signals/bounded waits (no sleeps). Run
  them locally before a window/runtime PR.
- The dependency test and docs link check run in CI. The AOT publish smoke (headless and windowed)
  is a required local gate, not a CI job.
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
- [ ] Dependency matrix respected; no unintended public types.
- [ ] Files sit in the area folder and the namespace matches it (IDE0130).
- [ ] No runtime reflection/TOML/dynamic code.
- [ ] NativeAOT smoke publishes/runs when runtime-affecting, including
      `--windowed --ticks 120` for window/runtime changes.
- [ ] Pack format/version updated if assets changed.
- [ ] Owning docs/API/decision log updated; status markers honest.
- [ ] No unjustified suppressions.

## 9. Related documents

- [Projects and dependencies](architecture/projects.md) — boundaries.
- [NativeAOT](architecture/nativeaot.md) — AOT constraints.
- [Logging](architecture/logging.md) — ZLogger/MEL usage.
- [Assets and tooling](architecture/assets-and-tooling.md) — formats and bake.
- [Roadmap](roadmap.md) — milestone sequence.
