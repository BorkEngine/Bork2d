using Bork2d.Assets.Vfs;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Sources;

namespace Bork2d.Bake.Baking;

/// <summary>
/// Validates every reference site of the baked custom-data set against the complete baked asset index
/// (M4 §9), before <c>PackBuilder</c> sees an entry.
/// </summary>
/// <remarks>
/// <para>
/// The rules are §9's six: the path must be canonical, the bake must produce it, the kind must match
/// (<c>AssetRef</c> targets MessagePack, <c>DataRef</c> targets raw), and a typed reference must match
/// the target's baked type. An absent reference is never recorded as a site and is valid by
/// construction.
/// </para>
/// <para>
/// Diagnostics point at the reference value's source file, line, and column with the member path and
/// reference path, so the user fixes the TOML rather than reading a pack-writer exception. Any failure
/// here stops the bake before pack composition; a later <c>PackBuilder</c> reference failure is an
/// internal baker error (BK5xxx).
/// </para>
/// </remarks>
internal static class BakeReferenceValidator
{
    /// <summary>Validates every reference site of every bound asset.</summary>
    /// <param name="assets">The bound custom-data assets.</param>
    /// <param name="index">The complete baked asset index.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns><see langword="true"/> when every reference is valid.</returns>
    internal static bool Validate(
        IReadOnlyList<BoundAsset> assets,
        BakedAssetIndex index,
        BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(diagnostics);

        bool valid = true;
        foreach (BoundAsset asset in assets)
        {
            foreach (BakeReferenceSite site in asset.References)
            {
                valid &= ValidateSite(asset, site, index, diagnostics);
            }
        }

        return valid;
    }

    private static bool ValidateSite(
        BoundAsset asset,
        BakeReferenceSite site,
        BakedAssetIndex index,
        BakeDiagnostics diagnostics)
    {
        string memberPath = site.MemberPath;
        string referencePath = site.ReferencePath;

        if (!AssetPath.TryValidateCanonical(referencePath, out string? reason))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ReferenceNotCanonical,
                $"The reference at '{memberPath}' uses the non-canonical asset path "
                    + $"'{referencePath}': {reason}.",
                asset.SourcePath,
                site.Line,
                site.Column,
                referencePath,
                memberPath);
            return false;
        }

        if (!index.TryGet(referencePath, out BakedAssetEntry? target))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ReferenceMissing,
                $"The reference at '{memberPath}' targets '{referencePath}', which this bake does "
                    + "not produce.",
                asset.SourcePath,
                site.Line,
                site.Column,
                referencePath,
                memberPath);
            return false;
        }

        if (site.IsData != target.IsRaw)
        {
            string expected = site.IsData ? "a raw payload" : "a MessagePack asset";
            string actual = target.IsRaw ? "a raw payload" : "a MessagePack asset";
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ReferenceWrongKind,
                $"The reference at '{memberPath}' targets '{referencePath}' as {expected}, but the "
                    + $"baked entry is {actual}.",
                asset.SourcePath,
                site.Line,
                site.Column,
                referencePath,
                memberPath);
            return false;
        }

        if (site.IsData || site.TargetTypeName is null || target.IsRaw)
        {
            return true;
        }

        if (string.Equals(site.TargetTypeName, target.TypeName, StringComparison.Ordinal))
        {
            return true;
        }

        diagnostics.Add(
            BakeSeverity.Error,
            BakeDiagnosticCodes.ReferenceWrongType,
            $"The reference at '{memberPath}' targets '{referencePath}' as "
                + $"'{site.TargetTypeName}', but the baked entry is "
                + (target.TypeName is null
                    ? "an untyped MessagePack asset."
                    : $"'{target.TypeName}'."),
            asset.SourcePath,
            site.Line,
            site.Column,
            referencePath,
            memberPath);
        return false;
    }
}
