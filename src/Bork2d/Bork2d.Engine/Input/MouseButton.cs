namespace Bork2d.Engine.Input;

/// <summary>Identifies one of the five mouse buttons the engine reports.</summary>
/// <remarks>
/// <para>
/// The numeric values equal the one-based SDL2 button index, so they also identify the button's bit
/// in SDL's pressed-button mask (<c>1 &lt;&lt; (index - 1)</c>): <c>Left</c> is SDL index 1 (bit 0),
/// <c>Middle</c> is index 2 (bit 1), <c>Right</c> is index 3 (bit 2), <c>Back</c> (X1) is index 4
/// (bit 3), and <c>Forward</c> (X2) is index 5 (bit 4). The order of the members follows the public
/// contract, not the numeric order.
/// </para>
/// <para>
/// The values are unstable across builds and must never be serialized or persisted; compare
/// members instead. Any value that is not one of the five members, including the default value
/// <c>(MouseButton)0</c>, is an undefined query: the <see cref="IInput"/> query methods return
/// <see langword="false"/> for it and never throw. Buttons other than these five are ignored.
/// </para>
/// </remarks>
public enum MouseButton
{
    /// <summary>The left button: SDL index 1, mask bit 0.</summary>
    Left = 1,

    /// <summary>The right button: SDL index 3, mask bit 2.</summary>
    Right = 3,

    /// <summary>The middle button: SDL index 2, mask bit 1.</summary>
    Middle = 2,

    /// <summary>The first extra button (X1): SDL index 4, mask bit 3.</summary>
    Back = 4,

    /// <summary>The second extra button (X2): SDL index 5, mask bit 4.</summary>
    Forward = 5,
}
