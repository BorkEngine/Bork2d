# M1 — Foundation and NativeAOT Baseline (record)

> **Status:** implemented (WP1–WP8) and merged into `develop` on 2026-10-25 (PR #1). This is a
> historical record, not a live contract. The living contracts are the
> [API reference](../api/reference.md), [Runtime lifecycle](../architecture/runtime-lifecycle.md),
> [Scene ownership](../architecture/scene-ownership.md), and the [decision log](../decisions.md).

## 1. Goal

Produce the foundation later milestones build on: the project graph and public/internal boundary,
a builder and host with caller-constructed game injection, a root-owned scene graph with
remove/re-add lifetime, a thread-affinity-free API, MEL/ZLogger logging, warnings/style enforcement,
and NativeAOT/trim analyzers plus a publish smoke. M1 did **not** create a window, renderer, pack,
or asset system.

## 2. Delivered

| Area | Result |
| --- | --- |
| Build baseline | `Directory.Build.props`, `.editorconfig`, `global.json`, xUnit project, dependency test, CI build/test/format. |
| Public contracts | `IEngine`, `IGame`, `ISceneRoot`, `ITransform`, `Bork2dException`; no `IDisposable` on scene entities. |
| Scene graph | Internal `Transform`/`SceneRoot`/`ISceneNode`; structural and per-object locks; world-preserving re-parent; cycle/self/root rejection; unordered tree; camera `ZIndex`. |
| Host | `Bork2dAppBuilder<TLayer>` validation, one-tick `Bork2dApp.Run`, idempotent `Dispose`, mode selection. |
| Logging | App-owned MEL/ZLogger factory, default plain-text console provider, startup log, categories. |
| NativeAOT | `IsAotCompatible` plus trim/AOT analyzers on libraries; `Bork2d.AotSmoke` with `PublishAot`; Nerdbank generated-shape smoke. |
| Tests | 135 xUnit tests covering ownership/GC, re-parent, rotation, camera, builder, logging, concurrency, and the dependency matrix. |

The accepted decisions behind this work are D-001–D-026 in the [decision log](../decisions.md);
M1-specific resolved questions (DetachAll scope, lock granularity, stub window, standalone
transform, logging format, rotation unit, scene identity, camera ordering, AOT/CI policy) are
recorded there.

## 3. Evidence at merge

- Debug and Release builds had zero warnings/errors; warnings, style, and AOT/trim diagnostics were
  errors.
- `dotnet format --verify-no-changes` was clean; `Bork2d.AotSmoke` published for `win-x64` and ran
  with zero AOT/trim warnings.
- Root ownership held: parented objects persisted, unparented objects collected, remove/re-add
  preserved the object, `Root.DetachAll()` detached, and `ISceneRoot` mutations threw.
- No `EntityList<T>`, weak registry, or entity `Dispose`/`IsAlive` remained.
- The tree had no SDL, Vulkan, pack, or renderer code; those arrived in M2/M3/M6+.

## 4. Out of scope

SDL window/input, Vulkan, render thread, snapshot ring, renderer, `assets.pck`, asset loading, TOML
baking, editor, real mode loops, scene serialization/prefabs, and lifetime scopes.

## 5. Related documents

- [Roadmap](../roadmap.md#5-milestone-detail) — milestone sequence and status.
- [Decision log](../decisions.md) — D-001–D-026.
- [Scene ownership](../architecture/scene-ownership.md), [Threading](../architecture/threading.md),
  [Runtime lifecycle](../architecture/runtime-lifecycle.md).
- [NativeAOT](../architecture/nativeaot.md), [Logging](../architecture/logging.md),
  [Contributing](../contributing.md).
