# Scene Ownership

> **Status:** accepted for M1; implementation planned. This replaces the scaffold's
> weak-reference registry. See the [M1 implementation spec](../specs/m1-foundation.md).

## 1. Problem

A global strong registry would retain every created camera, sprite, and transform until app
shutdown. Since there is no per-object `Dispose()` requirement, a developer who loses a local
reference would leak the object permanently.

A weak registry avoids the leak by collecting unreferenced objects, but makes gameplay lifetime
nondeterministic: a camera or renderable can disappear at an arbitrary GC point. That is worse
for a game engine than an explicit lifetime.

The chosen model is **scene-rooted strong ownership**. Lifetime is deterministic and bounded by
scene membership, not by the process or the GC.

## 2. Model

- `IEngine.Root` is an `ISceneRoot`: the root of the live scene graph.
- A parent transform holds its children **strongly**.
- The engine holds `Root`, but does **not** hold global camera/sprite/transform lists.
- An object is retained only while it is reachable from `Root` (or from game code).
- An unparented object is not retained by the engine. If the game drops its last reference, it
  is collectible.
- There is **no `Dispose()` and no `IsAlive` state on scene objects**. Removing an object from
  its parent removes it from the scene; adding it back returns the same object, with its state,
  without recreating it.
- `CreateCamera(parent)` and `CreateSpriteRender(parent)` require a parent, so the developer
  makes the membership decision at creation. `engine.Root` is the common parent.
- `CreateTransform(parent = null)` may be standalone for math or temporary use; it is not
  rendered and is not retained unless parented.
- Detaching (`Parent = null`) removes the object from its parent. If no game reference remains,
  it is collectible.
- `ITransform.DetachAll()` detaches a transform's direct children. `Root.DetachAll()` detaches
  the scene's top-level children; objects remain valid and can be re-parented later. Any
  `ITransform` can have children and expose `DetachAll()`.
- `ISceneRoot` is **read-only**: setting its parent, local position, rotation, or scale throws
  `InvalidOperationException`. The root is an identity transform at the origin.
- There is no auto-parenting of created objects. Auto-parenting everything to the root would
  recreate the permanent-registry leak with extra indirection.

## 3. Public API

```csharp
public interface ISceneRoot : ITransform
{
    // Read-only root marker; no extra members.
}

public interface ITransform
{
    ITransform? Parent { get; set; }
    Vector2 LocalPosition { get; set; }
    float LocalRotation { get; set; }
    Vector2 LocalScale { get; set; }

    void DetachAll(); // detaches direct children
    // WorldPosition/WorldRotation/WorldScale: planned, read-only
}

public interface IEngine<TLayer> where TLayer : struct, Enum
{
    ISceneRoot Root { get; }

    ITransform CreateTransform(ITransform? parent = null);
    ICamera<TLayer> CreateCamera(ITransform parent);
    ISpriteRender<TLayer> CreateSpriteRender(ITransform parent);
}
```

`ICamera<TLayer>` and `ISpriteRender<TLayer>` remain transforms; they can be parented anywhere
in the rooted tree, not only directly under `Root`.

Rules:

- `Root.Parent` is always `null`; assigning a parent throws.
- Setting any local transform value on `Root` throws `InvalidOperationException`.
- Setting `Parent` on a normal transform rejects cycles, including self-parenting.
- Re-parenting preserves the world transform and recomputes local values.
- Removing a child (`Parent = null`) does not clear its local/world state.
- Re-adding a removed object to a parent restores it without creating a new object.
- `ITransform.DetachAll()` detaches direct children; their `Parent` becomes `null`.
  Grandchildren stay attached to those children.
- Any `ITransform` can have children; `DetachAll()` is not root-only.
- Entity IDs are monotonic and not reused. Snapshot/render order remains entity-ID based.

## 4. Lifetime cases

| Case | Result |
| --- | --- |
| Create camera/sprite/transform, never parent, lose reference | Collectible by GC. |
| Parent under `Root`, lose game reference | Remains part of the scene while rooted. |
| Parent under a non-rooted transform | Not part of the scene; collectible when unreachable. |
| Remove from parent (`Parent = null`), lose reference | Collectible by GC. |
| Remove from parent, keep reference, re-add later | Same object and state; works normally. |
| `Root.DetachAll()` | All rooted children are detached; refs remain valid and can be re-parented. |
| Clear root, lose reference | Collectible by GC. |
| Keep a reference across `Root.DetachAll()` | Still valid; re-attach when needed. |

