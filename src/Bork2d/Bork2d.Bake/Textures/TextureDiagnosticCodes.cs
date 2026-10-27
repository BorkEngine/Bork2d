namespace Bork2d.Bake.Textures;

/// <summary>
/// The stable <c>BK4xxx</c> diagnostic codes for texture decoding, dimensions, and payload emission.
/// </summary>
/// <remarks>
/// <para>
/// This file owns only the texture decode/probe range. Sidecar parsing was retired with the
/// hand-written parser: sidecars are bound by the shared shape-driven TOML binder and report the
/// <c>BK2xxx</c> codes. Texture-pipeline discovery codes were retired with
/// <c>TexturePipeline</c>; discovery and output-path failures are the generic processor codes.
/// </para>
/// <para>
/// Codes are stable once a range is released. The published table belongs to the documentation work
/// package.
/// </para>
/// </remarks>
internal static class TextureDiagnosticCodes
{
    /// <summary>A texture source could not be read.</summary>
    internal const string TextureSourceRead = "BK4001";

    /// <summary>A texture source is corrupt, truncated, or unsupported by the decoder.</summary>
    internal const string TextureDecode = "BK4003";

    /// <summary>A PNG source declares a 16-bit bit depth, which M4 rejects (Q-12).</summary>
    internal const string TextureBitDepth = "BK4004";

    /// <summary>A texture source has zero, negative, overflowing, or oversized dimensions.</summary>
    internal const string TextureDimensions = "BK4005";

    /// <summary>A texture decode failed because the process ran out of memory (D-M3-06).</summary>
    internal const string TextureOutOfMemory = "BK4013";

    /// <summary>The decoded RGBA8 payload length is not <c>width × height × 4</c>.</summary>
    internal const string TexturePayloadSize = "BK4014";

    /// <summary>
    /// A source is shorter than its derivable byte layout (uncompressed BMP/TGA) or a JPEG stream
    /// does not end with its end-of-image marker.
    /// </summary>
    internal const string TextureTruncated = "BK4015";
}
