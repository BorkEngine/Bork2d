using PolyType;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// A settings-only sidecar shape with string-array and string-list members, which the canonical
/// sidecar writer writes as TOML arrays, plus one nullable scalar to prove scalar behavior is
/// unchanged.
/// </summary>
[GenerateShape]
public sealed partial class StringArraySidecar
{
    /// <summary>Gets or sets an array of strings written as a TOML array.</summary>
    public string[]? Ranges { get; set; }

    /// <summary>Gets or sets a read-only string list written as a TOML array.</summary>
    public IReadOnlyList<string>? Glyphs { get; set; }

    /// <summary>Gets or sets an optional scalar setting.</summary>
    public int? Count { get; set; }
}
