# M2 — SDL Windowed Platform (spec)

> **Status:** implemented (M2 WP1–WP5 on `m-2/common`; pending PR review). Decisions approved by the
> human owner on 2026-10-26; the approved reconciliations in §4.2 record the as-built details.
> This spec owns **M2 scope and semantics**. The [roadmap](../roadmap.md#5-milestone-detail) owns
> sequence and status, the [decision log](../decisions.md) owns accepted decisions, and
> [AGENTS.md](../../AGENTS.md) owns the workflow.
>
> **Outcome:** a window that ticks, reads input, and exits cleanly. No Vulkan, no renderer, no
> assets, no snapshot ring.

## 1. Goal

Replace the M1 one-tick smoke with a real Windows windowed host:

- SDL2 (via `Silk.NET.SDL`) creates one visible window in windowed mode.
- An engine-owned OS/window thread owns SDL, pumps events, and applies window commands.
- The tick thread runs the game loop (`Setup` → `Tick`… → `Teardown`) with no artificial pacing.
- `IWindow` and `IInput` become real in windowed mode; `ITime` is wired; `Exit`/close/`Dispose`
  shut down deterministically.
- Headless and offscreen keep the M1 one-tick behavior and never initialize SDL.
- `Bork2d.AotSmoke --windowed` proves the SDL path under NativeAOT locally.

M2 stops before Vulkan. The window is created with the Vulkan-capable flag so M6 can create a
surface on the same window, but M2 adds no GPU code, no render thread, and no present path.

## 2. Scope

### In scope

| Area | M2 deliverable |
| --- | --- |
| Dependency | `Silk.NET.SDL` pinned in `Bork2d.Runtime`; bundled SDL2 natives verified; distribution documented. |
| Window | Visible window at `Run()` start; title, framebuffer size, resizable, borderless fullscreen, cursor visibility, minimize state, close. |
| Threading | One OS/window thread; queued `IWindow` commands; tick-start input freeze; no public thread affinity. |
| Input | Physical scancodes (full set), five mouse buttons, position/delta/wheel, UTF-16 text events, edge accumulation, focus loss/regain. |
| Loop | Windowed loop until `Exit()`/close/fatal error; headless/offscreen keep one tick. |
| Time | `StopwatchTime` wired; first-tick semantics; `MaxDeltaTime` clamp. |
| Lifetime | `Run`/`Dispose`/`Exit`/close/post-shutdown contracts; window and thread destroyed deterministically. |
| Diagnostics | SDL version/driver/window/DPI/exit logging; no per-frame logging. |
| Verification | Deterministic fake-based tests; opt-in real-window tests; AOT headless + windowed runs. |

### Out of scope (do not implement in M2)

- Vulkan, a render thread, swapchains, surfaces, present, VSync effect, `WithValidation` effect.
- The snapshot ring, frame handoff, headless pacing (`ticksPerSecond`), offscreen rendering.
- Assets, pack loading, textures, sprites (the `IAssets` stub remains).
- Gamepad/joystick, touch, IME/composition, clipboard, drag-and-drop, window icons, message boxes.
- Multi-window, window show/hide APIs, display/resolution selection, exclusive fullscreen.
- Logical (point) public coordinates, DPI-scale exposure, `IsFocused`/`IsVisible` public members.
- Speed/keyboard-layout mapping: `KeyCode` is physical scancodes, not characters; text comes from
  `OnTextTyped`.
- `examples/` projects (M9); the M2 demo lives in `Bork2d.AotSmoke`.

## 3. Baseline and reconciliation

The M1 tree is the starting point ([M1 spec](m1-foundation.md)):

- `Bork2dAppBuilder` validates modes/options but creates no window or threads.
- `AppRuntime` runs `Setup` → one `Tick` → `Teardown` on the caller thread.
- `IEngine.Window` returns `UnsupportedWindow` in every mode; `IEngine.Input` throws;
  `ITime` is never advanced.
- `Engine<TLayer,TGame>.Run()` exists but is **never called**; the real sequence lives in
  `AppRuntime`.
- `IWindow` has `Title`, `Size`, `Fullscreen`, `CursorVisible`; `IInput` has the target shape;
  `KeyCode` has only `A`; `MouseButton` has only `Left`.

M2 changes or removes these statements (all intentional, recorded in §4):

| Current text | M2 change |
| --- | --- |
| `IWindow` is a throwing stub in every mode | Real in windowed; throwing stub in headless/offscreen. |
| `IInput` getter throws in every mode | Throws only without a window; real input in windowed. |
| `ITime` stays `0` | Wired for the windowed loop; remains `0` for the one-tick modes. |
| `IWindow.Size` is "client/framebuffer size" | Framebuffer **physical pixels**, get and set (§8). |
| Window hidden until first successful present (roadmap default) | Visible at `Run()` start, before `Setup` (D-M2-03). |
| `Dispose()` is safe before/after `Run()` | Still true; **during** `Run()` it throws (D-M2-18). |
| `Engine<TLayer,TGame>.Run()` exists | Deleted; the host owns the loop (D-M2-20). |
| "Escape" in the M2 exit criterion | The demo game handles Escape; the engine never binds it (D-M2-15). |
| Headless/offscreen become real loops in M2 | They stay one-tick until M7 (D-M2-01). |
| `Bork2d.AotSmoke` is one-tick headless only | Gains an opt-in `--windowed` demo; default is unchanged (D-M2-07). |
| `IInput`/`IWindow`/`ITime` lack XML docs (known gap) | Must be documented in this milestone. |

## 4. Locked decisions

All rows below were explicitly approved by the human owner on 2026-10-26. Implementers must not
re-litigate them; if the implementation cannot satisfy one, stop and report (AGENTS.md).

| ID | Decision |
| --- | --- |
| **D-M2-01** | **Windowed-only real loop.** Windowed runs an uncapped loop until `Exit`, close, or a fatal error. Headless and offscreen keep the M1 one-tick `Setup` → `Tick` → `Teardown` until M7. |
| **D-M2-02** | **Dedicated OS/window thread.** `Run()` in windowed mode creates one engine-owned OS/window thread; it owns SDL initialization, the window, event pumping, and all SDL window calls. The tick thread stays separate and never calls SDL. |
| **D-M2-03** | **Window visible at Run start.** The window is created visible before `Setup`; builder options are applied at creation. It is destroyed after `Teardown`, before `Run()` returns. |
| **D-M2-04** | **Queued `IWindow` commands.** Setters enqueue a command and return without blocking; getters return the last confirmed state. Commands apply within one OS pump; getters may lag one pump. |
| **D-M2-05** | **SDL lives in `Bork2d.Runtime`.** All SDL code is internal to the composition root; no new project, no dependency-matrix change. |
| **D-M2-06** | **Dependency:** `Silk.NET.SDL` **2.23.0** pinned in `Bork2d.Runtime`. The package brings `Ultz.Native.SDL` **2.32.10** transitively for win-x64 `SDL2.dll`, but the loaded binary reports runtime `SDL_GetVersion` **2.30.8** (and the same file version); the package label is not the runtime version. WP1 verified the native payload and AOT cleanliness against the 2.30.8 binary. |
| **D-M2-07** | **AOT smoke:** `Bork2d.AotSmoke` gains an opt-in `--windowed` demo. The default headless one-tick run and its assertions stay unchanged. Windowed AOT publish/run is a required **local** gate (never CI). |
| **D-M2-08** | **Testability:** an internal SDL adapter seam with fakes drives deterministic unit tests. Real-window tests run only with `BORK2D_RUN_WINDOW_TESTS=1`. CI scope is unchanged (no SDL/desktop tests). |
| **D-M2-09** | **Fullscreen:** borderless desktop fullscreen (`SDL_WINDOW_FULLSCREEN_DESKTOP`) on the primary monitor; leaving fullscreen restores the previous windowed bounds. No display/resolution/exclusive options. |
| **D-M2-10** | **DPI:** physical framebuffer pixels everywhere. `SDL_WINDOW_ALLOW_HIGHDPI` plus per-monitor DPI awareness; `WithSize` is the initial framebuffer size; `IWindow.Size` get/set is framebuffer pixels; mouse coordinates are converted to framebuffer pixels. |
| **D-M2-11** | **Minimize:** ticks continue while minimized; `IWindow.IsMinimized` is public; `IWindow.Size` keeps reporting the last non-minimized framebuffer size. |
| **D-M2-12** | **Mode is not exposed** on `IEngine` or `Bork2dApp`; hosts branch on their own configuration. |
| **D-M2-13** | **No other public surface.** The only public API changes are `IWindow.IsMinimized` and the full `KeyCode`/`MouseButton` member sets. |
| **D-M2-14** | **Input enums:** `KeyCode` mirrors the full SDL physical scancode set (~230 members; numeric values are SDL scancodes). `MouseButton` is `Left`, `Right`, `Middle`, `Back`, `Forward`. Numeric values are unstable and never serialized. Undefined values are safe queries that return `false` (no exception). |
| **D-M2-15** | **Escape is game-handled.** The engine never binds Escape. The window close button/`SDL_QUIT` always requests a graceful exit. |
| **D-M2-16** | **Time:** `Time` is `0` at the first tick, then raw (unclamped) monotonic seconds since that tick. `DeltaTime` is the raw interval between tick starts, exactly `0` on the first tick, clamped to `MaxDeltaTime`; no `TickCount`. |
| **D-M2-17** | **Post-shutdown:** after `Run()` returns or the app is disposed, retained `IWindow`/`IInput` references remain valid objects but their members throw `ObjectDisposedException`; `ITime` keeps its last values readable; `Exit()` is a no-op. The `IEngine.Window` getter itself never throws. |
| **D-M2-18** | **`Dispose()` during an active `Run()` throws `InvalidOperationException`** (from any thread); stop with `IEngine.Exit()` or the close button first. |
| **D-M2-19** | **Exit during `Setup`:** finish `Setup`, run **no** ticks, call `Teardown` once, return normally. This also covers a close request received during `Setup`. |
| **D-M2-20** | **Delete** the unused internal `Engine<TLayer,TGame>.Run()`; the tick loop lives only in `Bork2d.Runtime`. |
| **D-M2-21** | **Mouse/initial input:** `MouseDelta` is `MousePosition` minus the previous freeze position; it is zero on the first freeze, the focus-loss freeze, and the focus-regain freeze. `MousePosition` starts at `(0,0)` until the first mouse event. Input reads before the first freeze are legal and return a **fully empty** snapshot (all `Down` false, zero position/delta/wheel/text); keys held during `Setup` therefore produce press edges at the first tick. |
| **D-M2-22** | **Focus:** a focus-loss freeze synthesizes release edges for every held key/button, clears held state, wheel, and pending text, and reports delta `0`. A focus-regain freeze re-seeds held state from SDL (`Down` true, **no** press edges), re-syncs the position, and reports delta `0`. Focus transitions are applied in event order. |
| **D-M2-23** | **Text:** SDL text input is always enabled while focused; `OnTextTyped` receives UTF-16 `char`s (a surrogate pair is two events) on the tick thread **before** `Tick`. IME/composition is out of scope. Key-repeat `KEYDOWN` never creates press edges and the engine never synthesizes text from key presses; text comes only from `SDL_TEXTINPUT`. |
| **D-M2-24** | **`IWindow` details:** `Size` is ignored while fullscreen with a debug log (and updates the restore size while minimized); size components must be finite and positive (`ArgumentOutOfRangeException` otherwise); `CursorVisible` defaults to `true` and never auto-hides in fullscreen; `Title` requires non-null but allows empty/whitespace (unlike the builder's `WithTitle`). |

### 4.1 Roadmap open decisions resolved by this spec

[Roadmap §9](../roadmap.md#9-open-decisions) lists five M2 decisions. They are resolved as:

| Roadmap row | Resolution |
| --- | --- |
| Window shown before first frame or after first present | Visible at `Run()` start (D-M2-03); M6/M7 must not hide it until first present unless a new decision reverses this. |
| Fullscreen mode | Borderless on the primary monitor (D-M2-09). |
| Minimize: pause ticks or presentation only | Ticks continue; no presentation exists until M6; `IsMinimized` exposed (D-M2-11). |
| Expose the selected mode on `IEngine` | Not exposed (D-M2-12). |
| DPI policy; `IWindow.Size` under scaling | Framebuffer pixels (D-M2-10). |

### 4.2 As-built reconciliations approved during M2

These were reviewed and approved by the human owner while implementing WP1–WP5. They refine, and
do not reverse, the decisions in §4; the affected sections below are updated to match.

| Item | As-built rule |
| --- | --- |
| SDL native version | `Silk.NET.SDL` **2.23.0** brings `Ultz.Native.SDL` **2.32.10**, whose win-x64 `SDL2.dll` reports runtime `SDL_GetVersion` **2.30.8**. M2 is implemented and verified against 2.30.8; do not document or claim 2.32.10 at runtime (D-M2-06, §11). |
| DPI hints | The OS thread sets `SDL_WINDOWS_DPI_AWARENESS=permonitorv2` **and** `SDL_WINDOWS_DPI_SCALING=1` before `SDL_Init`. `WithSize`, `IWindow.Size`, and `IInput` mouse coordinates are framebuffer physical pixels; window points differ at non-100% DPI (§7.3, §8). |
| `KeyCode` names | Readable word splitting supersedes the older "mechanical PascalCase" wording: `PageUp`, `CapsLock`, `LeftCtrl`, `AudioNext`, `KeypadMemStore`; 247 members plus `Unknown = 0`; top-row digits are `Digit1`…`Digit0`; `MouseButton` is `Left`/`Right`/`Middle`/`Back`/`Forward` (§5.4, §17). |
| AOT suppression | NativeAOT publishes with Silk.NET's default resolver plus a narrow project-level `IL3000;IL3002` `NoWarn` in `Bork2d.AotSmoke` only. No other suppression is used (§11). |
| Fatal-path exception | On an unrecoverable SDL error after startup the OS thread may destroy the window before `Teardown`; the proxies remain attached for best-effort access. Normal, `Exit`, and close paths destroy the window only after `Teardown` (D-M2-03 remains true there; §7.4, §7.5). |

## 5. Public contract

### 5.1 `IWindow` (windowed)

```csharp
public interface IWindow
{
    string Title { get; set; }
    Vector2 Size { get; set; }
    bool Fullscreen { get; set; }
    bool CursorVisible { get; set; }
    bool IsMinimized { get; }     // new in M2
}
```

| Member | Status | Semantics |
| --- | --- | --- |
| `Title` | `Current` (M2) | Non-null string; empty/whitespace allowed. Initial value is the builder title. Setter queues; getter returns the last confirmed title (may lag one OS pump). |
| `Size` | `Current` (M2) | Framebuffer physical pixels. Getter: last confirmed **non-zero** framebuffer size (also while minimized). Setter: finite and `> 0` components, converted to window points with the current scale, queued; ignored while fullscreen with a debug log; while minimized it updates the size restored later. Rounding is an implementation detail; the getter reflects reality. |
| `Fullscreen` | `Current` (M2) | `true` = borderless fullscreen on the primary monitor; `false` = windowed at the previous bounds. Setter queues; same-value calls are dropped. Initial value from the builder. |
| `CursorVisible` | `Current` (M2) | Default `true`; no capture, no relative mode, no auto-hide. Setter queues. |
| `IsMinimized` | `Current` (M2) | `true` while the window is minimized, otherwise `false`. Read-only, readable from any thread. |

- In **headless/offscreen**, `IEngine.Window` returns the existing throwing stub: the property
  access succeeds and every member throws `NotSupportedException`. `IsMinimized` behaves the same.
- In **windowed after shutdown**, members throw `ObjectDisposedException` (D-M2-17).
- `IWindow` has no `IsFocused`, `IsVisible`, DPI scale, or framebuffer-size member (D-M2-13).
- Minimum/maximum size, position, icon, and monitor selection are not part of M2.

### 5.2 `IInput` (windowed)

The interface is unchanged; behavior becomes `Current` (M2).

| Member | Semantics |
| --- | --- |
| `event Action<char> OnTextTyped` | UTF-16 chars from `SDL_TEXTINPUT`, dispatched on the tick thread before `Tick`. Subscription/unsubscription is thread-safe. |
| `IsKeyPressed/Released(Down)` | Physical scancode edges/held state. Edges are true for exactly one tick; a tap inside one tick reports both pressed and released with `Down == false`; key repeat produces no edge. Undefined `KeyCode` values return `false` (D-M2-14). |
| `MousePosition` | Framebuffer pixels, top-left origin. Starts at `(0,0)` until the first mouse event; keeps its last value while unfocused; re-synced on focus regain. |
| `MouseDelta` | Difference between the current and the previous freeze position; zero on the first freeze, the focus-loss freeze, the focus-regain freeze, and while unfocused. |
| `MouseWheelDelta` | Accumulated wheel ticks since the previous freeze; positive = up/away; reset after each freeze; cleared on focus loss. |
| `IsMousePressed/Released/Down(MouseButton)` | Same edge/held rules as keys, for `Left`, `Right`, `Middle`, `Back` (X1), `Forward` (X2). Undefined values return `false`. |

- Input reads are frozen at tick start and stable for the whole `Tick`. Events arriving during
  `Tick` belong to the next freeze.
- Reads during `Setup` return the empty initial snapshot; reads during `Teardown` return the most
  recently frozen snapshot (D-M2-21).
- Text-handler exceptions are treated like a `Tick` failure (§7.5).
- Input is never part of a render snapshot; no gamepad/touch/IME (D-M2-23).
- In headless/offscreen, the `IEngine.Input` getter itself throws `NotSupportedException`; after
  windowed shutdown, the returned object's members throw `ObjectDisposedException`.

### 5.3 `ITime`

Interface unchanged. M2 wires it for the windowed loop (D-M2-16):

| Property | Semantics |
| --- | --- |
| `Time` | `0` on the first tick; afterwards raw monotonic seconds since the first tick (never clamped). |
| `DeltaTime` | Raw interval between tick starts, exactly `0` on the first tick, otherwise `min(raw, MaxDeltaTime)`. |
| `MaxDeltaTime` | Builder value (`0.25` s default), validated by `Build()` as in M1; it is a clamp, not a delay. |

- In headless/offscreen (one-tick), the clock is not advanced: both values stay `0` (M1 behavior
  retained until M7).
- Reads are thread-safe snapshots; no torn doubles. There is no `TickCount`.
- Focus loss and minimization do not pause the clock.

### 5.4 `KeyCode` and `MouseButton`

- `KeyCode` has **one member per SDL physical scancode** (247 members including `Unknown = 0`),
  named after the SDL name without the `SDL_SCANCODE_` prefix using readable word splitting, not
  mechanical concatenation: `SDL_SCANCODE_PAGEUP` is `PageUp`, `SDL_SCANCODE_CAPSLOCK` is
  `CapsLock`, `SDL_SCANCODE_LCTRL` is `LeftCtrl`, `SDL_SCANCODE_AUDIONEXT` is `AudioNext`, and
  `SDL_SCANCODE_KP_MEMSTORE` is `KeypadMemStore`. `KP_` becomes `Keypad` (`SDL_SCANCODE_KP_0` is
  `Keypad0`), `NONUS` becomes `NonUs` (`SDL_SCANCODE_NONUSBACKSLASH` is `NonUsBackslash`), and
  top-row digits are `Digit1`…`Digit0` because C# identifiers cannot start with a digit. Values
  equal the SDL scancode integers.
- `MouseButton` = `Left`, `Right`, `Middle`, `Back`, `Forward`.
- A test asserts every `KeyCode` value maps to exactly one SDL scancode and that names are unique,
  so an accidental SDL/binding update cannot silently mis-map input.
- Values are unstable across builds and must never be serialized (existing rule).
- Undefined values are safe queries that return `false`; they throw nothing.

### 5.5 Error contract (additions)

| Situation | Behavior |
| --- | --- |
| `SDL_Init`, window creation, or platform-thread startup fails | `Bork2dException` from `Run()` before `Setup`; include `SDL_GetError()`; no `Teardown`. |
| A fatal platform error after startup | Loop stops at the next tick boundary; `Teardown` runs; `Run()` throws the original `Bork2dException`. |
| `IWindow.Size` with `<= 0`, `NaN`, or infinity; `IWindow.Title = null` | `ArgumentOutOfRangeException` / `ArgumentNullException` from the member (setter), before enqueue. |
| `IWindow`/`IInput` member after shutdown | `ObjectDisposedException`. |
| `Dispose()` during `Run()` | `InvalidOperationException`. |
| Second `Run()`, `Run()` after `Dispose()` | Unchanged: `InvalidOperationException` / `ObjectDisposedException`. |
| Undefined `KeyCode`/`MouseButton` query | `false`. |
| Escape | Not an engine error; the game calls `Exit()` if it wants to. |

### 5.6 Documentation requirements

`IWindow`, `IInput`, `ITime`, `KeyCode`, and `MouseButton` gain XML docs (closing the
[contributing known gap](../contributing.md#4-c-conventions)). The reference/guide status
markers move to `Current` only for behavior actually implemented.

## 6. Modes and loop

| Mode | M2 behavior |
| --- | --- |
| **Windowed** (default) | Real window + OS thread + uncapped loop until `Exit()`, close, or fatal error. |
| **Headless** | Unchanged M1 one-tick path; no SDL, no window, no OS thread; `Window`/`Input` throwing stubs; time stays `0`. |
| **Offscreen** | Unchanged M1 one-tick path; no SDL, no Vulkan, no window, no OS thread; `Window`/`Input` throwing stubs; time stays `0`. |

### 6.1 Windowed loop

```
Run() [windowed, on the caller thread]
  platform = SdlPlatform.Start(options, logger)     // blocks until window created or failed
  attach platform.Window / platform.Input to the engine
  try
    if (!exitRequested && !fatal) game.Setup(engine)
    while (!exitRequested && !fatal)
        snapshot = platform.FreezeInput()           // tick-start snapshot
        time.NewFrame()
        DispatchText(snapshot.Text)                 // OnTextTyped, tick thread, before Tick
        game.Tick()
    // loop condition sees Exit/close/fatal
  finally
    if (setupSucceeded) game.Teardown()             // window and input are still alive here
    platform.Shutdown()                             // stop pump, destroy window, quit SDL, join OS thread
  propagate errors per §7.5
```

- `Exit()` finishes the current tick and stops the loop; no tick starts after the flag is
  observed.
- A close request between ticks stops the loop before the next freeze.
- A close request during `Setup` skips the loop entirely (D-M2-19).
- The loop has **no** target rate, frame limiter, deadline wait, `Thread.Sleep`, `SpinWait`, or
  `Thread.Yield`. In M2 it therefore consumes a full core; this is accepted until M7 supplies
  frame-handoff backpressure. Documented as temporary, not a design goal.
- The only blocking waits in the process are the OS thread's SDL event wait and ordinary locks;
  they are not pacing.
- No render thread, snapshot, or GPU work exists.

### 6.2 One-tick modes

`Setup` → exactly one `Tick` → `Teardown`, on the `Run()` caller thread, with no platform and no
clock advance. This preserves the M1 AOT headless gate and all one-tick tests. M7 replaces it.

## 7. Threading, startup, and shutdown

### 7.1 Threads (windowed)

| Thread | Created by | Owns | Never does |
| --- | --- | --- | --- |
| **Tick** | Host (the `Run()` caller) | `Setup`/`Tick`/`Teardown`, input freeze, time, exit decisions | Call SDL directly; touch the SDL window object |
| **OS/window** | `Run()` (windowed only) | `SDL_Init(SDL_INIT_VIDEO)`, window create/destroy, event pump, command application, focus/minimize/DPI state, `SDL_Quit` | Call game callbacks; touch Vulkan (M6 adds the rule) |
| Game workers | Game | Their own entities inside the tick window | Outlive `Tick` |

- No thread is the process main thread by requirement; the public API keeps no thread affinity
  ([D-024](../decisions.md#d-024-thread-affinity-free-api)). The OS thread is not required to be
  the process main thread; WP1/WP2 verify SDL video works from it on Windows.
- One app per process; concurrent windowed apps and multi-window hosting are unsupported
  (invariant 10). Sequential apps each initialize and quit SDL for their own `Run()`.

### 7.2 Window command and state handoff

- **Commands**: `Title`, `Size`, `Fullscreen`, `CursorVisible` setters validate, drop commands
  redundant with the last requested value, then enqueue into one MPSC queue and wake the OS thread
  with a registered SDL user event. The OS thread drains the queue in order and applies each
  command with SDL. Pending commands are coalesced per property (latest value wins) so a game that
  writes a window property every tick cannot grow the queue without bound; order between different
  properties is preserved.
- **Confirmed state**: after applying a command or receiving a window event, the OS thread
  publishes an immutable snapshot of `Title`, framebuffer size, scale, fullscreen, cursor
  visibility, minimized, and alive. Getters read the latest published snapshot; they never block
  and may lag by one pump.
- **No blocking on the OS thread**: the tick thread never waits for a window command. This
  preserves the any-thread API and prevents a deadlock once the render thread exists (M6/M7).
- **Wakeups**: a single registered SDL user event wakes `SDL_WaitEvent` for commands and stop.
  The pump drains commands before waiting and re-checks the queue after each wait to avoid a lost
  wakeup. No timeout is a pacing mechanism.
- **Locks**: one command-queue lock, one input-accumulator lock, and atomics/volatile flags for
  exit/fatal/focus. No lock is held across a game callback, an SDL call, or a blocking wait. The
  two locks are independent and are never held together; never hold the input lock while
  enqueueing a window command.
- **Fatal errors**: an SDL failure after startup is stored as a fatal exception, the exit flag is
  set, and the loop stops at the next boundary. The tick thread rethrows it after `Teardown`.

### 7.3 Window creation (OS thread, before `Setup`)

1. Set the DPI hints needed for per-monitor awareness **before** `SDL_Init`
   (`SDL_HINT_WINDOWS_DPI_AWARENESS=permonitorv2` and `SDL_HINT_WINDOWS_DPI_SCALING=1`, verified
   in WP2), then `SDL_Init(SDL_INIT_VIDEO)` only (no audio/gamecontroller/haptic). With scaling
   enabled, SDL reports window sizes and mouse positions in points while the framebuffer stays in
   physical pixels.
2. Create one visible window centered on the primary display (display 0) with:
   `SDL_WINDOW_VULKAN | SDL_WINDOW_ALLOW_HIGHDPI`, plus `SDL_WINDOW_RESIZABLE` when the builder
   allows it, plus `SDL_WINDOW_FULLSCREEN_DESKTOP` when `WithFullscreen(true)`.
3. Apply the builder title and initial framebuffer size. Because SDL takes window **points**,
   measure the achieved framebuffer size and, if it does not match the requested framebuffer size,
   adjust the window size once with the measured scale; log both sizes.
4. Read and publish the confirmed state, including the framebuffer/window scale.
5. Report success (or a `Bork2dException` with `SDL_GetError()`) to `Run()` before `Setup` starts.
6. The window is visible immediately; there is no hidden-then-show step and no wait for a first
   present (D-M2-03).

### 7.4 Shutdown order

Windowed `Run()` normal/exception path:

1. Stop freezing input and dispatch no further text.
2. `IGame.Teardown()` once, if `Setup` succeeded; the window and input are still alive here, so
   the game may set a final title/size. Such commands are best-effort: the window is destroyed
   immediately afterwards, so the change may not be observed.
3. Destroy the window and quit the SDL video subsystem on the OS thread.
4. Join the OS thread (bounded by actual SDL teardown, not a timeout-based sleep).
5. Return/throw to the caller.

**Accepted fatal-path exception.** On an unrecoverable SDL error after startup the OS thread stores
the fatal `Bork2dException`, requests an error exit, and runs its cleanup immediately; it may
therefore destroy the window before the tick thread reaches `Teardown`. The proxies remain attached
until the host shuts the platform down, so `Teardown` can still attempt best-effort access (getters
generally work; setters may be dropped). Normal, `Exit`, and close paths keep the window and input
alive through `Teardown` and destroy the window only afterwards, so D-M2-03 is unchanged there (see
D-033 and [Runtime lifecycle](../architecture/runtime-lifecycle.md#6-shutdown-and-error-propagation)).

`Bork2dApp.Dispose()` (after `Run()` or before it) detaches the root's direct children and disposes
the app-owned logger factory, unchanged. `Dispose()` during `Run()` throws (D-M2-18).

### 7.5 Error and lifecycle matrix

| Failure | Where | Behavior |
| --- | --- | --- |
| Invalid builder config / layer / conflicting modes | `Build()` | Unchanged `Bork2dException`; no window, thread, or SDL. |
| SDL init/window creation failure | `Run()` before `Setup` | `Bork2dException` with `SDL_GetError()`; no `Setup`, no `Teardown`; root empty; `Dispose` safe. |
| `Setup` throws | `Run()` | Platform shut down; original exception; no `Teardown`. |
| `Tick` throws | `Run()` | Platform shut down; `Teardown` attempted; original rethrown. |
| Text handler throws | `Run()` | Treated as a `Tick` failure: no further text dispatch, loop stops, `Teardown` attempted, exception rethrown. |
| `Tick` and `Teardown` both throw | `Run()` | `AggregateException` with both (M1 rule), after platform shutdown. |
| Fatal platform error after startup | OS thread → `Run()` | Loop stops at the next boundary; `Teardown` attempted; `Bork2dException` rethrown (aggregated if `Teardown` also throws). The OS thread's cleanup may already have destroyed the window before `Teardown`; the proxies stay attached for best-effort access (accepted D-033 exception). |
| `Exit()`/close during `Setup` | `Run()` | Finish `Setup`, zero ticks, `Teardown` once, normal return. |
| `Exit()` during a tick | loop | Tick completes; loop stops; `Teardown`; normal return. |
| `Dispose()` during `Run()` | `Dispose()` | `InvalidOperationException`; the loop is unaffected; caller must `Exit()`. |
| `Run()` twice / after `Dispose()` | `Run()` | `InvalidOperationException` / `ObjectDisposedException` (unchanged). |
| Post-shutdown `IWindow`/`IInput` member | member | `ObjectDisposedException`. |

### 7.6 Internal seams (no public contract)

These are internal structure requirements for WP4; they create no public types and can be
adjusted if the observable contract above still holds.

- `Build()` still creates the logger factory, internal engine, scene root, and app runtime, and
  still creates **no** SDL state, window, or thread.
- In windowed mode the engine is constructed with internal window/input **proxies** that are not
  attached yet; in headless/offscreen it keeps the throwing window stub and no input, so the
  `IInput` getter throws `NotSupportedException`.
- `Run()` attaches the live platform window/input to the proxies **before** `Setup`, and marks them
  disposed at the end of platform shutdown. Proxy members throw `NotSupportedException` only in the
  unattached state (not publicly reachable) and `ObjectDisposedException` after shutdown.
- `AppRuntime` hosts either the one-tick driver (headless/offscreen) or the windowed driver, sharing
  the M1 error/teardown rules. `Engine<TLayer,TGame>.Run()` is removed.
- `IEngine.Exit()` writes the same internal exit token that the windowed loop reads; the one-tick
  driver keeps ignoring it (M1 behavior).
- `StopwatchTime` accepts `MaxDeltaTime`, implements the zero-first-frame and raw-`Time` rules, and
  publishes thread-safe reads (one lock or an equivalent atomic snapshot); it is advanced only by
  the windowed loop.

## 8. Window semantics

| Aspect | M2 behavior |
| --- | --- |
| Visibility | Visible from creation at `Run()` start; no hide/show API. |
| Title | Builder title at creation; `IWindow.Title` updates queued and confirmed by getter. |
| Initial size | `WithSize(width, height)` = framebuffer pixels; at non-100% DPI the window's point size differs (D-M2-10). `WithFullscreen(true)` ignores the initial size until the window leaves fullscreen. |
| Resize | User resize allowed when `WithResizable(true)`; resize events update the confirmed framebuffer size and scale. Programmatic `Size` works even when not user-resizable. |
| DPI | `SDL_WINDOW_ALLOW_HIGHDPI` + per-monitor awareness and `SDL_WINDOWS_DPI_SCALING=1`; scale = framebuffer px / window points; log at startup and on change. Mouse coordinates are converted from points to framebuffer pixels. |
| Fullscreen | Borderless desktop on display 0; enter/leave via `Fullscreen`; leaving restores the previous windowed bounds (SDL-managed; verify); framebuffer size follows the desktop; `Size` while fullscreen is ignored with a debug log. |
| Minimize | Ticks continue; `IsMinimized` true; `Size` keeps the last non-zero framebuffer size; presentation is irrelevant until M6. |
| Cursor | Visible by default; `CursorVisible=false` hides it; no capture/relative mode; no auto-hide in fullscreen. |
| Focus loss | Synthesized releases, cleared wheel/text, frozen position, zero delta (D-M2-22). |
| Focus regain | Held state re-seeded from SDL (no press edges), position re-synced, zero delta (D-M2-22). |
| Close | `SDL_QUIT`/window close requests a graceful exit (same as `Exit()`); the window is destroyed at shutdown. |
| VSync / validation | Stored builder options; **no effect** in M2 (no GPU); log at Debug; M6 consumes them. |
| Multi-monitor | Out of scope; primary monitor only. DPI changes on move are observed as resize events if SDL reports them. |

## 9. Input pipeline

### 9.1 Accumulator (OS thread)

- Keyboard: SDL key events map through `KeyCode`; a non-repeat down on an up key queues a press
  edge and sets down; an up on a down key queues a release edge and clears down. Duplicate/ignored
  transitions produce no edges. Repeat `KEYDOWN` updates nothing.
- Mouse buttons: `SDL_BUTTON_LEFT/RIGHT/MIDDLE/X1/X2` map to the five `MouseButton` values; same
  edge rules; other buttons are ignored.
- Motion: `SDL_MOUSEMOTION` updates the window-point position; the freeze converts it to
  framebuffer pixels (points × scale). Delta is computed at freeze (not accumulated from event
  deltas).
- Wheel: `SDL_MOUSEWHEEL.y` accumulates, respecting the flipped flag; positive = up/away.
- Text: `SDL_TEXTINPUT` UTF-8 is decoded to UTF-16 chars and appended to a text queue; malformed
  UTF-8 is replaced, never throws.
- Focus loss/regain: as in D-M2-22, processed in event order. If loss and regain happen between
  two freezes, the next freeze reports the release edges from the loss and the re-seeded held
  state, with delta `0`.
- `SDL_QUIT` and window close set the exit request.
- Minimize/restore updates `IsMinimized`; it does not pause ticks or input.

### 9.2 Freeze (tick thread, once per tick)

1. Lock the accumulator; copy the down/pressed/released sets, mouse state, position, and wheel;
   transfer and clear the text queue; clear the pending edge/wheel state; unlock.
2. Compute delta: `0` if this is the first freeze, the focus-loss freeze, the focus-regain freeze,
   or while unfocused; otherwise `position - previousFreezePosition`. Store the current position as
   the next baseline.
3. Publish the immutable snapshot for this tick. Reads during `Tick` see only this snapshot.
4. Advance the clock, then dispatch the queued chars in order before `Tick`, so handlers observe
   the current tick's `ITime` values.

Steady state must not allocate on the freeze path; bitsets/masks and reusable buffers are
expected. Text buffering may allocate only when text actually arrives.

### 9.3 Edge cases

- Press and release in the same interval: `Pressed` and `Released` are both true, `Down` is false.
- Held across ticks: `Down` true, no edges after the first.
- Multiple press/release cycles inside one interval: the edges are still one tick; `Down` reflects
  the final state.
- Focus loss with a held key then regain before the next tick: release edge from the loss, `Down`
  re-seeded true, no press edge (D-M2-22).
- Input after shutdown: `ObjectDisposedException`.
- Undefined enum values: `false`.

## 10. Logging

Use `Bork2d.Runtime` ([categories](../architecture/logging.md#5-categories)); no per-frame,
per-key, or per-mouse-move logging.

| Event | Level | Fields |
| --- | --- | --- |
| Windowed platform start | Information | mode, SDL version, video driver, window points, framebuffer pixels, scale, fullscreen, resizable, vsync=stored |
| Windowed platform stop | Information | reason (`Exit`/`Close`/`Error`), ticks, elapsed seconds, final framebuffer size |
| Window command applied | Debug | command, confirmed value |
| Focus gained/lost, minimize/restore, DPI scale change | Debug | state |
| `Size` ignored while fullscreen | Debug | requested size |
| SDL failure | Error | operation + `SDL_GetError()` |

`Build()` keeps its existing startup line. The AOT smoke's default logging output must stay
trim/AOT-clean.

## 11. Dependency, NativeAOT, and deployment

- `Bork2d.Runtime` gains a pinned `PackageReference` to `Silk.NET.SDL` **2.23.0** (D-M2-06). No
  other project references it; `Bork2d.Api` stays free of SDL types; the dependency matrix is
  unchanged (D-M2-05).
- The package brings `Ultz.Native.SDL` **2.32.10** transitively; its win-x64 `SDL2.dll` is the
  native that ships. The loaded binary reports `SDL_GetVersion` **2.30.8** and the same file
  version, so M2 is implemented and verified against 2.30.8: the publish output contains the
  native, the runtime logs `sdlVersion=2.30.8`, and `SDL_Init`/create/destroy works. Do not
  describe the runtime as 2.32.10. WP1 verified the payload, and WP5 verified the windowed AOT run.
- NativeAOT publishes with Silk.NET's default resolver (`Sdl.GetApi()`). That resolver probes
  `Assembly.Location`, `Assembly.CodeBase`, and `DependencyContext`, which ILC reports as
  `IL3000`/`IL3002` single-file warnings. The approved, narrow mitigation is a project-level
  `NoWarn` for exactly those two codes in `Bork2d.AotSmoke`; no other suppression is used and
  `SuppressTrimAnalysisWarnings` is not set. The SDL2 native ships from the package and is resolved
  by the normal native-asset lookup, so the flagged fallback probes are not used. Remove the
  suppression when Silk.NET stops probing those APIs.
- **Stop and report** if the package ever loses its usable win-x64 native, needs a self-managed
  SDL2 binary, or produces AOT/trim warnings beyond the recorded `IL3000`/`IL3002` codes.
- AOT analyzers stay on for `Bork2d.Runtime`; no reflection/dynamic code is introduced.
- Headless/offscreen must never call any SDL function: no `SDL_Init`, no SDL P/Invoke. A test
  asserts the internal platform was never initialized; the AOT headless run remains the proof.
- Required local gates (CI does not run them): headless AOT publish/run (unchanged) and the new
  windowed AOT publish/run (§12.3).
- Document the native deployment: the DLL ships from the package; a clean machine needs the
  Windows desktop session and whatever runtime the SDL2 build requires. Full clean-machine
  validation remains M10.

## 12. Verification

### 12.1 Deterministic unit tests (CI, no SDL)

The SDL adapter seam is internal to `Bork2d.Runtime`; tests use fakes and are exposed through the
existing `InternalsVisibleTo` for `Bork2d.Tests`. No new test package.

| Area | Required cases |
| --- | --- |
| Input edges | press/release across ticks; press+release in one interval; repeat `KEYDOWN`; duplicate up; held state; five mouse buttons; unknown enum values return false |
| Text | UTF-8→UTF-16 incl. surrogate pair; order preserved; dispatched before `Tick`; cleared on focus loss; handler exception fails the run |
| Focus | loss synthesizes release edges and clears wheel/text; regain re-seeds `Down` without edges and zero delta; loss+regain in one interval; position re-sync |
| Mouse/wheel | initial `(0,0)`; first freeze delta `0`; delta = position difference; scale conversion; wheel accumulation/reset; unfocused delta `0` |
| Freeze | reads stable within a tick; events during `Tick` land in the next freeze; empty pre-first-freeze snapshot; `Setup` key presses produce first-tick edges |
| Window commands | any-thread enqueue; order preserved; same-value drop; getters last confirmed; `Size` validation; `Size` ignored while fullscreen; minimized restore size; disposed throws |
| Time | first tick `Time=0`, `DeltaTime=0`; monotonic `Time`; clamp; thread-safe reads; one-tick modes stay `0` |
| Loop/lifetime | `Exit` in `Setup` → 0 ticks; close in `Setup` → 0 ticks; `Exit` in `Tick` → tick completes; close between ticks; `Dispose` during `Run` throws; post-shutdown `ObjectDisposedException`; second `Run`; window failure before `Setup`; fatal mid-run; tick+teardown aggregate |
| Headless isolation | headless/offscreen build succeeds and no SDL platform is created; `Window`/`Input` stubs behave as documented |
| Enum mapping | every `KeyCode` maps to exactly one SDL scancode; unique names; mouse mapping for five buttons |
| Allocation | freeze with no text allocates nothing in steady state (after warmup); no per-tick garbage on the input path |

Existing M1 tests stay green **after a deliberate migration**: any test that builds the default
(windowed) mode and expects `Run()` to return after one tick must select `WithHeadless()` (or the
fake platform) instead, because windowed now loops until `Exit`. Windowed-loop tests use the fake
platform and assert tick counts/exit reasons. No test may spin a real window in CI.

### 12.2 Opt-in real-window tests (local only)

A custom `[WindowFact]` attribute sets xUnit's `Skip` unless `BORK2D_RUN_WINDOW_TESTS=1` (no new
package, no dynamic skip dependency). With the variable set, run:

- SDL init/create/destroy; window is visible; title round-trip; framebuffer size `> 0`; scale
  logged; fullscreen enter/leave restores a windowed size; cursor toggle; `IsMinimized` false;
  `Run()` exits on a close event (`SDL_PushEvent` of `SDL_QUIT`/window close) and on `Exit()`.
  The implemented suite also runs the real `Bork2dApp.Run()` twice: once with `Exit()` in the first
  tick (one `Setup`/`Tick`/`Teardown`, `Run()` returns) and once with a pushed `SDL_QUIT` after
  `Setup` signals (`reason=Close`). Both use bounded joins and signals, never sleeps.
- Run them on a desktop session only. CI does not set the variable and does not need a display.

### 12.3 AOT and acceptance runs

```powershell
# Headless (unchanged default; still one tick, no SDL)
dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj -c Release -r win-x64 --nologo
& src/Bork2d/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe

# Windowed demo (interactive; Escape or close exits)
& src/Bork2d/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe --windowed

# Windowed automated run (exits 0 after N ticks; used as the local evidence)
& src/Bork2d/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe --windowed --ticks 120
```

The `--windowed` demo must set a title, create a camera/transform tree, read input, move a
transform on arrow/WASD input, log input transitions (not every frame), honor Escape via
`Exit()`, and exit on close. `--ticks N` selects an automated run that calls `Exit()` after `N`
ticks (`N >= 1`) and exits `0`; without it the demo runs until Escape/close. The default
no-argument path must keep the M1 assertions (`Setup/Tick/Teardown = 1/1/1`, root cleared,
MessagePack shape).

### 12.4 Required gates (every WP, and the final branch)

```powershell
dotnet build src/Bork2d/Bork2d.slnx -c Debug
dotnet build src/Bork2d/Bork2d.slnx -c Release
dotnet test src/Bork2d/Bork2d.slnx -c Debug --no-build
dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
pwsh scripts/check-docs.ps1
```

Plus the AOT publish/run for runtime-affecting WPs, and `git diff --check`. CI remains
build/test/format on PRs to `develop`; no windowed or AOT job is added.

### 12.5 Acceptance evidence recorded by WP5

Run on Windows 11, 150% display scaling, SDK `11.0.100-rc.1.26425.128`:

| Gate | Result |
| --- | --- |
| `dotnet build ... -c Debug` / `-c Release` | Build succeeded, 0 warnings, 0 errors. |
| `dotnet test ... -c Debug --no-build` | 292 total, 283 passed, 9 skipped (the `[WindowFact]` real-window tests skip without the env var). |
| `BORK2D_RUN_WINDOW_TESTS=1 dotnet test ...` | 292 passed, 0 skipped, 0 failed; includes the two real-window `Run()` tests (game `Exit()` in the first tick and a pushed `SDL_QUIT`). |
| `dotnet format ... --verify-no-changes --no-restore` | Clean. |
| `pwsh scripts/check-docs.ps1` | Passed. |
| AOT publish (`-c Release -r win-x64`) | 0 AOT/trim warnings; `SDL2.dll` is in the publish output. |
| Headless AOT run (no args) | Exit 0 with the M1 assertions (`Setup/Tick/Teardown = 1/1/1`, root cleared, MessagePack shape). |
| `--windowed --ticks 120` | Exit 0; `sdlVersion=2.30.8`, window points 853×480, framebuffer 1280×720, scale 1.5006; `Setup/Tick/Teardown = 1/120/1`. |
| `--windowed --ticks 5000000` | Observed a live window (`MainWindowTitle = "Bork2d M2 Demo"`, `MainWindowHandle = 0x1913E4`) during the run; exit 0 after 5,000,000 ticks. |
| Interactive `--windowed` | Injected `D` press/release moved the player to `(3.07, 0)`; injected Escape logged the transition and exited 0 with `reason=Exit`. |

## 13. Work packages

The WPs are **sequential** (one wave each): they share `Bork2d.Runtime` internals and the same
docs, so parallel work would overlap. Branch model and review/merge rules are in
[AGENTS.md](../../AGENTS.md). All five are implemented on `m-2/common`; M2 is pending PR review.

| WP | Branch | Delivers | Depends |
| --- | --- | --- | --- |
| **WP1** Dependency and SDL spike | `m-2/wp-1` | `Silk.NET.SDL` 2.23.0 in `Bork2d.Runtime`; internal SDL adapter interface; a local create/destroy probe; native/AOT verification; dependency/AOT docs draft. No public API. | — |
| **WP2** Window platform | `m-2/wp-2` | OS/window thread; command queue + confirmed state; window creation (flags, title, size, DPI, fullscreen, cursor, minimize); event pump; `IWindow` (incl. `IsMinimized`); lifecycle, errors, logging; fake-based tests. | WP1 |
| **WP3** Input | `m-2/wp-3` | Full `KeyCode`/`MouseButton`; accumulator; edges/repeat; mouse/wheel; text decode/queue; focus loss/regain; freeze snapshot; fake-based tests. | WP2 |
| **WP4** Runtime wiring and loop | `m-2/wp-4` | Engine window/input attachment; windowed loop; `StopwatchTime`/clamp; `Exit`/close; one-tick modes preserved; `Dispose`/post-shutdown contracts; delete `Engine.Run`; lifecycle tests. | WP3 |
| **WP5** AOT demo, docs, acceptance | `m-2/wp-5` | `Bork2d.AotSmoke --windowed [--ticks N]` + demo game; API/reference/guide/lifecycle/threading/nativeaot/logging/projects updates; decision-log entries; roadmap resolution/status; full gates and acceptance evidence. | WP4 |

Each WP:

- is created from the latest `origin/m-2/common` and merged back only after review;
- runs the §12.4 gates (plus AOT when runtime-affecting) and reports commands/results;
- updates the owning docs in the same change;
- stops and reports instead of guessing when a spec/doc conflict, a public API change, a missing
  dependency/tool, an unexpected error, or a flaky test appears;
- uses commits `m2(wp-<K>): <imperative summary>`.

## 14. Acceptance checklist (milestone exit)

- [x] Windowed AOT-published demo opens a visible window, shows the builder title, and reports the
      requested framebuffer size at 100% DPI (and correctly converts at 150% DPI). Locally verified
      at 150% (1280×720 framebuffer in an 853×480 window); 100% is the identity path.
- [x] Input works end-to-end: held state, one-tick edges, text, mouse position/delta/wheel, five
      buttons; the demo prints/moves on input.
- [x] Focus loss synthesizes releases and clears wheel/text; focus regain re-seeds without press
      edges and reports zero delta.
- [x] Escape (game-handled) and the window close button both exit gracefully; `Run()` returns;
      `Teardown` ran once; window destroyed; OS thread joined; no hang or leak.
- [x] Borderless fullscreen enter/leave works; minimize keeps ticking and exposes
      `IsMinimized`; `Size` semantics match §8.
- [x] `ITime` follows D-M2-16 in the windowed loop; one-tick modes still report `0`.
- [x] No Vulkan dependency, instance, device, surface, or render thread exists.
- [x] No `Thread.Sleep`, target rate, frame limiter, or `SpinWait` in the windowed/offscreen path.
- [x] Headless/offscreen are still one tick and never initialize SDL (test + AOT headless run).
- [x] `Dispose`-during-`Run` throws; post-shutdown members throw `ObjectDisposedException`;
      `Exit()` after shutdown is a no-op.
- [x] All deterministic tests pass; opt-in window tests pass locally with the env var;
      `Debug`/`Release` have zero warnings; format and docs checks pass.
- [x] AOT publish has zero AOT/trim warnings; headless and windowed runs exit `0`; `SDL2.dll` is in
      the publish output.
- [x] Public surface is exactly the M1 surface plus `IWindow.IsMinimized`, the full `KeyCode` set,
      and the five `MouseButton` members; XML docs exist; no public types leaked from internal
      projects; dependency matrix unchanged.
- [x] Docs updated in the same milestone: reference, guide, lifecycle, threading, nativeaot,
      logging, projects, README map, roadmap §9/status, decision log.

## 15. Documentation updates (same change)

| Document | Required update |
| --- | --- |
| [`docs/api/reference.md`](../api/reference.md) | `IWindow` (real, `IsMinimized`, DPI/size/fullscreen/cursor/minimize, post-shutdown), `IInput` (real, focus/text/first-tick), `ITime` (M2), `IEngine.Window/Input/Exit` per mode, `KeyCode`/`MouseButton`, defaults table, errors table, status markers. |
| [`docs/api/guide.md`](../api/guide.md) | Window/input/time sections become `Current` for windowed; Escape/exit example; DPI and framebuffer-pixel note; headless/offscreen one-tick reminder. |
| [`docs/architecture/runtime-lifecycle.md`](../architecture/runtime-lifecycle.md) | Mode table, windowed startup/loop/shutdown sequence, `Dispose`/post-shutdown, window failure, one-tick modes, time, no Vulkan. |
| [`docs/architecture/threading.md`](../architecture/threading.md) | OS/window thread `Current` for M2; command queue, input freeze/handoff, wakeups, locking/ordering, shutdown, test strategy. |
| [`docs/architecture/nativeaot.md`](../architecture/nativeaot.md) | SDL dependency/natives, windowed AOT gate, headless-no-SDL rule, package pin. |
| [`docs/architecture/logging.md`](../architecture/logging.md) | Windowed startup/stop fields and no-per-frame rule. |
| [`docs/architecture/projects.md`](../architecture/projects.md) | `Silk.NET.SDL` in `Bork2d.Runtime`, status, no new project. |
| [`docs/architecture/rendering.md`](../architecture/rendering.md) | Window creation is M2; Vulkan/surface remains M6. |
| [`docs/decisions.md`](../decisions.md) | New accepted entries covering: OS/window thread + queued commands (extends D-024); windowed-only M2 loop + visible-at-Run; framebuffer DPI + borderless primary-monitor fullscreen; `IWindow.IsMinimized`; full scancode `KeyCode` + five buttons; `Silk.NET.SDL` 2.23.0/natives + AOT demo; M2 time/shutdown/`Dispose` contract. |
| [`docs/roadmap.md`](../roadmap.md) | §9: remove/replace the five resolved M2 rows; §5: M2 status/evidence links; §10: progress row when merged. |
| [`docs/README.md`](../README.md) | Add this spec to the documentation map. |
| [`docs/contributing.md`](../contributing.md) | SDL prerequisites; opt-in window-test env var; windowed AOT gate command. |

`AGENTS.md` is not modified; milestone content stays out of it.

## 16. Risks and stop-and-ask triggers

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Silk.NET.SDL package lacks usable win-x64 natives or needs extra deployment | Window cannot start on a clean publish | WP1 verifies publish output; stop and ask before adding a workaround. |
| Silk.NET.SDL triggers AOT/trim warnings | Gate failure | WP1 checks; isolate behind the adapter; never suppress without a decision. |
| SDL video init requires the process main thread | OS-thread design fails | WP1/WP2 verify on Windows; if it fails, stop and ask (do not switch threading models silently). |
| Uncapped tick loop pegs a core | Temporary CPU/battery cost | Accepted for M2; M7 adds backpressure; documented, not optimized away with sleeps. |
| Queued window commands lag or race with events | Getters stale, focus ordering bugs | Published-state snapshot, event-order tests, enqueue/apply tests with fakes. |
| DPI hint conflicts with host awareness; scale wrong | Size/mouse mismatch | Log scale; test 100/125/150/200%; verify `SDL_GetWindowSizeInPixels`; stop if the framebuffer contract cannot hold. |
| Focus/minimize event combinations between ticks | Wrong edges/held state | Deterministic fake event-order tests; §9.3 cases. |
| Opt-in window tests rot (CI cannot run them) | Late breakage | Keep them small, documented, and run before the milestone PR; the functional proof is the AOT demo. |
| SDL is a new native dependency in CI restore/build | CI failures | Windows runners restore nuget.org; no runtime SDL use in CI tests. |
| `Dispose`/`Run` concurrency | Races | Existing `_gate` state machine plus the new `Running` rejection and tests. |

## 17. Open questions

**No product or API decisions remain open.** Every choice needed by this spec was answered by the
human owner on 2026-10-26 and is recorded in §4.

### Derived details (confirm in review)

These follow mechanically from the approved decisions rather than from a separate question. They
are listed so review can reject any of them explicitly instead of discovering them in code:

1. A text-handler exception fails the run exactly like a `Tick` exception (Teardown, then rethrow).
2. The clock advances before text dispatch, so handlers observe the current tick's `ITime`.
3. `WithFullscreen(true)` ignores `WithSize` until the window leaves fullscreen.
4. Window commands coalesce per property; getters may lag one pump (D-M2-04).
5. Window commands issued during `Teardown` are best-effort before the window is destroyed.
6. `KeyCode` naming uses readable word splitting, not mechanical PascalCase: strip
   `SDL_SCANCODE_`, split recognized words, map `KP_` to `Keypad`, `NONUS` to `NonUs`, and a
   standalone top-row digit to `Digit` (`SDL_SCANCODE_PAGEUP` → `PageUp`, `SDL_SCANCODE_LCTRL` →
   `LeftCtrl`, `SDL_SCANCODE_KP_0` → `Keypad0`, `SDL_SCANCODE_1` → `Digit1`). The enum has 247
   members including `Unknown = 0`; a mapping test guards the set against the binding.

### Verification items (not decisions)

The following are *verification items* for WP1/WP2, not decisions. If one fails, stop and report:

1. The exact SDL version and native payload shipped by `Silk.NET.SDL` 2.23.0: the package carries
   `Ultz.Native.SDL` 2.32.10 and a win-x64 `SDL2.dll`, and the loaded binary reports
   `SDL_GetVersion` **2.30.8**. AOT publish copies the native. **Verified in WP1/WP5** (see §11).
2. Whether `SDL_HINT_WINDOWS_DPI_AWARENESS=permonitorv2` is honored when the host already set DPI
   awareness, and the resulting framebuffer/window scale.
3. Whether SDL video initialization and window creation work on the engine-created OS thread on
   Windows (expected yes; v1 is Windows-only, D-005).
4. SDL's restore behavior for windowed bounds after leaving borderless fullscreen.
5. Whether the library's event/marshaling APIs are AOT-safe with no suppressions.

## 18. Related documents

- [Roadmap](../roadmap.md#5-milestone-detail) — M2 scope and status; [open decisions](../roadmap.md#9-open-decisions).
- [Runtime lifecycle](../architecture/runtime-lifecycle.md#2-modes) — modes, startup, shutdown.
- [Threading](../architecture/threading.md#5-input-across-threads-windowed-only) — input handoff and thread ownership.
- [API reference](../api/reference.md#5-iwindow) — window/input/time contracts; [API guide](../api/guide.md#7-input) — usage.
- [NativeAOT](../architecture/nativeaot.md#6-native-libraries-and-silknet) — SDL/native constraints.
- [Logging](../architecture/logging.md#5-categories) — categories and default format.
- [Projects](../architecture/projects.md#3-public-surface-and-assembly-visibility) — public/internal boundary.
- [Decision log](../decisions.md) — accepted decisions; [M1 spec](m1-foundation.md) — baseline.
