# M4 WP0 — Serialization and Bake Feasibility Spike (report)

> **Status:** complete (WP0 gate). This is the feasibility report, not production code. The spike
> sources lived in an untracked `.spike/` directory and were deleted before the commit; every
> experiment is reproduced by the commands, snippets, and observed output below.
>
> **Scope:** D-M4-11/D-M4-12 shape and load contracts; Tomlyn 2.10.1 binding, converters, and
> source spans (Q-06/Q-07); generated-host build/load and type identity (Q-08); NativeAOT shape
> proof; incremental-hash primitive (Q-09, XXH3 override).
>
> **Owner decisions honored:** all §18 defaults, with Q-04 (mapped types may live outside
> `Project.Common` when referenced by it) and Q-09 (XXH3 instead of SHA-256).
>
> **Environment:** Windows 11 Pro 10.0.26300, PowerShell 7.6.8, .NET SDK
> `11.0.100-rc.1.26425.128`, `net11.0`, win-x64, MSVC (VS 18) for NativeAOT.
>
> **Repository state:** branch `m-4/wp-0`, base `de0fcfe3505e1777bfdbb1b80105c7f5594a0bd6`. No
> production code, project, package, or document other than this report is changed.

## 1. Verdict summary

| Area | Verdict | Notes |
| --- | --- | --- |
| A. PolyType 1.4.1 shapes + Nerdbank.MessagePack 1.4.95 | **PASS** | class root, `readonly record struct` root (primary ctor), nested struct members, struct collections, byte-stable output (same and separate processes). |
| A. MessagePack `nil` for a struct root | **PASS with a required loader change** | Nerdbank returns `default(T)` silently for a `nil` struct payload; `DeserializeObject` boxes the default (still no null). The loader must inspect the first MessagePack token (`0xC0`) and fault `Decode` before deserializing. Proven with a one-byte `ReadByte` + replay-stream peek. |
| A. `Color` via `[TypeShape(Marshaler = ...)]` | **PASS** | The spec's proposed form is correct for PolyType 1.4.1, including cross-assembly, non-`partial`, and no `[GenerateShape]` on `Color`. Exact signature in §3. |
| A. D-M4-11 relaxation (`class` removal) | **PASS** | a shape-only `where T : IShapeable<T>` compiles and works for struct roots; `AssetRef<Struct>` needs the same relaxed constraint and marshals as a string. |
| B. Tomlyn 2.10.1 + D-M4-04 STJ attributes | **PASS** | `[JsonPropertyName]`, `[JsonIgnore]`, `[JsonRequired]` are honored natively (reflection and source-gen). `[JsonUnmappedMemberHandling]` is **not** honored. |
| B. TOML mapping / converters / spans | **PASS for the DOM binder; source-gen insufficient** | unknown keys are silently ignored and source-gen exposes no spans; `MetadataStore` gives key spans only. → Q-07 = generated DOM binder; Q-06 = syntax-tree value-span pass. |
| C. Generated host + ALC + type identity | **PASS with one production blocker** | fixed `Bork2d.BakeHost` name, non-collectible shared ALC, internal contract + IVT, `PackBuilder` round-trip all work. **`AssetReferenceCollector.VisitSurrogate` is not implemented**, so `PackBuilder.AddMessagePack<T>` throws for any type containing a marshaler-mapped member (the exact `Color` case). See §5.4 and §9. |
| D. NativeAOT | **PASS** | `dotnet publish -c Release -r win-x64` with **0 warnings**; class root, struct root, `nil` decode fault, and the marshaler shape run under AOT and exit 0. |
| E. `System.IO.Hashing` 11.0.0-rc.1.26425.128 | **PASS** | `XxHash3`/`XxHash128` produce deterministic output in-process and across processes; incremental `Append`/`GetHashAndReset` works. |

### 1.1 Decisions made by WP0

| ID | Decision |
| --- | --- |
| Q-06 | **Value spans come from a `SyntaxParser.Parse` → `DocumentSyntax` pass** keyed by member path (`items[2].icon`, `meta.icon`, `inline[0].icon`), reading `KeyValueSyntax.Value.Span.Start.Line/Column` (0-based → add 1). `MetadataStore`/`TomlPropertyMetadata.Span` is **not** used: it is the key span, not the value span, and it is not populated by a source-gen context. |
| Q-07 | **Generated DOM binder.** The host generator emits code that reads `TomlTable` values and resolves member-path → value span from the syntax pass, assigns members, and records reference sites. Tomlyn's POCO/source-gen path cannot enforce unknown-key errors and exposes no value spans; it is not used for binding. |
| Q-08 | **CLI `generate-host` writes a normal SDK `BakeHost.csproj`; MSBuild builds it; the CLI loads it in a non-collectible `AssemblyLoadContext` named for the fixed assembly `Bork2d.BakeHost`.** The resolver shares default-context `Bork2d.*`, `Tomlyn`, `PolyType`, `Nerdbank.*`, `System.*`, `Microsoft.*` and loads only the host and `Project.Common` from the host directory. Proven end to end. |

## 2. Packages and exact versions

