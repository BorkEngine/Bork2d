using PolyType;

namespace Bork2d.Assets.Descriptors;

/// <summary>
/// The internal MessagePack texture descriptor asset: a raw RGBA8 payload path plus its dimensions
/// and sampler state.
/// </summary>
/// <remarks>
/// <para>
/// Descriptors are internal generated-shape classes and use Nerdbank's property-name keys (not
/// <c>[Key]</c>); there is no per-asset version. The payload is raw RGBA8 bytes,
/// <c>Width × Height × 4</c> long, row-major, top-left origin, straight alpha, treated as sRGB. M3
/// validates the payload entry's metadata but never decompresses it; upload is M8.
/// </para>
/// <para>
/// Properties are nullable so a malformed payload is reported as invalid data by
/// <see cref="AssetValidation"/> rather than as a deserialization failure.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class TextureDescriptor
{
    /// <summary>Gets or sets the raw asset path containing the pixels.</summary>
    public string? Payload { get; set; }

    /// <summary>Gets or sets the pixel width, which must be positive.</summary>
    public uint Width { get; set; }

    /// <summary>Gets or sets the pixel height, which must be positive.</summary>
    public uint Height { get; set; }

    /// <summary>Gets or sets the pixel format; only <c>rgba8</c> is supported in v1.</summary>
    public string? Format { get; set; }

    /// <summary>Gets or sets the sampler filter: <c>nearest</c> or <c>linear</c>.</summary>
    public string? Filter { get; set; }

    /// <summary>Gets or sets the sampler wrap mode: <c>clamp</c> or <c>repeat</c>.</summary>
    public string? Wrap { get; set; }
}
