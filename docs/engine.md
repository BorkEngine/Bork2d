# Engine

`Bork2d.Engine` is the public contract plus its implementation. Games, the runtime host, the
renderer, and the bake tooling all depend on it; it depends on no windowing, GPU, or logging
provider package.

## Namespaces

| Namespace | Key public types |
| --- | --- |
| `Bork2d.Engine` | `IEngine<TLayer>`, `IGame<TLayer>`, `Bork2dException` |
| `Bork2d.Engine.Scene` | `ISceneRoot`, `ITransform`, `IBoundedTransform`, `IRenderable<TLayer>`, `ICamera<TLayer>`, `ISpriteRender<TLayer>`, `ILineRender<TLayer>`, `IFontRender<TLayer>`, `TextAlignment` |
| `Bork2d.Engine.Rendering` | `Color` (immutable linear RGBA), `RenderStats`, `ClearKind`, `FrameCaptureResult` |
| `Bork2d.Engine.Assets` | `IAssets`, `SpriteAsset`, `ShaderAsset`, `ShaderUniform`, `ShaderTextureSlot`, `ShaderUniformType`, `TextureDescriptor`, `TextureFormat`, `FontAsset`, `FontGlyph`, `FontKerning`, `AssetRef<T>`, `DataRef`, enums |
| `Bork2d.Engine.Assets.Pack` | `PackBuilder`, `PackCompressionMode` (tooling only) |
| `Bork2d.Engine.Input` | `IInput`, `KeyCode`, `MouseButton` |
| `Bork2d.Engine.Mathematics` | `Bounds`, `Ray2D`, `Vector2Extensions`, `Vector2ApproximatelyExtensions` |
| `Bork2d.Engine.Platform` | `IWindow`, `ITime` |

`GameEngine`, `Transform`, `Renderable2D`, `Camera`, `SpriteRender`, `LineRender`, `FontRender`,
`FontRenderData`, `FontLayoutData`, `FontSnapshot`, `FontRenderableDefinition`, `FontBounds`,
`AssetService`, `FrameSnapshot`, `ShaderRenderData`, the typed render-data
buffers/producers/definitions, `RenderableRegistry`, and the projection and sprite-bounds math are
internal.
`Bork2d.Engine.Assets.Pack` exposes only the pack writer and its compression enum; everything else
there is implementation.

## Game and engine

The game is a caller-constructed `IGame<TLayer>`: `Setup` runs once before the first tick, `Tick`
once per tick, and `Teardown` once before shutdown. `Setup` returns a `Task` that the host blocks on
before renderer readiness and the first tick. The host invokes the callbacks on the tick thread and
they are never concurrent: no `Tick` or `Teardown` starts while `Setup` runs, although a continuation
after an `await` inside `Setup` may resume on another thread. Exceptions, including a faulted setup
task, propagate to `Bork2dApp.Run` after cleanup; a `Setup` failure runs no `Tick` and no `Teardown`.

`IEngine<TLayer>` provides `Time`, `Window`, `Input`, `Assets`, `LoggerFactory`, `Root`,
`RenderStats`, `CreateTransform`, `CreateCamera`, `CreateSpriteRender`, `CreateLineRender`,
`CreateFontRender`, `SetGlobalUniform` (float, `Vector2`, `Vector3`, `Vector4`, and `Color`
overloads), `ResetGlobalUniforms`, `CaptureFrameAsync`, and `Exit`. Access rules differ by mode:

| Member | Windowed | Offscreen | Headless |
| --- | --- | --- | --- |
| `Window` | Live proxy during the run | Functional facade: `Size` is the fixed target extent, setters are inert, and nothing throws | Stub; members throw `NotSupportedException` |
| `Input` | Live proxy during the run | Getter throws `NotSupportedException` | Getter throws `NotSupportedException` |
| `CaptureFrameAsync` | Supported | Supported | Throws `NotSupportedException` |
| `RenderStats` | Last completed frame | Last completed frame | `default` |

Retained window/input references throw `ObjectDisposedException` after a windowed run; the offscreen
window facade stays functional. `Exit` may be called from any thread; the current tick finishes and
the next tick does not start.

### Global shader uniforms

`SetGlobalUniform` stores engine-wide user uniforms shared by every custom shader, and
`ResetGlobalUniforms` clears them. A name is matched ordinally and has exactly one
`ShaderUniformType` while it is set: setting an existing name with the same type
replaces its value, while setting it with a different type throws `ArgumentException` and leaves the
stored value unchanged. A null, empty, or whitespace name throws `ArgumentException`. These calls
may run on any thread and use a dedicated lock that is independent of the scene's structural lock.
User globals are copied into each frame snapshot after the scene walk. The built-in time input is
not a user global; the frame's time is captured separately from `ITime.Time`.

