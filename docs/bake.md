# Bake

`Bork2d.Bake` is the build-time asset pipeline. It is a JIT library used by game-owned assets
executables; it references `Bork2d.Engine` and the pack writer, but no runtime project references
it. The bake tooling, TOML parser, image decoder, KTX2 writer, block encoder, shader compiler, SVG
parser, MSDF generator, and processors never enter a game's compile or publish graph.

The built-in processors bake textures, fragment shaders, and SVG sources. An SVG source becomes a
normal `SpriteAsset` and a linear multi-channel signed distance field (MSDF) texture. SeasonMSDF is
a build-only dependency of `Bork2d.Bake`; it never reaches a runtime project or an AOT publish
graph.

A game owns an assets executable (for example
[Pong.Assets](../examples/pong/Pong.Assets)) whose `Program` configures `BakeApplicationBuilder`
and returns `RunAsync(args)`.

## Configuration

```csharp
using Bork2d.Bake;
using Bork2d.Engine.Assets;
using Pong.Assets.Processors;
using Pong.Common;

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

- `WithProjectRoot` is the base for relative roots, pack paths, and diagnostic paths. The build
  target passes `$(MSBuildProjectDirectory)`.
- `WithPack(packPath, root)` adds one ordered pack. The first is the main pack; later packs are
  DLC/overlay packs whose entries shadow the main pack at runtime.
- `UseDefaultAssets()` registers the engine authoring types (`SpriteAsset`) and attaches their
  built-in validators.
- `RegisterAsset<T>()` registers one data type. `T` must carry a compiled `[GenerateShape]`; there is
  no reflection binding.
- `RegisterAsset<T>(validator)` registers the type when it is not registered yet and appends a
  semantic validator. It never reports the duplicate-type diagnostic; multiple validators for one type
  run in registration order, and a null validator throws.
- `UseDefaultProcessors()` registers the built-in texture, fragment shader, and SVG processors, in
  that order.
- `WithTextureDefaults` supplies sampler state for textures without a sidecar.
- `AddProcessor` adds a custom processor; later registrations override earlier ones for the same
  extension and report an informational diagnostic.

Pack paths and roots are relative and must not contain empty, `.` or `..` segments; each is resolved
under its output root or project root and cannot escape it. A configuration error is a `BK1xxx`
diagnostic with exit code `2`; `Build()` itself does not throw for semantic errors.

## Sources and asset paths

Each pack root is walked recursively in ordinal path order. Reparse points are skipped. Files are
classified as follows:

- **Processor source.** A non-TOML file whose longest registered compound extension matches a
  processor (for example `.png`, `.frag`, `.svg`, or `.circle`). Its asset base path is the
  pack-relative path with the extension removed and lowercased.
- **Data TOML.** A `.toml` file with a root `[bork2d]` table whose `type` is a registered CLR type.
  The asset path is the file path without `.toml`, lowercased. Constructor parameters/properties
  bind by name; a data TOML filename must not contain a dot.
- **Sidecar.** A `.toml` whose exact path is `<processor source relative path>.toml`, for a
  processor with `SidecarPolicy.Required`. Sidecars are settings-only; references are rejected.
- **Untyped TOML.** A `.toml` without `[bork2d]` is an error unless it is the sidecar of a
  required-sidecar processor. A sidecar with no matching source is reported as orphaned.

Canonical asset paths are lowercase with `/` separators and segments limited to letters, digits,
`_`, and `-`, so an extension never appears in an asset path.

### Example

`Assets/sprites/hero.toml` becomes asset `sprites/hero`:

```toml
texture = "art/hero"
uvs = [[0, 0], [1, 0], [1, 1], [0, 1]]
scale = [1.0, 1.0]
pivot = [0.5, 0.5]
topology = "quad"

