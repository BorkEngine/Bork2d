using System.Text.Json.Serialization;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using PolyType;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// The class-root custom-data type used by the bake tests: required members, a nested struct, a
/// <see cref="Color"/>, a typed texture reference, and the BCL source-mapping attributes in one
/// asset.
/// </summary>
/// <remarks>
/// The type carries <c>[GenerateShape]</c> on a <c>partial</c> declaration (D-M4-12), so the bake
/// host can bind and pack it without relying on the removed example fixture assembly. It is a
/// stand-in for a game's <c>Project.Common</c> library and never references the bake tooling.
/// </remarks>
[GenerateShape]
public sealed partial class GameConfig
{
    /// <summary>Gets or sets the display name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the starting health.</summary>
    public int Health { get; set; }

    /// <summary>Gets or sets the nested combat stats.</summary>
    public Stats Stats { get; set; }

    /// <summary>Gets or sets the linear tint color.</summary>
    public Color Tint { get; set; }

    /// <summary>Gets or sets the referenced texture descriptor.</summary>
    public AssetRef<TextureDescriptor> Texture { get; set; }

    /// <summary>Gets or sets the player cap; the TOML key is renamed with <see cref="JsonPropertyNameAttribute"/>.</summary>
    [JsonPropertyName("max_players")]
    public int MaxPlayers { get; set; }

    /// <summary>Gets or sets a diagnostic label that is not part of the source mapping.</summary>
    [JsonIgnore]
    public string? DebugLabel { get; set; }

    /// <summary>Gets or sets a value the source must supply (<see cref="JsonRequiredAttribute"/>).</summary>
    [JsonRequired]
    public string? Must { get; set; }
}
