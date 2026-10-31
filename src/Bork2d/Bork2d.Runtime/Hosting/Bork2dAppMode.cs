namespace Bork2d.Runtime.Hosting;

/// <summary>
/// The host mode selected by <see cref="Bork2dAppBuilder{TLayer}"/>.
/// </summary>
internal enum Bork2dAppMode
{
    /// <summary>Windowed mode; the default.</summary>
    Windowed = 0,

    /// <summary>Headless mode; no window, GPU, or render thread.</summary>
    Headless = 1,

    /// <summary>Offscreen mode; windowless rendering.</summary>
    Offscreen = 2,
}
