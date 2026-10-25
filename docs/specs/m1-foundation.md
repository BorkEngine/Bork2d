# M1 — Foundation and NativeAOT Baseline: Implementation Spec

> **Status:** implemented (WP1–WP8); final PR `m-1/common` → `develop` pending human review.
> **M0 is complete** (2026-10-25). This spec supersedes the older M1 bullet list in the roadmap;
> the roadmap links here. M1 shipped 135 tests, Debug/Release builds with zero warnings, a clean
> `dotnet format` check, and a local NativeAOT publish/run smoke. It is not merged or reviewed.

## 1. Goal

Produce the stable foundation the rest of the engine builds on:

- the project graph and public/internal boundary;
- a public builder/host contract with instance-based game injection;
- the **root-owned scene graph** with remove/re-add lifetime (no weak registry, no
  `Dispose`/`IsAlive` on scene entities);
- a thread-affinity-free public API;
- logging through Microsoft.Extensions.Logging + ZLogger;
- warnings/style enforcement and a clean CI baseline;
- NativeAOT/trim analyzers plus a publish smoke app.

M1 does **not** create a window, renderer, pack, or asset system. Those are M2/M3/M6+.

## 2. Exit criteria

M1 is complete when all of these are demonstrated:

1. Every project builds with warnings-as-errors and style violations as errors.
2. `dotnet format --verify-no-changes` passes.
3. The dependency test passes and no implementation type is accidentally public.
4. `Bork2d.Tests` (xUnit) covers builder validation, scene ownership, transform hierarchy,
   layers, camera `ZIndex` defaults/round-trip, and the concurrency rules.
5. An AOT-published smoke app runs without reflection, trim, or AOT warnings.
6. `IEngine.LoggerFactory` is usable and a default `Bork2dAppBuilder` run produces pretty
   ZLogger console output.
7. Root ownership works: parented objects persist, unparented objects are collectible,
   remove/re-add preserves the object, and `Root.DetachAll()` detaches the tree.
8. `ISceneRoot` rejects all transform mutations.
9. No public API requires the process main thread.
10. No `EntityList<T>`/weak-reference ownership remains in shipped code.
11. The docs in this spec, the API reference/guide, roadmap, and decision log match the code.

## 3. Locked decisions

| Topic | Decision |
| --- | --- |
| Ownership | Root-owned strong scene graph; add/remove membership; no weak ownership. |
| Object lifetime | No `Dispose`/`IsAlive` on scene entities. Detach to remove; re-attach the same object later. |
| Root | `IEngine.Root : ISceneRoot`; read-only identity transform; mutations throw. |
| Creation | `CreateCamera(parent)` and `CreateSpriteRender(parent)` require a parent. |
| Cleanup | `Root.DetachAll()` detaches children. App shutdown detaches root children. |
| Threads | No thread affinity. Any thread may call the public API during `Setup`/`Tick`/`Teardown`; locks/queues make it safe. |
| AOT | Runtime/game AOT-safe; editor/baker JIT later. |
| Logging | `IEngine.LoggerFactory`; `WithLogging(Action<ILoggingBuilder>)`; pretty ZLogger default. |
| Rotation | Public unit is **degrees**, positive CCW; `LocalRotation` normalized to `[0, 360)` on assignment (D-025). Internal matrix math stays in radians. |
| Scene tree | Root-owned strong tree of parent/child links. It is **unordered**: no entity IDs, no ordered discovery, and no public child enumeration in M1 (D-026). |
| Structural locking | One engine-owned global structural `System.Threading.Lock`, shared by `SceneRoot`, `Transform`, `Camera`, and `SpriteRender`. There is no `SceneGraph` holder. |
| Camera order | `ICamera.ZIndex`; cameras apply in ascending order, higher draws later, default `0`; ties are unspecified (D-026). |
| Snapshots | Not part of M1. Internal value-only `CameraSnapshot`/`SpriteSnapshot` structs and snapshot capture belong to M7 (D-026). |
| Style | Warnings-as-errors, style errors, `dotnet format` gate, all projects. |
| Tests | xUnit. |
| Pack | Format accepted; actual pack implementation is M3. M1 exposes only builder option validation. |
| M1 `Run` | `Setup` → one `Tick` → `Teardown`, then return. Smoke only; real loop is M2/M7. |

## 4. Work packages

### WP1 — Repository, build, and quality baseline

Deliver:

- root `Directory.Build.props`:
  - `TreatWarningsAsErrors=true`;
  - `EnforceCodeStyleInBuild=true`;
  - deterministic builds;
  - consistent nullable/implicit-usings settings.
