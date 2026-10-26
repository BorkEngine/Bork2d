using System.Numerics;
using Bork2d.Api;

namespace Bork2d.Runtime;

/// <summary>
/// The event-fed input state the OS/window thread writes and the tick thread freezes once per tick.
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="Lock"/> guards every mutable member: held and edge bitsets, the mouse position in
/// window points, the wheel accumulator, the pending text characters, and the focus/delta flags. The
/// lock is never held across an SDL call, a game callback, or a blocking wait. All methods are called
/// from the OS thread except <see cref="Freeze"/> and the delta baseline fields, which only the tick
/// thread touches.
/// </para>
/// <para>
/// Edges follow the transition rule: a non-repeat key down on an up key (or a supported button down
/// on an up button) sets the held bit and the pressed bit; an up on a down key sets the released bit
/// and clears the held bit; duplicates and repeat downs change nothing. Undefined scancodes, the
/// unknown scancode zero, and buttons other than the five supported ones are ignored.
/// </para>
/// <para>
/// Focus loss synthesizes a release for every held key and button, clears the held state, wheel, and
/// pending text, and forces the next delta to zero. Focus regain overwrites the held state and the
/// mouse position from the operating system without creating press edges, and also forces the next
/// delta to zero. Both are applied in event order.
/// </para>
/// </remarks>
internal sealed class InputAccumulator
{
    private const int ScancodeCount = InputSnapshot.ScancodeCount;
    private const int KeyWordCount = InputSnapshot.KeyWordCount;
    private const byte MouseMask = InputSnapshot.MouseMask;

    // One bit per KeyCode member, generated from the SDL scancode enum so malformed or undefined
    // scancodes in an event are ignored instead of setting a bit no KeyCode member can name. The
    // enum mapping test keeps this bitset and KeyCode in sync.
    private static readonly ulong[] _definedScancodes =
    [
        0xFFFFFFFFFFFFFFF1UL,
        0xFFFFFFFFFFFFFFFFUL,
        0xFFFF001FFFFFFFE3UL,
        0x000000FF3FFFFFFFUL,
        0x00000007FFFFFFFEUL,
        0x0000000000000000UL,
        0x0000000000000000UL,
        0x0000000000000000UL,
    ];

    private readonly Lock _gate = new();
    private readonly ulong[] _keyDown = new ulong[KeyWordCount];
    private readonly ulong[] _keyPressed = new ulong[KeyWordCount];
    private readonly ulong[] _keyReleased = new ulong[KeyWordCount];
    private readonly List<char> _text = [];

    private byte _mouseDown;
    private byte _mousePressed;
    private byte _mouseReleased;
    private int _mouseX;
    private int _mouseY;
    private float _wheel;
    private bool _focused;
    private bool _zeroDelta;

    // Tick-thread-only delta baseline; never touched by the OS thread.
    private Vector2 _previousPosition;
    private bool _hasFrozen;

    /// <summary>Sets the initial focus state after window creation; creates no edges.</summary>
    /// <param name="focused">Whether the window has keyboard focus at startup.</param>
    internal void SetFocused(bool focused)
    {
        lock (_gate)
        {
            _focused = focused;
        }
    }

    /// <summary>Applies a key down.</summary>
    /// <param name="scancode">The physical scancode from the SDL event.</param>
    /// <param name="isRepeat">Whether SDL flagged the event as a key repeat.</param>
    internal void KeyDown(int scancode, bool isRepeat)
    {
        if (isRepeat || !IsScancode(scancode))
        {
            return;
        }

        lock (_gate)
        {
            if (!_focused)
            {
                return;
            }

            var word = scancode >> 6;
            var bit = 1UL << (scancode & 63);
            if ((_keyDown[word] & bit) != 0)
            {
                return;
            }

            _keyDown[word] |= bit;
            _keyPressed[word] |= bit;
        }
    }

    /// <summary>Applies a key up.</summary>
    /// <param name="scancode">The physical scancode from the SDL event.</param>
    internal void KeyUp(int scancode)
    {
        if (!IsScancode(scancode))
        {
            return;
        }

        lock (_gate)
        {
            if (!_focused)
            {
                return;
            }

            var word = scancode >> 6;
            var bit = 1UL << (scancode & 63);
            if ((_keyDown[word] & bit) == 0)
            {
                return;
            }

            _keyDown[word] &= ~bit;
            _keyReleased[word] |= bit;
        }
    }

    /// <summary>Applies a mouse button down.</summary>
    /// <param name="buttonIndex">The one-based SDL button index.</param>
    internal void MouseButtonDown(int buttonIndex)
    {
        if (!IsMouseButton(buttonIndex))
        {
            return;
        }

        var bit = (byte)(1 << (buttonIndex - 1));
        lock (_gate)
        {
            if (!_focused)
            {
                return;
            }

            if ((_mouseDown & bit) != 0)
            {
                return;
            }

            _mouseDown |= bit;
            _mousePressed |= bit;
        }
    }

    /// <summary>Applies a mouse button up.</summary>
    /// <param name="buttonIndex">The one-based SDL button index.</param>
    internal void MouseButtonUp(int buttonIndex)
    {
        if (!IsMouseButton(buttonIndex))
        {
            return;
        }

        var bit = (byte)(1 << (buttonIndex - 1));
        lock (_gate)
        {
            if (!_focused)
            {
                return;
            }

            if ((_mouseDown & bit) == 0)
            {
                return;
            }

            _mouseDown &= (byte)~bit;
            _mouseReleased |= bit;
        }
    }

