using Microsoft.Extensions.Logging;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The explicit request that creates one internal Vulkan instance.
/// </summary>
/// <remarks>
/// The request is the only way to construct the production instance: required extensions are
/// supplied by the caller (SDL's list windowed, empty offscreen), validation is requested rather
/// than assumed, and the logger factory determines where validation and selection messages go.
/// </remarks>
internal sealed class VulkanInstanceRequest
{
    /// <summary>
    /// Gets the instance extensions the caller requires (SDL's list windowed, empty offscreen).
    /// </summary>
    internal IReadOnlyList<string> RequiredExtensions { get; init; } = Array.Empty<string>();

    /// <summary>Gets a value indicating whether the Khronos validation layer is requested.</summary>
    internal bool ValidationRequested { get; init; }

    /// <summary>
    /// Gets the application name, or <see langword="null"/> to default the entry assembly name.
    /// </summary>
    internal string? ApplicationName { get; init; }

    /// <summary>
    /// Gets the engine name, or <see langword="null"/> to use the Bork2d default.
    /// </summary>
    internal string? EngineName { get; init; }

    /// <summary>Gets the factory used to create the <c>Bork2d.Render</c> category logger.</summary>
    internal required ILoggerFactory LoggerFactory { get; init; }
}
