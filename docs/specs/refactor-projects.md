# Project Consolidation — Refactor Spec

> **Status:** In progress on `task/project-consolidation` (2026-10-27). Owner-approved refactor:
> the engine solution shrinks from eleven projects to five, the engine contract moves to the
> `Bork2d.Engine` namespace, the bake tooling merges into `Bork2d.Bake`, and the smoke/fixture
> projects move to a separate examples solution. This spec is the implementation authority for the
> task; the as-built record is filled in at §12.

## 1. Goal

Reduce the project count and consolidate the assembly/namespace surface without changing engine
semantics:

- `Bork2d.Bake` = `Bork2d.Bake` + `Bork2d.Bake.Builder`;
- `Bork2d.Engine` (new) = `Bork2d.Common` + `Bork2d.Assets` + `Bork2d.Api`;
- `Bork2d.Render` and `Bork2d.Runtime` stay separate;
- `Bork2d.Tests` stays the only test project;
- smoke/fixture projects move to `examples/` with their own solution.

The refactor is pre-1.0 and owner-approved as a public API break under D-014. There is no
compatibility shim: the old assemblies and namespaces are retired.

## 2. Target layout

```
src/Bork2d/Bork2d.slnx                     examples/Bork2d.Examples.slnx
  Bork2d.Engine      (new)                   Bork2d.AotSmoke
  Bork2d.Bake        (merged)                Bork2d.BakeFixture
  Bork2d.Render                              Bork2d.BakeFixture.Assets
  Bork2d.Runtime
  Bork2d.Tests
```

`Bork2d.Engine` is laid out by area. `RootNamespace` equals the project name, so the existing
folder-per-area rule and IDE0130 stay satisfied:

| Folder | Namespace | Source |
| --- | --- | --- |
| root | `Bork2d.Engine` | `IEngine`, `IGame`, `Bork2dException` |
| `Scene/` | `Bork2d.Engine.Scene` | public scene contracts + internal `Transform`/`SceneRoot`/`GameEngine` |
| `Rendering/` | `Bork2d.Engine.Rendering` | `Color`, `ColorMarshaler`, `ClearKind` |
| `Input/` | `Bork2d.Engine.Input` | `IInput`, `KeyCode`, `MouseButton` |
| `Platform/` | `Bork2d.Engine.Platform` | `ITime`, `IWindow` |
| `Time/` | `Bork2d.Engine.Time` | internal frame clock |
| `Assets/` | `Bork2d.Engine.Assets` | asset contract and implementation |
| `Assets/Pack/`, `Assets/Vfs/`, `Assets/Loading/` | `.Assets.Pack`, `.Assets.Vfs`, `.Assets.Loading` | pack writer/reader, VFS, loader |
| `Serialization/` | `Bork2d.Engine.Serialization` | internal MessagePack smoke helpers (moved to the smoke app in P3) |

`Bork2d.Bake` keeps its implementation folders and gains the former builder surface at the project
root (`Bork2d.Bake`), with `Commands/` and the builder `Processors/` merged into the existing
folders.

## 3. Namespace and public API mapping

| Current | New |
| --- | --- |
| `Bork2d.Api` | `Bork2d.Engine` |
| `Bork2d.Api.Scene` | `Bork2d.Engine.Scene` |
| `Bork2d.Api.Rendering` | `Bork2d.Engine.Rendering` |
| `Bork2d.Api.Input` | `Bork2d.Engine.Input` |
| `Bork2d.Api.Platform` | `Bork2d.Engine.Platform` |
| `Bork2d.Api.Assets` | `Bork2d.Engine.Assets` |
| `Bork2d.Assets.Pack` (tooling public) | `Bork2d.Engine.Assets.Pack` |
| `Bork2d.Bake.Builder` | `Bork2d.Bake` |
| `Bork2d.Bake.Builder.Commands` | `Bork2d.Bake.Commands` |
| `Bork2d.Bake.Builder.Processors` | `Bork2d.Bake.Processors` |

Internal namespace mappings: `Bork2d.Common.Scene` → `Bork2d.Engine.Scene`,
`Bork2d.Common.Time` → `Bork2d.Engine.Time`,
`Bork2d.Common.Serialization` → `Bork2d.Engine.Serialization`,
`Bork2d.Assets.Vfs` → `Bork2d.Engine.Assets.Vfs`,
`Bork2d.Assets.Loading` → `Bork2d.Engine.Assets.Loading`.