The renderer applies a captured user global to each shader run's 144-byte global block by its ordinal
name when the shader declares the same type, and uses the declaration's shader default when the name
is absent. A type mismatch between the engine value and a shader declaration makes that custom shader
fall back to its kind's default (`shader/sprite` for a sprite, `shader/line` for a line, or
`shader/sdf` for a font) for the frame with a one-time warning; the stored bytes are never
reinterpreted. The frame time always occupies the block's `time.x`.

### Frame capture

`CaptureFrameAsync` returns a `FrameCaptureResult`: row-major, top-left-origin RGBA8 pixels with
sRGB-encoded color channels, the captured frame's actual `Width`, `Height`, and `Format`
(`TextureFormat.Rgba8`). The copy is recorded from the final UNORM target after the present pass, so
the bytes match the presented image. `Pixels` is
exactly `Width * Height * 4` bytes and the result is a shared immutable capture result — every
request served by the same GPU frame observes the same memory, so callers must not mutate it. One
array is allocated per captured frame, not per request.

A request is served by the next frame recorded from a published snapshot, and the renderer completes
it from that frame's fence even when the game never publishes another snapshot. At most eight
requests may be outstanding at once, including requests already taken by an in-flight frame; a
request beyond the limit faults with `Bork2dException` instead of blocking the caller or being
dropped, and a slot returns only when its request completes, faults, or is cancelled. Cancellation is
checked first and faults the task with `OperationCanceledException`; a renderer stop or fatal faults
pending requests. Waiting for the task synchronously inside `Tick`, or awaiting it inside `Setup`,
before the snapshot that serves the request is published can block the producer needed to generate
that frame.

## Scene model

The scene is a strong tree rooted at `IEngine.Root`, a read-only identity transform. Any
`ITransform` can own children. A parent retains its children strongly; unparented objects are
collectible when game code drops them.

- `CreateTransform(parent)` creates a transform, optionally parented.
- `Parent = null` detaches without destroying and preserves the node's world transform; the same
  instance can be re-attached later.
- Re-parenting preserves the world transform. Cycles and parenting the root are rejected.
- `DetachAll()` clears one node's direct children while each direct child keeps its world
  transform; grandchildren keep their parents.
- There is no entity `Dispose`, liveness flag, ID, or ordered child enumeration.

Cameras are transforms plus projection and clear state: normalized `Viewport`, `Size` (half the
visible height), `CullingMask`, `Clear`, `ClearColor`, and `ZIndex`. `WorldSize`, `WorldToScreen`,
and `ScreenToWorld` use the engine window's framebuffer size and the same projection math as the
renderer: the windowed window, or the fixed offscreen target extent. Headless mode has no
framebuffer, so the helpers throw `InvalidOperationException`. A camera's world scale must stay `1`
(within `1e-4`), and its matrix must be invertible; otherwise the camera is skipped. Camera defaults
are a full viewport, size `10`, all layers, color clear to black, and `ZIndex` `0`.

Sprites, lines, and fonts implement `IRenderable<TLayer>`, the shared 2D renderable contract. It adds
the linear RGBA `Color`, the optional custom fragment `Shader`, the one-bit `Layer`, the shared
`ZIndex`, and the instance uniform members (`SetUniform` overloads and `ResetUniforms`) on top of
`IBoundedTransform`. A camera is only an `IBoundedTransform`; it is not drawn.

Sprite renderables are transforms plus `Sprite`, the shared `IRenderable<TLayer>` draw state, and
`FlipX`/`FlipY`. `SpriteAsset` is a MessagePack quad description: a `Texture` reference, four
normalized UVs (top-left, top-right, bottom-right, bottom-left), a per-axis `Vector2` world `Scale`,
a normalized pivot, a topology, and optional indices. The renderer derives the quad; there is no
vertex array. Quad world size is `(maxU - minU) * Scale.X` by `(maxV - minV) * Scale.Y`, so texture
pixel dimensions never affect geometry. `ZIndex` is the shared 2D draw order for sprites, lines, and
fonts: higher draws later, it is not layer-scoped, and equal values fall back to per-camera
screen-space Y then X. `Layer` is only a camera culling bit and never affects the order.

A sprite renderable also has an optional custom fragment `Shader` and up to four additional texture
references (`AdditionalTextures`). The `AdditionalTextures` setter clones the array and `null`
clears it; the getter returns a copy. Every entry is stored, but capture copies the first four plus
the total count, so the renderer can warn about and ignore references beyond four. Assigning a
shader freezes its declarations once, and capture carries only that frozen `ShaderRenderData`, never
the live `ShaderAsset`.

