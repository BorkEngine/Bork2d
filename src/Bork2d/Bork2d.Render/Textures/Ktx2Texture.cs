using Bork2d.Engine.Assets;

namespace Bork2d.Render.Textures;

/// <summary>
/// One parsed KTX2 level index entry: the level data's offset and stored byte length.
/// </summary>
/// <param name="ByteOffset">The level data's absolute offset in the payload.</param>
/// <param name="ByteLength">The stored byte length, without alignment padding.</param>
internal readonly record struct Ktx2Level(ulong ByteOffset, ulong ByteLength);

/// <summary>
/// The parsed narrow KTX2 profile of one texture page payload.
/// </summary>
/// <remarks>
/// Parsing happens once when a page's content is validated and again when its upload is recorded; it
/// never runs per frame. The instance is immutable and only carries the format, base dimensions, and
/// per-level offsets/lengths the upload needs.
/// </remarks>
internal sealed class Ktx2Texture
{
    /// <summary>Initializes a new instance of the <see cref="Ktx2Texture"/> class.</summary>
    /// <param name="format">The descriptor format the payload was validated against.</param>
    /// <param name="width">The base-level width in pixels.</param>
    /// <param name="height">The base-level height in pixels.</param>
    /// <param name="levels">The level index entries, base level first.</param>
    internal Ktx2Texture(
        TextureFormat format,
        uint width,
        uint height,
        Ktx2Level[] levels)
    {
        Format = format;
        Width = width;
        Height = height;
        Levels = levels;
    }

    /// <summary>Gets the descriptor format the payload was validated against.</summary>
    internal TextureFormat Format { get; }

    /// <summary>Gets the base-level pixel width.</summary>
    internal uint Width { get; }

    /// <summary>Gets the base-level pixel height.</summary>
    internal uint Height { get; }

    /// <summary>Gets the level index entries, base level first.</summary>
    internal IReadOnlyList<Ktx2Level> Levels { get; }

    /// <summary>Gets the number of mip levels.</summary>
    internal int LevelCount => Levels.Count;
}
