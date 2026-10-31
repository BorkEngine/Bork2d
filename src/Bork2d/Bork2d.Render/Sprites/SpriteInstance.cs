using System.Numerics;
using System.Runtime.InteropServices;

namespace Bork2d.Render.Sprites;

/// <summary>
/// The fixed 96-byte std430 instance record consumed by the sprite vertex shader (M8 §6.6).
/// </summary>
/// <remarks>
/// <para>
/// The layout is six <c>vec4</c> values with no padding: <c>transform0</c> (M11, M12, M21, M22),
/// <c>transform1</c> (M31, M32, sizeX, sizeY), <c>uv0</c> (uTL, vTL, uTR, vTR), <c>uv1</c>
/// (uBR, vBR, uBL, vBL), <c>tint</c> (linear RGBA), and <c>misc</c> (pivotX, pivotY,
/// textureIndex, flags). <c>flags</c> is reserved and is zero in M8.
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
    /// <summary>The instance stride in bytes (6 x vec4).</summary>
    internal const int SizeInBytes = 96;

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

    /// <summary>Gets or sets pivotX, pivotY, textureIndex, flags (reserved, zero).</summary>
    internal Vector4 Misc;
}
