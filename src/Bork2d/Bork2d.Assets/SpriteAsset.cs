using System.Numerics;
using PolyType;

namespace Bork2d.Assets;

/// <summary>
/// The internal MessagePack sprite asset: a texture descriptor reference, four normalized UVs, a
/// pixels-per-unit scale, a normalized pivot, a topology, and optional indices.
/// </summary>
/// <remarks>
/// <para>
/// Sprites are internal generated-shape classes and use Nerdbank's property-name keys (not
/// <c>[Key]</c>); there is no per-asset version and no vertices array. UVs use a top-left origin and
/// are ordered top-left, top-right, bottom-right, bottom-left. <see cref="Vector2"/> round-trips as
/// a map with <c>X</c>/<c>Y</c> through Nerdbank's generated shape.
/// </para>
/// <para>
/// Properties are nullable where a malformed payload could omit them, so
/// <see cref="AssetValidation"/> can report invalid data deterministically. <see cref="Pivot"/>
/// defaults to the normalized center.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class SpriteAsset
{
    /// <summary>Gets or sets the texture descriptor asset path.</summary>
    public string? Texture { get; set; }

    /// <summary>Gets or sets exactly four normalized UVs with a top-left origin.</summary>
    public Vector2[]? Uvs { get; set; }

    /// <summary>Gets or sets the scale in pixels per world unit; it must be positive.</summary>
    public float Scale { get; set; }

    /// <summary>Gets or sets the normalized pivot inside the quad; the default is the center.</summary>
    public Vector2 Pivot { get; set; } = new(0.5f, 0.5f);

    /// <summary>Gets or sets the topology stored as an integer in the pack.</summary>
    public SpriteTopology Topology { get; set; }

    /// <summary>
    /// Gets or sets the optional index buffer. It is <see langword="null"/> for
    /// <see cref="SpriteTopology.Quad"/> and required for the indexed topologies.
    /// </summary>
    public ushort[]? Indices { get; set; }
}
