using System.Text.Json.Serialization;
using PolyType;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>A sidecar shape whose member name needs a quoted TOML key.</summary>
[GenerateShape]
public sealed partial class QuotedKeySidecar
{
    /// <summary>Gets or sets a value keyed by a name containing a space.</summary>
    [JsonPropertyName("display name")]
    public string? Text { get; set; }
}