The renderer resolves a captured sprite's shader from that frozen metadata. No assigned `Shader`
selects the built-in `shader/sprite` entry; a custom shader is keyed by its raw SPIR-V `DataRef` path
and loaded asynchronously, so a sprite waits (and is counted as skipped) until its frame becomes
ready. A custom shader that fails loading, validation, module creation, or pipeline creation draws
with `shader/sprite` and logs one warning per shader path. Its declared additional textures are
resolved positionally in the first four `AdditionalTextures` slots; a still-loading extra texture
skips the sprite, a failed or absent one samples the bindless placeholder, references beyond the
shader's declared slot count are not loaded, and references beyond four are ignored with a one-time
warning. A catalog mount reloads a custom path from the new winning entry on its next sighting,
retries a previously failed path, and reloads all three default shaders while keeping each active
until its replacement is ready.

Instance uniform values are strict. The `SetUniform` overloads validate immediately against the
currently assigned shader's frozen metadata: with no shader assigned, an undeclared name, a
declared type that does not match the overload, or a null/empty/whitespace name, the call throws
`ArgumentException` and stores nothing. Accepted values are stored per renderable, and
`ResetUniforms` clears them so the shader defaults apply again. Reassigning `Shader` does not clear
stored values; a value whose name or type no longer matches the new shader is inert at capture.
Capture applies the stored overrides over the frozen default block without allocating on the warm
path. A sprite with no custom shader has a zero instance-uniform block in the snapshot; the renderer
seeds it from the default entry's declared default block, so a game pack that shadows
`shader/sprite` with instance-uniform declarations behaves correctly without per-sprite overrides.

Line renderables are transforms plus an ordered local-space polyline — `Points`, `Closed`, `Width`,
and `AntiAlias` — and the shared `IRenderable<TLayer>` draw state. Defaults are empty points, an
open line, width `1`, white, anti-aliasing on, no shader, layer `0`, and z-index `0`. `ZIndex` is
the shared 2D draw order described for sprites: higher draws later, it is not layer-scoped, and
equal values fall back to per-camera screen-space Y then X. `Layer` is only a camera culling bit.
The `Points` setter copies the supplied list and stores `null` as empty; the getter returns a cached
read-only view of that copy, so mutating the caller's array cannot change a renderable or a
published snapshot and reading it allocates nothing. A line with `Layer` `0` or a combined mask is
captured as-is and skipped later, exactly like a sprite.

The engine expands a line on the CPU into immutable local-space triangles with per-vertex line UVs
and `uint32` indices, and caches the result per renderable. Points, width, and closed state rebuild
it; color, anti-aliasing, shader, layer, z-index, and transform changes do not. An open line needs
at least two distinct points after duplicate consecutive points are skipped, and a closed line
needs at least three; a closed line drops a final point equal to the first before joining the last
point to the first. Every point must be finite and `Width` must be finite and greater than zero;
otherwise the line is captured as not drawable and the renderer skips it later with a one-time
diagnostic, while capture and bounds reads never throw. Open ends are butt caps, interior corners use
a miter join with limit `4`, a miter beyond the limit becomes a bevel, and a closed line has no caps.
Line UV `x` runs from `0` to `1` across the width and `y` is the accumulated local-space distance
along the line.

A line's `Shader` uses the same freeze rule as a sprite's, so capture carries only the frozen
`ShaderRenderData`. A line without a custom shader resolves to the built-in `shader/line` entry; a
custom line shader is keyed by its SPIR-V path and loaded asynchronously, so a still-loading shader
skips the line (and counts in `SkippedLines`), while a failed, invalid, or conflicting shader draws
with `shader/line` and logs one warning per path. Instance uniform values follow the same strict
rules: `SetUniform` validates immediately against the assigned shader's frozen declarations and
stores accepted values per renderable, `ResetUniforms` clears them, reassigning `Shader` keeps them,
and capture applies only values whose name and type still match. A line without a custom shader has
a zero instance-uniform block in the snapshot; the renderer seeds it from `shader/line`'s declared
default block, so a pack that shadows the line default with declarations behaves correctly.

Font renderables are transforms plus a frozen `FontAsset`, `Text`, `Size` (world units per em),
`Alignment`, and `LineSpacing` (a multiplier of the default line height), together with the shared
`IRenderable<TLayer>` draw state. Defaults are no font, empty text, size `1`, left alignment,
spacing `1`, white, no shader, layer `0`, and z-index `0`. The local origin is the first line's
baseline at `y = 0`; lines advance downward by
`(Ascent - Descent + LineGap) * Size * LineSpacing`, and each line is shifted around `x = 0` by its
alignment: left starts at zero, center shifts by minus half its advance width, and right shifts by
minus the whole width. `\n`, `\r\n`, and a lone `\r` start a new line, and a trailing break
contributes a final empty line to measurement and height. Layout uses the frozen metrics, glyph
plane bounds, advances, and kerning; the bearing is descriptive only. It is cached and rebuilt only
when `Font`, `Text`, `Size`, `Alignment`, or `LineSpacing` changes, so transform, color, layer,
z-index, shader, and uniform changes never rebuild it and a warm capture or bounds read allocates
nothing.

