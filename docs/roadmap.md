# Bork2d 0.2 Roadmap

Bork2d 0.2 turns the fixed sprite renderer into a programmable 2D content pipeline. The release has
five user-visible features: custom fragment shaders, KTX2 compressed textures, thick `LineRender`
geometry, SVG-to-MSDF assets, and TTF/OTF fonts with `FontRender`. Each feature lands with its bake
processor, runtime asset and API, renderer path, tests, owning documentation, and a use in the Pong
example. Pong is the integration gate: it must show a line-drawn court, compressed paddles, an SDF
wordmark, and font-rendered HUD text in one playable frame.

The release is deliberately vertical rather than a set of isolated demos. A feature is done only
when an author can produce the asset, a game can consume it through the documented API, the
renderer draws it in the normal 2D order, and the result is covered by the build and test gates.

This document is the version plan. It does not contain implementation and does not replace the
owning documents; when a workstream lands, its behavior is documented as-built in
[engine.md](engine.md), [render.md](render.md), [bake.md](bake.md), and [runtime.md](runtime.md).

## Release goals

| Goal | 0.2 outcome | Pong proof |
| --- | --- | --- |
| Custom shaders | Baked fragment shaders with typed uniforms, up to four extra textures, a default `shader/sprite`, and per-sprite shader selection. | The court uses a custom line shader driven by time. |
| KTX2 textures | Semantic texture types, native BC compression, full mip chains, and a sidecar-driven bake. | Paddles are BC7 KTX2 textures. |
| Line rendering | Open and closed polylines with world-space width, miter joins, butt caps, custom shaders, and normal 2D ordering. | The court border and center line are `LineRender`. |
| SVG support | A shape-only SVG subset baked to a multi-channel signed distance field and drawn through `shader/sdf`. | The Pong wordmark is a baked SVG. |
| Font support | TTF/OTF glyph selection baked to an MSDF atlas and drawn with `FontRender`. | The score and HUD text use the shipped font. |

## Release gates

The version is complete only when all of the following are true:

1. All five features are usable through documented public APIs, not only through example code.
2. Pong runs windowed with the court, compressed paddles, SDF wordmark, and font-rendered HUD text,
   and remains playable with the existing controls.
3. The example builds in Debug and Release, passes its format gate, and publishes with NativeAOT
   with zero AOT or trim warnings.
4. The standard engine gates pass: Debug and Release builds, tests, format verification, docs link
   check, and `git diff --check`.
5. New GPU behavior has environment-gated tests: custom shaders and uniforms, BC texture formats
   and mips, line draws, SDF sampling, and font glyphs.
6. Steady-state rendering allocates nothing, built-in sprites still draw once per camera, and
   custom shaders add draw calls only at contiguous shader boundaries.
7. A device without `textureCompressionBC` fails renderer startup with a clear message rather than
   silently drawing untextured geometry. The release targets desktop Vulkan 1.3 devices, which are
   expected to support BC.
8. The breaking changes are recorded in the owning docs and in a short 0.2 migration note; no old
   shader, texture-payload, or renderable behavior is left documented as current.

## Baseline

The 0.1 engine is a strong fixed-function sprite renderer:

- The sprite pipeline uses a 96-byte instanced record, one vertex/fragment shader pair, a 64-byte
  view-projection push constant, a bindless texture array, and one draw per non-empty camera.
- Its `SpriteAsset` referenced one `TextureDescriptor` whose textures were raw RGBA8, sRGB,
  single-mip.
- Shaders are compiled at engine build time into generated C# arrays; there is no shader asset and
  no runtime shader I/O.
- There is no line, SVG, or font renderable, and the Pong scene is three textured quads plus a
  camera clear.

0.2 changes each of those assumptions. The renderer keeps instancing and bindless sampling, but a
sprite may select a baked fragment shader, carry up to four extra texture paths, and provide typed
uniform values. Textures become KTX2 payloads with mip chains and optional BC formats. The 2D pass
grows a unified order for sprites, lines, and text.

## Decisions

These choices are fixed for the 0.2 design. The public names below are the intended shape; exact
signatures are finalized in the implementation tasks.

| Area | Decision |
| --- | --- |
| Shader model | Fragment-only shaders on the engine-owned sprite vertex ABI. The asset schema is versioned so a future full-pipeline asset can be added without changing fragment-only assets. |
| Shader batching | Contiguous runs of equal kind and shader, preserving the per-camera `(ZIndex, screen Y, screen X)` order. Built-in sprites remain one draw per camera. |
| Uniforms | Named typed slots declared in the shader sidecar, packed at bake time. Instance values are frozen at capture and written into the instance record; global values live in a per-frame block. |
| Built-in globals | `BORK2D_TIME` only; it is `ITime.Time` in seconds. User globals are separate named slots. |
| Extra textures | A sprite has one main texture plus up to four additional bindless texture paths. |
| Shader compilation | `Bork2d.Bake` compiles GLSL 460 to SPIR-V through shaderc. The compiler is build-only and never enters a runtime or AOT output. |
| KTX2 | Native Vulkan BC formats inside KTX2, encoded at bake time. There is no RGBA fallback and no runtime transcoder. |
| Texture sidecar | Semantic `type` and `compression`; the bake maps them to a concrete linear `TextureFormat`. |
| Mipmaps | A full mip chain for every baked texture, including MSDF atlases and compressed textures. |
| Line geometry | CPU-expanded triangles with miter joins and butt caps; anti-aliasing in the fragment shader. |
| 2D ordering | Sprites, lines, and text share one `ZIndex` draw order (then per-camera screen-space Y and X) and one 2D render pass; `Layer` is only a camera culling bit. |
| SVG | Shapes, paths, groups, and transforms with fill only; paint, stroke, text, images, CSS, and filters are ignored. Output is an SDF texture plus a ready sprite. |
| Fonts | TTF/OTF with MSDF, ASCII plus explicit `U+` ranges and single glyphs, left-to-right layout, and basic kerning. `resolution` is the atlas's fixed square side length; glyph em size is derived to fit. |
| MSDF generation | A managed C# MSDF library, build-time only, rather than a native P/Invoke dependency. |
| SDF settings | `resolution` is the output texture's maximum dimension in pixels, `range` is the distance spread in pixels, and `padding` adds pixels around the artboard or glyph. |
| SDF compression | The SVG sidecar's `compression` is `none` (default, linear `Rgb8`) or an explicit `bc7` opt-in; the generic texture sidecar's `type = "rgb"` with `compression = "auto"` (opaque BC1) is never used for SDF data. |
| Built-in shaders | `shader/sprite`, `shader/line`, and `shader/sdf` are ordinary assets in an embedded built-in pack mounted below game packs. |
| API shape | `ShaderAsset` plus `IRenderable<TLayer>.Shader` and per-renderable uniform state; no separate material asset in 0.2. |
| Compatibility | 0.2 is a breaking release. Additive APIs are preferred where they do not compromise the design, but shader, texture, and renderable contracts may change. |

## Workstreams

### 1. Custom shaders

#### Goals

- A shader is a baked asset at any canonical asset path (for example `shader/*`), not a hardcoded
  renderer feature.
- `shader/sprite` is the default and is an ordinary asset that a game pack may shadow.
- A sprite selects a shader through `IRenderable<TLayer>.Shader`; `null` means `shader/sprite`.
- A sprite can bind one main texture and up to four additional textures.
- A shader declares instance and global uniform names and types; games set values without using
  raw SPIR-V layout offsets.
