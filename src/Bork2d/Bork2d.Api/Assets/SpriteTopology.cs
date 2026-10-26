namespace Bork2d.Api.Assets;

/// <summary>
/// Selects how a sprite asset's indices describe the quad.
/// </summary>
/// <remarks>
/// The value is stored as an integer in the pack. v1 draws <see cref="Quad"/> only; the indexed
/// topologies are preserved for the future indexed path.
/// </remarks>
public enum SpriteTopology
{
    /// <summary>A four-corner triangle strip with no index buffer.</summary>
    Quad = 0,

    /// <summary>An indexed triangle strip over the four quad corners.</summary>
    TriangleStrip = 1,

    /// <summary>An indexed triangle list over the four quad corners.</summary>
    Triangles = 2,
}