A missing codepoint renders `?` when the frozen font has that glyph; otherwise the codepoint is
skipped and one warning per codepoint per renderable is logged. A skipped codepoint is absent for
kerning. A non-finite or non-positive `Size` or `LineSpacing`, a non-finite metric, or a
non-positive line height produces an empty layout and one warning, and never makes capture or
bounds throw. An undefined `TextAlignment` value behaves as left.

Assigning a font freezes its atlas reference, metrics, glyphs, and kerning once; mutating the asset
afterwards cannot change a cached layout, a bounds read, or a published snapshot, and re-assigning
the same asset keeps the first frozen values. `FontRender.Bounds` is the world-space AABB of the
laid-out glyph quads, not the layout box; empty text, a null font, an empty layout, or a layout with
no drawable quads returns `Bounds.Empty`.

A font's `Shader` uses the same freeze rule as a sprite's or line's, so capture carries only the
frozen `ShaderRenderData`. A font without a custom shader resolves to the built-in `shader/sdf`; a
custom fragment shader is drawn with the font glyph instance ABI, and a failed or invalid custom
shader falls back to `shader/sdf` with a one-time warning. Instance uniform values follow the same
strict rules: `SetUniform` validates immediately against the frozen declarations, `ResetUniforms`
clears them, reassigning `Shader` keeps them, and capture applies only values whose name and type
still match. A font without a custom shader has a zero instance-uniform block; the renderer seeds
the SDF default's declared defaults. A still-loading custom shader or atlas page skips the font
for that frame and counts in `RenderStats.SkippedFonts`; submitted glyph instances count in
`RenderStats.Glyphs`, while an empty layout draws nothing and is not a skip.

`FontAsset.MeasureText(ReadOnlySpan<char>, float)` returns the layout box for a text and size: X is
the widest line's advance width including kerning and spaces, and Y is the line count times the
default line height. It ignores alignment and uses a line-spacing multiplier of `1`, uses the same
`?`/skip missing-glyph rules without logging, reads the asset's current fields (there is no frozen
cache), and returns `Vector2.Zero` for empty or invalid input. It is the layout box, not the tight
glyph AABB.

Cameras, sprites, lines, and fonts are `IBoundedTransform`s and expose a read-only world-space
`Bounds` AABB for conservative renderer culling. Sprites, lines, and fonts are also
`IRenderable<TLayer>`s; a camera is not drawn. A camera's bounds contain its transformed visible
rectangle; a sprite's bounds contain the drawn quad, derived from the frozen render data, pivot,
flips, and composed world matrix; a line's bounds contain its expanded geometry, including half the
width and every miter or bevel extent; a font's bounds contain its laid-out glyph quads. All return
`Bounds.Empty` for an invalid renderable, a headless or shut-down app, or a renderable with no
drawable content, and reading them never throws for those cases. Empty bounds mean "unknown": the
renderer must not treat them as "outside".

`SpriteAsset` is a mutable authoring and loading DTO, but rendering reads only an immutable frozen
copy of its draw fields. Assigning an asset to a renderable freezes it once; mutating the asset
afterwards (including its UV and index arrays) does not change the drawn sprite or any published
snapshot, and re-assigning the same asset keeps the first frozen values. Load or create a new asset
instance to change the rendered result.

`ShaderAsset` is a MessagePack custom fragment shader description: a `DataRef` to a SPIR-V module, a
layout version, an entry point (default `main`), instance and global `ShaderUniform` declarations,
and `ShaderTextureSlot` declarations. The engine's current layout version is `3`. Version `3` is the
first version shared by sprite and line renderables: the fixed fragment interface adds the line-only
`vLineUv`, `vColor`, and `vLineFlags` inputs, and the sprite and line vertex stages both write the
full interface with neutral values for the other kind. A uniform's
`Offset` is a float-lane offset inside the 16-float instance block (four `Vector4` lanes) or the
eight-`Vector4` global block, and `Default` components beyond the declared `ShaderUniformType` width
are ignored. The declaration arrays are non-null and empty by default. The loader does not validate
names, types, offsets, overlaps, or the layout version; freezing tolerates malformed declarations,
and the renderer owns rejecting or falling back from them.