- The built-in sprite path keeps instancing and one draw per camera.
- No GLSL compilation, reflection, or shader file I/O happens at runtime.

#### Architecture options considered

| Option | Authoring | Performance | Flexibility | Validation | 0.2 cost |
| --- | --- | --- | --- | --- | --- |
| A. Fragment-only on the fixed sprite ABI | Fragment GLSL, engine vertex path, metadata sidecar | Best: same instance buffer and bindless array; contiguous runs | Covers most 2D color, distortion, glow, dissolve, and SDF effects; no custom vertex deformation or blend state | Strong: bake checks names, types, offsets, and texture slots | Lowest |
| B. Full pipeline asset (vertex + fragment + state) | Both stages, input layout, blend/raster state | Good but pipeline permutations and state changes complicate batching | Maximum | Large schema and many invalid combinations | High |
| C. C# shader/material objects | C# objects with generated binding metadata | Per-material descriptor state and more CPU work | High, in a C#-native way | Runtime and codegen paths | Medium-high |
| D. Fragment-only and full pipelines together | Both of the above | Best where used, but two planner paths | Highest | Two asset models | Highest |

Option A is selected. 2D effects overwhelmingly live in the fragment stage, and a fixed vertex ABI
keeps the existing 96-byte instance model, one shared pipeline layout, bindless textures, and the
fast sort/plan path. The asset schema records its layout version and stage list so a full-pipeline
asset can be added later without reinterpreting existing shader assets. Full pipelines, custom
blend states, and custom vertex deformation are explicitly out of scope for 0.2.

#### Shader asset

A shader source is a fragment GLSL file with an optional sidecar:

```text
Assets/shader/glow.frag
Assets/shader/glow.frag.toml
```

The processor emits a `ShaderAsset` at `shader/glow` and the raw SPIR-V payload at
`shader/glow-spv`. `ShaderAsset` is a generated-shape MessagePack type with:

| Field | Meaning |
| --- | --- |
| `SpirV` | `DataRef` to the fragment SPIR-V payload. |
| `LayoutVersion` | Version of the fragment ABI and sidecar interpretation. |
| `EntryPoint` | Entry point name, `main` in 0.2. |
| `InstanceUniforms` | Ordered name/type/offset descriptors for the 64-byte instance block. |
| `GlobalUniforms` | Ordered name/type/offset descriptors for the global user block. |
| `TextureSlots` | Named additional texture slots, at most four. |

The shader asset never contains GLSL source. The pack contains the SPIR-V module and the metadata
needed to create a pipeline and pack uniforms. Assigning a `ShaderAsset` to a renderable freezes its
SPIR-V reference and metadata; mutating the asset afterwards cannot change a published snapshot.

#### GLSL authoring model

The bake prepends a fixed prologue to the user's fragment source. The prologue declares the stable
fragment interface, texture access macros, global block, and macros for the declared uniforms. A
shader author writes only the body:

```glsl
void main()
{
    vec4 color = texture(BORK2D_MAIN_TEXTURE, vUv) * vTint;
    color.rgb *= 0.75 + 0.25 * sin(BORK2D_TIME * 2.0 + vWorldPosition.x);
    outColor = color * uPulse;
}
```

The fixed interface provides:

- `vUv`: the sprite's interpolated UV.
- `vTint`: the sprite's linear RGBA tint.
- `vWorldPosition`: the fragment's world position.
- `vLocalPosition`: the fragment's position in the sprite's local quad space.
- `vTextureIndex`: the main bindless texture index.
- `vExtraTextureIndices`: up to four additional bindless indices.
- `vInstanceParams[4]`: the packed instance uniform block.
- `outColor`: the linear RGBA output.

The prologue also provides `BORK2D_MAIN_TEXTURE`, `BORK2D_TEXTURE0` through `BORK2D_TEXTURE3`,
`BORK2D_TIME`, and a macro for each sidecar-declared uniform. A uniform name is usable directly,
for example `uPulse`, rather than by manually indexing a `vec4` array.

The engine-owned vertex stage writes every varying above. Fragment shaders may ignore any varying.
The interface is versioned; changing a location or meaning requires a new `LayoutVersion`. Sources
must not contain `#version` or `#extension`; the processor supplies the fixed version, extension
set, prologue, and entry point. Only `.frag` sources are user shader assets in 0.2; a `.vert` file
is not processed as a user shader.

#### Uniforms and defaults

The instance block is 64 bytes: sixteen floats, packed into four `vec4` values. Supported types are
`float`, `vec2`, `vec3`, `vec4`, and `color` (a linear RGBA `vec4`). A shader may declare at most
sixteen floats of instance uniforms. The bake rejects overflow; there is no silent truncation.

The global block contains `time` (a float, padded to 16-byte alignment) and up to eight user
`vec4` values. `time` is always available. User global values are named and can be set on the
engine; a shader that does not declare a name never sees it. Global names are engine-wide: two
shaders that declare the same name must use the same type, and a mismatch is rejected when the
shader loads rather than silently reinterpreted.

Sidecar example:

```toml
# Assets/shader/glow.frag.toml
textureSlots = ["mask", "noise"]

[uniforms]
uPulse = { type = "float", default = 1.0 }
uTint = { type = "color", default = [1.0, 1.0, 1.0, 1.0] }

[globals]
uWind = { type = "vec2", default = [0.0, 0.0] }
```

A missing sidecar is valid. The bake creates a default sidecar with no uniforms and no extra
texture slots, and reports an informational diagnostic, matching the texture sidecar convention.
Unknown names, duplicate names, unsupported types, and excess sizes are bake errors. Defaults are
stored in the shader asset; the renderer fills any value the game did not set. `ResetUniforms`
returns a sprite to those defaults.

At runtime, unknown or type-mismatched names in `SetUniform` are rejected with a one-time warning
and do not affect the draw. The sidecar is the source of truth; a typo in hand-written game code is
reported at the first draw.

#### Textures

The main texture is the sprite's `SpriteAsset.Texture`, as in 0.1. `ISpriteRender.AdditionalTextures`
is an optional array of `AssetRef<TextureDescriptor>` with at most four entries. The array is
frozen at capture. Entries beyond four are ignored with a warning; absent or failed pages use the
bindless placeholder, exactly like the main texture.

The shader sidecar's `textureSlots` names correspond by position to `AdditionalTextures`. A shader
that declares a slot but receives no path samples the placeholder. This is deliberate: a missing
mask should not crash the frame. If an additional texture is still loading, the sprite is skipped
for that frame exactly like a pending main texture; a failed path uses the placeholder.

#### Pipeline and draw plan

Every sprite shader shares one pipeline layout and one descriptor-set layout:

- Set `0`, binding `0`: per-frame instance storage buffer, read by the vertex stage.
- Set `0`, binding `1`: bindless combined image sampler array, read by the fragment stage.
- Set `0`, binding `2`: per-frame global uniform buffer.
- Push constant: the 64-byte view-projection matrix.
- Fixed state: linear UNORM scene color attachment, dynamic viewport/scissor, triangle strip, no
  vertex input, no depth, cull disabled, straight-alpha blending.

