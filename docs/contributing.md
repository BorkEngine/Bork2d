# Contributing

> **Status:** current through M4, the merged D-036/D-037/D-038 asset-processor task
> (2026-10-27, PR #8), the Pong example (D-043/D-044; merged 2026-10-29, PR #10), and M6 (Vulkan
> bring-up; merged 2026-10-31, PR #11). M6 delivered the Render
> foundation, the windowed renderer and opt-in windowed GPU test, the offscreen renderer, Runtime
> host wiring, and opt-in offscreen GPU tests; WP-5 proves the three-mode AOT gate. **M7 (frame
> handoff and pacing) is merged into `develop` (2026-10-31, PR #12, merge `5de5ac3`).** M7 delivered
> the snapshot ring/capture, renderer consumption, host
> backpressure, the paced headless loop, and the Pong `--headless` 120-tick gate
> (`--headless --ticks N` overrides). The
> warning/style/format/AOT gates and the docs link check are active. **M8 (sprite renderer) is in
> progress** (WP-1 docs reconciliation, WP-2 shader pipeline/camera math/instance planning, and WP-3
> texture pages/`RenderStats` are complete): the remaining work adds the draw path, sprite GPU tests,
> and the 50,000-sprite benchmark
> (§2.1/§6). CI runs the docs link
> check, build, tests, and format verification on PRs to `develop` and pushes to `develop`; the
> NativeAOT publish/run gate (`Pong.Desktop` headless, windowed, and offscreen) is a required
> local gate.
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
- Vulkan 1.3 GPU/driver for windowed/offscreen runs and the GPU tests; the Khronos validation
  layer (Vulkan SDK) for validation evidence (its absence skips the strict GPU assertions with a
  warning, D-M6-20). Headless builds/tests need neither. **The Vulkan SDK is not required to
  build or run the shader pipeline:** the checked-in GLSL is compiled at build time by the
  build-only `Bork2d.ShaderCompiler` tool (Silk.NET.Shaderc 2.23.0); `glslangValidator`, `glslc`,
  and `spirv-val` are optional local tools and are not invoked by the build.
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

The engine suite is **1158 tests**: 1141 deterministic tests run in CI; 17 opt-in/conditional tests
are skipped unless the environment variable or capability is present (9 `[WindowFact]` real-window
tests, 3 reparse-point capability tests, and 5 `[GpuFact]` real-GPU tests). The deterministic set
includes the pack-format/writer tests, the path/VFS/browse/contract tests, the loader/threading and
error-contract tests, generic pack-time reference validation, the public asset-reference/pack-build
contracts, the runtime asset lifecycle/error-matrix tests, the builder/no-descriptor/no-host tests,
explicit/default `RegisterAsset<T>()` tests, processor registration/override and sidecar policies,
the shape-driven TOML binder, the processor/bake suites, schema-3 incremental tests, the M6
renderer seam/policy tests, the SDL Vulkan seam, the SPSC control-channel, windowed start-info/
lifecycle tests, the renderer host lifecycle matrix (Setup-before-readiness, first-tick gate,
offscreen `Exit`/`ITime`, headless no renderer), the platform Vulkan start/posts/surface-replacement
tests, the offscreen factory validation tests, and the M7 snapshot ring/stress/capture/allocation,
host handoff, minimized tick-cap, and headless loop/pacing tests. They build packs in memory or in
temp files,
generate their own test images, and use **no committed binary test/pack fixtures**.
`Pong.Tests` adds 46 game tests (simulation/AI/title, bake/load integration, the headless game with
an explicit tick limit, and the linked `PongArgumentsTests`).
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

On a Vulkan 1.3 machine run the opt-in real-GPU tests with:

```powershell
$env:BORK2D_RUN_GPU_TESTS = '1'
dotnet test src/Bork2d/Bork2d.slnx -c Debug --filter "Category=Gpu"
Remove-Item Env:BORK2D_RUN_GPU_TESTS
```

The five `[GpuFact]` tests are skipped by default and are not a CI job (D-M6-05). One creates a
validation-enabled instance and selects an offscreen device; the windowed renderer test creates a
real SDL window, instance, and surface on the test thread, consumes one snapshot per presented
frame (30+), exercises resize/fullscreen/minimize, stops under frame load with a bounded join, and
asserts zero validation errors/warnings. The offscreen renderer test creates a 320×240 sRGB target
without a surface or swapchain, consumes/discards 30+ frames, and stops cleanly; the windowed app
test drives the real `Bork2dApp` host per-tick publication through the renderer; the offscreen app
test drives `WithOffscreen(320, 240)` + `WithValidation(true)` until `Exit` through the internal
factory hook and asserts `Setup`/`Tick`/`Teardown`, advanced `ITime`, discarded frames, a joined
renderer, and zero validation messages. They skip at runtime when the loader, desktop session, a
suitable device, or the validation layer is unavailable. Run them locally before a renderer PR.

NativeAOT publish/run is required for runtime-affecting changes (the runs are local, never CI):

```powershell
dotnet publish examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Release -r win-x64
$pong = 'examples/pong/Pong.Desktop/bin/Release/net11.0/win-x64/publish/Pong.Desktop.exe'
& $pong --headless                        # headless gate: 120 ticks by default, baked 7-entry pack
& $pong --headless --ticks 7              # headless override: exactly N completed ticks
& $pong --windowed --ticks 120            # windowed gate: exit 0 after exactly 120 ticks
& $pong --offscreen --ticks 120           # offscreen gate: exit 0 after exactly 120 ticks
```

The publish must report zero AOT/trim warnings and contain `assets.pck` and `SDL2.dll` (and no
`vulkan-1.dll`, which is the system loader). M6 wires the windowed/offscreen renderer into the
Runtime host, so the AOT publish graph compiles the Vulkan bindings; the published `--headless` run
still never loads Vulkan, and the published windowed (`--windowed --ticks 120`) and offscreen
(`--offscreen --ticks 120`) runs are the three-mode AOT gate on a GPU machine. The headless gate
exits 0 after exactly 120 completed ticks by default and after exactly N with `--headless --ticks
N` (Q15=O2/F3); the windowed/offscreen gate is backpressured and exits on `Exit` after exactly 120
ticks, and their frame counts are not asserted (Q22=V1). The GPU tests prove the discard path. Any
`IL3000`/`IL3002` warning stays covered by the existing narrow `Pong.Desktop`
suppression; a diagnostic publish with `NoWarn` cleared reports only those two codes (see
[NativeAOT §6](architecture/nativeaot.md#6-native-libraries-and-silknet)).
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
7-entry browse, exactly `N` completed ticks (`N = 120` by default or the explicit `--ticks` value),
and the detached world all pass. The `--windowed --ticks 120` and `--offscreen --ticks 120` runs
load the same pack and assert one `Setup`, exactly 120 completed ticks, and one `Teardown`; the
backpressured runs are short by design and assert no frame count or duration (Q22=V1). The
interactive `--windowed` run is the manual visibility/input check (the window shows the M7
one-frame-per-snapshot black clear; the M8 WP-2 shader pipeline and the WP-3 texture pages/startup
placeholder are created but no draw is recorded until WP-4; the title carries the score/state).
Inspect
the publish directory: it is a NativeAOT single-file output plus PDBs, so the
managed assemblies are linked in. It must contain `Pong.Desktop.exe`, `assets.pck`, `SDL2.dll`, and
the engine/runtime/`Pong.Common` PDBs (`Pong.Common.pdb` is the on-disk proof that the game assembly
is in the graph), and must not contain a `Pong.Assets`, `Bork2d.Bake`, `Tomlyn`, or `StbImageSharp`
assembly.
`dotnet clean src/Bork2d/Bork2d.slnx` and `dotnet clean examples/pong/Pong.slnx` remove the packs and
the incremental state under the assets project's `obj/bork2d/bake`.

### 2.1 Shader compiler, packages, and M8 gates

The M8 sprite renderer compiles shaders at **build time**, not at runtime:

- The build-only JIT tool `build/Bork2d.ShaderCompiler` pins `Silk.NET.Shaderc` **2.23.0** and
  `Silk.NET.Shaderc.Native` **2.23.0** (build-only; they never enter the runtime or AOT publish
  graph). No runtime project references them.
- An MSBuild target (`build/Bork2d.ShaderCompiler.targets`, imported by `Bork2d.Render`) restores
  and builds the tool out of process (with `RemoveProperties` so the consumer's RID/publish settings
  do not leak), compiles the checked-in GLSL
  (`src/Bork2d/Bork2d.Render/Shaders/sprite.vert`, `sprite.frag`) to SPIR-V (Vulkan 1.3 target;
  debug info in Debug, optimization in Release), and emits
  `obj/<Configuration>/<TFM>/ShaderCompiler/SpriteShaders.g.cs` with the SPIR-V byte arrays into
  `Bork2d.Render`'s compilation before `CoreCompile`. The generated file is not committed and is never
  a loose publish file; the code is linked into the app and the publish contains no `.spv` and no
  shaderc assembly. The target also runs for design-time/format builds, so the clean-clone CI sequence
  (`dotnet restore` → `dotnet format --no-restore` → `dotnet build --no-restore`) works without a
  prior build. The tool project is **not** in `src/Bork2d/Bork2d.slnx` and is never referenced by a
  runtime project.
- **No Vulkan SDK is required.** `glslangValidator`, `glslc`, and `spirv-val` are not build
  dependencies; installing the SDK is still useful for the validation-layer GPU evidence.
- Do not add runtime shader compilation, shader file I/O, or a runtime shaderc dependency.

M8 gates (in addition to the Debug/Release build, tests, format, docs link check, and AOT gate):

```powershell
pwsh scripts/check-docs.ps1
git diff --check
$env:BORK2D_RUN_GPU_TESTS = '1'
dotnet test src/Bork2d/Bork2d.slnx -c Debug --filter "Category=Gpu"
Remove-Item Env:BORK2D_RUN_GPU_TESTS
```

- **GPU tests stay local-only** (`[GpuFact]`, serialized `RealGpu` collection, skipped without
  `BORK2D_RUN_GPU_TESTS=1`); CI is unchanged and has no GPU runner. M8 GPU tests assert
  counters/passes/validation/lifecycle/placeholder/paging and the `CaptureFrameAsync` **contract**;
  there are **no golden/pixel tests**. Visual acceptance is a manual windowed `Pong` check recorded
  in the milestone PR.
- **Benchmark (local-only, opt-in `[GpuFact]`):** offscreen 1920×1080, one camera, 50,000 generated
  sprites, a 10-second measurement window after warm-up, vsync off, no artificial cap or sleep.
  Record p50/p95/max frame time, FPS, render-thread CPU, allocations, and the
  machine/GPU/driver/SDK; zero steady-state allocations in the measured path. Pass criterion:
  **median ≥165 FPS** (p50 ≤ ~6.06 ms). If the target cannot be met, stop and report; do not lower
  it.

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
  handoff changes; GPU tests for renderer changes (structural/counter/lifecycle/API-contract
  assertions — **no pixel/golden tests**). Asset/pack tests build packs in memory or
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
- **M8 renderer changes** add deterministic plan/math/page/instance-layout/public-API tests, the
  fake-`IAssets` page-state and `RenderStats` tests (WP-3), and zero-allocation tests, plus opt-in GPU
  rendering, placeholder/paging, and `CaptureFrameAsync` contract tests (WP-4). The 50,000-sprite
  benchmark and the manual windowed visual check are local evidence, not CI; there are no
  pixel/golden assertions.
- Real-window tests use `[WindowFact]`, are skipped without `BORK2D_RUN_WINDOW_TESTS=1`, share the
  serialized `RealSdlWindow` collection, and may use only signals/bounded waits (no sleeps). Run
  them locally before a window/runtime PR. Real-GPU tests use `[GpuFact]`, are skipped without
  `BORK2D_RUN_GPU_TESTS=1`, share the serialized `RealGpu` collection, skip when the loader/device/
  validation layer is unavailable, and likewise use no sleeps. Run them locally before a renderer PR.
- The dependency test and docs link check run in CI. The AOT publish gate (`Pong.Desktop`
  `--headless` with its 120-tick default, `--headless --ticks N`, `--windowed --ticks 120`, and
  `--offscreen --ticks 120`) is a required local gate, not a CI job. CI scope is unchanged: pull
  requests to `develop` and pushes to `develop` only (restore, format verify, build, Debug tests);
  there is no GPU, AOT, or examples-solution job.
- GPU/validation checks need a Vulkan 1.3 machine from M6; the deterministic suite, headless builds,
  and data tests must run without a GPU.
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
- [ ] `Pong.Desktop` publishes/runs when runtime-affecting, including `--headless` (120 ticks by
      default), `--headless --ticks N`, `--windowed --ticks 120`, and `--offscreen --ticks 120` for
      window/renderer changes.
- [ ] Pack format/version updated if assets changed.
- [ ] Owning docs/API/decision log updated; status markers honest.
- [ ] Shader changes compile through the build-time `Bork2d.ShaderCompiler` tool; no runtime shader
      I/O, no new runtime package, and the publish has no shaderc assembly or loose `.spv`.
- [ ] Renderer changes run the opt-in GPU tests locally; M8 renderer PRs record the
      benchmark/manual visual check when they apply. No pixel/golden tests are added.
- [ ] No unjustified suppressions.

## 9. Related documents

- [Projects and dependencies](architecture/projects.md) — boundaries.
- [NativeAOT](architecture/nativeaot.md) — AOT constraints.
- [Logging](architecture/logging.md) — ZLogger/MEL usage.
- [Assets and tooling](architecture/assets-and-tooling.md) — formats and bake.
- [Roadmap](roadmap.md) — milestone sequence.