Like `SpriteAsset`, `ShaderAsset` freezes once per asset instance when assigned to a sprite, line, or
font renderable: mutating it afterwards (including replacing or editing its declaration arrays) does
not change a published snapshot, and re-assigning the same asset keeps the first frozen values.
Freezing is tolerant of malformed metadata: null arrays become empty, null and duplicate names are
skipped with the first declaration winning, and invalid offsets or unknown types contribute no lanes.
The renderer owns rejecting or falling back from malformed declarations.

Layer enums must be `int`-backed `[Flags]` enums with 1-32 named single-bit values; `None = 0` is
allowed. Invalid layer types fail at build/host creation.

## Color

`Color` is an immutable linear RGBA value type in `Bork2d.Engine.Rendering`. It stores its four
components in one `System.Numerics.Vector4`, so the value is exactly 16 bytes and the basic
arithmetic operators run through the vector operators. `R`, `G`, `B`, `A`, the get-only indexer,
`Grayscale` (`0.299*R + 0.587*G + 0.114*B`), and `MaxColorComponent` expose the components without
allowing mutation; `Lerp` clamps its ratio to `[0, 1]` and `LerpUnclamped` does not.

`ToOkLab` converts the linear RGB components to OkLab with the standard linear sRGB (D65) matrices,
ignores alpha, and applies no sRGB gamma decode. `FromOkLab` converts OkLab back to the same linear
space, sets alpha from its optional `alpha` argument (default `1`), and applies no sRGB gamma
encode. Neither conversion clamps to the `[0, 1]` gamut, so an out-of-gamut OkLab color keeps its
negative or greater-than-one linear components. `LerpOkLab` converts both colors to OkLab,
interpolates `L`, `a`, `b`, and alpha linearly with the same ratio, and converts back without a gamut
clamp, clamping `t` to `[0, 1]`; `LerpOkLabUnclamped` is the same without the `t` clamp and may
extrapolate. There is no separate OkLab type; the conversions use `out` parameters on `Color`.

The surface follows Unity 6000.6 `Color`: the three- and four-argument constructors, implicit
conversions to and from `Vector4`, component arithmetic, `HSVToRGB`/`RGBToHSV`, `ToString` with the
`RGBA(r, g, b, a)` form (default `F3`, invariant culture when no provider is given), and the full
set of Unity presets plus `Transparent` and `Clear` (both transparent black) and both `Gray` and
`Grey`. Equality is exact .NET component equality: `==` and `Equals(Color)` compare exactly,
`GetHashCode` is consistent with them, and no approximate comparison is used. Gamma conversion,
`Color32`, and `ColorUtility` are out of scope and are not part of the surface.

## Vector2 extensions

`Vector2` remains `System.Numerics.Vector2`. `Bork2d.Engine.Mathematics.Vector2Extensions` adds the
Unity 6000.6 surface as C# 14 extension members, so there is no second vector type and no existing
public signature changes. Import `using Bork2d.Engine.Mathematics;` to use `Magnitude`,
`SqrMagnitude`, `Normalized`, `Vector2.Up`/`Down`/`Left`/`Right`, `Angle`, `SignedAngle`,
`ClampMagnitude`, `MoveTowards`, `Perpendicular`, `Scale`, `LerpUnclamped`, `LerpClamped`,
`NormalizeOrZero`, `ApproximatelyEquals`, and `SmoothDamp`, plus the mutating `Set`, `Scale`, and
`Normalize`.

Members that `System.Numerics.Vector2` already provides keep their .NET semantics and are not
redeclared: `Lerp` is unclamped, static `Normalize` returns NaN components for a zero vector, `==`,
`Equals`, `GetHashCode`, and `ToString` are exact or unchanged, and there are no implicit
conversions between `Vector2` and `Vector3`. Unity's clamped interpolation, zero-safe
normalization, and approximate equality are available as `LerpClamped`, `NormalizeOrZero`, and
`ApproximatelyEquals`. `SmoothDamp` takes an explicit `deltaTime`; there is no ambient frame clock.

The static `ApproximatelyEquals(Vector2, Vector2)` lives in `Vector2ApproximatelyExtensions`
because C# extension blocks lower a same-named static and instance member to colliding static
methods. Both call forms work in the same namespace: `Vector2.ApproximatelyEquals(a, b)` and
`a.ApproximatelyEquals(b)`.

## Bounds and Ray2D

`Bork2d.Engine.Mathematics.Bounds` is the 2D adaptation of Unity 6000.6 `Bounds`: a mutable
world-space axis-aligned box stored as a center and half extents (two `Vector2` fields, 16 bytes).
`Center`, `Size`, `Extents`, `Min`, and `Max` are settable, and the value exposes Unity's mutating
`Encapsulate`, `Expand`, and `SetMinMax` helpers plus `ClosestPoint`, `Contains`, `Intersects`,
`SqrDistance`, and `IntersectRay`. Construct it from a center and a full size with
`new Bounds(center, size)`, or from two corners with `Bounds.FromMinMax(min, max)`. The type lives in
the mathematics namespace; `IBoundedTransform.Bounds` and the renderer's culling bounds use it from
there.

