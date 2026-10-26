using System.Reflection;
using System.Runtime.Loader;

namespace Bork2d.Bake.Hosting;

/// <summary>
/// A loaded, identity-checked generated bake host.
/// </summary>
/// <remarks>
/// The load context is deliberately non-collectible: the host and <c>Project.Common</c> live for the
/// tooling process, while shared engine, TOML, shape, and MessagePack assemblies resolve to the
/// already-loaded default-context instances. A collectible editor context is an M5 concern (Q-08).
/// </remarks>
internal sealed class BakeHostHandle
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BakeHostHandle"/> class.
    /// </summary>
    /// <param name="loadContext">The non-collectible host load context.</param>
    /// <param name="hostAssembly">The loaded <c>Bork2d.BakeHost</c> assembly.</param>
    /// <param name="manifest">The instantiated manifest.</param>
    internal BakeHostHandle(
        AssemblyLoadContext loadContext,
        Assembly hostAssembly,
        IBakeManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(loadContext);
        ArgumentNullException.ThrowIfNull(hostAssembly);
        ArgumentNullException.ThrowIfNull(manifest);

        LoadContext = loadContext;
        HostAssembly = hostAssembly;
        Manifest = manifest;
    }

    /// <summary>Gets the non-collectible host load context.</summary>
    internal AssemblyLoadContext LoadContext { get; }

    /// <summary>Gets the loaded <c>Bork2d.BakeHost</c> assembly.</summary>
    internal Assembly HostAssembly { get; }

    /// <summary>Gets the instantiated manifest.</summary>
    internal IBakeManifest Manifest { get; }
}