Only the fragment shader module differs per shader; the vertex module is shared. Pipelines are
cached by `(pipeline kind, shader content hash, output color format)`. A shader module is keyed by
content hash and its pipelines by kind, content hash, and format, so identical fragment content
shares its module while the sprite, line, and font pipelines stay distinct. A catalog remount that
changes a shader creates a new entry and retires the old one after the frames that reference it
complete. A shader that fails to load or create a pipeline falls back to its kind's default
(`shader/sprite`, `shader/line`, or `shader/sdf`) with a one-time warning; a failure of any default
itself is fatal at renderer startup.

The instance record grows from 96 to 176 bytes: the current six `vec4` values, one `vec4` holding
the four additional texture indices, and four `vec4` values holding the packed instance uniforms.
The main texture index stays in the existing `misc` slot.

The planner extends the existing sprite pass:

1. No global sprite sort: collect each camera's visible candidates and sort them per camera by
   `(ZIndex, screen Y, screen X)`.
2. Resolve each sprite's shader and texture pages.
3. Build a camera-independent instance record containing the existing quad data, the main and
   extra texture indices, and the four `vec4` instance-uniform values.
4. Compact contiguous sprites with the same shader into one draw run.
5. Record one instanced draw per run per camera.

Sprites with the default shader therefore produce exactly one run and one draw per camera. A custom
shader adds a run only at its boundaries; it never reorders sprites. If a custom shader is still
loading, its sprites are skipped for that frame and counted as pending, rather than drawn with the
wrong shader; the built-in default is ready at startup.

#### Built-in shader packaging

The build produces an embedded `bork2d-builtin.pck` containing `shader/sprite`, `shader/line`, and
`shader/sdf`. It also contains any metadata those shaders need. The runtime mounts this pack below
the game's packs, including when `WithoutAssets()` is used. All three are renderer startup defaults
with fixed entry ids: sprite `0`, line `1`, and the font SDF default `2`; custom entries start at
`3`. The SDF default is required startup content even when a game has no text.

Consequences:

- Built-ins are ordinary assets and can be shadowed by a game pack at the same path.
- `WithoutAssets()` still has working default sprite, line, and font shaders.
- The renderer no longer contains generated sprite SPIR-V arrays; it loads shader modules from the
  asset service like any other content.
- The pack builder is build-time code and never enters the publish graph.

The built-in pack build is part of the engine build, not a game authoring step. A malformed or
missing built-in pack fails renderer startup with a clear error.

#### Public surface sketch

```csharp
public sealed partial class ShaderAsset
{
    public DataRef SpirV { get; set; }
    public uint LayoutVersion { get; set; }
    public string EntryPoint { get; set; }
    public ShaderUniform[] InstanceUniforms { get; set; }
    public ShaderUniform[] GlobalUniforms { get; set; }
    public ShaderTextureSlot[] TextureSlots { get; set; }
}

public interface IRenderable<TLayer> : IBoundedTransform where TLayer : struct, Enum
{
    ShaderAsset? Shader { get; set; }
    Color Color { get; set; }
    TLayer Layer { get; set; }
    int ZIndex { get; set; }

    void SetUniform(string name, float value);
    void SetUniform(string name, Vector2 value);
    void SetUniform(string name, Vector3 value);
    void SetUniform(string name, Vector4 value);
    void SetUniform(string name, Color value);
    void ResetUniforms();
}

public interface ISpriteRender<TLayer> : IRenderable<TLayer> where TLayer : struct, Enum
{
    SpriteAsset? Sprite { get; set; }
    AssetRef<TextureDescriptor>[]? AdditionalTextures { get; set; }
    bool FlipX { get; set; }
    bool FlipY { get; set; }
}
```

`IEngine<TLayer>` gains the corresponding `SetGlobalUniform` and `ResetGlobalUniforms` members.
The engine records global values in the frame snapshot; the renderer writes the global block once
per frame. Instance values are copied into the sprite's snapshot record, so a later mutation cannot
change a published frame.

```csharp
ShaderAsset glow = engine.Assets.LoadAsset<ShaderAsset>("shader/glow").GetAwaiter().GetResult();
ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(engine.Root);
sprite.Sprite = spriteAsset;
sprite.Shader = glow;
sprite.AdditionalTextures = ["art/mask", "art/noise"];
sprite.SetUniform("uPulse", 0.75f);
engine.SetGlobalUniform("uWind", new Vector2(1f, 0f));
```

#### Non-goals for 0.2

- Custom vertex stages, vertex deformation, custom meshes, or geometry shaders.
- Custom blend, depth, cull, or topology state.
- Runtime GLSL parsing, compilation, reflection, or hot reload.
- Shader graphs or generated shader permutations.
- Material assets shared across sprites. Per-sprite uniform state is the 0.2 model.

### 2. KTX2 textures

#### Goals

- Every baked texture payload is a KTX2 file with a full mip chain.
- The sidecar selects a semantic channel type and a compression mode.
- Native Vulkan BC formats are used for compressed textures: BC7, BC1, BC5, and BC4.
- BC support is a hard renderer requirement; there is no RGBA fallback and no runtime transcoder.
- Pong's paddles are compressed, reducing both pack size and sampled bandwidth.

#### Texture types and formats

The sidecar `type` is semantic; the descriptor stores the concrete linear format:

| Sidecar type | Uncompressed format (`compression = "none"`) | Auto compressed format (`compression = "auto"`) | Notes |
| --- | --- | --- | --- |
| `rgba` | `Rgba8` | `CompRgba8` (BC7) | Full alpha; BC7 is the high-quality choice. |
| `rgb` | `Rgb8` (RGBA8 with alpha forced to 255) | `CompRgb8` (opaque BC1) | Alpha is not part of the asset. |
| `rg` | `Rg8` | `CompRg8` (BC5) | B is read as 0 and A as 1. |
| `r` | `R8` | `CompR8` (BC4) | G and B are read as 0 and A as 1. |

All textures are linear. There is no sRGB texture format and no `colorSpace` key, so the bake never
applies a transfer function.

The `compression` key accepts `auto` and `none`. `none` is the default so existing color textures are
not silently made lossy; `auto` selects the block format listed for the channel type.

#### KTX2 container

0.2 uses a deliberately narrow KTX2 profile:

- One 2D image, one array layer, no cubemaps or 3D images.
- A complete mip chain down to 1x1.
- No supercompression, no Basis Universal, no runtime transcode.
- `vkFormat` names the concrete Vulkan format; the data format descriptor and level index are
  written by the bake.
- The descriptor's `Width`, `Height`, `Format`, and `MipLevels` must match the KTX2 metadata; the
  runtime rejects a mismatch rather than guessing.

The bake uses a build-time block encoder for BC1/BC4/BC5/BC7 and a small KTX2 container writer. The
runtime uses a matching KTX2 reader in the renderer. Both are content-format code, not general
image tooling, and neither enters the other's project graph. The bake version and encoder version
are part of the incremental manifest, so a tool upgrade invalidates the affected outputs.

A bake-side texture helper builds the KTX2 payload from straight-alpha RGBA8, the sidecar settings,
and the mip chain. The built-in texture processor and Pong's `CircleProcessor` use it, so a custom
processor does not hand-write KTX2 headers or block data.

`TextureDescriptor` changes to:

| Field | Meaning |
| --- | --- |
| `Payload` | `DataRef` to the KTX2 payload. |
| `Width`, `Height` | Base-level pixel dimensions. |
| `Format` | Concrete format such as `CompRgba8`, `CompRg8`, or `Rgba8`. |
| `MipLevels` | Number of levels, at least 1. |
| `Filter`, `Wrap` | Sampler state. |