| Package | Version | Role |
| --- | --- | --- |
| `PolyType` | `1.4.1` (exact) | `[GenerateShape]`, `[TypeShape]`, `IShapeable<T>`, `IMarshaler<,>`, `TypeShapeVisitor` |
| `Nerdbank.MessagePack` | `1.4.95` (exact) | MessagePack writer/reader over generated shapes |
| `Tomlyn` | `2.10.1` (exact) | TOML 1.1.0 parser, `TomlTable` DOM, syntax tree, optional POCO/source-gen serializer |
| `System.IO.Hashing` | `11.0.0-rc.1.26425.128` (exact) | `XxHash3` / `XxHash128` for the incremental manifest |

All four restore and run on SDK `11.0.100-rc.1.26425.128` / `net11.0`. No new package is needed
for WP1–WP3 beyond these (Tomlyn and `System.IO.Hashing` are already tooling/runtime pins).

## 3. A — PolyType 1.4.1 and Nerdbank.MessagePack 1.4.95

### 3.1 Shapes that work

```csharp
using Nerdbank.MessagePack;
using PolyType;

[GenerateShape]
public sealed partial class EnemyDefinition
{
    public required string Name { get; set; }
    public int Health { get; set; }
    public EnemyStats Stats { get; set; }
    public Vec2[]? Waypoints { get; set; }
    public List<EnemyStats>? History { get; set; }
}

[GenerateShape]
public readonly partial record struct EnemyStats(int Attack, int Defense);

[GenerateShape]
public readonly partial record struct NestedRoot(
    EnemyStats Id, Vec2[] Points, Dictionary<string, EnemyStats> Map);
```

* `partial` is required on every `[GenerateShape]` root; the generated shape type is
  `PolyType.SourceGenModel.SourceGenObjectTypeShape<T>` and the type implements `IShapeable<T>`.
* Nested structs and collections of structs are generated transitively; they do **not** need their
  own attribute.
* Obtaining the shape for the *explicit-shape* serializer overloads needs a generic helper
  (`T.GetTypeShape()` is a static abstract interface member and is not callable on a concrete type):

```csharp
static ITypeShape<T> Shape<T>() where T : IShapeable<T> => T.GetTypeShape();
```

* `new MessagePackSerializer()` has only a parameterless constructor in 1.4.95. The **instance**
  `Serialize<T>` / `Deserialize<T>` overloads constrain `T : IShapeable<T>`; the unconstrained
  overloads are extension methods and are what `NBMsgPack051` warns about. There is **no
  reflection fallback**: the extension path on a non-shapeable type throws
  `NotSupportedException: The type 'NoShape' does not have a generated shape. Ensure that the type is
  annotated with the 'GenerateShape' attribute.` This confirms the repository's existing
  no-reflection-resolver claim.

### 3.2 Byte stability (D-M4-07)

Serializing the same instance twice produced identical bytes; two separate process runs produced
the same hex. Observed (PascalCase map keys, `CA` float32):

```text
class root (130): 85A44E616D65A56772756E74A64865616C74680A…A9576179706F696E7473…
class stable = True
struct root (18): 82A641747461636B07A7446566656E736509
struct stable = True
nested struct root (70): 83A2496482A641747461636B01A7446566656E736502…A34D6170…
```

Separate-process comparison of the same program produced no diff (`Compare-Object` empty).
The generated shape is the only metadata source, so payload bytes are deterministic for identical
values and a fixed serializer configuration.

### 3.3 `nil` payload for a struct root — the loader requirement

| Payload | `Deserialize<T>(bytes, shape)` | `DeserializeObject(bytes, shape)` |
| --- | --- | --- |
| `C0` (nil), `T` = struct root | returns `default(T)` (e.g. `(0,0)`) — **silent default** | returns the boxed default, not `null` |
| `C0` (nil), `T` = class root | returns `null` (the existing loader null check catches it) | returns `null` |
| empty (`0` bytes), struct root | `MessagePackSerializationException: An error occurred during deserialization.` | same |
| class member `Id = nil` where `Id` is a struct | `MessagePackSerializationException` (constructor binding rejects nil) | — |

**D-M4-11 implementation requirement.** `AssetDeserializer.Deserialize<T>` must reject a `nil`
root before deserializing. The minimal, streaming-safe mechanism proven in the spike:

```csharp
int first = stream.ReadByte();
if (first < 0) throw AssetException(Decode, path, "empty payload");
if (first == 0xC0) throw AssetException(Decode, path, "nil payload for a value-type root");
using var replay = new PrependStream((byte)first, stream); // one-byte replay stream
T value = new MessagePackSerializer().Deserialize<T>(replay, T.GetTypeShape());
```

A one-byte replay stream is required because `Deserialize<T>(Stream, ITypeShape<T>)` owns the
stream position; buffering the whole payload is not necessary. The check is safe to apply
unconditionally (class roots still produce `null` for non-nil maps only when the map itself is
nil, which is the same fault). The relaxed signature compiles for both classes and structs:

```csharp
internal static T? DeserializeAsset<T>(Stream stream) where T : IShapeable<T>
    => _serializer.Deserialize<T>(stream, T.GetTypeShape());
```

### 3.4 `Color`: exact working contract

PolyType 1.4.1's `TypeShapeAttribute` supports `Marshaler` and is valid on **Class, Struct, Enum,
Interface**; `GenerateShapeAttribute` is a separate attribute (Class, Struct, Interface). The
spec's proposed form is correct. Exact working signature (verified cross-assembly, non-`partial`,
no `[GenerateShape]` on the marshaled type):

