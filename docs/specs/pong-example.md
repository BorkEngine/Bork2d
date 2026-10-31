# Pong Example — Task Spec

> **Status:** merged into `develop` (2026-10-29, PR #10, merge `0273f58`). Implementation
> ran on `task/pong-example/wp-1` … `wp-5` and integrated through `task/pong-example/common`. This
> document is the scope authority for the task; the owner-approved decisions are D-043 and D-044.
> As-built evidence is in §12. **M7 (frame handoff and pacing) extends the host gates on
> `m-7/common`: `--headless` now runs 120 ticks by default with `--headless --ticks N` overriding,
> and the windowed/offscreen runs are ring-backpressured. M7 is implemented; its ready-for-review PR
> is open/awaiting human review and is not merged.** See §12.5.

## 1. Goal

Replace the exploratory example projects (`Bork2d.AotSmoke`, `Bork2d.BakeFixture`,
`Bork2d.BakeFixture.Assets`) with one real game solution, `examples/pong`, shaped like a normal
Bork2d game: `Pong.Common`, `Pong.Assets`, `Pong.Desktop`, plus `Pong.Tests`.

Pong is now the engine's living example and its NativeAOT gate. It should exercise the existing
public contract end to end: pack load, TOML data, `AssetRef` chains, baked textures, a custom bake
processor, sprites, camera, layers, tint, transforms, time, keyboard input, window title, logging,
and AOT/headless startup. There is no sprite renderer yet (M8), so the window is intentionally
black; the game is still playable as a simulation and observable through the title, logs, and
headless assertions. M6 adds the Vulkan clear and the three-mode AOT gate.

## 2. Non-goals

- No engine API, namespace, asset-format, or renderer change.
- No rendered court, score text, UI, sound, or AI difficulty settings; score and prompts use the
  window title until M8 (and a later UI milestone).
- No DLC/overlay pack, no struct-root data other than `GameSettings`, and no PPM/Unit acceptance
  processors; those were example-fixture concerns and are removed.
- No CI job for the examples solution; the local AOT gate covers Pong.

## 3. Target layout

```text
examples/pong/
  Pong.slnx
  Pong.Common/                  -> Bork2d.Engine only; AOT-compatible
    Pong.Common.csproj
    GameLayer.cs
    GameSettings.cs
    PongSide.cs
    PongSimulation.cs
    PongAi.cs
    PongGame.cs
  Pong.Assets/                  -> Bork2d.Bake + Pong.Common; JIT; build-time only
    Pong.Assets.csproj
    Program.cs
    Processors/CircleProcessor.cs
    Assets/
      art/paddle.png
      art/paddle.png.toml
      textures/ball.circle
      sprites/paddle.toml
      sprites/ball.toml
      game_settings.toml
  Pong.Desktop/                 -> Bork2d.Runtime + Pong.Common; AOT
    Pong.Desktop.csproj
    Program.cs
    PongArguments.cs
  Pong.Tests/                   -> Pong.Common, Pong.Assets, Bork2d.Bake, Bork2d.Runtime; xUnit
    Pong.Tests.csproj
    PongArgumentsTests.cs       -> links Pong.Desktop/PongArguments.cs
    ...
```

`examples/pong/Pong.slnx` lists the four Pong projects. `examples/Bork2d.Examples.slnx` and the
three old example projects are deleted.

References:

| Project | References | Must not reference |
| --- | --- | --- |
| `Pong.Common` | `Bork2d.Engine` | Runtime, Render, Bake |
| `Pong.Assets` | `Bork2d.Bake`, `Pong.Common` | Runtime, Render |
| `Pong.Desktop` | `Bork2d.Runtime`, `Pong.Common`; build-only `Pong.Assets` via the copy targets | Render, Bake in its compile/publish graph |
| `Pong.Tests` | `Pong.Common`, `Pong.Assets`, `Bork2d.Engine`, `Bork2d.Bake`, `Bork2d.Runtime` | Pong.Desktop |

`Pong.Assets` imports `build/Bork2d.Assets.targets`; `Pong.Desktop` imports
`build/Bork2d.Assets.Copy.targets` and sets `Bork2dAssetsProjectDirectory`,
`Bork2dAssetsProjectFile`, `Bork2dAssetsExecutable`, and `Bork2dAssetsTargetFramework`. AOT desktop
must not take a normal project reference to the JIT assets executable; the copy target builds it
out of process, exactly as the old AOT smoke did.

## 4. Assets

### 4.1 Paddle texture

`Assets/art/paddle.png` is a committed 16×96 white rounded PNG with transparent corners. It is
white so the per-player `Tint` produces clean red/blue. Sidecar `paddle.png.toml`:
`filter = "linear"`, `wrap = "clamp"`. Sprite `scale = 64` pixels per world unit gives a
0.25 × 1.5-unit paddle. The source PNG is generated once and committed; no build-time generator.

### 4.2 Circle processor and ball texture

`Assets/textures/ball.circle` contains exactly:

```json
{"resolution": 64, "size": 48}
```

`CircleProcessor : IAssetProcessor` is registered by `Pong.Assets` with `Extensions = ["circle"]`
and `SidecarPolicy.None`. It owns the JSON, not the TOML binder.

- `resolution` is the texture edge length in pixels; `size` is the circle diameter in pixels.
- Valid: `1 <= resolution <= 4096`, `1 <= size <= resolution`.
- It renders a white, straight-alpha, antialiased circle on a transparent background and emits
  `TextureDescriptor` at `textures/ball` plus RGBA8 at `textures/ball-raw`,
  `Format = Rgba8`, `Filter = Linear`, `Wrap = Clamp`.
- Unreadable/invalid JSON is `BK7001`; a valid JSON document with missing/out-of-range values is
  `BK7002`; a failed source emits no outputs.
- Output paths are base-path-derived (`BaseAssetPath`, `BaseAssetPath + "-raw"`).
- 64×64 with a 48-pixel diameter leaves an 8-pixel transparent margin; at 64 PPU the ball is
  0.75 world units in diameter.

### 4.3 Sprites

`Assets/sprites/paddle.toml`:

```toml
texture = "art/paddle"
uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]
scale = 64.0
pivot = [0.5, 0.5]
topology = "quad"

[bork2d]
type = "Bork2d.Engine.Assets.SpriteAsset"
```

`Assets/sprites/ball.toml` is identical except `texture = "textures/ball"`.

### 4.4 Settings

`Assets/game_settings.toml` bakes to the canonical root path `game_settings`:

```toml
paddle = "sprites/paddle"
ball = "sprites/ball"
paddleSpeed = 14.0
ballSpeed = 12.0
winningScore = 11
leftPaddleTint = [1.0, 0.1, 0.1, 1.0]
rightPaddleTint = [0.1, 0.3, 1.0, 1.0]

[bork2d]
type = "Pong.Common.GameSettings"
```

`GameSettings` is a public `readonly partial record struct` with `[GenerateShape]` and positional
members `AssetRef<SpriteAsset> Paddle`, `AssetRef<SpriteAsset> Ball`, `float PaddleSpeed`,
`float BallSpeed`, `int WinningScore`, `Color LeftPaddleTint`, and `Color RightPaddleTint`.

### 4.5 Bake configuration

`Pong.Assets/Program.cs` configures exactly one main pack:

```csharp
return await new BakeApplicationBuilder()
    .WithProjectRoot(".")
    .WithPack("assets.pck", "Assets")
    .UseDefaultAssets()
    .RegisterAsset<GameSettings>()
    .UseDefaultProcessors()
    .WithTextureDefaults(TextureFilter.Linear, TextureWrap.Clamp)
    .AddProcessor(new CircleProcessor())
    .Build()
    .RunAsync(args);
```

Expected `assets.pck` entries (7): `art/paddle`, `art/paddle-raw`, `game_settings`,
`sprites/ball`, `sprites/paddle`, `textures/ball`, `textures/ball-raw`.

## 5. Game contract

### 5.1 Layers, camera, entities

- `GameLayer`: `None = 0`, `Court = 1`, `Paddles = 2`, `Ball = 4`.
- One camera under `Root`: `Size = 9` (18 units tall), `Clear = Color`, dark clear color,
  `CullingMask = Court | Paddles | Ball`, `ZIndex = 0`.
- A `World` transform under `Root` owns the two paddles and the ball (parenting coverage).
- Both paddles share one loaded `SpriteAsset`; `Layer = Paddles`, `ZIndex = 10`. Left tint is
  `LeftPaddleTint` (red), right is `RightPaddleTint` (blue).
- Ball uses the ball sprite; `Layer = Ball`, `ZIndex = 20`, white tint.
- The court half-width follows the window aspect: `halfHeight = 9`,
  `halfWidth = 9 * framebufferWidth / framebufferHeight`; headless uses 16:9. Resizing the window
  resizes the court and clamps the entities.
- Paddle half-size is 0.125 × 0.75; ball radius is 0.375; paddle centers sit 1.0 unit from the
  side walls.

### 5.2 Simulation

`PongSimulation` is engine-free, deterministic, and public for tests. It owns paddle Y, ball
position/velocity, scores, pause, match-over, and the serve timer.

- Legal input is `[-1, 1]`; paddle speed comes from settings, clamped to the court.
- Initial serve delay is 0.5 s, later serves 0.75 s. The ball launches at `ballSpeed` with a small
  upward bias; a serve goes toward the side that conceded.
- Ball bounces off top/bottom walls and paddles; a paddle hit reflects horizontally with an angle
  derived from the hit offset, capped at 60 degrees. A missed ball scores for the other side.
- Reaching `WinningScore` ends the match. `ResetMatch` clears scores, paddle positions, pause,
  match-over, and restarts the opening serve.
- `ResizeCourt` clamps paddles and ball.
- Pausing stops the simulation; a finished match cannot be paused.

`PongAi.Steer(simulation)` is the right-paddle policy: track the ball with a small dead zone while
it approaches; drift to center when it moves away; return a value in `[-1, 1]` (use 0.85 speed so
the player can win).

### 5.3 Input and presentation

Windowed mode reads `IInput`:

| Key | Action |
| --- | --- |
| `W` / `S` | left paddle up/down |
| `Space` | pause/resume |
| `R` | reset the match |
| `Escape` | graceful `IEngine.Exit()` |

The right paddle is always AI. The window title is the temporary score/prompt surface and is only
updated when the text changes, never per frame:

- `Pong - 0 : 0 - W/S move, Space pause, R reset, Esc quit`
- `Pong - 3 : 2 - Serving...`
- `Pong - 3 : 2 - Paused - Space to resume`
- `Pong - 11 : 7 - Left wins - R to restart, Esc to quit`

Scores, pause, match end, and setup are logged at Information; no per-frame logging.

### 5.4 Loading and lifecycle

`Setup` loads, in order and synchronously (startup blocking is allowed by the API guide):
`GameSettings` from `game_settings`, the paddle and ball `SpriteAsset`s through their `AssetRef`s,
both `TextureDescriptor`s through the sprites' texture references, and the raw byte counts through
`TextureDescriptor.Payload`. These loads prove the full baked `AssetRef` chain. It also captures
`Browse().Count` for the smoke assertions.

`Setup` creates the camera/world/entities and the simulation. `Tick` uses
`IEngine.Time.DeltaTime` in windowed mode and a fixed 1/60 step in headless mode; headless never
reads `IInput` or `IWindow`. `Teardown` logs the final score and detaches the root's children.

## 6. Host modes

`Pong.Desktop`:

- no arguments or `--windowed`: interactive `1280x720`, resizable, max delta 0.25, pack from
  `AppContext.BaseDirectory/assets.pck`.
- `--windowed --ticks N`: automated windowed acceptance; exits 0 after exactly `N` ticks.
- `--offscreen --ticks N`: automated, non-interactive offscreen acceptance (M6); loads the same
  pack, runs `PongGame(interactive: false, tickLimit: N)` through `WithOffscreen(1280, 720)`, and
  exits 0 after exactly `N` ticks. The offscreen renderer clears a fixed sRGB target and discards
  continuous frames; the frame count is not asserted (D-M6-13).
- `--headless`: headless acceptance run; loads the pack, constructs the game with a tick limit,
  runs the paced headless loop for **120 ticks by default**, and `--headless --ticks N` overrides
  (Q15=O2/F3). It asserts setup/teardown, exactly `N` completed ticks, 7 pack entries, settings,
  sprites, texture dimensions (16×96 and 64×64), raw byte counts (16×96×4 and 64×64×4), tint
  values, and detached root, then exits 0.
- `--ticks N` is valid with `--windowed`, `--offscreen`, and `--headless`; offscreen requires it.
  The three modes are mutually exclusive. Invalid/duplicate/unknown/malformed arguments print usage
  and return 2.
- `PublishAot=true`, `InvariantGlobalization=true`, and the existing narrow
  `IL3000;IL3002` Silk.NET suppression (SDL and Vulkan resolve through the same Silk.NET default
  resolver).

## 7. Removal and test migration

Delete:

- `examples/Bork2d.AotSmoke`, `examples/Bork2d.BakeFixture`,
  `examples/Bork2d.BakeFixture.Assets`, `examples/Bork2d.Examples.slnx`;
- `Bork2d.BakeFixture`/`Bork2d.BakeFixture.Assets` project references from `Bork2d.Tests`;
- fixture-specific tests: the MSBuild fixture integration test, the fixture project/public-surface
  guards, and the PPM/Unit/Fixture-processor tests;
- `TempProcessRunner` and the separate-process fixture test.

Keep engine bake coverage by making the shared bake tests self-contained: add
`Bork2d.Tests.Bake.Baking.GameConfig`, `.Stats`, and `.StatsRoot` (same shapes as the old fixture
types) in the test assembly, replace the fixture type names in test TOMLs and registrations, and
keep the builder/processor/pipeline/CLI/incremental/texture tests. This is the owner-approved
"self-contained is good" choice.

## 8. Pong.Tests

`Pong.Tests` is in `examples/pong/Pong.slnx`, not CI. It covers:

- simulation: serve delay, ball movement, wall/paddle bounce, scoring, pause, match end/reset,
  resize, AI steering;
- bake/load integration: copy the real `Pong.Assets/Assets` tree to a temp project, bake through
  the public `BakeApplicationBuilder` with `CircleProcessor`, load the pack through a public
  headless `Bork2dAppBuilder` probe, and assert settings, 7 entries, sprite texture refs,
  descriptor dimensions, raw payload lengths, and circle alpha (center opaque, corner transparent);
- the full `PongGame` headless run (setup/tick/teardown, loaded assets, detached world) with an
  explicit tick limit so the K1 loop terminates;
- invalid `.circle` input returns a bake failure with a `BK7xxx` diagnostic;
- command-line parsing (`PongArgumentsTests`, linked from `Pong.Desktop/PongArguments.cs` so the
  test project does not reference the AOT host): default/windowed/headless/offscreen valid cases
  (including the 120-tick headless default and an explicit override), the required offscreen tick
  limit, headless/windowed/offscreen mutual exclusion, malformed `--ticks` values, and
  duplicate/unknown arguments.

The suite is **46 tests** after M7 (the 20 original Pong tests, 26 argument cases, and the
updated headless lifecycle/bake-load tests).

## 9. Work packages

Sequential; each branch starts from the latest `origin/task/pong-example/common`.

| WP | Branch | Scope | Acceptance |
| --- | --- | --- | --- |
| WP-1 | `task/pong-example/wp-1` | This spec and D-043/D-044. No code. | Docs link check passes; decisions are consistent. |
| WP-2 | `task/pong-example/wp-2` | Delete old examples/solution and fixture-specific tests; self-contained bake test types; update `Bork2d.Tests`, `RepositoryPaths`, dependency guards. | Engine solution builds/tests/format; no `examples` references remain in the engine solution. |
| WP-3 | `task/pong-example/wp-3` | Pong solution layout, `Pong.Common` (types/settings/simulation/AI), `Pong.Assets` (paddle PNG, circle processor, TOMLs, bake), `Pong.Desktop` headless host, pack copy. | `examples/pong` builds; `Pong.Assets` bakes 7 entries; headless smoke loads them and exits 0. |
| WP-4 | `task/pong-example/wp-4` | Full gameplay in `PongGame` (input, AI, title, score logs, resize) and `Pong.Tests` (simulation, bake/load, game smoke). | Pong tests pass; windowed `--ticks N` exits 0; title/prompt behavior covered. |
| WP-5 | `task/pong-example/wp-5` | AOT gate, docs (roadmap, projects, nativeaot, logging, API guide, README, contributing), full gates, PR. | AOT publish/run headless + windowed; docs check; PR merged to `develop` (2026-10-29, #10). |

## 10. Acceptance criteria

- `examples/` contains only `pong`; no references remain in current code, tests, or current docs
  (historical milestone specs, decision rows, and progress-log rows are retained).
- Engine solution: Debug/Release build, tests, and format pass with zero warnings.
- Pong solution: Debug/Release build, Pong tests, and format pass with zero warnings.
- `assets.pck` has exactly the 7 expected entries; `game_settings` is a `GameSettings` struct;
  the sprite/descriptor/raw references resolve.
- The circle processor produces a 64×64 white antialiased ball with a 48-pixel diameter and the
  expected RGBA8 payload.
- Windowed mode reads W/S, Space, R, and Escape; the right paddle is AI; the title shows the score
  and prompt/state; no per-frame logging.
- AOT publish contains `Pong.Desktop.exe`, `assets.pck`, and `SDL2.dll`, includes the `Pong.Common`
  game assembly (NativeAOT single-file links it; `Pong.Common.pdb` is the on-disk proof), and
  contains no `Pong.Assets`, `Bork2d.Bake`, Tomlyn, or StbImageSharp assembly, and no
  `vulkan-1.dll`; `--headless` (120 ticks by default), `--headless --ticks N`, `--windowed
  --ticks 120`, and `--offscreen --ticks 120` exit 0 (M6 adds the offscreen mode; M7 adds the
  120-tick headless default/override).
- `scripts/check-docs.ps1`, `git diff --check`, and the dependency/public-surface tests pass.
- `AGENTS.md` (already updated on `develop`) points the AOT gate and examples policy at
  `examples/pong/Pong.Desktop` and `examples/pong/Pong.slnx`.

## 11. Risks and deviations

| Risk | Mitigation |
| --- | --- |
| Deleting fixture projects breaks bake test coverage | Keep the non-fixture bake tests with test-local data types; Pong.Tests adds an end-to-end bake/load path. |
| AOT publish tries to include the JIT assets executable | Use the existing out-of-process copy-target pattern; verify the publish contents. |
| No renderer makes the game look empty | Document it; verify through title/logs/headless assertions; sprites/camera are already wired for M8. |
| Title updates on every tick | Compare the formatted title and only assign on change. |
| Resize changes the court aspect | Recompute half-width from the framebuffer and call `ResizeCourt` each windowed tick. |

## 12. As-built evidence

Implemented on `task/pong-example` from `origin/task/pong-example/common` (base `45a69b4` for WP-1;
the integration head before WP-5 was `4b434c5`); each work package was merged with `--no-ff` into
`task/pong-example/common`; the final PR #10 merged as `0273f58` on 2026-10-29.

| WP | Commits | Merge into `task/pong-example/common` | Delivered |
| --- | --- | --- | --- |
| WP-1 | `0f0b667` | `31c17cf` | This spec and D-043/D-044; no code. |
| WP-2 | `13d0ca2` | `a17bea9` | Deleted `Bork2d.AotSmoke`/`Bork2d.BakeFixture`/`Bork2d.BakeFixture.Assets`/`examples/Bork2d.Examples.slnx`; self-contained bake test data types; `Bork2d.Tests`/`RepositoryPaths`/dependency-guard updates. |
| WP-3 | `8d89ab8`, `801033b` | `c574e03` | `examples/pong` solution, `Pong.Common`, `Pong.Assets` (paddle PNG, `CircleProcessor`, TOMLs), `Pong.Desktop` headless host, pack copy. |
| WP-4 | `061720a` | `4b434c5` | Full `PongGame` gameplay/input/AI/title/logging/resize and the 20 `Pong.Tests`. |
| WP-5 | `6ab4c3c`, `8f0f376`, `c3852e5` | `fda4df8` | `PublishAot`/`InvariantGlobalization`/narrow `IL3000;IL3002` suppression, cold-clone assets restore in the shared copy target, current-doc migration from the removed examples to Pong, full gates, PR evidence. |

### 12.1 Tests

- `Pong.Tests`: **20 tests**, all passing (simulation, AI, title, bake/load integration, and the full
  headless `PongGame` run).
- `Bork2d.Tests` after WP-2: **907 passed + 12 skipped = 919 total**. The suite no longer references
  the removed examples; the former fixture/MSBuild/AOT integration tests are self-contained bake
  tests (`Bork2d.Tests.Bake.Baking.GameConfig`/`.Stats`/`.StatsRoot`).

### 12.2 AOT evidence

```powershell
dotnet publish examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Release -r win-x64 --nologo
& examples/pong/Pong.Desktop/bin/Release/net11.0/win-x64/publish/Pong.Desktop.exe --headless
& examples/pong/Pong.Desktop/bin/Release/net11.0/win-x64/publish/Pong.Desktop.exe --windowed --ticks 120
```

- Publish: **0 warnings, 0 errors**, zero AOT/trim diagnostics.
- Verified from a cold workspace (both Pong projects' `obj`/`bin` removed, no prior solution build):
  `Bork2d.Assets.Copy.targets` restores the assets project out of process before building it, so the
  direct publish command above succeeds on its own.
- Headless: exit `0`; `mode=Headless`, `validation=passed`, 7 pack entries, paddle 16×96 (6144 raw
  bytes), ball 64×64 (16384 raw bytes), setup/tick/teardown 1/1/1, world detached.
- `--windowed --ticks 120`: exit `0` after exactly 120 ticks (setup/teardown 1/1), SDL 2.30.8,
  1280×720 framebuffer.
- Publish contents: `Pong.Desktop.exe`, `assets.pck`, `SDL2.dll`, and the PDBs
  (`Pong.Desktop.pdb`, `Pong.Common.pdb`, `Bork2d.Engine.pdb`, `Bork2d.Render.pdb`,
  `Bork2d.Runtime.pdb`). NativeAOT single-file links the managed assemblies into the executable, so
  `Pong.Common.pdb` is the on-disk proof that the game assembly is in the publish graph (the old AOT
  smoke documented the same behavior for `Bork2d.BakeFixture.pdb`). The publish contains no
  `Pong.Assets`, `Bork2d.Bake`, Tomlyn, or StbImageSharp assembly.

### 12.3 Docs and gates

- `pwsh scripts/check-docs.ps1` passes; `git diff --check` is clean.
- Debug/Release builds, `dotnet test`, and `dotnet format --verify-no-changes` pass with zero
  warnings for both `src/Bork2d/Bork2d.slnx` and `examples/pong/Pong.slnx`.
- **Known Release-solution note:** a `dotnet build examples/pong/Pong.slnx -c Release` builds the
  external engine project references in **Debug** (the `.slnx` has no configuration mapping for
  projects outside `examples/pong`). The direct `dotnet publish … -c Release -r win-x64` above is the
  Release proof for the engine/desktop graph; the examples solution build is a shape/test gate.
- `docs/decisions.md` D-043/D-044 status is unchanged; the final merge status is left to the PR.

### 12.4 M6 extension: offscreen mode and the three-mode AOT gate (2026-10-30)

M6 (Vulkan bring-up; merged 2026-10-31, PR #11) adds D-M6-13:
`Pong.Desktop` gains `--offscreen --ticks N`, runs the non-interactive `PongGame` through
`WithOffscreen(1280, 720)`, and exits 0 after exactly `N` ticks. The offscreen renderer clears a
fixed 1280×720 `R8G8B8A8Srgb` target with two images and discards continuous frames; the frame
count is not asserted. `PongArguments` now selects windowed/headless/offscreen exclusively;
`--ticks N` is required for offscreen and allowed for windowed, and `Pong.Tests` links
`PongArguments.cs` to test the parser (41 Pong tests total: 20 original + 21 argument cases).

AOT evidence (`dotnet publish examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Release
-r win-x64 --nologo`, zero warnings; run from the publish directory):

```powershell
& .\Pong.Desktop.exe --headless                 # exit 0: 7-entry pack, 1/1/1, detached world
& .\Pong.Desktop.exe --windowed --ticks 120     # exit 0: 1/120/1, black clear, window destroyed
& .\Pong.Desktop.exe --offscreen --ticks 120    # exit 0: 1/120/1, sRGB target, renderer joined
```

The windowed run creates the SDL window, instance, surface, device, and swapchain on a Vulkan 1.3
device and presents a black clear; the offscreen run creates no surface/swapchain and discards
frames. The publish contains `Pong.Desktop.exe`, `assets.pck`, `SDL2.dll`, and the
engine/runtime/game PDBs, with no `vulkan-1.dll`, `Pong.Assets`, `Bork2d.Bake`, Tomlyn, or
StbImageSharp assembly. Full machine/GPU/driver/SDK details and the deviations are in the
[M6 as-built evidence](m6-vulkan-bringup.md#18-as-built-evidence-2026-10-30).

### 12.5 M7 extension: the 120-tick headless gate and the handoff host (2026-10-31)

M7 (frame handoff and pacing; implemented on `m-7/common`, PR open/awaiting review, not merged)
changes the three-mode gate without changing the Pong game contract:

- **Headless tick gate (Q15=O2/F3).** `Pong.Desktop --headless` now runs the paced K1 headless loop
  for exactly **120 ticks by default**; `--headless --ticks N` overrides with a positive `N` and the
  run asserts exactly `N` completed ticks. The usage, `PongArguments` XML docs, and
  `PongArgumentsTests` changed together: `--headless` defaults `TickLimit` to 120, `--ticks N` is
  accepted with `--headless`, `--ticks` without a mode is still rejected, and
  duplicate/malformed/zero/negative values are still rejected. `PongGame` needed no behavior change:
  it uses the fixed 1/60 simulation step in headless mode and calls `Exit` after the tick limit; the
  host passes the parsed limit. The run prints
  `Pong headless run completed: setup/tick/teardown=1/120/1, tickLimit=120, entries=7, ... exit 0.`
- **Windowed/offscreen handoff (Q22=V1).** `--windowed --ticks 120` and `--offscreen --ticks 120`
  are unchanged as commands, but the M7 host backpressures the tick through the three-slot ring and
  the renderer draws one black-clear frame per consumed snapshot. The runs assert exit 0, one
  `Setup`/`Teardown`, and exactly 120 completed ticks; they do not assert frame counts or duration.
- **Pong.Tests.** The suite is **46 tests**. The headless lifecycle test supplies an explicit
  `tickLimit` (3) so the K1 loop terminates and asserts `setup/tick/teardown = 1/3/1`; the
  bake/load probe game calls `Exit` after its single tick; the linked `PongArgumentsTests` covers the
  120 default, the override, and the headless malformed/duplicate cases.
- **AOT evidence.** The publish is zero-warning and contains `Pong.Desktop.exe`, `assets.pck`,
  `SDL2.dll`, and the engine/runtime/game PDBs, with no `vulkan-1.dll` and no
  `Pong.Assets`/`Bork2d.Bake`/Tomlyn/StbImageSharp assembly. The four published runs exit 0:
  `--headless` (`1/120/1`, `tickLimit=120`, 7 entries), `--headless --ticks 7` (`1/7/1`,
  `tickLimit=7`), `--windowed --ticks 120` (`1/120/1`; SDL 2.30.8, 1280×720, `B8G8R8A8Srgb`, FIFO,
  3 images; `snapshotsPublished=120`, `snapshotsConsumed=1`, `framesPresented=1`, zero validation
  errors/warnings), and `--offscreen --ticks 120` (`1/120/1`; 1280×720 `R8G8B8A8Srgb`, 2 images;
  `snapshotsPublished=120`, `snapshotsConsumed=1`, `framesDiscarded=1`, zero validation
  errors/warnings).
- **Known short-run observation.** The 120-tick windowed/offscreen gates publish 120 snapshots but
  consume only the first: the acceptance run exits and joins the renderer before it catches up, so
  the published-vs-consumed relationship is not steady state. This is not a frame-count assertion
  (Q22=V1); steady-state coupling/backpressure is covered by the M7 deterministic ring tests and the
  opt-in GPU handoff tests. The full M7 evidence is in the
  [M7 spec as-built section](m7-frame-handoff.md#18-as-built-evidence-2026-10-31).