`TextureFormat` keeps `Rgba8` as RGBA8 UNORM and adds `Rgb8`, `Rg8`, `R8`, `CompRgba8`, `CompRgb8`,
`CompRg8`, and `CompR8`, in that order and as stable integer values. `Rgba8` remains the default.

#### Sidecar

```toml
# Assets/art/paddle.png.toml
type = "rgba"          # rgba | rgb | rg | r
compression = "auto"   # auto | none
filter = "linear"
wrap = "clamp"
```

A missing sidecar is created with `type = "rgba"`, `compression = "none"`, and the builder's
filter/wrap defaults. This preserves existing textures while making compression an explicit
authoring choice. Pong's paddle sidecar uses `compression = "auto"`, which selects BC7 at its current
channel type.

#### Runtime

- Renderer startup requires the Vulkan `textureCompressionBC` feature. If it is absent, startup
  fails with a message naming the feature and the engine's desktop requirement.
- Each requested BC format is checked with the device's format properties. A format the device
  cannot sample is a fatal renderer error, not a silent placeholder.
- Image creation uses the descriptor's format and mip count. Each level is copied from the KTX2
  payload with the correct block extent and row length; uncompressed levels follow the same path.
- Samplers use the descriptor filter for minification and magnification, linear mip filtering, and
  `MaxLod = VK_LOD_CLAMP_NONE`; anisotropy is not enabled in 0.2.
- Texture-page budgets use the KTX2 payload byte length. Compressed pages therefore consume less
  upload and residency budget than an equivalent RGBA8 page.
- The placeholder page stays available at bindless index 0. It is loaded from the embedded built-in
  pack as an uncompressed KTX2 texture at startup; no sprite may sample an incomplete page.

#### Mipmaps

All baked textures get a full chain. For color textures, the bake box-filters the raw linear values
and encodes each level with the same block format. For SDF and MSDF textures, the bake uses a
distance-preserving downsample rather than a naive color average:

- Each channel is filtered as a signed distance, with the valid range preserved.
- The reconstructed median field is checked at 0.5x and 0.25x.
- The base level keeps the MSDF channels; mips are generated from the same field so text and vector
  art remain stable at distance and during camera zoom.

MSDF mip quality is a release gate for the Pong wordmark and HUD text. If the filter cannot meet the
quality bar, the fallback is to generate MSDF mips from a higher-range field, not to disable mips
for only some assets.

### 3. LineRender

#### Goals

- Open and closed polylines with points in world units.
- Width in world units, independent of screen resolution.
- Butt caps for open ends and miter joins, with a miter limit.
- A custom shader per line, using the same shader asset and uniform model as sprites.
- Correct 2D ordering with sprites and text.
- Court rendering in Pong.

#### Surface

```csharp
public interface ILineRender<TLayer> : IRenderable<TLayer> where TLayer : struct, Enum
{
    IReadOnlyList<Vector2> Points { get; set; }
    bool Closed { get; set; }
    float Width { get; set; }
    bool AntiAlias { get; set; }
}

IEngine<TLayer>.CreateLineRender(ISceneNode parent);
```

`Shader == null` means the built-in `shader/line`. Points are copied when assigned, so mutating the
caller's array does not change a published snapshot. `AntiAlias` defaults to `true` and `Color` to
white. The color, layer, z-index, shader, and line uniform values follow the shared
`IRenderable<TLayer>` contract and are frozen at capture. A line is a scene entity like a sprite: no
`Dispose`, and detaching preserves the world transform.

Validation is conservative and happens at capture/draw:

- At least two points for an open line, at least three for a closed line.
- Every point is finite; `Width` is finite and greater than zero.
- Duplicate consecutive points are skipped; zero-length segments produce no geometry.
- A non-finite or degenerate line is skipped with a one-time diagnostic and `Bounds.Empty`, never
  a crash.
- A miter beyond the limit (4) falls back to a bevel for that corner.

#### Geometry

The engine expands a polyline on the CPU into triangles:

- Each segment becomes a quad with the requested world-space width.
- Interior corners use miter joins, clamped by the miter limit.
- Open ends use butt caps; closed lines join the last point to the first.
- `AntiAlias` adds a small screen-space coverage term in the fragment shader rather than extra
  geometry. The built-in line shader uses `vLineUv.x` and `fwidth` to soften the edges.

The expansion is cached per line and rebuilt only when points, width, or closed state changes;
color is a draw state, so changing it never rebuilds geometry. The renderer applies the snapshot
world matrix while writing the triangles into the frame's line vertex/index buffers and merges
contiguous lines of the same kind and resolved shader into one draw run. Line records carry the same
64-byte instance-uniform block as sprites; the line vertex stage forwards it to the fragment stage, so
lines that share a shader can still merge even when their uniform values differ.

#### Shader interface

A line fragment shader uses the same prologue as a sprite shader, with line-specific inputs:

- `vLineUv`: `x` across the width (0 at one edge, 1 at the other), `y` along the line in world
  units.
- `vWorldPosition`: the fragment's world position.
- `vColor`: the line's linear color.
- `BORK2D_TIME` and the declared instance/global uniforms.

Lines have no main texture and no additional textures in 0.2. The built-in `shader/line` draws a
solid anti-aliased color. A custom line shader can add pulses, dashes, or edge glow; Pong uses a
custom court shader with a subtle time-based glow and a dashed center pattern.

#### Unified 2D order

`LineRender` does not get a separate pass that draws before or after sprites. The 2D pass sorts
sprite records, line records, and font glyph records together per camera by `ZIndex` ascending, then
screen-space Y ascending, then screen X ascending. `ZIndex` is not layer-scoped, and `Layer` is only a
camera culling bit. Ties at equal `ZIndex`/screen Y/screen X are explicitly unspecified: there is no
kind, capture-order, or record-index tie-break. This keeps background court lines behind gameplay at
the same depth without making the order depend on which kind happened to be captured first.

The existing sprite planner becomes the 2D planner:

1. Build each camera's visible candidate list and sort it by `(ZIndex, screen Y, screen X)`.
2. Build sprite instance records, line triangles, and one instance record per font glyph quad.
3. Compact contiguous runs with the same draw kind, shader, and font asset.
4. Record each run in order, binding the pipeline appropriate to the run.
5. Apply the camera's layer mask and conservative world-AABB culling to every record.

A line's bounds are the AABB of its outer geometry including half the width and miter extents. A
font's bounds are the AABB of its glyph quads and are tested once per font record. Empty bounds mean
unknown and never cull.

### 4. SVG and MSDF

#### Supported SVG subset

0.2 accepts shape geometry, not paint:

- `svg`, `g`, `path`, `rect`, `circle`, `ellipse`, `line`, `polyline`, and `polygon`.
- Group and element transforms, nested groups, and `viewBox` scaling.
- Path commands for move, line, horizontal/vertical line, cubic and smooth cubic, quadratic and
  smooth quadratic, arc, and close.
- Nonzero and even-odd fill rules, including holes formed by subpaths.
- Any `fill`, `stroke`, `opacity`, `color`, gradient, filter, mask, clip path, text, image, or CSS
  is ignored. The output is a silhouette; color comes from the sprite color or shader. A shape with
  no fill produces no geometry, so a stroke-only shape is empty in 0.2.

Unsupported elements are skipped with an informational diagnostic; a malformed path is an error.
`defs` and `use` are not expanded in 0.2.

#### Bake output

An SVG source at `Assets/art/wordmark.svg` with sidecar `wordmark.svg.toml` produces:

- A `TextureDescriptor` at `art/wordmark-sdf` with linear `Rgb8` (alpha forced to `255`) by default,
  explicit BC7 `CompRgba8` with `compression = "bc7"`, and a full mip chain.
- A `SpriteAsset` at `art/wordmark` that references that texture with full UVs, a center pivot, and
  a scale that makes the visible artboard one unit high while the SDF margin extends outside it.
- The SDF texture reserves the field margin around the artboard; the sprite's UVs and scale include
  that margin, so the visible artboard keeps the source aspect and the intended size.

The sprite is a normal `SpriteAsset`; assign `Shader = shader/sdf` to draw it. The SVG processor
does not bake a shader choice into the asset, so the asset stays reusable and the shader remains an
ordinary asset.

```toml
# Assets/art/wordmark.svg.toml
resolution = 512      # maximum output texture dimension in pixels
range = 8             # distance spread in pixels
padding = 2           # pixels around the artboard
compression = "none"  # none | bc7
```

`resolution` is the maximum of the output texture's width and height; the other axis follows the
artboard aspect ratio. The bake rounds to at least 1 pixel after padding. The bake reserves at least
`range` pixels around the artboard for the distance field and `padding` extra pixels around that
margin; the artwork is scaled to fit inside the reserved area. `range` controls how far the field
extends and therefore how soft a glow can be. `padding` may be 0.

#### `shader/sdf`

`shader/sdf` is the built-in font SDF fragment shader and the third renderer startup default. It
reconstructs a signed distance from the three MSDF channels, applies a screen-space antialiasing
width, and multiplies by the sprite or font color. It samples the main texture `.rgb` only. Its
instance uniforms are `uThreshold` (default `0.5`) and `uSoftness` (default `1.0`); a sprite or font
may override either through `SetUniform`. A sprite may still assign `shader/sdf` as ordinary custom
content: it shares the SDF fragment asset and module but resolves with the sprite pipeline kind,
while a font assigned the same shader uses the font pipeline kind.

```csharp
SpriteAsset wordmark = engine.Assets.LoadAsset<SpriteAsset>("art/wordmark").GetAwaiter().GetResult();
ShaderAsset sdf = engine.Assets.LoadAsset<ShaderAsset>("shader/sdf").GetAwaiter().GetResult();
ISpriteRender<GameLayer> logo = engine.CreateSpriteRender(engine.Root);
logo.Sprite = wordmark;
logo.Shader = sdf;
logo.Color = new Color(1f, 1f, 1f, 0.15f);
```

#### Quality and mipmaps

The MSDF is generated from the normalized SVG contours at the requested resolution. The bake
validates the output's signed-distance range, rejects empty or all-inside fields, and checks a small
set of sample points for silhouette correctness. Mips use the distance-preserving path from the KTX2
workstream. The sidecar's `compression` is `none` by default and stores linear `Rgb8`;
`compression = "bc7"` is an explicit opt-in that must keep the base level within the accepted median
fidelity and preserve the decoded sign on every validated mip, and the bake rejects an encoding that
fails rather than falling back to uncompressed output. The Pong wordmark is the visual acceptance
check: its corners must remain sharp at the default camera size and at a reduced window size.

### 5. Fonts and FontRender

#### Supported fonts and layout

- TTF and OTF outlines, including TrueType `glyf` and CFF outlines.
- ASCII plus explicit Unicode ranges and single glyphs.
- Left-to-right, single-line and explicit `\n` line breaks.
- Basic kerning from the font's pair data.
- No complex shaping, bidi, RTL, ligatures, font fallback, or word wrapping in 0.2.

#### Sidecar

```toml
# Assets/fonts/score.ttf.toml
resolution = 512                   # fixed square atlas side length in pixels
range = 8                          # distance spread in pixels
padding = 2                        # extra pixels around the field margin
ranges = ["U+0020-U+007E"]         # explicit Unicode ranges
glyphs = ["U+2605"]                # optional single glyphs
```

If the sidecar is absent, the bake creates one with printable ASCII, `resolution = 2048`,
`range = 8`, `padding = 2`, and no extra glyphs. A missing font source or an unreadable outline is a
bake error. Ranges are inclusive and use `U+` hexadecimal notation. An empty selection is an error.

#### Atlas and derived glyph size

`resolution` is the atlas's fixed square side length in pixels. The bake derives the glyph em size:

1. Rasterize the selected glyphs at a candidate em size.
2. Pack their MSDF cells plus the `range` field margin and `padding` into a
   `resolution × resolution` atlas.
3. Binary-search the largest integer em size that fits, bounded below by 8 pixels.
4. If even 8 pixels cannot fit every selected glyph, fail the asset with a diagnostic naming the
   glyph count and atlas size.

The derived em size is stored in the `FontAsset`. This makes quality predictable for a bounded
selection such as the Pong HUD's printable ASCII while still failing clearly for an oversized
selection. The atlas is packed with a deterministic shelf algorithm, sorted by glyph height and
codepoint, so incremental bakes are stable.

The atlas texture uses `type = "rgb"`, `compression = "none"`, and a full mip chain. MSDF distance
data is not color data, so the generic color path's `type = "rgb"` with `compression = "auto"`
(opaque BC1) is never used for an MSDF atlas.

#### `FontAsset`

`FontAsset` is a generated-shape MessagePack asset:

| Field | Meaning |
| --- | --- |
| `Atlas` | `AssetRef<TextureDescriptor>` for the MSDF atlas. |
| `PixelsPerEm` | Derived em size used to rasterize the glyphs. |
| `Ascent`, `Descent`, `LineGap` | Font metrics in em units. |
| `Glyphs` | Per-codepoint atlas rectangle, bearing, advance, and plane bounds. |
| `Kerning` | Sorted pair adjustments, or an empty array. |
| `DistanceRange` | The MSDF spread in atlas pixels. |

Only selected glyphs are present. If a codepoint is not in the selected set, `FontRender` substitutes
`?` when that glyph is selected; otherwise it skips the codepoint with a one-time warning. This keeps
the atlas closed over the declared selection and makes missing content visible in development.

`FontAsset.MeasureText(ReadOnlySpan<char>, float)` returns the layout box: X is the widest line's
advance width including kerning and spaces, and Y is the line count times the default line height.
It ignores alignment, always uses a line-spacing multiplier of `1`, applies the same `?`/skip
missing-glyph rule without logging, and reads the asset's current fields without freezing it. Empty
or invalid input returns `Vector2.Zero`. The result is the layout box, not the tight glyph AABB.

#### `FontRender`

```csharp
public interface IFontRender<TLayer> : IRenderable<TLayer> where TLayer : struct, Enum
{
    FontAsset? Font { get; set; }
    string Text { get; set; }
    float Size { get; set; }          // world units per em
    TextAlignment Alignment { get; set; }
    float LineSpacing { get; set; }
}

IEngine<TLayer>.CreateFontRender(ITransform parent);
```

`FontRender` lays text out into local-space glyph quads using the font metrics, the derived em size,
and kerning. The local origin is the first line's baseline at `y = 0`; lines advance downward by
`(Ascent - Descent + LineGap) * Size * LineSpacing`, where `LineSpacing` is a multiplier of the
default line height, not extra world units. `\n` (including `\r\n` and a lone `\r`) starts a new
line, and a trailing break contributes a final empty line. Layout is cached and rebuilt only when
`Font`, `Text`, `Size`, `Alignment`, or `LineSpacing` changes, so a static score allocates nothing
per frame. A missing codepoint uses `?` when that glyph exists and is otherwise skipped with a
one-time warning. A non-finite or non-positive `Size` or `LineSpacing` produces an empty layout and
one warning; bounds and capture never throw. Assigning a `FontAsset` freezes its atlas reference,
metrics, glyphs, and kerning; mutating the loaded asset afterwards cannot change a published frame.

