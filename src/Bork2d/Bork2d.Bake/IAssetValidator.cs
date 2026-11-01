using PolyType;

namespace Bork2d.Bake;

/// <summary>
/// A semantic validator for one data type registered with
/// <see cref="BakeApplicationBuilder.RegisterAsset{T}(IAssetValidator{T})"/>.
/// </summary>
/// <remarks>
/// <para>
/// A validator runs after a custom-data TOML is bound and before any processor output, asset index, or
/// pack is composed. It inspects the bound value and reports problems through
/// <see cref="IAssetProcessorDiagnostics"/>: an error fails the pack at the validation stage, while a
/// warning or info is reported without failing.
/// </para>
/// <para>
/// Validators run synchronously on the bake thread and one instance is reused for every asset of its
/// type, so a validator must not keep per-asset state. It must not throw for malformed asset data;
/// report every problem through <paramref name="diagnostics"/> so the bake can collect all of them.
/// </para>
/// </remarks>
/// <typeparam name="T">The data type, which must carry a compiled <c>[GenerateShape]</c>.</typeparam>
public interface IAssetValidator<T>
    where T : IShapeable<T>
{
    /// <summary>Validates one bound asset.</summary>
    /// <param name="assetPath">The asset's canonical path, used in diagnostic messages.</param>
    /// <param name="asset">The bound typed asset.</param>
    /// <param name="diagnostics">The per-asset diagnostic sink; an error fails the pack.</param>
    void Validate(string assetPath, T asset, IAssetProcessorDiagnostics diagnostics);
}