```csharp
// In Bork2d.Api (WP1)
using PolyType;

[TypeShape(Marshaler = typeof(ColorMarshaler))]
public readonly record struct Color(float R, float G, float B, float A);

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ColorMarshaler : IMarshaler<Color, float[]>
{
    public float[] Marshal(Color value) => [value.R, value.G, value.B, value.A];

    public Color Unmarshal(float[]? surrogate)
        => new(surrogate![0], surrogate[1], surrogate[2], surrogate[3]);
}
```

Observed with a mirror assembly (`SpikeColor` + marshaler in a referenced assembly, holder in the
consumer assembly):

```text
SpikeColor holder (77): 82A454696E7494CA3F800000…  (surrogate float[4], 94 CA …)
stable = True
roundtrip = Tint=SpikeColor { R = 1, G = 0,5, B = 0,25, A = 1 }
```

Without the attribute, the generated member shape is an object map (`84 A1 52 …` = `{R,G,B,A}`),
which is what real `Color` produces today; WP1's attribute changes it to `[r, g, b, a]`.

Consequences of the verified contract:

* `Color` must stay non-`partial` and must **not** get `[GenerateShape]`; the marshaled shape is
  generated into the consumer's provider, and `Color` itself does not implement `IShapeable<Color>`
  (`typeof(IShapeable<Color>).IsAssignableFrom(typeof(Color)) == false`).
  `Color` therefore cannot be a top-level asset root (it is not a §7.3 root anyway).
* `IMarshaler<TTarget, TSource>` is `TTarget Marshal(TSource)` / `TSource Unmarshal(TTarget)`; the
  concrete `Unmarshal` parameter must be nullable (`float[]?`) or the compiler reports CS8767.
* The surrogate is `float[]`, so serialization allocates a 4-element array per `Color`. Acceptable
  for authoring/bake volume; a custom `readonly record struct` surrogate would avoid the allocation
  but changes the packed form and is not part of D-M4-12.
* D-M4-12 does not need to change.

### 3.5 D-M4-11 `AssetRef<Struct>`

A mirror of the relaxed contract compiles and round-trips (the production types still carry
`where TAsset : class`, so WP1 removes it):

```csharp
[TypeShape(Marshaler = typeof(StructAssetRefMarshaler<>))]
public readonly record struct StructAssetRef<TAsset>(string? Path)
    where TAsset : IShapeable<TAsset>;

public sealed class StructAssetRefMarshaler<TAsset> : IMarshaler<StructAssetRef<TAsset>, string?>
    where TAsset : IShapeable<TAsset> { /* Path <-> string? */ }
```

Observed: `81A4426F7373A9646174612F626F7373` (`{"Boss": "data/boss"}`) and a matching round-trip;
a struct root also round-trips with the shape-only path. No PolyType or Nerdbank blocker.

## 4. B — Tomlyn 2.10.1 mapping, converters, and spans

### 4.1 D-M4-04: System.Text.Json attribute support — **works**

Tomlyn 2.10.1 advertises and implements STJ attribute interop. Verified with the reflection path
(`PropertyNamingPolicy = JsonNamingPolicy.CamelCase`) and with a source-gen context:

| Contract | Result |
| --- | --- |
| camelCase via `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` | pass (`count = 3` → `Count`) |
| `[JsonPropertyName("custom_name")]` | pass (reads `custom_name`) |
| `[JsonIgnore]` | pass (key ignored; a value present under the ignored name is not bound) |
| `[JsonRequired]` | pass (`TomlException: (2,1) : error : Missing required TOML key 'must'…`) |
| `[JsonConstructor]` / `[TomlConstructor]` | supported (documented; source-gen also matches constructor parameter names) |
| `[TomlExtensionData]` (and documented `[JsonExtensionData]`) | pass for `[TomlExtensionData]` (captures unmapped keys into the dictionary) |
| `[JsonUnmappedMemberHandling(Disallow)]` | **not honored** (unknown key still accepted) |
| C# `required` keyword (reflection path) | **not enforced**; only `[JsonRequired]`/`[TomlRequired]`. Source-gen contexts honor C# `required` (documented, and `[JsonRequired]` verified). |
| Unknown keys as errors (Q-05 default) | **not expressible** in Tomlyn 2.10.1; the only unknown-key mechanism is extension-data capture |

`TomlSerializerOptions` has no unknown-key/unmapped-member setting (verified by reflection of all
properties). Therefore the approved Q-05 default (“unknown keys are errors”) requires the generated
DOM binder.

### 4.2 POCO binding matrix (reflection path, camelCase)

| Case | Result |
| --- | --- |
| class with `[JsonRequired]` | pass / missing key throws with line 2 col 1 |
| `readonly record struct` root with primary constructor | pass (`attack = 7`, `defense = 9`) |
| nested struct member | pass |
| `List<T>` / `T[]` / `Dictionary<string,T>` | pass (including TOML `[[table-array]]` → `List<T>`) |
| enums | **permissive**: `"quad"`, `"Quad"`, and integer `2` all bind to `Triangles`. Q-33's camelCase/case-sensitive/reject-integers rule must be enforced by a converter (planned) or the DOM binder. |
| nullable / absent key | pass (absent → default/null) |
| numeric range | pass (`byte` = 300 → `TOML integer value 300 is out of range`, span `(1,9)-(1,11)`); `int` from float rejected (`Expected Integer token but was Float`) |
| float from integer | pass |
| duplicate keys | pass (`TomlException` at the value span) |
| `Guid` | pass |
| `DateTime` with `Z` | pass, `Kind = Utc` |
| `DateTime` without offset | **accepted** as `DateTimeKind.Unspecified`; Q-05's “local date-time is an error” needs a converter/DOM check |
| `DateTimeOffset` with offset | pass |
| unknown keys | silently ignored |