A `FontRender` with `Shader == null` draws with the built-in `shader/sdf` fragment and the font
atlas. A custom fragment shader may be assigned and is drawn with the font glyph instance ABI; a
failed custom shader falls back to `shader/sdf` with a one-time warning. The shared instance-uniform
rules and freeze behavior are the same as for sprites and lines.

Text joins the unified 2D order as a third record type. A contiguous run of glyphs from the same
font is merged into one draw. `FontRender.Bounds` is the world-space AABB of the laid-out glyph
quads, not the layout box.

Pong draws every player-visible text surface with `FontRender`s: the left score right-aligned and
the right score left-aligned near the top center of the court, a centered match-state line below
them, and a centered control prompt above the bottom court line. The score text changes only when a
point is scored and the state and prompt text only when the match state changes. All previously
title-only text (the match state and the prompt) is drawn in-scene with `FontRender`s; the window
title stays the host's static `Pong` title.

## Renderer integration

### Snapshot and slots

The frame snapshot reserves:

- `0`: cameras.
- `1`: sprites.
- `2`: lines.
- `3`: fonts.

Custom renderable producers start at `4`. This is a breaking change to the reserved-slot contract,
but it keeps the built-in 2D kinds on the same planner and avoids a hidden second pass.

The capture remains value-only. Sprites carry frozen `SpriteRenderData`; lines carry the immutable
cached geometry and style; fonts carry a frozen layout reference. No live scene entity or mutable
asset crosses the tick/render boundary.

### 2D pass

The sprite pass becomes the 2D pass. For each camera it:

1. Sorts each camera's visible records by `(ZIndex, screen Y, screen X)`; ties are unspecified.
2. Applies the camera's culling mask and conservative world-AABB test.
3. Builds sprite instance ranges, line triangle ranges, and one glyph instance record per drawable
   font glyph quad, all in draw order.
4. Uploads the frame's shared instance buffer and line vertex/index buffers.
5. Records contiguous runs in order, one pipeline bind per run: a run changes on kind, shader entry,
   or font asset.

The built-in shader path stays one instanced draw per camera. The line and font paths add draw
runs only when their pipeline, shader, or font asset changes; glyphs share the sprite instance
buffer and the sprite vertex stage, so no dedicated glyph buffer exists. `RenderStats` gains
`Glyphs` and `SkippedFonts` (and keeps the sprite, line, camera, and draw-call accounting); glyph
counts are separate from the sprite-only `Instances`. Fonts that are still loading skip with a
counter, an empty layout adds no run and is not skipped, and bounds-culled fonts are not skipped.
Frame capture records after the 2D pass, so it includes all three kinds.

### Resource ownership

- One shared pipeline layout and descriptor-set layout for sprite, line, and font pipelines.
- One per-frame instance storage buffer holding both sprite and font glyph instance records.
- One per-frame line vertex/index buffer pair.
- A font pipeline kind that reuses the sprite vertex stage's SPIR-V and instance ABI; a separate
  Vulkan module handle is owned alongside the sprite and line modules.
- A pipeline cache keyed by pipeline kind, shader content hash, and output color format.
- A shader module cache keyed by canonical shader path and content hash.
- Texture pages remain the single bindless texture owner; fonts sample their atlas page through the
  same bindless array, the built-in line vertex stage writes the placeholder texture index `0` and
  zero extra indices, and a shared custom shader may sample the bindless array from a line.

All resources are render-thread-owned. Frame buffers are fenced like the existing instance buffer;
steady-state frames allocate nothing.

### Time and global block

`BORK2D_TIME` is the snapshot's `Time` value, which is `ITime.Time` in seconds. The global uniform
buffer is written once per frame. User global values are copied from the engine's frozen snapshot
state; the renderer never reads live game objects.

### Texture formats and sampler

The texture page upload path handles both uncompressed and BC KTX2 levels, the descriptor's mip
count, and the concrete format. The sampler cache is keyed by `(Filter, Wrap)` and uses linear mip
filtering with `MaxLod = VK_LOD_CLAMP_NONE`; every page has a complete chain, so the maximum LOD
clamps to it and the mip count does not need to be part of the key. Placeholder and KTX2 pages can
coexist in the same bindless array.

## Asset pipeline

### Processors

| Source | Processor | Outputs | Sidecar |
| --- | --- | --- | --- |
| `.frag` | Fragment shader processor | `ShaderAsset` at the source's base path, SPIR-V data at `<base>-spv` | Optional; defaults to no uniforms and no extra slots. |
| `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tga` | Texture processor | KTX2 `TextureDescriptor` and payload | Optional; creates a default when missing. |
| `.svg` | SVG SDF processor | MSDF texture at `<name>-sdf`, `SpriteAsset` at `<name>` | Optional; creates defaults. |
| `.ttf`, `.otf` | Font processor | MSDF atlas texture at `<name>-atlas`, `FontAsset` at `<name>` | Optional; creates ASCII defaults. |

All processors run on the bake thread, are reusable, and write through the existing
`AssetProcessorContext`. A failed source emits no outputs. Shader compilation errors, invalid
sidecar values, malformed SVG paths, unreadable fonts, and atlas overflow are bake errors with
source locations, never runtime surprises.

### Built-in pack build

The repository gains a build-time built-in assets step that compiles the three built-in fragment
shaders and bakes `bork2d-builtin.pck`. The pack is embedded in the runtime assembly and mounted
below game packs. It is built with the same processors as user content, which is what makes
`shader/sprite` "not special". The build must also work in a clean clone and in CI, where no Vulkan
SDK is required.

### Diagnostics

New bake diagnostics use the `BK9xxx` range:

| Range | Area |
| --- | --- |
| `BK9000-BK9099` | Shader source, sidecar, compile, and metadata validation. |
| `BK9100-BK9199` | KTX2 encoding, level layout, BC format, and texture sidecar validation. |
| `BK9200-BK9299` | SVG parsing, supported subset, and MSDF generation. |
| `BK9300-BK9399` | Font parsing, glyph selection, atlas packing, and MSDF generation. |

Diagnostics name the source path, line, and column where the binder has it, and the asset path and
field otherwise. Existing `BK2xxx` sidecar binding and `BK4xxx` texture diagnostics remain in use
where they already apply.

### Incremental builds

The manifest records the shader compiler, BC encoder, MSDF library, SVG parser, and font parser
versions in addition to the existing processor/type closure. Any tool or sidecar change invalidates
the affected outputs. Shader and MSDF output must be deterministic for the same inputs, so an
up-to-date bake writes nothing.

### Dependencies

`Bork2d.Bake` gains build-only references for shaderc, the BC encoder, the managed MSDF library,
the SVG parser, and the TTF/OTF outline reader. These packages never enter `Bork2d.Engine`,
`Bork2d.Render`, `Bork2d.Runtime`, the game output, or the AOT publish graph. `Bork2d.Engine` gains
generated-shape asset types and the public scene contracts for shaders, lines, and fonts, but no new
external package. `Bork2d.Render` gains KTX2 parsing and GPU upload code, not an image or font
library. The exact package and version for the BC encoder, MSDF generator, SVG parser, and font
reader are selected in the task that adds each one; the build-time-only boundary is fixed here.

