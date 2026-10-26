# Decision Log

> Lightweight ADR register. **Accepted** = in force; **Proposed** = needs sign-off before the
> relevant milestone; **Superseded** = replaced by a later entry. This log is the single owner of
> accepted decisions and their status; operational detail lives in the architecture and API
> documents. Unresolved choices are tracked in [Roadmap §9](roadmap.md#9-open-decisions), not here.
> Last review: 2026-10-26.

## Core decisions (D-001–D-035)

| ID | Status | Decision |
| --- | --- | --- |
| D-001 | Accepted | Game code references only `Bork2d.Api` and `Bork2d.Runtime`; `Common`, `Assets`, and `Render` stay internal behind `InternalsVisibleTo`, except the tooling-only pack surface (`PackBuilder`, `PackCompressionMode` in `Bork2d.Assets.Pack`). `Bork2d.Editor` is a separate JIT tool surface. |
| D-002 | Accepted | A tick thread owns callbacks, time, and snapshot publication; a render thread owns Vulkan in windowed/offscreen; asset loaders are background threads. The public API has no main-thread affinity. |
| D-003 | Accepted | Three snapshot slots (`Free → Writing → Ready → Rendering → Free`); the tick runs ahead while slots are free and waits only when the ring is full. Newest `Ready` wins; no target tick rate or `Sleep` in windowed/offscreen. |
| D-004 | Accepted | Variable timestep: `DeltaTime` is wall-clock between tick starts, clamped by the builder's `WithMaxDeltaTime` (default 0.25 s). No accumulator or render interpolation; headless is timer-paced. |
| D-005 | Accepted | v1 targets Windows x64; SDL/Vulkan boundaries stay portable; Linux/macOS are not v1 guarantees. |
| D-006 | Accepted | `TLayer` is an `int`-backed `[Flags]` enum: one non-zero bit per renderable, masks on cameras, at most 32 flags, validated once at `Build()`. |
| D-007 | Accepted | Each non-empty camera produces one instanced draw; there is no global one-draw rule. Camera order is `ZIndex` (D-026). |
| D-008 | Accepted | Abstract units, +X right/+Y up; `Camera.Size` is the half visible height; each sprite asset carries pixels-per-unit scale; the shader generates quad corners; no vertices array. Rotation rules are D-025. |
| D-009 | Accepted | Vulkan 1.3 + SDL2/Silk.NET, one pipeline of instanced quads, bindless texture pages, dynamic rendering, no depth/MSAA/HDR; sort layer then `ZIndex`; straight alpha. |
| D-010 | Accepted | Runtime assets live in `assets.pck`: 28-byte prefix, always-compressed and XXH3-64-checksummed MessagePack header, `uint64` offsets, `uint32` entry length/size. Later `LoadPack` overlays win; no unload; a missing base pack is fatal at `Run()`. No per-asset version and no policy size caps. |
| D-011 | Accepted | `Bork2d.Editor` is an Avalonia JIT library/app that slices textures and edits source data; no Vulkan preview and no scene editing. |
| D-012 | Accepted | TOML is the source format; a JIT baker emits MessagePack plus raw payloads. The NativeAOT runtime reads MessagePack only, using Nerdbank generated shapes. |
| D-013 | Accepted | Game solution: `Project.Common` (Api), `Project.Desktop` (Runtime + Common), `Project.Editor` (Editor + Common); registrations are compile-time, never assembly scanning. |
| D-014 | Accepted | Breaking changes are allowed until the v1 API freeze (M10); each updates the guide, reference, and this log. |
| D-015 | Accepted | Scene mutation happens only inside `Setup`/`Tick`/`Teardown`. Workers may create/mutate distinct entities in parallel and must join before `Tick` returns; composite same-entity updates are game-synchronized. |
| D-016 | Accepted | Runtime libraries are NativeAOT/trim-safe: no runtime reflection, TOML, or dynamic code; Nerdbank shapes are generated. Editor and baker stay JIT. |
| D-017 | Accepted | Two non-windowed modes: `WithHeadless(ticksPerSecond = 60, 0 = uncapped)` and `WithOffscreen(w, h)`; mutually exclusive with each other and with window options. |
| D-018 | Accepted | `WithGame(instance)` takes a caller-constructed game; no `new()` constraint or `Activator`. The caller keeps it alive through `Run()`. |
| D-019 | Superseded by D-035 | M3 descriptors and sprites were internal MessagePack models loaded by a sprite-specific CPU pipeline; D-035 makes them public assets loaded through `LoadAsset<T>`. |
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
| D-030 | Accepted | The M2 public-surface additions are `IWindow.IsMinimized`, the full readable `KeyCode` set (247 members including `Unknown = 0`), and the five `MouseButton` members. |
| D-031 | Accepted | `Silk.NET.SDL` 2.23.0 is pinned; its `Ultz.Native.SDL` 2.32.10 dependency ships win-x64 `SDL2.dll`, whose runtime `SDL_GetVersion` is 2.30.8. NativeAOT uses Silk.NET's default resolver with a narrow `IL3000;IL3002` suppression in `Bork2d.AotSmoke` only; the `--windowed [--ticks N]` demo is the windowed gate. |
| D-032 | Accepted | M2 time/exit/lifetime: first tick `Time = DeltaTime = 0`, then raw monotonic `Time` and clamped `DeltaTime`; `Exit()` stops before the next freeze; `Dispose()` during `Run()` throws; after shutdown `IWindow`/`IInput` throw `ObjectDisposedException`, `ITime` stays readable, and `Exit()` is a no-op. |
| D-033 | Accepted | Accepted fatal-platform-error exception to D-028: after an unrecoverable post-startup SDL error the OS thread may destroy the window before `Teardown`; the proxies remain attached for best-effort access. Normal, `Exit`, and close paths destroy the window only after `Teardown`. |
| D-034 | Accepted | Source layout is folder-per-area and the namespace is derived from the project plus the folder path (IDE0130 enforced). Public contracts are split into `Bork2d.Api.Assets`, `.Scene`, `.Rendering`, `.Input`, and `.Platform`, with `IEngine`/`IGame`/`Bork2dException` staying in `Bork2d.Api`; the tooling pack writer is `Bork2d.Assets.Pack`. Breaking namespace change accepted under D-014; behavior unchanged. |
| D-035 | Accepted | Post-M3 asset-reference refactor: `LoadSprite`, `Sprite`, `SpriteResource`, `TextureResource`, the handle table, and the sprite CPU resource pipeline are removed. `SpriteAsset`, `TextureDescriptor`, and `SpriteTopology` are public in `Bork2d.Api.Assets`. `AssetRef<TAsset>` and `DataRef` are typed canonical-path values serialized as strings; `null` means absent, and an empty/invalid path is rejected at pack build. `LoadAsset<T>` deserializes only; `PackBuilder` validates every non-null reference at `Build`/`Write`; field validation belongs to the M4 baker/editor. `ISpriteRender.Sprite` is a `SpriteAsset?`. Supersedes D-019, D-M3-12, D-M3-13, and D-M3-17. |

## M2 platform decisions (approved 2026-10-26)

Detailed approvals. D-027–D-033 in the core table consolidate the same work; the D-M2 rows are the
authoritative wording for M2-specific behavior.

| ID | Status | Decision |
| --- | --- | --- |
| D-M2-01 | Accepted | Windowed-only real loop. Windowed runs an uncapped loop until `Exit`, close, or a fatal error; headless and offscreen keep the M1 one-tick `Setup` → `Tick` → `Teardown` until M7. |
| D-M2-02 | Accepted | One engine-owned OS/window thread owns SDL initialization, the window, event pumping, and all SDL window calls. The tick thread never calls SDL. |
| D-M2-03 | Accepted | The window is created visible before `Setup` with the builder options applied, and is destroyed after `Teardown`, before `Run()` returns. |
| D-M2-04 | Accepted | `IWindow` setters validate, drop redundant requests, and enqueue without blocking; getters return the last confirmed state and may lag one pump. |
| D-M2-05 | Accepted | All SDL code is internal to `Bork2d.Runtime`; no new project and no dependency-matrix change. |
| D-M2-06 | Accepted | `Silk.NET.SDL` 2.23.0 is pinned. Its `Ultz.Native.SDL` 2.32.10 dependency ships win-x64 `SDL2.dll`, whose loaded binary reports `SDL_GetVersion` 2.30.8; the package label is not the runtime version. |
| D-M2-07 | Accepted | `Bork2d.AotSmoke` gains `--windowed [--ticks N]`; the default headless run is unchanged. Windowed AOT publish/run is a local gate. |
| D-M2-08 | Accepted | An internal SDL adapter seam with fakes drives deterministic tests. Real-window tests run only with `BORK2D_RUN_WINDOW_TESTS=1`; CI scope is unchanged. |
| D-M2-09 | Accepted | Fullscreen is borderless desktop mode on the primary monitor; leaving it restores the previous windowed bounds. No display/resolution/exclusive options. |
| D-M2-10 | Accepted | Physical framebuffer pixels everywhere: `SDL_WINDOW_ALLOW_HIGHDPI` plus per-monitor awareness; `WithSize`, `IWindow.Size`, and mouse coordinates are framebuffer pixels, converted at the boundary. |
| D-M2-11 | Accepted | Ticks continue while minimized; `IWindow.IsMinimized` is public; `IWindow.Size` keeps the last non-minimized framebuffer size. |
| D-M2-12 | Accepted | The selected mode is not exposed on `IEngine` or `Bork2dApp`; hosts branch on their own configuration. |
| D-M2-13 | Accepted | The only public API additions are `IWindow.IsMinimized` and the full `KeyCode`/`MouseButton` member sets. |
| D-M2-14 | Accepted | `KeyCode` mirrors the full SDL physical scancode set (247 members including `Unknown = 0`); `MouseButton` is `Left`, `Right`, `Middle`, `Back`, `Forward`. Numeric values are unstable and never serialized; undefined values are safe `false` queries. |
| D-M2-15 | Accepted | Escape is game-handled. The window close button/`SDL_QUIT` always requests a graceful exit. |
| D-M2-16 | Accepted | `Time` is `0` at the first tick, then raw monotonic seconds. `DeltaTime` is `0` on the first tick, then the raw interval between tick starts clamped to the builder's `WithMaxDeltaTime`. No `TickCount`. |
| D-M2-17 | Accepted | After shutdown, retained `IWindow`/`IInput` objects remain valid but their members throw `ObjectDisposedException`; `ITime` keeps its last values readable; `Exit()` is a no-op; the `IEngine` getters never throw. |
| D-M2-18 | Accepted | `Dispose()` during an active `Run()` throws `InvalidOperationException` from any thread. |
| D-M2-19 | Accepted | Exit or close during `Setup` finishes `Setup`, runs zero ticks, calls `Teardown` once, and returns normally. |
| D-M2-20 | Accepted | The unused internal `Engine<TLayer,TGame>.Run()` is deleted; the tick loop lives only in `Bork2d.Runtime`. |
| D-M2-21 | Accepted | `MouseDelta` is `MousePosition` minus the previous freeze position; zero on the first freeze and on focus transitions. Reads before the first freeze return a fully empty snapshot. |
| D-M2-22 | Accepted | Focus loss synthesizes releases, clears held state/wheel/text, and zeroes delta. Focus regain re-seeds held state without press edges, re-syncs position, and zeroes delta. |
| D-M2-23 | Accepted | Text comes only from `SDL_TEXTINPUT` as UTF-16 chars before `Tick`; key repeat never creates press edges or text; IME is out of scope. |
| D-M2-24 | Accepted | `IWindow` details: `Size` is ignored while fullscreen with a debug log and updates the restore size while minimized; size components must be finite and positive; `CursorVisible` defaults to `true` and never auto-hides; `Title` allows empty/whitespace. |

## M3 asset decisions (approved 2026-10-26)

Detailed approvals. D-010 and D-019 in the core table summarize the overlapping pack and asset-model
points; the D-M3 rows are the authoritative wording for M3-specific behavior.

| ID | Status | Decision |
| --- | --- | --- |
| D-M3-01 | Accepted | The base pack opens in `Run()` before the window and `Setup`; `Build()` stays filesystem-free. A missing or malformed base pack throws `Bork2dException` before any window, `Setup`, or `Teardown`. |
| D-M3-02 | Accepted | A base pack is required unless `Bork2dAppBuilder.WithoutAssets()` is used; `WithoutAssets` starts an empty VFS and still permits `LoadPack` overlays. `WithAssetsPack` plus `WithoutAssets` is a `Build()` error. |
| D-M3-03 | Accepted | `WithAssetsPack`/`LoadPack` filesystem paths resolve against the process current directory; absolute paths are accepted. |
| D-M3-04 | Accepted | `Bork2d.Assets` is public for tooling only (`PackBuilder`, `PackCompressionMode` in `Bork2d.Assets.Pack`); game code still references only `Api`/`Runtime`. `Bork2d.AotSmoke` references `Bork2d.Assets` for the AOT pack proof. |
| D-M3-05 | Accepted | Pack v1 uses integer-keyed `PackHeader`/`PackEntry`, `uint32` entry `Length`/`Size`, `uint64` `Offset`, and no per-asset schema version. |
| D-M3-06 | Accepted | No policy size caps. Bounds come from field widths, the pack file length, range/overlap checks, and .NET allocation limits; `LoadData` is bounded by `Array.MaxLength`, and `LoadAsset<T>` streams. |
| D-M3-07 | Accepted | Header always maximum-quality Zstd; debug payloads uncompressed; release payloads maximum-quality Zstd only when strictly smaller; no payload checksum. |
| D-M3-08 | Accepted | No byte-level determinism guarantee; entries are sorted by path but compressed bytes may vary. |
| D-M3-09 | Accepted | Base pack plus later-wins overlays, no unload; `LoadPack` is synchronous/atomic/thread-safe and a bad overlay is not mounted. |
| D-M3-10 | Accepted | Asset paths are normalized (lowercase, `\` → `/`) then validated; invalid paths throw `ArgumentException`. `Browse(null)` means all; prefixes match on segments. |
| D-M3-11 | Accepted | One dedicated asset loader thread, unbounded FIFO queue, asynchronous completion; shutdown cancels pending/in-flight work and joins the thread. |
| D-M3-12 | Superseded by D-035 | Sprite and descriptor CPU resources were cached per path, and `LoadSprite` was idempotent per path. |
| D-M3-13 | Superseded by D-035 | M3 `LoadSprite` resolved a handle synchronously, loaded sprite and descriptor asynchronously, and never read the raw payload. |
| D-M3-14 | Accepted | `IEngine.Assets` returns a real service in every mode; the getter never throws. After `Run()`/`Dispose`, every `IAssets` member throws `ObjectDisposedException`. |
| D-M3-15 | Accepted | Pack open/parse/mount failures use `Bork2dException`; asset lookup/decode/IO failures use `AssetException` with `AssetErrorKind`/`AssetPath`; argument errors use BCL exceptions. |
| D-M3-16 | Superseded by D-M3-22 | `LoadAsset<T>` was shape-generic with M3 tests using internal shapes, and a missing shape faulted at runtime. |
| D-M3-17 | Superseded by D-035 | Descriptors and sprites were internal; topology was an integer in the pack and a string in TOML; only RGBA8/sRGB was accepted. |
| D-M3-18 | Accepted | No automatic retry, no loose-file overlay, no KTX2/color-space fields. |
| D-M3-19 | Accepted | The default AOT smoke builds a temporary pack, loads raw and generated-shape MessagePack assets, browses/exists, then deletes it; `--windowed` keeps the M2 demo with `WithoutAssets()`. |
| D-M3-20 | Accepted | `Bork2d.Assets` pins `Nerdbank.MessagePack` 1.4.95, `PolyType` 1.4.1, and `System.IO.Hashing` 11.0.0-rc.1.26425.128; Zstd is the .NET 11 built-in codec. |
| D-M3-21 | Accepted | Five sequential work packages (pack format/writer, VFS/browse, loader/threading, descriptors/sprites, runtime/AOT/docs/acceptance); no parallel wave. |
| D-M3-22 | Accepted (the `class` constraint is superseded by D-M4-11) | Supersedes D-M3-16. `IAssets.LoadAsset<T>` is `Task<T> LoadAsset<T>(string assetPath) where T : class, IShapeable<T>`; `Bork2d.Api` references PolyType 1.4.1 for this contract. `PackBuilder.AddMessagePack<T>` keeps its `T : IShapeable<T>` constraint. D-M4-11 removes the `class` constraint; the shape contract itself remains. |

## M4 serialization and bake decisions (approved 2026-10-26)

Detailed approvals. D-M4-01, D-M4-02, D-M4-11, D-M4-12, and the Q-04/Q-09 resolutions are landed by
WP1; WP2 lands D-M4-04 and the Q-06/Q-07 binding decisions. The remaining D-M4 entries are added by
the work packages that implement them.

| ID | Status | Decision |
| --- | --- | --- |
| D-M4-01 | Accepted | Bake tooling shape: a new JIT `Bork2d.Bake` library plus a `Bork2d.Bake.Cli` console. The M5 editor reuses the library; MSBuild invokes the CLI. The tooling surface is internal with `InternalsVisibleTo` (Q-02). |
| D-M4-02 | Accepted | Type mapping: the manifest is generated from `Project.Common` metadata and compiled into a **generated bake host** that references `Project.Common` and `Bork2d.Bake`. The host calls typed, generated bind/write code. No runtime reflection over game members; `Project.Common`'s game types reference `Bork2d.Api` only, and Q-04 may add a referenced data library that contains a mapped type. |
| D-M4-04 | Accepted (landed by WP2) | TOML library: Tomlyn 2.10.1, TOML 1.1.0. The authoring contract stays the BCL `System.Text.Json.Serialization` attributes (`[JsonPropertyName]`, `[JsonRequired]`, `[JsonIgnore]`), and `Bork2d` converters cover references, vectors, colors, and enums. The generated host implements those semantics with the generic shape-driven DOM binder (Q-07) rather than Tomlyn's POCO/source-gen mapper, because that mapper cannot reject unknown keys or expose the value spans the reference diagnostics require; Tomlyn remains the parser, DOM, and syntax-tree source. |
| D-M4-11 | Accepted | Struct roots: a top-level asset root may be any struct, including a `readonly record struct`. `IAssets.LoadAsset<T>` (both overloads), `AssetRef<TAsset>`, and `AssetRefMarshaler<TAsset>` use `where T : IShapeable<T>`; `AssetRef<TAsset>` can target a struct. The deserializer rejects an empty or MessagePack `nil` root before deserializing, so a struct root never silently becomes `default`; both are `AssetErrorKind.Decode` faults. This supersedes the `class` part of D-M3-22 (its shape contract remains) and is a public API change approved under D-014. |
| D-M4-12 | Accepted | Registration, `partial`, and `Color`: PolyType `[GenerateShape]` is the single shape attribute; the descriptor is the registration list; there is no `Bork2dSerializableAttribute` and no `Bork2d.Bake.Generators`. A `[GenerateShape]` root must be `partial` because PolyType injects `IShapeable<T>` through a partial declaration. `Color` gets `[TypeShape(Marshaler = typeof(ColorMarshaler))]` and stays non-`partial` with no `[GenerateShape]`; `ColorMarshaler` is public serialization infrastructure (`[EditorBrowsable(Never)]`) and serializes the linear float surrogate `[r, g, b, a]`. A null surrogate is a decode fault. Supersedes D-M4-03 and D-M4-10. |
| Q-04 | Accepted | Type resolution searches `Project.Common`, then the assemblies it references, then `Bork2d.Api`; an ambiguous full name names every candidate. A mapped type may live outside `Project.Common` when the library containing it is referenced by `Project.Common`. |
| Q-06 | Accepted (WP0; implemented by WP2) | Value spans come from a `SyntaxParser.Parse` → `DocumentSyntax` pass indexed by segment-aware member path (`items[2].icon`), reading `KeyValueSyntax.Value.Span.Start` (0-based → 1-based). Tomlyn's `MetadataStore` is not used: it reports key spans only. Reference sites carry their value line/column so reference diagnostics point at the value without a second document pass. |
| Q-07 | Accepted (owner-refined; implemented by WP2) | The generated host emits only the type manifest and the `Bind`/`AddToPack` switches; binding is a **generic shape-driven DOM binder** in `Bork2d.Bake.Toml`. The binder walks the compiled PolyType shape (`IObjectTypeShape`, constructor/parameter setters, enum members, optional/enumerable/dictionary facets, and marshaler surrogates) and reads the Tomlyn DOM, using shape attribute providers for the D-M4-04 STJ attributes. It is reflection-free over game members (no `PropertyInfo`, `Activator`, or `Enum.GetNames`), and it preserves the Q-05 unknown-key/required/case rules, the Q-06 value spans, the D-M4-04 attributes, and the Q-33 camelCase-enum/offset-date-time rules. This is a deliberate owner-approved refinement of WP0's generated per-type DOM binder (which would have emitted per-type binding code); it keeps one binder implementation and makes the generated host source depend only on the mapped types. |
| Q-09 | Accepted | The incremental manifest uses XXH3 (`System.IO.Hashing.XxHash128`, 16-byte uppercase hex, explicit field framing), not SHA-256. |

## Superseding a decision

Add a new entry, mark the old one `Superseded by D-XXX`, and update every linked document in the
same change. Do not delete history.
