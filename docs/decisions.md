# Decision Log

> Lightweight ADRs. **Accepted** = in force; **Proposed** = needs sign-off before the relevant
> milestone; **Superseded** = replaced by a later entry. Last review: 2026-10-26.
> Unresolved choices are tracked in [Roadmap §9](roadmap.md#9-open-decisions), not here.

## Decisions

| ID | Status | Decision |
| --- | --- | --- |
| D-001 | Accepted | Game code references only `Bork2d.Api` and `Bork2d.Runtime`; `Common`, `Assets`, and `Render` stay internal behind `InternalsVisibleTo`. `Bork2d.Assets` is public for the M3 pack-tooling surface only (`PackBuilder`, `PackCompressionMode`, now in `Bork2d.Assets.Pack` — D-034); `Editor` is a separate JIT tool surface. |
| D-002 | Accepted | A tick thread owns callbacks, time, and snapshot publication; a render thread owns Vulkan in windowed/offscreen; asset loaders are background threads. The public API has no main-thread affinity. |
| D-003 | Accepted | Three snapshot slots (`Free → Writing → Ready → Rendering → Free`); the tick runs ahead while slots are free and waits only when the ring is full. Newest `Ready` wins; no target tick rate or `Sleep` in windowed/offscreen. |
| D-004 | Accepted | Variable timestep: `DeltaTime` is wall-clock between tick starts, clamped by `MaxDeltaTime` (default 0.25 s). No accumulator or render interpolation; headless is timer-paced. |
| D-005 | Accepted | v1 targets Windows x64; SDL/Vulkan boundaries stay portable; Linux/macOS are not v1 guarantees. |
| D-006 | Accepted | `TLayer` is an `int`-backed `[Flags]` enum: one non-zero bit per renderable, masks on cameras, at most 32 flags, validated once at `Build()`. |
| D-007 | Accepted | Each non-empty camera produces one instanced draw; there is no global one-draw rule. Camera order is `ZIndex` (D-026). |
| D-008 | Accepted | Abstract units, +X right/+Y up; `Camera.Size` is the half visible height; each sprite asset carries pixels-per-unit scale; the shader generates quad corners; no vertices array. Rotation rules are D-025. |
| D-009 | Accepted | Vulkan 1.3 + SDL2/Silk.NET, one pipeline of instanced quads, bindless texture pages, dynamic rendering, no depth/MSAA/HDR; sort layer then `ZIndex`; straight alpha. |
| D-010 | Accepted | Runtime assets live in `assets.pck`: 28-byte prefix, always-compressed and XXH3-64-checksummed MessagePack header, `uint64` offsets, `uint32` entry length/size. Later `LoadPack` overlays win; no unload; a missing base pack is fatal at `Run()`. M3 adds no per-asset version and no policy size caps (intrinsic/streaming bounds only). |
| D-011 | Accepted | `Bork2d.Editor` is an Avalonia JIT library/app that slices textures and edits source data; no Vulkan preview and no scene editing. |
| D-012 | Accepted | TOML is the source format; a JIT baker emits MessagePack plus raw payloads. The NativeAOT runtime reads MessagePack only, using Nerdbank generated shapes. |
| D-013 | Accepted | Game solution: `Project.Common` (Api), `Project.Desktop` (Runtime + Common), `Project.Editor` (Editor + Common); registrations are compile-time, never assembly scanning. |
| D-014 | Proposed | Breaking changes are allowed until the v1 API freeze (M10); each updates the guide, reference, and this log. |
| D-015 | Accepted | Scene mutation happens only inside `Setup`/`Tick`/`Teardown`. Workers may create/mutate distinct entities in parallel and must join before `Tick` returns; composite same-entity updates are game-synchronized. |
| D-016 | Accepted | Runtime libraries are NativeAOT/trim-safe: no runtime reflection, TOML, or dynamic code; Nerdbank shapes are generated. Editor and baker stay JIT. |
| D-017 | Accepted | Two non-windowed modes: `WithHeadless(ticksPerSecond = 60, 0 = uncapped)` and `WithOffscreen(w, h)`; mutually exclusive with each other and with window options. |
| D-018 | Accepted | `WithGame(instance)` takes a caller-constructed game; no `new()` constraint or `Activator`. The caller keeps it alive through `Run()`. |
| D-019 | Accepted | Textures use a MessagePack descriptor plus raw RGBA8 payload; sprites are MessagePack assets with texture path, UVs, pixels-per-unit scale, normalized pivot, topology, and optional indices. Source is TOML. M3 validates descriptors/sprites and loads sprite + descriptor CPU metadata; raw payload loading/upload is M8. |
| D-020 | Accepted | Editor extensions use compile-time registrations: `WithProject`, `WithAssetType<T>()`, `WithWindow<T>()`/instance. No assembly scanning. |
| D-021 | Accepted | Microsoft.Extensions.Logging + ZLogger; `IEngine.LoggerFactory`; `WithLogging`; pretty plain-text console by default. Versions are pinned. |
| D-022 | Accepted | Warnings-as-errors, `EnforceCodeStyleInBuild`, enforced style rules, `dotnet format --verify-no-changes`, and AOT/trim analyzers on every project. |
| D-023 | Accepted | `IEngine.Root` strongly owns an unordered scene tree. Cameras/sprites require a parent; entities have no `Dispose`/`IsAlive`; detach and re-attach the same object; no weak registry. |
| D-024 | Accepted | No public API requires the process main thread. One engine-owned structural `System.Threading.Lock` serializes parent changes; per-object `Lock`s protect properties. |
| D-025 | Accepted | Public rotation is degrees, positive counter-clockwise, normalized to `[0, 360)` on set and get; non-finite values throw. Internal matrix math uses radians. |
| D-026 | Accepted | The scene is unordered: no entity IDs, ordered discovery, or child enumeration. Cameras apply in ascending `ZIndex`; ties are unspecified. Snapshots are internal value-only structs (M7). |
| D-027 | Accepted | Windowed mode runs one engine-owned OS/window thread that owns SDL; `IWindow` setters enqueue commands and getters read the last confirmed snapshot. Extends D-024 to the platform. |
| D-028 | Accepted | The M2 real loop is windowed only: a visible window before `Setup`, an uncapped `Setup` → `Tick`… → `Teardown`, stop on `Exit`/close/fatal, and destruction after `Teardown`. Headless/offscreen stay one-tick. |
| D-029 | Accepted | Sizes and mouse input are physical framebuffer pixels (per-monitor-v2 DPI awareness plus `SDL_WINDOWS_DPI_SCALING=1`); fullscreen is borderless on the primary monitor; ticks continue while minimized and `Size` keeps the last non-minimized framebuffer size. |
| D-030 | Accepted | `IWindow.IsMinimized`, the full readable `KeyCode` scancode set (247 members plus `Unknown = 0`), and the five `MouseButton` members are the only public-surface additions in M2. |
| D-031 | Accepted | `Silk.NET.SDL` 2.23.0 is pinned; its `Ultz.Native.SDL` 2.32.10 dependency ships win-x64 `SDL2.dll`, whose runtime `SDL_GetVersion` is 2.30.8. NativeAOT uses Silk.NET's default resolver with a narrow `IL3000;IL3002` suppression in `Bork2d.AotSmoke` only; the `--windowed [--ticks N]` demo is the windowed gate. |
| D-032 | Accepted | M2 time/exit/lifetime: first tick `Time = DeltaTime = 0`, then raw monotonic `Time` and clamped `DeltaTime`; `Exit()` stops before the next freeze; `Dispose()` during `Run()` throws; after shutdown `IWindow`/`IInput` throw `ObjectDisposedException`, `ITime` stays readable, and `Exit()` is a no-op. |
| D-033 | Accepted | Accepted fatal-platform-error exception to D-028: after an unrecoverable post-startup SDL error the OS thread may destroy the window before `Teardown`; the proxies remain attached for best-effort access. Normal, `Exit`, and close paths destroy the window only after `Teardown`. |
| D-034 | Accepted | Source layout is folder-per-area and the namespace is derived from the project plus the folder path (IDE0130 enforced). Public contracts are split into `Bork2d.Api.Assets`, `.Scene`, `.Rendering`, `.Input`, and `.Platform`, with `IEngine`/`IGame`/`Bork2dException` staying in `Bork2d.Api`; the tooling pack writer is `Bork2d.Assets.Pack`. Breaking namespace change accepted under D-014; behavior unchanged. |
| D-035 | Accepted | Post-M3 asset-reference refactor: `LoadSprite`, `Sprite`, `SpriteResource`, `TextureResource`, the handle table, and the sprite CPU resource pipeline are removed. `SpriteAsset`, `TextureDescriptor`, and `SpriteTopology` are public in `Bork2d.Api.Assets`. `AssetRef<TAsset>` and `DataRef` are typed canonical-path values serialized as strings; `null` means absent, an empty/invalid path is rejected at pack build. `LoadAsset<T>` deserializes only; `PackBuilder` validates every non-null reference at `Build`/`Write`; field validation belongs to the M4 baker/editor. `ISpriteRender.Sprite` is a `SpriteAsset?`. Supersedes D-M3-12, D-M3-13, and D-M3-17. |

## M3 asset decisions (approved 2026-10-26)

Semantics, schema details, and the approved derived details are in the [M3 asset spec](specs/m3-assets.md).
These entries record the accepted decisions only.

| ID | Status | Decision |
| --- | --- | --- |
| D-M3-01 | Accepted | The base pack opens in `Run()` before the window platform and `Setup`; `Build()` stays filesystem-free. Missing/malformed base pack → `Bork2dException` before any window/`Setup`/`Teardown`. |
| D-M3-02 | Accepted | A base pack is required unless `Bork2dAppBuilder.WithoutAssets()` is used; `WithoutAssets` starts an empty VFS and still permits `LoadPack` overlays. `WithAssetsPack` + `WithoutAssets` is a `Build()` error. |
| D-M3-03 | Accepted | `WithAssetsPack`/`LoadPack` filesystem paths resolve against the process current directory; absolute paths are accepted. |
| D-M3-04 | Accepted | `Bork2d.Assets` is public for tooling only (`PackBuilder`, `PackCompressionMode` in `Bork2d.Assets.Pack` — D-034); game code still references only `Api`/`Runtime`. `Bork2d.AotSmoke` references `Bork2d.Assets` for the AOT pack proof. |
| D-M3-05 | Accepted | Pack v1 uses integer-keyed `PackHeader`/`PackEntry`, `uint32` entry `Length`/`Size`, `uint64` `Offset`, and no per-asset schema version. |
| D-M3-06 | Accepted | No policy size caps. Bounds come from field widths, the pack file length, range/overlap checks, and .NET allocation limits; `LoadData` is bounded by `Array.MaxLength`, and `LoadAsset<T>` streams. |
| D-M3-07 | Accepted | Header always maximum-quality Zstd; debug payloads uncompressed; release payloads maximum-quality Zstd only when strictly smaller; no payload checksum. |
| D-M3-08 | Accepted | No byte-level determinism guarantee; entries are sorted by path but compressed bytes may vary. |
| D-M3-09 | Accepted | Base pack plus later-wins overlays, no unload; `LoadPack` is synchronous/atomic/thread-safe and a bad overlay is not mounted. |
| D-M3-10 | Accepted | Asset paths are normalized (lowercase, `\` → `/`) then validated; invalid paths throw `ArgumentException`. `Browse(null)` means all; prefixes match on segments. |
| D-M3-11 | Accepted | One dedicated asset loader thread, unbounded FIFO queue, tasks complete asynchronously, shutdown cancels pending/in-flight work and joins the thread. |
| D-M3-12 | Superseded by D-035 | Sprite and descriptor resources are cached per path; `LoadData` returns a fresh `byte[]` and `LoadAsset<T>` a fresh instance per call; `LoadSprite` is idempotent per path. |
| D-M3-13 | Superseded by D-035 | M3 `LoadSprite` resolves the path/allocates a handle synchronously, loads sprite + descriptor asynchronously, and does not load the raw payload (M8). Missing → `Sprite.Empty` + log; failures stay non-renderable. |
| D-M3-14 | Accepted | `IEngine.Assets` returns a real service in every mode; the getter never throws. After `Run()`/`Dispose`, every `IAssets` member throws `ObjectDisposedException`. |
| D-M3-15 | Accepted | Pack open/parse/mount failures use `Bork2dException`; asset lookup/decode/IO failures use public `AssetException` with `AssetErrorKind`/`AssetPath`; argument errors use BCL exceptions. |
| D-M3-16 | Superseded by D-M3-22 | `LoadAsset<T>` is shape-generic and requires a generated shape; M3 tests use internal shapes and `Project.Common` stays `Api`-only. A missing shape faults with `AssetException` naming `T`. `PackBuilder.AddMessagePack<T>` requires `T : IShapeable<T>`. |
| D-M3-17 | Superseded by D-035 | Texture descriptors and sprite assets stay internal; sprite topology is an integer enum in the pack and a string in TOML; only RGBA8/sRGB is accepted; no per-asset version. |
| D-M3-18 | Accepted | No automatic retry, no loose-file overlay, no KTX2/color-space fields. |
| D-M3-19 | Accepted | The default AOT smoke run builds a temporary pack, loads raw + generated-shape MessagePack assets, browses/exists, then deletes it; `--windowed` keeps the M2 demo with `WithoutAssets()`. |
| D-M3-20 | Accepted | `Bork2d.Assets` pins `Nerdbank.MessagePack` 1.4.95, `PolyType` 1.4.1, and `System.IO.Hashing` 11.0.0-rc.1.26425.128; Zstd is the .NET 11 built-in codec. |
| D-M3-21 | Accepted | Five sequential work packages (pack format/writer, VFS/browse, loader/threading, descriptors/sprites, runtime/AOT/docs/acceptance); no parallel wave. |
| D-M3-22 | Accepted | **Supersedes D-M3-16.** `IAssets.LoadAsset<T>` is `Task<T> LoadAsset<T>(string assetPath) where T : class, IShapeable<T>` (PolyType `IShapeable<T>`), so `Bork2d.Api` references PolyType 1.4.1 for this contract. Unconstrained/reflection-shape deserialization is rejected; the `NBMsgPack051` suppression and the runtime "missing shape" fault are removed. `PackBuilder.AddMessagePack<T>` keeps its `T : IShapeable<T>` constraint. D-001's game-code reference rule is unchanged: game code references only `Api`/`Runtime`, and `Bork2d.Assets` stays tooling-only. |

## Detail: implemented and public-contract decisions

### D-001 Public surface: Api + Runtime

- **Decision:** Only `Bork2d.Api` and `Bork2d.Runtime` are supported for game code.
  `Common`/`Assets`/`Render` are internal; `Runtime` is the composition root. The one exception is
  the M3 public pack-tooling surface in `Bork2d.Assets` (`Bork2d.Assets.Pack.PackBuilder`,
  `PackCompressionMode`),
  which is for tooling and not part of the game contract.
- **Consequences:** Small game contract; internals can change. A public type in an internal project
  is a bug unless intentionally re-exported or part of the documented tooling surface.
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

### D-027 OS/window thread and queued window commands

- **Decision:** A windowed app creates one engine-owned OS/window thread for the duration of
  `Run()`. It owns `SDL_Init`, the window, the event pump, DPI/focus/minimize state, command
  application, and `SDL_Quit`. `IWindow` setters validate and enqueue; getters read an immutable
  confirmed snapshot. This extends D-024 (no public thread affinity) to the platform: the tick
  thread never calls SDL and never waits for a window command.
- **Consequences:** Getters may lag one pump and commands coalesce per property. The OS thread
  cannot call the game, and no lock is held across an SDL call, a game callback, or the event
  wait. The platform is internal to `Bork2d.Runtime`; the public surface stays `IWindow`.
- **Docs:** [Threading](architecture/threading.md#51-window-thread-command-queue-and-confirmed-state-m2-internal),
  [Runtime lifecycle](architecture/runtime-lifecycle.md#71-windowed-window-platform-m2-internal)

### D-028 Windowed-only M2 loop, visible at `Run()`, shutdown order

- **Decision:** Only windowed mode starts a real loop in M2. The window is created visible before
  `Setup`, builder options are applied at creation, and the loop runs `Setup` → `Tick`… →
  `Teardown` until `Exit()`, close/`SDL_QUIT`, or a fatal error. On normal, `Exit`, and close paths
  the window is destroyed after `Teardown`; headless/offscreen keep the one-tick M1 sequence.
  There is no target rate, frame limiter, `Thread.Sleep`, `SpinWait`, or yield in the windowed loop.
- **Consequences:** The uncapped loop consumes a core until M7 supplies frame backpressure; this
  is temporary, not a design goal. A request during `Setup` finishes `Setup`, runs zero ticks, and
  tears down once. A close request keeps the window alive until `Teardown` so the game can still
  use it.
- **Docs:** [Runtime lifecycle](architecture/runtime-lifecycle.md#5-tick-loop-and-pacing),
  [API reference](api/reference.md#bork2dapp)

### D-029 Framebuffer DPI, borderless fullscreen, and minimize

- **Decision:** `WithSize`, `IWindow.Size`, and mouse coordinates are framebuffer physical pixels.
  The platform sets `SDL_WINDOWS_DPI_AWARENESS=permonitorv2` and `SDL_WINDOWS_DPI_SCALING=1` before
  `SDL_Init`, so window sizes and SDL mouse state are window points while the framebuffer stays in
  pixels; the platform converts at the boundary. Fullscreen is borderless desktop mode on the
  primary monitor and leaving it restores the previous windowed bounds. Ticks continue while
  minimized, `IsMinimized` is public, and `Size` keeps the last non-minimized framebuffer size.
- **Consequences:** At non-100% Windows scaling the window's point size differs from its
  framebuffer size (for example a 1280×720 request is an 853×480 window at 150%). Games must not
  cache absolute pixel positions across DPI changes and should not set `Size` every tick.
  Display/monitor selection and exclusive fullscreen are out of scope.
- **Docs:** [API reference](api/reference.md#5-iwindow), [API guide](api/guide.md#9-window)

### D-030 `IWindow.IsMinimized` and the full input enums

- **Decision:** The only public API changes in M2 are `IWindow.IsMinimized`, the full readable
  `KeyCode` set (247 members plus `Unknown = 0`), and the five `MouseButton` members
  (`Left`, `Right`, `Middle`, `Back`, `Forward`). `KeyCode` names readable SDL words rather than
  mechanically concatenated ones (`PageUp`, `CapsLock`, `LeftCtrl`, `AudioFastForward`,
  `KeypadMemStore`); values are the SDL scancodes and top-row digits are `Digit1`…`Digit0`.
  Undefined values are safe queries that return `false`.
- **Consequences:** A mapping test fails if the binding and the enum drift. Numeric values are
  unstable and never serialized. Physical scancodes are layout positions, not characters; text
  comes only from `OnTextTyped`. No focus/visibility/DPI-scale members are added.
- **Docs:** [API reference](api/reference.md#11-value-types), [KeyCode](../src/Bork2d/Bork2d.Api/Input/KeyCode.cs)

### D-031 `Silk.NET.SDL` pin, native version, AOT resolver, and the windowed demo

- **Decision:** `Bork2d.Runtime` pins `Silk.NET.SDL` **2.23.0**. Its transitive
  `Ultz.Native.SDL` **2.32.10** supplies `runtimes/win-x64/native/SDL2.dll`, but the loaded binary
  reports `SDL_GetVersion` **2.30.8** (its Windows file version is also 2.30.8). The package label
  and the loaded binary version are recorded as different facts; M2 requires the runtime 2.30.8
  behavior and every API it uses, including `SDL_GetWindowSizeInPixels` and the Windows DPI hints.
  NativeAOT uses Silk.NET's default resolver (`Sdl.GetApi()`), and `Bork2d.AotSmoke` carries a
  narrow project-level `IL3000;IL3002` `NoWarn` for the resolver's single-file probes. No other
  suppression exists, and `SuppressTrimAnalysisWarnings` is not used. `Bork2d.AotSmoke
  --windowed [--ticks N]` is the windowed acceptance demo and AOT gate.
- **Consequences:** The SDL2 native ships from the package and is copied to the publish output; no
  manual install or fallback binary is needed. A dependency update must re-verify the runtime
  version and remove the suppression when Silk.NET stops probing `Assembly.Location`/`CodeBase`/
  `DependencyContext`. CI still does not publish AOT; the gate is local.
- **Docs:** [NativeAOT](architecture/nativeaot.md#6-native-libraries-and-silknet),
  [Projects](architecture/projects.md#1-current-projects),
  [Contributing](contributing.md#2-build-test-and-local-gates)

### D-032 M2 time, `Exit`, `Dispose`, and post-shutdown contract

- **Decision:** In windowed mode the first tick publishes `Time = 0` and `DeltaTime = 0`; afterwards
  `Time` is raw monotonic seconds since the first tick and `DeltaTime` is the interval between tick
  starts clamped to the builder's `MaxDeltaTime`. `Exit()` lets the current tick finish and stops
  before the next freeze; `Exit()` during `Setup` runs zero ticks. `Dispose()` during an active
  `Run()` throws `InvalidOperationException` from any thread. After the run or disposal, retained
  `IWindow`/`IInput` objects throw `ObjectDisposedException`, `ITime` keeps its last values readable,
  and `Exit()` is a no-op. Headless/offscreen keep the M1 one-tick path with a `0` clock.
- **Consequences:** There is no `TickCount`, fixed step, accumulator, render interpolation, or
  clock pause on focus loss/minimize. Games that need determinism implement their own fixed-step
  accumulator. The clamp limits a stall's gameplay step; it is not a pacing delay.
- **Docs:** [API reference](api/reference.md#4-itime),
  [Runtime lifecycle](architecture/runtime-lifecycle.md#4-time)

### D-033 Accepted fatal-platform-error exception to D-028

- **Decision:** On an unrecoverable SDL error after startup, the OS thread stores a fatal
  `Bork2dException`, requests an error exit, and runs its cleanup immediately, which may destroy
  the window before the tick thread reaches `Teardown`. The window/input proxies stay attached
  until the host shuts the platform down, so `Teardown` can still attempt best-effort access
  (getters generally work; setters may be dropped). This is an accepted M2 exception to D-028.
  Normal, `Exit`, and close paths keep the window alive through `Teardown` and destroy it only
  afterwards.
- **Consequences:** Games must not rely on a live window in `Teardown` after a fatal platform
  error. `Run()` still rethrows the fatal failure after teardown and platform shutdown, aggregated
  with a teardown failure. The exception is limited to the unrecoverable-error path; M6+ must
  define the renderer/vsync equivalent rather than assuming M2's timing.
- **Docs:** [Runtime lifecycle](architecture/runtime-lifecycle.md#6-shutdown-and-error-propagation),
  [Threading](architecture/threading.md#51-window-thread-command-queue-and-confirmed-state-m2-internal)

### D-034 Folder-per-area namespaces and the split public contract

- **Decision:** Every project groups files by area (`Assets/`, `Scene/`, `Pack/`, `Vfs/`, `Loading/`,
  …); a file's namespace is the project namespace plus the folder path, enforced by IDE0130.
  `Bork2d.Api` is split into `Bork2d.Api.Assets` (`IAssets`, `AssetInfo`, `AssetException`,
  `AssetErrorKind`), `Bork2d.Api.Scene` (`ISceneRoot`, `ITransform`, `ICamera`, `ISpriteRender`),
  `Bork2d.Api.Rendering` (`Color`, `ClearKind`), `Bork2d.Api.Input` (`IInput`, `KeyCode`,
  `MouseButton`), and `Bork2d.Api.Platform` (`IWindow`, `ITime`); `IEngine`, `IGame`, and
  `Bork2dException` stay in `Bork2d.Api`. `Data.cs` is split into `Sprite.cs`, `Color.cs`, and
  `ClearKind.cs` (the `Sprite` handle file was later removed by D-035). The public pack tooling
  (`PackBuilder`, `PackCompressionMode`) moves to `Bork2d.Assets.Pack`.
- **Consequences:** Game code adds the relevant namespace usings; fully-qualified names and the
  dependency test change. This is a source- and binary-breaking namespace change accepted under
  D-014, with no behavior, unit, or semantic change. The `Bork2d.Assets` export set is unchanged in
  count and remains tooling-only.
- **Docs:** [Projects and dependencies](architecture/projects.md#4-source-layout-and-namespaces),
  [API reference](api/reference.md#15-namespaces),
  [Contributing](contributing.md#4-c-conventions)

### D-035 Generic asset references replace the special sprite path

- **Decision:** The runtime no longer has `IAssets.LoadSprite`, the `Sprite` handle, `SpriteResource`,
  `TextureResource`, the sprite handle table, or the sprite CPU resource pipeline. Game code loads
  sprites like any other asset: `await engine.Assets.LoadAsset<SpriteAsset>("sprites/player")`.
  `SpriteAsset`, `TextureDescriptor`, and `SpriteTopology` are public in `Bork2d.Api.Assets`.
  `AssetRef<TAsset>` and `DataRef` are typed canonical-path values that serialize as strings; a
  `null` path is an absent reference, and an empty/non-canonical path is rejected by pack creation.
  `ISpriteRender<TLayer>.Sprite` is a `SpriteAsset?`; `null` draws nothing. `LoadAsset<T>` only
  resolves and deserializes; it does not validate fields or links. `PackBuilder.Build`/`Write`
  validate every non-null reference against the final entry table (existence and raw/MessagePack
  kind). Field/schema validation moves to the M4 baker/editor. `AssetRef<T>` and `DataRef` resolve
  their string form through public PolyType marshalers because the shape is generated in the
  consumer assembly; `Bork2d.Api` grants `Bork2d.Assets` internal access to the reference marker so
  the builder can collect links without reflection.
- **Consequences:** Sprite handling stops being a special runtime path. Missing or invalid links
  become pack-build errors, not load-time `InvalidData`; a hand-built invalid pack can still
  deserialize malformed asset fields. `AssetRef<T>` cannot verify the referenced CLR type because
  the pack header has no type metadata. Removing `Sprite`/`LoadSprite` is a breaking change under
  D-014; D-M3-12, D-M3-13, and D-M3-17 are superseded.
- **Docs:** [Assets and tooling](architecture/assets-and-tooling.md#6-public-asset-api),
  [API reference](api/reference.md#7-iassets),
  [Refactor spec](specs/refactor-asset-references.md)

## Superseding a decision

Add a new entry, mark the old one `Superseded by D-XXX`, and update every linked document in the
same change. Do not delete history.