`TomlException.Line`/`Column` are 1-based; `Span` is a `TomlSourceSpan` with `Start`/`End`.

### 4.3 Converters

`TomlSerializerOptions.Converters` is a settable `IReadOnlyList<TomlConverter>`; a converter is
either `TomlConverter<T>` (`Read(TomlReader)`, `Write(TomlWriter,T)`) or a `TomlConverterFactory`
(`CanConvert(Type)`, `CreateConverter(Type, TomlSerializerOptions)`). Verified:

* **Reflection path**: `Color`, `Vector2`, `AssetRef<TextureDescriptor>` (via factory), and
  `SpriteTopology` (via factory) all bind and serialize in the required forms
  (`tint = [1.0, 0.5, 0.25, 1.0]`, `pivot = [0.5, 0.5]`, `texture = "textures/player"`,
  `topology = "quad"`).
* **Source-gen path**: only **closed concrete** converter types in
  `[TomlSourceGenerationOptions(Converters = [typeof(...)])]` are honored. A `TomlConverterFactory`
  in that list compiles but is **ignored** (the generic `AssetRef<TextureDescriptor>` stayed a
  table). Every generic/enum converter would therefore have to be emitted closed (per referenced
  asset type, per game enum), and unknown-key/span gaps remain.
* **Array-valued converter token contract** (needed for `Color`/`Vector2`): at entry the reader is
  positioned on `StartArray`; the working sequence is
  `Read(); Get(); Read(); Get(); …; Read(); Read();`
  i.e. one `Read` per element and two final `Read`s to leave the array and position on the next
  property. `TomlReader` exposes `TokenType`, `Line`, `Column`, `CurrentSpan`, `PropertyName`,
  `GetRawText`, `Skip`, and `CreateException(string)`, so converters can produce precise spans.

### 4.4 Source spans — the decisive finding

* `ITomlMetadataStore` / `TomlMetadataStore` + `TomlPropertyMetadata.Span` capture **key** spans, not
  value spans. Example for `[[items]] id = "apple"`: `span=(3,1)-(3,2)` (the `id` key), while the
  value starts later.
* With a **source-gen context** (`TypeInfoResolver = context`) the store captured **no properties**
  at all (`TryGetProperties` returned nothing) — source-gen has no span path.
* **Value spans are available from the syntax tree.** `SyntaxParser.Parse(text, sourceName)` returns
  a `DocumentSyntax`; every `SyntaxNode` (and `SyntaxToken`) has a public field
  `Span` (`Tomlyn.Syntax.SourceSpan`) with `Start`/`End` `TextPosition` (`Line`, `Column`, `Offset`,
  all **0-based**). `KeyValueSyntax.Value.Span` is the value span.
* A path index (`member path → value span`) was built and proven for: root keys, `[table]` members,
  `[[table-array]]` elements, inline tables, and arrays of inline tables. Observed:

```text
span[name]              = (1,8)   text='"player"'
span[texture]           = (2,11)  text='"textures/player"'
span[meta.icon]         = (11,8)  text='"data/icon"'
span[items[0].icon]     = (13,8)  text='"data/apple"'
span[items[1].icon]     = (16,8)  text='"data/pear"'
span[inline[0].icon]    = (8,20)  text='"data/inline0"'
span[inline[1].icon]    = (8,59)  text='"data/inline1"'
```

The generated host then produced reference sites with exact locations:

```text
reference texture       -> textures/player @ Assets/data/player.toml(2,11)
reference meta.icon     -> data/icon       @ Assets/data/player.toml(11,8)
reference items[0].icon -> data/apple       @ Assets/data/player.toml(13,8)
reference items[1].icon -> data/pear        @ Assets/data/player.toml(16,8)
```

Limitations to carry into WP2: keys are read via `KeySyntax.ToString()` in the spike, so **dotted
and quoted keys need segment-aware path building** (walk `KeySyntax.Key`/`DotKeys`) rather than a
`Split('.')`; `SourceSpan` end positions are inclusive (length = `End.Offset - Start.Offset + 1`);
`TextPosition` is 0-based and diagnostics are 1-based.

### 4.5 Q-06/Q-07 decision

**Q-07 = generated DOM binder.** The source-gen path fails two hard requirements: unknown-key
errors (§7.3, Q-05 approved default) and value spans for reference diagnostics (§9, §16.3). Its
converter support is also limited to closed converter types, which would force generating closed
converters for every `AssetRef<T>`/enum. The binder reads `TomlTable` (value conversion, unknown
keys, required, ranges, enums, dates, converters as plain generated code) and resolves value spans
from the syntax pass. This is the §7.2 option-2 fallback the spec explicitly authorizes; it does
**not** change D-M4-04's authoring contract — the same `[JsonPropertyName]`/`[JsonIgnore]`/
`[JsonRequired]` attributes remain the authoring surface, but the generated binder implements their
semantics (reading the attributes from `Project.Common` metadata at generation time) instead of
Tomlyn's POCO mapper. WP6 must adjust the “uses Tomlyn's POCO mapping” wording in
`assets-and-tooling.md` §10 to describe the binder.

