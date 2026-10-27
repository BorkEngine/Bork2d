using Bork2d.Assets.Pack;
using Bork2d.Bake.Toml;
using PolyType;

namespace Bork2d.Bake.Baking;

/// <summary>
/// One explicit data-type registration: the compile-time binding and pack-write closures for a type
/// that may appear in a data TOML's <c>[bork2d].type</c> (D-036/D-038).
/// </summary>
/// <remarks>
/// <para>
/// The registration is created only by the generic <see cref="Create{T}"/> factory, so
/// <c>T.GetTypeShape()</c> is a compiled shape rather than a runtime lookup. <see cref="Bind"/> calls
/// the shared <see cref="TomlShapeBinder"/> and <see cref="Write"/> closes over
/// <c>PackBuilder.AddMessagePack&lt;T&gt;</c>; the pipeline never performs reflection or assembly
/// scanning.
/// </para>
/// <para>
/// Registration is keyed by <see cref="TypeName"/> (<see cref="Type.FullName"/>), and the type must
/// be accessible to the assets executable: public, or internal with an <c>InternalsVisibleTo</c>
/// grant.
/// </para>
/// </remarks>
internal sealed class BakeAssetTypeRegistration
{
    private readonly Func<BakeBindContext, object?> _bind;
    private readonly Action<PackBuilder, string, object> _write;

    private BakeAssetTypeRegistration(
        Type type,
        string typeName,
        Func<BakeBindContext, object?> bind,
        Action<PackBuilder, string, object> write)
    {
        Type = type;
        TypeName = typeName;
        _bind = bind;
        _write = write;
    }

    /// <summary>Gets the registered CLR type.</summary>
    internal Type Type { get; }

    /// <summary>Gets the registered type's full name, the <c>[bork2d].type</c> selector.</summary>
    internal string TypeName { get; }

    /// <summary>Binds a parsed source document into this registration's type.</summary>
    /// <param name="context">The per-source binding context.</param>
    /// <returns>The bound value, or a default value when a diagnostic was reported.</returns>
    internal object? Bind(BakeBindContext context) => _bind(context);

    /// <summary>Writes a bound value of this registration's type into a pack.</summary>
    /// <param name="builder">The pack builder to add the entry to.</param>
    /// <param name="assetPath">The canonical asset path.</param>
    /// <param name="value">The bound typed value.</param>
    internal void Write(PackBuilder builder, string assetPath, object value)
        => _write(builder, assetPath, value);

    /// <summary>Creates the registration for <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The shapeable data type.</typeparam>
    /// <returns>The registration closing over the compiled shape and the typed pack writer.</returns>
    internal static BakeAssetTypeRegistration Create<T>()
        where T : IShapeable<T>
    {
        string typeName = typeof(T).FullName
            ?? throw new InvalidOperationException(
                $"The registered type '{typeof(T)}' has no full name.");

        return new BakeAssetTypeRegistration(
            typeof(T),
            typeName,
            context => TomlShapeBinder.Bind<T>(context),
            (builder, assetPath, value) => builder.AddMessagePack(assetPath, (T)value));
    }
}
