using System.Numerics;
using System.Runtime.InteropServices;

namespace Bork2d.Render.Sprites;

/// <summary>
/// The fixed 176-byte std430 instance record consumed by the sprite vertex shader (M8 §6.6,
/// shader ABI v2).
/// </summary>
/// <remarks>
/// <para>
/// The layout is eleven <c>vec4</c> values with no padding: <c>transform0</c> (M11, M12, M21, M22),
/// <c>transform1</c> (M31, M32, sizeX, sizeY), <c>uv0</c> (uTL, vTL, uTR, vTR), <c>uv1</c>
/// (uBR, vBR, uBL, vBL), <c>tint</c> (linear RGBA), <c>misc</c> (pivotX, pivotY, mainTextureIndex,
/// flags), <c>extraTextureIndices</c> (four additional bindless texture indices as floats), and
/// <c>instanceParams0</c> through <c>instanceParams3</c> (the 16-float custom-shader instance
/// uniform block). <c>flags</c> is reserved and is zero in this milestone.
/// </para>
/// <para>
/// <c>sizeX</c>/<c>sizeY</c> carry the <c>FlipX</c>/<c>FlipY</c> sign, which is the algebraically
/// identical form of the approved <c>size * sx</c>/<c>size * sy</c> corner formula. The CPU writer
/// and the GLSL struct must stay in lockstep; a deterministic test asserts the field offsets.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct SpriteInstance
{
    /// <summary>The instance stride in bytes (11 x vec4).</summary>
    internal const int SizeInBytes = 176;

    /// <summary>The byte offset of <see cref="Transform0"/>.</summary>
    internal const int Transform0Offset = 0;

    /// <summary>The byte offset of <see cref="Transform1"/>.</summary>
    internal const int Transform1Offset = 16;

    /// <summary>The byte offset of <see cref="Uv0"/>.</summary>
    internal const int Uv0Offset = 32;

    /// <summary>The byte offset of <see cref="Uv1"/>.</summary>
    internal const int Uv1Offset = 48;

    /// <summary>The byte offset of <see cref="Tint"/>.</summary>
    internal const int TintOffset = 64;

    /// <summary>The byte offset of <see cref="Misc"/>.</summary>
    internal const int MiscOffset = 80;

    /// <summary>The byte offset of <see cref="ExtraTextureIndices"/>.</summary>
    internal const int ExtraTextureIndicesOffset = 96;

    /// <summary>The byte offset of <see cref="InstanceParams0"/>.</summary>
    internal const int InstanceParams0Offset = 112;

    /// <summary>The byte offset of <see cref="InstanceParams1"/>.</summary>
    internal const int InstanceParams1Offset = 128;

    /// <summary>The byte offset of <see cref="InstanceParams2"/>.</summary>
    internal const int InstanceParams2Offset = 144;

    /// <summary>The byte offset of <see cref="InstanceParams3"/>.</summary>
    internal const int InstanceParams3Offset = 160;

    /// <summary>Gets or sets M11, M12, M21, M22 (the 2x2 linear part).</summary>
    internal Vector4 Transform0;

    /// <summary>Gets or sets M31, M32, sizeX, sizeY (the translation and signed quad size).</summary>
    internal Vector4 Transform1;

    /// <summary>Gets or sets uTL, vTL, uTR, vTR.</summary>
    internal Vector4 Uv0;

    /// <summary>Gets or sets uBR, vBR, uBL, vBL.</summary>
    internal Vector4 Uv1;

    /// <summary>Gets or sets the linear RGBA tint.</summary>
    internal Vector4 Tint;

    /// <summary>Gets or sets pivotX, pivotY, mainTextureIndex, flags (reserved, zero).</summary>
    internal Vector4 Misc;

    /// <summary>Gets or sets the four additional bindless texture indices as floats.</summary>
    internal Vector4 ExtraTextureIndices;

    /// <summary>Gets or sets the first four float lanes of the instance uniform block.</summary>
    internal Vector4 InstanceParams0;

    /// <summary>Gets or sets the second four float lanes of the instance uniform block.</summary>
    internal Vector4 InstanceParams1;

    /// <summary>Gets or sets the third four float lanes of the instance uniform block.</summary>
    internal Vector4 InstanceParams2;

    /// <summary>Gets or sets the fourth four float lanes of the instance uniform block.</summary>
    internal Vector4 InstanceParams3;
}
