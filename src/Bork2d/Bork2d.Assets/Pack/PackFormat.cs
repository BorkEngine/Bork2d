namespace Bork2d.Assets.Pack;

/// <summary>
/// Fixed v1 pack prefix layout and version.
/// </summary>
internal static class PackFormat
{
    /// <summary>The fixed 28-byte prefix length.</summary>
    internal const int PrefixLength = 28;

    /// <summary>The only supported pack format version.</summary>
    internal const uint CurrentVersion = 1;

    /// <summary>The ASCII pack magic, <c>BORK2DPK</c>.</summary>
    internal static ReadOnlySpan<byte> Magic => "BORK2DPK"u8;
}