**Q-06 = syntax-tree value-span pass.** `MetadataStore` is not usable (key spans only, and empty with
source-gen). The binder joins `TomlSerializer.Deserialize<TomlTable>(toml)` with the
`SyntaxParser.Parse(toml)` index by member path. The mapper is proven for nested tables, table
arrays, inline tables, and arrays of inline tables.

**Alternatives considered and rejected:** (a) source-gen context + separate unknown-key/span passes —
three components over two structures, closed-converter generation, and Tomlyn still cannot report
unknown keys, so the validator has to duplicate the member schema anyway; (b) reflection POCO
binding + DOM validation — same duplication plus a reflection dependency the spec's tooling boundary
does not forbid but does not need.

## 5. C — Generated host, assembly loading, and type identity

### 5.1 What was built

A scratch `Project.Common`-like library (`Project.Common`), a scratch tooling contract library
(`Bork2d.Bake`, internal `IBakeManifest`/`BakeTypeRegistration`/`BakeReferenceSite`/`BakeAssetKind`
with `InternalsVisibleTo` to `Bork2d.BakeHost`, `Bork2d.Bake.Cli`, `Bork2d.Tests`), a hand-written
“generated” host project (`AssemblyName=Bork2d.BakeHost`, `BakeManifest.g.cs`) implementing the
§5.3 contract, a CLI (`AssemblyName=Bork2d.Bake.Cli`) that loads the host, and a separate
read-back app (`AssemblyName=Bork2d.Tests`) that opens the pack with `Bork2d.Assets` internals.

Host manifest shape actually used:

```csharp
[assembly: BakeManifest(typeof(BakeManifest))]   // public attribute in Bork2d.Bake

internal sealed class BakeManifest : IBakeManifest
{
    public IReadOnlyList<BakeTypeRegistration> Types { get; } = [...];
    public object Bind(string typeName, string toml, out IReadOnlyList<BakeReferenceSite> references);
    public void AddToPack(PackBuilder builder, string typeName, string assetPath, object value)
        => builder.AddMessagePack(assetPath, (T)value);   // generated per type; no member reflection
}
```

Discovery used the assembly attribute and `Activator.CreateInstance(attribute.ManifestType,
nonPublic: true)`. `[assembly: ...]` + internal type + IVT works across the ALC boundary.

### 5.2 ALC mechanism (Q-08)

```csharp
internal sealed class HostLoadContext(string hostDir)
    : AssemblyLoadContext("Bork2d.BakeHost", isCollectible: false)
{
    private static readonly string[] Shared = ["Bork2d.", "Tomlyn", "PolyType", "Nerdbank.", "System.", "Microsoft."];

    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name is "Bork2d.BakeHost" or "Project.Common")
            return LoadFromAssemblyPath(Path.Combine(hostDir, name.Name + ".dll"));

        if (Shared.Any(p => name.Name!.StartsWith(p, StringComparison.Ordinal)))
            return Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name)
                ?? Default.LoadFromAssemblyName(name);

        return null;
    }
}
```

**Requirement:** every shared dependency (`Tomlyn`, `PolyType`, `Nerdbank.MessagePack`, …) must be
in the CLI's own dependency graph so `Default.LoadFromAssemblyName` resolves it; otherwise the
resolver would have to load it into the default context from the host directory (possible, but it
must be the *same* instance the host binds to). In the real solution the CLI references
`Bork2d.Bake`, which references Tomlyn, so the dependency graph already contains it.

### 5.3 Observed results

```text
ALC name=Bork2d.BakeHost collectible=False
host assembly=Bork2d.BakeHost alc=Bork2d.BakeHost
manifest type=Bork2d.BakeHost.BakeManifest alc=Bork2d.BakeHost
manifest interface identity=True
bound config type=Project.Common.GameConfig assembly=Project.Common alc=Bork2d.BakeHost
bound boss type=Project.Common.EnemyStats value=EnemyStats { Attack = 7, Defense = 9 }
Texture property type == default AssetRef<TextureDescriptor>: True   (context=Default)
Tint property type == default Color: True                            (context=Default)
Boss property type: Project.Common.EnemyStats alc=Bork2d.BakeHost
PackBuilder context=Default
manifest interface context=Default
pack written: …\assets.pck bytes=365
```

Read-back through `Bork2d.Assets` internals (the same assembly the runtime uses):

```text
archive …assets.pck: entries=4
  entry data/boss          raw=False compressed=False size=18
  entry data/config        raw=False compressed=True  size=150
  entry textures/player    raw=False compressed=False size=83
  entry textures/raw/player raw=True  compressed=False size=16
config: name=campaign maxPlayers=4 boss=EnemyStats { Attack = 10, Defense = 5 } tint=Color { … } texture=textures/player wave=EnemyStats { Attack = 1, Defense = 1 };EnemyStats { Attack = 2, Defense = 2 }
struct root: EnemyStats { Attack = 7, Defense = 9 }
nil struct root: Decode fault (Asset 'data/boss' is a MessagePack nil payload and cannot be decoded as Project.Common.EnemyStats.)
```

