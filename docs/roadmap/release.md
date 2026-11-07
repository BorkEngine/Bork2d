# Release, Integration, and Verification

[Back to the 0.3 roadmap](../roadmap.md)

Cross-cutting 0.3 material: release gates, baseline, decisions, integration, verification, risks,
and scope.

Owning docs: [engine.md](../engine.md), [render.md](../render.md), [bake.md](../bake.md),
[runtime.md](../runtime.md), and [architecture.md](../architecture.md).

## Release gates

The version is complete only when all of the following are true:

1. Every feature is usable through documented public APIs, not only through example code or tests.
2. Pong runs windowed with keyboard and gamepad controls, plays its baked sounds through scene
   sources and one-shots, and remains playable with the existing controls.
3. The renderer has no privileged pass path: the 2D and present passes are registered `IRenderPass`
   implementations, the frame loop contains no branch on pass type or renderable kind, and a
   user-authored pass and custom vertex/instance renderable draw through the same contract.
4. The built-in 0.2 rendering behavior is preserved: sprite, line, and font order, culling, shader
   fallback, capture bytes, draw-run accounting, one draw per camera for the default sprite path,
   and zero steady-state allocations.
5. Audio plays AAC through `Bork2d.Audio`; windowed startup requires Steam Audio and fails with a
   clear error when it is unavailable; offscreen and headless runs are silent; the mixer allocates
   nothing in steady state.
6. Gamepad input freezes once per tick, reports one press/release edge, handles connect, disconnect,
   remap, and focus changes, supports all connected pads, and routes rumble without blocking the
   tick thread.
7. Linux builds in Debug and Release, passes the test and format gates, publishes for `linux-x64`
   with zero AOT/trim warnings, and passes the lavapipe offscreen GPU suite in CI. A WSLg windowed
   Pong run is a local release gate.
8. macOS builds and AOT-publishes for `osx-arm64` with zero warnings, and the provided Apple Silicon
   Mac runs Pong windowed through MoltenVK with gamepad and audio. Intel Macs are not supported.
9. The standard engine and example gates pass: Debug and Release builds, tests, format verification,
   docs link check, `git diff --check`, Pong build/format, and per-RID AOT publishes.
10. Breaking changes are recorded in the owning docs and in a 0.3 migration note; no old pass,
    input, audio, or shader behavior is documented as current.

macOS verification is hardware-gated: it is not a CI render gate because hosted macOS runners do not
provide a usable Metal device. The release is not complete until the provided Mac passes the runtime
gate. If no Mac is available when the rest of the release is ready, 0.3 ships with macOS explicitly
marked unverified and the gate carries to 0.4; it is not silently waived.

## Baseline

0.2 is a single-platform, fixed-pass engine:

- Input is keyboard, mouse, and text only. `IInput` freezes once per tick, reports one-tick edges,
  synthesizes releases on focus loss, and has no gamepad concept.
- There is no audio project, asset, API, or device. SDL is initialized with `SDL_INIT_VIDEO` only and
  explicitly never initializes audio, game controllers, or haptics.
- The renderer has an internal ordered pass registry with two passes: the unified 2D pass (sprites,
  lines, fonts) and the present pass. Passes are internal, their plan and record contexts expose
  Vulkan and internal snapshot types, and a new renderable kind requires editing the engine and the
  renderer.
- The 2D pipeline has one engine-owned vertex ABI: sprites and fonts read a 176-byte instance record
  from a storage buffer, lines use a 112-byte indexed vertex record, and custom shaders are
  fragment-only. There is no user vertex stage, vertex layout, index data, or custom pipeline.
- Texture pages are combined image samplers in one bindless array, with a 256-entry minimum capacity.
- SDL2 is supplied by `Silk.NET.SDL` 2.23.0, which ships SDL 2.32.10 natives for Windows, Linux, and
  macOS. The host, renderer, and engine have no `DllImport` and no Windows-only code path; the
  remaining Windows couplings are loader names in diagnostics and tests, DPI hints, MSBuild path
  literals, the CI runner, and the documented `win-x64` publish.
