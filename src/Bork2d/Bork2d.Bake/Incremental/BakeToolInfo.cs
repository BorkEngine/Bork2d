using System.Reflection;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// The bake tool identity recorded by the incremental manifest and the host-generation state.
/// </summary>
/// <remarks>
/// The version is the tooling assembly's informational version (which includes the pinned SDK's
/// source revision when available). A tool upgrade therefore invalidates both skips, so a manifest or
/// state file written by an older tool is never trusted.
/// </remarks>
internal static class BakeToolInfo
{
    /// <summary>Gets the bake tool version.</summary>
    internal static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        Assembly assembly = typeof(BakeToolInfo).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }
}
