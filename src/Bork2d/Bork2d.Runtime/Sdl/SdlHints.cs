namespace Bork2d.Runtime.Sdl;

/// <summary>
/// The SDL hint names and values the windowed platform needs.
/// </summary>
/// <remarks>
/// Hints must be set before <see cref="ISdlApi.InitializeVideo"/>; SDL reads several of them once
/// during subsystem initialization. The windowed platform requests per-monitor-v2 DPI awareness
/// and enables DPI scaling so window sizes and mouse coordinates are reported in window points
/// while the framebuffer size stays in physical pixels (D-M2-10).
/// </remarks>
internal static class SdlHints
{
    /// <summary><c>SDL_WINDOWS_DPI_AWARENESS</c>: the process DPI awareness mode to request.</summary>
    internal const string WindowsDpiAwareness = "SDL_WINDOWS_DPI_AWARENESS";

    /// <summary><c>SDL_WINDOWS_DPI_SCALING</c>: whether SDL exposes window sizes in points.</summary>
    internal const string WindowsDpiScaling = "SDL_WINDOWS_DPI_SCALING";

    /// <summary>The per-monitor-v2 value for <see cref="WindowsDpiAwareness"/>.</summary>
    internal const string PerMonitorV2 = "permonitorv2";

    /// <summary>The enabled value for <see cref="WindowsDpiScaling"/>.</summary>
    internal const string DpiScalingEnabled = "1";
}
