using System.ComponentModel;
using PolyType;

namespace Bork2d.Engine.Assets;

/// <summary>
/// A MessagePack texture descriptor: a KTX2 payload reference plus its base dimensions, format, mip
/// count, and sampler state.
/// </summary>
/// <remarks>
/// <para>
/// The payload is a complete KTX2 2.0 file: one 2D image, one array layer, one face, no
/// supercompression, and a full mip chain down to 1x1. Its metadata must match this descriptor:
/// <see cref="Width"/> and <see cref="Height"/> are the base level's pixel dimensions,
/// <see cref="Format"/> selects the concrete linear Vulkan format, and <see cref="MipLevels"/> is the
/// number of levels in the chain. This type only describes the payload; it does not read, decode, or
/// upload it.
/// </para>
/// <para>
/// The fields are non-null enums, not strings, so a pack cannot carry a free-form format name. The
/// defaults are the builder and processor defaults (<see cref="TextureFormat.Rgba8"/>,
/// <see cref="TextureFilter.Linear"/>, <see cref="TextureWrap.Clamp"/>, and one mip level).
/// </para>
/// <para>
/// Load with <see cref="IAssets.LoadAsset{T}(string)"/> using the canonical texture path. The loader
/// deserializes the packed asset as-is; it does not validate the fields or resolve
/// <see cref="Payload"/>. Pack creation is responsible for reference integrity, and the bake tooling
/// is responsible for field validation.
/// </para>
/// </remarks>
[GenerateShape]
public sealed partial class TextureDescriptor
{
    /// <summary>
    /// Gets or sets the referenced KTX2 payload, or an absent reference for no payload.
    /// </summary>
    public DataRef Payload { get; set; }

    /// <summary>
    /// Gets or sets the base-level pixel width.
    /// </summary>
    public uint Width { get; set; }

    /// <summary>
    /// Gets or sets the base-level pixel height.
    /// </summary>
    public uint Height { get; set; }

    /// <summary>
    /// Gets or sets the concrete linear pixel format. The default is
    /// <see cref="TextureFormat.Rgba8"/>.
    /// </summary>
    public TextureFormat Format { get; set; } = TextureFormat.Rgba8;

    /// <summary>
    /// Gets or sets the number of mip levels in the KTX2 payload. The value is at least one, and the
    /// default is one.
    /// </summary>
    [DefaultValue(1u)]
    public uint MipLevels { get; set; } = 1;

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
