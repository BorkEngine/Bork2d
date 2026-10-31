namespace Bork2d.Runtime.Sdl;

/// <summary>
/// A window dimension pair, used for both window points and framebuffer pixels.
/// </summary>
/// <remarks>
/// The seam keeps the two units in separate <see cref="ISdlWindow"/> members so size bugs surface as
/// a unit mismatch rather than as a silent scale error.
/// </remarks>
internal readonly struct SdlWindowSize
{
    /// <summary>Initializes a new instance of the <see cref="SdlWindowSize"/> struct.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    internal SdlWindowSize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Gets the width.</summary>
    internal int Width { get; }

    /// <summary>Gets the height.</summary>
    internal int Height { get; }

    /// <summary>Gets a value indicating whether either component is not positive.</summary>
    internal bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Returns the size as <c>widthxheight</c>.</summary>
    /// <returns>The formatted size.</returns>
    public override string ToString() => $"{Width}x{Height}";
}