`Bounds.Empty` is the default value and means "no bounds" or "unknown bounds". A box without
positive area is empty: the default value, a zero-area box, an inverted box (a minimum beyond the
maximum on either axis), and any box with a non-finite center or extent. This is a deliberate
divergence from Unity, which has no empty sentinel and treats a zero-size box as valid, because the
engine uses empty bounds as "unknown, never cull". Queries are empty-safe: `Contains`, `Intersects`,
and `IntersectRay` return `false` for an empty operand, `ClosestPoint` returns `Vector2.Zero`, and
`SqrDistance` returns `float.PositiveInfinity`. The `Center`, `Size`, and `Extents` getters return
`Vector2.Zero` for an empty box, while `Min` and `Max` return the raw stored edges so the mutating
helpers can grow an empty box. Equality and hashing use the raw center and extents, and the value
implements `IEquatable<Bounds>` and `IFormattable`.

`Bounds.IntersectRay` uses a 2D slab test in world units: a ray origin inside the box reports
distance `0`, a zero-direction ray hits only when its origin is contained, and a parallel or
pointing-away ray misses. `Bork2d.Engine.Mathematics.Ray2D` is the matching mutable ray type: its
constructor and `Direction` setter normalize the direction (a zero or tiny vector stays zero), and
`GetPoint(distance)` returns `Origin + Direction * distance`. `Bounds.ToString()` prints
`Center: (x, y), Extents: (x, y)` from the raw stored center and extents, and `Ray2D.ToString()`
prints `Origin: (x, y), Dir: (x, y)` from the raw origin and direction, both with `F2` components
and invariant culture when no provider is given.

## Time

`ITime.DeltaTime` is zero on the first tick. Windowed and offscreen runs then report the real
interval between tick starts, clamped to the builder's maximum delta (default `0.25` s).
`ITime.Time` is monotonic seconds since the first tick and is never clamped. Headless runs use a
synthesized clock: tick `N` reports `N / ticksPerSecond` and `1 / ticksPerSecond`, with no clamp.
There is no fixed timestep, accumulator, or render interpolation.

## Assets

`IAssets` is thread-safe. Loads complete asynchronously on one loader thread; pack mounts are
synchronous.

- `LoadAsset<T>` / `LoadAsset<T>(AssetRef<T>)` deserialize a MessagePack asset. `T` must carry a
  compiled `[GenerateShape]`; reflection-shape fallback is unsupported.
- `LoadData` / `LoadData(DataRef)` return decompressed bytes for raw or MessagePack entries.
- `LoadPack` mounts a pack above the current stack. Later packs shadow earlier ones by canonical
  path; there is no unload.
- `Browse(prefix)` returns a sorted, materialized snapshot; `Exists(path)` is an exact lookup.

A mount changes which entry a canonical path resolves to. The renderer observes the asset catalog
version, so a texture path reloads from the new winner on its next sighting and a previously failed
path is retried; the previous texture pages are retired rather than reused.

Asset paths are canonical runtime keys: lowercase ASCII, `/` separators, no leading/trailing slash,
no empty or `.`/`..` segments, and segments limited to lowercase letters, digits, `_`, and `-`.
Public input is normalized (backslashes become `/`, lowercased) before validation. Source
extensions therefore never appear in an asset path.

`AssetRef<T>` and `DataRef` store canonical path strings; a null path is an absent reference. The
runtime loader treats a pack as trusted content: it does not validate asset fields or follow
references. Pack creation validates reference integrity and the bake tooling validates fields through
registered asset validators.

### Textures

`TextureDescriptor` is the MessagePack texture contract. `Payload` references a complete KTX2 2.0
file with one 2D image, one array layer, one face, no supercompression, and a full mip chain down to
1x1. `Width` and `Height` are the base level's pixel dimensions, `Format` is the concrete linear
format, `MipLevels` is the number of levels (at least one, default one), and `Filter`/`Wrap` are
sampler state with `Linear`/`Clamp` defaults. The descriptor's dimensions, format, and mip count must
match the KTX2 metadata; the runtime rejects a mismatch instead of guessing. Loading a descriptor does
not read or decode the payload.