## Public API summary

New or changed public members in 0.2:

- `ShaderAsset`, `ShaderUniform`, `ShaderTextureSlot`, `ShaderUniformType`.
- `IBoundedTransform` (renamed from the non-generic `IRenderable`) and `IRenderable<TLayer>`,
  which owns the shared `Shader`, `Color`, `Layer`, `ZIndex`, `SetUniform` overloads, and
  `ResetUniforms`.
- `ISpriteRender.AdditionalTextures`; `ISpriteRender.Tint` is removed, and the shared
  `IRenderable.Color` replaces it.
- `IEngine.SetGlobalUniform`, `IEngine.ResetGlobalUniforms`.
- `ILineRender<TLayer>`, `IEngine.CreateLineRender`.
- `IFontRender<TLayer>`, `IEngine.CreateFontRender`, `FontAsset`, `FontGlyph`, `TextAlignment`,
  `FontAsset.MeasureText`.
- `TextureFormat` expanded to the linear UNORM and BC values; `TextureDescriptor.MipLevels`.
- `RenderStats` gains `Glyphs` and `SkippedFonts` alongside the sprite and line counters
  (`Instances` stays sprite-only, and draw calls include font runs).

`FontRender` uses the built-in `shader/sdf` when no custom `Shader` is assigned and may select a
custom fragment shader for the font glyph instance ABI; a failed custom shader falls back to
`shader/sdf`.

The exact overload sets, nullability, and defaults are settled in the implementation tasks and
recorded in [engine.md](engine.md) and [render.md](render.md). A breaking-change note calls out the
renamed `IBoundedTransform`/`IRenderable<TLayer>` contract, the new `TextureFormat` values, the KTX2
payload contract, the new reserved snapshot slots, and the shader asset contract.

## Pong 0.2

### Scene and assets

Pong keeps the existing simulation, controls, and camera; the window title stays the host's static
`Pong`, and the score, match state, and control prompt are all drawn in-scene. Its asset tree grows:

```text
examples/pong/Pong.Assets/Assets/
  art/paddle.png
  art/paddle.png.toml          # type = "rgba", compression = "auto"
  art/wordmark.svg
  art/wordmark.svg.toml        # resolution = 512, range = 8, padding = 2, compression = "none"
  fonts/score.ttf
  fonts/score.ttf.toml         # resolution = 512, printable ASCII, range = 8
  shader/court.frag
  shader/court.frag.toml       # time-driven glow; border and center line use different uniforms
```

The font is JetBrains Mono Regular (OFL 1.1), committed under `Pong.Assets` with its license file and
attribution. It is a monospaced face with clear digits and punctuation, so the one HUD font covers
the score, the match state, and the prompt; no system font is required.

`GameSettings` gains references for the wordmark sprite, the HUD font, and the court shader;
`ScoreFont` keeps its field name and TOML key but now feeds all HUD text (score, state, and prompt).
`GameLayer` gains `Hud = 8` for all HUD text, and the camera culling mask includes it; the existing
Court, Paddles, and Ball bits keep their values. The Pong `CircleProcessor` is updated to emit its
generated ball through the KTX2 texture helper.

### Rendering

- **Court.** A fixed 32x18 world court is created once: a closed `LineRender` draws the border,
  inset by half its width so the full stroke stays inside the court, and an open vertical
  `LineRender` draws the center line. `shader/court` applies a time-based glow and an anti-aliased
  edge; an instance uniform switches the dash pattern on for the center line and off for the border.
  A window resize changes only the camera size, which fits the whole court and lets the clear color
  fill the extra axis.
- **Paddles.** The existing paddle sprite uses a BC7 KTX2 texture. The ball remains a generated
  circle texture; it may use uncompressed KTX2 with mips, but it is not required to be compressed.
- **Wordmark.** `art/wordmark.svg` bakes to the `art/wordmark` sprite plus the linear
  `art/wordmark-sdf` MSDF texture and its KTX2 payload; `art/wordmark.svg.toml` sets
  `resolution = 512`, `range = 8`, `padding = 2`, and `compression = "none"`, so the field stays
  uncompressed `Rgb8`. The sprite is a normal `SpriteAsset` drawn through the built-in `shader/sdf`
  on the `Court` layer at `ZIndex` 1, centered on the origin, scaled 5.5 times the bake's
  one-unit-high artboard scale, and tinted `(0.65, 0.78, 1, 0.15)`. It therefore sits behind the
  court lines at `ZIndex` 5, the paddles at 10, and the ball at 20, and the center line draws over
  it. It exercises the full SVG, MSDF, linear texture, mip, and `shader/sdf` path without affecting
  the simulation, controls, or camera fitting.
- **HUD text.** Four `FontRender`s draw every player-visible text surface on the `Hud` layer at
  `ZIndex` 30, above every gameplay element. The two scores sit near the top center, right- and
  left-aligned respectively; each uses the baked `fonts/score` font at 1.5 world units per em, the
  built-in `shader/sdf`, and the paddle tints (`settings.LeftPaddleTint` for the left score,
  `settings.RightPaddleTint` for the right). The first line's baseline sits at `y = 7.2`, and the
  left score's right alignment edge and the right score's left alignment edge stay at `x = -0.4` and
  `x = +0.4`, so the pair stays centered and a two-digit score grows away from the center while
  staying inside the court. The centered state line uses `Size = 1.0` with its baseline at `y = 6.0`
  in the court color, and the centered prompt uses `Size = 0.65` with its baseline at `y = -8.0` in
  the court color at 65% alpha; both stay clear of the score and the border lines and inside the
  32x18 court. All four texts share the font asset, shader, layer, and z-index, so they stay
  contiguous in the 2D order and merge into one font run. The score text changes only when
  `LeftScore` or `RightScore` changes, and the state and prompt text only when the match state
  changes. The window title is the host's static `Pong` title and is never written by the game.
- **Ordering.** Court lines, ball, paddles, wordmark, and HUD text all use the shared `ZIndex` and
  per-camera screen-space 2D order in the unified 2D pass; `Layer` only selects cameras. No
  feature-specific pass is added.

### Acceptance

- Windowed play is unchanged: W/S, Space, R, Esc, resize, pause, reset, win condition.
- The whole court stays visible and correctly aligned at multiple window sizes.
- Paddles show the compressed texture without visible block artifacts at their on-screen size.
- The wordmark is sharp at 1280x720 and at a reduced window size.
- The score, state, and prompt update and are legible.
- Render stats show a small, stable draw-call count and no steady-state allocations.
- The example builds, formats, and publishes with zero AOT/trim warnings.

## Milestones

### M1 - Shader foundation

Deliver the shader asset, sidecar binder, bake processor, SPIR-V compiler integration, shared
pipeline layout, shader module cache, instance record v2, named uniforms, extra textures,
`IRenderable<TLayer>.Shader`, global uniforms, default `shader/sprite`, and the embedded built-in pack.

Exit: a sprite draws with a custom fragment shader, a time uniform, an instance uniform, and two
extra textures; the built-in path is still one draw per camera; shader bake errors are covered.

### M2 - KTX2

Deliver the KTX2 writer/reader, BC encoder integration, expanded `TextureFormat`, mip generation,
sidecar keys, runtime upload and sampler changes, BC feature validation, and the Pong paddle sidecar.