- The documented platform is Windows. Linux and macOS are not tested.

0.3 changes each of those assumptions without discarding the 0.2 content model. Baked shaders,
KTX2 textures, MSDF text, and the frame-snapshot handoff remain. The 2D pass becomes a pass written
against the same public contract as a user pass; the bindless table is split for macOS; AAC and
Steam Audio add a second real-time subsystem; gamepads extend the existing frozen-input model.

## Decisions

These choices are fixed for the 0.3 design. Public names below are indicative; exact signatures and
defaults are settled in the implementation tasks and recorded in the owning docs.

| Area | Decision |
| --- | --- |
| SDL | Stay on SDL2 through `Silk.NET.SDL` 2.23.0. SDL3 is a later migration, not hidden work inside 0.3. The bundled mapping database is used; no community `gamecontrollerdb.txt` is shipped. |
| Gamepad placement | Gamepads live on `IInput.Gamepads`, are available only in windowed mode, and follow the same freeze and lifetime rules as keyboard and mouse. |
| Gamepad buttons | Positional `South`, `East`, `West`, `North`, shoulders, sticks, dpad, start/back/guide, and touchpad. No brand names. SDL's label hint is disabled so the positional contract is true on every pad. |
| Gamepad analog | No engine deadzone. Sticks are normalized to `[-1, 1]` with `+Y` up; triggers are `[0, 1]`. Games apply their own deadzone and curves. |
| Gamepad lifecycle | Connect-order snapshot, retained disconnected objects with `Connected == false`, at most eight pads, no engine-assigned player slots. |
| Gamepad output | Rumble only, with capability flags; no gyro, accelerometer, LED, battery, or touchpad fingers in 0.3. |
| Audio project | Public audio contracts live in `Bork2d.Engine.Audio`; the baked asset type lives in `Bork2d.Engine.Assets`, next to the texture and font assets. Implementation, decoding, mixing, and native bindings live in the new `Bork2d.Audio` project, which exposes no public types. `Bork2d.Runtime` composes it. |
| Audio thread | One dedicated mixer thread. SDL uses a null callback and `SDL_QueueAudio`; the mixer never runs inside the SDL callback. |
| Audio spatializer | Steam Audio is mandatory in windowed mode. Startup fails with a clear error when the native library, context, HRTF, or output device cannot be created. Offscreen and headless are silent and never initialize Steam Audio. |
| Audio codec | AAC-LC only. The bake emits ADTS AAC-LC at 48 kHz, mono or stereo, CBR, with a sidecar bitrate. The runtime decodes with `SharpJaad.AAC` plus a small ADTS splitter. |
| Audio authoring | WAV, AIFF, FLAC, Ogg Vorbis, MP3, and M4A/AAC sources. M4A/AAC input is re-encoded with a warning. The bake uses a pinned external FFmpeg; it is a documented build-host prerequisite and CI installs it. |
| Audio residency | The whole AAC payload is resident in both modes. `memory` decodes the whole clip to PCM at load; `stream` decodes incrementally into a PCM ring. Compressed payload streaming from the pack is out of scope. |
| Audio looping | Whole-clip looping only. Loop points, gapless loop metadata, and crossfades are later. |
| Audio devices | Device enumeration and selection are runtime operations through `IAudio`; `null` means the system default. A failed switch leaves the current device active and reports the failure. |
| Audio non-windowed modes | Offscreen and headless audio are silent: `IsAvailable == false`, sources still update state, and no SDL audio or Steam Audio object is created. |
| Render pass model | Every pass, including the built-in 2D and present passes, is an `IRenderPass` in one ordered registry. A pass plans value-only records into frame-owned buffers and records bulk draw commands through the engine-owned record context. No pass gets a private recording path and the frame loop never branches on pass type. |
| Render authoring | A game authors a custom renderable as a content object implementing `ICustomRenderable<TRecord>`; the engine wraps it in an internal scene node that owns transform, parenting, capture, and bounds. |
| Render geometry | Custom kinds declare a per-kind vertex/instance layout and may use a custom vertex stage. Custom vertex and fragment SPIR-V, the vertex layout, topology, and blend mode travel in a pipeline asset. |
| Render targets | No intermediate render targets, post-processing, or render graph in 0.3. Camera and frame passes draw into the existing scene image; the present pass is the only pass that writes the final target. |
| Render order | Phases: driver-owned scene clear, per-camera passes in registration order, frame passes in registration order, then exactly one present pass. Present is last by validation, not convention. |
| Render state | Fixed blend modes only: straight alpha, additive, opaque. No depth, stencil, cull, or custom raster state; the scene has no depth attachment. |
| Render statistics | `RenderStats` keeps its current shape. `DrawCalls` includes custom passes; a custom pass owns any extra counters it exposes to the game. |
| Render failures | An exception from a pass is a one-shot renderer fatal. A pending shader or texture skips the affected record; a failed asset falls back or draws the placeholder under the existing rules. |
| Breaking changes | 0.3 may break public API and content contracts where the design requires it, with a migration note and as-built docs. The bindless split and pipeline asset model are expected breaking changes. |

