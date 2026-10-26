using PolyType;

namespace Bork2d.Api.Assets;

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
    /// Gets or sets the pixel format; <c>rgba8</c> is the runtime format in v1.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Gets or sets the sampler filter, for example <c>nearest</c> or <c>linear</c>.
    /// </summary>
    public string? Filter { get; set; }

    /// <summary>
    /// Gets or sets the sampler wrap mode, for example <c>clamp</c> or <c>repeat</c>.
    /// </summary>
    public string? Wrap { get; set; }
}
