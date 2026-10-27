using Bork2d.Assets.Pack;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Toml;

namespace Bork2d.Bake.Hosting;

/// <summary>
/// The contract a generated bake host implements and the CLI consumes.
/// </summary>
/// <remarks>
/// <para>
/// A host is generated code: it knows the concrete mapped types and never reflects over their
/// members. <see cref="Bind"/> dispatches on the mapped type name and calls the generic
/// <see cref="TomlShapeBinder"/> with the typed shape; the binder records reference sites on the
/// context. <see cref="AddToPack"/> is generated per type and calls
/// <see cref="PackBuilder.AddMessagePack{T}"/> directly. <see cref="ValidateShapes"/> runs the
/// host-load check that every mapped root has a supported shape kind.
/// </para>
/// <para>
/// The contract is internal and the generated host sees it through
/// <c>InternalsVisibleTo("Bork2d.BakeHost")</c> (Q-02).
/// </para>
/// </remarks>
internal interface IBakeManifest
{
    /// <summary>Gets the mapped type registrations in descriptor order.</summary>
    IReadOnlyList<BakeTypeRegistration> Types { get; }

    /// <summary>Binds a parsed TOML source into the mapped type.</summary>
    /// <param name="typeName">The mapped type's full name.</param>
    /// <param name="context">The per-source binding context.</param>
    /// <returns>The typed instance, or <see langword="null"/> when a diagnostic was reported.</returns>
    object? Bind(string typeName, BakeBindContext context);

    /// <summary>Adds a bound value to the pack with the type's generated writer.</summary>
    /// <param name="builder">The pack builder to add to.</param>
    /// <param name="typeName">The mapped type's full name.</param>
    /// <param name="assetPath">The canonical asset path.</param>
    /// <param name="value">The bound typed value.</param>
    void AddToPack(PackBuilder builder, string typeName, string assetPath, object value);

    /// <summary>Validates every mapped root's shape kind at host load.</summary>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    void ValidateShapes(BakeDiagnostics diagnostics);
}
