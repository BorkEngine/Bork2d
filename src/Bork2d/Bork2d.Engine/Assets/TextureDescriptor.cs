using PolyType;

namespace Bork2d.Engine.Assets;

/// <summary>
/// A MessagePack texture descriptor: a raw RGBA8 payload reference plus its dimensions and sampler
/// state.
/// </summary>
/// <remarks>
/// <para>
/// The payload is raw RGBA8 bytes, <c>Width × Height × 4</c> long, row-major, top-left origin,
/// straight alpha, treated as sRGB. This type only describes the payload; it does not read,
/// decompress, or upload it.
/// </para>
/// <para>
/// The fields are non-null enums, not strings: <see cref="Format"/> selects the pixel format
/// (<see cref="TextureFormat.Rgba8"/> is the only v1 format), and <see cref="Filter"/>/<see cref="Wrap"/>
/// are sampler state. The defaults are the builder and processor defaults
/// (<see cref="TextureFormat.Rgba8"/>, <see cref="TextureFilter.Linear"/>, and
/// <see cref="TextureWrap.Clamp"/>).
/// </para>
/// <para>
/// Load with <see cref="IAssets.LoadAsset{T}(string)"/> using the canonical texture path. The
/// loader deserializes the packed asset as-is; it does not validate the fields or resolve
/// <see cref="Payload"/>. Pack creation is responsible for reference integrity, and the bake
/// tooling is responsible for field validation.
/// </para>
/// </remarks>
[GenerateShape]
public sealed partial class TextureDescriptor
{
    /// <summary>
    /// Gets or sets the referenced raw RGBA8 payload, or an absent reference for no payload.
    /// </summary>
    public DataRef Payload { get; set; }

    /// <summary>
    /// Gets or sets the pixel width.
    /// </summary>
    public uint Width { get; set; }

    /// <summary>
    /// Gets or sets the pixel height.
    /// </summary>
    public uint Height { get; set; }

    /// <summary>
    /// Gets or sets the pixel format. <see cref="TextureFormat.Rgba8"/> is the only v1 format and the
    /// default.
    /// </summary>
    public TextureFormat Format { get; set; } = TextureFormat.Rgba8;

    /// <summary>
    /// Gets or sets the sampler filter. This is sampler state, not decoded pixel data; the default is
    /// <see cref="TextureFilter.Linear"/>.
    /// </summary>
    public TextureFilter Filter { get; set; } = TextureFilter.Linear;

    /// <summary>
    /// Gets or sets the sampler wrap mode. This is sampler state, not decoded pixel data; the default
    /// is <see cref="TextureWrap.Clamp"/>.
    /// </summary>
    public TextureWrap Wrap { get; set; } = TextureWrap.Clamp;
}
