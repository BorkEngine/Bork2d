# macOS — 0.3 Milestones

[Back to the 0.3 roadmap](../roadmap.md)

macOS workstream for Bork2d 0.3: Apple Silicon, MoltenVK, the bindless split, packaging, tests, and
the M1-M3 milestones.

Owning docs: [runtime.md](../runtime.md) (host, native loading, and AOT), [render.md](../render.md)
(device, surface, and bindless), and [architecture.md](../architecture.md) (platform seams).

## Goals

- The engine builds and AOT-publishes for `osx-arm64` on a Mac.
- A windowed Pong runs through MoltenVK on Apple Silicon with gamepad and audio.
- The bindless table works within MoltenVK's descriptor limits without lowering the engine's
  content model to a toy.
- The app ships as a `.app` bundle with its pack, MoltenVK, and Steam Audio native library.

## Scope

- Apple Silicon only. Intel Macs are not supported and fail with a clear message if attempted.
- macOS 13.3 or newer, which is the first version with Metal BC texture support through
  `supportsBCTextureCompression`.
- MoltenVK 1.4.x or newer, bundled with the app. A system MoltenVK installation is not required.
- Xcode Command Line Tools on the Mac for NativeAOT and MoltenVK packaging.
- No notarization or App Store distribution in 0.3. A locally signed/ad-hoc bundle that runs is the
  gate.

## MoltenVK and the bindless split

MoltenVK reports `maxPerStageDescriptorUpdateAfterBindSamplers = 16` even with Metal argument buffers,
and sampled images are limited to 16 without argument buffers and 128 with them. The 0.2 bindless
array is one combined image-sampler array with a 256-entry minimum, so it cannot fit.

The fix is a cross-platform bindless ABI change:

- Descriptor set 0 uses a sampled-image array with a variable count and a separate single sampler,
  instead of a combined image-sampler array. Shaders combine the two at sample time, for example
  `texture(sampler2D(images[index], sampler), uv)`.
- The shader prologue and every built-in and custom pipeline use the new layout. The shader layout
  version is bumped; 0.2 shaders must be re-baked.
- Texture-page capacity is device-driven. The renderer starts at a documented minimum that covers the
  built-in placeholder and the app's startup content, clamps to the device's update-after-bind
  sampled-image limit, and grows only up to that limit. A device with fewer pages than the required
  startup content fails with a clear message instead of silently dropping textures.
- Metal argument buffers are enabled in the MoltenVK configuration to reach the higher sampled-image
  limit. The exact configuration is part of the bundle and is tested on the Mac.

The split is not macOS-only. It lands on every platform so there is one shader ABI and one descriptor
layout. The existing 0.2 parity tests and the page growth/residency tests are the guard.

The instance-storage binding and the dynamic global uniform set remain. The split changes only the
texture binding and the shader prologue.

## MoltenVK integration

- Enable `VK_KHR_portability_enumeration` on the instance when advertised and set the enumerate
  portability instance flag; otherwise the loader hides the MoltenVK device.
- Handle `VK_INCOMPLETE` from physical-device enumeration if the loader returns it.
- Enable `VK_KHR_portability_subset` on the device when advertised and validate the portability
  subset features the engine uses. The engine uses no depth/stencil, no cull, and in-bounds vertex
  attributes, but this is asserted rather than assumed.
- Use `VK_EXT_metal_surface` from SDL's required extension list. SDL creates the surface on the
  window thread; only the opaque handle crosses to the render thread, as on Windows.
- Request at most three swapchain images; MoltenVK supports two or three. Keep the existing clamp.
- The windowed surface-recovery path stays the single recovery path.

## BC and memory

Apple Silicon supports BC through Metal's `supportsBCTextureCompression` from macOS 13.3. The engine's
`textureCompressionBC` requirement remains, and BC1/BC4/BC5/BC7 are the supported formats. BC is
emulated by the driver on Apple Silicon, so the KTX2 payload bytes used by the residency budget
understate real GPU memory. The budget remains payload-based for consistency across platforms; the
macOS docs state the undercount, and Pong's content fits comfortably.

A device or OS without BC fails renderer startup with the documented message. macOS 13.3 is the
minimum.

## Packaging and AOT

- Publish `osx-arm64` with NativeAOT on the Mac; cross-OS AOT is unsupported.
- Bundle layout: `Pong.app/Contents/MacOS/Pong.Desktop`, `Contents/Info.plist` with the bundle id,
  executable, minimum system version, and high-resolution flag, the game pack next to the executable
  at `Contents/MacOS/assets.pck` (or an explicit path supplied by the host),
  `Contents/Frameworks/libMoltenVK.dylib`, and the Steam Audio `libphonon.dylib`.
- SDL's Cocoa backend needs an app bundle to get a normal menu bar and activation; a bare executable
  is not the acceptance path.
- The app is locally signed. A hardened-runtime distribution build needs library-validation
  entitlements for the bundled dylibs; notarization is later.
- The built-in pack remains embedded; the game pack is the file next to the executable or the
  explicit path the app supplies.

## Tests and milestones

M1 - macOS build and AOT. `macos-latest` CI restores, builds Debug and Release, runs the normal tests,
and publishes `osx-arm64` with zero warnings. No render assertions on hosted macOS. Exit: green build
and publish.

M2 - MoltenVK runtime. Consume the R2 bindless split; add portability flags, portability subset,
Metal surface, device selection, BC, swapchain, and windowed Pong on the provided Mac. Exit: the
renderer runs with the device-reported sampled-image capacity under MoltenVK; Pong runs windowed with
correct court, paddles, wordmark, HUD, gamepad input, and audio; capture matches the window.

M3 - Bundle and release gate. `.app` layout, MoltenVK and Steam Audio dylibs, local signing, and the
documented hardware gate. Exit: the provided Apple Silicon Mac runs the bundled app with zero AOT
warnings and no missing native library.