[bork2d]
type = "Bork2d.Engine.Assets.SpriteAsset"
```

`scale` is the per-axis world size of a full UV quad in world units; the source texture's pixel
dimensions never affect the quad geometry.

`Assets/art/hero.png` is handled by the built-in texture processor. It emits a
`TextureDescriptor` at `art/hero` and its KTX2 payload at `art/hero-ktx2`. Its settings sidecar
`Assets/art/hero.png.toml` selects the semantic channel type, the compression mode, and the sampler
state:

```toml
type = "rgba"          # rgba | rgb | rg | r
compression = "none"   # none | auto
filter = "linear"      # nearest | linear
wrap = "clamp"         # clamp | repeat
```

Every key is optional: an absent key inherits the default (`type = "rgba"`, `compression = "none"`,
and the builder's filter/wrap values). A missing sidecar is created with all four explicit keys and
reported as an informational diagnostic. `type` selects the channels the texture semantically owns:
`rgb` forces alpha to `255`, `rg` stores red and green, and `r` stores red. `compression = "auto"`
selects the block format that matches the type: BC7 for `rgba`, opaque BC1 for `rgb`, BC5 for `rg`,
and BC4 for `r`. `none` keeps the matching uncompressed UNORM format and is the default, so a texture
never becomes lossy without an explicit authoring choice.

Pong's `Assets/art/paddle.png.toml` sets `type = "rgba"` and `compression = "auto"`, so the paddle
bakes as BC7 with a full mip chain.

The payload is a complete KTX2 2.0 file: one 2D image, one array layer, one face, no
supercompression, and a full mip chain down to 1x1. Every texture is linear. Mip generation is a
deterministic box filter over the raw channel values; there is no gamma conversion. Decoding
produces straight-alpha RGBA8 without premultiplication, gamma changes, flipping, or resampling;
16-bit PNGs are rejected.

### Fragment shaders

`Assets/materials/lit.frag` is handled by the built-in shader processor. It emits a `ShaderAsset`
at `materials/lit` and the raw SPIR-V payload at `materials/lit-spv`. A shader is an ordinary asset,
not a reserved directory: it may live under any canonical asset path.

The processor supplies `#version 460`, `#extension GL_EXT_nonuniform_qualifier : require`, the fixed
fragment interface, and the `main` entry point. A source therefore contains only its own functions
and `void main()`; a `#version` or `#extension` directive (after optional leading whitespace) is a
bake error. The source is read as strict UTF-8. A source may use `discard`: the bake emits the
`DemoteToHelperInvocation` SPIR-V capability for it, so the render device must enable
`shaderDemoteToHelperInvocation` (see
[render.md](render.md#device-and-presentation)).

The prefix defines `main` as `bork2d_user_main`, and the processor appends a fixed epilogue after the
source that undefines the macro and supplies the real `void main()`. The epilogue calls the user body
and then references every fixed-interface input in a branch that cannot be reached for valid engine
data: it ORs the four extra bindless indices and compares them against `0x003FFFFF`, above the
engine's 1,048,576-entry bindless cap, and the unreachable body also references the remaining
varyings, including the line-only `vLineUv`, `vColor`, and `vLineFlags`. This keeps a user source that
does not consume every varying from making the engine-owned vertex stage's outputs appear unconsumed
(a Vulkan `WARNING-Shader-OutputNotConsumed`), and the branch body never runs, so no valid frame's
pixels change. The epilogue suffix is not part of the prologue line count used to map shaderc
diagnostics back to the source; the `#define main` line is part of the prefix and is included in that
offset.

The fixed interface is versioned by the shader layout version `3`. Version `3` is the first version
shared by sprite and line renderables: every fragment shader declares the same interface, and both
engine-owned vertex stages write all of it. Each renderable kind writes neutral values for the other
kind's inputs, so one shader asset may be assigned to either kind.

| Input | Meaning |
| --- | --- |
| `vUv` | Interpolated sprite UV; on a line, the line's `vLineUv`. |
| `vTint` | Sprite linear RGBA tint; on a line, the line's linear color. |
| `vWorldPosition` | Fragment world position. |
| `vLocalPosition` | Fragment position in the sprite's local quad space; on a line, the line's `vLineUv`. |
| `vTextureIndex` | Bindless index of the sprite's main texture; `0` (the placeholder) on a line. |
| `vExtraTextureIndices[4]` | Bindless indices of up to four additional textures; all zero on a line. |
| `vInstanceParams[4]` | The packed per-renderable uniform block. |
| `vLineUv` | Line stroke coordinate: `x` across the width (`0` at one edge, `1` at the other), `y` the distance along the line in world units. Zero on a sprite. |
| `vColor` | The line's linear RGBA color. Zero on a sprite. |
| `vLineFlags` | Flat line flags: bit `0` (value above `0.5`) means anti-aliasing is enabled; the other bits are reserved zero. Zero on a sprite. |
| `outColor` | The linear RGBA output. |

The prologue places the bindless sampler array at set `0`, binding `1` and the global block
(`vec4 time; vec4 globals[8]`) at set `1`, binding `2`. Layout version `2` was the first version with
that fixed placement; version `3` keeps it unchanged and adds the line-only inputs at locations `13`
through `15`, after `vExtraTextureIndices` (`5` through `8`) and `vInstanceParams` (`9` through
`12`).

The prologue also defines the direct-use macros `BORK2D_MAIN_TEXTURE`, `BORK2D_TEXTURE0` through
`BORK2D_TEXTURE3`, and `BORK2D_TIME`, plus one macro per sidecar-declared uniform so a declared name
such as `uPulse` is usable directly. Every macro expansion is parenthesized.

The required sidecar `<source>.toml` declares the additional texture slots and uniforms:

```toml
textureSlots = ["mask", "noise"]

[uniforms]
uPulse = { type = "float", default = 1.0 }
uTint = { type = "color", default = [1.0, 1.0, 1.0, 1.0] }

[globals]
uWind = { type = "vec2", default = [0.0, 0.0] }
```

- `textureSlots` holds at most four unique GLSL identifiers. Their positions correspond to the
  sprite's additional texture positions; the names are metadata only.
- `type` is exactly `float`, `vec2`, `vec3`, `vec4`, or `color` (case-sensitive).
- `default` is absent for a zero value, a scalar for `float`, or an array of numbers whose length
  matches the declared component count: `float` 1, `vec2` 2, `vec3` 3, and `vec4`/`color` 4. Every
  component must be finite.
- A declared name must be a GLSL identifier that does not start with `gl_`, `BORK2D_`, or
  `bork2d`, and must be unique across `[uniforms]` and `[globals]`.

The instance block has 16 float lanes. Declarations are sorted by ordinal name and assigned in that
order: `float` takes one lane and aligns to one, `vec2` takes two and aligns to two, and `vec3`,
`vec4`, and `color` take their width and align to four, so a declaration never crosses a `vec4`
lane boundary. Overflow is an error; the bake never truncates. The global block has eight `vec4`
slots: each declaration gets one slot at float lane `slot * 4`, in ordinal name order. The emitted
`ShaderAsset` records the packed declarations in that order and the texture slots in sidecar order;
unused `Default` components are zero.

A missing sidecar is created empty and reported as an informational diagnostic, so the shader
declares no uniforms and no extra slots. An existing empty sidecar has the same meaning. Shader
compilation uses shaderc 2.23.0 in the bake tooling; no runtime project references it.

### SVG and MSDF

`Assets/art/wordmark.svg` is handled by the built-in SVG processor. An SVG source is a silhouette:
the processor parses shape geometry, bakes it into a linear multi-channel signed distance field
(MSDF) texture, and emits ordinary assets:

- A `SpriteAsset` at `art/wordmark` that references the `art/wordmark-sdf` texture with full UVs
  (top-left, top-right, bottom-right, bottom-left), a center pivot, and the scale from the bake
  result. It is a normal sprite asset and the built-in `shader/sdf` is the intended fragment shader
  for it; the asset remains shader-agnostic because the bake does not choose a shader and a game
  assigns it.
- A `TextureDescriptor` at `art/wordmark-sdf` with the base dimensions, the linear `Rgb8` (or
  explicit BC7) format, the full mip chain, `Filter = linear`, and `Wrap = clamp`.
- The raw KTX2 payload at `art/wordmark-sdf-ktx2`.

The required sidecar `wordmark.svg.toml` carries the bake settings:

```toml
resolution = 512      # maximum output texture dimension in pixels, 1..8192
range = 8             # distance spread in pixels, greater than zero
padding = 2           # extra pixels beyond the range margin, at least zero
compression = "none"  # none | bc7
```

Every key is optional. An absent key inherits the default (`resolution = 512`, `range = 8`,
`padding = 2`, `compression = "none"`). A missing sidecar is created with all four explicit keys and
reported as an informational diagnostic. Invalid TOML, an unknown key, or an unknown enum value uses
the shared `BK2xxx` binder diagnostics; a value that binds but cannot form a bake is `BK9204`.

#### Supported subset

The root `svg` element, `g`, `path`, `rect`, `circle`, `ellipse`, `line`, `polyline`, and `polygon`
are supported, with `viewBox`, `width`/`height`, `transform`, `fill`, and `fill-rule`. Elements are
matched by local name, so a prefixed SVG namespace still parses. Paint, stroke, opacity, color,
gradients, filters, masks, clip paths, text, images, CSS, `defs`, and `use` are ignored; `defs` and
`use` are not expanded. An unsupported element and its subtree are skipped with one informational
diagnostic and the rest of the document still bakes, while a stroke-only shape, `fill="none"`, or any
other document without drawable fill geometry is `BK9205`. A malformed path, transform, length, fill
rule, or geometry attribute is `BK9202`.

#### Artboard and sizing

The artboard is the root `viewBox` rectangle, else the root `width`/`height`, else the geometry
bounds. The bake reserves `range + padding` pixels on every side of the artboard. `resolution` is the
maximum output dimension in pixels: the longer artboard axis fills `resolution - 2 * (range +
padding)`, the shorter axis keeps the artboard aspect, and each texture dimension is the content
extent plus both margins rounded up. The long axis is exactly `resolution`; a layout that leaves no
usable artboard is `BK9204`.

The sprite's `Scale` makes the visible artboard exactly one world unit high while preserving the
source aspect: with texture width `W`, texture height `H`, and content height `C` in pixels,
`Scale = (W / C, H / C)`. The field margin is part of the texture and the full UVs, so the sprite
quad covers the margin and the visible artboard (not the padded texture) is one unit high. For a
`200x100` viewBox with `resolution = 128`, `range = 8`, and `padding = 2`, the texture is `128x74`,
the content is `108x54`, and the scale is `(128/54, 74/54)`.

#### Compression

`compression = "none"` stores the field as uncompressed linear `Rgb8` (logical RGB in RGBA8 with
alpha forced to `255`) and is the default. `compression = "bc7"` is an explicit opt-in that stores
the field as BC7 `CompRgba8`. Encoding is validated: the base level must stay within the accepted
median fidelity (maximum error 1.25 level pixels, RMS 0.10 level pixels), and every mip level whose
long axis is at least four pixels must preserve the decoded sign; smaller levels are skipped. The
bake reports `BK9207` for a base level that exceeds the accepted median fidelity or a validated mip
whose decoded sign flips, and never falls back to uncompressed output. MSDF data is distance data,
not color data, so the generic texture sidecar's `type = "rgb"` with `compression = "auto"` (opaque
BC1) is never used for it; BC7 is available only through this sidecar's explicit `compression` key.

Every MSDF payload is a complete KTX2 2.0 file with one 2D image, one array layer, one face, no
supercompression, and a full distance-preserving mip chain down to 1x1. MSDF generation uses
SeasonMSDF 0.2.0 in the bake tooling; no runtime project references it.

#### Diagnostics

| Code | Meaning |
| --- | --- |
| `BK9201` | The source could not be read as strict UTF-8, is not well-formed XML, or does not have an `svg` root element. |
| `BK9202` | Invalid supported SVG content: a bad `viewBox`, `width`, `height`, length, transform, fill rule, path data, or geometry attribute, a non-finite number, or a rounded rectangle. |
| `BK9203` | An unsupported element or feature was skipped; the parse continues and the bake emits outputs. |
| `BK9204` | An SDF bake option is invalid: `resolution` outside `1..8192`, `range` not positive, `padding` negative, or margins that leave no usable artboard. |
| `BK9205` | The document has no drawable fill geometry. |
| `BK9206` | The field cannot represent the shape, or the bake failed while generating or encoding it. |
| `BK9207` | Opt-in BC7 encoding changed the field beyond the accepted fidelity or flipped the decoded sign; no fallback is emitted. |

Parse and bake diagnostics carry the source location when they have one (for example
`line 2, column 5`). A source with an error emits no sprite, descriptor, or payload.

## Asset validators

A registered data type can carry semantic validators. `RegisterAsset<T>(validator)` registers the type
if necessary and appends the validator, so several validators for one type run in registration order;
`RegisterAsset<T>()` alone only registers the type. `UseDefaultAssets()` attaches the built-in
`SpriteAsset` validator exactly once; when the game already registered a `SpriteAsset` validator, the
built-in one is appended after it.

```csharp
public sealed class GameSettingsValidator : IAssetValidator<GameSettings>
{
    public void Validate(
        string assetPath,
        GameSettings asset,
        IAssetProcessorDiagnostics diagnostics)
    {
        if (asset.PaddleSpeed <= 0f)
        {
            diagnostics.Error("BK8100", $"Asset '{assetPath}' has a non-positive 'paddleSpeed'.");
        }
    }
}
```

```csharp
return await new BakeApplicationBuilder()
    .WithProjectRoot(".")
    .WithPack("assets.pck", "Assets")
    .UseDefaultAssets()
    .RegisterAsset<GameSettings>(new GameSettingsValidator())
    .Build()
    .RunAsync(args);
```

Validators run after a pack's custom data binds and before its processors, index, reference validation,
composition, or staging; `--check` and `validate` run them too. A validator reports through
`IAssetProcessorDiagnostics`, the same public sink a processor uses, and every diagnostic carries the
asset's canonical path and its source display path. An error fails the pack at the validation stage, so
every target stays byte-identical and no staging file is written; warnings and infos are reported
without failing. Validators run synchronously on the bake thread and one instance is reused for every
asset of its type, so a validator must not keep per-asset state or throw for malformed data. A custom
validator uses the `BK8xxx` range and names the offending field in its message.

The built-in sprite validator rejects the field values the renderer treats as fatal:

| Code | Condition |
| --- | --- |
| `BK8001` | `uvs` is null or does not have exactly four entries, or a UV coordinate is not finite. |
| `BK8002` | `scale` is not finite or is not greater than zero on either axis. |
| `BK8003` | `pivot` is not finite or is outside `[0,1]`. |
| `BK8004` | The topology is an unknown value, a `quad` carries indices, or an indexed topology has no indices or an index outside `0..3`. |

An absent texture is valid because the renderer draws its placeholder, and UV spans outside `[0,1]`
are valid because repeat and clamp samplers use them. Validators cover custom-data assets only; they do
not validate processor outputs, sidecars, or runtime-loaded assets.

## Processors

`UseDefaultProcessors()` registers the built-in processors:

| Source | Name | Outputs | Sidecar |
| --- | --- | --- | --- |
| `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tga` | `texture` | `TextureDescriptor` at the base path, KTX2 payload at `<base>-ktx2`. | Required; created with `type`/`compression` and the `WithTextureDefaults` sampler values when missing. |
| `.frag` | `shader` | `ShaderAsset` at the base path, raw SPIR-V at `<base>-spv`. | Required; created empty when missing. |
| `.svg` | `svg` | `SpriteAsset` at the base path, `TextureDescriptor` at `<base>-sdf`, KTX2 payload at `<base>-sdf-ktx2`. | Required; created with `resolution`, `range`, `padding`, and `compression` when missing. |

Implement `IAssetProcessor` for a sidecar-free source, or derive from `AssetProcessor<TSidecar>` for
a required-sidecar source. A processor declares a stable `Name`, its extension list, and its
sidecar policy. Its `Process` method reads the source and emits outputs through the per-source
`AssetProcessorContext`:

- `Outputs.AddAsset<T>(path, value)` emits a typed MessagePack asset; the engine serializes it.
- `Outputs.AddData(path, bytes)` emits an opaque raw payload.
- `Diagnostics.Error(code, message)` reports a failure using a `BK7xxx` code or the processor's
  specific range (the shader and SVG processors use `BK9xxx`); the source's outputs are discarded.
  `Warning` and `Info` report without discarding.

`IAssetProcessor`, `AssetProcessorContext`, `IAssetOutputSink`, `IAssetProcessorDiagnostics`, and
`SidecarPolicy` are the only processor contract. The processor registry, runner, and sidecar binder
execute that contract directly. A framework-internal registration wrapper holds only the metadata
the pipeline needs beyond the contract — the processor's CLR type for incremental hashing and the
typed-sidecar dispatch for `AssetProcessor<TSidecar>` — and adds no second context, sink,
diagnostics, or policy model.

Processors run synchronously on the bake thread and may be reused, so they must not keep
per-source state. They never touch MessagePack or the pack writer directly. A failed source emits
no outputs. See [CircleProcessor.cs](../examples/pong/Pong.Assets/Processors/CircleProcessor.cs)
for a worked custom processor: it renders the ball pixels and calls `TextureBaker.Bake` with
`TextureChannelType.Rgba` and `TextureCompression.None`, so the ball is an uncompressed RGBA8 KTX2
texture with the full mip chain, and the helper's format and mip count go into the emitted
descriptor.

### Texture baking

`TextureBaker.Bake` is the public KTX2 producer. The built-in texture processor and a custom
processor that generates pixels both call it, so neither hand-writes KTX2 headers or block data:

```csharp
BakedTexture baked = TextureBaker.Bake(
    rgba8Pixels,
    width,
    height,
    TextureChannelType.Rgba,
    TextureCompression.Auto);

TextureDescriptor descriptor = new()
{
    Payload = context.BaseAssetPath + "-ktx2",
    Width = (uint)width,
    Height = (uint)height,
    Format = baked.Format,
    MipLevels = baked.MipLevels,
};
context.Outputs.AddAsset(context.BaseAssetPath, descriptor);
context.Outputs.AddData(context.BaseAssetPath + "-ktx2", baked.Payload.Span);
```

- `rgba8Pixels` is straight-alpha RGBA8, row-major, exactly `width × height × 4` bytes.
- `TextureChannelType` is `Rgba`, `Rgb`, `Rg`, or `R`; `TextureCompression` is `None` or `Auto`.
- `BakedTexture.Payload` is the complete KTX2 file, `Format` is the concrete `TextureFormat`, and
  `MipLevels` is the full chain length. The descriptor must carry those same values.
- Invalid arguments throw `ArgumentException`/`ArgumentOutOfRangeException`; an encoding or container
  failure throws `InvalidOperationException`. The helper exposes no block-encoder types.

The built-in processor maps the sidecar to the same enums, so a custom processor that wants a
sidecar can use its own typed sidecar and call the helper directly.

## Built-in asset pack

The engine's own shader and placeholder assets live under
[src/Bork2d/BuiltInAssets/Assets](../src/Bork2d/BuiltInAssets/Assets) and are baked by the build-only
[Bork2d.BuiltInAssets](../build/Bork2d.BuiltInAssets/Bork2d.BuiltInAssets.csproj) generator. The
generator configures the ordinary builder with `WithPack("bork2d-builtin.pck", "Assets")`,
`UseDefaultProcessors()`, and the tool-only `builtin-vertex`, `builtin-present`, and
`builtin-placeholder` processors, runs the normal `bake` command into a unique temporary output
directory, and writes the pack as a generated C# byte array. `Bork2d.Runtime` compiles that file into
its assembly and mounts the pack below the game packs; no loose pack file is published.

The source tree uses the same processors as game content. `shader/sprite.frag` and its checked-in
empty `shader/sprite.frag.toml` sidecar produce the `shader/sprite` `ShaderAsset` and the
`shader/sprite-spv` payload, exactly like a game shader, and `shader/line.frag` with its empty
`shader/line.frag.toml` sidecar produces the built-in line fragment `shader/line` and its
`shader/line-spv` payload. `shader/sdf.frag` with its checked-in `shader/sdf.frag.toml` sidecar
produces the built-in SDF fragment `shader/sdf` and its `shader/sdf-spv` payload; that sidecar
declares the `uSoftness` and `uThreshold` instance uniforms. The sidecars are committed so a clean
build never creates or mutates a source file. The tool-only `builtin-vertex` processor owns `.vert`:
it reads each source as strict UTF-8, compiles it through the shared `GlslCompiler`, and emits
`<base>-spv`, so the sprite stage yields `shader/vertex-spv` and the new line stage yields
`shader/line-vertex-spv`. `.vert` is not registered by `UseDefaultProcessors()`, so it is not a
user-facing source type. `builtin-present` does the same for the engine-owned `.pvert`/`.pfrag`
present stages and emits `present/vertex-spv` and `present/fragment-spv`.

`textures/placeholder.checker` is a UTF-8 text source containing one integer resolution (8). The
tool-only `builtin-placeholder` processor validates it against a small range, generates a magenta and
opaque-black checkerboard, and calls `TextureBaker.Bake` with the uncompressed RGBA channel type and
the full mip chain. It emits an uncompressed RGBA8 `TextureDescriptor` at `textures/placeholder` with
nearest filtering and clamp wrapping, plus the KTX2 payload at `textures/placeholder-ktx2`. The
runtime loads and validates that pair at renderer startup as the bindless placeholder page at index
`0`; it is ordinary pack content and has no generated-pixel fallback path.

[build/Bork2d.BuiltInAssets.targets](../build/Bork2d.BuiltInAssets.targets) is imported by
`Bork2d.Runtime.csproj`. Before `CoreCompile` it restores and builds the generator out of process,
regenerates the pack when any built-in source or the tool assembly changes, and adds the generated
file to the compilation. It works from a clean clone and never writes into the source tree: the pack
goes to a temporary output directory and the generator removes the bake's incremental state
afterwards. Generated files are never committed.

## CLI

The assets executable is the CLI:

| Command | Purpose |
| --- | --- |
| `bake` | Bake every configured pack. Incremental unless `--force`; `--check` validates and writes nothing. |
| `validate` | Run the full validation pipeline without writing a pack. |
| `clean` | Delete every configured pack output and the incremental bake state. |
| `copy-packs --output-root <dir>` | Copy each configured pack from next to the assets executable to the output root. |
| `--help`, `--version` | Usage and tool version. |

Common options: `--project-root <dir>`, `--output-root <dir>` (`bake`/`clean`/`copy-packs`),
`--mode debug|release` (`bake`/`validate`), `--check`/`--force` (`bake`), and `--verbose|--quiet`.

Exit codes: `0` success or up to date, `1` source diagnostics, `2` usage/configuration error, `3`
unexpected internal failure. Diagnostics go to `stderr` as `path(line,col): error BKxxxx: message`;
the summary goes to `stdout`.

The output root defaults to the assets executable's directory. `bake` stages every pack as soon as
that pack validates: after binding and semantic validation, the composed pack is written to a unique
temporary file next to its target and the builder is released before the next pack is composed, so
only one `PackBuilder` and its payloads are alive at a time and staged packs live on disk. A validator
error fails the pack before composition, so nothing is staged for that pack and the previous targets
stay unchanged. The staged set is published only after every configured pack has validated and staged.
Publishing replaces one target at a time: an existing target is moved to a unique backup, the staged
file takes its place, and if any replacement fails every already-replaced target is restored from its
backup and the failing path is reported as `BK5002`. A failed bake therefore leaves every pack
byte-identical, and `--check` writes neither targets nor staging files.

A normal write failure is fully rolled back. A process crash between the target-to-backup and
staged-to-target moves cannot be rolled back by the baker; the backup and staging files are then the
only recovery source and may be left behind.

## Diagnostic codes

Codes are stable once a range is released. A diagnostic renders as
`path(line,col): error BKxxxx: message` on `stderr`.

| Range | Area |
| --- | --- |
| `BK1xxx` | Builder configuration and data-type registration. |
| `BK2xxx` | TOML parse/bind and sidecars. |
| `BK3xxx` | Asset references and baked-set validation. |
| `BK4xxx` | Built-in texture decode, probe, and payload; sidecars use the shared `BK2xxx` codes. |
| `BK5xxx` | Pack layout/write, pack copy, and incremental state. |
| `BK6xxx` | MSBuild/build integration. |
| `BK7xxx` | Processor registration and processing. |
| `BK8xxx` | Registered semantic asset validators, including the built-in sprite validator. |
| `BK9xxx` | Fragment shader sources and metadata: unreadable or forbidden source (`BK9001`), shaderc compilation failure (`BK9002`), invalid sidecar name/type/default/slots (`BK9003`), and instance/global uniform block overflow (`BK9004`). Also bake-side texture failures: KTX2/BC encoding or container writing (`BK9100`) and an impossible sidecar channel-type/compression mapping (`BK9101`). SVG sources and MSDF baking use `BK9200`-`BK9299`: malformed or unreadable source (`BK9201`), invalid supported content (`BK9202`), ignored features (`BK9203`), invalid SDF options (`BK9204`), no drawable geometry (`BK9205`), SDF representation/encoding failure (`BK9206`), and rejected BC7 fidelity/sign (`BK9207`). |

## Incremental builds

`bake` records a schema-3 manifest at `<project root>/obj/bork2d/bake/manifest.json`. It tracks the
tool version, configuration hash, registered type/processor assembly closure, mode, every input
path/hash, and every output path/hash. The configuration hash shape is
`bork2d.bake.configuration.v4`; the manifest schema version is unchanged. The hash includes the
shader tool marker `shaderc-2.23.0/layout-3/prologue-4/packer-1`, the texture tool marker
`ktx2-1/bcn-2.3.0/mips-1`, and the SVG tool marker `svg-1/seasonmsdf-0.2.0/sdfmips-1`, so upgrading
shaderc or BCnEncoder.Net, changing the fragment layout, prologue, uniform packer, KTX2 writer, mip
filter, SVG parser, MSDF generator, or SDF mip handling invalidates a manifest written by the
earlier tool. When all match, the bake prints an up-to-date summary and writes nothing. Any changed
source, registration, processor, pack path, mode, or output triggers a full bake; `--force` bypasses
the check, and `--check`/`validate` always run the pipeline and its validators. A missing required
sidecar that the bake creates is re-collected as a tracked input, so the next bake can be up to date.
`clean` removes the manifest with the packs.

## MSBuild integration

Import [`build/Bork2d.Assets.targets`](../build/Bork2d.Assets.targets) from the assets executable.
After `Build` it runs `bake --project-root $(MSBuildProjectDirectory) --mode <debug|release>`; before
`CoreClean` it runs `clean`. Useful properties: `Bork2dAssetsMode`, `Bork2dAssetsVerbose`,
`Bork2dAssetsEnabled`, `Bork2dAssetsSourceRoot` (project-relative, default `Assets`),
`Bork2dAssetsTargetFramework`, `Bork2dDotNetHost`.

Import [`build/Bork2d.Assets.Copy.targets`](../build/Bork2d.Assets.Copy.targets) from a desktop or
AOT app to copy the baked packs. Set `Bork2dAssetsProjectDirectory` and
`Bork2dAssetsExecutable`; when `Bork2dAssetsProjectFile` is set, the targets restore and build the
assets executable out of process before copying to `$(TargetDir)` and `$(PublishDir)`. This keeps
the tooling out of the app's output and publish graph, including for a self-contained AOT app.

The mode selects payload compression, not automatic asset watching: Debug packs are uncompressed
and Release packs use Zstd only when it is strictly smaller. The assets and copy targets declare
the source root as `UpToDateCheckInput` and as incremental target inputs, with a stamp output, so
Visual Studio, Rider, and plain MSBuild rerun the bake after an asset-only edit instead of skipping
an apparently up-to-date project. Without that declaration an IDE can skip MSBuild entirely after
the first build and the manifest's own change detection never runs. A stale project can always be
refreshed with Rebuild; the bake manifest then detects the changed source and rewrites the pack.
