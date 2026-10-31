namespace Bork2d.Render.Vulkan;

/// <summary>
/// Packs and formats Vulkan API versions without touching the Vulkan loader.
/// </summary>
internal static class VulkanVersion
{
    /// <summary>The packed Vulkan 1.3.0 version, the minimum M6 requires.</summary>
    internal const uint ApiVersion13 = (1u << 22) | (3u << 12);

    /// <summary>Packs a major/minor/patch version the same way as <c>VK_MAKE_VERSION</c>.</summary>
    internal static uint Make(uint major, uint minor, uint patch)
        => (major << 22) | (minor << 12) | patch;

    /// <summary>Gets the major component of a packed version.</summary>
    internal static uint Major(uint version) => version >> 22;

    /// <summary>Gets the minor component of a packed version.</summary>
    internal static uint Minor(uint version) => (version >> 12) & 0x3FF;

    /// <summary>Gets the patch component of a packed version.</summary>
    internal static uint Patch(uint version) => version & 0xFFF;

    /// <summary>Formats a packed version as <c>major.minor.patch</c>.</summary>
    internal static string Format(uint version)
        => $"{Major(version)}.{Minor(version)}.{Patch(version)}";
}
