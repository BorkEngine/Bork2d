# Scene Ownership

> **Status:** Current for the M1 scene graph in `Bork2d.Common`. Root-owned strong ownership
> replaced the scaffold's weak-reference registry. Public members are listed in the
> [API reference](../api/reference.md#2-ienginetlayer).

## 1. Model

`IEngine.Root` (`ISceneRoot`) is the root of one live scene tree.

- A parent holds its children **strongly**; the engine holds `Root` and no global
  camera/sprite/transform lists.
- An object is retained while reachable from `Root` or from game code. An unparented object is
  collectible once the game drops its last reference.
- There is no `Dispose()`/`IsAlive` on scene objects. Detach with `Parent = null`; re-attach the
  same instance later with its state intact.
- `CreateCamera(parent)` and `CreateSpriteRender(parent)` require a non-null engine-owned parent,
  so membership is decided at creation; `Root` is the common parent. `CreateTransform(null)`
  returns a standalone transform: valid for math, not rendered, and not retained unless
  parented.
- A newly created object attaches with default local values (position zero, rotation zero, scale
  one) at the parent's origin. Assigning `Parent` afterwards preserves the world transform.
- `ITransform.DetachAll()` detaches direct children; `Root.DetachAll()` detaches the scene's
  top-level children. Detached objects stay valid and can be re-parented; grandchildren remain
  attached to their parents.
- `ISceneRoot` is **read-only**: the `Parent`, `LocalPosition`, `LocalRotation`, and `LocalScale`
  setters throw `InvalidOperationException`; the root is the identity transform at the origin and
  cannot be parented.
- There is no auto-parenting to `Root`. Implicit global ownership was rejected because a lost
  reference would leak, and a weak registry would make scene lifetime nondeterministic.

The root is a dedicated node implementing `ISceneRoot` directly; it is not a transform entity.
Root and transforms share only the internal `ISceneNode` structural contract (child list,
local/world matrix, structural lock). `ISceneNode` and the concrete root type are internal, so
callers treat `Root` as `ISceneRoot`/`ITransform` and must not assume a concrete type.

## 2. Parent rules

| Assignment | Result |
| --- | --- |
| `Parent = null` | Detach; local and world state retained. |
| Engine node in the same scene | Re-parent; the world matrix is preserved exactly. |
| Node from a different engine scene | `Bork2dException`. |
| `ITransform` not created by an engine | `Bork2dException`. |
| Self or a descendant (cycle) | `InvalidOperationException`. |
| `Root.Parent = ...` | `InvalidOperationException`; the root cannot be parented. |
| Parent with a singular world matrix | `Bork2dException`; the world transform cannot be preserved. |

- At creation, a non-null camera/sprite parent that is not engine-owned, or belongs to a
  different engine scene, throws `Bork2dException`; a null camera/sprite parent throws
  `ArgumentNullException`.
- Re-parenting assigns the exact affine local matrix derived from the old world matrix and the
  inverse of the new parent's world matrix, so shear from a non-uniform scale combined with
  rotation survives.
- `LocalPosition` changes only the matrix translation. `LocalRotation` and `LocalScale`
  recompose from the authored scalars, so writing either discards re-parent shear.
- Local position/rotation/scale are an authored scalar view over that matrix: negative scale axes
  round-trip, and rotation is degrees normalized to `[0, 360)` on get and set (`-90` → `270`,
  `360`/`720` → `0`). A non-finite rotation throws `ArgumentOutOfRangeException`; matrix math
  uses radians.
- The tree is **unordered**: no entity IDs, no ordered discovery, no public child enumeration or
  traversal helper. Snapshot capture and render order are M7/M8 (D-026).

## 3. Lifetime cases

| Case | Result |
| --- | --- |
| Create camera/sprite/transform, never parent, lose reference | Collectible by GC. |
| Parent under `Root`, lose game reference | Remains part of the scene while rooted. |
| Parent under a non-rooted transform | Not part of the scene; collectible when unreachable. |
| Remove from parent (`Parent = null`), lose reference | Collectible by GC. |
| Remove from parent, keep reference, re-add later | Same object and state; works normally. |
| `Root.DetachAll()` | All rooted children are detached; references remain valid and can be re-parented. |
| Clear root, lose reference | Collectible by GC. |
| Keep a reference across `Root.DetachAll()` | Still valid; re-attach when needed. |

A rooted object whose game reference is lost is deliberately kept: strong ownership cannot
distinguish an accidental lost reference from a scene object. Removing it from the tree is the
explicit release; `Root.DetachAll()` bounds the whole scene.

## 4. Renderer and snapshot relationship

The renderer never walks the live tree. After `Tick` (workers joined) the engine traverses the
rooted tree in no specified order and builds internal **value-only** `CameraSnapshot` /
`SpriteSnapshot` structs; no `Transform`/`Camera`/`SpriteRender` reference crosses the boundary.
The structs are internal, not part of `Bork2d.Api`, and not implemented in M1: capture is M7 and
camera ordering is M8. Details are in [Threading](threading.md#3-frame-snapshot).

- Cameras are applied in ascending `ZIndex`; higher draws later, default `0`, ties unspecified.
- Sprites are sorted by `(Layer, ZIndex)`; ties unspecified.
- A renderable detached during `Tick` is absent from the snapshot published for that tick; a
  sprite whose asset is not ready is captured and skipped by the renderer.
- Scene mutation is logically tick-window scoped (`Setup`/`Tick`/`Teardown`); the API itself has
  no thread affinity and engine locks make concurrent calls safe.

## 5. Performance

- Parent add/remove: amortized O(1) with a child list and swap-back removal under the structural
  lock.
- Snapshot traversal (`Planned`, M7): O(n) over live rooted nodes, with no weak-reference checks
  and no finalizers.
- Root detach: O(n) over direct children, with no object destruction.
- Allocation-free traversal once child lists reach capacity (`Planned`).
- World matrices are evaluated on demand by composing the local matrix through the parent chain;
  caching unchanged subtrees behind dirty flags is a planned optimization.

Start with one root traversal per capture. If discovery profiles hot, `Root` may maintain typed
rooted lists updated on attach/detach; that remains scene-scoped ownership and does not change
the leak semantics.

## 6. Related documents

- [API reference](../api/reference.md#2-ienginetlayer) — exact public members.
- [Runtime lifecycle](runtime-lifecycle.md#1-public-startup-contract) — builder, startup,
  shutdown.
- [Threading](threading.md#2-tick-scoped-concurrency) — locking and the tick window.
- [Decision D-023](../decisions.md) — decision record.
- [Decision D-026](../decisions.md) — no entity IDs, unordered tree, camera `ZIndex`.
