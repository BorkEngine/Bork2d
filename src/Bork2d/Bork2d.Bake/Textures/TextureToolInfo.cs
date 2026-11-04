namespace Bork2d.Bake.Textures;

/// <summary>
/// Identifies the KTX2 writer, block encoder, and mip generator versions.
/// </summary>
/// <remarks>
/// <see cref="ConfigurationMarker"/> participates in the incremental configuration hash, so changing
/// the KTX2 profile, upgrading BCnEncoder.Net, or changing the mip filter invalidates every cached
/// texture payload instead of reusing bytes produced by a different tool.
/// </remarks>
internal static class TextureToolInfo
{
    /// <summary>The KTX2 writer profile version, bumped when the container layout changes.</summary>
    internal const string Ktx2WriterVersion = "ktx2-1";

    /// <summary>The block encoder package version.</summary>
    internal const string BcEncoderVersion = "bcn-2.3.0";

    /// <summary>The mip generation version, bumped when the box filter changes.</summary>
    internal const string MipGeneratorVersion = "mips-1";

    /// <summary>
    /// Gets the marker recorded in the incremental configuration hash:
    /// <c>ktx2-1/bcn-2.3.0/mips-1</c>.
    /// </summary>
    internal static string ConfigurationMarker { get; } =
        $"{Ktx2WriterVersion}/{BcEncoderVersion}/{MipGeneratorVersion}";
}
