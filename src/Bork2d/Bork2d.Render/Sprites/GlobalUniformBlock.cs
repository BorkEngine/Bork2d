using System.Numerics;
using System.Runtime.InteropServices;

namespace Bork2d.Render.Sprites;

/// <summary>
/// The per-frame std140 global uniform block shared by every custom fragment shader: one
/// <c>vec4</c> time input followed by eight <c>vec4</c> user global slots.
/// </summary>
/// <remarks>
/// <para>
/// The block is exactly 144 bytes and matches the fragment prologue's
/// <c>vec4 time; vec4 globals[8];</c> declaration. <see cref="Time"/> carries the frame's
/// <c>ITime.Time</c> in <c>X</c>; the remaining time components are zero. Each user global slot holds
/// the engine-wide value with the same ordinal name and type when the run's shader declares one, or
/// that declaration's shader default when no engine value exists.
/// </para>
/// <para>
/// The renderer writes one block per planned shader run into the frame's host-visible dynamic uniform
/// buffer and binds it with the run's own dynamic offset.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct GlobalUniformBlock
{
    /// <summary>The block size in bytes (nine <c>vec4</c> values).</summary>
    internal const int SizeInBytes = 144;

    /// <summary>The number of <c>vec4</c> values in the block.</summary>
    internal const int VectorCount = 9;

    /// <summary>The number of user <c>vec4</c> slots after the time input.</summary>
    internal const int GlobalSlotCount = 8;

    /// <summary>The byte offset of <see cref="Time"/>.</summary>
    internal const int TimeOffset = 0;

    /// <summary>The byte offset of <see cref="Global0"/>.</summary>
    internal const int GlobalsOffset = 16;

    /// <summary>Gets or sets the time input; <c>X</c> is the frame time in seconds.</summary>
    internal Vector4 Time;

    /// <summary>Gets or sets user global slot 0.</summary>
    internal Vector4 Global0;

    /// <summary>Gets or sets user global slot 1.</summary>
    internal Vector4 Global1;

    /// <summary>Gets or sets user global slot 2.</summary>
    internal Vector4 Global2;

    /// <summary>Gets or sets user global slot 3.</summary>
    internal Vector4 Global3;

    /// <summary>Gets or sets user global slot 4.</summary>
    internal Vector4 Global4;

    /// <summary>Gets or sets user global slot 5.</summary>
    internal Vector4 Global5;

    /// <summary>Gets or sets user global slot 6.</summary>
    internal Vector4 Global6;

    /// <summary>Gets or sets user global slot 7.</summary>
    internal Vector4 Global7;
}
