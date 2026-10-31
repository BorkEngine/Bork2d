using System.Numerics;

namespace Bork2d.Engine.Input;

/// <summary>Reads keyboard, mouse, wheel, and text input for the current tick.</summary>
/// <remarks>
/// <para>
/// Input is available only in windowed mode. Offscreen and headless builds throw
/// <see cref="NotSupportedException"/> from the engine's <c>Input</c> getter; after windowed shutdown
/// a retained <see cref="IInput"/> reference throws <see cref="ObjectDisposedException"/> from every
/// member. Before the first tick, reads are legal and return an empty snapshot: every query is
/// <see langword="false"/> and every position, delta, wheel, and text value is zero.
/// </para>
/// <para>
/// The platform freezes input at tick start, so every read during one <c>Tick</c> returns the same
/// values; events that arrive during <c>Tick</c> belong to the next freeze. <see cref="IsKeyPressed"/>
/// and <see cref="IsKeyReleased"/> (and the mouse equivalents) are true for exactly one tick. A press
/// and release inside one interval report both edges with <see cref="IsKeyDown"/> false for the key
/// (and <see cref="IsMouseDown"/> false for the button). Key repeat produces no press edge, and the
/// engine never synthesizes text from key presses.
/// </para>
/// <para>
/// Focus loss synthesizes a release for every held key and button, clears held state, wheel, and
/// pending text, and reports a zero <see cref="MouseDelta"/>. Focus regain re-seeds held state and
/// position from the operating system without creating press edges and also reports a zero delta.
/// </para>
/// <para>
/// <see cref="KeyCode"/> and <see cref="MouseButton"/> map the physical devices, not characters or
/// layout-dependent keys; undefined values are safe queries that return <see langword="false"/>.
/// Mouse coordinates are framebuffer physical pixels with a top-left origin, not logical points and
/// not world units. Input is not part of the frame snapshot and is never exposed to a render thread.
/// </para>
/// </remarks>
public interface IInput
{
    /// <summary>Raised for each committed UTF-16 character of operating-system text input.</summary>
    /// <remarks>
    /// Events are raised on the engine tick thread, in arrival order, before the tick's
    /// <c>IGame.Tick</c> callback. A surrogate pair is delivered as two separate <see cref="char"/>
    /// values; malformed encoded text is replaced by U+FFFD. Adding or removing a handler is
    /// thread-safe. An exception from a handler is treated like a tick failure.
    /// </remarks>
    event Action<char> OnTextTyped;

    /// <summary>Returns whether <paramref name="key"/> went down since the previous freeze.</summary>
    /// <param name="key">The physical key to query.</param>
    /// <returns><see langword="true"/> for exactly one tick after an up-to-down transition.</returns>
    bool IsKeyPressed(KeyCode key);

    /// <summary>Returns whether <paramref name="key"/> went up since the previous freeze.</summary>
    /// <param name="key">The physical key to query.</param>
    /// <returns><see langword="true"/> for exactly one tick after a down-to-up transition.</returns>
    bool IsKeyReleased(KeyCode key);

    /// <summary>Returns whether <paramref name="key"/> is held at this freeze.</summary>
    /// <param name="key">The physical key to query.</param>
    /// <returns><see langword="true"/> while the key is held.</returns>
    bool IsKeyDown(KeyCode key);

    /// <summary>Gets the mouse position in framebuffer physical pixels, top-left origin.</summary>
    /// <remarks>
    /// Starts at <c>(0,0)</c> until the first mouse event, keeps its last value while unfocused, and
    /// is re-synchronized when focus returns. This is not a logical-point or world-space position.
    /// </remarks>
    Vector2 MousePosition { get; }

    /// <summary>Gets the mouse movement since the previous freeze in framebuffer pixels.</summary>
    /// <remarks>
    /// Zero on the first freeze, the focus-loss freeze, the focus-regain freeze, and while the window
    /// is unfocused; otherwise the difference between the current and the previous freeze position.
    /// </remarks>
    Vector2 MouseDelta { get; }

    /// <summary>Gets the accumulated vertical wheel movement since the previous freeze.</summary>
    /// <remarks>
    /// Positive means up/away from the user, independent of the operating system's natural-scrolling
    /// setting. The value is reset after each freeze and cleared on focus loss.
    /// </remarks>
    float MouseWheelDelta { get; }

    /// <summary>Returns whether <paramref name="button"/> went down since the previous freeze.</summary>
    /// <param name="button">The mouse button to query.</param>
    /// <returns><see langword="true"/> for exactly one tick after an up-to-down transition.</returns>
    bool IsMousePressed(MouseButton button);

    /// <summary>Returns whether <paramref name="button"/> went up since the previous freeze.</summary>
    /// <param name="button">The mouse button to query.</param>
    /// <returns><see langword="true"/> for exactly one tick after a down-to-up transition.</returns>
    bool IsMouseReleased(MouseButton button);

    /// <summary>Returns whether <paramref name="button"/> is held at this freeze.</summary>
    /// <param name="button">The mouse button to query.</param>
    /// <returns><see langword="true"/> while the button is held.</returns>
    bool IsMouseDown(MouseButton button);
}
