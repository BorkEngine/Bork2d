using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PolyType;
using PolyType.Abstractions;

namespace Bork2d.Bake.Toml;

/// <summary>
/// Writes a shape's settings object as canonical TOML for a missing required sidecar.
/// </summary>
/// <remarks>
/// <para>
/// The writer is shape-driven and reflection-free: it walks the compiled properties in shape order,
/// applies the same camelCase/<see cref="JsonPropertyNameAttribute"/>/<see cref="JsonIgnoreAttribute"/>
/// rules as the binder, and supports scalar leaves only (bool, integer, float, double, string, enum).
/// A null member is skipped, which is how an optional setting stays absent.
/// </para>
/// <para>
/// Output is deterministic: bare keys where TOML allows them, quoted keys otherwise, LF line
/// endings, and no trailing blank line. A nested object, collection, or reference member is an
/// unsupported sidecar value and is reported as a writer failure.
/// </para>
/// </remarks>
internal static class TomlSidecarWriter
{
    /// <summary>Writes one settings object as canonical TOML.</summary>
    /// <param name="shape">The compiled root shape, which must be an object shape.</param>
    /// <param name="value">The sidecar instance to write.</param>
    /// <param name="text">The canonical TOML text when successful.</param>
    /// <param name="reason">The failure reason when unsuccessful.</param>
    /// <returns><see langword="true"/> when the value was written.</returns>
    internal static bool TryWrite(
        ITypeShape shape,
        object value,
        out string text,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(value);

        text = string.Empty;

        if (shape.Kind != TypeShapeKind.Object || shape is not IObjectTypeShape objectShape)
        {
            reason = $"the sidecar type '{shape.Type.FullName}' is not an object shape";
            return false;
        }

        var builder = new StringBuilder();
        foreach (IPropertyShape property in objectShape.Properties)
        {
            if (!property.HasGetter
                || property.AttributeProvider.IsDefined<JsonIgnoreAttribute>())
            {
                continue;
            }

            object? memberValue = GetValue(property, value);
            if (memberValue is null)
            {
                continue;
            }

            string key = ResolveKeyName(property);
            if (!TryFormatValue(property.PropertyType, memberValue, out string formatted, out reason))
            {
                return false;
            }

            builder.Append(FormatKey(key)).Append(" = ").Append(formatted).Append('\n');
        }

        text = builder.ToString();
        reason = null;
        return true;
    }

    private static object? GetValue(IPropertyShape property, object value)
        => property.Accept(new PropertyValueGetter(), value);

    private static bool TryFormatValue(
        ITypeShape shape,
        object value,
        out string formatted,
        out string? reason)
    {
        switch (value)
        {
            case string text:
                formatted = Quote(text);
                reason = null;
                return true;

            case bool boolean:
                formatted = boolean ? "true" : "false";
                reason = null;
                return true;

            case long or int or short or sbyte or byte or ushort or uint or ulong
                or nint or nuint:
                formatted = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
                reason = null;
                return true;

            case float single:
                if (!float.IsFinite(single))
                {
                    formatted = string.Empty;
                    reason = "a floating-point member is not finite";
                    return false;
                }

                formatted = EnsureFloatText(single.ToString("R", CultureInfo.InvariantCulture));
                reason = null;
                return true;

            case double real:
                if (!double.IsFinite(real))
                {
                    formatted = string.Empty;
                    reason = "a floating-point member is not finite";
                    return false;
                }

                formatted = EnsureFloatText(real.ToString("R", CultureInfo.InvariantCulture));
                reason = null;
                return true;
        }

        if (shape.Kind == TypeShapeKind.Enum)
        {
            string? name = (string?)shape.Accept(new EnumNameVisitor(), value);
            if (name is null)
            {
                formatted = string.Empty;
                reason = $"the enum value '{value}' has no matching member name";
                return false;
            }

            formatted = Quote(name);
            reason = null;
            return true;
        }

        formatted = string.Empty;
        reason = $"the member type '{shape.Type.FullName}' is not a supported sidecar scalar; "
            + "sidecars accept bool, integer, float, double, string, and enum settings only";
        return false;
    }

    private static string EnsureFloatText(string text)
        => text.IndexOfAny(['.', 'e', 'E']) >= 0 ? text : text + ".0";

    private static string ResolveKeyName(IPropertyShape property)
    {
        JsonPropertyNameAttribute? attribute =
            property.AttributeProvider.GetCustomAttribute<JsonPropertyNameAttribute>();
        if (attribute is not null && !string.IsNullOrEmpty(attribute.Name))
        {
            return attribute.Name;
        }

        return JsonNamingPolicy.CamelCase.ConvertName(property.Name);
    }

    private static string FormatKey(string key)
    {
        if (key.Length == 0)
        {
            return Quote(key);
        }

        foreach (char character in key)
        {
            bool bare = character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_' or '-';
            if (!bare)
            {
                return Quote(key);
            }
        }

        return key;
    }

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (char character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    if (character < 0x20 || character == 0x7F)
                    {
                        builder.Append("\\u").Append(
                            ((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private sealed class PropertyValueGetter : TypeShapeVisitor
    {
        public override object? VisitProperty<TDeclaringType, TPropertyType>(
            IPropertyShape<TDeclaringType, TPropertyType> shape,
            object? state)
        {
            if (state is not TDeclaringType declaring)
            {
                return null;
            }

            Getter<TDeclaringType, TPropertyType> getter = shape.GetGetter();
            return getter(ref declaring);
        }
    }

    private sealed class EnumNameVisitor : TypeShapeVisitor
    {
        public override object? VisitEnum<TEnum, TUnderlying>(
            IEnumTypeShape<TEnum, TUnderlying> shape,
            object? state)
        {
            if (state is not TEnum value)
            {
                return null;
            }

            foreach (KeyValuePair<string, TUnderlying> member in shape.Members)
            {
                TEnum candidate = (TEnum)Enum.ToObject(typeof(TEnum), member.Value);
                if (EqualityComparer<TEnum>.Default.Equals(candidate, value))
                {
                    return JsonNamingPolicy.CamelCase.ConvertName(member.Key);
                }
            }

            return null;
        }
    }
}