Exit: a BC7 paddle renders with mips; a BC5/BC4/R8 fixture renders the expected channels; an
unsupported device fails startup with the documented message; no fallback path exists.

### M3 - 2D queue and LineRender

Deliver the reserved line slot, `ILineRender`, line geometry and joins, the unified 2D planner,
line pipelines, built-in `shader/line`, custom line shaders, stats, and the Pong court.

Exit: a closed border and an open center line draw with correct width, joins, AA, and ordering
relative to sprites; a line can use a custom shader; malformed points are skipped safely.

### M4 - SVG and SDF

Deliver the SVG parser for the supported subset, the MSDF generator integration, the SVG sidecar
with optional `none`/`bc7` compression, SDF texture and sprite outputs, linear texture handling, the
built-in `shader/sdf`, and the Pong wordmark.

Exit: a shape-only SVG bakes to a linear MSDF `Rgb8` (or explicit BC7) texture, draws through
`shader/sdf` with the default `uThreshold`/`uSoftness`, preserves its aspect and padding, and remains
sharp at multiple sizes; unsupported SVG features are diagnosed.

### M5 - Fonts and FontRender

Deliver TTF/OTF outline reading, glyph range parsing, atlas packing and derived em size, the font
sidecar, `FontAsset`, `FontRender`, font layout and kerning, the SDF font pipeline, and the Pong
HUD.

Exit: the shipped font bakes with printable ASCII, the score and HUD text render and update, missing
glyphs and oversized selections have defined behavior, and the atlas fits within `resolution`.

### M6 - Pong integration and hardening

Deliver the complete Pong scene, the OFL font and license, the 0.2 migration note, as-built docs,
performance measurements, AOT publish, full gates, and cleanup of the old generated-shader path.

Exit: all release gates pass; Pong is the integration proof; no unfinished marker or follow-up
remains for the version.

M1 is the foundation for M3-M5. M2 can proceed in parallel with M1 after the texture-descriptor
shape is settled, because shader assets and KTX2 containers are independent. M4 and M5 share the
MSDF core and should be sequenced. M6 starts only after M1-M5 are merged.

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
dotnet publish examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Release -r win-x64
```

The publish must finish with zero AOT/trim warnings. A Vulkan 1.3 desktop run of the published
`Pong.Desktop.exe` is the final visual check.

### Environment-gated tests

Run the affected GPU and window suites and report any skips:

```powershell
$env:BORK2D_RUN_WINDOW_TESTS = '1'
dotnet test src/Bork2d/Bork2d.slnx -c Debug

$env:BORK2D_RUN_GPU_TESTS = '1'
dotnet test src/Bork2d/Bork2d.slnx -c Debug --filter "Category=Gpu"
```

The release-specific coverage:

- **Shaders.** Sidecar binding, uniform packing, extra texture slots, run compaction, pipeline
  cache keys, missing shader fallback, and a frame capture of a custom shader.
- **KTX2.** KTX2 header/level parsing, format/type mapping, BC7/BC5/BC4/BC1 upload, mip sampling,
  descriptor mismatch rejection, and BC feature failure.
- **Lines.** Open/closed geometry, miter limit, butt caps, degenerate points, AA, bounds culling,
  custom line shader, and ordering with sprites.
- **SVG.** Path commands, transforms, holes and fill rules, unsupported-element diagnostics,
  resolution/padding, linear sampling, and mip quality.
- **Fonts.** Glyph range parsing, atlas derivation, metrics, kerning, line breaks, alignment,
  missing glyphs, layout cache behavior, and HUD text rendering.
- **2D pass.** Per-camera sort key, run grouping, camera culling, draw-run accounting, and stats.

Bake tests build assets in a temporary directory; no binary fixture, generated pack, or generated
image is committed. Authored Pong content is content, not a fixture.

### Performance

Release GPU measurements cover:

- The built-in sprite path still produces one draw per camera and no steady-state allocations.
- A custom shader adds only boundary draws; a Pong frame stays under a small draw-call budget
  (target: 10 or fewer).
- Instance record growth and shared sprite/glyph instance-buffer traffic are measured against the
  existing sprite path.
- KTX2 compressed pages reduce declared upload/residency bytes compared with RGBA8.
- MSDF mips and the font atlas stay within the texture residency budget.

## Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| KTX2 container or DFD details are wrong. | Textures fail to upload or render incorrectly. | Keep the profile narrow, validate against the KTX2 specification and an independent tool where available, and add GPU round-trip tests. |
| BC support is absent on a device. | Startup fails for that device. | Treat `textureCompressionBC` as a documented requirement, query every requested format, and fail with a clear message; no silent fallback. |
| MSDF mip filtering degrades text or vector edges. | Blurry or broken glyphs. | Use a distance-preserving downsample, test at 0.5x/0.25x, and make Pong's wordmark and HUD text release checks. |
| Font atlas cannot fit the selected glyphs. | Bake fails or quality drops. | Derive em size by binary search, fail with the glyph count and atlas size, and keep Pong's selection a bounded printable-ASCII set. |
| SVG arcs, transforms, or even-odd holes are mishandled. | Wrong silhouettes. | Limit the subset, add focused path/transform/hole tests, and reject rather than approximate silently. |
| Shader varying or uniform limits are exceeded. | Pipeline creation fails. | Bake-side size checks, a versioned fixed interface, and a GPU test that creates every built-in pipeline. |
| The unified 2D pass is more complex than the current sprite pass. | Schedule or performance regression. | Keep the built-in shader path on the existing instance/run model, benchmark each milestone, and preserve the one-draw-per-camera default. |
| Breaking changes break existing games or docs. | Upgrade friction. | Publish a 0.2 migration note and update the owning docs in the same changes. |
| The embedded built-in pack is fragile in clean builds or AOT. | No default shader. | Build it in CI, test `WithoutAssets()`, and keep the pack format identical to user packs. |
| Font licensing is unclear. | Content cannot ship. | Commit an OFL font with its license and attribution; no system-font dependency. |

## Out of scope

- A full vertex/fragment pipeline asset, custom blend/raster state, custom meshes, or a shader graph.
- Runtime shader compilation, reflection, hot reload, or shader variants.
- Basis Universal, runtime transcoding, RGBA fallback payloads, or compressed-texture fallback.
- Material assets or shared per-material uniform sets.
- SVG paint, strokes, text, images, CSS, filters, masks, clip paths, `use`, or full SVG.
- Complex text shaping, bidi, RTL, ligatures, font fallback, rich text, or word wrapping.
- A dedicated `SdfRender`. `FontRender` defaults to `shader/sdf` and may select a custom fragment
  shader for the font glyph path.
- Round line caps/joins, dashed line styles as a geometry feature, or textured lines.
- Editor, 3D, depth, MSAA, render graph, texture streaming, cubemaps, or texture arrays.
- Non-desktop platform guarantees. The BC requirement is a desktop Vulkan 1.3 decision.

## Later

After 0.2, the schema leaves room for:

- Full-pipeline shader assets with custom vertex stages and fixed-function state.
- Material assets that share shader plus uniform values across sprites.
- Basis Universal KTX2 and runtime transcode for non-BC targets.
- Full SVG paint/stroke support and a dedicated SDF renderable.
- Complex text shaping, font fallback, and rich text.
- Line caps/joins, dash styles, and screen-space-constant line widths.
- Shader hot reload for development builds, without changing the baked runtime path.