The milestone map, dependencies, and exit criteria are in the [roadmap overview](../roadmap.md) and
the workstream documents. The plan does not estimate implementation time.

## Cross-cutting integration

### Projects and dependencies

The solution gains `Bork2d.Audio`:

```text
Bork2d.Tests ───> Bork2d.Engine, Bork2d.Render, Bork2d.Runtime, Bork2d.Bake, Bork2d.Audio

Bork2d.Runtime ─> Bork2d.Render ─> Bork2d.Engine
      ├───────────────────────────^
      └──────────> Bork2d.Audio ─> Bork2d.Engine

Bork2d.Bake ──────────────────────> Bork2d.Engine   (build time only)
```

`Bork2d.Audio` references only `Bork2d.Engine`, `SteamAudio.NET`, `SteamAudio.NET.Natives`, and
`SharpJaad.AAC`. It exposes no public types and never references `Bork2d.Render`, `Bork2d.Runtime`,
SDL, or bake tooling. `Bork2d.Engine` gains the `Bork2d.Engine.Audio` namespace and the
`AudioClipAsset` type in `Bork2d.Engine.Assets`, but no audio implementation package.
`Bork2d.Runtime` references `Bork2d.Audio` and owns the SDL output seam.

The dependency-matrix and public-surface tests are updated with approval: the solution has six
projects, Runtime references Audio, Engine exposes the audio namespace, the engine dependency guard
asserts no Steam Audio/SharpJaad/SDL reference, `Bork2d.Audio` has a no-public-types test, and the
tests reference the new project. These are contract changes, not bookkeeping.

### Frame handoff and threading

- Input is still frozen on the tick thread and never crosses to the render thread. Gamepad state is
  part of the same freeze.
- The render snapshot gains custom renderable records and the audio snapshot is separate. Audio
  capture is its own value-only walk under the structural lock, not a render slot and not a render
  producer. Audio source records, listener pose, and one-shot requests are value-only. No live
  `IAudioSource`, `ICamera`, `AudioClipAsset`, or mesh object crosses to the mixer.
- The audio mixer reads the latest complete audio snapshot at block boundaries. Scene sources are
  level-triggered; one-shots are a bounded multi-producer ring.
- The renderer's pass list is immutable for the run. Custom renderable registrations happen at
  build/start; no pass or kind is added while frames are in flight.
- Shutdown order: `Teardown`, stop/join renderer, stop/join audio (mixer, decode, device close),
  platform shutdown (SDL audio subsystem before `SDL_Quit`), asset stop, detach proxies. Audio stops
  before the asset service so decode cannot touch a closed service.