This proves `PackBuilder`, `IBakeManifest`, `AssetRef<T>`, `TextureDescriptor`, `Color`, and PolyType
identity are shared, that the host's generated `AddToPack` produces entries the loader reads back,
and that the mapped types load in the host ALC while their API-typed members stay default-context
types. Project.Common was deliberately **not** referenced by the CLI, matching §5.3 (“loads the host
and `Project.Common` from the host directory”); the mapped-type *dependencies* are shared, the
mapped types themselves are host-private.

### 5.4 Blocker found: `AssetReferenceCollector.VisitSurrogate`

`PackBuilder.AddMessagePack<T>` runs `AssetReferenceCollector` (a `TypeShapeVisitor`) over the
value. The visitor implements `VisitObject`, `VisitProperty`, `VisitOptional`, `VisitEnumerable`,
and `VisitDictionary` — but **not** `VisitSurrogate`. Any type whose graph contains a
marshaler-mapped member (exactly the D-M4-12 `Color`) therefore fails:

```text
PackBuilder.AddMessagePack<ColorProbe> failed:
Failed to serialize MessagePack asset 'data/probe': The visitor method
AssetReferenceCollector.VisitSurrogate has not been implemented.
```

`AssetRef<T>`/`DataRef` do not hit this because `VisitValue` short-circuits on `IAssetReference`.
The fix is internal and small (no public API change): override
`VisitSurrogate<T, TSurrogate>` in `Bork2d.Assets.Pack.AssetReferenceCollector`, marshal the value
with `shape.Marshaler.Marshal(value)`, and visit `shape.SurrogateType` so references hidden inside a
surrogate are still found:

```csharp
public override object? VisitSurrogate<T, TSurrogate>(
    ISurrogateTypeShape<T, TSurrogate> shape, object? state)
{
    if (state is not T value) return null;
    TSurrogate surrogate = shape.Marshaler.Marshal(value);
    VisitValue(shape.SurrogateType, surrogate);
    return null;
}
```

The AOT/C spikes used real `Color` (no marshaler) for packed assets and proved the marshaler
separately (`ColorProbe`) until this fix lands. See §9 for the owner/orchestrator action.

## 6. D — NativeAOT proof

Command (from the scratch app directory):

```powershell
dotnet publish -c Release -r win-x64 --nologo
```

Result: `0 warnings`, `0 errors`; the publish output is the single native executable plus PDBs.
Tomlyn, `PolyType`, and Nerdbank.MessagePack are linked into that binary; StbImageSharp is not in the
AOT reference graph at all. The AOT app references only `Bork2d.Api`, `Bork2d.Assets`,
`Bork2d.Common`, and the fixture `Project.Common`.
The executable was run directly and exited 0:

```text
pack built: …\bork2d-wp0-aot-….pck
pack entries: 4
class root loaded: aot/2/EnemyStats { Attack = 10, Defense = 5 }/Color { R = 1, G = 0.5, B = 0.25, A = 1 }/textures/player
struct root loaded: EnemyStats { Attack = 7, Defense = 9 }
texture loaded: 2x2/textures/raw/player
nil struct root: Decode fault (MessagePack nil payload for a value-type root.)
Color marshaler: bytes=82A454696E7494CA3F800000CA00000000CA00000000CA3F800000A6436F6C6F72739194CA… roundtrip=…
Bork2d WP0 NativeAOT spike passed.
EXIT=0
```

What this proves: generated shapes compile and run under ILC for a class root, a struct root
(primary constructor), nested struct members, a `List<struct>`, `AssetRef<TextureDescriptor>`,
`TextureDescriptor`, and the surrogate-based `Color` marshaler (in-memory round-trip). The
packed class root used the un-marshaled `Color` shape because of §5.4; once the collector fix lands,
the same pack path will carry the marshaled form without any AOT change (the marshaler is a
generated shape, not reflection).

## 7. E — Incremental hash primitive (Q-09 XXH3 override)

`System.IO.Hashing` `11.0.0-rc.1.26425.128` provides `XxHash3` and `XxHash128` on `net11.0`.

```csharp
ulong h64 = XxHash3.HashToUInt64(readOnlySpan);          // seeded overload: (bytes, seed)
byte[] h128 = XxHash128.Hash(readOnlySpan);              // 16 bytes; seeded overload available

var incremental = new XxHash128();
incremental.Append(descriptorBytes);
incremental.Append([0]);                                  // unambiguous field separator
incremental.Append(fileHashBytes);
byte[] digest = incremental.GetHashAndReset();
```

Observed (same input, two processes — identical):

```text
XxHash3: B3EDC4B06ADBA991 stable=True
XxHash128: 9CB6D1E34AFC3DAB0629DCAD9F7F4235 length=16 stable=True
XxHash128 incremental: 76FBD8EF7CBDAAB7A50904DF2D83CA2E stable=True
FP1=B3EDC4B06ADBA991 FP2=9CB6D1E34AFC3DAB0629DCAD9F7F4235   (same in a second process)
```

**Recommendation:** use `XxHash128` (16 bytes, hex via `Convert.ToHexString`) for the project/
host manifest digest with the incremental `Append`/`GetHashAndReset` API; use `XxHash3` for any
single-file convenience hash. Field framing must be explicit (fixed-width lengths or a separator
that cannot occur in the field) and paths must be normalized and sorted before appending so the
digest is independent of filesystem order. The manifest records the hex strings; comparison is
ordinal.

## 8. Required repository gates (run in the workspace, after the spike builds)

