using Bork2d.Assets.Pack;

namespace Bork2d.Bake.Hosting;

/// <summary>
/// The contract a generated bake host implements and the CLI consumes.
/// </summary>
/// <remarks>
/// A host is generated code: it knows the concrete mapped types and never reflects over their
/// members. <see cref="Bind"/> returns the typed instance and every reference site with the
/// member path needed for source spans; <see cref="AddToPack"/> is generated per type and calls
/// <see cref="PackBuilder.AddMessagePack{T}"/> directly.
/// </remarks>
internal interface IBakeManifest
{
    /// <summary>Gets the mapped type registrations.</summary>
    IReadOnlyList<BakeTypeRegistration> Types { get; }

    /// <summary>Binds a TOML source into the mapped type.</summary>
    /// <param name="typeName">The mapped type's full name.</param>
    /// <param name="toml">The TOML source text.</param>
    /// <param name="references">The reference sites found in the bound object graph.</param>
    /// <returns>The typed instance.</returns>
    object Bind(
        string typeName,
        string toml,
        out IReadOnlyList<BakeReferenceSite> references);

    /// <summary>Adds a bound value to the pack with the type's generated writer.</summary>
    /// <param name="builder">The pack builder to add to.</param>
    /// <param name="typeName">The mapped type's full name.</param>
    /// <param name="assetPath">The canonical asset path.</param>
    /// <param name="value">The bound typed value.</param>
    void AddToPack(PackBuilder builder, string typeName, string assetPath, object value);
}
