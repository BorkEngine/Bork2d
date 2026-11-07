# Gamepad Input — 0.3 Milestones

[Back to the 0.3 roadmap](../roadmap.md)

Gamepad input workstream for Bork2d 0.3: goals, options, public surface, semantics, SDL integration,
tests, and the G1/G2 milestones.

Owning docs: [engine.md](../engine.md) (input contract) and [runtime.md](../runtime.md)
(SDL, window, and input).

## Goals

- Every connected gamepad is visible through `IInput.Gamepads`; a game can read buttons, edges,
  sticks, triggers, identity, and rumble capability without owning SDL.
- Input still freezes exactly once per tick. A press or release is true for exactly one tick, a
  connect creates no button edge, a disconnect synthesizes releases, and focus loss clears held
  state.
- Hotplug, remap, and duplicate devices are normal, not exceptional.
- The 0.2 keyboard and mouse contract is unchanged.

## Options considered

| Option | Authoring | Hotplug | Platform cost | Verdict |
| --- | --- | --- | --- | --- |
| A. SDL2 GameController on `IInput.Gamepads` | Add a device table to the existing accumulator and snapshot | Five SDL events, instance ids | None: the functions are already bound and natives already ship | Selected |
| B. Separate `IEngine.Gamepads` service | A second input service | A second freeze and lifecycle | Splits one input model into two | Rejected |
| C. SDL3 gamepad API | Cleaner instance ids and positional names | Native SDL3 | No stable Silk.NET SDL3 package; a second major migration in the same release | Deferred |
| D. Raw joystick only | Manual mapping per device | Joystick events | No mapping database; games reimplement it | Rejected |

SDL2 is retained. `Silk.NET.SDL` 2.23.0 already binds the controller open/close, axis, button,
mapping, rumble, sensor, and capability functions, and ships SDL 2.32.10 natives for Windows, Linux,
and macOS. SDL3 would be a separate migration with a new binding package, not a gamepad detail.

## Public surface

```csharp
public interface IInput
{
    // Existing members are unchanged.

    /// <summary>Gamepads connected at this freeze, in connect order.</summary>
    IReadOnlyList<IGamepad> Gamepads { get; }
}

public interface IGamepad
{
    int InstanceId { get; }        // New on reconnect; unique for one connection.
    string Name { get; }
    string HardwareId { get; }     // SDL GUID string; duplicates are normal.
    GamepadType Type { get; }
    bool Connected { get; }
    int PlayerIndex { get; }       // SDL value, read-only; no engine assignment.

    bool IsDown(GamepadButton button);
    bool IsPressed(GamepadButton button);   // True for exactly one freeze.
    bool IsReleased(GamepadButton button);  // True for exactly one freeze.

    Vector2 LeftStick { get; }     // Normalized [-1, 1], +X right, +Y up, no deadzone.
    Vector2 RightStick { get; }
    float LeftTrigger { get; }     // [0, 1], no deadzone.
    float RightTrigger { get; }

    bool HasRumble { get; }
    bool HasRumbleTriggers { get; }
    void Rumble(float low, float high, TimeSpan duration);
    void RumbleTriggers(float left, float right, TimeSpan duration);
    void StopRumble();
}

public enum GamepadButton
{
    South = 0, East = 1, West = 2, North = 3,
    Back = 4, Guide = 5, Start = 6,
    LeftStick = 7, RightStick = 8,
    LeftShoulder = 9, RightShoulder = 10,
    DpadUp = 11, DpadDown = 12, DpadLeft = 13, DpadRight = 14,
    Misc1 = 15, Touchpad = 20,
}
```

`GamepadType` identifies the hardware family for UI glyphs (`Xbox360`, `XboxOne`, `PlayStation3/4/5`,
`SwitchPro`, `SwitchJoyconLeft/Right/Pair`, `Virtual`, `Unknown`). It does not affect button
semantics.

## Semantics

- `Gamepads` is a snapshot of the freeze, in connect order. A reconnect appends a new object with a
  new `InstanceId`; it never reuses the old position. The list capacity is eight; a ninth connect is
  ignored with one warning.
- A disconnected object stays valid with `Connected == false`, every query neutral, and
  `IsPressed == false`. The list no longer contains it. Retained references do not throw while the
  app is running; after shutdown they throw `ObjectDisposedException` like `IInput`.
- A connect seeds the physical button and axis state without creating a press edge, mirroring focus
  regain. A disconnect produces a release edge for every held button in the freeze that removes it.
- Focus loss synthesizes releases for held gamepad buttons and zeroes axes; focus regain re-seeds
  them from SDL without edges. This is required because SDL stops delivering controller events while
  the app is in the background by default.
- Sticks are normalized from the SDL `-32768..32767` range and `Y` is negated once so `+Y` is up.
  Triggers are `0..32767` mapped to `[0, 1]`. No deadzone is applied; a drifting stick reports its
  drift and the game owns deadzone and response curves.
- Rumble is an output command, not frozen input. It is queued to the window thread with the latest
  value per instance id and motor pair, applied on the pump, and never blocks the tick. A missing or
  disconnected pad drops the command. `HasRumble` and `HasRumbleTriggers` are frozen capability bits;
  an unsupported call is a no-op. `StopRumble` stops both motor pairs.
- Gamepads are never part of the frame snapshot and never cross to the render or audio thread.

## SDL integration

- Initialize the controller subsystem with the video subsystem. Load the mapping database before
  controller initialization so already-connected pads receive `ADDED` events with mappings.
- Map `CONTROLLERDEVICEADDED`, `REMOVED`, `REMAPPED`, `AXIS`, `BUTTONDOWN`, and `BUTTONUP` through the
  existing `ISdlApi`/`SdlEvent` seam. The seam exposes device indices and values, never native
  pointers, so `FakeSdlApi` can drive hotplug deterministically.
- `ADDED` carries a device index; `REMOVED` and `REMAPPED` carry an instance id. Open on `ADDED`,
  close on `REMOVED`, and refresh name/mapping on `REMAPPED` without creating edges.
- Close every open controller before `SDL_Quit`. The window-thread cleanup owns this, like text
  input.
- The positional button contract requires setting `SDL_HINT_GAMECONTROLLER_USE_BUTTON_LABELS` to
  `0` before controller initialization. A label-mode option is not exposed in 0.3.

## Tests and milestones

G1 - Core read path. SDL seam members, five events, device table in the accumulator, snapshot
entries, `GamepadList`/`GamepadProxy`, freeze/focus parity, mapping load, cleanup. Exit: a fake pad
connects mid-run, a button produces one press edge, a stick is normalized and Y-negated, a
disconnect releases held buttons and empties the list, a retained object reports
`Connected == false`, post-run access throws, and `Freeze()` allocates no bytes.

G2 - Rumble and integration. Capability flags, queued rumble, Pong gamepad control, a virtual
joystick window test, and a hardware checklist for Xbox, DualSense, and Switch Pro. Exit: Pong is
playable with a pad, rumble fires on a paddle hit, and hotplug/reconnect works on the checklist
hardware.

