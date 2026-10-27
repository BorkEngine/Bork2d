using System.Text.Json.Serialization;
using PolyType;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// A settings-only sidecar shape with scalar leaves, a renamed key, an enum, and an ignored member.
/// </summary>
[GenerateShape]
public sealed partial class TestSidecar
{
    /// <summary>Gets or sets the required integer setting.</summary>
    public int Value { get; set; }

    /// <summary>Gets or sets the enum setting, written as a camelCase member name.</summary>
    public TestSidecarMode Mode { get; set; }

    /// <summary>Gets or sets an optional key renamed with <see cref="JsonPropertyNameAttribute"/>.</summary>
    [JsonPropertyName("display")]
    public string? Label { get; set; }

    /// <summary>Gets or sets a member that is never written or bound.</summary>
    [JsonIgnore]
    public string? Ignored { get; set; }
}
