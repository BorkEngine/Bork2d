namespace Bork2d.Bake.Textures;

/// <summary>
/// One Khronos Data Format Descriptor sample: the bit layout and numeric range of one channel.
/// </summary>
/// <remarks>
/// Sample positions are always zero for the formats this bake writes, so they are not stored. The
/// channel type is the low nibble of the DFD channel byte; the high nibble (qualifiers) is always
/// zero.
/// </remarks>
/// <param name="BitOffset">The sample's bit offset inside the texel block.</param>
/// <param name="BitLength">The sample's bit length.</param>
/// <param name="ChannelType">The Khronos channel id inside the color model.</param>
/// <param name="Lower">The sample's lower bound.</param>
/// <param name="Upper">The sample's upper bound.</param>
internal readonly record struct DfdSample(
    ushort BitOffset,
    byte BitLength,
    byte ChannelType,
    uint Lower,
    uint Upper);
