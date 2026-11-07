# Linux and WSL — 0.3 Milestones

[Back to the 0.3 roadmap](../roadmap.md)

Linux and WSL workstream for Bork2d 0.3: portability fixes, the CI/offscreen GPU gate, the WSLg
windowed proof, and the L1-L3 milestones.

Owning docs: [runtime.md](../runtime.md) (host and native loading), [render.md](../render.md)
(device and surface), and [architecture.md](../architecture.md) (projects and platform seams).

## Goals

- The engine builds, tests, formats, and publishes on Linux with no Windows-only code path.
- Offscreen rendering and GPU tests run on Linux in CI using a software Vulkan 1.3 device.
- A windowed Pong runs under WSLg on the same software device, proving SDL windowing, Vulkan
  presentation, input, and audio correctness (not frame rate).
- Linux AOT publish works on Linux. Cross-OS AOT is not a goal.

## Scope and acceptance

The Linux acceptance has three tiers:

1. Build and test. `dotnet build` Debug and Release, `dotnet test`, and `dotnet format
   --verify-no-changes` on Ubuntu. No Vulkan device is required for the normal suite.
2. Offscreen GPU. Install Mesa's lavapipe ICD, force `VK_DRIVER_FILES` to it, and run
   `BORK2D_RUN_GPU_TESTS=1` with the offscreen suite. Verify that the device reports Vulkan 1.3,
   `textureCompressionBC`, descriptor indexing, dynamic rendering, and synchronization 2. Run Pong
   offscreen and capture a frame containing the court, paddle, wordmark, and HUD.
3. Windowed WSLg. On a WSLg desktop, run Pong windowed through XWayland and lavapipe. Verify the
   window, resize, cursor, input, audio device, and `CaptureFrameAsync`. Low frame rate is acceptable;
   correctness is not.

WSL's `dzn` Vulkan-on-D3D12 path is not a conformant Vulkan 1.3 implementation and does not meet the
engine's device floor. The documented WSL path is lavapipe for correctness. A vendor WSL ICD that
reports 1.3 and the required features is best effort, not a gate. Native Linux with a real GPU is
the normal high-performance path.

## Required changes

- Add a platform file-name table for Vulkan loader and native libraries in diagnostics and GPU test
  probes. Four GPU tests currently hardcode `vulkan-1.dll` and silently skip on Linux/macOS.
- Make the surface-extension fixtures express "the platform surface extension" rather than
  `VK_KHR_win32_surface`.
- Guard the Windows DPI hints so they are set only on Windows and the diagnostics do not claim
  Windows behavior on other platforms.
- Replace MSBuild `Exec` path literals that use `\` with separator-neutral paths in the Pong, assets,
  and built-in-pack targets.
- Add Linux runtime identifiers to the publish gates and docs. AOT must run on Linux; a Windows host
  cannot cross-compile it.
- Verify that the bundled `libSDL2-2.0.so` resolves from the app directory under NativeAOT; the
  NuGet native is the dependency, not a distribution SDL2 package. If the loader needs help, use the
  standard native-library search paths, not a second copy of SDL.
- Verify that `libvulkan.so.1` resolves from the system loader and that a missing loader produces a
  clear error.
- Keep the bake case-sensitive on Linux. A source tree that differs only by case is a duplicate error
  and must be fixed in the content.
- Bundle `libphonon.so` from the Steam Audio native package and verify it beside the published
  executable.

The bundled SDL2 Linux build supports X11, not native Wayland. Native Wayland is therefore out of
scope; WSLg and most desktops work through XWayland. SDL3 is the later fix, not a 0.3 dependency.

## Tests and milestones

L1 - Portability fixes. Loader names, surface fixtures, DPI hints, MSBuild paths, bake case behavior,
and diagnostics. Exit: a clean Ubuntu build, format, and test run; GPU tests no longer self-skip for a
Windows-only loader name.

L2 - Linux offscreen CI. Lavapipe install, forced ICD, Debug/Release build, full tests, GPU suite,
and a Linux AOT publish. Exit: the CI job is green on a clean runner and the captured offscreen Pong
frame contains the expected content.

L3 - WSLg windowed proof. SDL window, Vulkan surface, input, audio device, resize, and capture under
WSLg. Exit: Pong is playable and the capture matches the window; the run is documented as a local
correctness gate, not a performance gate.

