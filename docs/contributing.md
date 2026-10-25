# Contributing

> **Status:** current. The warnings/style/format/AOT gates are active from M1 and run in this
> repository today. CI runs build, tests, and format verification on pull requests targeting
> `develop` and on pushes to `develop`; the NativeAOT publish/run smoke is a required local gate.

## 1. Prerequisites

- .NET SDK pinned by `global.json` (`11.0.100-rc.1`, target `net11.0`).
- Windows for the windowed/Vulkan targets; headless builds/tests should stay cross-platform
  where practical.
- Vulkan 1.3 GPU/driver for windowed/offscreen checks from M6; not required for M1 unit tests.
- SDL2 native binaries for windowed runs from M2.
- NativeAOT toolchain (MSVC) for the local runtime publish check.

## 2. Build and test

```powershell
dotnet build src/Bork2d/Bork2d.slnx -c Debug
dotnet build src/Bork2d/Bork2d.slnx -c Release
dotnet test src/Bork2d/Bork2d.slnx -c Debug
dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
```

The solution builds with **zero warnings**; CI treats warnings and style violations as errors.
CI runs on pull requests targeting `develop` and on pushes to `develop`; it does not run on
milestone/task branch pushes.

NativeAOT publish/run is a **required local gate** for runtime-affecting changes:

```powershell
dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64
& src/Bork2d/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe
```

## 3. Build, style, and warnings

All projects—engine, editor, tests, samples—use the same baseline:

- `TreatWarningsAsErrors=true`;
- `EnforceCodeStyleInBuild=true`;
- Microsoft/.NET-standard `.editorconfig` (file-scoped namespaces, braces, naming, spacing,
  `var`/expression preferences, using ordering, etc.);
- IDE style rules set to `error` for rules the team enforces;
- AOT/trim analyzers as errors for runtime projects (`IsAotCompatible`, `EnableAotAnalyzer`,
  `EnableTrimAnalyzer`);
- `dotnet format --verify-no-changes` in CI.

The shared settings live in `Directory.Build.props` and the root `.editorconfig`, so new
projects inherit them automatically. Do not override them per project without a decision-log
entry.

### Suppressions

- Prefer fixing the code.
- If a suppression is unavoidable, use the narrowest `#pragma warning disable` or
  `[SuppressMessage]`, include the justification, and keep it scoped to the smallest block.
- `NoWarn` in a project file is a code-review flag; it needs a reason and an expiry/owner.
- Do not disable style rules wholesale to make a build pass.

## 4. C# conventions

The standard baseline (exact `.editorconfig` values finalised with the tooling in M1):

- file-scoped namespaces;
- nullable reference types enabled; no `!` unless the invariant is documented;
- explicit accessibility on public API;
- `var` only when the type is obvious from the right-hand side;
- braces for all control-flow bodies;
- one type per file, file name matches the type;
- private fields `_camelCase`, constants `PascalCase`, interfaces `I`-prefixed;
- `async` methods end in `Async`; no `async void` outside event handlers;
- `ArgumentNullException.ThrowIfNull`-style guard clauses;
- no regions except generated code;
- no commented-out code;
- XML docs on public API (the API guide/reference mirror them);
- `Microsoft.Extensions.Logging` for logging, not `Console.WriteLine`;
- `System.Text.Json`/Nerdbank.MessagePack only where explicitly designed; no ad-hoc
  serialization.

## 5. Architecture boundaries

- Game code references `Bork2d.Api`/`Bork2d.Runtime` only; see
  [Projects and dependencies](architecture/projects.md).
- Editor/tooling code stays JIT; do not leak Avalonia, TOML, or reflection into runtime
  projects.
- Runtime projects stay NativeAOT/trim-safe: no runtime reflection, assembly scanning, runtime
  TOML, dynamic code, or unregistered Nerdbank shapes.
- No Vulkan/SDL types in `Bork2d.Api`; logging abstractions are the only allowed external
  contract dependency.
- Keep pack format changes versioned and documented in
  [Assets and pack files](architecture/assets.md).

## 6. Tests and CI

- Add unit tests with behavior changes; pack/format tests for asset changes; threading tests
  for snapshot changes; GPU/visual tests for renderer changes.
- The dependency test runs in CI. The AOT publish smoke is a required local gate, not a CI job:
  CI runs build/tests/format on pull requests targeting `develop` and on `develop` pushes
  (human decision 2026-10-25).
- A GPU/validation runner is required for windowed/offscreen checks; headless/data tests must
  run without a GPU.
- Do not merge with disabled tests, flaky retries without a tracking entry, or a lowered
  warning level.

## 7. Documentation

- Update the architecture docs, API guide/reference, roadmap status, and decision log in the
  same change as a contract change.
- Keep status markers accurate (`Current`/`Partial`/`Planned`/`Open`/`Deferred`).
- Add a decision entry for public-contract or format changes, including alternatives.
- Update the roadmap progress table when a milestone lands.

## 8. Pull-request checklist

- [ ] `dotnet build` has zero warnings.
- [ ] `dotnet test` passes.
- [ ] `dotnet format --verify-no-changes` passes.
- [ ] Dependency matrix respected.
- [ ] No runtime reflection/TOML/dynamic code.
- [ ] NativeAOT smoke still publishes/runs (when runtime-affecting).
- [ ] Pack format/paths/version updated if assets changed.
- [ ] Docs/API/decision log updated.
- [ ] No unjustified suppressions.

## 9. Related documents

- [Projects and dependencies](architecture/projects.md) — boundaries and build.
- [NativeAOT](architecture/nativeaot.md) — runtime AOT constraints.
- [Logging](architecture/logging.md) — ZLogger/MEL usage.
- [Assets and pack files](architecture/assets.md) — format changes.
- [Roadmap](roadmap.md) — M1 establishes the build/style baseline.
