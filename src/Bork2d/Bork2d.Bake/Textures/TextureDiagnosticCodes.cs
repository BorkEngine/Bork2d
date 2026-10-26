namespace Bork2d.Bake.Textures;

/// <summary>
/// The stable <c>BK4xxx</c> diagnostic codes for texture discovery, sidecars, decoding, dimensions,
/// and payload emission.
/// </summary>
/// <remarks>
/// <para>
/// This file owns only the texture range; the shared <c>BakeDiagnosticCodes</c> table is WP1's and
/// must not grow per work package. A sidecar TOML syntax error uses the shared
/// <see cref="Diagnostics.BakeDiagnosticCodes.TomlSyntax"/> code because it is a TOML parse failure,
/// not a texture rule.
/// </para>
/// <para>
/// Codes are stable once a range is released. The published table belongs to the M4 documentation
/// work package.
/// </para>
/// </remarks>
internal static class TextureDiagnosticCodes
{
    /// <summary>A texture source could not be read.</summary>
    internal const string TextureSourceRead = "BK4001";

    /// <summary>A texture source path cannot be mapped to a canonical asset path.</summary>
    internal const string TextureSourcePath = "BK4002";

    /// <summary>A texture source is corrupt, truncated, or unsupported by the decoder.</summary>
    internal const string TextureDecode = "BK4003";

    /// <summary>A PNG source declares a 16-bit bit depth, which M4 rejects (Q-12).</summary>
    internal const string TextureBitDepth = "BK4004";

    /// <summary>A texture source has zero, negative, overflowing, or oversized dimensions.</summary>
    internal const string TextureDimensions = "BK4005";

    /// <summary>Two texture sources map to the same canonical asset path.</summary>
    internal const string TexturePathCollision = "BK4006";

    /// <summary>A texture source lives under the reserved <c>textures/raw/</c> path.</summary>
    internal const string TextureReservedPath = "BK4007";

    /// <summary>A <c>&lt;source&gt;.toml</c> sidecar has no matching source file.</summary>
    internal const string TextureSidecarWithoutSource = "BK4008";

    /// <summary>A <c>.toml</c> file under <c>textures/</c> is not a <c>&lt;source&gt;.toml</c> sidecar.</summary>
    internal const string TextureStrayToml = "BK4009";

    /// <summary>A texture sidecar could not be read.</summary>
    internal const string TextureSidecarRead = "BK4010";

    /// <summary>A texture sidecar declares a key outside <c>filter</c>/<c>wrap</c> (Q-24).</summary>
    internal const string TextureSidecarUnknownKey = "BK4011";

    /// <summary>A texture sidecar <c>filter</c>/<c>wrap</c> value has the wrong type or contents.</summary>
    internal const string TextureSidecarValue = "BK4012";

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
