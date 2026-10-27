using System.Reflection;
using System.Runtime.Loader;
using Bork2d.Api.Assets;
using Bork2d.Assets.Pack;

namespace Bork2d.Bake.Hosting;

/// <summary>
/// The non-collectible load context that isolates the generated host from the tooling process.
/// </summary>
/// <remarks>
/// <para>
/// The tooling assemblies (<c>Bork2d.Api</c>, <c>Bork2d.Assets</c>, <c>Bork2d.Bake</c>) and the
/// tooling package/runtime assemblies (<c>Tomlyn</c>, <c>PolyType</c>, <c>Nerdbank.*</c>,
/// <c>System.*</c>, <c>Microsoft.*</c>) always resolve to the default context, so
/// <see cref="IBakeManifest"/>, <see cref="PackBuilder"/>, the API asset types, and PolyType shapes
/// keep one identity. The host assembly and every game assembly (<c>Project.Common</c>, a Q-04 game
/// data library, or a fixture named <c>Bork2d.*</c>) load from the host directory.
/// </para>
/// <para>
/// A shared assembly that the default context cannot resolve throws
/// <see cref="BakeHostLoadException"/> naming it, so the CLI reports a <c>BK1xxx</c> diagnostic
/// instead of a raw loader failure.
/// </para>
/// </remarks>
internal sealed class BakeHostLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> _sharedAssemblyNames = new(StringComparer.Ordinal)
    {
        "Bork2d.Api",
        "Bork2d.Assets",
        "Bork2d.Bake",
    };

    private static readonly string[] _sharedPrefixes =
    [
        "Microsoft.",
        "Nerdbank.",
        "PolyType",
        "System.",
        "Tomlyn",
    ];

    private readonly string _hostDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="BakeHostLoadContext"/> class.
    /// </summary>
    /// <param name="hostDirectory">The directory that contains the host and its game assemblies.</param>
    internal BakeHostLoadContext(string hostDirectory)
        : base(BakeHostIdentity.AssemblyName, isCollectible: false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostDirectory);

        _hostDirectory = hostDirectory;
    }

    /// <inheritdoc/>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);

        string? name = assemblyName.Name;
        if (name is null)
        {
            return null;
        }

        if (string.Equals(name, BakeHostIdentity.AssemblyName, StringComparison.Ordinal))
        {
            return LoadHostFile(name);
        }

        if (IsToolingAssembly(name))
        {
            return ResolveShared(name);
        }

        string candidate = Path.Combine(_hostDirectory, name + ".dll");
        if (File.Exists(candidate))
        {
            return LoadFromAssemblyPath(candidate);
        }

        return ResolveShared(name);
    }

    /// <summary>
    /// Tests whether an assembly name belongs to the tooling (and therefore always resolves from the
    /// default context).
    /// </summary>
    /// <param name="name">The simple assembly name.</param>
    /// <returns><see langword="true"/> for an engine/tooling or tooling-package assembly.</returns>
    /// <remarks>
    /// The incremental bake reuses this rule to identify the game-side assembly closure next to the
    /// host (everything that is not tooling), so the two boundaries cannot drift.
    /// </remarks>
    internal static bool IsToolingAssembly(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_sharedAssemblyNames.Contains(name))
        {
            return true;
        }

        foreach (string prefix in _sharedPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Assembly ResolveShared(string name)
    {
        Assembly? loaded = Default.Assemblies.FirstOrDefault(
            assembly => string.Equals(assembly.GetName().Name, name, StringComparison.Ordinal));
        if (loaded is not null)
        {
            return loaded;
        }

        try
        {
            return Default.LoadFromAssemblyName(new AssemblyName(name));
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException)
        {
            throw new BakeHostLoadException(
                $"The bake tooling could not resolve the shared assembly '{name}' from its own "
                    + $"dependency graph: {exception.Message}",
                exception);
        }
    }

    private Assembly LoadHostFile(string name)
    {
        string candidate = Path.Combine(_hostDirectory, name + ".dll");
        if (!File.Exists(candidate))
        {
            throw new BakeHostLoadException(
                $"The generated bake host assembly '{candidate}' does not exist.");
        }

        return LoadFromAssemblyPath(candidate);
    }
}