The second row is deliberate: a rooted object is a scene object. No strong-ownership model can
distinguish "the game lost the reference accidentally" from "this should stay alive." Removing
it from the scene is the explicit release; `Root.DetachAll()` bounds the whole scene.

## 5. Renderer and snapshot relationship

- Snapshot capture walks the rooted tree and collects cameras, sprite renderables, and
  transforms. The renderer never walks the live tree.
- Cameras and sprites are sorted by entity ID before snapshot output, preserving the existing
  ordering contract.
- Renderers copy the values they need. Holding a game reference to a live entity never gives
  the render thread access to it.
- Scene mutation is logically tick-window scoped: `Setup`, `Tick` (including joined workers), or
  `Teardown`. The API itself has no thread affinity; engine locks make concurrent calls safe.
- Structural attach/detach uses a scene-graph structural `System.Threading.Lock`. Individual
  property access uses per-object `Lock`s; prefer `Interlocked`/immutable paths where practical.
  A logical multi-property update to one object still needs game synchronization if it must be
  atomic.

## 6. Performance

The root tree is expected to be cheaper than the current weak dictionary:

- parent add/remove: amortized O(1) with a child list and swap-back removal, under the
  structural `System.Threading.Lock`;
- snapshot traversal: O(n) over live rooted transforms, with no weak-reference checks and no
  GC finalizers;
- world transforms: dirty flags; update only changed subtrees, not every node every frame;
- scene detach: O(n) for root's direct children; no object destruction;
- allocation-free traversal once child lists reach capacity.
- Prefer lock-free paths (`Interlocked`/`Volatile`/immutable snapshots); locks are only for
  structural changes and short per-object mutations.

Start with a single root traversal per capture. If profiling shows camera/renderable discovery
is hot, `Root` can maintain typed rooted lists updated on attach/detach. That is still
scene-scoped ownership and does not change the leak semantics.

## 7. Alternatives considered

| Model | Why not |
| --- | --- |
| Global strong registry | Permanent leak if the developer loses a reference. |
| Weak registry (scaffold) | Nondeterministic disappearance; unpredictable camera/renderable lifetime. |
| Finalizer-backed owner handles | Eventual cleanup, but GC-timing-dependent and objects can render after being forgotten. |
| `Dispose`/`IsAlive` on every entity | Reintroduces a destroy lifecycle and stale-reference checks; removing/adding is simpler. |
| Arena/generational IDs | Viable, but a large API rewrite; a lost ID still leaves a scene object until removal. |
| Auto-parent everything to `Root` | Recreates permanent ownership; losing a reference still leaks. |

## 8. M1 implementation tasks

1. Replace `EntityList<T>` with a rooted transform tree.
2. Add `ISceneRoot`, `IEngine.Root`, and read-only root setters.
3. Change `CreateTransform`/`CreateCamera`/`CreateSpriteRender` to the parent-aware signatures.
4. Implement parent add/remove, cycle rejection, `Root.DetachAll()` detach, and re-attach.
5. Capture cameras/sprites by traversing the rooted tree; remove weak registries.
6. Add ownership/lifetime tests, including remove/re-add, root clear/re-add, lost-reference/GC,
   root read-only setters, cycles, and concurrent distinct-entity operations.
7. Remove `IDisposable`/`Dispose`/`IsAlive` from scene entities.
8. Update API docs, threading notes, and the M1 spec.

## 9. Related documents

- [M1 implementation spec](../specs/m1-foundation.md) — work breakdown and acceptance.
- [Runtime lifecycle](runtime-lifecycle.md#11-entity-and-scene-ownership) — startup/shutdown.
- [Threading](threading.md#2-tick-scoped-concurrency) — mutation and locking rules.
- [API reference](../api/reference.md#2-iengine) — exact public members.
- [Decision D-023](../decisions.md#d-023-root-owned-scene-graph) — decision record.
