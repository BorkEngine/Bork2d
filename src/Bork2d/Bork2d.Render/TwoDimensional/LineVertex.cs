using System.Numerics;
using System.Runtime.InteropServices;

namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// The fixed 112-byte line vertex record consumed by the built-in line vertex shader (shader ABI v3).
/// </summary>
/// <remarks>
/// <para>
/// The layout is seven records with no padding: <see cref="Position"/> (world space),
/// <see cref="LineUv"/> (edge coordinate in <c>x</c>, distance along the line in <c>y</c>),
/// <see cref="Color"/> (linear RGBA), <see cref="Misc"/> (<c>x</c> is the anti-alias flag; the other
/// components are zero), and <see cref="Params0"/> through <see cref="Params3"/> (the 16-float
/// custom-shader instance uniform block). The record is written once per triangle vertex; there is
/// no instance buffer for line draws.
/// </para>
/// <para>
/// The built-in line vertex stage binds this record as attribute locations 0-7 with a 112-byte
/// stride, so the CPU writer and the GLSL declarations must stay in lockstep. A deterministic test
/// asserts the field offsets and the stride.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct LineVertex
{
    /// <summary>The vertex stride in bytes (two vec2 values and five vec4 values).</summary>
    internal const int SizeInBytes = 112;

    /// <summary>The number of scalar floats in one record.</summary>
    internal const int FloatCount = 28;

    /// <summary>The byte offset of <see cref="Position"/>.</summary>
    internal const int PositionOffset = 0;

    /// <summary>The byte offset of <see cref="LineUv"/>.</summary>
    internal const int LineUvOffset = 8;

    /// <summary>The byte offset of <see cref="Color"/>.</summary>
    internal const int ColorOffset = 16;

    /// <summary>The byte offset of <see cref="Misc"/>.</summary>
    internal const int MiscOffset = 32;

    /// <summary>The byte offset of <see cref="Params0"/>.</summary>
    internal const int Params0Offset = 48;

    /// <summary>The byte offset of <see cref="Params1"/>.</summary>
    internal const int Params1Offset = 64;

    /// <summary>The byte offset of <see cref="Params2"/>.</summary>
    internal const int Params2Offset = 80;

    /// <summary>The byte offset of <see cref="Params3"/>.</summary>
    internal const int Params3Offset = 96;

    /// <summary>Gets or sets the world-space triangle vertex position.</summary>
    internal Vector2 Position;

    /// <summary>Gets or sets the stroke edge coordinate (0 or 1) and the distance along the line.</summary>
    internal Vector2 LineUv;

    /// <summary>Gets or sets the linear RGBA line color.</summary>
    internal Vector4 Color;

    /// <summary>Gets or sets the misc record; <c>x</c> is the anti-alias flag (1 or 0).</summary>
    internal Vector4 Misc;

    /// <summary>Gets or sets the first four float lanes of the instance uniform block.</summary>
    internal Vector4 Params0;

    /// <summary>Gets or sets the second four float lanes of the instance uniform block.</summary>
    internal Vector4 Params1;

    /// <summary>Gets or sets the third four float lanes of the instance uniform block.</summary>
    internal Vector4 Params2;

    /// <summary>Gets or sets the fourth four float lanes of the instance uniform block.</summary>
    internal Vector4 Params3;
}