The renderer's KTX2 reader validates the narrow container and cross-checks the header format, base
dimensions, and mip count against the descriptor before any GPU work. A malformed or mismatched page
is a content failure that draws the bindless placeholder; the built-in placeholder itself is ordinary
pack content (`textures/placeholder` plus its KTX2 payload), loaded and validated at renderer startup,
and its failure is fatal. The runtime uploads every level in the descriptor's concrete linear or BC
Vulkan format and requires the desktop Vulkan 1.3 `textureCompressionBC` feature for the compressed
formats; there is no format fallback or runtime transcode.

Every texture is linear. There is no sRGB format and no color-space field; the bake never applies a
gamma conversion. `TextureFormat` has eight stable integer values:

| Value | Format | Storage |
| --- | --- | --- |
| `Rgba8` | RGBA8 UNORM | Four 8-bit channels. |
| `Rgb8` | RGBA8 UNORM | Logical RGB; alpha is forced to `255`. |
| `Rg8` | RG8 UNORM | Red and green. |
| `R8` | R8 UNORM | Red only. |
| `CompRgba8` | BC7 UNORM | Four channels. |
| `CompRgb8` | BC1 RGB UNORM | Opaque RGB; decoded alpha is `1`. |
| `CompRg8` | BC5 UNORM | Two channels. |
| `CompR8` | BC4 UNORM | One channel. |

The bake selects a format from the texture sidecar's semantic `type` (`rgba`, `rgb`, `rg`, `r`) and
`compression` (`none`, `auto`). `none` maps to the matching uncompressed format and is the default, so
an existing texture never becomes lossy without an explicit authoring choice; `auto` maps to BC7 for
`rgba`, opaque BC1 for `rgb`, BC5 for `rg`, and BC4 for `r`.

### SVG sources

An `.svg` source is baked to ordinary assets rather than a new runtime type: a `SpriteAsset` at the
source's base path and a linear multi-channel signed distance field (MSDF) `TextureDescriptor` at
`<base>-sdf` with its KTX2 payload at `<base>-sdf-ktx2`. The sprite has full UVs and a center pivot,
and its texture is linear MSDF data in `Rgb8` by default (or explicit BC7), so a game can load the
built-in `shader/sdf` fragment shader and assign it to the sprite like any other custom shader. The
sprite itself stays a normal `SpriteAsset`; there is no SVG renderable or other new public engine
type.

### Fonts

`FontAsset` is a generated-shape MessagePack asset that describes one baked font atlas:

| Field | Meaning |
| --- | --- |
| `Atlas` | `AssetRef<TextureDescriptor>` for the linear MSDF atlas. |
| `PixelsPerEm` | The derived em size in atlas pixels. |
| `Ascent`, `Descent`, `LineGap` | Font metrics in em units; ascent is positive and descent is negative. |
| `Glyphs` | The selected glyphs, sorted by codepoint. |
| `Kerning` | Nonzero pair adjustments in em units, sorted by `(LeftCodepoint, RightCodepoint)`. |
| `DistanceRange` | The MSDF distance spread in atlas pixels. |

`FontGlyph` is a value-only record: `Codepoint`, `Advance` in em units, `Bearing` (pen to quad
bottom-left in em units), `PlaneBounds` (the cell quad in em units including the MSDF field margin),
and `AtlasBounds` (normalized U0/V0/U1/V1 with a top-left origin and V down). `Bearing` equals the
first two components of `PlaneBounds`. `Kerning` is a value-only record of a left codepoint, a right
codepoint, and an em-unit adjustment.

The atlas is fixed and square: its `Width` and `Height` are equal, its format is linear `Rgb8` with
alpha forced to `255`, and it has a full mip chain. A glyph that has no drawable outline (for example
a space) keeps its advance and has zero plane and atlas bounds. Codepoints that share a font glyph
share the same cell and measurements, and the bake selects only the codepoints the atlas actually
contains. `FontAsset` is a data description only; loading it neither reads nor decodes the atlas
payload.

`FontAsset.MeasureText(ReadOnlySpan<char>, float)` computes the layout box from the asset's current
fields without freezing or mutating it. `X` is the widest line's advance width, including kerning and
spaces, and `Y` is the line count times the default line height. The method ignores alignment and
uses a line-spacing multiplier of `1`; a trailing line break contributes a final empty line. Missing
codepoints use the same `?` substitution or skip rule as a font renderable, but `MeasureText` does not
log. Empty text, a non-finite or non-positive size, and any result that is not finite return
`Vector2.Zero`. The result is the layout box, not the tight AABB of the drawable glyph quads.

### Pack format

A pack is a v1 `BORK2DPK` file: a 28-byte prefix, a maximum-quality Zstd-compressed MessagePack
header carrying entries and uint64 payload offsets, and stored payloads. The header is checksummed
with XXH3-64. `PackCompressionMode.Debug` stores payloads uncompressed;
`PackCompressionMode.Release` compresses each payload only when that is strictly smaller. Packs are
sorted by canonical path; the bake stages each complete pack and replaces the previous file through a
unique backup, so a failed write is rolled back.