| Command | Result |
| --- | --- |
| `dotnet build src/Bork2d/Bork2d.slnx -c Debug` | **passed**: 0 warnings, 0 errors, 2.4 s |
| `dotnet build src/Bork2d/Bork2d.slnx -c Release` | **passed**: 0 warnings, 0 errors, 1.4 s |
| `dotnet test src/Bork2d/Bork2d.slnx -c Debug --no-build` | **passed**: Failed 0, Passed 519, Skipped 9, Total 528, 10 s |
| `dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore` | **passed**: exit 0, no output |
| `pwsh scripts/check-docs.ps1` | **passed**: “Documentation link check passed: 23 files, 341 local links.” (23 includes this report; 22 before it was added) |

No repository source file changed: `git status --short` showed only `.spike/` (deleted before the
commit) and this report. The scratch host build, ALC load, and AOT publish/run evidence is in §5–§6.
All five gates were re-run on the final tree after adding this report (the docs check reports 23
files because this report is one of them).

## 9. F — Risks, pitfalls, and exact blockers

| # | Finding | Impact | Action / recommendation |
| --- | --- | --- | --- |
| F1 | `AssetReferenceCollector.VisitSurrogate` is not implemented (exact error in §5.4) | **Blocking for the approved D-M4-12 `Color` + `PackBuilder` combination**: any baked type containing a `Color` member cannot be added to a pack. | Orchestrator to schedule the internal fix in **WP1** (it lands with the `Color` marshaler; WP1 also owns `Api`) with a regression test `PackBuilder.AddMessagePack<TypeWithColor>`. No public API change; do not “treat surrogate as scalar” (a marshaler could hide references). |
| F2 | Struct `nil` decodes to `default` silently; `DeserializeObject` does not help | A silent default would violate D-M4-11/§16.1. | WP1 implements the first-token `0xC0` check in `AssetDeserializer` (one-byte replay stream). Add the acceptance test from §16.1. |
| F3 | Tomlyn has no unknown-key error mode and no `[JsonUnmappedMemberHandling]` | The approved Q-05 default cannot be met by the Tomlyn POCO path. | Q-07 = generated DOM binder; implement unknown-key errors per object/table in the binder. |
| F4 | `MetadataStore` spans are key spans; source-gen captures none | File/line/column for reference values would be wrong/absent with the POCO path. | Q-06 = syntax-tree span pass (proven). WP2 must build paths segment-aware (dotted/quoted keys) and convert 0-based positions. |
| F5 | Source-gen ignores `TomlConverterFactory` in `Converters`; only closed converter types work | Every generic/enum converter would need a generated closed type. | Not chosen (DOM binder); if WP2 ever revisits source-gen, emit closed converters and keep the separate unknown-key/span passes. |
| F6 | Tomlyn enums are permissive (case-insensitive names, integers accepted) | Q-33's approved default is not Tomlyn's native behavior. | Binder's enum converter must map camelCase exactly and reject integers; test `"Quad"`, `"quad"`, `0`, `2`. |
| F7 | Tomlyn accepts a local date-time as `DateTimeKind.Unspecified` | Q-05's approved “local date-time is an error” is not native. | Binder rejects `TomlDateTime.Kind` in `{LocalDateTime, LocalDate, LocalTime}` and converts offset values with `DateTime.UtcDateTime`. |
| F8 | Reflection POCO path does not enforce C# `required` (only `[JsonRequired]`/`[TomlRequired]`) | If the POCO path were used, `required` members could be missing silently. | DOM binder treats `required` + `[JsonRequired]`/`[TomlRequired]` as required (read from metadata). No contract change. |
| F9 | ALC resolver needs every shared dependency resolvable from the default context | A missing shared dependency would load a duplicate into the host ALC and break type identity. | Keep the CLI→Bake→Tomlyn/PolyType/Nerdbank dependency chain; fail fast with a clear BK1xxx diagnostic if `Default.LoadFromAssemblyName` fails; add the type-identity assertion to WP2 tests. |
| F10 | Q-04: a mapped type outside `Project.Common` and `internal` | The generated host needs compile-time access; `internal` requires `InternalsVisibleTo("Bork2d.BakeHost")` in the owning assembly, and the host csproj must reference that assembly. | WP1/WP2: require `public` or a documented IVT grant; descriptor validation reports the exact reason. Ambiguity detection (same full name in `Project.Common`, a referenced assembly, and `Bork2d.Api`) must name all candidates. |
| F11 | `partial` + `[GenerateShape]` is a hard authoring rule (CS0260) | Developer friction only. | Document in §7.1/API guide (WP6); already an accepted D-M4-12 risk. |
| F12 | Array-valued TomlConverter token protocol is non-obvious (`Read/Get` ×n + `Read/Read`) | Only relevant if the source-gen/POCO path is ever used. | Recorded for completeness; not used by the DOM binder. |
| F13 | Dotted/quoted TOML keys and `SourceSpan` end semantics | Path mapping in WP2 could mis-key spans. | Build path segments from `KeySyntax` children; treat end offsets as inclusive; add tests with `[a.b]`, quoted keys, and nested inline tables. |
| F14 | AOT publish requires the fixture/mapped types to be in the AOT project's reference graph | A missing `Project.Common` reference would drop shapes. | WP6's AOT proof references the fixture library (Q-16) and asserts a missing shape is a compile-time error. |

