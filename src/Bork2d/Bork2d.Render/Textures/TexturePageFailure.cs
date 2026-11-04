namespace Bork2d.Render.Textures;

/// <summary>
/// Why a texture page's content was rejected (M8 §6.5/§6.12).
/// </summary>
/// <remarks>
/// Every value is a content failure: the path is marked failed for the current asset catalog, the
/// sprite draws the placeholder, and the renderer keeps running. A later mount retires the failure,
/// so the path is retried from the new catalog. Vulkan allocation/copy/queue failures and a missing
/// sampled format are fatal <c>Bork2dException</c>s instead and never map to this enum.
/// </remarks>
internal enum TexturePageFailure
{
    /// <summary>No failure; the page is pending, uploading, or ready.</summary>
    None,

    /// <summary>The texture descriptor asset could not be loaded or was absent.</summary>
    MissingDescriptor,

    /// <summary>The descriptor has no payload reference.</summary>
    MissingPayload,

    /// <summary>The descriptor's format has no runtime KTX2/Vulkan mapping.</summary>
    UnsupportedFormat,

    /// <summary>The declared width or height is zero.</summary>
    InvalidDimensions,

    /// <summary>The declared width or height exceeds the device's <c>maxImageDimension2D</c>.</summary>
    DimensionLimit,

    /// <summary>
    /// The payload is not a valid narrow-profile KTX2 2.0 file: identifier, header profile fields,
    /// data format descriptor, level index, level lengths, level offsets/alignment, or bounds.
    /// </summary>
    MalformedKtx2,

    /// <summary>The KTX2 header's <c>vkFormat</c> does not match the descriptor's format mapping.</summary>
    FormatMismatch,

    /// <summary>The KTX2 header's base pixel dimensions do not match the descriptor.</summary>
    DimensionMismatch,

    /// <summary>
    /// The KTX2 level count does not match the descriptor's mip count, or it is not the complete chain
    /// down to 1x1.
    /// </summary>
    MipMismatch,

    /// <summary>The descriptor or payload load faulted (missing entry, decode error, disposed service).</summary>
    LoadFault,
}
