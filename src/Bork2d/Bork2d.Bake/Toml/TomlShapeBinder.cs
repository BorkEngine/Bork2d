using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bork2d.Api.Assets;
using Bork2d.Bake.Diagnostics;
using PolyType;
using PolyType.Abstractions;
using Tomlyn;
using Tomlyn.Model;

namespace Bork2d.Bake.Toml;

/// <summary>
/// The generic, shape-driven TOML DOM binder: it walks the compiled PolyType shape of a mapped type
/// and reads the Tomlyn DOM, so no member-level reflection is used for binding.
/// </summary>
/// <remarks>
/// <para>
/// This is the owner-approved Q-07 refinement of WP0's generated per-type binder. The generated host
/// contains only the type manifest and the <c>Bind</c>/<c>AddToPack</c> switches; this binder reads
/// shape metadata (properties, constructor parameters, enum members, optional/enumerable/dictionary
/// facets and marshaler surrogates) and the STJ attributes PolyType exposes through attribute
/// providers. Member values are read from <see cref="BakeBindContext.Root"/> and source positions
/// come from the syntax index in <see cref="BakeBindContext.Syntax"/>, so every diagnostic points at
/// the exact value.
/// </para>
/// <para>
/// Mapping rules are the M4 §7.3 contract: camelCase keys by default, <c>[JsonPropertyName]</c>
/// overrides, <c>[JsonIgnore]</c> exclusion, <c>[JsonRequired]</c>/C# <c>required</c> enforcement,
/// case-insensitive read, unknown keys as errors, exact camelCase enum names, offset date-times
/// only, range-checked integers, and <c>Vector2/3/4</c>, <c>Color</c>, <c>AssetRef&lt;T&gt;</c>, and
/// <c>DataRef</c> forms. A member type outside that table is a <c>BK2xxx</c> diagnostic, never a
/// silent default.
/// </para>
/// <para>
/// Missing-value handling also enforces the §7.3 non-nullable-reference rule: an absent
/// non-nullable reference member that is not an enumerable/dictionary shape is a missing-value
/// error unless a constructor default or member initializer supplies a non-null value. Absent
/// collections, nullable references, and value types keep their C# defaults.
/// </para>
/// </remarks>
internal static class TomlShapeBinder
{
    private static readonly ShapeVisitor _visitor = new();

    private const string ReservedMetadataTable = "bork2d";

    /// <summary>Binds a parsed document into the mapped type.</summary>
    /// <typeparam name="T">The mapped type, which must carry a compiled shape.</typeparam>
    /// <param name="context">The per-source binding context.</param>
    /// <returns>The bound value, or a default value when a diagnostic was reported.</returns>
    internal static object? Bind<T>(BakeBindContext context)
        where T : IShapeable<T>
    {
        ArgumentNullException.ThrowIfNull(context);
        return BindShape(
            T.GetTypeShape(),
            context,
            BakeBindNode.Root(CreateBindRoot(context.Root)));
    }

    /// <summary>
    /// Removes the reserved top-level <c>bork2d</c> metadata table from the root a mapped type sees.
    /// </summary>
    /// <remarks>
    /// The reserved table is metadata (D-M4-13): it is neither an unknown key nor a bindable member.
    /// A mapped type therefore cannot receive a root <c>Bork2d</c> member from it; a required member
    /// with that name reports a missing key. Nested tables named <c>bork2d</c> are ordinary member
    /// tables and are unaffected because only the root is filtered.
    /// </remarks>
    private static object CreateBindRoot(object root)
    {
        if (root is not TomlTable table
            || !table.ContainsKey(ReservedMetadataTable))
        {
            return root;
        }

        var filtered = new TomlTable();
        foreach (KeyValuePair<string, object> pair in table)
        {
            if (!string.Equals(pair.Key, ReservedMetadataTable, StringComparison.Ordinal))
            {
                filtered[pair.Key] = pair.Value;
            }
        }

        return filtered;
    }

    /// <summary>A typed setter that applies one bound member value to an argument state.</summary>
    /// <typeparam name="TArgumentState">The constructor's argument state.</typeparam>
    /// <param name="state">The argument state.</param>
    /// <param name="value">The boxed member value.</param>
    private delegate void ArgumentSetter<TArgumentState>(ref TArgumentState state, object? value);

    private static object? BindShape(
        ITypeShape shape,
        BakeBindContext context,
        in BakeBindNode node)
    {
        if (TryBindLeaf(shape.Type, context, node, out object? leaf))
        {
            return leaf;
        }

        switch (shape.Kind)
        {
            case TypeShapeKind.Object:
            case TypeShapeKind.Enum:
            case TypeShapeKind.Optional:
            case TypeShapeKind.Enumerable:
            case TypeShapeKind.Dictionary:
            case TypeShapeKind.Surrogate:
                return shape.Accept(_visitor, new BindState(context, node));

            default:
                context.AddError(
                    BakeDiagnosticCodes.BinderUnsupportedLeaf,
                    $"The member type '{FormatType(shape.Type)}' is not supported by the TOML binder.",
                    node.Path,
                    node.MemberPath);
                return null;
        }
    }