### Public API summary

New or changed public members in 0.3:

- `Bork2d.Engine.Input`: `IGamepad`, `GamepadButton`, `GamepadType`, `IInput.Gamepads`.
- `Bork2d.Engine.Audio`: `IAudio<TLayer>`, `IAudioSource`, `AudioDeviceInfo`, `AudioHandle`,
  `AudioSpace`.
- `Bork2d.Engine.Assets`: `AudioClipAsset`, `AudioCodec`, `AudioClipLoadMode`, `PipelineAsset`,
  `VertexLayout`, `PrimitiveTopology`.
- `IEngine<TLayer>`: `Audio`, `CreateAudioSource`, `CreateCustomRenderable`.
- Builder: audio options (`WithAudioEnabled`, `WithAudioDevice`, `WithMasterVolume`), custom
  renderable/pass registration, and pass ordering.
- `Bork2d.Engine.Scene`: `ICustomRenderable<TRecord>`, `ICustomRenderableNode<TLayer>`.
- `Bork2d.Engine.Rendering`: `IRenderPass`, `RenderPassPhase`, plan/record contexts, draw state,
  camera view, pipeline handle, `BlendMode`.
- `AssetRef`/`DataRef` remain the only asset handles; no implementation package type appears in a
  public signature.

Exact overload sets, nullability, defaults, and exception types are settled in the implementation
tasks and recorded in [engine.md](../engine.md), [render.md](../render.md), [runtime.md](../runtime.md), and
[bake.md](../bake.md). A breaking-change note calls out the shader layout version bump, the pipeline
asset model, the new reserved snapshot/record kinds, and the new project.

### Pong 0.3

Pong remains the playable integration gate. Its simulation, court, paddles, ball, wordmark, and HUD
stay. It gains:

- Gamepad control. The left stick or dpad moves the left paddle; `Start`/`South` pauses and
  `Back`/`East` resets, mirroring Space/R. Rumble fires on a paddle hit and a score. The keyboard
  controls stay unchanged and remain the fallback.
- Audio. Baked `audio/paddle`, `audio/wall`, `audio/score`, and `audio/ambience` assets. A looping
  2D ambience scene source (its transform is present but ignored because it is 2D); paddle and wall
  impacts are 3D one-shots at the impact position; the score is a 2D one-shot. The listener is the
  Pong camera.
- Renderer. Pong draws through the same 2D and present passes as every other app. The passes are
  ordinary registered passes; Pong adds no renderer special case. A custom pass is not required for
  Pong; the custom pass and custom-vertex proof lives in the GPU tests, not in the game.
- Assets. The four audio sources with committed sidecars; the existing font, SVG, KTX2, and shader
  content stays. The built-in pack still supplies the default pipelines.

Pong acceptance: windowed play with keyboard and pad, audible impacts and ambience, correct 2D/3D
panning as the ball moves, no steady-state allocations, Debug/Release builds, format, and per-RID AOT
publishes with zero warnings.

## Integration milestone

The final `I1` milestone is tracked in the [roadmap overview](../roadmap.md). Its deliverable is the
complete Pong integration, as-built docs, all gates, the migration note, and no unfinished marker;
its exit criterion is a release with every other milestone merged and every gate green.

## Verification

### Standard gates

```powershell
dotnet build src/Bork2d/Bork2d.slnx -c Debug
dotnet build src/Bork2d/Bork2d.slnx -c Release
dotnet test  src/Bork2d/Bork2d.slnx -c Debug --no-build
dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
pwsh scripts/check-docs.ps1
git diff --check
```

### Example and AOT gates

```powershell
dotnet build examples/pong/Pong.slnx -c Debug
dotnet build examples/pong/Pong.slnx -c Release
dotnet format examples/pong/Pong.slnx --verify-no-changes --no-restore
# Windows:
dotnet publish examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Release -r win-x64
# Linux:
dotnet publish examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Release -r linux-x64
# macOS, on a Mac:
dotnet publish examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Release -r osx-arm64
```