Consequences:

- The internal `Engine<TLayer>` class is renamed `GameEngine<TLayer>` (owner-confirmed) so the
  type is not `Bork2d.Engine.Scene.Engine`.
- Engine-authored data types referenced from TOML become `Bork2d.Engine.Assets.SpriteAsset` /
  `Bork2d.Engine.Assets.TextureDescriptor`. Game-owned `[bork2d].type` values are unchanged.
- Logging categories become `Bork2d.Runtime`, `Bork2d.Engine`, `Bork2d.Engine.Assets`,
  `Bork2d.Render` (owner-confirmed).
- The public `Bork2d.Engine` surface is the documented contract plus the tooling-only
  `Bork2d.Engine.Assets.Pack.PackBuilder`/`PackCompressionMode`.

## 4. Reference graph and visibility

| Project | References | Notes |
| --- | --- | --- |
| `Bork2d.Engine` | none (Bork2d) | PolyType 1.4.1, Nerdbank.MessagePack 1.4.95, System.IO.Hashing 11.0.0-rc.1.26425.128; AOT/trim analyzers on |
| `Bork2d.Render` | Engine | |
| `Bork2d.Runtime` | Engine, Render | SDL, ZLogger, Logging packages unchanged |
| `Bork2d.Bake` | Engine | Tomlyn, PolyType, StbImageSharp, Hashing; JIT |
| `Bork2d.Tests` | Engine, Render, Runtime, Bake, examples `Bork2d.BakeFixture`, examples `Bork2d.BakeFixture.Assets` | no smoke reference |
| examples `Bork2d.AotSmoke` | Engine, Runtime, Fixture (+ build-only Fixture.Assets) | NativeAOT |
| examples `Bork2d.BakeFixture` | Engine | AOT-compatible |
| examples `Bork2d.BakeFixture.Assets` | Bake, Fixture | build-time only |

`InternalsVisibleTo`:

| Project | Visible to |
| --- | --- |
| `Bork2d.Engine` | Render, Runtime, Bake, Editor, Tests (the AotSmoke grant is removed in P3) |
| `Bork2d.Bake` | Editor, Tests |
| `Bork2d.Render` | Runtime, Tests |
| `Bork2d.Runtime` | Tests |
| `Bork2d.BakeFixture.Assets` | Tests |

`Bork2d.Bake` needs the Engine-internal canonical path grammar (`AssetPath`). The grant is
intentional and is guarded by a metadata test: every `Bork2d.Engine` type referenced by
`Bork2d.Bake` must be in `Bork2d.Engine`, `.Rendering`, `.Assets`, `.Assets.Pack`, or
`.Assets.Vfs` (Q-20 is therefore test-enforced, not assembly-enforced).

## 5. Work phases

Phases are sequential on one branch; the file moves conflict if parallelised.

### P0 — Process docs and branch

- Update `AGENTS.md` §7 (AOT gate path, no examples CI) and §8 (public boundary, logging
  categories); commit directly to `develop` as the §1 docs-only commit.
- Create `task/project-consolidation` from that `develop`.

### P1 — Merge Api + Common + Assets into `Bork2d.Engine`

- Create `src/Bork2d/Bork2d.Engine/Bork2d.Engine.csproj` (AOT-compatible; merged package
  references; merged IVT).
- `git mv` the 70 source files into the area layout of §2.
- Rewrite namespaces and `using` directives in the engine and every consumer (Render, Runtime,
  both Bake projects, Tests, fixture projects, smoke).
- Rename `Engine<TLayer>` to `GameEngine<TLayer>` and its file.
- Replace `"Bork2d.Assets"` in `BakeAssemblyClosure` with `"Bork2d.Engine"`; update logging
  categories.
- Update the project references of Render, Runtime, Bake, Bake.Builder, Tests, fixture projects,
  and the smoke; delete the old project directories.
- Rewrite `DependencyMatrixTests` for the new graph and exact public surfaces.

### P2 — Merge `Bork2d.Bake.Builder` into `Bork2d.Bake`

