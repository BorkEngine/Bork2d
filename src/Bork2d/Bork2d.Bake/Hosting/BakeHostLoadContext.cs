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
/// Shared assemblies (<c>Bork2d.*</c>, <c>Tomlyn</c>, <c>PolyType</c>, <c>Nerdbank.*</c>,
/// <c>System.*</c>, <c>Microsoft.*</c>) always resolve to the default context, so
/// <see cref="IBakeManifest"/>, <see cref="PackBuilder"/>, the API asset types, and PolyType shapes
/// keep one identity. The host assembly and every non-shared assembly (<c>Project.Common</c>, a Q-04
/// game data library) load from the host directory.
/// </para>
/// <para>
/// A shared assembly that the default context cannot resolve throws
/// <see cref="BakeHostLoadException"/> naming it, so the CLI reports a <c>BK1xxx</c> diagnostic
/// instead of a raw loader failure.
/// </para>
/// </remarks>
internal sealed class BakeHostLoadContext : AssemblyLoadContext
{
    private static readonly string[] _sharedPrefixes =
    [
        "Bork2d.",
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

        if (IsShared(name))
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

    private static bool IsShared(string name)
    {
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