## 10. Unresolved issues and required owner/orchestrator decisions

1. **F1 ownership/scheduling** — the `VisitSurrogate` fix is internal and does not need an owner
   decision on the contract, but it must be assigned (WP1 recommended, because WP1 lands the
   `Color` marshaler and its tests). Until it lands, “Color member in a baked pack” is not
   demonstrable end to end; the AOT/C spikes proved the parts separately. If the owner prefers not
   to touch `AssetReferenceCollector` in WP1, the alternative is to defer `Color` members in packed
   data to WP4, which would leave §16.1's “class root containing `Color`” unproven at the WP1 gate.
2. **D-M4-04 mechanism wording** — the authoring contract (STJ attributes) is unchanged and works
   natively, but Q-07's chosen DOM binder implements the semantics instead of Tomlyn's POCO mapper.
   This is the §7.2 fallback, not a contract change; WP6 must update the “tooling uses Tomlyn's POCO
   mapping” sentence and the `assets-and-tooling.md` §10 text to say the generated binder implements
   the same attributes. No owner approval is required under §7.2, but the orchestrator should record
   it in the milestone PR.
3. **Q-30 / Q-04 visibility** — `internal` mapped types need a fixed-name IVT grant to
   `Bork2d.BakeHost` (and to the owning third-party assembly for Q-04). This is a documented
   authoring requirement, not a spike blocker; WP1’s descriptor validation should report it.
4. **Nullability of the relaxed `AssetRef` marshaler** — the production `AssetRef<TAsset>` and
   `AssetRefMarshaler<TAsset>` still carry `where TAsset : class`; WP1's edit must also relax both
   and add the struct-reference test (§3.5).

## 11. Recommendations for WP1–WP3

* **WP1**
  * Apply `[TypeShape(Marshaler = typeof(ColorMarshaler))]` to the real `Color` and add the public
    `ColorMarshaler : IMarshaler<Color, float[]>` with the nullable `Unmarshal(float[]?)`
    signature exactly as in §3.4.
  * Relax `IAssets.LoadAsset<T>` (both), `AssetRef<T>`, `AssetRefMarshaler<T>` to
    `where T : IShapeable<T>`; add the `nil` first-token check in `AssetDeserializer` (F2).
  * Fix `AssetReferenceCollector.VisitSurrogate` (F1) and add the pack-time regression test.
  * Descriptor/type resolution (Q-04): resolve full names in `Project.Common`, then its referenced
    assemblies, then `Bork2d.Api`; validate `IShapeable<T>`; report ambiguity with every candidate
    assembly; require `public` or the fixed-name IVT for cross-assembly internal types.
  * Keep the `Bake`/`Bake.Cli` tooling boundary and the `System.IO.Hashing` `XxHash128` pin.
* **WP2**
  * Implement the generated host as the §5.3 shape: `BakeManifest.g.cs` with the static manifest,
    per-type binders, and per-type `AddToPack` delegates; internal contract + `InternalsVisibleTo`
    to `Bork2d.BakeHost` and `Bork2d.Bake.Cli`; fixed assembly name `Bork2d.BakeHost`.
  * Implement the DOM binder (Q-07): `TomlTable` values + `DocumentSyntax` path→value-span index
    (Q-06), segment-aware keys, `required`/`[JsonRequired]`/`[TomlRequired]`, `[JsonPropertyName]`/
    `[JsonIgnore]`, unknown-key errors, camelCase enum converter (F6), date-time offset rule (F7),
    numeric range checks, and reference sites with exact spans.
  * Implement the ALC loader per §5.2 with explicit type-identity assertions; fail fast with a
    BK1xxx diagnostic naming the unresolvable shared assembly.
  * Emit the `BakeHost.csproj` that references `Project.Common`, `Bork2d.Api`, `Bork2d.Assets`,
    `Bork2d.Bake` (and Tomlyn transitively) and builds with the same configuration.
* **WP3**
  * Keep the texture pipeline separate from custom data; the host/DOM binder pieces do not depend
    on it. Reuse the same canonical path rules and the same diagnostics model.
  * When the fixture project (Q-16) lands, include a `Color` member and a struct root in its data
    so the F1/F2 regression paths are exercised by the normal test and AOT gates.

## 12. Reproduction notes

The spike directories were `.spike/{A1,A2,A3,A4,B1,B2,B3,B4,B4b,C,D,E,F}` and were deleted before
the commit. Each experiment is a standalone `net11.0` console/library project with the package pins
from §2 and project references to the repository `Bork2d.Api` / `Bork2d.Assets` / `Bork2d.Common`
as needed. The key commands were:

```powershell
dotnet run -c Debug                       # A1/A2/A3/A4/B1/B2/B3/B4/B4b/F
dotnet build .spike/C/Host/Bork2d.BakeHost.csproj -c Debug
dotnet run --project .spike/C/Cli -c Debug -- .spike/C/Host/bin/Debug/net11.0 .spike/out/assets.pck
dotnet run --project .spike/C/ReadBack -c Debug -- .spike/out/assets.pck
dotnet publish .spike/D -c Release -r win-x64 --nologo
.spike/D/bin/Release/net11.0/win-x64/publish/Bork2d.Tests.exe
dotnet run --project .spike/E -c Debug
```

The AOT publish produced no warnings and the executable exited 0; the CLI host-load and the
read-back both exited 0. Full gate results are in §8.