- `git mv` the 19 builder files: root files to the project root, `Commands/` to `Commands/`, and
  `Processors/` into the existing `Processors/` (no file-name collisions).
- Merge the csproj files and `AssemblyReferenceAnchor`; leave `Bork2d.Bake` JIT.
- Flatten namespaces to `Bork2d.Bake`/`.Commands`/`.Processors`.
- Update the fixture assets executable, Tests, the MSBuild-integration test, and the dependency
  tests.

### P3 — Examples solution and smoke decoupling

- Move the three projects to `examples/`; add `examples/Bork2d.Examples.slnx`.
- Update the smoke csproj paths, `RepositoryPaths`, the MSBuild-integration fixture path, and the
  dependency tests.
- Move `MessagePackSmokeData`/`MessagePackSmokeSerializer` into the smoke app and rewrite the
  shape test with a tests-local `[GenerateShape]` type; remove the Engine → AotSmoke IVT.
- Remove `Bork2d.Tests` → `Bork2d.AotSmoke` and the smoke assembly assertion.
- Do **not** add the examples solution to CI (owner decision); the local AOT gate exercises it.

### P4 — Docs, decisions, verification

- Create the decision entries (D-041/D-042) and annotate the affected earlier entries.
- Rewrite `docs/architecture/projects.md`; update the API guide/reference, NativeAOT, logging,
  assets/tooling, asset-processors, overview, contributing, README, and the roadmap progress log.
- Run every gate, record the as-built evidence in §12, and open the PR to `develop`.

## 6. Tests and guards

- `DependencyMatrixTests`: Engine references no Bork2d assembly and only allowed packages; exact
  exported namespace/type allowlist for Engine and Bake; internal types stay internal; Render →
  Engine; Runtime → Engine/Render; Bake → Engine; Tests → Engine/Render/Runtime/Bake + fixtures;
  fixture references; no tooling assembly in runtime references.
- New metadata test: `Bork2d.Bake` only references allowed `Bork2d.Engine` namespaces (Q-20
  guard).
- New solution-shape test: `src/Bork2d/Bork2d.slnx` lists exactly the five projects.
- Removed: `AotSmokeReferencesOnlyTheRuntimeSurfaceItUses` (the smoke is no longer referenced by
  Tests).
- Updated: fixture paths (`RepositoryPaths`), MSBuild integration paths and tooling name lists,
  hard-coded full type names, logging-category assertions, TOML type strings.

## 7. Documentation

Current documents are rewritten; historical milestone specs and decision history keep their old
names. The README project table and links, `docs/architecture/projects.md`,
`docs/architecture/nativeaot.md`, `docs/architecture/logging.md`,
`docs/architecture/assets-and-tooling.md`, `docs/architecture/asset-processors.md`,
`docs/architecture/overview.md`, `docs/api/guide.md`, `docs/api/reference.md`,
`docs/contributing.md`, `docs/README.md`, and `docs/roadmap.md` are in scope. `AGENTS.md` was
updated in P0.

## 8. Acceptance criteria

- `src/Bork2d/Bork2d.slnx` lists exactly: `Bork2d.Engine`, `Bork2d.Bake`, `Bork2d.Render`,
  `Bork2d.Runtime`, `Bork2d.Tests`.
- No `src/Bork2d/Bork2d.{Api,Common,Assets,Bake.Builder,BakeFixture,BakeFixture.Assets,AotSmoke}`
  directory remains; the stale `Bork2d.Bake.Cli` artifacts are removed.
- `examples/Bork2d.Examples.slnx` builds and contains the three example projects; Tests does not
  reference the smoke.
- All tests are green; Debug and Release builds have zero warnings; both format gates pass.
- The local AOT gate publishes and runs headless and with `--windowed --ticks 120`; the publish
  contains `Bork2d.Engine.dll`, both packs, and `SDL2.dll`, and no Bake/Tomlyn/StbImageSharp/
  assets-executable assembly.
- The dependency tests prove the reference matrix and the public surfaces; the Q-20 metadata
  guard passes.
- `scripts/check-docs.ps1` and `git diff --check` are clean.
- Current docs and README describe the new layout; the decision log records the break.

## 9. Verification

