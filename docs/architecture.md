# Architecture

Bork2d separates the platform-free engine, the Vulkan renderer, the host that composes them, and
build-time asset tooling. The [README](../README.md) has the feature overview.

## Projects and dependency rules

```text
Bork2d.Tests ───> Bork2d.Engine, Bork2d.Render, Bork2d.Runtime, Bork2d.Bake

Bork2d.Runtime ─> Bork2d.Render ─> Bork2d.Engine
      └──────────────────────────────^
Bork2d.Bake ────────────────────────> Bork2d.Engine   (build time only)
```

| Project | Role |
| --- | --- |
| [Bork2d.Engine](../src/Bork2d/Bork2d.Engine) | Public contract and implementation: scene, time, input/platform seams, assets, pack reader/writer. No GPU, OS, or logging-provider dependency. |
| [Bork2d.Render](../src/Bork2d/Bork2d.Render) | Internal Vulkan implementation: device selection, swapchain/offscreen target, sprite pipeline, texture pages, snapshot handoff, capture. |
| [Bork2d.Runtime](../src/Bork2d/Bork2d.Runtime) | Public host: builder, modes, SDL window/input, Vulkan instance/surface, renderer composition, logging. |
| [Bork2d.Bake](../src/Bork2d/Bork2d.Bake) | Build-time JIT tooling: TOML binding, processors, texture decode, pack composition, incremental manifest, CLI. |
| [Bork2d.Tests](../src/Bork2d/Bork2d.Tests) | xUnit suite, including dependency-matrix and public-surface tests. |

The rules are test-enforced by
[DependencyMatrixTests.cs](../src/Bork2d/Bork2d.Tests/Architecture/DependencyMatrixTests.cs):
`Bork2d.Render` references only `Bork2d.Engine`, `Bork2d.Runtime` references only engine and render,
`Bork2d.Bake` references only engine, and no runtime project references bake tooling. The engine may
reference only the BCL and its four implementation packages (PolyType, Nerdbank.MessagePack,
Microsoft.Extensions.Logging.Abstractions, System.IO.Hashing).

## Threads

| Thread | Owns | Notes |
| --- | --- | --- |
| Tick (game) thread | `IGame` callbacks, scene mutation | The caller's thread. `Run` and all engine members may run on any thread. |
| Render thread | Vulkan device, swapchain/target, pipelines, texture pages, snapshot consumption | One thread per renderer; started by the host and joined before `Run` returns. |
| Window thread | SDL video, the window, the event pump, the Vulkan instance/surface | Window commands are queued and coalesced; getters read the last confirmed state. |
| Asset loader thread | Pack reads, decompression, MessagePack deserialization | One thread per app; load tasks complete asynchronously. |
| Bake thread | Source discovery, binding, processing, pack writes | Build time only; never present in the runtime process. |

No member requires the process main thread. Scene structure is serialized by one engine-owned lock;
per-object property access uses a per-object lock, with structural-then-object lock ordering.

## Frame handoff

Windowed and offscreen runs publish one snapshot per tick and render one GPU frame per consumed
snapshot:

1. The tick thread acquires a free slot from the three-slot ring. This blocks only when all three
   slots are occupied, which bounds run-ahead.
2. It freezes input, advances the clock, dispatches text, and calls `IGame.Tick`.
3. It walks the rooted scene once with an iterative, parent-first traversal that composes one world
   matrix and takes one per-object lock per node, and captures every camera and sprite renderable
   into the slot as value-only records.
4. It publishes the slot as the newest snapshot. A newer publish supersedes the previous `Ready`
   slot, never a `Rendering` one.
5. The render thread takes the latest `Ready` snapshot, plans the frame, records it, and releases
   the slot when that GPU frame's fence signals.

The renderer never repeats or re-presents the previous snapshot. A tick that requests `Exit` still
publishes its snapshot, so the final frame can be drawn. Headless mode has no renderer, ring, or
snapshot; it only runs the paced game loop.

## Host modes

| Mode | Window / input | Renderer | Loop |
| --- | --- | --- | --- |
| Windowed (default) | SDL window; input available | Vulkan swapchain | Backpressured by the ring; stops on `Exit`, close, renderer fatal, or platform fatal. |
| Offscreen | None | Fixed sRGB Vulkan target; frames discarded | Same handoff loop; stops on `Exit` or renderer fatal. |
| Headless | None | None, no GPU or SDL | Absolute-deadline paced loop with a synthesized clock; stops on `Exit`. |

## Assets and tooling

Assets are built ahead of time, not loaded from loose files at runtime:

1. A game-owned assets executable configures `BakeApplicationBuilder` and registers data types and
   processors.
2. The bake discovers sources under ordered pack roots, binds TOML, runs processors, validates
   references, and writes one or more packs atomically.
3. The runtime mounts the base pack (and any later overlays), resolving a canonical path to the
   winning entry.
4. The asset loader thread decompresses and deserializes on demand.
5. The render thread prefetches texture descriptors/payloads through `IAssets` and uploads them to
   bindless texture pages without blocking on a load.

## Invariants

- **Scene ownership.** The root is a read-only identity transform. Parents hold children strongly;
  scene entities have no `Dispose` or liveness flag. Detach with `Parent = null` or `DetachAll()`
  and the detached node keeps its world transform; re-attach the same object later.
- **Coordinates.** World space is +X right, +Y up, in absolute world units. `Camera.Size` is the
  half visible height; viewports are normalized with a top-left origin.
- **Layers.** A layer type is an `int`-backed `[Flags]` enum with 1-32 named single-bit values
  (`None = 0` allowed). A sprite uses exactly one bit; a camera culls with a mask.
- **Snapshots.** Capture contains no live scene entities. The only object reference that crosses
  the tick/render boundary is immutable sprite render data, frozen from the loaded `SpriteAsset`
  when it was assigned; the mutable asset itself is never captured. Reusing a slot clears its
  previous camera and sprite ranges, so it retains no obsolete render data.
- **AOT.** Generated shapes are required; there is no reflection-shape fallback or runtime TOML.
  Runtime projects enable trim and AOT analyzers.
- **Tooling isolation.** The bake tooling, TOML parser, image decoder, and shader compiler never
  enter a runtime project's output or publish graph.

## Quality gates

- `dotnet build` treats warnings and C# style violations as errors.
- `dotnet test` includes dependency-matrix, public-surface, handoff, and GPU/window seams.
- `dotnet format --verify-no-changes` enforces formatting.
- [`scripts/check-docs.ps1`](../scripts/check-docs.ps1) verifies local Markdown links and anchors.
- The Pong desktop app is the local NativeAOT publish gate.