- root `.editorconfig` with the Microsoft/.NET-standard baseline; enforced style rules at
  `error`;
- `global.json` pinning the installed SDK (`11.0.100-rc.1`) with the chosen roll-forward policy;
- `Bork2d.Tests` using **xUnit** and a dependency test;
- CI: build, tests, format verify on pull requests targeting `develop` and on pushes to
  `develop`; AOT publish/run is a required local gate, not a CI job;
- remove placeholder `Class1` files.

Acceptance: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, and the local
AOT publish/run gate pass. CI runs the build/test/format jobs on pull requests targeting
`develop` and after a merge to `develop`.

### WP2 — API contract changes

Add/adjust in `Bork2d.Api`:

```csharp
public interface ISceneRoot : ITransform
{
    // Read-only marker: all ITransform setters throw on the root.
}

public interface ITransform
{
    ITransform? Parent { get; set; }
    Vector2 LocalPosition { get; set; }
    float LocalRotation { get; set; }
    Vector2 LocalScale { get; set; }

    void DetachAll(); // detaches all direct children
    // WorldPosition/WorldRotation/WorldScale stay planned.
}

public interface IEngine<TLayer> where TLayer : struct, Enum
{
    ITime Time { get; }
    IWindow Window { get; }
    IInput Input { get; }              // declaration/planned
    IAssets Assets { get; }
    ILoggerFactory LoggerFactory { get; }
    ISceneRoot Root { get; }

    ITransform CreateTransform(ITransform? parent = null);
    ICamera<TLayer> CreateCamera(ITransform parent);
    ISpriteRender<TLayer> CreateSpriteRender(ITransform parent);

    void Exit();
}
```

Also:

- `Bork2dException` for engine-originated startup/validation failures;
- `IGame.Teardown()` declaration;
- remove `IDisposable` from `ITransform`, `ICamera`, and `ISpriteRender`;
- remove `Dispose()`/`IsAlive` from scene entities;
- reference `Microsoft.Extensions.Logging.Abstractions` from `Api`;
- keep GPU/OS/Silk/Vulkan types out of `Api`.

Acceptance: API compiles, docs match, dependency test allows only the intended references.

### WP3 — Root-owned scene graph

Replace `EntityList<T>` and the weak registry with:

- `Transform` child storage (`List<Transform>`/array) and parent management;
- an internal `ISceneNode` structural contract shared by `Transform` and `SceneRoot` (parent
  node, local/world matrix, child-list operations); it is not public and exposes no graph or
  scene identity, entity ID, child enumeration, or traversal helper;
- `SceneRoot` implements `ISceneRoot` **directly** (it does **not** inherit `Transform`):
  - read-only: parent/local setters throw `InvalidOperationException`;
  - inherits `DetachAll()` from `ITransform` and implements it over its own child list;
  - root itself remains valid;
- `Transform` stores the parent as an internal scene node and exposes it as `ITransform`; only
  engine-owned nodes (root or transform) are accepted as parents;
- parent attach/detach:
  - remove from old parent, add to new parent;
  - reject self/cycles;
  - preserve world transform on re-parent;
  - root cannot be parented;
  - detach returns the object to normal (possibly unrooted) life, not a destroyed state;
- `ITransform.DetachAll()` detaches all direct children; any transform can own children;
- remove `Dispose`/`IsAlive`/disposed checks from scene entities;
- removing from a parent and re-adding later must preserve the object and its state;
- `CreateTransform/CreateCamera/CreateSpriteRender` attach to the supplied parent;
- `CreateCamera(null)`/`CreateSpriteRender(null)` fail validation;
- camera/sprite constructors do not self-register in a global list;
- snapshot discovery and capture are **not** implemented in M1; the tree is unordered and no
  entity IDs or ordered discovery exist (internal value-only snapshot structs land in M7);
- no concurrent dictionary and no entity ID allocator;
- all parent/child operations use one engine-owned global structural `System.Threading.Lock`
  (`Lock`), owned by `Engine` and shared by every node it creates; there is no `SceneGraph`
  context object;
- property access is per-object locked (`Lock`), with lock-free paths where practical;
  composite updates to one object still need game synchronization where atomicity matters;
- avoid `lock (object)`; use `Lock`, `Interlocked`, `Volatile`, and immutable snapshots;
- no thread-affinity assertions.

Acceptance tests:

1. Unparented transform/camera/sprite is collectible after dropping the last reference.
2. Rooted object persists after dropping the game reference and stays retained by the root.
3. Detached object is collectible after dropping the last reference.
4. Remove from parent, keep the reference, re-add later: same object, same state.
5. `Root.DetachAll()` detaches children; refs remain valid and can be re-parented.
6. `ITransform.DetachAll()` detaches any transform's direct children (not only root).
7. Root setters (parent/position/rotation/scale) throw.
8. Cycle/self-parent attempts throw; root parent assignment throws.
9. Re-parenting preserves world position/rotation/scale.
10. Concurrent operations from workers succeed under locks; composite same-object updates are
    documented as game-synchronized.
11. There are no entity IDs and no ordered discovery; the tree is unordered, and snapshot
    capture/order is deferred to M7/M8.

### WP4 — Builder and host skeleton

Add `Bork2dAppBuilder<TLayer>` and `Bork2dApp`:

- `WithGame(IGame<TLayer> game)`; instance only, non-null;
- `WithHeadless(int ticksPerSecond = 60)`, `WithOffscreen(width, height)`;
- `WithAssetsPack(string path)` (path validation only in M1);
- `WithLogging(Action<ILoggingBuilder>)`;
- window options (`WithTitle`, `WithSize`, `WithResizable`, `WithFullscreen`, `WithVSync`);
- `WithMaxDeltaTime`, `WithValidation`;
- `Build()` validation:
  - exactly one mode;
  - game present;
  - valid layer enum;
  - positive sizes/deltas;
  - headless tick rate `>= 0`;
  - no window options with headless/offscreen;
  - non-empty pack path when supplied;
- `Build()` creates the logger, options, engine, and `Root`; no window/GPU/threads;
- `Run()` **M1 smoke behavior**: calls `IGame.Setup`, runs exactly one `Tick`, calls
  `IGame.Teardown`, then returns. No window, renderer, or pack. M2/M7 replace this with the real
  loop.
- `Dispose()` is idempotent; detaches/clears the root, disposes the logger.
- No thread-affinity requirement on `Build`/`Run`/`Dispose`; OS-required work is marshaled by
  the platform host in later milestones.

### WP5 — Logging

- `IEngine.LoggerFactory` wired from the app.
- `WithLogging(Action<ILoggingBuilder>)`; default pretty ZLogger console.
- Categories: `Bork2d.Runtime`, `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render`.
- Startup logs the selected mode and validation outcome.
- AOT/trim smoke includes the default logging path.
- Exact provider version/format is settled during WP5.

### WP6 — NativeAOT and serialization policy

**Status:** implemented on `m-1/wp-6`.

Acceptance evidence:

- `Bork2d.Api`, `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render`, and `Bork2d.Runtime` set
  `IsAotCompatible`, `EnableTrimAnalyzer`, and `EnableAotAnalyzer`; no library sets
  `PublishAot`.
- `Bork2d.AotSmoke` (`PublishAot`, `InvariantGlobalization`, console) publishes with
  `dotnet publish ... -c Release -r win-x64` with zero AOT/trim warnings, runs to exit `0`, and
  exercises the default logging path plus one `Setup`/`Tick`/`Teardown` with a parented
  transform/camera/sprite and a cleared root.
- `Bork2d.Common` pins `Nerdbank.MessagePack` `1.4.95`, has an internal `[GenerateShape]` smoke
  type and a round-trip helper (no reflection resolver), and exposes it to `Bork2d.AotSmoke`
  via `InternalsVisibleTo`; `Bork2d.Tests` covers the round trip.
- AOT publish/run remains a required local gate and is not added to CI; full pack serialization
  is M3/M4.

Deliverables:

- `PublishAot`/`IsAotCompatible` on runtime/game projects.
- AOT/trim analyzers as errors.
- AOT smoke app: build a logger, builder, root, create parented objects, run the one-tick smoke,
  clear root, exit.
- Add Nerdbank.MessagePack package/policy and a generated-shape smoke type; full pack
  serialization is M3/M4.
- No runtime reflection, assembly scanning, TOML, or dynamic code in runtime projects.
- Editor/baker stay JIT and absent from the solution until M5.

### WP7 — Tests and CI

xUnit test groups:

- builder validation/mode exclusivity;
- scene ownership/lifetime/GC cases;
- remove/re-add and root clear/re-add;
- root read-only setters;
- transform hierarchy/parenting/cycles;
- layer validation and camera/sprite defaults;
- camera `ZIndex` default/round-trip and its documented ordering contract; ordering is applied
  in M8;