Each publish runs on its own OS; cross-OS AOT is not supported. Each must finish with zero AOT/trim
warnings. The published app is the run gate on a Vulkan 1.3 desktop and, for macOS, on the provided
Apple Silicon Mac.

### Environment-gated tests

```powershell
$env:BORK2D_RUN_WINDOW_TESTS = '1'
dotnet test src/Bork2d/Bork2d.slnx -c Debug

$env:BORK2D_RUN_GPU_TESTS = '1'
dotnet test src/Bork2d/Bork2d.slnx -c Debug --filter "Category=Gpu"

$env:BORK2D_RUN_AUDIO_TESTS = '1'
dotnet test src/Bork2d/Bork2d.slnx -c Debug --filter "Category=Audio"
```

Release-specific coverage:

- **Gamepad.** Enum mapping, hotplug order, connect/disconnect edges, focus parity, stick
  normalization, trigger range, duplicate pads, retained disconnected objects, freeze allocation,
  rumble queue, virtual-joystick end-to-end.
- **Audio.** ADTS parsing, encoder-delay trimming, PCM conversion, resampling, voice start/stop/
  pause/loop/end, gain and pitch ramps, snapshot reconciliation, one-shot ring and stealing, device
  switch failure, flat-plane direction vectors at center/edge/top/bottom and rotated cameras,
  HRTF block application, SDL lifecycle order, headless/offscreen silence.
- **Render.** Pass phase order, present-last validation, built-in 2D parity bytes, custom record
  capture, custom pass draws, custom vertex layout validation, pipeline cache keys, pending/failed
  pipeline and texture behavior, additive blend, culling, stats, capture.
- **Platform.** Linux loader/surface fixtures, lavapipe device features, macOS portability flags and
  bindless capacity, bundle native library presence.

Bake tests build assets in temporary directories. No binary audio fixture or generated pack is
committed. An AAC decode test may use a locally supplied fixture path and skip with a reason when it
is absent; authored Pong audio is content, not a fixture.

### Performance

Release measurements cover:

- The default sprite path still records one draw per camera and the 50,000-instance benchmark keeps
  its frame-rate floor and zero allocated bytes.
- The generic pass path adds no per-record virtual call and no steady-state allocation.
- Line and font runs keep their current draw-run counts.
- The audio mixer allocates nothing after warm-up; a 32-voice 48 kHz stereo block fits the audio
  block budget; no underruns in a long run.
- The AAC decode and Steam Audio apply costs are measured per voice and reported, not guessed.

## Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| The public pass/draw contract is too narrow for the built-in 2D and present passes. | Built-ins need a private path, violating the release rule. | Implement 2D and present first as contract clients; any needed capability becomes a bulk, public command; no internal recording shortcut. |
| The generic pass path regresses the sprite hot path. | Frame-rate or allocation gate fails. | Keep the 2D planner and run builder; benchmark 50k instances; assert one draw per camera and zero allocations at every render milestone. |
| Steam Audio native packaging or AOT fails. | Mandatory windowed startup cannot be delivered. | Spike load/context/HRTF/apply/AOT/publish on each RID before A2; pin the binding/native pair; fall back to a minimal hand-written binding for the same SDK if needed. |
| The SteamAudio.NET binding resolves only `phonon.dll`. | Linux/macOS startup fails. | Install a `NativeLibrary` resolver in `Bork2d.Audio`; test each RID's library name and path. |
| FFmpeg is unavailable or varies across hosts. | Audio bake fails or is not reproducible. | Pin version and hash, fail with `BK9404`, install it in CI, and measure encoder delay per build; document per-toolchain reproducibility. |
| Encoder delay is wrong. | Clicks at the start of every clip and broken loops. | Calibrate with an impulse fixture per pinned tool; store the measured value in the asset and the tool marker. |
| AAC decode or ADTS parsing is wrong. | Silent or corrupt playback. | Validate every header against the asset, add decode fixtures, and bound clip slots/decoded bytes with clear failures. |
| The mixer or decode thread races with assets/scene. | Crashes or stale audio. | Snapshot handoff, immutable PCM, no live objects on the mixer, bounded rings, and shutdown order tests. |
| The bindless split breaks 0.2 shaders or content. | Rendering regressions. | Land the split on all platforms, bump the layout version, re-bake built-in and Pong content, and run parity/capture tests before macOS. |
| MoltenVK reports fewer pages or lacks a required feature. | macOS startup fails. | Device-driven capacity, argument buffers, portability-subset validation, and a clear startup error; verify on the Mac before the gate. |
| macOS hardware is unavailable at release time. | macOS cannot be verified. | Hardware-gated runtime gate; CI builds/AOT; mark unverified and carry the gate to 0.4 rather than silently waiving it. |
| WSL's dzn path does not meet Vulkan 1.3. | Windowed WSL fails on a machine without lavapipe. | Document lavapipe as the WSL correctness path; treat vendor WSL ICD as best effort; native Linux is the performance target. |
| The bundled SDL2 Linux build has no Wayland driver. | Native Wayland desktops need XWayland. | Document XWayland; keep native Wayland for the SDL3 migration. |
| Gamepad drift with no deadzone. | Games feel broken on worn sticks. | Document that deadzone is game-owned; expose normalized drift faithfully; add a sample helper only if requested later. |
| Rumble is routed from the tick to the window thread. | Latency or dropped commands. | Latest-value coalescing per instance, non-blocking queue, capability flags, and fake-seam tests. |
| Native and encoder licensing is incomplete. | The audio stack cannot ship or distribute. | Ship Steam Audio's Apache/third-party notices and CIPIC acknowledgment; review FFmpeg's LGPL terms for the bake tool before distribution. |
| Breaking changes break existing games/content. | Upgrade friction. | Publish a 0.3 migration note and update all owning docs in the same change. |

## Out of scope

- Audio capture/microphone, occlusion, reverb, ambisonics, SOFA/HRTF customization, an effects graph,
  multiple listeners, split-screen audio, true world-space 3D audio, distance attenuation, and
  off-screen muting.
- Compressed-payload streaming from the pack, runtime clip unload, loop points, gapless loop
  metadata, loop crossfades, and runtime clip transcoding.
- Gamepad deadzone, response curves, gyro/accelerometer, LED, battery, touchpad fingers, player-slot
  assignment, and Steam Input-specific behavior.
- SDL3 migration, native Wayland, Linux ARM/32-bit, macOS Intel, iOS, Android, and consoles.
- macOS notarization, App Store distribution, a Metal backend, or a system MoltenVK requirement.
- Render graph, intermediate render targets, post-processing, ping-pong chains, compute, depth/
  stencil, cull/raster state, geometry shaders, tessellation, and custom blend equations beyond the
  three fixed modes.
- 3D rendering, a 3D scene, meshes with lighting, or a material asset shared across renderables.
- Runtime shader compilation, shader hot reload, and a `.pass` asset that replaces C# pass logic.
- A general job system, audio jobs, or asynchronous scene streaming.

## Later

After 0.3, the design leaves room for:

- SDL3, native Wayland, and a single gamepad/audio backend across desktop platforms.
- macOS Intel, notarization, and a distribution-grade bundle.
- True 3D audio with distance, occlusion, reverb, and multiple listeners.
- Audio loop points, gapless metadata, crossfades, streaming from the pack, and runtime device
  hot-swap.
- Render targets and a small render graph for post-processing, with the pass contract as the
  execution layer.
- Custom compute passes, depth/stencil, and a full pipeline-state asset.
- Material assets that share a pipeline and uniform values across renderables.
- Shader hot reload for development builds without changing the baked runtime path.
