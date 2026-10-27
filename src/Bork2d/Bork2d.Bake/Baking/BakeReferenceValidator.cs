using Bork2d.Assets.Vfs;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Sources;

namespace Bork2d.Bake.Baking;

/// <summary>
/// Validates every reference site of one baked pack against the entries that pack produces and, for a
/// DLC pack, the main pack's entries (M4 §9; D-M4-14).
/// </summary>
/// <remarks>
/// <para>
/// The rules are §9's six: the path must be canonical, the bake must produce it locally or, for a
/// DLC pack only, in the main pack; the kind must match (<c>AssetRef</c> targets MessagePack,
/// <c>DataRef</c> targets raw), and a typed reference must match the target's baked type. An absent
/// reference is never recorded as a site and is valid by construction.
/// </para>
/// <para>
/// The main pack is strict: it passes no external index, so a reference to a DLC path fails. A DLC
/// pack resolves a local entry first (a local overlay wins), then the main pack's index. A path that
/// exists only in another DLC pack is not in either index and is therefore an error. Diagnostics name
/// the checked pack so a cross-pack mistake is obvious.
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
    /// <summary>Validates every reference site of every bound asset in one pack.</summary>
    /// <param name="assets">The bound custom-data assets of the pack.</param>
    /// <param name="index">The complete baked asset index of the pack.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns><see langword="true"/> when every reference is valid.</returns>
    internal static bool Validate(
        IReadOnlyList<BoundAsset> assets,
        BakedAssetIndex index,
        BakeDiagnostics diagnostics)
        => Validate(assets, index, externalIndex: null, packPath: null, diagnostics);

    /// <summary>Validates one pack's references with the optional main-pack external scope.</summary>
    /// <param name="assets">The bound custom-data assets of the pack.</param>
    /// <param name="localIndex">The complete baked asset index of the pack.</param>
    /// <param name="externalIndex">
    /// The main pack's index for a DLC pack, or <see langword="null"/> for the main pack / single-pack
    /// projects.
    /// </param>
    /// <param name="packPath">The pack label used in diagnostics, or <see langword="null"/>.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <param name="externalPackPath">The main pack's label when <paramref name="externalIndex"/> is supplied.</param>
    /// <returns><see langword="true"/> when every reference is valid.</returns>
    internal static bool Validate(
        IReadOnlyList<BoundAsset> assets,
        BakedAssetIndex localIndex,
        BakedAssetIndex? externalIndex,
        string? packPath,
        BakeDiagnostics diagnostics,
        string? externalPackPath = null)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(localIndex);
        ArgumentNullException.ThrowIfNull(diagnostics);

        bool valid = true;
        foreach (BoundAsset asset in assets)
        {
            foreach (BakeReferenceSite site in asset.References)
            {
                valid &= ValidateSite(
                    asset,
                    site,
                    localIndex,
                    externalIndex,
                    packPath,
                    externalPackPath,
                    diagnostics);
            }
        }

        return valid;
    }

    private static bool ValidateSite(
        BoundAsset asset,
        BakeReferenceSite site,
        BakedAssetIndex localIndex,
        BakedAssetIndex? externalIndex,
        string? packPath,
        string? externalPackPath,
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

        BakedAssetIndex? targetIndex = localIndex;
        if (!localIndex.TryGet(referencePath, out BakedAssetEntry? target))
        {
            if (externalIndex is null || !externalIndex.TryGet(referencePath, out target))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.ReferenceMissing,
                    BuildMissingMessage(memberPath, referencePath, packPath, externalPackPath),
                    asset.SourcePath,
                    site.Line,
                    site.Column,
                    referencePath,
                    memberPath);
                return false;
            }

            targetIndex = externalIndex;
        }

        string targetScope = ReferenceEquals(targetIndex, externalIndex)
            ? externalPackPath is null ? string.Empty : $" in the main pack '{externalPackPath}'"
            : string.Empty;

        if (site.IsData != target.IsRaw)
        {
            string expected = site.IsData ? "a raw payload" : "a MessagePack asset";
            string actual = target.IsRaw ? "a raw payload" : "a MessagePack asset";
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ReferenceWrongKind,
                $"The reference at '{memberPath}' targets '{referencePath}' as {expected}, but the "
                    + $"baked entry{targetScope} is {actual}.",
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
                + $"'{site.TargetTypeName}', but the baked entry{targetScope} is "
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

    private static string BuildMissingMessage(
        string memberPath,
        string referencePath,
        string? packPath,
        string? externalPackPath)
    {
        if (packPath is null)
        {
            return $"The reference at '{memberPath}' targets '{referencePath}', which this bake does "
                + "not produce.";
        }

        if (externalPackPath is null)
        {
            return $"The reference at '{memberPath}' targets '{referencePath}', which the asset pack "
                + $"'{packPath}' does not produce.";
        }

        return $"The reference at '{memberPath}' targets '{referencePath}', which the DLC pack "
            + $"'{packPath}' does not produce and is not present in the main pack "
            + $"'{externalPackPath}'.";
    }
}
