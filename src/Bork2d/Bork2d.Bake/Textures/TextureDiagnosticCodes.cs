namespace Bork2d.Bake.Textures;

/// <summary>
/// The stable <c>BK4xxx</c> texture decode/probe codes and <c>BK91xx</c> bake-side KTX2/BC codes.
/// </summary>
/// <remarks>
/// <para>
/// This file owns the texture decode/probe range and the bake-side KTX2/BC sub-range. Sidecar parsing
/// is owned by the shared shape-driven TOML binder and reports the <c>BK2xxx</c> codes; an invalid
/// <c>type</c> or <c>compression</c> value is therefore a <c>BK2006</c> before either bake code can
/// run. Discovery and output-path failures are the generic processor codes.
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

    /// <summary>A texture could not be baked: block encoding or KTX2 container writing failed.</summary>
    internal const string TextureBake = "BK9100";

    /// <summary>
    /// The sidecar's channel type and compression mode do not map to a texture format. The TOML
    /// binder rejects an invalid enum value first, so this guards a programmatic sidecar.
    /// </summary>
    internal const string TextureFormatMapping = "BK9101";
}
