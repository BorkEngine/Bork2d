# Decision Log

> Lightweight ADRs. **Accepted** = in force; **Proposed** = needs sign-off before the relevant
> milestone; **Superseded** = replaced by a later entry. Last review: 2026-10-25.
> Unresolved choices are tracked in [Roadmap §9](roadmap.md#9-open-decisions), not here.

## Decisions

| ID | Status | Decision |
| --- | --- | --- |
| D-001 | Accepted | Game code references only `Bork2d.Api` and `Bork2d.Runtime`; `Common`, `Assets`, and `Render` stay internal behind `InternalsVisibleTo`; `Editor` is a separate JIT tool surface. |
| D-002 | Accepted | A tick thread owns callbacks, time, and snapshot publication; a render thread owns Vulkan in windowed/offscreen; asset loaders are background threads. The public API has no main-thread affinity. |
| D-003 | Accepted | Three snapshot slots (`Free → Writing → Ready → Rendering → Free`); the tick runs ahead while slots are free and waits only when the ring is full. Newest `Ready` wins; no target tick rate or `Sleep` in windowed/offscreen. |
| D-004 | Accepted | Variable timestep: `DeltaTime` is wall-clock between tick starts, clamped by `MaxDeltaTime` (default 0.25 s). No accumulator or render interpolation; headless is timer-paced. |
| D-005 | Accepted | v1 targets Windows x64; SDL/Vulkan boundaries stay portable; Linux/macOS are not v1 guarantees. |
| D-006 | Accepted | `TLayer` is an `int`-backed `[Flags]` enum: one non-zero bit per renderable, masks on cameras, at most 32 flags, validated once at `Build()`. |
| D-007 | Accepted | Each non-empty camera produces one instanced draw; there is no global one-draw rule. Camera order is `ZIndex` (D-026). |
| D-008 | Accepted | Abstract units, +X right/+Y up; `Camera.Size` is the half visible height; each sprite asset carries pixels-per-unit scale; the shader generates quad corners; no vertices array. Rotation rules are D-025. |
| D-009 | Accepted | Vulkan 1.3 + SDL2/Silk.NET, one pipeline of instanced quads, bindless texture pages, dynamic rendering, no depth/MSAA/HDR; sort layer then `ZIndex`; straight alpha. |
| D-010 | Accepted | Runtime assets live in `assets.pck`: 28-byte prefix, always-compressed and XXH3-64-checksummed MessagePack header, uint64 offsets. Later `LoadPack` overlays win; no unload; a missing base pack is fatal. |
| D-011 | Accepted | `Bork2d.Editor` is an Avalonia JIT library/app that slices textures and edits source data; no Vulkan preview and no scene editing. |
| D-012 | Accepted | TOML is the source format; a JIT baker emits MessagePack plus raw payloads. The NativeAOT runtime reads MessagePack only, using Nerdbank generated shapes. |
| D-013 | Accepted | Game solution: `Project.Common` (Api), `Project.Desktop` (Runtime + Common), `Project.Editor` (Editor + Common); registrations are compile-time, never assembly scanning. |
| D-014 | Proposed | Breaking changes are allowed until the v1 API freeze (M10); each updates the guide, reference, and this log. |
| D-015 | Accepted | Scene mutation happens only inside `Setup`/`Tick`/`Teardown`. Workers may create/mutate distinct entities in parallel and must join before `Tick` returns; composite same-entity updates are game-synchronized. |
| D-016 | Accepted | Runtime libraries are NativeAOT/trim-safe: no runtime reflection, TOML, or dynamic code; Nerdbank shapes are generated. Editor and baker stay JIT. |
| D-017 | Accepted | Two non-windowed modes: `WithHeadless(ticksPerSecond = 60, 0 = uncapped)` and `WithOffscreen(w, h)`; mutually exclusive with each other and with window options. |
| D-018 | Accepted | `WithGame(instance)` takes a caller-constructed game; no `new()` constraint or `Activator`. The caller keeps it alive through `Run()`. |
| D-019 | Accepted | Textures use a MessagePack descriptor plus raw RGBA8 payload; sprites are MessagePack assets with texture path, UVs, pixels-per-unit scale, normalized pivot, topology, and optional indices. Source is TOML. |
| D-020 | Accepted | Editor extensions use compile-time registrations: `WithProject`, `WithAssetType<T>()`, `WithWindow<T>()`/instance. No assembly scanning. |
| D-021 | Accepted | Microsoft.Extensions.Logging + ZLogger; `IEngine.LoggerFactory`; `WithLogging`; pretty plain-text console by default. Versions are pinned. |
| D-022 | Accepted | Warnings-as-errors, `EnforceCodeStyleInBuild`, enforced style rules, `dotnet format --verify-no-changes`, and AOT/trim analyzers on every project. |
| D-023 | Accepted | `IEngine.Root` strongly owns an unordered scene tree. Cameras/sprites require a parent; entities have no `Dispose`/`IsAlive`; detach and re-attach the same object; no weak registry. |
| D-024 | Accepted | No public API requires the process main thread. One engine-owned structural `System.Threading.Lock` serializes parent changes; per-object `Lock`s protect properties. |
| D-025 | Accepted | Public rotation is degrees, positive counter-clockwise, normalized to `[0, 360)` on set and get; non-finite values throw. Internal matrix math uses radians. |
| D-026 | Accepted | The scene is unordered: no entity IDs, ordered discovery, or child enumeration. Cameras apply in ascending `ZIndex`; ties are unspecified. Snapshots are internal value-only structs (M7). |

## Detail: implemented and public-contract decisions

### D-001 Public surface: Api + Runtime

- **Decision:** Only `Bork2d.Api` and `Bork2d.Runtime` are supported for game code.
  `Common`/`Assets`/`Render` are internal; `Runtime` is the composition root.
- **Consequences:** Small contract; internals can change. A public type in an internal project is
  a bug unless intentionally re-exported.
- **Docs:** [Projects and dependencies](architecture/projects.md#3-public-surface-and-assembly-visibility)

### D-003 Bounded run-ahead snapshot ring

- **Decision:** Three slots; the tick holds a slot until the GPU frame fence, and the renderer
  takes the newest `Ready` snapshot. Superseded snapshots are dropped.
- **Consequences:** Tick rate follows the renderer/GPU rate within the ring depth. Windowed and
  offscreen never sleep; headless is the only timer-paced mode.
- **Docs:** [Threading](architecture/threading.md#4-triple-buffer-handoff)

### D-006 Layer flags and culling masks

- **Decision:** A renderable has exactly one non-zero bit; a camera has a mask. `None = 0` is
  allowed; composites are rejected at `Build()`. Maximum 32 flags.
- **Consequences:** The renderer works on `uint` masks; invalid enum shapes fail fast.
- **Docs:** [API guide](api/guide.md#4-layers), [Rendering](architecture/rendering.md#8-sorting-layers-and-clears)

### D-009 Vulkan 1.3, instancing, bindless, dynamic rendering

- **Decision:** One pipeline draws instanced generated quads; texture pages are selected by a
  descriptor-indexed bindless array; swapchain rendering uses dynamic rendering.
- **Consequences:** Draw count scales with non-empty cameras, not sprites. Device must support
  descriptor indexing; transparency needs explicit ordering. Indexed non-Quad drawing is not in
  v1.
- **Docs:** [Rendering](architecture/rendering.md)

### D-018 Game instance injection

- **Decision:** `WithGame(IGame<TLayer>)` accepts a caller-constructed instance, validates
  non-null, is called exactly once, and the app owns the run lifetime.
- **Consequences:** Constructor injection works and is AOT-safe; the caller must keep the game
  alive through `Run()`.
- **Docs:** [Runtime lifecycle](architecture/runtime-lifecycle.md#1-public-startup-contract)

### D-021 ZLogger logging surface

- **Decision:** MEL + ZLogger. `IEngine.LoggerFactory` is the public seam; `WithLogging`
  configures providers and suppresses the default pretty console provider.
- **Consequences:** A default run is readable without configuration. The app owns/disposes the
  factory and provider instances added with `AddProvider`. Pinned in M1: `ZLogger` 2.5.10,
  `Microsoft.Extensions.Logging` 11.0.0-rc.1.26425.128.
- **Docs:** [Logging](architecture/logging.md)

### D-022 Warnings-as-errors and C# style gate

- **Decision:** All projects use `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, enforced
  style rules, `dotnet format --verify-no-changes`, and AOT/trim analyzers on runtime projects.
- **Consequences:** Style violations fail the build. Suppressions need a narrow scope and a
  recorded justification.
- **Docs:** [Contributing](contributing.md#3-build-style-and-warnings)

### D-023 Root-owned scene graph

- **Decision:** `IEngine.Root` (`ISceneRoot`) strongly owns an unordered transform tree.
  `CreateCamera(parent)` and `CreateSpriteRender(parent)` require an engine-owned parent;
  `CreateTransform(parent = null)` is standalone. Remove with `Parent = null`; re-attach the same
  object later. No `Dispose`/`IsAlive`, weak registry, or global entity list.
- **Consequences:** Lifetime is bounded by scene membership and is deterministic. A rooted object
  whose game reference is lost persists until detached.
- **Docs:** [Scene ownership](architecture/scene-ownership.md)

### D-024 Thread-affinity-free API

- **Decision:** No member requires the process main thread. Calls may originate on any thread
  during `Setup`/`Tick`/`Teardown`; one structural `System.Threading.Lock` serializes parent
  changes, per-object `Lock`s protect properties.
- **Consequences:** Workers may mutate distinct entities concurrently and must join before
  `Tick` returns. Composite multi-property updates remain the game's synchronization
  responsibility.
- **Docs:** [Threading](architecture/threading.md#2-tick-scoped-concurrency)

### D-025 Degrees and normalized rotation

- **Decision:** `LocalRotation` is degrees, positive counter-clockwise, normalized to `[0, 360)`
  on assignment and on read. `-90` becomes `270`; `360` and `720` become `0`. Non-finite values
  throw `ArgumentOutOfRangeException`.
- **Consequences:** Authored values outside the range normalize rather than round-trip. Internal
  matrix math converts at the boundary only.
- **Docs:** [API reference](api/reference.md#10-itransform), [Rendering](architecture/rendering.md#7-coordinates-camera-and-projection)

### D-026 No entity IDs, unordered tree, camera `ZIndex`, internal snapshots

- **Decision:** No entity IDs or ordered discovery. One engine-owned structural lock. Cameras
  apply in ascending `ZIndex` (default 0); sprites sort by `(Layer, ZIndex)`; ties are
  unspecified. Snapshots are internal value-only structs (M7), not part of `Bork2d.Api`.
- **Consequences:** Games must assign distinct keys where order matters. Tests assert
  relationships and identity, never IDs or order.
- **Docs:** [Scene ownership](architecture/scene-ownership.md), [Threading](architecture/threading.md#3-frame-snapshot), [API reference](api/reference.md)

## Superseding a decision

Add a new entry, mark the old one `Superseded by D-XXX`, and update every linked document in the
same change. Do not delete history.
