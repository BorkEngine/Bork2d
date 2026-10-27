using System.Reflection;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// Identifies the bake tool version recorded in the incremental manifest.
/// </summary>
/// <remarks>
/// The version is the <see cref="AssemblyInformationalVersionAttribute"/> of the
/// <c>Bork2d.Bake</c> assembly, falling back to the assembly version and then to <c>0.0.0</c>.
/// A tool change therefore invalidates every manifest written by an earlier build.
/// </remarks>
internal static class BakeToolInfo
{
    /// <summary>Gets the bake tool version.</summary>
    internal static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        Assembly assembly = typeof(BakeToolInfo).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
