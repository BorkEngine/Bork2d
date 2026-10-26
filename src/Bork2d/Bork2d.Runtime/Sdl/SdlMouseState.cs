namespace Bork2d.Runtime.Sdl;

/// <summary>
/// The SDL global mouse state: position in window points and the pressed-button mask.
/// </summary>
/// <remarks>
/// The mask uses the SDL convention where bit <c>button - 1</c> is set for the one-based button
/// index (<c>SDL_BUTTON_LEFT</c> is 1). The M2 input accumulator maps the five supported buttons
/// from this mask when it re-seeds held state after focus regain.
/// </remarks>
internal readonly struct SdlMouseState
{
    /// <summary>Initializes a new instance of the <see cref="SdlMouseState"/> struct.</summary>
    /// <param name="x">The x position in window points.</param>
    /// <param name="y">The y position in window points.</param>
    /// <param name="buttons">The pressed-button mask.</param>
    internal SdlMouseState(int x, int y, uint buttons)
    {
        X = x;
        Y = y;
        Buttons = buttons;
    }

    /// <summary>Gets the x position in window points.</summary>
    internal int X { get; }

    /// <summary>Gets the y position in window points.</summary>
    internal int Y { get; }

    /// <summary>Gets the pressed-button mask.</summary>
    internal uint Buttons { get; }
}
