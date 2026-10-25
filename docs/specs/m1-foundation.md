# M1 — Foundation and NativeAOT Baseline (record)

> **Status:** implemented (WP1–WP8) and merged into `develop` on 2026-10-25 (PR #1). This is the
> historical record; the living contracts are in [API reference](../api/reference.md),
> [Runtime lifecycle](../architecture/runtime-lifecycle.md), and [Scene ownership](../architecture/scene-ownership.md).

## 1. Goal

Produce the foundation the later milestones build on: project graph and public/internal boundary,
builder/host with instance game injection, root-owned scene graph with remove/re-add lifetime,
thread-affinity-free API, MEL/ZLogger logging, warnings/style enforcement, and NativeAOT/trim
analyzers plus a publish smoke. M1 did **not** create a window, renderer, pack, or asset system.

## 2. Locked decisions

| Topic | Decision |
| --- | --- |
| Public surface | `Api` + `Runtime` only; `Common`/`Assets`/`Render` internal. |
| Ownership | Root-owned strong scene graph; parent-required cameras/sprites; no weak registry. |
| Lifetime | No `Dispose`/`IsAlive` on entities; detach with `Parent = null`, re-attach later. |
| Root | `ISceneRoot : ITransform`; identity transform; all setters throw. |
| Cleanup | `DetachAll()` detaches direct children; app shutdown clears root children. |
| Threads | No thread affinity; one structural `Lock` + per-object `Lock`. |
| Scene tree | Unordered; no entity IDs, ordered discovery, or child enumeration. |
| Rotation | Degrees, CCW, normalized `[0, 360)`; internal math in radians. |
| Camera order | `ZIndex` ascending; higher later; default `0`; ties unspecified. |
| Snapshots | Not in M1; internal value-only structs and capture are M7. |
| Logging | `IEngine.LoggerFactory`; `WithLogging`; pretty ZLogger console default. |
| M1 `Run` | `Setup` → one `Tick` → `Teardown`; smoke only. |
| Pack | Format accepted; no implementation; builder validates the path shape only. |

## 3. Work packages (delivered)

| WP | Delivered |
| --- | --- |
| WP1 | `Directory.Build.props`, `.editorconfig`, `global.json`, xUnit project, dependency test, CI build/test/format, placeholder removal. |
| WP2 | `ISceneRoot`, `ITransform` (no `IDisposable`), `IEngine`, `IGame.Teardown`, `Bork2dException`; Api logging abstraction. |
| WP3 | Internal `Transform`/`SceneRoot`/`ISceneNode`; structural + per-object locks; world-preserving re-parent; cycle/self/root rejection; unordered tree; camera `ZIndex`. |
| WP4 | `Bork2dAppBuilder<TLayer>` validation, one-tick `Bork2dApp.Run`, idempotent `Dispose`, mode selection. |
| WP5 | App-owned MEL/ZLogger factory, default console provider, startup log, categories. |
| WP6 | `IsAotCompatible` + analyzers on libraries; `Bork2d.AotSmoke` with `PublishAot`; Nerdbank generated-shape smoke. |
| WP7 | 135 xUnit tests (ownership/GC, re-parent, rotation, camera, builder, logging, concurrency, dependency matrix). |
| WP8 | Docs reconciliation and the same-change contract updates. |

## 4. Acceptance evidence

- Debug and Release builds: zero warnings/errors; warnings, style, and AOT/trim diagnostics are
  errors.
- `dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore`: clean.
- `dotnet test src/Bork2d/Bork2d.slnx -c Debug`: 135 passed.
- Dependency test asserts the reference matrix; implementation types are internal.
- `Bork2d.AotSmoke` publishes for `win-x64` and runs with zero AOT/trim warnings: default logging,
  one `Setup`/`Tick`/`Teardown`, parented transform/camera/sprite, cleared root, MessagePack
  generated-shape round trip.
- Root ownership: parented objects persist; unparented objects collect; remove/re-add preserves the
  object; `Root.DetachAll()` detaches; `ISceneRoot` mutations throw.
- No `EntityList<T>`, weak registry, or entity `Dispose`/`IsAlive` remains.

## 5. Resolved M1 questions

| Question | Resolution |
| --- | --- |
| `DetachAll()` scope | Direct children only; grandchildren stay attached. |
| Lock granularity | One structural lock for parent changes; one `Lock` per object for properties. |
| `IWindow` in M1 | No window; `Window` returns a stub whose members throw; `Input` getter throws. |
| Standalone transform | `CreateTransform(null)` is legal, unrendered, and collectible. |
| Lifetime scopes | Deferred; root ownership is the model. |
| Logging versions/format | `ZLogger` 2.5.10; `Microsoft.Extensions.Logging` 11.0.0-rc.1.26425.128; plain-text local time/level/category; no colors. |
| `WithAssetsPack` in M1 | Non-empty path shape only; no filesystem access. |
| Rotation unit | Degrees, normalized `[0, 360)`; non-finite throws. |
| Scene identity | No IDs or order; tests assert relationships/identity/GC. |
| Camera ordering | `ZIndex` exists and round-trips; applied in M8. |
| AOT/CI | AOT publish/run is a local gate; CI is build/test/format on `develop` PRs/pushes. |

## 6. Out of scope for M1

SDL window/input, Vulkan, render thread, snapshot ring, renderer, `assets.pck`, asset loading, TOML
baking, editor, real mode loops, scene serialization/prefabs, and lifetime scopes.

## 7. Related documents

- [Roadmap](../roadmap.md#5-milestone-detail) — next milestones.
- [Decision log](../decisions.md) — D-001…D-026.
- [Scene ownership](../architecture/scene-ownership.md), [Threading](../architecture/threading.md),
  [Runtime lifecycle](../architecture/runtime-lifecycle.md).
- [NativeAOT](../architecture/nativeaot.md), [Logging](../architecture/logging.md),
  [Contributing](../contributing.md).
