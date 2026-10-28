using Bork2d.Engine.Assets;
using PolyType;
using PolyType.Abstractions;

namespace Bork2d.Engine.Assets.Pack;

/// <summary>
/// Collects <see cref="AssetRef{TAsset}"/> and <see cref="DataRef"/> values from a MessagePack
/// asset using its compiled PolyType shape.
/// </summary>
/// <remarks>
/// Traversal is source-generated and reflection-free. It covers object properties, arrays and other
/// enumerables, dictionary values, nullable wrappers, and marshaler surrogates (the marshaled value
/// is visited, never treated as a scalar). A reference value is recorded at the property that
/// contains it; the collector never descends into the reference itself. Polymorphic and
/// <see cref="object"/>-typed members are not traversed in v1.
/// </remarks>
internal sealed class AssetReferenceCollector : TypeShapeVisitor
{
    private readonly string _sourcePath;
    private readonly List<AssetReferenceInfo> _references = [];
    private readonly List<string> _path = [];
    private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

    private AssetReferenceCollector(string sourcePath)
    {
        _sourcePath = sourcePath;
    }

    /// <summary>
    /// Collects all references in <paramref name="value"/>.
    /// </summary>
    /// <typeparam name="T">The shapeable asset type.</typeparam>
    /// <param name="sourcePath">The canonical path of the asset being serialized.</param>
    /// <param name="value">The asset value.</param>
    /// <returns>The references found, in traversal order.</returns>
    internal static IReadOnlyList<AssetReferenceInfo> Collect<T>(string sourcePath, T value)
        where T : IShapeable<T>
    {
        var collector = new AssetReferenceCollector(sourcePath);
        collector.VisitValue(T.GetTypeShape(), value);
        return collector._references;
    }

    /// <inheritdoc/>
    public override object? VisitObject<T>(IObjectTypeShape<T> shape, object? state)
    {
        if (state is not T value || IsScalar(typeof(T)))
        {
            return null;
        }

        bool isReference = !typeof(T).IsValueType;
        if (isReference && !_visited.Add(value))
        {
            return null;
        }

        foreach (IPropertyShape property in shape.Properties)
        {
            if (!property.HasGetter)
            {
                continue;
            }

            _path.Add(property.Name);
            property.Accept(this, value);
            _path.RemoveAt(_path.Count - 1);
        }

        if (isReference)
        {
            _visited.Remove(value);
        }

        return null;
    }

    /// <inheritdoc/>
    public override object? VisitProperty<TDeclaringType, TPropertyType>(
        IPropertyShape<TDeclaringType, TPropertyType> shape,
        object? state)
    {
        if (state is not TDeclaringType declaring)
        {
            return null;
        }

        Getter<TDeclaringType, TPropertyType> getter = shape.GetGetter();
        TPropertyType value = getter(ref declaring);
        VisitValue(shape.PropertyType, value);
        return null;
    }

    /// <inheritdoc/>
    public override object? VisitOptional<TOptional, TElement>(
        IOptionalTypeShape<TOptional, TElement> shape,
        object? state)
    {
        if (state is not TOptional optional)
        {
            return null;
        }

        OptionDeconstructor<TOptional, TElement> deconstruct = shape.GetDeconstructor();
        if (deconstruct(optional, out TElement? element))
        {
            VisitValue(shape.ElementType, element);
        }

        return null;
    }

    /// <inheritdoc/>
    public override object? VisitEnumerable<TEnumerable, TElement>(
        IEnumerableTypeShape<TEnumerable, TElement> shape,
        object? state)
    {
        if (state is not TEnumerable enumerable || IsScalar(shape.ElementType.Type))
        {
            return null;
        }

        Func<TEnumerable, IEnumerable<TElement>> getEnumerable = shape.GetGetEnumerable();
        int index = 0;
        foreach (TElement element in getEnumerable(enumerable))
        {
            _path.Add($"[{index}]");
            VisitValue(shape.ElementType, element);
            _path.RemoveAt(_path.Count - 1);
            index++;
        }

        return null;
    }

    /// <inheritdoc/>
    public override object? VisitDictionary<TDictionary, TKey, TValue>(
        IDictionaryTypeShape<TDictionary, TKey, TValue> shape,
        object? state)
    {
        if (state is not TDictionary dictionary || IsScalar(shape.ValueType.Type))
        {
            return null;
        }

        Func<TDictionary, IReadOnlyDictionary<TKey, TValue>> getDictionary =
            shape.GetGetDictionary();
        foreach (KeyValuePair<TKey, TValue> pair in getDictionary(dictionary))
        {
            _path.Add($"[{pair.Key}]");
            VisitValue(shape.ValueType, pair.Value);
            _path.RemoveAt(_path.Count - 1);
        }

        return null;
    }

    /// <inheritdoc/>
    public override object? VisitSurrogate<T, TSurrogate>(
        ISurrogateTypeShape<T, TSurrogate> shape,
        object? state)
    {
        if (state is not T value)
        {
            return null;
        }

        // Marshal first, then visit the surrogate shape: a marshaler may hide an AssetRef/DataRef
        // inside the surrogate, so the surrogate must not be treated as a scalar leaf.
        TSurrogate? surrogate = shape.Marshaler.Marshal(value);
        VisitValue(shape.SurrogateType, surrogate);
        return null;
    }

    private void VisitValue(ITypeShape shape, object? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is IAssetReference reference)
        {
            _references.Add(new AssetReferenceInfo(
                _sourcePath,
                string.Join('.', _path),
                reference.Path,
                reference.IsData,
                reference.TargetTypeName));
            return;
        }

        if (IsScalar(shape.Type))
        {
            return;
        }

        shape.Accept(this, value);
    }

    private static bool IsScalar(Type type)
        => type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(Guid)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan);
}
