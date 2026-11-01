namespace Bork2d.Bake.Baking;

/// <summary>
/// The non-generic view of one registered <see cref="IAssetValidator{T}"/> that the pipeline executes
/// over a bound asset whose static type is not known there.
/// </summary>
/// <remarks>
/// The generic <see cref="BakeAssetValidatorRegistration{T}"/> performs the cast from the bound
/// <see cref="object"/> to the registered type; the pipeline only needs the validator's identity for
/// diagnostics and the type-erased validate call.
/// </remarks>
internal interface IBakeAssetValidator
{
    /// <summary>Gets the validator implementation's CLR type.</summary>
    Type ValidatorType { get; }

    /// <summary>Gets the validator's simple type name, used as the diagnostic prefix.</summary>
    string Name { get; }

    /// <summary>Validates one bound asset through the public validator contract.</summary>
    /// <param name="assetPath">The canonical asset path.</param>
    /// <param name="asset">The bound value, which must be the validator's registered type.</param>
    /// <param name="diagnostics">The per-asset diagnostic sink.</param>
    void Validate(string assetPath, object asset, IAssetProcessorDiagnostics diagnostics);
}
