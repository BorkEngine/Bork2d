using Bork2d.Api.Input;

namespace Bork2d.Runtime.Sdl;

/// <summary>
/// One SDL event in the seam's neutral, blittable form.
/// </summary>
/// <remarks>
/// <para>
/// The struct flattens the native <c>SDL_Event</c> union into named members. Only the data the M2
/// platform consumes is carried; every unused member keeps its default value. The type is internal,
/// immutable, and allocation-free except for <see cref="Text"/>, which exists only on text input.
/// </para>
/// <para>
/// Fakes construct events with the static factories, which keeps test data readable and independent
/// of Silk.NET types.
/// </para>
/// </remarks>
internal readonly struct SdlEvent
{
    /// <summary>Gets the event kind.</summary>
    internal SdlEventType Type { get; init; }

    /// <summary>Gets the physical scancode for a key event.</summary>
    internal int Scancode { get; init; }

    /// <summary>Gets a value indicating whether a key event is a key repeat.</summary>
    internal bool IsRepeat { get; init; }

    /// <summary>Gets the mouse x position in window points for a mouse event.</summary>
    internal int MouseX { get; init; }

    /// <summary>Gets the mouse y position in window points for a mouse event.</summary>
    internal int MouseY { get; init; }

    /// <summary>Gets the one-based SDL button index for a mouse button event.</summary>
    internal int ButtonIndex { get; init; }

    /// <summary>Gets the horizontal wheel delta for a wheel event.</summary>
    internal int WheelX { get; init; }

    /// <summary>Gets the vertical wheel delta for a wheel event.</summary>
    internal int WheelY { get; init; }

    /// <summary>Gets a value indicating whether the wheel event used flipped (natural) direction.</summary>
    internal bool IsWheelFlipped { get; init; }

    /// <summary>Gets the new width in window points for a resize window event.</summary>
    internal int Width { get; init; }

    /// <summary>Gets the new height in window points for a resize window event.</summary>
    internal int Height { get; init; }

    /// <summary>Gets the decoded UTF-16 text for a text input event.</summary>
    internal string? Text { get; init; }

    /// <summary>Gets the registered SDL event type for a user event.</summary>
    internal int UserCode { get; init; }

    /// <summary>Creates an event the platform does not consume.</summary>
    /// <returns>An unknown event.</returns>
    internal static SdlEvent Unknown() => new() { Type = SdlEventType.Unknown };

    /// <summary>Creates an <c>SDL_QUIT</c> event.</summary>
    /// <returns>A quit event.</returns>
    internal static SdlEvent Quit() => new() { Type = SdlEventType.Quit };

    /// <summary>Creates a window event.</summary>
    /// <param name="type">A <see cref="SdlEventType"/> window member.</param>
    /// <param name="width">The new width in window points, for resize events.</param>
    /// <param name="height">The new height in window points, for resize events.</param>
    /// <returns>The window event.</returns>
    internal static SdlEvent Window(SdlEventType type, int width = 0, int height = 0)
        => new() { Type = type, Width = width, Height = height };

    /// <summary>Creates a key event.</summary>
    /// <param name="type">Either <see cref="SdlEventType.KeyDown"/> or <see cref="SdlEventType.KeyUp"/>.</param>
    /// <param name="scancode">The physical scancode.</param>
    /// <param name="isRepeat">Whether the key down is a repeat.</param>
    /// <returns>The key event.</returns>
    internal static SdlEvent Key(SdlEventType type, int scancode, bool isRepeat = false)
        => new() { Type = type, Scancode = scancode, IsRepeat = isRepeat };

    /// <summary>Creates a text input event.</summary>
    /// <param name="text">The decoded UTF-16 text.</param>
    /// <returns>The text input event.</returns>
    internal static SdlEvent TextInput(string text)
        => new() { Type = SdlEventType.TextInput, Text = text };

    /// <summary>Creates a mouse motion event.</summary>
    /// <param name="x">The x position in window points.</param>
    /// <param name="y">The y position in window points.</param>
    /// <returns>The mouse motion event.</returns>
    internal static SdlEvent MouseMotion(int x, int y)
        => new() { Type = SdlEventType.MouseMotion, MouseX = x, MouseY = y };

    /// <summary>Creates a mouse button event.</summary>
    /// <param name="buttonIndex">The one-based SDL button index.</param>
    /// <param name="isDown">Whether the button went down.</param>
    /// <param name="x">The x position in window points.</param>
    /// <param name="y">The y position in window points.</param>
    /// <returns>The mouse button event.</returns>
    internal static SdlEvent MouseButton(int buttonIndex, bool isDown, int x, int y)
        => new()
        {
            Type = isDown ? SdlEventType.MouseButtonDown : SdlEventType.MouseButtonUp,
            ButtonIndex = buttonIndex,
            MouseX = x,
            MouseY = y,
        };

    /// <summary>Creates a mouse wheel event.</summary>
    /// <param name="x">The horizontal delta.</param>
    /// <param name="y">The vertical delta.</param>
    /// <param name="isFlipped">Whether the delta uses flipped (natural) direction.</param>
    /// <returns>The mouse wheel event.</returns>
    internal static SdlEvent MouseWheel(int x, int y, bool isFlipped = false)
        => new() { Type = SdlEventType.MouseWheel, WheelX = x, WheelY = y, IsWheelFlipped = isFlipped };

    /// <summary>Creates a registered user event.</summary>
    /// <param name="eventType">The SDL event type returned by <see cref="ISdlApi.RegisterEvents"/>.</param>
    /// <returns>The user event.</returns>
    internal static SdlEvent User(int eventType)
        => new() { Type = SdlEventType.UserEvent, UserCode = eventType };
}