    private static bool TryBindShape(
        ITypeShape shape,
        BakeBindContext context,
        in BakeBindNode node,
        out object? value)
    {
        int before = context.Diagnostics.Count;
        value = BindShape(shape, context, node);
        return context.Diagnostics.Count == before;
    }

    private static bool TryBindLeaf(
        Type type,
        BakeBindContext context,
        in BakeBindNode node,
        out object? value)
    {
        value = null;

        if (type == typeof(string))
        {
            value = Read<string>(context, node, "a string");
            return true;
        }

        if (type == typeof(bool))
        {
            value = Read<bool>(context, node, "a boolean");
            return true;
        }

        if (type == typeof(long) || type == typeof(int) || type == typeof(short)
            || type == typeof(sbyte) || type == typeof(byte) || type == typeof(ushort)
            || type == typeof(uint) || type == typeof(ulong) || type == typeof(nint)
            || type == typeof(nuint))
        {
            return TryReadInteger(type, context, node, out value);
        }

        if (type == typeof(float) || type == typeof(double))
        {
            return TryReadFloat(type, context, node, out value);
        }

        if (type == typeof(Guid))
        {
            object? raw = Read<string>(context, node, "a GUID string");
            if (raw is not string text)
            {
                return true;
            }

            if (!Guid.TryParse(text, out Guid guid))
            {
                AddInvalidValue(context, node, "a GUID string");
                return true;
            }

            value = guid;
            return true;
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return TryReadDateTime(type, context, node, out value);
        }

        if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4))
        {
            return TryReadVector(type, context, node, out value);
        }

        if (type == typeof(object)
            || type == typeof(char)
            || type == typeof(decimal)
            || type == typeof(Half)
            || type == typeof(TimeSpan)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(Uri))
        {
            context.AddError(
                BakeDiagnosticCodes.BinderUnsupportedLeaf,
                $"The member type '{FormatType(type)}' is not a supported TOML value; "
                    + "use a bool, integer, float, double, string, enum, Guid, date-time, "
                    + "Vector2/3/4, Color, AssetRef<T>, DataRef, or a supported collection.",
                node.Path,
                node.MemberPath);
            return true;
        }

        return false;
    }

    private static object? Read<T>(
        BakeBindContext context,
        in BakeBindNode node,
        string expected)
    {
        if (node.Value is T typed)
        {
            return typed;
        }

        AddTypeMismatch(context, node, expected);
        return null;
    }

    private static bool TryReadInteger(
        Type type,
        BakeBindContext context,
        in BakeBindNode node,
        out object? value)
    {
        value = null;
        if (node.Value is not long number)
        {
            if (node.Value is double)
            {
                context.AddError(
                    BakeDiagnosticCodes.BinderTypeMismatch,
                    $"'{node.MemberPath}' must be an integer; got a float "
                        + "(a float is never truncated to an integer).",
                    node.Path,
                    node.MemberPath);
                return true;
            }

            AddTypeMismatch(context, node, "an integer");
            return true;
        }

        try
        {
            value = ConvertInteger(type, number);
            return true;
        }
        catch (OverflowException)
        {
            (string min, string max) = IntegerRange(type);
            context.AddError(
                BakeDiagnosticCodes.BinderNumericRange,
                $"'{node.MemberPath}' must be an integer between {min} and {max}; "
                    + $"got {number.ToString(CultureInfo.InvariantCulture)}.",
                node.Path,
                node.MemberPath);
            return true;
        }
    }

    private static object ConvertInteger(Type type, long value)
    {
        if (type == typeof(long))
        {
            return value;
        }

        if (type == typeof(int))
        {
            return checked((int)value);
        }

        if (type == typeof(short))
        {
            return checked((short)value);
        }

        if (type == typeof(sbyte))
        {
            return checked((sbyte)value);
        }

        if (type == typeof(byte))
        {
            return checked((byte)value);
        }

        if (type == typeof(ushort))
        {
            return checked((ushort)value);
        }

        if (type == typeof(uint))
        {
            return checked((uint)value);
        }

        if (type == typeof(ulong))
        {
            return checked((ulong)value);
        }

        if (type == typeof(nint))
        {
            return checked((nint)value);
        }

        return checked((nuint)value);
    }

    private static (string Min, string Max) IntegerRange(Type type)
    {
        if (type == typeof(long))
        {
            return (Text(long.MinValue), Text(long.MaxValue));
        }

        if (type == typeof(int))
        {
            return (Text(int.MinValue), Text(int.MaxValue));
        }

        if (type == typeof(short))
        {
            return (Text(short.MinValue), Text(short.MaxValue));
        }

        if (type == typeof(sbyte))
        {
            return (Text(sbyte.MinValue), Text(sbyte.MaxValue));
        }

        if (type == typeof(byte))
        {
            return (Text(byte.MinValue), Text(byte.MaxValue));
        }

        if (type == typeof(ushort))
        {
            return (Text(ushort.MinValue), Text(ushort.MaxValue));
        }

        if (type == typeof(uint))
        {
            return (Text(uint.MinValue), Text(uint.MaxValue));
        }

        if (type == typeof(ulong))
        {
            return (Text(ulong.MinValue), Text(ulong.MaxValue));
        }

        if (type == typeof(nint))
        {
            return (Text(nint.MinValue), Text(nint.MaxValue));
        }

        return (Text(nuint.MinValue), Text(nuint.MaxValue));
    }

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool TryReadFloat(
        Type type,
        BakeBindContext context,
        in BakeBindNode node,
        out object? value)
    {
        value = null;
        switch (node.Value)
        {
            case long integer:
                value = type == typeof(float) ? (object)(float)integer : (double)integer;
                return true;

            case double real:
                value = type == typeof(float) ? (object)(float)real : real;
                return true;

            default:
                AddTypeMismatch(context, node, "a float");
                return true;
        }
    }

    private static bool TryReadDateTime(
        Type type,
        BakeBindContext context,
        in BakeBindNode node,
        out object? value)
    {
        value = null;
        if (node.Value is not TomlDateTime dateTime)
        {
            AddTypeMismatch(context, node, "an offset date-time");
            return true;
        }

        if (dateTime.Kind is not TomlDateTimeKind.OffsetDateTimeByZ
            and not TomlDateTimeKind.OffsetDateTimeByNumber)
        {
            context.AddError(
                BakeDiagnosticCodes.BinderLocalDateTime,
                $"'{node.MemberPath}' must be a TOML offset date-time with a UTC offset; "
                    + "a local date, time, or date-time is not accepted.",
                node.Path,
                node.MemberPath);
            return true;
        }

        if (type == typeof(DateTime))
        {
            value = dateTime.DateTime.UtcDateTime;
        }
        else
        {
            value = dateTime.DateTime;
        }

        return true;
    }

    private static bool TryReadVector(
        Type type,
        BakeBindContext context,
        in BakeBindNode node,
        out object? value)
    {
        value = null;
        int count = type == typeof(Vector2) ? 2 : type == typeof(Vector3) ? 3 : 4;

        if (node.Value is not TomlArray array || array.Count != count)
        {
            context.AddError(
                BakeDiagnosticCodes.BinderTypeMismatch,
                $"'{node.MemberPath}' must be an array of {count} numbers; "
                    + $"got {Describe(node.Value)}.",
                node.Path,
                node.MemberPath);
            return true;
        }

        var components = new float[count];
        for (int i = 0; i < count; i++)
        {
            switch (array[i])
            {
                case long integer:
                    components[i] = integer;
                    break;
                case double real:
                    components[i] = (float)real;
                    break;
                default:
                    context.AddError(
                        BakeDiagnosticCodes.BinderTypeMismatch,
                        $"'{node.MemberPath}[{i}]' must be a number; got {Describe(array[i])}.",
                        node.Path.Element(i),
                        $"{node.MemberPath}[{i}]");
                    return true;
            }
        }

        value = type == typeof(Vector2)
            ? new Vector2(components[0], components[1])
            : type == typeof(Vector3)
                ? new Vector3(components[0], components[1], components[2])
                : new Vector4(components[0], components[1], components[2], components[3]);
        return true;
    }

    private static void AddTypeMismatch(BakeBindContext context, in BakeBindNode node, string expected)
        => context.AddError(
            BakeDiagnosticCodes.BinderTypeMismatch,
            $"'{node.MemberPath}' must be {expected}; got {Describe(node.Value)}.",
            node.Path,
            node.MemberPath);

    private static void AddInvalidValue(BakeBindContext context, in BakeBindNode node, string expected)
        => context.AddError(
            BakeDiagnosticCodes.BinderInvalidValue,
            $"'{node.MemberPath}' must be {expected}; got '{node.Value}'.",
            node.Path,
            node.MemberPath);

    private static string Describe(object? value)
        => value switch
        {
            null => "an absent value",
            string => "a string",
            bool => "a boolean",
            long => "an integer",
            double => "a float",
            TomlDateTime => "a date-time",
            TomlArray or TomlTableArray => "an array",
            TomlTable => "a table",
            _ => "a " + FormatType(value.GetType()),
        };

    private static string FormatType(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        string name = type.GetGenericTypeDefinition().FullName ?? type.Name;
        int arity = name.IndexOf('`', StringComparison.Ordinal);
        if (arity >= 0)
        {
            name = name[..arity];
        }

        return name + "<" + string.Join(", ", type.GetGenericArguments().Select(FormatType)) + ">";
    }

    private static string? GetReferenceTargetTypeName(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AssetRef<>))
        {
            return type.GetGenericArguments()[0].FullName;
        }

        return null;
    }

    private static bool IsAssetReferenceType(Type type, out bool isData)
    {
        if (type == typeof(DataRef))
        {
            isData = true;
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AssetRef<>))
        {
            isData = false;
            return true;
        }

        isData = false;
        return false;
    }

    /// <summary>
    /// Gets whether an absent member is governed by the §7.3 non-nullable-reference rule: the shape
    /// reports non-nullability, the member type is a reference type, and it is not an
    /// enumerable/dictionary shape (arrays, lists, and dictionaries are exempt and stay null).
    /// </summary>
    private static bool IsNonNullableReference(ITypeShape shape, bool isNonNullable)
        => isNonNullable
            && !shape.Type.IsValueType
            && shape.Kind is not TypeShapeKind.Enumerable and not TypeShapeKind.Dictionary;

    private sealed class BindState
    {
        internal BindState(BakeBindContext context, BakeBindNode node)
        {
            Context = context;
            Node = node;
        }

        internal BakeBindContext Context { get; }

        internal BakeBindNode Node { get; }
    }

    private sealed class ShapeVisitor : TypeShapeVisitor
    {
        public override object? VisitObject<T>(IObjectTypeShape<T> shape, object? state)
        {
            var bindState = (BindState)state!;
            BakeBindContext context = bindState.Context;
            BakeBindNode node = bindState.Node;

            if (node.Value is not TomlTable table)
            {
                AddTypeMismatch(context, node, "a table");
                return default(T)!;
            }

            IConstructorShape? constructor = shape.Constructor;
            if (constructor is null)
            {
                context.AddError(
                    BakeDiagnosticCodes.BinderUnsupportedLeaf,
                    $"The mapped type '{FormatType(typeof(T))}' has no supported constructor.",
                    node.Path,
                    node.MemberPath);
                return default(T)!;
            }

            var objectState = new ObjectBindState(context, shape, table, node);
            return constructor.Accept(new ConstructorVisitor(), objectState);
        }

        public override object? VisitEnum<TEnum, TUnderlying>(
            IEnumTypeShape<TEnum, TUnderlying> shape,
            object? state)
        {
            var bindState = (BindState)state!;
            BakeBindContext context = bindState.Context;
            BakeBindNode node = bindState.Node;

            string expected = string.Join(
                ", ",
                shape.Members.Keys.Select(
                    name => "'" + JsonNamingPolicy.CamelCase.ConvertName(name) + "'"));

            if (node.Value is not string text)
            {
                if (node.Value is long or double)
                {
                    context.AddError(
                        BakeDiagnosticCodes.BinderEnumValue,
                        $"'{node.MemberPath}' must be one of the enum member names "
                            + $"({expected}); an integer enum value is not accepted.",
                        node.Path,
                        node.MemberPath);
                }
                else
                {
                    AddTypeMismatch(context, node, "an enum member name");
                }

                return default(TEnum)!;
            }

            foreach (KeyValuePair<string, TUnderlying> member in shape.Members)
            {
                if (string.Equals(
                    JsonNamingPolicy.CamelCase.ConvertName(member.Key),
                    text,
                    StringComparison.Ordinal))
                {
                    return member.Value;
                }
            }

            context.AddError(
                BakeDiagnosticCodes.BinderEnumValue,
                $"'{text}' is not a valid value for {FormatType(typeof(TEnum))}; "
                    + $"expected one of: {expected}.",
                node.Path,
                node.MemberPath);
            return default(TEnum)!;
        }

        public override object? VisitOptional<TOptional, TElement>(
            IOptionalTypeShape<TOptional, TElement> shape,
            object? state)
        {
            var bindState = (BindState)state!;
            BakeBindContext context = bindState.Context;
            BakeBindNode node = bindState.Node;

            if (!TryBindShape(shape.ElementType, context, node, out object? element))
            {
                return default(TOptional)!;
            }

            return shape.GetSomeConstructor()((TElement)element!);
        }

        public override object? VisitEnumerable<TEnumerable, TElement>(
            IEnumerableTypeShape<TEnumerable, TElement> shape,
            object? state)
        {
            var bindState = (BindState)state!;
            BakeBindContext context = bindState.Context;
            BakeBindNode node = bindState.Node;

            IReadOnlyList<object?>? items = node.Value switch
            {
                TomlArray array => array.Cast<object?>().ToArray(),
                TomlTableArray tableArray => tableArray.Cast<object?>().ToArray(),
                _ => null,
            };

            if (items is null)
            {
                AddTypeMismatch(context, node, "an array");
                return default(TEnumerable)!;
            }

            if (shape.ConstructionStrategy == CollectionConstructionStrategy.None)
            {
                AddUnsupportedCollection(context, node, typeof(TElement));
                return default(TEnumerable)!;
            }

            var values = new List<TElement>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                BakeBindNode elementNode = node.Element(i, items[i]);
                if (!TryBindShape(shape.ElementType, context, elementNode, out object? item))
                {
                    return default(TEnumerable)!;
                }

                values.Add((TElement)item!);
            }

            switch (shape.ConstructionStrategy)
            {
                case CollectionConstructionStrategy.Parameterized:
                    return shape.GetParameterizedConstructor()(
                        CollectionsMarshal.AsSpan(values));

                case CollectionConstructionStrategy.Mutable:
                    MutableCollectionConstructor<TElement, TEnumerable> create =
                        shape.GetDefaultConstructor();
                    TEnumerable collection = create();
                    EnumerableAppender<TEnumerable, TElement> append = shape.GetAppender();
                    foreach (TElement value in values)
                    {
                        append(ref collection, value);
                    }

                    return collection;

                default:
                    AddUnsupportedCollection(context, node, typeof(TElement));
                    return default(TEnumerable)!;
            }
        }

        public override object? VisitDictionary<TDictionary, TKey, TValue>(
            IDictionaryTypeShape<TDictionary, TKey, TValue> shape,
            object? state)
        {
            var bindState = (BindState)state!;
            BakeBindContext context = bindState.Context;
            BakeBindNode node = bindState.Node;

            if (node.Value is not TomlTable table)
            {
                AddTypeMismatch(context, node, "a table");
                return default(TDictionary)!;
            }

            if (typeof(TKey) != typeof(string))
            {
                context.AddError(
                    BakeDiagnosticCodes.BinderUnsupportedCollection,
                    $"'{node.MemberPath}' uses dictionary key type '{FormatType(typeof(TKey))}'; "
                        + "only Dictionary<string, T> is supported.",
                    node.Path,
                    node.MemberPath);
                return default(TDictionary)!;
            }

            if (shape.ConstructionStrategy == CollectionConstructionStrategy.None)
            {
                AddUnsupportedCollection(context, node, typeof(TValue));
                return default(TDictionary)!;
            }

            var values = new List<KeyValuePair<TKey, TValue>>(table.Count);
            foreach (string key in table.Keys)
            {
                BakeBindNode valueNode = node.DictionaryValue(key, table[key]);
                if (!TryBindShape(shape.ValueType, context, valueNode, out object? item))
                {
                    return default(TDictionary)!;
                }

                values.Add(new KeyValuePair<TKey, TValue>((TKey)(object)key, (TValue)item!));
            }

            switch (shape.ConstructionStrategy)
            {
                case CollectionConstructionStrategy.Parameterized:
                    return shape.GetParameterizedConstructor()(
                        CollectionsMarshal.AsSpan(values));

                case CollectionConstructionStrategy.Mutable:
                    MutableCollectionConstructor<TKey, TDictionary> create =
                        shape.GetDefaultConstructor();
                    TDictionary dictionary = create();
                    DictionaryInserter<TDictionary, TKey, TValue> insert =
                        shape.GetInserter(DictionaryInsertionMode.Throw);
                    foreach (KeyValuePair<TKey, TValue> pair in values)
                    {
                        _ = insert(ref dictionary, pair.Key, pair.Value);
                    }

                    return dictionary;

                default:
                    AddUnsupportedCollection(context, node, typeof(TValue));
                    return default(TDictionary)!;
            }
        }

        public override object? VisitSurrogate<T, TSurrogate>(
            ISurrogateTypeShape<T, TSurrogate> shape,
            object? state)
        {
            var bindState = (BindState)state!;
            BakeBindContext context = bindState.Context;
            BakeBindNode node = bindState.Node;

            if (!TryBindShape(shape.SurrogateType, context, node, out object? surrogate))
            {
                return default(T)!;
            }

            T value;
            try
            {
                value = shape.Marshaler.Unmarshal((TSurrogate)surrogate!)!;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                context.AddError(
                    BakeDiagnosticCodes.BinderInvalidValue,
                    $"'{node.MemberPath}' is not a valid {FormatType(typeof(T))}: "
                        + exception.Message,
                    node.Path,
                    node.MemberPath);
                return default(T)!;
            }

            if (IsAssetReferenceType(typeof(T), out bool isData) && surrogate is string path)
            {
                context.AddReference(
                    node.MemberPath,
                    path,
                    isData,
                    node.Path,
                    GetReferenceTargetTypeName(typeof(T)));
            }

            return value;
        }

        private static void AddUnsupportedCollection(
            BakeBindContext context,
            in BakeBindNode node,
            Type elementType)
            => context.AddError(
                BakeDiagnosticCodes.BinderUnsupportedCollection,
                $"'{node.MemberPath}' uses a collection type that cannot be constructed from TOML "
                    + $"for element type '{FormatType(elementType)}'.",
                node.Path,
                node.MemberPath);
    }

    private sealed class ObjectBindState
    {
        internal ObjectBindState(
            BakeBindContext context,
            IObjectTypeShape objectShape,
            TomlTable table,
            BakeBindNode node)
        {
            Context = context;
            ObjectShape = objectShape;
            Table = table;
            Node = node;
        }

        internal BakeBindContext Context { get; }

        internal IObjectTypeShape ObjectShape { get; }

        internal TomlTable Table { get; }

        internal BakeBindNode Node { get; }
    }

    private delegate void MemberSetter<TDeclaring>(ref TDeclaring declaring, object? value);

    private delegate object? MemberGetter<TDeclaring>(ref TDeclaring declaring);

    private sealed class MemberBinding
    {
        internal MemberBinding(
            int parameterIndex,
            string key,
            string memberPath,
            bool required,
            bool nonNullable,
            bool memberInitializer,
            IPropertyShape? property)
        {
            ParameterIndex = parameterIndex;
            Key = key;
            MemberPath = memberPath;
            Required = required;
            NonNullable = nonNullable;
            MemberInitializer = memberInitializer;
            Property = property;
        }

        internal int ParameterIndex { get; }

        internal string Key { get; }

        internal string MemberPath { get; }

        internal bool Required { get; }

        internal bool NonNullable { get; }

        internal bool MemberInitializer { get; }

        internal IPropertyShape? Property { get; }
    }

    private sealed class PropertyBinding
    {
        internal PropertyBinding(
            IPropertyShape property,
            string key,
            string memberPath,
            bool required,
            bool nonNullable)
        {
            Property = property;
            Key = key;
            MemberPath = memberPath;
            Required = required;
            NonNullable = nonNullable;
        }

        internal IPropertyShape Property { get; }

        internal string Key { get; }

        internal string MemberPath { get; }

        internal bool Required { get; }

        internal bool NonNullable { get; }
    }

    private sealed class ConstructorVisitor : TypeShapeVisitor
    {
        public override object? VisitConstructor<TDeclaringType, TArgumentState>(
            IConstructorShape<TDeclaringType, TArgumentState> shape,
            object? state)
        {
            var objectState = (ObjectBindState)state!;

            if (shape.Parameters.Count == 0)
            {
                return BindDefaultConstructed(objectState, shape);
            }

            BakeBindContext context = objectState.Context;
            TomlTable table = objectState.Table;
            BakeBindNode node = objectState.Node;

            TArgumentState argumentState = shape.GetArgumentStateConstructor()();

            var bindings = new List<MemberBinding>(shape.Parameters.Count);
            var setters = new List<ArgumentSetter<TArgumentState>>(shape.Parameters.Count);
            var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < shape.Parameters.Count; i++)
            {
                IParameterShape parameter = shape.Parameters[i];
                IPropertyShape? property = FindProperty(objectState.ObjectShape, parameter);

                if (IsIgnored(parameter, property))
                {
                    continue;
                }

                string key = ResolveKeyName(parameter, property);
                knownKeys.Add(key);

                string memberPath = node.MemberPath.Length == 0
                    ? key
                    : node.MemberPath + "." + key;

                bool required = IsRequired(parameter, property);
                bindings.Add(new MemberBinding(
                    i,
                    key,
                    memberPath,
                    required,
                    !required && RequiresNonNullableValue(parameter),
                    parameter.Kind == ParameterKind.MemberInitializer,
                    property));

                setters.Add(CreateSetter<TArgumentState>(parameter));
            }

            ReportUnknownKeys(table, knownKeys, node, context);

            var absentMemberInitializers = new List<int>();
            for (int i = 0; i < bindings.Count; i++)
            {
                MemberBinding binding = bindings[i];
                if (!TryResolveKey(
                    table,
                    binding.Key,
                    binding.MemberPath,
                    node.Path,
                    context,
                    out string actualKey))
                {
                    if (binding.Required)
                    {
                        context.AddError(
                            BakeDiagnosticCodes.BinderMissingRequired,
                            $"Missing required key '{binding.MemberPath}'.",
                            node.Path,
                            binding.MemberPath);
                    }
                    else if (binding.NonNullable)
                    {
                        if (binding.MemberInitializer)
                        {
                            // A member initializer may still supply a non-null value, so the
                            // resulting value is checked after construction.
                            absentMemberInitializers.Add(i);
                        }
                        else
                        {
                            context.AddError(
                                BakeDiagnosticCodes.BinderMissingRequired,
                                $"Missing key '{binding.MemberPath}'; the non-nullable reference "
                                    + "parameter has no non-null default.",
                                node.Path,
                                binding.MemberPath);
                        }
                    }

                    IParameterShape parameter = shape.Parameters[binding.ParameterIndex];
                    if (parameter.HasDefaultValue)
                    {
                        setters[i](ref argumentState, parameter.DefaultValue);
                    }

                    continue;
                }

                IParameterShape boundParameter = shape.Parameters[binding.ParameterIndex];
                BakeBindNode memberNode = node.Child(actualKey, table[actualKey]);
                if (!TryBindShape(boundParameter.ParameterType, context, memberNode, out object? value))
                {
                    continue;
                }

                setters[i](ref argumentState, value);
            }

            TDeclaringType instance = shape.GetParameterizedConstructor()(ref argumentState);
            ReportNullInitializers(
                bindings,
                absentMemberInitializers,
                node,
                context,
                ref instance);
            return instance;
        }

        private static void ReportNullInitializers<TDeclaringType>(
            List<MemberBinding> bindings,
            List<int> absentMemberInitializers,
            BakeBindNode node,
            BakeBindContext context,
            ref TDeclaringType instance)
        {
            foreach (int index in absentMemberInitializers)
            {
                MemberBinding binding = bindings[index];
                if (binding.Property is not { HasGetter: true } property)
                {
                    continue;
                }

                var getter = (MemberGetter<TDeclaringType>)property.Accept(
                    new PropertyGetterFactory(),
                    null)!;
                if (getter(ref instance) is not null)
                {
                    continue;
                }

                context.AddError(
                    BakeDiagnosticCodes.BinderMissingRequired,
                    $"Missing key '{binding.MemberPath}'; the non-nullable reference member is "
                        + "null after binding. Supply the key or initialize the member.",
                    node.Path,
                    binding.MemberPath);
            }
        }


        private static object? BindDefaultConstructed<TDeclaringType, TArgumentState>(
            ObjectBindState objectState,
            IConstructorShape<TDeclaringType, TArgumentState> shape)
        {
            Func<TDeclaringType>? create = null;
            try
            {
                create = shape.GetDefaultConstructor();
            }
            catch (InvalidOperationException)
            {
                // Reported below.
            }

            BakeBindContext context = objectState.Context;
            BakeBindNode node = objectState.Node;

            if (create is null)
            {
                context.AddError(
                    BakeDiagnosticCodes.BinderUnsupportedLeaf,
                    $"The mapped type '{FormatType(typeof(TDeclaringType))}' has no supported "
                        + "parameterless constructor.",
                    node.Path,
                    node.MemberPath);
                return default(TDeclaringType)!;
            }

            TDeclaringType instance = create();
            if (instance is null)
            {
                context.AddError(
                    BakeDiagnosticCodes.BinderUnsupportedLeaf,
                    $"The mapped type '{FormatType(typeof(TDeclaringType))}' could not be created.",
                    node.Path,
                    node.MemberPath);
                return default(TDeclaringType)!;
            }

            TomlTable table = objectState.Table;
            var bindings = new List<PropertyBinding>(objectState.ObjectShape.Properties.Count);
            var setters = new List<MemberSetter<TDeclaringType>>(objectState.ObjectShape.Properties.Count);
            var getters = new List<MemberGetter<TDeclaringType>?>(objectState.ObjectShape.Properties.Count);
            var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (IPropertyShape property in objectState.ObjectShape.Properties)
            {
                if (!property.HasSetter
                    || property.AttributeProvider.IsDefined<JsonIgnoreAttribute>())
                {
                    continue;
                }

                string key = ResolvePropertyKeyName(property);
                knownKeys.Add(key);

                string memberPath = node.MemberPath.Length == 0
                    ? key
                    : node.MemberPath + "." + key;

                bool required = property.AttributeProvider.IsDefined<JsonRequiredAttribute>();
                bindings.Add(new PropertyBinding(
                    property,
                    key,
                    memberPath,
                    required,
                    !required && RequiresNonNullableValue(property)));

                object? setter = property.Accept(new PropertySetterFactory(), null);
                setters.Add((MemberSetter<TDeclaringType>)setter!);
                getters.Add(
                    property.HasGetter && RequiresNonNullableValue(property)
                        ? (MemberGetter<TDeclaringType>)property.Accept(
                            new PropertyGetterFactory(),
                            null)!
                        : null);
            }

            ReportUnknownKeys(table, knownKeys, node, context);

            var absent = new List<int>();
            for (int i = 0; i < bindings.Count; i++)
            {
                PropertyBinding binding = bindings[i];
                if (!TryResolveKey(
                    table,
                    binding.Key,
                    binding.MemberPath,
                    node.Path,
                    context,
                    out string actualKey))
                {
                    if (binding.Required)
                    {
                        context.AddError(
                            BakeDiagnosticCodes.BinderMissingRequired,
                            $"Missing required key '{binding.MemberPath}'.",
                            node.Path,
                            binding.MemberPath);
                    }

                    absent.Add(i);
                    continue;
                }

                BakeBindNode memberNode = node.Child(actualKey, table[actualKey]);
                if (!TryBindShape(
                    binding.Property.PropertyType,
                    context,
                    memberNode,
                    out object? value))
                {
                    continue;
                }

                setters[i](ref instance, value);
            }

            ReportNullNonNullableReferences(bindings, getters, absent, node, context, ref instance);
            return instance;
        }

        private static void ReportNullNonNullableReferences<TDeclaringType>(
            List<PropertyBinding> bindings,
            List<MemberGetter<TDeclaringType>?> getters,
            List<int> absent,
            BakeBindNode node,
            BakeBindContext context,
            ref TDeclaringType instance)
        {
            foreach (int index in absent)
            {
                PropertyBinding binding = bindings[index];
                if (!binding.NonNullable || getters[index] is not { } getter)
                {
                    continue;
                }

                if (getter(ref instance) is not null)
                {
                    continue;
                }

                context.AddError(
                    BakeDiagnosticCodes.BinderMissingRequired,
                    $"Missing key '{binding.MemberPath}'; the non-nullable reference member is "
                        + "null after binding. Supply the key or initialize the member.",
                    node.Path,
                    binding.MemberPath);
            }
        }

        private static ArgumentSetter<TArgumentState> CreateSetter<TArgumentState>(
            IParameterShape parameter)
        {
            object? setter = parameter.Accept(new SetterFactory(), null);
            return (ArgumentSetter<TArgumentState>)setter!;
        }

        private static void ReportUnknownKeys(
            TomlTable table,
            HashSet<string> knownKeys,
            BakeBindNode node,
            BakeBindContext context)
        {
            foreach (string tableKey in table.Keys)
            {
                if (knownKeys.Contains(tableKey))
                {
                    continue;
                }

                string unknownMemberPath = node.MemberPath.Length == 0
                    ? tableKey
                    : node.MemberPath + "." + tableKey;
                context.AddKeyError(
                    BakeDiagnosticCodes.BinderUnknownKey,
                    $"Unknown key '{unknownMemberPath}'.",
                    node.Path.Child(tableKey),
                    unknownMemberPath);
            }
        }

        private static bool TryResolveKey(
            TomlTable table,
            string key,
            string memberPath,
            TomlPath parentPath,
            BakeBindContext context,
            out string actualKey)
        {
            actualKey = key;
            string? match = null;

            foreach (string candidate in table.Keys)
            {
                if (!string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (match is not null)
                {
                    context.AddKeyError(
                        BakeDiagnosticCodes.BinderAmbiguousKey,
                        $"Key '{candidate}' and '{match}' both match the member '{memberPath}'; "
                            + "source keys must differ beyond casing.",
                        parentPath.Child(candidate),
                        memberPath);
                    return false;
                }

                match = candidate;
            }

            if (match is null)
            {
                return false;
            }

            actualKey = match;
            return true;
        }

        private static IPropertyShape? FindProperty(
            IObjectTypeShape objectShape,
            IParameterShape parameter)
        {
            if (parameter.MemberInfo is { } member)
            {
                foreach (IPropertyShape property in objectShape.Properties)
                {
                    if (property.MemberInfo is { } propertyMember
                        && string.Equals(
                            propertyMember.Name,
                            member.Name,
                            StringComparison.Ordinal))
                    {
                        return property;
                    }
                }
            }

            foreach (IPropertyShape property in objectShape.Properties)
            {
                if (string.Equals(property.Name, parameter.Name, StringComparison.Ordinal))
                {
                    return property;
                }
            }

            return null;
        }

        private static bool IsIgnored(IParameterShape parameter, IPropertyShape? property)
            => parameter.AttributeProvider.IsDefined<JsonIgnoreAttribute>()
                || (property is not null
                    && property.AttributeProvider.IsDefined<JsonIgnoreAttribute>());

        private static bool IsRequired(IParameterShape parameter, IPropertyShape? property)
            => parameter.IsRequired
                || parameter.AttributeProvider.IsDefined<JsonRequiredAttribute>()
                || (property is not null
                    && property.AttributeProvider.IsDefined<JsonRequiredAttribute>());

        private static bool RequiresNonNullableValue(IParameterShape parameter)
            => (!parameter.HasDefaultValue || parameter.DefaultValue is null)
                && IsNonNullableReference(parameter.ParameterType, parameter.IsNonNullable);

        private static bool RequiresNonNullableValue(IPropertyShape property)
            => IsNonNullableReference(
                property.PropertyType,
                property.IsGetterNonNullable || property.IsSetterNonNullable);

        private static string ResolveKeyName(IParameterShape parameter, IPropertyShape? property)
        {
            JsonPropertyNameAttribute? attribute =
                parameter.AttributeProvider.GetCustomAttribute<JsonPropertyNameAttribute>()
                ?? property?.AttributeProvider.GetCustomAttribute<JsonPropertyNameAttribute>();

            if (attribute is not null && !string.IsNullOrEmpty(attribute.Name))
            {
                return attribute.Name;
            }

            return JsonNamingPolicy.CamelCase.ConvertName(parameter.Name);
        }

        private static string ResolvePropertyKeyName(IPropertyShape property)
        {
            JsonPropertyNameAttribute? attribute =
                property.AttributeProvider.GetCustomAttribute<JsonPropertyNameAttribute>();

            if (attribute is not null && !string.IsNullOrEmpty(attribute.Name))
            {
                return attribute.Name;
            }

            return JsonNamingPolicy.CamelCase.ConvertName(property.Name);
        }
    }

    private sealed class SetterFactory : TypeShapeVisitor
    {
        public override object? VisitParameter<TArgumentState, TParameterType>(
            IParameterShape<TArgumentState, TParameterType> shape,
            object? state)
        {
            Setter<TArgumentState, TParameterType> setter = shape.GetSetter();

            void Apply(ref TArgumentState argumentState, object? value)
                => setter(ref argumentState, (TParameterType)value!);

            return (ArgumentSetter<TArgumentState>)Apply;
        }
    }

    private sealed class PropertySetterFactory : TypeShapeVisitor
    {
        public override object? VisitProperty<TDeclaringType, TPropertyType>(
            IPropertyShape<TDeclaringType, TPropertyType> shape,
            object? state)
        {
            Setter<TDeclaringType, TPropertyType> setter = shape.GetSetter();

            void Apply(ref TDeclaringType declaring, object? value)
                => setter(ref declaring, (TPropertyType)value!);

            return (MemberSetter<TDeclaringType>)Apply;
        }
    }

    private sealed class PropertyGetterFactory : TypeShapeVisitor
    {
        public override object? VisitProperty<TDeclaringType, TPropertyType>(
            IPropertyShape<TDeclaringType, TPropertyType> shape,
            object? state)
        {
            Getter<TDeclaringType, TPropertyType> getter = shape.GetGetter();

            object? Read(ref TDeclaringType declaring) => getter(ref declaring);

            return (MemberGetter<TDeclaringType>)Read;
        }
    }
}
