# Bork2d 0.3 Roadmap

Bork2d 0.3 turns a Windows-only, fixed-pass, keyboard-and-mouse engine into a portable engine with
gamepad input, spatial audio, and a renderer whose passes are all ordinary, user-extensible passes.
The release has five user-visible features: multi-gamepad input, an AAC asset and playback pipeline
with Steam Audio HRTF, a custom renderable and render-pass system, Linux/WSL support, and macOS
support.

This document is the overview and table of contents. The workstream plans and milestones live in
separate documents under [`docs/roadmap/`](roadmap/), and the cross-cutting release material lives in
[release.md](roadmap/release.md). The owning as-built documents remain
[engine.md](engine.md), [render.md](render.md), [bake.md](bake.md), [runtime.md](runtime.md), and
[architecture.md](architecture.md); a workstream updates its owning documents when it lands.

## 0.3 at a glance

| Goal | Outcome | Document |
| --- | --- | --- |
| Gamepad input | `IInput.Gamepads` exposes every connected pad as an `IGamepad` with positional buttons, analog sticks and triggers, hotplug, frozen edges, and rumble. | [gamepad-input.md](roadmap/gamepad-input.md) |
| Audio | `AudioClipAsset` is baked from common sources to AAC-LC; `Bork2d.Audio` plays it on a dedicated audio thread through SDL2; `IAudio` and `IAudioSource` expose devices, master volume, listener camera, 2D/3D sources, one-shots, pitch, looping, and Steam Audio HRTF. | [audio.md](roadmap/audio.md) |
| Render passes | Every frame pass, including the unified 2D pass and the present pass, is an `IRenderPass` registered in one ordered list. Users can write a custom renderable with its own vertex/instance ABI, a custom vertex and fragment shader, and a custom pass for it. | [render-passes.md](roadmap/render-passes.md) |
| Linux and WSL | The engine builds, tests, publishes, and renders offscreen on Linux; WSLg runs a windowed Pong locally. | [linux-wsl.md](roadmap/linux-wsl.md) |
| macOS | The engine builds and AOT-publishes for Apple Silicon, runs on MoltenVK with the split bindless table, and plays Pong with audio and gamepad. | [macos.md](roadmap/macos.md) |

## Workstream documents

| Document | Contents | Milestones |
| --- | --- | --- |
| [gamepad-input.md](roadmap/gamepad-input.md) | SDL2 controller integration, public surface, freeze/hotplug semantics, rumble, tests. | G1-G2 |
| [audio.md](roadmap/audio.md) | Bake and AAC policy, `Bork2d.Audio`, mixer thread, sources, listener/spatialization, Steam Audio, tests. | A1-A3 |
| [render-passes.md](roadmap/render-passes.md) | Public pass model, built-in 2D/present clients, bindless split, custom renderables and vertex ABI, tests. | R1-R5 |
| [linux-wsl.md](roadmap/linux-wsl.md) | Portability fixes, lavapipe CI, WSLg windowed proof, Linux AOT. | L1-L3 |
| [macos.md](roadmap/macos.md) | Apple Silicon, MoltenVK, bindless split, BC, bundle, AOT. | M1-M3 |
| [release.md](roadmap/release.md) | Release gates, baseline, decisions, integration, public API summary, verification, risks, scope. | I1 |

## Milestone map

| ID | Milestone | Document |
| --- | --- | --- |
| G1 | Core gamepad read path | [gamepad-input.md](roadmap/gamepad-input.md) |
| G2 | Gamepad rumble and Pong | [gamepad-input.md](roadmap/gamepad-input.md) |
| A1 | Audio bake and asset | [audio.md](roadmap/audio.md) |
| A2 | Audio runtime foundation | [audio.md](roadmap/audio.md) |
| A3 | Audio spatial and scene | [audio.md](roadmap/audio.md) |
| R1 | Pass framework and built-in clients | [render-passes.md](roadmap/render-passes.md) |
| R2 | Bindless split | [render-passes.md](roadmap/render-passes.md) |
| R3 | Custom kinds and passes | [render-passes.md](roadmap/render-passes.md) |
| R4 | Custom vertex ABI | [render-passes.md](roadmap/render-passes.md) |
| R5 | Render hardening | [render-passes.md](roadmap/render-passes.md) |
| L1 | Linux portability | [linux-wsl.md](roadmap/linux-wsl.md) |
| L2 | Linux offscreen CI | [linux-wsl.md](roadmap/linux-wsl.md) |
| L3 | WSLg windowed | [linux-wsl.md](roadmap/linux-wsl.md) |
| M1 | macOS build/AOT | [macos.md](roadmap/macos.md) |
| M2 | MoltenVK runtime | [macos.md](roadmap/macos.md) |
| M3 | Bundle and macOS gate | [macos.md](roadmap/macos.md) |
| I1 | Integration and release | [release.md](roadmap/release.md) |

Dependencies: G1-G2 and A1-A3 can run in parallel with the render work. A1 precedes A2; A2 precedes
A3. R1 precedes R2 and R3; R2 precedes R4's final shader ABI; R3 precedes R4. L1-L3 can start after
R1 and must be re-run after R2/R4. The macOS milestones depend on R1 and R2; M2 also depends on audio
and gamepad. I1 starts only after every other milestone is merged.

## Release gates at a glance

The full gate list is in [release.md](roadmap/release.md). In short, 0.3 is complete only when:

- every feature is usable through documented public APIs, not only through example code or tests;
- Pong plays with keyboard, gamepad, and audio, and every rendering pass, including 2D and present,
  is an ordinary registered pass;
- the Linux/WSL and macOS gates pass;
- the standard engine/example gates, the per-RID AOT publishes, and the 0.3 migration note are green.

## Confirmed decisions

The full table is in [release.md](roadmap/release.md). The load-bearing choices are:

- SDL2, positional gamepad buttons, no engine deadzone, rumble only, and `IInput.Gamepads` in
  windowed mode.
- Audio contracts in `Bork2d.Engine.Audio`, implementation in `Bork2d.Audio`, one dedicated mixer
  thread, Steam Audio mandatory in windowed mode, and silent offscreen/headless runs.
- AAC-LC/ADTS at 48 kHz, a pinned external FFmpeg for the bake, whole-payload residency, and
  `memory`/`stream` decode strategies.
- One public pass contract for all passes, custom renderables with a per-kind vertex/instance ABI and
  custom vertex shaders, fixed blend modes, and no intermediate render targets.
- Linux/WSL verified with lavapipe; macOS Apple Silicon 13.3+ with the bindless split and a
  hardware-gated runtime check.
- 0.3 may break public API where required, with a migration note and as-built docs.

## Verification, risks, and scope

The gate commands, environment-gated suites, performance measurements, risks, out-of-scope items,
and later work are in [release.md](roadmap/release.md).

## Reading order

1. Read [gamepad-input.md](roadmap/gamepad-input.md), [audio.md](roadmap/audio.md), and
   [render-passes.md](roadmap/render-passes.md) for the release work.
2. Read [linux-wsl.md](roadmap/linux-wsl.md) and [macos.md](roadmap/macos.md) for platform delivery.
3. Read [release.md](roadmap/release.md) for gates, decisions, integration, and verification.
