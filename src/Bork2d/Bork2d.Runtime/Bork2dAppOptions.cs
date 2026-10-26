using System.Reflection;
using Bork2d.Api;
using Microsoft.Extensions.Logging;

namespace Bork2d.Runtime;

/// <summary>
/// The mutable configuration collected by <see cref="Bork2dAppBuilder{TLayer}"/> and validated
/// by <see cref="Bork2dAppBuilder{TLayer}.Build"/>.
/// </summary>
/// <remarks>
/// The host consumes the game, the mode, the window options, the maximum delta time, the asset
/// selection, and the internal SDL factory seam. The remaining options are stored and validated now
/// so the M6 composition can consume them without changing the public builder surface.
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class Bork2dAppOptions<TLayer> where TLayer : struct, Enum
{
    internal Bork2dAppOptions()
    {
        Title = GetDefaultTitle();
        Validation = DefaultValidation();
    }

    /// <summary>Gets or sets the selected host mode.</summary>
    internal Bork2dAppMode Mode { get; set; } = Bork2dAppMode.Windowed;

    /// <summary>Gets or sets a value indicating whether <c>WithHeadless</c> was called.</summary>
    internal bool HeadlessSelected { get; set; }

    /// <summary>Gets or sets a value indicating whether <c>WithOffscreen</c> was called.</summary>
    internal bool OffscreenSelected { get; set; }

    /// <summary>Gets or sets the caller-owned game instance.</summary>
    internal IGame<TLayer>? Game { get; set; }

    /// <summary>Gets or sets the number of <c>WithGame</c> calls.</summary>
    internal int GameSetCount { get; set; }

    /// <summary>Gets or sets the base assets pack path.</summary>
    internal string AssetsPackPath { get; set; } = "assets.pck";

    /// <summary>Gets or sets a value indicating whether <c>WithAssetsPack</c> was called.</summary>
    /// <remarks>
    /// The default pack path is always present, so this flag distinguishes an explicit pack choice
    /// from the default when validating the <c>WithoutAssets</c> conflict.
    /// </remarks>
    internal bool AssetsPackSelected { get; set; }

    /// <summary>Gets or sets a value indicating whether <c>WithoutAssets</c> was called.</summary>
    internal bool WithoutAssetsSelected { get; set; }

    /// <summary>
    /// Gets or sets the logging configuration accepted by <c>WithLogging</c>.
    /// </summary>
    /// <remarks>
    /// When set, <c>Build</c> invokes it exactly once instead of installing the default ZLogger
    /// console provider.
    /// </remarks>
    internal Action<ILoggingBuilder>? ConfigureLogging { get; set; }

    /// <summary>Gets or sets the window title, defaulting to the entry assembly name.</summary>
    internal string Title { get; set; }

    /// <summary>Gets or sets the windowed framebuffer width.</summary>
    internal int Width { get; set; } = 1280;

    /// <summary>Gets or sets the windowed framebuffer height.</summary>
    internal int Height { get; set; } = 720;

    /// <summary>Gets or sets a value indicating whether the window is resizable.</summary>
    internal bool Resizable { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the window starts fullscreen.</summary>
    internal bool Fullscreen { get; set; }

    /// <summary>Gets or sets a value indicating whether vertical sync is preferred.</summary>
    internal bool VSync { get; set; } = true;

    /// <summary>Gets or sets the headless ticks per second; 0 means uncapped.</summary>
    internal int HeadlessTicksPerSecond { get; set; } = 60;

    /// <summary>Gets or sets the offscreen target width.</summary>
    internal int OffscreenWidth { get; set; }

    /// <summary>Gets or sets the offscreen target height.</summary>
    internal int OffscreenHeight { get; set; }

    /// <summary>Gets or sets the gameplay delta-time clamp in seconds.</summary>
    internal double MaxDeltaTime { get; set; } = 0.25;

    /// <summary>Gets or sets a value indicating whether GPU validation layers are requested.</summary>
    internal bool Validation { get; set; }

    /// <summary>
    /// Gets or sets the internal test seam for creating the SDL adapter on the OS/window thread.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> means production: the runtime creates a <see cref="SilkSdlApi"/> on
    /// the OS thread. Tests supply a fake so the real windowed loop and platform run without a
    /// desktop session or the native library. This member is internal and not part of the public
    /// builder surface.
    /// </remarks>
    internal Func<ISdlApi>? SdlApiFactory { get; set; }

    /// <summary>Gets or sets the window-only options that were explicitly set.</summary>
    internal Bork2dWindowOptionFlags WindowOptionsSet { get; set; }

    private static string GetDefaultTitle()
        => Assembly.GetEntryAssembly()?.GetName().Name ?? "Bork2d";

    private static bool DefaultValidation()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }
}
