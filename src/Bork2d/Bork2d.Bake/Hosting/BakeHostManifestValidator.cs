using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Project;

namespace Bork2d.Bake.Hosting;

/// <summary>
/// Checks that a generated host's type manifest matches the project descriptor.
/// </summary>
/// <remarks>
/// A host generated from a different descriptor, or one that predates a <c>[[types]]</c> edit, would
/// bind the wrong types or fail halfway through a bake. The check is explicit and reports every
/// missing and every extra registration so the fix (regenerate the host) is obvious.
/// </remarks>
internal static class BakeHostManifestValidator
{
    /// <summary>Validates a host manifest against a descriptor.</summary>
    /// <param name="descriptor">The parsed project descriptor.</param>
    /// <param name="manifest">The loaded host manifest.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns><see langword="true"/> when every mapping is present and no extra registration exists.</returns>
    internal static bool Validate(
        ProjectDescriptor descriptor,
        IBakeManifest manifest,
        BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(diagnostics);

        IReadOnlyList<BakeTypeRegistration> registrations = manifest.Types;
        bool valid = true;

        foreach (ProjectTypeMapping mapping in descriptor.Types)
        {
            bool found = false;
            foreach (BakeTypeRegistration registration in registrations)
            {
                if (string.Equals(registration.SourcePrefix, mapping.Prefix, StringComparison.Ordinal)
                    && string.Equals(registration.TypeName, mapping.TypeName, StringComparison.Ordinal)
                    && registration.Kind == BakeAssetKind.MessagePack)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.HostStale,
                    $"The generated bake host is stale: it has no MessagePack registration for the "
                        + $"descriptor mapping '{mapping.Prefix}' -> '{mapping.TypeName}'. Run "
                        + "'bork2d-bake generate-host' and rebuild the host.",
                    descriptor.DescriptorPath,
                    mapping.TypeLine,
                    mapping.TypeColumn);
                valid = false;
            }
        }

        foreach (BakeTypeRegistration registration in registrations)
        {
            bool found = false;
            foreach (ProjectTypeMapping mapping in descriptor.Types)
            {
                if (string.Equals(mapping.Prefix, registration.SourcePrefix, StringComparison.Ordinal)
                    && string.Equals(mapping.TypeName, registration.TypeName, StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.HostStale,
                    $"The generated bake host is stale: it registers '{registration.SourcePrefix}' -> "
                        + $"'{registration.TypeName}', which the descriptor no longer maps. Run "
                        + "'bork2d-bake generate-host' and rebuild the host.",
                    descriptor.DescriptorPath);
                valid = false;
            }
        }

        return valid;
    }
}
