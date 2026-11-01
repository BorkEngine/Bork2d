# Bake

`Bork2d.Bake` is the build-time asset pipeline. It is a JIT library used by game-owned assets
executables; it references `Bork2d.Engine` and the pack writer, but no runtime project references
it. The bake tooling, TOML parser, image decoder, and processors never enter a game's compile or
publish graph.

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
- `UseDefaultProcessors()` registers the built-in texture processor.
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
  processor (for example `.png` or `.circle`). Its asset base path is the pack-relative path with
  the extension removed and lowercased.
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
`TextureDescriptor` at `art/hero` and the raw RGBA8 payload at `art/hero-raw`. Its settings sidecar
`Assets/art/hero.png.toml` overrides sampler state:

```toml
filter = "linear"   # nearest | linear
wrap = "clamp"      # clamp | repeat
```

An absent key falls back to `WithTextureDefaults`. A missing sidecar is created with both defaults
and reported as an informational diagnostic. Texture decoding produces straight-alpha RGBA8 without
premultiplication, gamma changes, flipping, or resampling; 16-bit PNGs are rejected.

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

Implement `IAssetProcessor` for a sidecar-free source, or derive from `AssetProcessor<TSidecar>` for
a required-sidecar source. A processor declares a stable `Name`, its extension list, and its
sidecar policy. Its `Process` method reads the source and emits outputs through the per-source
`AssetProcessorContext`:

- `Outputs.AddAsset<T>(path, value)` emits a typed MessagePack asset; the engine serializes it.
- `Outputs.AddData(path, bytes)` emits an opaque raw payload.
- `Diagnostics.Error(code, message)` reports a `BK7xxx` failure; the source's outputs are discarded.
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
for a worked custom processor.

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

## Incremental builds

`bake` records a schema-3 manifest at `<project root>/obj/bork2d/bake/manifest.json`. It tracks the
tool version, configuration hash, registered type/processor assembly closure, mode, every input
path/hash, and every output path/hash. When all match, the bake prints an up-to-date summary and
writes nothing. Any changed source, registration, processor, pack path, mode, or output triggers a
full bake; `--force` bypasses the check, and `--check`/`validate` always run the pipeline and its
validators. `clean` removes the manifest with the packs.

## MSBuild integration

Import [`build/Bork2d.Assets.targets`](../build/Bork2d.Assets.targets) from the assets executable.
After `Build` it runs `bake --project-root $(MSBuildProjectDirectory) --mode <debug|release>`; before
`CoreClean` it runs `clean`. Useful properties: `Bork2dAssetsMode`, `Bork2dAssetsVerbose`,
`Bork2dAssetsEnabled`, `Bork2dAssetsTargetFramework`, `Bork2dDotNetHost`.

Import [`build/Bork2d.Assets.Copy.targets`](../build/Bork2d.Assets.Copy.targets) from a desktop or
AOT app to copy the baked packs. Set `Bork2dAssetsProjectDirectory` and
`Bork2dAssetsExecutable`; when `Bork2dAssetsProjectFile` is set, the targets restore and build the
assets executable out of process before copying to `$(TargetDir)` and `$(PublishDir)`. This keeps
the tooling out of the app's output and publish graph, including for a self-contained AOT app.
