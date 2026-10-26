using System.Reflection;
using Bork2d.Api.Assets;
using Bork2d.Assets.Pack;
using Bork2d.Bake.Diagnostics;

namespace Bork2d.Bake.Hosting;

/// <summary>
/// Loads a generated bake host in a non-collectible, shared-identity load context and asserts its
/// contract and type identities.
/// </summary>
/// <remarks>
/// Every expected failure (missing file, bad image, missing manifest, type-identity mismatch,
/// unresolvable shared assembly) becomes a <c>BK1xxx</c> diagnostic; no loader or instantiation
/// exception escapes to the CLI as a crash.
/// </remarks>
internal static class BakeHostLoader
{
    /// <summary>Loads and validates a generated bake host.</summary>
    /// <param name="hostAssemblyPath">The host assembly path.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The loaded host, or <see langword="null"/> when a diagnostic failed it.</returns>
    internal static BakeHostHandle? TryLoad(string hostAssemblyPath, BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(hostAssemblyPath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(hostAssemblyPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.HostAssemblyMissing,
                $"Invalid bake host path '{hostAssemblyPath}': {exception.Message}",
                hostAssemblyPath);
            return null;
        }

        if (!File.Exists(fullPath))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.HostAssemblyMissing,
                $"The generated bake host '{fullPath}' does not exist; run "
                    + "'bork2d-bake generate-host' and build the generated project.",
                fullPath);
            return null;
        }

        string directory = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory;
        var loadContext = new BakeHostLoadContext(directory);

        try
        {
            Assembly host = loadContext.LoadFromAssemblyPath(fullPath);
            if (!string.Equals(
                host.GetName().Name,
                BakeHostIdentity.AssemblyName,
                StringComparison.Ordinal))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.HostContractInvalid,
                    $"The bake host assembly is named '{host.GetName().Name}'; the contract requires "
                        + $"'{BakeHostIdentity.AssemblyName}'.",
                    fullPath);
                return null;
            }

            BakeManifestAttribute? attribute = host
                .GetCustomAttributes<BakeManifestAttribute>()
                .FirstOrDefault();
            if (attribute is null)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.HostContractInvalid,
                    $"The bake host assembly '{fullPath}' has no "
                        + "[assembly: BakeManifest(typeof(...))] attribute.",
                    fullPath);
                return null;
            }

            Type manifestType = attribute.ManifestType;
            if (!typeof(IBakeManifest).IsAssignableFrom(manifestType))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.HostContractInvalid,
                    $"The bake host manifest type '{manifestType.FullName}' does not implement "
                        + "Bork2d.Bake.Hosting.IBakeManifest.",
                    fullPath);
                return null;
            }

            if (Activator.CreateInstance(manifestType, nonPublic: true) is not IBakeManifest manifest)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.HostContractInvalid,
                    $"The bake host manifest type '{manifestType.FullName}' could not be created.",
                    fullPath);
                return null;
            }

            bool identitiesValid = true;
            identitiesValid &= AssertSharedIdentity(
                loadContext,
                "Bork2d.Assets",
                typeof(PackBuilder).Assembly,
                fullPath,
                diagnostics);
            identitiesValid &= AssertSharedIdentity(
                loadContext,
                "Bork2d.Api",
                typeof(IAssets).Assembly,
                fullPath,
                diagnostics);

            if (!identitiesValid)
            {
                return null;
            }

            int beforeShapes = diagnostics.Count;
            manifest.ValidateShapes(diagnostics);
            if (diagnostics.Count != beforeShapes)
            {
                return null;
            }

            return new BakeHostHandle(loadContext, host, manifest);
        }
        catch (BakeHostLoadException exception)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.HostLoadFailed,
                exception.Message,
                fullPath);
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.HostLoadFailed,
                $"Failed to load the bake host '{fullPath}': {exception.Message}",
                fullPath);
            return null;
        }
    }

    private static bool AssertSharedIdentity(
        BakeHostLoadContext loadContext,
        string assemblyName,
        Assembly expected,
        string hostPath,
        BakeDiagnostics diagnostics)
    {
        Assembly resolved;
        try
        {
            resolved = loadContext.LoadFromAssemblyName(new AssemblyName(assemblyName));
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException
            or BakeHostLoadException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.HostLoadFailed,
                $"The bake host '{hostPath}' cannot resolve '{assemblyName}': {exception.Message}",
                hostPath);
            return false;
        }

        if (!ReferenceEquals(resolved, expected))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.HostTypeIdentityMismatch,
                $"The bake host resolved '{assemblyName}' to a different assembly instance than the "
                    + "tooling; the host must share the tooling's Bork2d assemblies. Regenerate and "
                    + "rebuild the host against this tooling build.",
                hostPath);
            return false;
        }

        return true;
    }
}
