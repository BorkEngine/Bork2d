using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Project;
using Bork2d.Bake.Sources;

namespace Bork2d.Bake.Hosting;

/// <summary>
/// Checks that a generated host's type manifest matches the types declared by the project's sources.
/// </summary>
/// <remarks>
/// D-M4-13 moves registration into each custom-data TOML's <c>[bork2d].type</c>. The host is generated
/// from the distinct declarations, so a host generated from an older source set, or one that predates
/// a type/path edit, would bind the wrong types or fail halfway through a bake. The check is explicit
/// and reports every missing and every extra registration so the fix (regenerate the host) is
/// obvious. Missing declarations point at the source <c>type</c> value; extra registrations point at
/// the descriptor.
/// </remarks>
internal static class BakeHostManifestValidator
{
    /// <summary>Validates a host manifest against discovered source declarations.</summary>
    /// <param name="descriptor">The parsed project descriptor, used for descriptor-level diagnostics.</param>
    /// <param name="sources">The discovered sources with their declared types.</param>
    /// <param name="manifest">The loaded host manifest.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns><see langword="true"/> when every declared type is present and no extra registration exists.</returns>
    internal static bool Validate(
        ProjectDescriptor descriptor,
        IReadOnlyList<BakeSource> sources,
        IBakeManifest manifest,
        BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(diagnostics);

        IReadOnlyList<BakeTypeRegistration> registrations = manifest.Types;
        var registrationTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (BakeTypeRegistration registration in registrations)
        {
            registrationTypes.Add(registration.TypeName);
        }

        var declaredTypes = new HashSet<string>(StringComparer.Ordinal);
        var reportedMissing = new HashSet<string>(StringComparer.Ordinal);
        bool valid = true;

        foreach (BakeSource source in sources)
        {
            declaredTypes.Add(source.TypeName);
            if (registrationTypes.Contains(source.TypeName)
                || !reportedMissing.Add(source.TypeName))
            {
                continue;
            }

            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.HostStale,
                $"The generated bake host is stale: it has no MessagePack registration for "
                    + $"'{source.TypeName}' declared by '{source.RelativePath}'. Run "
                    + "'bork2d-bake generate-host' and rebuild the host.",
                BakeDiagnosticPath.ForSource(descriptor.DescriptorDirectory, source.FullPath),
                source.TypeLine,
                source.TypeColumn,
                assetPath: source.AssetPath);
            valid = false;
        }

        foreach (BakeTypeRegistration registration in registrations)
        {
            if (declaredTypes.Contains(registration.TypeName))
            {
                continue;
            }

            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.HostStale,
                $"The generated bake host is stale: it registers '{registration.TypeName}', which "
                    + "no source declares. Run 'bork2d-bake generate-host' and rebuild the host.",
                descriptor.DescriptorPath);
            valid = false;
        }

        return valid;
    }
}