```powershell
dotnet build src/Bork2d/Bork2d.slnx -c Debug
dotnet build src/Bork2d/Bork2d.slnx -c Release
dotnet test src/Bork2d/Bork2d.slnx -c Debug --no-build
dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
dotnet build examples/Bork2d.Examples.slnx -c Debug
dotnet format examples/Bork2d.Examples.slnx --verify-no-changes --no-restore
dotnet publish examples/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64 --nologo
& examples/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe
& examples/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe --windowed --ticks 120
./scripts/check-docs.ps1
git diff --check
```

## 10. Risks and deviations

| Risk | Mitigation |
| --- | --- |
| Large mechanical diff | `git mv` for history, one commit per phase, review the final tree. |
| Public API break | Owner-approved; migration note in the API guide; no shim. |
| Q-20 erosion through the merged Engine | Engine → Bake IVT plus the metadata guard test. |
| Incremental bake hash change | Expected one full re-bake; tests assert behavior, not literal hashes. |
| Cross-solution test references | Accepted; both solutions are built locally. |
| Examples not in CI | Owner decision; the local AOT gate covers the smoke. |

## 11. Resolved confirmations

- Internal class name: `GameEngine<TLayer>`.
- Logging categories: renamed as in §3.
- Smoke MessagePack helpers: moved into the example smoke app.
- CI: no examples job for now.
- `PackBuilder` stays public in `Bork2d.Engine.Assets.Pack`; moving it to `Bork2d.Bake` is a
  possible follow-up.

## 12. As-built evidence

- Branch: `task/project-consolidation`. Process-doc commit: `f5c4e68` (AGENTS.md, docs-only on
  `develop`); spec commit: `00ae614`; implementation commit: `03a0358`; the documentation update
  that includes this section follows on the same branch.
- Engine solution: exactly five projects (`Bork2d.Engine`, `Bork2d.Bake`, `Bork2d.Render`,
  `Bork2d.Runtime`, `Bork2d.Tests`); examples solution has the three example projects. No
  `src/Bork2d/Bork2d.{Api,Common,Assets,Bake.Builder,AotSmoke,BakeFixture,BakeFixture.Assets}`
  directory remains, and the stale `Bork2d.Bake.Cli` artifacts are gone. The root `.gitignore` now
  covers both `src/` and `examples/`.
- Builds: Debug and Release of the engine solution succeed with 0 warnings and 0 errors; the
  examples solution builds with 0 warnings and 0 errors.
- Tests: `942 passed, 12 skipped, 954 total` (`dotnet test src/Bork2d/Bork2d.slnx -c Debug
  --no-build`), including the new `EngineSolutionContainsOnlyTheFiveProjects` and
  `BakeReferencesOnlyTheAllowedEngineNamespaces` guards.
- Format: `dotnet format --verify-no-changes` exits 0 for both `Bork2d.slnx` and
  `Bork2d.Examples.slnx`.
- AOT: `dotnet publish examples/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64`
  reports zero AOT/trim warnings; the published headless run exits 0 with `mainEntries=8` /
  `mergedEntries=9`, and `--windowed --ticks 120` exits 0 after exactly 120 ticks. A clean publish
  directory contains `Bork2d.Engine.pdb`, `Bork2d.Runtime.pdb`, `Bork2d.Render.pdb`,
  `Bork2d.BakeFixture.pdb`, `Bork2d.AotSmoke.exe`, `assets.pck`, `dlc1.pck`, and `SDL2.dll`, and
  no `Bork2d.Bake`, `Bork2d.BakeFixture.Assets`, Tomlyn, or StbImageSharp assembly.
- Docs: `scripts/check-docs.ps1` passes (26 files, 432 local links); `git diff --check` is clean.
- Deviations from the phase plan: the engine, bake, and examples changes landed as one
  implementation commit because the file moves and namespace rewrites are entangled across all
  three; the examples keep their existing project/assembly names; the fixture assets executable and
  tests keep their names. The `Bork2d.Engine.csproj` file is recorded by git as a rename of the old
  `Bork2d.Assets.csproj`, and the builder files as renames from `Bork2d.Bake.Builder`.
  The examples solution is deliberately not a CI job (owner decision); the local AOT gate covers
  the smoke.