## Rendering data

`SnapshotCapture` walks the rooted tree once under the engine's structural lock after `IGame.Tick`
returns and writes value-only records into the current frame slot. The walk is iterative and
parent-first: an explicit reusable stack carries each node's evaluated parent world matrix, so
every node's world matrix is composed exactly once as its local matrix times the parent world
matrix, and a deep hierarchy cannot exhaust the call stack. Each node is read under one acquisition
of its per-object lock, and world-matrix composition happens outside that lock.

Capture is driven by registered typed producers, not a central type switch. The camera, sprite,
line, and font renderables are `CameraRenderableDefinition<TLayer>`/
`SpriteRenderableDefinition<TLayer>`/`LineRenderableDefinition<TLayer>`/
`FontRenderableDefinition<TLayer>`; each is both the renderable factory and its
`IRenderDataProducer`. `RenderableRegistry` assigns every producer a render-data slot and keeps the
ordered producer list, and `SnapshotCapture` offers each traversed node to those producers in
registration order: the first match captures the node and returns its world matrix, and a node that
matches no producer uses the plain transform path so its children still compose. The camera, sprite,
line, and font producers own the reserved slots `0`, `1`, `2`, and `3`; a custom renderable producer
owns a slot at or above `4`. Transforms are traversed, but only renderables produce records; the layer
enum is flattened to a raw 32-bit mask. Each sprite record carries the immutable
`SpriteRenderData` frozen when the asset was assigned, never the live `SpriteAsset`, and the
world-space AABB computed from that data when the capture composed the world matrix. It also carries
the immutable `ShaderRenderData` frozen when the shader was assigned (or null), the four `Vector4`
lanes of the instance uniform block (`SpriteSnapshot.Uniform0` through `Uniform3`) built from the
frozen declared defaults and matching stored overrides, the first four additional texture references
(`ExtraTexture0` through `ExtraTexture3`), and the total additional-texture count
(`AdditionalTextureCount`). Only stored instance uniforms still declared with the same name and type
are applied; malformed declarations contribute nothing at capture and are left for the renderer to
reject or fall back from. Each line record carries only its immutable cached local-space
`LineGeometryData` (null when the line is not drawable), the composed world matrix, the linear color,
the flattened layer, `ZIndex`, the anti-alias flag, the authored width for diagnostics, the frozen
`ShaderRenderData` (or null), the four instance-uniform lanes, and the world-space AABB computed from
the geometry bounds and world matrix. Each font record carries the immutable `FontRenderData` frozen
when the asset was assigned, the cached `FontLayoutData`, the composed world matrix, the linear color,
the flattened layer, `ZIndex`, the frozen `ShaderRenderData` (or null), the four instance-uniform
lanes, and the world-space AABB of the laid-out glyph quads. The renderer culls with those bounds
without reading the scene.

Frame buffers are compact typed pools (`RenderDataBuffer<T>`) owned by the snapshot through a
`RenderDataBufferSet`. They start empty, grow to the observed high-water mark, and never shrink;
resetting a slot clears every buffer's used range (not only cameras, sprites, lines, and fonts),
releasing obsolete references without shrinking the buffers, so a warm capture allocates nothing.
Adding a renderable kind registers one definition/producer, plus the renderer pass for its data; it
adds no capture switch case and no engine creation method.

Engine-wide user global uniforms live in a separate value-only buffer (`GlobalUniformSnapshot`:
name, type, value) filled from the engine's global map after the scene walk. They are not renderable
producer slots and do not consume a reserved slot. Resetting a slot clears that buffer too, releasing
obsolete names, and a warm capture appends globals without allocating. The frame's time is captured
separately and is not one of the user globals.

Camera projection is pure and shared with the renderer: the camera world matrix is the inverse-view
source, the rounded pixel viewport supplies the aspect, and the only Y conversion is the negation
for Vulkan clip space. The projection result also yields the world-space AABB of the camera's
visible rectangle, which the renderer uses for bounds culling. Invalid cameras (bad framebuffer,
size, viewport, clear mode, scale, or non-finite values) are skipped with a diagnostic instead of
being repaired.

## AOT and surface rules

Every runtime project sets `IsAotCompatible`, `EnableTrimAnalyzer`, and `EnableAotAnalyzer`;
warnings are errors. Serialization uses Nerdbank.MessagePack and PolyType generated shapes only.
Public signatures may not expose implementation packages (MessagePack, hashing, Tomlyn,
StbImageSharp). Tests assert the exported namespace/type lists and the dependency matrix, so this
surface is a contract, not a convention.