    /// <summary>Stores the mouse position in window points.</summary>
    /// <param name="x">The x coordinate in window points.</param>
    /// <param name="y">The y coordinate in window points.</param>
    internal void MouseMotion(int x, int y)
    {
        lock (_gate)
        {
            if (!_focused)
            {
                return;
            }

            _mouseX = x;
            _mouseY = y;
        }
    }

    /// <summary>Accumulates one vertical wheel event.</summary>
    /// <param name="y">The SDL vertical wheel delta.</param>
    /// <param name="isFlipped">Whether SDL reported the natural (flipped) direction.</param>
    internal void MouseWheel(int y, bool isFlipped)
    {
        lock (_gate)
        {
            if (!_focused)
            {
                return;
            }

            _wheel += isFlipped ? -y : y;
        }
    }

    /// <summary>Queues the characters of one committed text input event.</summary>
    /// <param name="text">The decoded UTF-16 text; a surrogate pair is two characters.</param>
    internal void Text(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_gate)
        {
            if (!_focused)
            {
                return;
            }

            for (var i = 0; i < text.Length; i++)
            {
                _text.Add(text[i]);
            }
        }
    }

    /// <summary>Synthesizes releases, clears transient state, and marks the next delta zero.</summary>
    internal void FocusLost()
    {
        lock (_gate)
        {
            for (var word = 0; word < KeyWordCount; word++)
            {
                _keyReleased[word] |= _keyDown[word];
                _keyDown[word] = 0;
            }

            _mouseReleased |= _mouseDown;
            _mouseDown = 0;
            _wheel = 0f;
            _text.Clear();
            _focused = false;
            _zeroDelta = true;
        }
    }

    /// <summary>Re-seeds held state and position from SDL without creating press edges.</summary>
    /// <param name="keyboardState">
    /// One byte per physical scancode, as returned by <c>SDL_GetKeyboardState</c>; may be shorter than
    /// the full scancode set.
    /// </param>
    /// <param name="mouse">The global SDL mouse state in window points.</param>
    internal void FocusGained(ReadOnlySpan<byte> keyboardState, SdlMouseState mouse)
    {
        lock (_gate)
        {
            Array.Clear(_keyDown);
            var count = Math.Min(keyboardState.Length, ScancodeCount);
            for (var scancode = 1; scancode < count; scancode++)
            {
                if (keyboardState[scancode] != 0 && IsScancode(scancode))
                {
                    _keyDown[scancode >> 6] |= 1UL << (scancode & 63);
                }
            }

            // SDL's mouse mask already uses the one-based button index minus one as its bit index,
            // so the five supported buttons are exactly its low five bits.
            _mouseDown = (byte)(mouse.Buttons & MouseMask);
            _mouseX = mouse.X;
            _mouseY = mouse.Y;
            _focused = true;
            _zeroDelta = true;
        }
    }

    /// <summary>Freezes the accumulated state into <paramref name="target"/> and clears pending edges.</summary>
    /// <param name="target">The preallocated snapshot instance to fill.</param>
    /// <param name="scale">The framebuffer-pixels-per-window-point scale for the mouse position.</param>
    /// <remarks>
    /// The lock covers only the copy, the text transfer, and the focus/delta flag reads. The delta and
    /// previous-position baseline are computed after the copy, on the tick thread. The path allocates
    /// nothing when no text is pending.
    /// </remarks>
    internal void Freeze(InputSnapshot target, float scale)
    {
        Vector2 position;
        bool focused;
        bool zeroDelta;
        lock (_gate)
        {
            Array.Copy(_keyDown, target.KeyDown, KeyWordCount);
            Array.Copy(_keyPressed, target.KeyPressed, KeyWordCount);
            Array.Copy(_keyReleased, target.KeyReleased, KeyWordCount);
            Array.Clear(_keyPressed);
            Array.Clear(_keyReleased);

            target.MouseDown = _mouseDown;
            target.MousePressed = _mousePressed;
            target.MouseReleased = _mouseReleased;
            _mousePressed = 0;
            _mouseReleased = 0;

            position = new Vector2(_mouseX * scale, _mouseY * scale);
            target.Position = position;
            target.Wheel = _wheel;
            _wheel = 0f;

            target.TextLength = 0;
            if (_text.Count > 0)
            {
                CopyText(target);
                _text.Clear();
            }

            focused = _focused;
            zeroDelta = _zeroDelta;
            _zeroDelta = false;
        }

        target.Delta = !focused || zeroDelta || !_hasFrozen
            ? default
            : position - _previousPosition;
        _previousPosition = position;
        _hasFrozen = true;
    }

    private void CopyText(InputSnapshot target)
    {
        var count = _text.Count;
        if (target.Text.Length < count)
        {
            target.Text = new char[Math.Max(count, 32)];
        }

        _text.CopyTo(target.Text);
        target.TextLength = count;
    }

    private static bool IsScancode(int scancode)
        => scancode != 0
            && (uint)scancode < ScancodeCount
            && (_definedScancodes[scancode >> 6] & (1UL << (scancode & 63))) != 0;

    private static bool IsMouseButton(int buttonIndex) => (uint)(buttonIndex - 1) < 5u;
}
