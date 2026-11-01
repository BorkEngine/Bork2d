using System.Numerics;
using Bork2d.Bake.Diagnostics;
using Bork2d.Engine.Assets;

namespace Bork2d.Bake.Baking;

/// <summary>
/// Validates the <see cref="SpriteAsset"/> fields the renderer treats as fatal.
/// </summary>
/// <remarks>
/// <para>
/// The renderer throws for a malformed UV array, a non-positive or non-finite scale, a pivot outside
/// <c>[0,1]</c>, an unknown topology, or a topology/index combination it cannot draw. This validator
/// reports those conditions as <c>BK8xxx</c> diagnostics at bake time, so a malformed sprite fails pack
/// creation instead of the first draw.
/// </para>
/// <para>
/// An absent texture is valid: the renderer draws its placeholder. UV spans outside <c>[0,1]</c> are
/// also valid because repeat and clamp samplers intentionally use them. The validator never throws for
/// malformed field values and reports every problem it finds in one pass.
/// </para>
/// </remarks>
internal sealed class SpriteAssetValidator : IAssetValidator<SpriteAsset>
{
    /// <inheritdoc/>
    public void Validate(
        string assetPath,
        SpriteAsset asset,
        IAssetProcessorDiagnostics diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(diagnostics);

        ValidateUvs(assetPath, asset.Uvs, diagnostics);
        ValidateScale(assetPath, asset.Scale, diagnostics);
        ValidatePivot(assetPath, asset.Pivot, diagnostics);
        ValidateTopology(assetPath, asset.Topology, asset.Indices, diagnostics);
    }

    private static void ValidateUvs(
        string assetPath,
        Vector2[]? uvs,
        IAssetProcessorDiagnostics diagnostics)
    {
        if (uvs is null)
        {
            diagnostics.Error(
                BakeDiagnosticCodes.SpriteUvsInvalid,
                Malformed(assetPath, "Uvs", "the UV array is null"));
            return;
        }

        if (uvs.Length != 4)
        {
            diagnostics.Error(
                BakeDiagnosticCodes.SpriteUvsInvalid,
                Malformed(assetPath, "Uvs", $"expected exactly four UVs, got {uvs.Length}"));
        }

        for (var index = 0; index < uvs.Length; index++)
        {
            Vector2 uv = uvs[index];
            if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y))
            {
                diagnostics.Error(
                    BakeDiagnosticCodes.SpriteUvsInvalid,
                    Malformed(assetPath, "Uvs", $"UV[{index}] is not finite"));
            }
        }
    }

    private static void ValidateScale(
        string assetPath,
        Vector2 scale,
        IAssetProcessorDiagnostics diagnostics)
    {
        if (!float.IsFinite(scale.X)
            || scale.X <= 0f
            || !float.IsFinite(scale.Y)
            || scale.Y <= 0f)
        {
            diagnostics.Error(
                BakeDiagnosticCodes.SpriteScaleInvalid,
                Malformed(
                    assetPath,
                    "Scale",
                    $"expected a finite world scale greater than zero on both axes, got "
                        + $"({scale.X}, {scale.Y})"));
        }
    }

    private static void ValidatePivot(
        string assetPath,
        Vector2 pivot,
        IAssetProcessorDiagnostics diagnostics)
    {
        if (!float.IsFinite(pivot.X)
            || !float.IsFinite(pivot.Y)
            || pivot.X < 0f
            || pivot.X > 1f
            || pivot.Y < 0f
            || pivot.Y > 1f)
        {
            diagnostics.Error(
                BakeDiagnosticCodes.SpritePivotInvalid,
                Malformed(
                    assetPath,
                    "Pivot",
                    $"expected a finite normalized pivot in [0,1], got ({pivot.X}, {pivot.Y})"));
        }
    }

    private static void ValidateTopology(
        string assetPath,
        SpriteTopology topology,
        ushort[]? indices,
        IAssetProcessorDiagnostics diagnostics)
    {
        if (topology is not (SpriteTopology.Quad
            or SpriteTopology.TriangleStrip
            or SpriteTopology.Triangles))
        {
            diagnostics.Error(
                BakeDiagnosticCodes.SpriteTopologyInvalid,
                Malformed(
                    assetPath,
                    "Topology",
                    $"the topology value '{(int)topology}' is not a known SpriteTopology"));
            return;
        }

        if (topology == SpriteTopology.Quad)
        {
            if (indices is not null)
            {
                diagnostics.Error(
                    BakeDiagnosticCodes.SpriteTopologyInvalid,
                    Malformed(assetPath, "Indices", "a Quad must have a null index buffer"));
            }

            return;
        }

        if (indices is null || indices.Length == 0)
        {
            diagnostics.Error(
                BakeDiagnosticCodes.SpriteTopologyInvalid,
                Malformed(
                    assetPath,
                    "Indices",
                    $"topology '{topology}' requires a non-empty index buffer"));
            return;
        }

        for (var index = 0; index < indices.Length; index++)
        {
            if (indices[index] > 3)
            {
                diagnostics.Error(
                    BakeDiagnosticCodes.SpriteTopologyInvalid,
                    Malformed(
                        assetPath,
                        "Indices",
                        $"index[{index}] is {indices[index]}, expected a value in 0..3"));
            }
        }
    }

    private static string Malformed(string assetPath, string field, string detail)
        => $"Sprite asset '{assetPath}' has a malformed '{field}' field: {detail}.";
}
