namespace Bork2d.Bake.Hosting;

/// <summary>
/// The fixed identity of the generated bake-host assembly.
/// </summary>
/// <remarks>
/// The host is generated into <c>obj/bork2d/bakehost/</c> and is not a solution project. The fixed
/// name is the anchor for <c>InternalsVisibleTo</c> grants and for the non-collectible
/// <c>AssemblyLoadContext</c> the CLI uses to load it (WP2).
/// </remarks>
internal static class BakeHostIdentity
{
    /// <summary>The generated host's assembly name.</summary>
    internal const string AssemblyName = "Bork2d.BakeHost";
}
