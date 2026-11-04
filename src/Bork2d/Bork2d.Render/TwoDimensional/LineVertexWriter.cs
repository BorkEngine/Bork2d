using System.Runtime.InteropServices;

namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// Writes a <see cref="LineVertex"/> directly into the mapped per-frame line vertex memory.
/// </summary>
/// <remarks>
/// The renderer owns one host-visible coherent line vertex buffer per GPU frame and writes records
/// straight into its mapping; there is no CPU frame array and no staging copy. The record is exactly
/// 28 floats (position, line UV, color, misc, and the four instance-uniform lanes). The
/// <see cref="Span{T}"/> overload exists for deterministic tests and any span-backed mapping; it
/// performs the same single blit.
/// </remarks>
internal static class LineVertexWriter
{
    /// <summary>Writes a vertex at the start of a mapped byte span.</summary>
    /// <param name="destination">The mapped destination; at least <see cref="LineVertex.SizeInBytes"/>.</param>
    /// <param name="vertex">The record to write.</param>
    /// <exception cref="ArgumentOutOfRangeException">The destination is too small.</exception>
    internal static void Write(Span<byte> destination, in LineVertex vertex)
    {
        if (destination.Length < LineVertex.SizeInBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination.Length,
                $"The destination must hold at least {LineVertex.SizeInBytes} bytes.");
        }

        MemoryMarshal.Write(destination, in vertex);
    }

    /// <summary>Writes a vertex at an unmanaged address.</summary>
    /// <param name="destination">The mapped destination address.</param>
    /// <param name="vertex">The record to write.</param>
    internal static unsafe void Write(byte* destination, in LineVertex vertex)
        => *(LineVertex*)destination = vertex;

    /// <summary>Writes a vertex at a mapped native address.</summary>
    /// <param name="destination">The mapped destination address.</param>
    /// <param name="vertex">The record to write.</param>
    internal static unsafe void Write(nint destination, in LineVertex vertex)
        => *(LineVertex*)destination = vertex;
}
