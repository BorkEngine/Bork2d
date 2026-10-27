using System.ComponentModel;
using System.Numerics;
using System.Text.Json.Serialization;
using Bork2d.Api.Assets;
using Bork2d.Api.Rendering;
using PolyType;

namespace Bork2d.Tests.Bake.Toml;

/// <summary>Every supported §7.3 leaf in one class root.</summary>
[GenerateShape]
public sealed partial class LeafSet
{
    public bool Flag { get; set; }

    public sbyte I8 { get; set; }

    public byte U8 { get; set; }

    public short I16 { get; set; }

    public ushort U16 { get; set; }

    public int I32 { get; set; }

    public uint U32 { get; set; }

    public long I64 { get; set; }

    public ulong U64 { get; set; }

    public nint NativeInt { get; set; }

    public nuint NativeUInt { get; set; }

    public float F32 { get; set; }

    public double F64 { get; set; }

    [DefaultValue("initial")]
    public string Name { get; set; } = "initial";

    public string? OptionalName { get; set; }

    public Guid Id { get; set; }

    public DateTime Timestamp { get; set; }

    public DateTimeOffset Offset { get; set; }

    public Vector2 V2 { get; set; }

    public Vector3 V3 { get; set; }

    public Vector4 V4 { get; set; }

    public Color Tint { get; set; }

    public Mood Mood { get; set; }

    public AssetRef<TextureDescriptor> Texture { get; set; }

    public DataRef Payload { get; set; }

    public int? Optional { get; set; }

    public Color? OptionalTint { get; set; }

    public string[]? Tags { get; set; }

    public List<int>? Numbers { get; set; }

    public IReadOnlyList<string>? Names { get; set; }

    public Dictionary<string, int>? Counts { get; set; }
}

/// <summary>Nested classes, structs, arrays of tables, and dictionaries of structs.</summary>
[GenerateShape]
public sealed partial class NestedRoot
{
    public required string Name { get; set; }

    public Stats Stats { get; set; }

    public Stats[]? History { get; set; }

    public List<Stats>? Log { get; set; }

    public Dictionary<string, Stats>? Map { get; set; }

    public Stats? Maybe { get; set; }

    public Inner? Meta { get; set; }

    public Inner[]? Items { get; set; }
}

/// <summary>A nested class member.</summary>
[GenerateShape]
public sealed partial class Inner
{
    public string? Icon { get; set; }

    public int Weight { get; set; }

    public string? Bork2d { get; set; }
}

/// <summary>A readonly record struct with a primary constructor.</summary>
[GenerateShape]
public readonly partial record struct Stats(int Attack, int Defense);

/// <summary>A struct asset root that also carries initializers and a nested struct.</summary>
[GenerateShape]
public readonly partial record struct StructRoot(
    int Attack,
    string Name,
    Color Tint,
    Vector2 Pivot,
    Stats Nested,
    int[] Values)
{
    [DefaultValue(7)]
    public int Bonus { get; init; } = 7;
}

/// <summary>STJ attribute mapping: rename, ignore, required, and C# <c>required</c>.</summary>
[GenerateShape]
public sealed partial class AttributeSet
{
    [JsonPropertyName("custom_name")]
    public string? CustomName { get; set; }

    [JsonIgnore]
    [DefaultValue("default")]
    public string? Ignored { get; set; } = "default";

    [JsonRequired]
    public string? Must { get; set; }

    public required string Needed { get; set; }

    public string? Optional { get; set; }

    [DefaultValue(3)]
    public int Count { get; set; } = 3;
}

/// <summary>Members whose CLR types are not supported TOML leaves.</summary>
[GenerateShape]
public sealed partial class UnsupportedSet
{
    public object? Anything { get; set; }

    public char Letter { get; set; }

    public decimal Money { get; set; }

    public TimeSpan Span { get; set; }
}

/// <summary>A dictionary with a non-string key type.</summary>
[GenerateShape]
public sealed partial class IntDictionary
{
    public Dictionary<int, string>? ById { get; set; }
}

/// <summary>The §7.3 missing-value matrix for non-nullable reference members.</summary>
[GenerateShape]
public sealed partial class NonNullableSet
{
    [DefaultValue("initial")]
    public string Name { get; set; } = "initial";

    public string Missing { get; set; } = null!;

    public Inner Child { get; set; } = null!;

    public string? OptionalName { get; set; }

    public string[]? Tags { get; set; }

    public List<string> Items { get; set; } = null!;

    public Dictionary<string, int> Map { get; set; } = null!;

    public int Count { get; set; }
}

/// <summary>
/// A required member forces member-initializer constructor binding; absent non-nullable reference
/// members must still honor their initializers.
/// </summary>
[GenerateShape]
public sealed partial class NonNullableInitializerSet
{
    public required string Trigger { get; set; }

    [DefaultValue("initial")]
    public string Name { get; set; } = "initial";

    public string Missing { get; set; } = null!;

    public Inner Child { get; set; } = null!;

    public string? OptionalName { get; set; }

    public List<string> Items { get; set; } = null!;

    public int Count { get; set; }
}

/// <summary>A game enum whose names map to exact camelCase source values.</summary>
public enum Mood
{
    /// <summary>Neutral.</summary>
    Calm = 0,

    /// <summary>Engaged.</summary>
    BattleFury = 1,
}

/// <summary>Quoted keys and dotted TOML keys resolved through the segment-aware path index.</summary>
[GenerateShape]
public sealed partial class KeyForms
{
    [JsonPropertyName("a.b")]
    public int Quoted { get; set; }

    public Stats Nested { get; set; }
}

/// <summary>References at the root, in a nested table, an array, and a dictionary.</summary>
[GenerateShape]
public sealed partial class RefRoot
{
    public AssetRef<TextureDescriptor> Texture { get; set; }

    public RefNode? Meta { get; set; }

    public RefNode[]? Items { get; set; }

    public Dictionary<string, RefNode>? ByName { get; set; }
}

/// <summary>A nested reference holder.</summary>
[GenerateShape]
public sealed partial class RefNode
{
    public AssetRef<TextureDescriptor> Icon { get; set; }

    public DataRef Payload { get; set; }
}

/// <summary>
/// A root with a member whose key collides with the reserved <c>[bork2d]</c> metadata table. The
/// binder must ignore the reserved table, so this member never receives it.
/// </summary>
[GenerateShape]
public sealed partial class ReservedMetadataRoot
{
    public int Value { get; set; }

    public string? Bork2d { get; set; }
}