- concurrent operations from multiple threads;
- dependency matrix;
- AOT publish/start smoke;
- logging default smoke;
- formatting/style gate.

CI order: restore → format verify → build → tests. AOT publish/run is a required local gate,
not CI (human decision 2026-10-25); run it for runtime-affecting changes and before the final PR.

### WP8 — Documentation

Update same-change:

- `docs/api/reference.md`, `docs/api/guide.md`;
- `docs/architecture/overview.md`, `runtime-lifecycle.md`, `threading.md`, `nativeaot.md`,
  `projects.md`;
- `docs/architecture/scene-ownership.md`, `rendering.md`;
- `docs/decisions.md` (D-023/D-024/D-025/D-026 and the index);
- `docs/roadmap.md` (M0 complete, M1 implemented/final PR pending);
- root `README.md`, `docs/README.md`, `docs/contributing.md` status and commands.

## 5. Implementation order

1. WP1 build/quality baseline.
2. WP2 API contracts compile.
3. WP3 scene graph + ownership tests.
4. WP4 builder/host smoke + validation tests.
5. WP5 logging.
6. WP6 AOT/serialization policy + smoke.
7. WP7 CI/tests complete.
8. WP8 docs final pass.

WP1 and WP2 can overlap; WP3 must land before WP4 because the builder creates `Root`.

## 6. API semantics detail

### `ISceneRoot`

- Read-only marker over `ITransform`; identity transform at the origin (`Parent == null`,
  position zero, rotation zero, scale one).
- Setting `Parent`, `LocalPosition`, `LocalRotation`, or `LocalScale` throws
  `InvalidOperationException`.
- `ISceneRoot` adds no members. `DetachAll()` is inherited from `ITransform` and can be called
  on any transform, not just the root.
- `Root.DetachAll()` detaches the root's direct children; subtrees remain intact and can be
  re-attached.
- App-owned; app shutdown calls `Root.DetachAll()`.

### `ITransform.DetachAll()`

- Detaches **direct children**; grandchildren stay attached to their parents.
- Children become `Parent == null`; the object and state remain valid.
- Can be called on any transform (root or subtree) from any thread during the tick window.
- Runs under the engine-owned global structural lock.

### Parent changes

- `null` detaches; the object remains valid and can be re-attached.
- Setting a parent preserves world transform.
- Root as child is rejected.
- Repeated attach/detach does not recreate the object or reset its state.
- Concurrent structural operations are serialized by the engine-owned global structural lock
  (`System.Threading.Lock`, one instance owned by `Engine`).
- Concurrent property writes use per-object `Lock`; a multi-property update that must be atomic
  still needs game synchronization. Prefer lock-free/`Interlocked` paths where possible.

### Threading

- No `MainThread`/`TickThread` assertions in the public API.
- `IGame.Setup/Tick/Teardown` are invoked by the engine on its tick thread; user code may call
  the public API from any thread.
- Scene mutation is logically tick-window scoped for game code; the API does not enforce a
  particular calling thread.
- `IWindow` operations are marshaled to the OS/window thread; `IInput`/`ITime` reads are
  snapshots. `IAssets` already permits any thread.
- The renderer and asset threads remain internal; their internals are not public.

## 7. Out of scope for M1

- SDL window/input; actual Vulkan; render thread; snapshot ring; renderer.
- `assets.pck` reader/writer; asset loading; TOML baking.
- `Bork2d.Editor` project.
- Full game loop/mode startup; M1 `Run` is the one-tick smoke only.
- Public scene serialization, prefabs, or editor scene editing.
- Lifetime scopes (future complement to root ownership).

## 8. Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| API churn from `ITransform`/creator signatures | Medium | Freeze M1 contract; update docs/tests in same change. |
| Root traversal performance | Medium | Start simple; profile; add typed rooted caches only if needed. |
| Lost references to rooted objects | Medium | Scene membership is the lifetime; document remove/re-add. |
| Locking overhead/contention | Medium | Structural + per-object `Lock`s in M1; keep lock-free paths; profile/optimize later. |
| AOT vs logging/Nerdbank/Silk | High | Smoke from M1; pin versions; isolate adapters. |
| Style gate breaks existing code | Low | Format/repair before enabling CI gate. |
| M1 `Run` mistaken for a real loop | Medium | Document one-tick smoke; replace in M2/M7. |

## 9. Acceptance checklist

