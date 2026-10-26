using System.ComponentModel;
using Bork2d.Api.Assets;
using PolyType;

namespace Bork2d.Api.Rendering;

/// <summary>
/// Serializes <see cref="Color"/> as a four-element linear float array (<c>[r, g, b, a]</c>).
/// </summary>
/// <remarks>
/// <para>
/// Components are linear values with no sRGB conversion. This type is public because PolyType
/// generates the marshaled shape in the assembly that declares the containing asset type; it is
/// infrastructure, not a game-facing API.
/// </para>
/// <para>
/// A <see langword="null"/> surrogate (a MessagePack <c>nil</c> where a color is required) is a
/// decode failure, never a silently defaulted color.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ColorMarshaler : IMarshaler<Color, float[]>
{
    /// <summary>Marshals a color to its four linear components.</summary>
    /// <param name="value">The color to marshal.</param>
    /// <returns>The <c>[r, g, b, a]</c> surrogate.</returns>
    public float[] Marshal(Color value) => [value.R, value.G, value.B, value.A];

    /// <summary>Unmarshals a four-element linear surrogate.</summary>
    /// <param name="surrogate">The <c>[r, g, b, a]</c> surrogate.</param>
    /// <returns>The color.</returns>
    /// <exception cref="AssetException">
    /// The surrogate is <see langword="null"/> or does not contain exactly four components; the
    /// failure kind is <see cref="AssetErrorKind.Decode"/>.
    /// </exception>
    public Color Unmarshal(float[]? surrogate)
    {
        if (surrogate is null || surrogate.Length != 4)
        {
            throw new AssetException(
                AssetErrorKind.Decode,
                assetPath: null,
                "A Color value cannot be deserialized from a null or non-four-element surrogate.");
        }

        return new(surrogate[0], surrogate[1], surrogate[2], surrogate[3]);
    }
}
