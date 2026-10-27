using Bork2d.Api;
using Bork2d.Assets.Pack;
using Bork2d.Bake.Diagnostics;

namespace Bork2d.Bake.Baking;

/// <summary>
/// Composes one pack from its baked entry set.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place the pipeline calls <c>PackBuilder</c>. Entries are added in the index's
/// ordinal path order: custom data through its registered typed writer, processor shaped output
/// through its stored engine write delegate, and processor data through <c>AddRaw</c>. The writer
/// sorts the entry table independently, so this order only affects how deterministic the composition
/// itself is.
/// </para>
/// <para>
/// A DLC pack may reference an asset in the main pack. The main index is passed as the external asset
/// set, which <see cref="PackBuilder"/> uses only for reference validation; it is never written into
/// the DLC pack and the on-disk format is unchanged. The main pack passes <see langword="null"/>, so
/// its references stay strictly local.
/// </para>
/// <para>
/// Reference and path validation already ran against the index, so a <see cref="Bork2dException"/>
/// here is an internal baker error (BK5001), not a user-facing reference diagnostic. It is still
/// reported as a normal diagnostic so the caller never sees a pack-writer exception or a partial pack.
/// </para>
/// </remarks>
internal static class PackComposer
{
    /// <summary>Composes the pack builder from the baked entry set.</summary>
    /// <param name="index">The complete baked asset index for this pack.</param>
    /// <param name="project">The validated project configuration with its type registrations.</param>
    /// <param name="mode">The payload compression mode.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <param name="externalIndex">
    /// The main pack's index when composing a DLC pack, or <see langword="null"/> for the main pack
    /// (or a single-pack project).
    /// </param>
    /// <returns>The populated builder, or <see langword="null"/> when composition failed.</returns>
    internal static PackBuilder? Compose(
        BakedAssetIndex index,
        BakeProject project,
        PackCompressionMode mode,
        BakeDiagnostics diagnostics,
        BakedAssetIndex? externalIndex = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var builder = new PackBuilder(mode);

        if (externalIndex is not null)
        {
            builder.SetExternalAssets(
                [.. externalIndex.Entries.Select(entry => new PackExternalAsset(entry.Path, entry.IsRaw))]);
        }

        string? currentPath = null;

        try
        {
            foreach (BakedAssetEntry entry in index.Entries)
            {
                currentPath = entry.Path;
                switch (entry.Kind)
                {
                    case BakedAssetEntryKind.CustomData:
                        WriteCustomData(builder, project, entry);
                        break;

                    case BakedAssetEntryKind.ProcessorAsset:
                        entry.ProcessorOutput!.Write!(builder, entry.Path);
                        break;

                    case BakedAssetEntryKind.ProcessorData:
                        builder.AddRaw(entry.Path, entry.ProcessorOutput!.RawPayload);
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unknown baked entry kind '{entry.Kind}'.");
                }
            }

            return builder;
        }
        catch (Exception exception) when (exception is Bork2dException or ArgumentException
            or InvalidOperationException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.PackComposeFailed,
                $"Internal bake error: the baked entry '{(currentPath ?? "<none>")}' could not be "
                    + $"written: {exception.Message}",
                assetPath: currentPath);
            return null;
        }
    }

    private static void WriteCustomData(
        PackBuilder builder,
        BakeProject project,
        BakedAssetEntry entry)
    {
        string typeName = entry.Asset!.TypeName;
        if (!project.AssetTypes.TryGetValue(typeName, out BakeAssetTypeRegistration? registration))
        {
            throw new InvalidOperationException(
                $"The data type '{typeName}' is not registered.");
        }

        registration.Write(builder, entry.Path, entry.Asset.Value);
    }
}
