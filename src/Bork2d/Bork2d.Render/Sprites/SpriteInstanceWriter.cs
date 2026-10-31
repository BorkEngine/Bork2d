using System.Runtime.InteropServices;

namespace Bork2d.Render.Sprites;

/// <summary>
/// Writes a <see cref="SpriteInstance"/> directly into the mapped per-frame instance memory
/// (M8 §6.6).
/// </summary>
/// <remarks>
/// The renderer owns one host-visible coherent instance buffer per GPU frame and writes records
/// straight into its mapping; there is no CPU frame array and no staging copy. The
/// <see cref="Span{T}"/> overload exists for deterministic tests and any span-backed mapping; it
/// performs the same single blit.
/// </remarks>
internal static class SpriteInstanceWriter
{
    /// <summary>Writes an instance at the start of a mapped byte span.</summary>
    /// <param name="destination">The mapped destination; at least <see cref="SpriteInstance.SizeInBytes"/>.</param>
    /// <param name="instance">The record to write.</param>
    /// <exception cref="ArgumentOutOfRangeException">The destination is too small.</exception>
    internal static void Write(Span<byte> destination, in SpriteInstance instance)
    {
        if (destination.Length < SpriteInstance.SizeInBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination.Length,
                $"The destination must hold at least {SpriteInstance.SizeInBytes} bytes.");
        }

        MemoryMarshal.Write(destination, in instance);
    }

    /// <summary>Writes an instance at an unmanaged address.</summary>
    /// <param name="destination">The mapped destination address.</param>
    /// <param name="instance">The record to write.</param>
    internal static unsafe void Write(byte* destination, in SpriteInstance instance)
        => *(SpriteInstance*)destination = instance;

    /// <summary>Writes an instance at a mapped native address.</summary>
    /// <param name="destination">The mapped destination address.</param>
    /// <param name="instance">The record to write.</param>
    internal static unsafe void Write(nint destination, in SpriteInstance instance)
        => *(SpriteInstance*)destination = instance;
}
