using System.Numerics;
using Bork2d.Api;

namespace Bork2d.Assets;

/// <summary>
/// Validates texture descriptors and sprite assets against the M3 rules, using the VFS snapshot a
/// request captured at enqueue time.
/// </summary>
/// <remarks>
/// <para>
/// Validation is metadata-only: it never decompresses or reads a payload. A descriptor's payload
/// must exist in the snapshot and be raw, and its declared <c>Size</c> must equal
/// <c>width × height × 4</c> for RGBA8. A sprite must have a canonical texture path, four finite
/// normalized UVs with a positive extent, a finite positive scale, a normalized pivot, a known
/// topology, and indices that match that topology.
/// </para>
/// <para>
/// Each method returns a reason string on failure and <see langword="null"/> on success. Callers
/// convert the reason into <see cref="Bork2d.Api.AssetException"/> with
/// <see cref="AssetErrorKind.InvalidData"/>.
/// </para>
/// </remarks>
internal static class AssetValidation
{
    private const int BytesPerRgba8Pixel = 4;

    /// <summary>Validates a texture descriptor and resolves its payload entry.</summary>
    /// <param name="descriptor">The deserialized descriptor.</param>
    /// <param name="snapshot">The enqueue-time VFS snapshot.</param>
    /// <param name="payload">The raw payload entry when validation succeeds.</param>
    /// <returns>A reason when invalid, or <see langword="null"/> when valid.</returns>
    internal static string? ValidateTexture(
        TextureDescriptor? descriptor,
        AssetSnapshot snapshot,
        out ResolvedAsset? payload)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        payload = null;

        if (descriptor is null)
        {
            return "the texture descriptor is null";
        }

        string? payloadPath = descriptor.Payload;
        if (!AssetPath.TryValidateCanonical(payloadPath, out string? pathReason))
        {
            return $"the payload path '{payloadPath ?? "<null>"}' is not canonical: {pathReason}";
        }

        if (!snapshot.TryGetEntry(payloadPath!, out ResolvedAsset? payloadEntry))
        {
            return $"the payload asset '{payloadPath}' is not present in the captured snapshot";
        }

        if (!payloadEntry.IsRaw)
        {
            return $"the payload asset '{payloadPath}' is not raw data";
        }

        if (descriptor.Width == 0)
        {
            return "the descriptor width must be greater than zero";
        }

        if (descriptor.Height == 0)
        {
            return "the descriptor height must be greater than zero";
        }

        if (!string.Equals(descriptor.Format, "rgba8", StringComparison.Ordinal))
        {
            return $"the format '{descriptor.Format ?? "<null>"}' is not supported; only 'rgba8' "
                + "is supported in v1";
        }

        if (!IsFilter(descriptor.Filter))
        {
            return $"the filter '{descriptor.Filter ?? "<null>"}' is not 'nearest' or 'linear'";
        }

        if (!IsWrap(descriptor.Wrap))
        {
            return $"the wrap mode '{descriptor.Wrap ?? "<null>"}' is not 'clamp' or 'repeat'";
        }

        ulong pixelCount = (ulong)descriptor.Width * descriptor.Height;
        if (pixelCount > ulong.MaxValue / BytesPerRgba8Pixel)
        {
            return $"the dimensions {descriptor.Width} × {descriptor.Height} overflow the RGBA8 "
                + "payload size calculation";
        }

        ulong expectedSize = pixelCount * BytesPerRgba8Pixel;
        if (expectedSize != (ulong)payloadEntry.Size)
        {
            return $"the payload asset '{payloadPath}' declares {payloadEntry.Size} bytes but "
                + $"{descriptor.Width} × {descriptor.Height} × 4 is {expectedSize}";
        }

        payload = payloadEntry;
        return null;
    }

    /// <summary>Validates the sprite metadata that does not require filesystem lookups.</summary>
    /// <param name="sprite">The deserialized sprite asset.</param>
    /// <returns>A reason when invalid, or <see langword="null"/> when valid.</returns>
    internal static string? ValidateSprite(SpriteAsset? sprite)
    {
        if (sprite is null)
        {
            return "the sprite asset is null";
        }

        string? texturePath = sprite.Texture;
        if (!AssetPath.TryValidateCanonical(texturePath, out string? pathReason))
        {
            return $"the texture path '{texturePath ?? "<null>"}' is not canonical: {pathReason}";
        }

        Vector2[]? uvs = sprite.Uvs;
        if (uvs is null)
        {
            return "the UV array is null";
        }

        if (uvs.Length != 4)
        {
            return $"the UV array has {uvs.Length} entries; exactly four are required";
        }

        float minU = float.PositiveInfinity;
        float maxU = float.NegativeInfinity;
        float minV = float.PositiveInfinity;
        float maxV = float.NegativeInfinity;
        for (int i = 0; i < uvs.Length; i++)
        {
            Vector2 uv = uvs[i];
            if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y))
            {
                return $"UV {i} is not finite";
            }

            if (uv.X is < 0f or > 1f || uv.Y is < 0f or > 1f)
            {
                return $"UV {i} ({uv.X}, {uv.Y}) is outside the normalized [0, 1] range";
            }

            minU = Math.Min(minU, uv.X);
            maxU = Math.Max(maxU, uv.X);
            minV = Math.Min(minV, uv.Y);
            maxV = Math.Max(maxV, uv.Y);
        }

        if (maxU <= minU)
        {
            return "the UVs have no horizontal extent (max U must be greater than min U)";
        }

        if (maxV <= minV)
        {
            return "the UVs have no vertical extent (max V must be greater than min V)";
        }

        if (!float.IsFinite(sprite.Scale) || sprite.Scale <= 0f)
        {
            return $"the scale {sprite.Scale} must be finite and greater than zero";
        }

        if (!IsNormalized(sprite.Pivot.X) || !IsNormalized(sprite.Pivot.Y))
        {
            return $"the pivot ({sprite.Pivot.X}, {sprite.Pivot.Y}) must be finite and in [0, 1]";
        }

        switch (sprite.Topology)
        {
            case SpriteTopology.Quad:
                if (sprite.Indices is not null)
                {
                    return "a Quad sprite must not have an index buffer";
                }

                return null;

            case SpriteTopology.TriangleStrip:
            case SpriteTopology.Triangles:
                return ValidateIndices(sprite);

            default:
                return $"the topology value {(int)sprite.Topology} is not Quad, TriangleStrip, or "
                    + "Triangles";
        }
    }

    private static string? ValidateIndices(SpriteAsset sprite)
    {
        ushort[]? indices = sprite.Indices;
        if (indices is null)
        {
            return $"a {sprite.Topology} sprite requires an index buffer";
        }

        if (indices.Length < 3)
        {
            return $"the index buffer has {indices.Length} entries; at least three are required";
        }

        foreach (ushort index in indices)
        {
            if (index > 3)
            {
                return $"the index {index} is outside the four quad corners (0..3)";
            }
        }

        if (sprite.Topology == SpriteTopology.Triangles && indices.Length % 3 != 0)
        {
            return $"the Triangles index buffer length {indices.Length} is not a multiple of 3";
        }

        return null;
    }

    private static bool IsFilter(string? filter)
        => filter is "nearest" or "linear";

    private static bool IsWrap(string? wrap)
        => wrap is "clamp" or "repeat";

    private static bool IsNormalized(float value)
        => float.IsFinite(value) && value is >= 0f and <= 1f;
}
