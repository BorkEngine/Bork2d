using System.Numerics;
using PolyType;

namespace Bork2d.Engine.Assets;

/// <summary>
/// A MessagePack sprite asset describing a textured quad.
/// </summary>
/// <remarks>
/// <para>
/// A sprite references a texture descriptor through <see cref="Texture"/> and carries four
/// normalized UVs, a per-axis world scale, a normalized pivot, a topology, and optional indices.
/// There is no vertices array; the renderer generates the quad corners from these values.
/// </para>
/// <para>
/// Assigning the asset to a sprite renderable freezes these fields once, and the renderer reads
/// only that frozen copy. Mutating the asset after assignment — including <see cref="Uvs"/>,
/// <see cref="Indices"/>, <see cref="Scale"/>, <see cref="Pivot"/>, <see cref="Topology"/>, or
/// <see cref="Texture"/> — does not change the drawn sprite or any published snapshot, and
/// re-assigning the same asset keeps the first frozen values. Load or create a new asset instance
/// to change the rendered result.
/// </para>
/// <para>
/// Load with <see cref="IAssets.LoadAsset{T}(string)"/> using the canonical sprite path. The
/// loader deserializes the packed asset as-is; it does not validate the fields or resolve
/// <see cref="Texture"/>. Pack creation is responsible for reference integrity, and the bake
/// tooling is responsible for field validation.
/// </para>
/// </remarks>
[GenerateShape]
public sealed partial class SpriteAsset
{
    /// <summary>
    /// Gets or sets the referenced texture descriptor, or an absent reference for no texture.
    /// </summary>
    public AssetRef<TextureDescriptor> Texture { get; set; }

    /// <summary>
    /// Gets or sets exactly four normalized UVs with a top-left origin, ordered top-left,
    /// top-right, bottom-right, bottom-left.
    /// </summary>
    public Vector2[]? Uvs { get; set; }

    /// <summary>
    /// Gets or sets the per-axis world scale in world units per normalized UV span.
    /// </summary>
    /// <remarks>
    /// A full UV quad's world size is <c>(maxU - minU) * Scale.X</c> by
    /// <c>(maxV - minV) * Scale.Y</c>; texture pixel dimensions never affect the geometry. Both
    /// components must be finite and greater than zero.
    /// </remarks>
    public Vector2 Scale { get; set; }

    /// <summary>
    /// Gets or sets the normalized pivot inside the quad; the default is the center.
    /// </summary>
    public Vector2 Pivot { get; set; } = new(0.5f, 0.5f);

    /// <summary>
    /// Gets or sets the topology stored as an integer in the pack.
    /// </summary>
    public SpriteTopology Topology { get; set; }

    /// <summary>
    /// Gets or sets the optional index buffer. It is <see langword="null"/> for
    /// <see cref="SpriteTopology.Quad"/> and required for the indexed topologies.
    /// </summary>
    public ushort[]? Indices { get; set; }
}
