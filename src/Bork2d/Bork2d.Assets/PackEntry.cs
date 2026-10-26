using Nerdbank.MessagePack;

namespace Bork2d.Assets;

/// <summary>
/// One v1 pack entry as stored in the MessagePack header.
/// </summary>
/// <remarks>
/// The shape for this type is generated transitively by <see cref="PackHeader"/>'s
/// <c>[GenerateShape]</c>; only the top-level header type carries the annotation.
/// </remarks>
internal sealed partial class PackEntry
{
    /// <summary>Gets the canonical asset path.</summary>
    [Key(0)]
    public required string Path { get; init; }

    /// <summary>Gets a value indicating whether the payload is opaque raw bytes.</summary>
    [Key(1)]
    public bool IsRaw { get; init; }

    /// <summary>Gets a value indicating whether the stored payload is Zstd-compressed.</summary>
    [Key(2)]
    public bool IsCompressed { get; init; }

    /// <summary>Gets the absolute byte offset of the stored payload.</summary>
    [Key(3)]
    public ulong Offset { get; init; }

    /// <summary>Gets the stored payload byte length.</summary>
    [Key(4)]
    public uint Length { get; init; }

    /// <summary>Gets the payload byte length after decompression.</summary>
    [Key(5)]
    public uint Size { get; init; }
}
