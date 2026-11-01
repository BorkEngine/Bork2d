using PolyType;

namespace Bork2d.Bake.Baking;

/// <summary>
/// The framework-internal registration over one public <see cref="IAssetValidator{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The registration closes over the validator instance and the generic factory captures
/// <c>T</c> at registration time, so the pipeline never reflects over a validator type or looks up its
/// generic interface. The cast from the bound <see cref="object"/> to <c>T</c> happens here, once per
/// asset, after the binder has already produced the registered type.
/// </para>
/// <para>
/// A validator registration adds no second diagnostics sink or policy model: the pipeline passes the
/// same public <see cref="IAssetProcessorDiagnostics"/> contract that processors use, and the
/// registration only names the validator for the diagnostic prefix.
/// </para>
/// </remarks>
/// <typeparam name="T">The data type the validator validates.</typeparam>
internal sealed class BakeAssetValidatorRegistration<T> : IBakeAssetValidator
    where T : IShapeable<T>
{
    private readonly IAssetValidator<T> _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="BakeAssetValidatorRegistration{T}"/> class.
    /// </summary>
    /// <param name="validator">The public validator instance.</param>
    internal BakeAssetValidatorRegistration(IAssetValidator<T> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        _validator = validator;
        ValidatorType = validator.GetType();
        Name = ValidatorType.Name;
    }

    /// <inheritdoc/>
    public Type ValidatorType { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public void Validate(
        string assetPath,
        object asset,
        IAssetProcessorDiagnostics diagnostics)
        => _validator.Validate(assetPath, (T)asset, diagnostics);
}
