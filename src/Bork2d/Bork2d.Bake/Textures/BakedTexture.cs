using Bork2d.Api.Assets;

namespace Bork2d.Bake.Textures;

/// <summary>
/// One successfully imported texture, ready for WP4 to add to a pack.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Descriptor"/> is a complete immutable-by-convention value: its payload path is
/// <see cref="PayloadPath"/>, its format is <c>rgba8</c>, and its sampler state came from
/// <see cref="Settings"/>. <see cref="Payload"/> is exactly <c>Width × Height × 4</c> bytes,
/// row-major, top-left origin, straight alpha.
/// </para>
/// <para>
/// A texture exists in the result only when discovery, sidecar parsing, header probing, decoding,
/// and payload validation all succeeded; a failure never leaves a partial descriptor or payload.
/// </para>
/// </remarks>
internal sealed class BakedTexture
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BakedTexture"/> class.
    /// </summary>
    /// <param name="assetPath">The canonical descriptor asset path, for example <c>textures/ui/button</c>.</param>
    /// <param name="descriptor">The descriptor to write as MessagePack.</param>
    /// <param name="payloadPath">The canonical raw payload path, for example <c>textures/raw/ui/button</c>.</param>
    /// <param name="payload">The RGBA8 payload bytes.</param>
    /// <param name="sourcePath">The absolute source image path.</param>
    /// <param name="settings">The resolved import settings and their sidecar spans.</param>
    internal BakedTexture(
        string assetPath,
        TextureDescriptor descriptor,
        string payloadPath,
        byte[] payload,
        string sourcePath,
        TextureImportSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadPath);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(settings);

        AssetPath = assetPath;
        Descriptor = descriptor;
        PayloadPath = payloadPath;
        Payload = payload;
        SourcePath = sourcePath;
        Settings = settings;
    }

    /// <summary>Gets the canonical descriptor asset path.</summary>
    internal string AssetPath { get; }

    /// <summary>Gets the descriptor to add as a MessagePack asset.</summary>
    internal TextureDescriptor Descriptor { get; }

    /// <summary>Gets the canonical raw payload path.</summary>
    internal string PayloadPath { get; }

    /// <summary>Gets the RGBA8 payload bytes.</summary>
    internal byte[] Payload { get; }

    /// <summary>Gets the absolute source image path.</summary>
    internal string SourcePath { get; }

    /// <summary>Gets the resolved import settings and their sidecar spans.</summary>
    internal TextureImportSettings Settings { get; }
}
