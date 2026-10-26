using System.Numerics;
using Bork2d.Api.Input;

namespace Bork2d.Runtime.Input;

/// <summary>
/// The immutable input state the tick thread reads for one tick.
/// </summary>
/// <remarks>
/// <para>
/// The proxy owns two preallocated instances and fills them alternately, so the freeze path copies
/// into an instance that no tick-thread read can observe until it is published. The bitsets use the
/// physical scancode as the bit index and the SDL pressed-button mask for the mouse, which keeps the
/// steady-state freeze allocation-free. Text is the only member that may grow, and only when text
/// input actually arrives.
/// </para>
/// <para>
/// A snapshot is replaced as a whole by a volatile reference write; the tick thread never mutates a
/// published instance except to consume its text through
/// <see cref="InputProxy.DispatchText"/>. Instances contain no SDL handles and no thread-affine data.
/// </para>
/// </remarks>
internal sealed class InputSnapshot
{
    /// <summary>The number of physical scancodes SDL defines; also the bitset width.</summary>
    internal const int ScancodeCount = 512;

    /// <summary>The number of 64-bit words in one scancode bitset.</summary>
    internal const int KeyWordCount = ScancodeCount / 64;

    /// <summary>The mask of the five supported mouse buttons (SDL mask bits 0 through 4).</summary>
    internal const byte MouseMask = 0x1F;

    /// <summary>Gets the one-bits of keys that are held at this freeze.</summary>
    internal readonly ulong[] KeyDown = new ulong[KeyWordCount];

    /// <summary>Gets the one-bits of keys that went down since the previous freeze.</summary>
    internal readonly ulong[] KeyPressed = new ulong[KeyWordCount];

    /// <summary>Gets the one-bits of keys that went up since the previous freeze.</summary>
    internal readonly ulong[] KeyReleased = new ulong[KeyWordCount];

    /// <summary>Gets the SDL pressed-button mask of buttons that are held at this freeze.</summary>
    internal byte MouseDown { get; set; }

    /// <summary>Gets the SDL pressed-button mask of buttons that went down since the previous freeze.</summary>
    internal byte MousePressed { get; set; }

    /// <summary>Gets the SDL pressed-button mask of buttons that went up since the previous freeze.</summary>
    internal byte MouseReleased { get; set; }

    /// <summary>Gets the mouse position in framebuffer pixels, top-left origin.</summary>
    internal Vector2 Position { get; set; }

    /// <summary>Gets the mouse movement since the previous freeze in framebuffer pixels.</summary>
    internal Vector2 Delta { get; set; }

    /// <summary>Gets the accumulated vertical wheel movement since the previous freeze.</summary>
    internal float Wheel { get; set; }

    /// <summary>Gets or sets the reusable text buffer; only the length prefix is meaningful.</summary>
    internal char[] Text { get; set; } = [];

    /// <summary>Gets or sets the number of valid characters in <see cref="Text"/>.</summary>
    internal int TextLength { get; set; }

    /// <summary>Returns whether <paramref name="key"/> is held in this snapshot.</summary>
    /// <param name="key">The physical key; any undefined value returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the key's bit is set.</returns>
    internal bool IsKeyDown(KeyCode key) => IsKeySet(KeyDown, (int)key);

    /// <summary>Returns whether <paramref name="key"/> went down since the previous freeze.</summary>
    /// <param name="key">The physical key; any undefined value returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the press bit is set.</returns>
    internal bool IsKeyPressed(KeyCode key) => IsKeySet(KeyPressed, (int)key);

    /// <summary>Returns whether <paramref name="key"/> went up since the previous freeze.</summary>
    /// <param name="key">The physical key; any undefined value returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the release bit is set.</returns>
    internal bool IsKeyReleased(KeyCode key) => IsKeySet(KeyReleased, (int)key);

    /// <summary>Returns whether <paramref name="button"/> is held in this snapshot.</summary>
    /// <param name="button">The button; any undefined value returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the button's mask bit is set.</returns>
    internal bool IsMouseDown(MouseButton button) => IsMouseSet(MouseDown, button);

    /// <summary>Returns whether <paramref name="button"/> went down since the previous freeze.</summary>
    /// <param name="button">The button; any undefined value returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the press mask bit is set.</returns>
    internal bool IsMousePressed(MouseButton button) => IsMouseSet(MousePressed, button);

    /// <summary>Returns whether <paramref name="button"/> went up since the previous freeze.</summary>
    /// <param name="button">The button; any undefined value returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the release mask bit is set.</returns>
    internal bool IsMouseReleased(MouseButton button) => IsMouseSet(MouseReleased, button);

    /// <summary>Returns the SDL pressed-button mask bit for a supported button, or zero.</summary>
    /// <param name="button">The button to map.</param>
    /// <returns>The mask bit, or zero for an undefined value.</returns>
    internal static byte MouseBit(MouseButton button)
    {
        var index = (int)button - 1;
        return (uint)index < 5u ? (byte)(1 << index) : (byte)0;
    }

    private static bool IsKeySet(ulong[] bits, int scancode)
    {
        if ((uint)scancode >= ScancodeCount)
        {
            return false;
        }

        return (bits[scancode >> 6] & (1UL << (scancode & 63))) != 0;
    }

    private static bool IsMouseSet(byte mask, MouseButton button)
        => (mask & MouseBit(button)) != 0;
}
