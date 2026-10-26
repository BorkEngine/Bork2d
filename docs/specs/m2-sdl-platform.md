# M2 — SDL Windowed Platform (record)

> **Status:** implemented (WP1–WP5) and merged into `develop` on 2026-10-26 (PR #2). This is a
> historical record, not a live contract. The approved decisions are D-M2-01…D-M2-24 in the
> [decision log](../decisions.md#m2-platform-decisions-approved-2026-10-26); the living contracts are
> the [API reference](../api/reference.md), [API guide](../api/guide.md),
> [Runtime lifecycle](../architecture/runtime-lifecycle.md), and [Threading](../architecture/threading.md).

## 1. Goal and outcome

Replace the M1 one-tick smoke with a real Windows windowed host:

- SDL2 (via `Silk.NET.SDL`) creates one visible window in windowed mode.
- An engine-owned OS/window thread owns SDL, pumps events, and applies queued window commands.
- The tick thread runs `Setup` → `Tick`… → `Teardown` with no artificial pacing.
- `IWindow` and `IInput` become real in windowed mode, `ITime` is advanced, and `Exit`/close/`Dispose`
  shut down deterministically.
- Headless and offscreen keep the M1 one-tick behavior and never initialize SDL.
- `Bork2d.AotSmoke --windowed [--ticks N]` proves the SDL path under NativeAOT locally.

M2 stopped before Vulkan: the window was created with the Vulkan-capable flag, but there was no GPU
code, render thread, or present path.

## 2. Scope

**In scope:** the `Silk.NET.SDL` dependency and native distribution; visible/resizable window with
title, framebuffer size, borderless fullscreen, cursor, minimize state, and close; one OS/window
thread with queued `IWindow` commands and confirmed-state reads; the full physical-scancode input
set, five mouse buttons, position/delta/wheel, UTF-16 text, edges, and focus handling; windowed loop,
time, and lifetime; SDL diagnostics; deterministic fake tests plus opt-in real-window tests.

**Out of scope:** Vulkan, a render thread, swapchains, surfaces, present, VSync effect, validation
effect, the snapshot ring, headless pacing, offscreen rendering, assets/pack loading, gamepad/touch,
IME/clipboard/drag-and-drop, multi-window, display selection, exclusive fullscreen, logical public
coordinates, and a public focus/visibility/DPI-scale surface.

## 3. As-built notes

- **SDL version.** `Silk.NET.SDL` 2.23.0 brings `Ultz.Native.SDL` 2.32.10, whose win-x64 `SDL2.dll`
  reports runtime `SDL_GetVersion` **2.30.8** (same file version). M2 was implemented and verified
  against 2.30.8; 2.32.10 is only the package label.
- **DPI.** The OS thread sets `SDL_WINDOWS_DPI_AWARENESS=permonitorv2` and
  `SDL_WINDOWS_DPI_SCALING=1` before `SDL_Init`. Window sizes and mouse input are framebuffer pixels;
  at 150% scaling the window points differ (a 1280×720 request becomes an 853×480 window).
- **`KeyCode`.** The enum has **247 members including `Unknown = 0`**, named with readable word
  splitting (`PageUp`, `CapsLock`, `LeftCtrl`, `AudioNext`, `KeypadMemStore`; top-row digits
  `Digit1`…`Digit0`). `MouseButton` is `Left`/`Right`/`Middle`/`Back`/`Forward`.
- **AOT.** NativeAOT uses Silk.NET's default resolver plus a narrow project-level `IL3000;IL3002`
  `NoWarn` in `Bork2d.AotSmoke` only; no other suppression exists.
- **Fatal path.** An unrecoverable post-startup SDL error may destroy the window before `Teardown`;
  the proxies stay attached for best-effort access. Normal, `Exit`, and close paths destroy the
  window only after `Teardown` (D-033).

## 4. Evidence at merge

Run on Windows 11 at 150% scaling with SDK `11.0.100-rc.1.26425.128`:

| Gate | Result |
| --- | --- |
| Debug/Release build | 0 warnings, 0 errors. |
| Deterministic tests | 292 total: 283 passed, 9 skipped (`[WindowFact]`). |
| Tests with `BORK2D_RUN_WINDOW_TESTS=1` | 292 passed; includes both real-window `Run()` exit paths. |
| Format, docs, `git diff --check` | Clean. |
| AOT publish (`-c Release -r win-x64`) | 0 AOT/trim warnings; `SDL2.dll` in the publish output. |
| Headless AOT run (no args) | Exit 0 with the M1 assertions. |
| `--windowed --ticks 120` | Exit 0; `sdlVersion=2.30.8`, 853×480 window points, 1280×720 framebuffer, scale 1.5006, `Setup/Tick/Teardown = 1/120/1`. |
| `--windowed --ticks 5000000` | Live window observed (`MainWindowTitle = "Bork2d M2 Demo"`); exit 0. |
| Interactive `--windowed` | Injected `D` moved the player; Escape exited 0 with `reason=Exit`. |

## 5. Deliberate migration and verification notes

- Tests that built the default windowed mode and expected one tick had to select `WithHeadless()`
  (or the fake platform); windowed now loops until `Exit`.
- Windowed-loop tests use the internal fake SDL seam and assert tick counts/exit reasons; no test
  spins a real window in CI.
- Real-window tests use `[WindowFact]`, skip unless `BORK2D_RUN_WINDOW_TESTS=1`, and use
  signals/bounded joins, never sleeps.
- Verification items confirmed during implementation: DPI hints are honored; SDL video works on the
  engine-created OS thread; borderless fullscreen restores windowed bounds; the Silk.NET event and
  marshaling paths are AOT-safe with the narrow suppression.

No product or API decisions remain open; all are recorded as D-M2-01…D-M2-24.

## 6. Related documents

- [Roadmap](../roadmap.md#5-milestone-detail) — milestone sequence and status.
- [Decision log](../decisions.md#m2-platform-decisions-approved-2026-10-26) — D-M2 decisions and
  the consolidated D-027–D-033 entries.
- [Runtime lifecycle](../architecture/runtime-lifecycle.md#2-modes) — modes, startup, shutdown.
- [Threading](../architecture/threading.md#5-input-across-threads-windowed-only) — input handoff.
- [API reference](../api/reference.md#5-iwindow) — window/input/time contracts.
- [NativeAOT](../architecture/nativeaot.md#6-native-libraries-and-silknet) — SDL/native constraints.
- [M1 record](m1-foundation.md) — baseline.