- [x] Build rules and `.editorconfig` active in all projects.
- [x] `dotnet format --verify-no-changes` passes.
- [x] Dependency test passes.
- [x] `ISceneRoot`/`IEngine.Root` implemented and read-only.
- [x] Parent-required creators implemented.
- [x] Remove/re-add preserves object/state.
- [x] `Root.DetachAll()` detaches; refs remain usable.
- [x] No `Dispose`/`IsAlive` on scene entities.
- [x] Lost-reference GC tests pass.
- [x] Root setter/cycle tests pass.
- [x] Camera `ZIndex` default/round-trip test passes; no entity IDs or ordered discovery exist.
- [x] Builder validation tests pass.
- [x] One-tick `Run` smoke passes.
- [x] ZLogger default + `IEngine.LoggerFactory` smoke passes.
- [x] AOT publish smoke runs with zero warnings.
- [x] No weak `EntityList` in shipped code.
- [x] Any-thread API smoke/tests pass.
- [x] Docs/API/decision log updated.
- [x] M0 marked complete; M1 progress table updated.

## 10. M1 open-question resolutions

The questions raised before implementation are resolved as follows; the M1 code and docs
reflect these choices.

| Question | Resolution |
| --- | --- |
| `DetachAll()` scope | **Direct children.** `Root.DetachAll()` removes the scene's top-level children; subtrees stay intact and remain re-attachable. |
| Per-object locking granularity | **One `System.Threading.Lock` per entity** for local property access. Prefer lock-free/`Interlocked` paths where practical; a composite multi-property update still needs game synchronization (D-024). |
| `IWindow` any-thread semantics | No thread affinity. Calls may originate on any thread during the tick window; window operations will be marshaled to the OS/window thread in M2. M1 has no window, so `IWindow`/`IInput` throw. |
| `CreateTransform(parent = null)` | **Optional.** A standalone transform is legal for math/temporary use; it is not retained unless parented. |
| Lifetime scopes | **Deferred.** Not part of M1; root ownership is the lifetime model. |
| ZLogger version/format | Pinned `ZLogger` `2.5.10` + `Microsoft.Extensions.Logging` `11.0.0-rc.1.26425.128`; plain-text console prefix with local time/milliseconds, level, and category; no colors; `Debug` minimum in Debug builds and `Information` in Release (D-021). |
| `WithAssetsPack` in M1 | **Path shape only** (non-empty; default `assets.pck`). No filesystem access; existence/parse/checksum enforcement lands with the M3 pack. |
| Rotation unit | **Degrees**, positive CCW, normalized to `[0, 360)` on assignment and getter; non-finite values throw. Internal matrix math stays in radians (D-025). |
| Scene identity/order | **No entity IDs, unordered tree, no ordered discovery, no public child enumeration** in M1 (D-026). Tests assert parent relationships, reference identity, and GC behavior, not IDs or order. |
| Structural locking | **One engine-owned global structural `System.Threading.Lock`** shared by every node the engine creates, plus per-object property locks. There is no `SceneGraph` holder (D-026). |
| Camera ordering | `ICamera<TLayer>.ZIndex` (int, default `0`) exists and round-trips in M1. Cameras apply in ascending `ZIndex` (higher later); ties are unspecified; the renderer applies it in M8 (D-026). |
| Snapshots | **Not in M1.** Internal value-only `CameraSnapshot`/`SpriteSnapshot` structs and capture after `Tick` are M7; no entity IDs and no live node references (D-026). |
| AOT and CI | AOT publish/run (`Bork2d.AotSmoke`, `win-x64`) is a **required local gate**. CI runs build/test/format only, on pull requests targeting `develop` and on pushes to `develop` (human decision 2026-10-25). |

## 11. Related documents

- [Scene ownership](../architecture/scene-ownership.md) — accepted model.
- [Runtime lifecycle](../architecture/runtime-lifecycle.md#11-entity-and-scene-ownership) —
  startup/shutdown.
- [Threading](../architecture/threading.md#2-tick-scoped-concurrency) — mutation window and locks.
- [API reference](../api/reference.md#2-iengine) — public surface.
- [Logging](../architecture/logging.md) — ZLogger.
- [NativeAOT](../architecture/nativeaot.md) — AOT constraints.
- [Roadmap](../roadmap.md#m1-foundation-and-nativeaot-baseline) — milestone context.
- [Decision D-023](../decisions.md#d-023-root-owned-scene-graph) — ownership decision.
- [Decision D-024](../decisions.md#d-024-thread-affinity-free-api) — threading decision.
- [Decision D-026](../decisions.md#d-026-no-entity-ids-unordered-tree-camera-zindex-internal-snapshots) —
  no entity IDs, unordered tree, camera `ZIndex`, internal snapshots.
