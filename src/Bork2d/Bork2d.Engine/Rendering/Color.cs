using System.Globalization;
using System.Numerics;
using PolyType;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// An immutable linear RGBA color stored as a <see cref="Vector4"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each component is a linear value, normally in <c>[0, 1]</c>; alpha <c>1</c> is opaque and <c>0</c>
/// is transparent. Sprite tints and clear colors are authored in linear space; sRGB conversion
/// happens at output.
/// </para>
/// <para>
/// The payload is one <see cref="Vector4"/>, so the value is exactly 16 bytes and arithmetic uses
/// the vector operators. Equality is exact .NET float equality per component, not Unity's
/// approximate comparison. The surface follows Unity 6000.6 <c>Color</c> except gamma conversion,
/// <c>Color32</c>, and <c>ColorUtility</c>, which this engine does not provide; OkLab conversion and
/// interpolation are added on top of that surface.
/// </para>
/// <para>
/// <see cref="ToOkLab"/> and <see cref="FromOkLab"/> convert between linear sRGB (D65) and OkLab,
/// and <see cref="LerpOkLab"/> and <see cref="LerpOkLabUnclamped"/> interpolate in OkLab. The
/// conversions apply no sRGB gamma decode or encode and do not clamp the result to the
/// <c>[0, 1]</c> gamut.
/// </para>
/// <para>
/// A color is serialized by <see cref="ColorMarshaler"/> as a four-element linear float array
/// (<c>[r, g, b, a]</c>); it has no generated object shape and stays non-<c>partial</c>.
/// </para>
/// </remarks>
[TypeShape(Marshaler = typeof(ColorMarshaler))]
public readonly struct Color : IEquatable<Color>, IFormattable
{
    private readonly Vector4 _value;

    /// <summary>Initializes a color from red, green, blue, and alpha components.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The alpha component.</param>
    public Color(float r, float g, float b, float a)
    {
        _value = new Vector4(r, g, b, a);
    }

    /// <summary>Initializes an opaque color from red, green, and blue components.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    public Color(float r, float g, float b)
    {
        _value = new Vector4(r, g, b, 1f);
    }

    private Color(Vector4 value)
    {
        _value = value;
    }

    /// <summary>Gets the red component.</summary>
    public float R => _value.X;

    /// <summary>Gets the green component.</summary>
    public float G => _value.Y;

    /// <summary>Gets the blue component.</summary>
    public float B => _value.Z;

    /// <summary>Gets the alpha component.</summary>
    public float A => _value.W;

    /// <summary>Gets one component by position: <c>0</c> red, <c>1</c> green, <c>2</c> blue, <c>3</c> alpha.</summary>
    /// <param name="index">The component position in <c>[0, 3]</c>.</param>
    /// <exception cref="IndexOutOfRangeException">The index is outside <c>[0, 3]</c>.</exception>
    public float this[int index] => index switch
    {
        0 => _value.X,
        1 => _value.Y,
        2 => _value.Z,
        3 => _value.W,
        _ => throw new IndexOutOfRangeException(
            $"Invalid Color index {index}; a color index must be 0, 1, 2, or 3."),
    };

    /// <summary>
    /// Gets the Unity luminance of the color: <c>0.299*R + 0.587*G + 0.114*B</c>.
    /// </summary>
    public float Grayscale => (0.299f * _value.X) + (0.587f * _value.Y) + (0.114f * _value.Z);

    /// <summary>Gets the largest of the red, green, and blue components.</summary>
    public float MaxColorComponent => MathF.Max(MathF.Max(_value.X, _value.Y), _value.Z);

    /// <summary>Linearly interpolates between two colors, clamping <paramref name="t"/> to <c>[0, 1]</c>.</summary>
    /// <param name="a">The color returned when <paramref name="t"/> is <c>0</c>.</param>
    /// <param name="b">The color returned when <paramref name="t"/> is <c>1</c>.</param>
    /// <param name="t">The interpolation ratio.</param>
    /// <returns>The interpolated color.</returns>
    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return LerpUnclamped(a, b, t);
    }

    /// <summary>Linearly interpolates between two colors without clamping <paramref name="t"/>.</summary>
    /// <param name="a">The color returned when <paramref name="t"/> is <c>0</c>.</param>
    /// <param name="b">The color returned when <paramref name="t"/> is <c>1</c>.</param>
    /// <param name="t">The interpolation ratio.</param>
    /// <returns>The interpolated color.</returns>
    public static Color LerpUnclamped(Color a, Color b, float t)
        => new(a._value + ((b._value - a._value) * t));

    /// <summary>Converts the color from linear sRGB (D65) to OkLab.</summary>
    /// <param name="l">The OkLab lightness; <c>0</c> is black and <c>1</c> is white.</param>
    /// <param name="a">The OkLab <c>a</c> opponent axis, from green (negative) to red (positive).</param>
    /// <param name="b">The OkLab <c>b</c> opponent axis, from blue (negative) to yellow (positive).</param>
    /// <remarks>
    /// <para>
    /// The red, green, and blue components are read as linear sRGB with a D65 white point and are
    /// converted directly to OkLab; no sRGB gamma decode is applied. The alpha component is
    /// ignored and is not part of the result.
    /// </para>
    /// <para>
    /// The conversion allocates nothing and does not clamp the result to a gamut.
    /// </para>
    /// </remarks>
    public void ToOkLab(out float l, out float a, out float b)
    {
        float red = _value.X;
        float green = _value.Y;
        float blue = _value.Z;

        float lmsL = (0.4122214708f * red) + (0.5363325363f * green) + (0.0514459929f * blue);
        float lmsM = (0.2119034982f * red) + (0.6806995451f * green) + (0.1073969566f * blue);
        float lmsS = (0.0883024619f * red) + (0.2817188376f * green) + (0.6299787005f * blue);

        lmsL = MathF.Cbrt(lmsL);
        lmsM = MathF.Cbrt(lmsM);
        lmsS = MathF.Cbrt(lmsS);

        l = (0.2104542553f * lmsL) + (0.7936177850f * lmsM) - (0.0040720468f * lmsS);
        a = (1.9779984951f * lmsL) - (2.4285922050f * lmsM) + (0.4505937099f * lmsS);
        b = (0.0259040371f * lmsL) + (0.7827717662f * lmsM) - (0.8086757660f * lmsS);
    }

    /// <summary>Converts an OkLab color to linear sRGB (D65).</summary>
    /// <param name="l">The OkLab lightness; <c>0</c> is black and <c>1</c> is white.</param>
    /// <param name="a">The OkLab <c>a</c> opponent axis, from green (negative) to red (positive).</param>
    /// <param name="b">The OkLab <c>b</c> opponent axis, from blue (negative) to yellow (positive).</param>
    /// <param name="alpha">The alpha component of the result; <c>1</c> is opaque.</param>
    /// <returns>The linear sRGB color with the given alpha.</returns>
    /// <remarks>
    /// <para>
    /// The result is linear sRGB with a D65 white point; no sRGB gamma encode is applied. Alpha is
    /// independent of OkLab and is copied from <paramref name="alpha"/>.
    /// </para>
    /// <para>
    /// The result is not clamped to the <c>[0, 1]</c> gamut, so an OkLab color outside the linear
    /// sRGB gamut produces negative or greater-than-one components.
    /// </para>
    /// </remarks>
    public static Color FromOkLab(float l, float a, float b, float alpha = 1f)
    {
        float lmsL = l + (0.3963377774f * a) + (0.2158037573f * b);
        float lmsM = l - (0.1055613458f * a) - (0.0638541728f * b);
        float lmsS = l - (0.0894841775f * a) - (1.2914855480f * b);

        lmsL = lmsL * lmsL * lmsL;
        lmsM = lmsM * lmsM * lmsM;
        lmsS = lmsS * lmsS * lmsS;

        float red = (4.0767416621f * lmsL) - (3.3077115913f * lmsM) + (0.2309699292f * lmsS);
        float green = (-1.2684380046f * lmsL) + (2.6097574011f * lmsM) - (0.3413193965f * lmsS);
        float blue = (-0.0041960863f * lmsL) - (0.7034186147f * lmsM) + (1.7076147010f * lmsS);

        return new Color(red, green, blue, alpha);
    }

    /// <summary>Interpolates between two colors in OkLab, clamping <paramref name="t"/> to <c>[0, 1]</c>.</summary>
    /// <param name="a">The color returned when <paramref name="t"/> is <c>0</c>.</param>
    /// <param name="b">The color returned when <paramref name="t"/> is <c>1</c>.</param>
    /// <param name="t">The interpolation ratio.</param>
    /// <returns>The interpolated color.</returns>
    /// <remarks>
    /// <para>
    /// Both colors are converted from linear sRGB (D65) to OkLab without gamma conversion, the
    /// OkLab lightness and opponent axes are interpolated linearly, and the result is converted back
    /// to linear sRGB. The result is not clamped to the <c>[0, 1]</c> gamut.
    /// </para>
    /// <para>
    /// Alpha is interpolated linearly with the same <paramref name="t"/> and is independent of the
    /// OkLab interpolation.
    /// </para>
    /// </remarks>
    public static Color LerpOkLab(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return LerpOkLabUnclamped(a, b, t);
    }

    /// <summary>Interpolates between two colors in OkLab without clamping <paramref name="t"/>.</summary>
    /// <param name="a">The color returned when <paramref name="t"/> is <c>0</c>.</param>
    /// <param name="b">The color returned when <paramref name="t"/> is <c>1</c>.</param>
    /// <param name="t">The interpolation ratio.</param>
    /// <returns>The interpolated color, which may extrapolate beyond both inputs.</returns>
    /// <remarks>
    /// <para>
    /// This is <see cref="LerpOkLab"/> without clamping: a <paramref name="t"/> outside <c>[0, 1]</c>
    /// extrapolates along the OkLab path and can produce linear sRGB components outside the
    /// <c>[0, 1]</c> gamut.
    /// </para>
    /// <para>
    /// Both colors are converted from linear sRGB (D65) to OkLab without gamma conversion and the
    /// result is converted back to linear sRGB without a gamut clamp. Alpha is interpolated linearly
    /// with the same <paramref name="t"/>.
    /// </para>
    /// </remarks>
    public static Color LerpOkLabUnclamped(Color a, Color b, float t)
    {
        a.ToOkLab(out float startL, out float startA, out float startB);
        b.ToOkLab(out float endL, out float endA, out float endB);

        float l = startL + ((endL - startL) * t);
        float okA = startA + ((endA - startA) * t);
        float okB = startB + ((endB - startB) * t);
        float alpha = a.A + ((b.A - a.A) * t);

        return FromOkLab(l, okA, okB, alpha);
    }

    /// <summary>Creates an RGB color from hue, saturation, and value, with high-dynamic-range output.</summary>
    /// <param name="h">The hue in <c>[0, 1]</c>.</param>
    /// <param name="s">The saturation in <c>[0, 1]</c>.</param>
    /// <param name="v">The value.</param>
    /// <returns>The color; its alpha is <c>1</c>.</returns>
    public static Color HSVToRGB(float h, float s, float v) => HSVToRGB(h, s, v, true);

    /// <summary>Creates an RGB color from hue, saturation, and value.</summary>
    /// <param name="h">The hue in <c>[0, 1]</c>.</param>
    /// <param name="s">The saturation in <c>[0, 1]</c>.</param>
    /// <param name="v">The value.</param>
    /// <param name="hdr">
    /// When <see langword="true"/>, components may exceed <c>1</c>; when <see langword="false"/>, the
    /// general conversion path clamps them to <c>[0, 1]</c>.
    /// </param>
    /// <returns>The color; its alpha is <c>1</c>.</returns>
    public static Color HSVToRGB(float h, float s, float v, bool hdr = true)
    {
        Vector4 result;
        if (s == 0f)
        {
            result = new Vector4(v, v, v, 1f);
        }
        else if (v == 0f)
        {
            result = new Vector4(0f, 0f, 0f, 1f);
        }
        else
        {
            float hToFloor = h * 6f;
            int whole = (int)MathF.Floor(hToFloor);
            float t = hToFloor - whole;
            float var1 = v * (1f - s);
            float var2 = v * (1f - (s * t));
            float var3 = v * (1f - (s * (1f - t)));

            result = whole switch
            {
                0 => new Vector4(v, var3, var1, 1f),
                1 => new Vector4(var2, v, var1, 1f),
                2 => new Vector4(var1, v, var3, 1f),
                3 => new Vector4(var1, var2, v, 1f),
                4 => new Vector4(var3, var1, v, 1f),
                5 => new Vector4(v, var1, var2, 1f),
                6 => new Vector4(v, var3, var1, 1f),
                -1 => new Vector4(v, var1, var2, 1f),
                _ => new Vector4(0f, 0f, 0f, 1f),
            };

            if (!hdr)
            {
                result.X = Math.Clamp(result.X, 0f, 1f);
                result.Y = Math.Clamp(result.Y, 0f, 1f);
                result.Z = Math.Clamp(result.Z, 0f, 1f);
            }
        }

        return new Color(result);
    }

    /// <summary>Calculates the hue, saturation, and value of an RGB color.</summary>
    /// <param name="rgb">The color whose RGB components are converted.</param>
    /// <param name="h">The hue in <c>[0, 1]</c>.</param>
    /// <param name="s">The saturation in <c>[0, 1]</c>.</param>
    /// <param name="v">The value.</param>
    public static void RGBToHSV(Color rgb, out float h, out float s, out float v)
    {
        if (rgb.B > rgb.G && rgb.B > rgb.R)
        {
            RGBToHSVHelper(4f, rgb.B, rgb.R, rgb.G, out h, out s, out v);
        }
        else if (rgb.G > rgb.R)
        {
            RGBToHSVHelper(2f, rgb.G, rgb.B, rgb.R, out h, out s, out v);
        }
        else
        {
            RGBToHSVHelper(0f, rgb.R, rgb.G, rgb.B, out h, out s, out v);
        }
    }

    private static void RGBToHSVHelper(
        float offset,
        float dominantColor,
        float colorOne,
        float colorTwo,
        out float h,
        out float s,
        out float v)
    {
        v = dominantColor;
        if (v != 0f)
        {
            float small = colorOne > colorTwo ? colorTwo : colorOne;
            float difference = v - small;
            if (difference != 0f)
            {
                s = difference / v;
                h = offset + ((colorOne - colorTwo) / difference);
            }
            else
            {
                s = 0f;
                h = offset + (colorOne - colorTwo);
            }

            h /= 6f;
            if (h < 0f)
            {
                h += 1f;
            }
        }
        else
        {
            s = 0f;
            h = 0f;
        }
    }

    /// <summary>Returns the color formatted as <c>RGBA(r, g, b, a)</c> with <c>F3</c> components.</summary>
    /// <returns>The formatted color.</returns>
    public override string ToString() => ToString(null, null);

    /// <summary>Returns the color formatted as <c>RGBA(r, g, b, a)</c>.</summary>
    /// <param name="format">A numeric format string; <see langword="null"/> or empty means <c>F3</c>.</param>
    /// <returns>The formatted color with invariant culture.</returns>
    public string ToString(string? format) => ToString(format, null);

    /// <summary>Returns the color formatted as <c>RGBA(r, g, b, a)</c>.</summary>
    /// <param name="format">A numeric format string; <see langword="null"/> or empty means <c>F3</c>.</param>
    /// <param name="formatProvider">
    /// The provider for each component; <see langword="null"/> means invariant culture.
    /// </param>
    /// <returns>The formatted color.</returns>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        format = string.IsNullOrEmpty(format) ? "F3" : format;
        formatProvider ??= CultureInfo.InvariantCulture;
        return string.Format(
            CultureInfo.InvariantCulture,
            "RGBA({0}, {1}, {2}, {3})",
            _value.X.ToString(format, formatProvider),
            _value.Y.ToString(format, formatProvider),
            _value.Z.ToString(format, formatProvider),
            _value.W.ToString(format, formatProvider));
    }

    /// <summary>Determines whether this color has exactly the same components as another color.</summary>
    /// <param name="other">The color to compare.</param>
    /// <returns><see langword="true"/> when every component is exactly equal.</returns>
    public bool Equals(Color other) => _value == other._value;

    /// <summary>Determines whether this color equals another object.</summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns><see langword="true"/> when <paramref name="obj"/> is a color with exactly equal components.</returns>
    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    /// <summary>Returns a hash code consistent with exact component equality.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => _value.GetHashCode();

    /// <summary>Splits the color into its four components.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The alpha component.</param>
    public void Deconstruct(out float r, out float g, out float b, out float a)
    {
        r = _value.X;
        g = _value.Y;
        b = _value.Z;
        a = _value.W;
    }

    /// <summary>Adds two colors component by component.</summary>
    /// <param name="a">The left color.</param>
    /// <param name="b">The right color.</param>
    /// <returns>The component-wise sum.</returns>
    public static Color operator +(Color a, Color b) => new(a._value + b._value);

    /// <summary>Subtracts one color from another component by component.</summary>
    /// <param name="a">The left color.</param>
    /// <param name="b">The right color.</param>
    /// <returns>The component-wise difference.</returns>
    public static Color operator -(Color a, Color b) => new(a._value - b._value);

    /// <summary>Multiplies two colors component by component.</summary>
    /// <param name="a">The left color.</param>
    /// <param name="b">The right color.</param>
    /// <returns>The component-wise product.</returns>
    public static Color operator *(Color a, Color b) => new(a._value * b._value);

    /// <summary>Scales every component of a color by a scalar.</summary>
    /// <param name="a">The color.</param>
    /// <param name="b">The scalar.</param>
    /// <returns>The scaled color.</returns>
    public static Color operator *(Color a, float b) => new(a._value * b);

    /// <summary>Scales every component of a color by a scalar.</summary>
    /// <param name="a">The scalar.</param>
    /// <param name="b">The color.</param>
    /// <returns>The scaled color.</returns>
    public static Color operator *(float a, Color b) => new(b._value * a);

    /// <summary>Divides every component of a color by a scalar.</summary>
    /// <param name="a">The color.</param>
    /// <param name="b">The scalar.</param>
    /// <returns>The scaled color.</returns>
    public static Color operator /(Color a, float b) => new(a._value / b);

    /// <summary>Determines whether two colors have exactly equal components.</summary>
    /// <param name="left">The left color.</param>
    /// <param name="right">The right color.</param>
    /// <returns><see langword="true"/> when every component is exactly equal.</returns>
    public static bool operator ==(Color left, Color right) => left._value == right._value;

    /// <summary>Determines whether two colors differ in any component.</summary>
    /// <param name="left">The left color.</param>
    /// <param name="right">The right color.</param>
    /// <returns><see langword="true"/> when any component differs exactly.</returns>
    public static bool operator !=(Color left, Color right) => !(left == right);

    /// <summary>Converts a color to its four-component vector.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The vector <c>(r, g, b, a)</c>.</returns>
    public static implicit operator Vector4(Color color) => color._value;

    /// <summary>Converts a four-component vector to a color.</summary>
    /// <param name="value">The vector <c>(r, g, b, a)</c>.</param>
    /// <returns>The color.</returns>
    public static implicit operator Color(Vector4 value) => new(value);

    /// <summary>The Unity <c>AliceBlue</c> preset; <c>RGBA(0.9411765f, 0.9725491f, 1f, 1f)</c>.</summary>
    public static readonly Color AliceBlue = new(0.9411765f, 0.9725491f, 1f, 1f);
    /// <summary>The Unity <c>AntiqueWhite</c> preset; <c>RGBA(0.9803922f, 0.9215687f, 0.8431373f, 1f)</c>.</summary>
    public static readonly Color AntiqueWhite = new(0.9803922f, 0.9215687f, 0.8431373f, 1f);
    /// <summary>The Unity <c>Aquamarine</c> preset; <c>RGBA(0.4980392f, 1f, 0.8313726f, 1f)</c>.</summary>
    public static readonly Color Aquamarine = new(0.4980392f, 1f, 0.8313726f, 1f);
    /// <summary>The Unity <c>Azure</c> preset; <c>RGBA(0.9411765f, 1f, 1f, 1f)</c>.</summary>
    public static readonly Color Azure = new(0.9411765f, 1f, 1f, 1f);
    /// <summary>The Unity <c>Beige</c> preset; <c>RGBA(0.9607844f, 0.9607844f, 0.8627452f, 1f)</c>.</summary>
    public static readonly Color Beige = new(0.9607844f, 0.9607844f, 0.8627452f, 1f);
    /// <summary>The Unity <c>Bisque</c> preset; <c>RGBA(1f, 0.8941177f, 0.7686275f, 1f)</c>.</summary>
    public static readonly Color Bisque = new(1f, 0.8941177f, 0.7686275f, 1f);
    /// <summary>The Unity <c>Black</c> preset; <c>RGBA(0f, 0f, 0f, 1f)</c>.</summary>
    public static readonly Color Black = new(0f, 0f, 0f, 1f);
    /// <summary>The Unity <c>BlanchedAlmond</c> preset; <c>RGBA(1f, 0.9215687f, 0.8039216f, 1f)</c>.</summary>
    public static readonly Color BlanchedAlmond = new(1f, 0.9215687f, 0.8039216f, 1f);
    /// <summary>The Unity <c>Blue</c> preset; <c>RGBA(0f, 0f, 1f, 1f)</c>.</summary>
    public static readonly Color Blue = new(0f, 0f, 1f, 1f);
    /// <summary>The Unity <c>BlueViolet</c> preset; <c>RGBA(0.5411765f, 0.1686275f, 0.8862746f, 1f)</c>.</summary>
    public static readonly Color BlueViolet = new(0.5411765f, 0.1686275f, 0.8862746f, 1f);
    /// <summary>The Unity <c>Brown</c> preset; <c>RGBA(0.6470588f, 0.1647059f, 0.1647059f, 1f)</c>.</summary>
    public static readonly Color Brown = new(0.6470588f, 0.1647059f, 0.1647059f, 1f);
    /// <summary>The Unity <c>Burlywood</c> preset; <c>RGBA(0.8705883f, 0.7215686f, 0.5294118f, 1f)</c>.</summary>
    public static readonly Color Burlywood = new(0.8705883f, 0.7215686f, 0.5294118f, 1f);
    /// <summary>The Unity <c>CadetBlue</c> preset; <c>RGBA(0.372549f, 0.6196079f, 0.627451f, 1f)</c>.</summary>
    public static readonly Color CadetBlue = new(0.372549f, 0.6196079f, 0.627451f, 1f);
    /// <summary>The Unity <c>Chartreuse</c> preset; <c>RGBA(0.4980392f, 1f, 0f, 1f)</c>.</summary>
    public static readonly Color Chartreuse = new(0.4980392f, 1f, 0f, 1f);
    /// <summary>The Unity <c>Chocolate</c> preset; <c>RGBA(0.8235295f, 0.4117647f, 0.1176471f, 1f)</c>.</summary>
    public static readonly Color Chocolate = new(0.8235295f, 0.4117647f, 0.1176471f, 1f);
    /// <summary>The Unity <c>Clear</c> preset; <c>RGBA(0f, 0f, 0f, 0f)</c>.</summary>
    public static readonly Color Clear = new(0f, 0f, 0f, 0f);
    /// <summary>The Unity <c>Coral</c> preset; <c>RGBA(1f, 0.4980392f, 0.3137255f, 1f)</c>.</summary>
    public static readonly Color Coral = new(1f, 0.4980392f, 0.3137255f, 1f);
    /// <summary>The Unity <c>CornflowerBlue</c> preset; <c>RGBA(0.3921569f, 0.5843138f, 0.9294118f, 1f)</c>.</summary>
    public static readonly Color CornflowerBlue = new(0.3921569f, 0.5843138f, 0.9294118f, 1f);
    /// <summary>The Unity <c>Cornsilk</c> preset; <c>RGBA(1f, 0.9725491f, 0.8627452f, 1f)</c>.</summary>
    public static readonly Color Cornsilk = new(1f, 0.9725491f, 0.8627452f, 1f);
    /// <summary>The Unity <c>Crimson</c> preset; <c>RGBA(0.8627452f, 0.07843138f, 0.2352941f, 1f)</c>.</summary>
    public static readonly Color Crimson = new(0.8627452f, 0.07843138f, 0.2352941f, 1f);
    /// <summary>The Unity <c>Cyan</c> preset; <c>RGBA(0f, 1f, 1f, 1f)</c>.</summary>
    public static readonly Color Cyan = new(0f, 1f, 1f, 1f);
    /// <summary>The Unity <c>DarkBlue</c> preset; <c>RGBA(0f, 0f, 0.5450981f, 1f)</c>.</summary>
    public static readonly Color DarkBlue = new(0f, 0f, 0.5450981f, 1f);
    /// <summary>The Unity <c>DarkCyan</c> preset; <c>RGBA(0f, 0.5450981f, 0.5450981f, 1f)</c>.</summary>
    public static readonly Color DarkCyan = new(0f, 0.5450981f, 0.5450981f, 1f);
    /// <summary>The Unity <c>DarkGoldenRod</c> preset; <c>RGBA(0.7215686f, 0.5254902f, 0.04313726f, 1f)</c>.</summary>
    public static readonly Color DarkGoldenRod = new(0.7215686f, 0.5254902f, 0.04313726f, 1f);
    /// <summary>The Unity <c>DarkGray</c> preset; <c>RGBA(0.6627451f, 0.6627451f, 0.6627451f, 1f)</c>.</summary>
    public static readonly Color DarkGray = new(0.6627451f, 0.6627451f, 0.6627451f, 1f);
    /// <summary>The Unity <c>DarkGreen</c> preset; <c>RGBA(0f, 0.3921569f, 0f, 1f)</c>.</summary>
    public static readonly Color DarkGreen = new(0f, 0.3921569f, 0f, 1f);
    /// <summary>The Unity <c>DarkKhaki</c> preset; <c>RGBA(0.7411765f, 0.7176471f, 0.4196079f, 1f)</c>.</summary>
    public static readonly Color DarkKhaki = new(0.7411765f, 0.7176471f, 0.4196079f, 1f);
    /// <summary>The Unity <c>DarkMagenta</c> preset; <c>RGBA(0.5450981f, 0f, 0.5450981f, 1f)</c>.</summary>
    public static readonly Color DarkMagenta = new(0.5450981f, 0f, 0.5450981f, 1f);
    /// <summary>The Unity <c>DarkOliveGreen</c> preset; <c>RGBA(0.3333333f, 0.4196079f, 0.1843137f, 1f)</c>.</summary>
    public static readonly Color DarkOliveGreen = new(0.3333333f, 0.4196079f, 0.1843137f, 1f);
    /// <summary>The Unity <c>DarkOrange</c> preset; <c>RGBA(1f, 0.5490196f, 0f, 1f)</c>.</summary>
    public static readonly Color DarkOrange = new(1f, 0.5490196f, 0f, 1f);
    /// <summary>The Unity <c>DarkOrchid</c> preset; <c>RGBA(0.6f, 0.1960784f, 0.8000001f, 1f)</c>.</summary>
    public static readonly Color DarkOrchid = new(0.6f, 0.1960784f, 0.8000001f, 1f);
    /// <summary>The Unity <c>DarkRed</c> preset; <c>RGBA(0.5450981f, 0f, 0f, 1f)</c>.</summary>
    public static readonly Color DarkRed = new(0.5450981f, 0f, 0f, 1f);
    /// <summary>The Unity <c>DarkSalmon</c> preset; <c>RGBA(0.9137256f, 0.5882353f, 0.4784314f, 1f)</c>.</summary>
    public static readonly Color DarkSalmon = new(0.9137256f, 0.5882353f, 0.4784314f, 1f);
    /// <summary>The Unity <c>DarkSeaGreen</c> preset; <c>RGBA(0.5607843f, 0.7372549f, 0.5607843f, 1f)</c>.</summary>
    public static readonly Color DarkSeaGreen = new(0.5607843f, 0.7372549f, 0.5607843f, 1f);
    /// <summary>The Unity <c>DarkSlateBlue</c> preset; <c>RGBA(0.282353f, 0.2392157f, 0.5450981f, 1f)</c>.</summary>
    public static readonly Color DarkSlateBlue = new(0.282353f, 0.2392157f, 0.5450981f, 1f);
    /// <summary>The Unity <c>DarkSlateGray</c> preset; <c>RGBA(0.1843137f, 0.3098039f, 0.3098039f, 1f)</c>.</summary>
    public static readonly Color DarkSlateGray = new(0.1843137f, 0.3098039f, 0.3098039f, 1f);
    /// <summary>The Unity <c>DarkTurquoise</c> preset; <c>RGBA(0f, 0.8078432f, 0.8196079f, 1f)</c>.</summary>
    public static readonly Color DarkTurquoise = new(0f, 0.8078432f, 0.8196079f, 1f);
    /// <summary>The Unity <c>DarkViolet</c> preset; <c>RGBA(0.5803922f, 0f, 0.8274511f, 1f)</c>.</summary>
    public static readonly Color DarkViolet = new(0.5803922f, 0f, 0.8274511f, 1f);
    /// <summary>The Unity <c>DeepPink</c> preset; <c>RGBA(1f, 0.07843138f, 0.5764706f, 1f)</c>.</summary>
    public static readonly Color DeepPink = new(1f, 0.07843138f, 0.5764706f, 1f);
    /// <summary>The Unity <c>DeepSkyBlue</c> preset; <c>RGBA(0f, 0.7490196f, 1f, 1f)</c>.</summary>
    public static readonly Color DeepSkyBlue = new(0f, 0.7490196f, 1f, 1f);
    /// <summary>The Unity <c>DimGray</c> preset; <c>RGBA(0.4117647f, 0.4117647f, 0.4117647f, 1f)</c>.</summary>
    public static readonly Color DimGray = new(0.4117647f, 0.4117647f, 0.4117647f, 1f);
    /// <summary>The Unity <c>DodgerBlue</c> preset; <c>RGBA(0.1176471f, 0.5647059f, 1f, 1f)</c>.</summary>
    public static readonly Color DodgerBlue = new(0.1176471f, 0.5647059f, 1f, 1f);
    /// <summary>The Unity <c>Firebrick</c> preset; <c>RGBA(0.6980392f, 0.1333333f, 0.1333333f, 1f)</c>.</summary>
    public static readonly Color Firebrick = new(0.6980392f, 0.1333333f, 0.1333333f, 1f);
    /// <summary>The Unity <c>FloralWhite</c> preset; <c>RGBA(1f, 0.9803922f, 0.9411765f, 1f)</c>.</summary>
    public static readonly Color FloralWhite = new(1f, 0.9803922f, 0.9411765f, 1f);
    /// <summary>The Unity <c>ForestGreen</c> preset; <c>RGBA(0.1333333f, 0.5450981f, 0.1333333f, 1f)</c>.</summary>
    public static readonly Color ForestGreen = new(0.1333333f, 0.5450981f, 0.1333333f, 1f);
    /// <summary>The Unity <c>Gainsboro</c> preset; <c>RGBA(0.8627452f, 0.8627452f, 0.8627452f, 1f)</c>.</summary>
    public static readonly Color Gainsboro = new(0.8627452f, 0.8627452f, 0.8627452f, 1f);
    /// <summary>The Unity <c>GhostWhite</c> preset; <c>RGBA(0.9725491f, 0.9725491f, 1f, 1f)</c>.</summary>
    public static readonly Color GhostWhite = new(0.9725491f, 0.9725491f, 1f, 1f);
    /// <summary>The Unity <c>Gold</c> preset; <c>RGBA(1f, 0.8431373f, 0f, 1f)</c>.</summary>
    public static readonly Color Gold = new(1f, 0.8431373f, 0f, 1f);
    /// <summary>The Unity <c>GoldenRod</c> preset; <c>RGBA(0.854902f, 0.6470588f, 0.1254902f, 1f)</c>.</summary>
    public static readonly Color GoldenRod = new(0.854902f, 0.6470588f, 0.1254902f, 1f);
    /// <summary>The Unity <c>Gray1</c> preset; <c>RGBA(0.1f, 0.1f, 0.1f, 1f)</c>.</summary>
    public static readonly Color Gray1 = new(0.1f, 0.1f, 0.1f, 1f);
    /// <summary>The Unity <c>Gray2</c> preset; <c>RGBA(0.2f, 0.2f, 0.2f, 1f)</c>.</summary>
    public static readonly Color Gray2 = new(0.2f, 0.2f, 0.2f, 1f);
    /// <summary>The Unity <c>Gray3</c> preset; <c>RGBA(0.3f, 0.3f, 0.3f, 1f)</c>.</summary>
    public static readonly Color Gray3 = new(0.3f, 0.3f, 0.3f, 1f);
    /// <summary>The Unity <c>Gray4</c> preset; <c>RGBA(0.4f, 0.4f, 0.4f, 1f)</c>.</summary>
    public static readonly Color Gray4 = new(0.4f, 0.4f, 0.4f, 1f);
    /// <summary>The Unity <c>Gray5</c> preset; <c>RGBA(0.5f, 0.5f, 0.5f, 1f)</c>.</summary>
    public static readonly Color Gray5 = new(0.5f, 0.5f, 0.5f, 1f);
    /// <summary>The Unity <c>Gray6</c> preset; <c>RGBA(0.6f, 0.6f, 0.6f, 1f)</c>.</summary>
    public static readonly Color Gray6 = new(0.6f, 0.6f, 0.6f, 1f);
    /// <summary>The Unity <c>Gray7</c> preset; <c>RGBA(0.7f, 0.7f, 0.7f, 1f)</c>.</summary>
    public static readonly Color Gray7 = new(0.7f, 0.7f, 0.7f, 1f);
    /// <summary>The Unity <c>Gray8</c> preset; <c>RGBA(0.8f, 0.8f, 0.8f, 1f)</c>.</summary>
    public static readonly Color Gray8 = new(0.8f, 0.8f, 0.8f, 1f);
    /// <summary>The Unity <c>Gray9</c> preset; <c>RGBA(0.9f, 0.9f, 0.9f, 1f)</c>.</summary>
    public static readonly Color Gray9 = new(0.9f, 0.9f, 0.9f, 1f);
    /// <summary>The Unity <c>Green</c> preset; <c>RGBA(0f, 1f, 0f, 1f)</c>.</summary>
    public static readonly Color Green = new(0f, 1f, 0f, 1f);
    /// <summary>The Unity <c>GreenYellow</c> preset; <c>RGBA(0.6784314f, 1f, 0.1843137f, 1f)</c>.</summary>
    public static readonly Color GreenYellow = new(0.6784314f, 1f, 0.1843137f, 1f);
    /// <summary>The Unity <c>Honeydew</c> preset; <c>RGBA(0.9411765f, 1f, 0.9411765f, 1f)</c>.</summary>
    public static readonly Color Honeydew = new(0.9411765f, 1f, 0.9411765f, 1f);
    /// <summary>The Unity <c>HotPink</c> preset; <c>RGBA(1f, 0.4117647f, 0.7058824f, 1f)</c>.</summary>
    public static readonly Color HotPink = new(1f, 0.4117647f, 0.7058824f, 1f);
    /// <summary>The Unity <c>IndianRed</c> preset; <c>RGBA(0.8039216f, 0.3607843f, 0.3607843f, 1f)</c>.</summary>
    public static readonly Color IndianRed = new(0.8039216f, 0.3607843f, 0.3607843f, 1f);
    /// <summary>The Unity <c>Indigo</c> preset; <c>RGBA(0.2941177f, 0f, 0.509804f, 1f)</c>.</summary>
    public static readonly Color Indigo = new(0.2941177f, 0f, 0.509804f, 1f);
    /// <summary>The Unity <c>Ivory</c> preset; <c>RGBA(1f, 1f, 0.9411765f, 1f)</c>.</summary>
    public static readonly Color Ivory = new(1f, 1f, 0.9411765f, 1f);
    /// <summary>The Unity <c>Khaki</c> preset; <c>RGBA(0.9411765f, 0.9019608f, 0.5490196f, 1f)</c>.</summary>
    public static readonly Color Khaki = new(0.9411765f, 0.9019608f, 0.5490196f, 1f);
    /// <summary>The Unity <c>Lavender</c> preset; <c>RGBA(0.9019608f, 0.9019608f, 0.9803922f, 1f)</c>.</summary>
    public static readonly Color Lavender = new(0.9019608f, 0.9019608f, 0.9803922f, 1f);
    /// <summary>The Unity <c>LavenderBlush</c> preset; <c>RGBA(1f, 0.9411765f, 0.9607844f, 1f)</c>.</summary>
    public static readonly Color LavenderBlush = new(1f, 0.9411765f, 0.9607844f, 1f);
    /// <summary>The Unity <c>LawnGreen</c> preset; <c>RGBA(0.4862745f, 0.9882354f, 0f, 1f)</c>.</summary>
    public static readonly Color LawnGreen = new(0.4862745f, 0.9882354f, 0f, 1f);
    /// <summary>The Unity <c>LemonChiffon</c> preset; <c>RGBA(1f, 0.9803922f, 0.8039216f, 1f)</c>.</summary>
    public static readonly Color LemonChiffon = new(1f, 0.9803922f, 0.8039216f, 1f);
    /// <summary>The Unity <c>LightBlue</c> preset; <c>RGBA(0.6784314f, 0.8470589f, 0.9019608f, 1f)</c>.</summary>
    public static readonly Color LightBlue = new(0.6784314f, 0.8470589f, 0.9019608f, 1f);
    /// <summary>The Unity <c>LightCoral</c> preset; <c>RGBA(0.9411765f, 0.5019608f, 0.5019608f, 1f)</c>.</summary>
    public static readonly Color LightCoral = new(0.9411765f, 0.5019608f, 0.5019608f, 1f);
    /// <summary>The Unity <c>LightCyan</c> preset; <c>RGBA(0.8784314f, 1f, 1f, 1f)</c>.</summary>
    public static readonly Color LightCyan = new(0.8784314f, 1f, 1f, 1f);
    /// <summary>The Unity <c>LightGoldenRod</c> preset; <c>RGBA(0.9333334f, 0.8666667f, 0.509804f, 1f)</c>.</summary>
    public static readonly Color LightGoldenRod = new(0.9333334f, 0.8666667f, 0.509804f, 1f);
    /// <summary>The Unity <c>LightGoldenRodYellow</c> preset; <c>RGBA(0.9803922f, 0.9803922f, 0.8235295f, 1f)</c>.</summary>
    public static readonly Color LightGoldenRodYellow = new(0.9803922f, 0.9803922f, 0.8235295f, 1f);
    /// <summary>The Unity <c>LightGray</c> preset; <c>RGBA(0.8274511f, 0.8274511f, 0.8274511f, 1f)</c>.</summary>
    public static readonly Color LightGray = new(0.8274511f, 0.8274511f, 0.8274511f, 1f);
    /// <summary>The Unity <c>LightGreen</c> preset; <c>RGBA(0.5647059f, 0.9333334f, 0.5647059f, 1f)</c>.</summary>
    public static readonly Color LightGreen = new(0.5647059f, 0.9333334f, 0.5647059f, 1f);
    /// <summary>The Unity <c>LightPink</c> preset; <c>RGBA(1f, 0.7137255f, 0.7568628f, 1f)</c>.</summary>
    public static readonly Color LightPink = new(1f, 0.7137255f, 0.7568628f, 1f);
    /// <summary>The Unity <c>LightSalmon</c> preset; <c>RGBA(1f, 0.627451f, 0.4784314f, 1f)</c>.</summary>
    public static readonly Color LightSalmon = new(1f, 0.627451f, 0.4784314f, 1f);
    /// <summary>The Unity <c>LightSeaGreen</c> preset; <c>RGBA(0.1254902f, 0.6980392f, 0.6666667f, 1f)</c>.</summary>
    public static readonly Color LightSeaGreen = new(0.1254902f, 0.6980392f, 0.6666667f, 1f);
    /// <summary>The Unity <c>LightSkyBlue</c> preset; <c>RGBA(0.5294118f, 0.8078432f, 0.9803922f, 1f)</c>.</summary>
    public static readonly Color LightSkyBlue = new(0.5294118f, 0.8078432f, 0.9803922f, 1f);
    /// <summary>The Unity <c>LightSlateBlue</c> preset; <c>RGBA(0.5176471f, 0.4392157f, 1f, 1f)</c>.</summary>
    public static readonly Color LightSlateBlue = new(0.5176471f, 0.4392157f, 1f, 1f);
    /// <summary>The Unity <c>LightSlateGray</c> preset; <c>RGBA(0.4666667f, 0.5333334f, 0.6f, 1f)</c>.</summary>
    public static readonly Color LightSlateGray = new(0.4666667f, 0.5333334f, 0.6f, 1f);
    /// <summary>The Unity <c>LightSteelBlue</c> preset; <c>RGBA(0.6901961f, 0.7686275f, 0.8705883f, 1f)</c>.</summary>
    public static readonly Color LightSteelBlue = new(0.6901961f, 0.7686275f, 0.8705883f, 1f);
    /// <summary>The Unity <c>LightYellow</c> preset; <c>RGBA(1f, 1f, 0.8784314f, 1f)</c>.</summary>
    public static readonly Color LightYellow = new(1f, 1f, 0.8784314f, 1f);
    /// <summary>The Unity <c>LimeGreen</c> preset; <c>RGBA(0.1960784f, 0.8039216f, 0.1960784f, 1f)</c>.</summary>
    public static readonly Color LimeGreen = new(0.1960784f, 0.8039216f, 0.1960784f, 1f);
    /// <summary>The Unity <c>Linen</c> preset; <c>RGBA(0.9803922f, 0.9411765f, 0.9019608f, 1f)</c>.</summary>
    public static readonly Color Linen = new(0.9803922f, 0.9411765f, 0.9019608f, 1f);
    /// <summary>The Unity <c>Magenta</c> preset; <c>RGBA(1f, 0f, 1f, 1f)</c>.</summary>
    public static readonly Color Magenta = new(1f, 0f, 1f, 1f);
    /// <summary>The Unity <c>Maroon</c> preset; <c>RGBA(0.6901961f, 0.1882353f, 0.3764706f, 1f)</c>.</summary>
    public static readonly Color Maroon = new(0.6901961f, 0.1882353f, 0.3764706f, 1f);
    /// <summary>The Unity <c>MediumAquamarine</c> preset; <c>RGBA(0.4f, 0.8039216f, 0.6666667f, 1f)</c>.</summary>
    public static readonly Color MediumAquamarine = new(0.4f, 0.8039216f, 0.6666667f, 1f);
    /// <summary>The Unity <c>MediumBlue</c> preset; <c>RGBA(0f, 0f, 0.8039216f, 1f)</c>.</summary>
    public static readonly Color MediumBlue = new(0f, 0f, 0.8039216f, 1f);
    /// <summary>The Unity <c>MediumOrchid</c> preset; <c>RGBA(0.7294118f, 0.3333333f, 0.8274511f, 1f)</c>.</summary>
    public static readonly Color MediumOrchid = new(0.7294118f, 0.3333333f, 0.8274511f, 1f);
    /// <summary>The Unity <c>MediumPurple</c> preset; <c>RGBA(0.5764706f, 0.4392157f, 0.8588236f, 1f)</c>.</summary>
    public static readonly Color MediumPurple = new(0.5764706f, 0.4392157f, 0.8588236f, 1f);
    /// <summary>The Unity <c>MediumSeaGreen</c> preset; <c>RGBA(0.2352941f, 0.7019608f, 0.4431373f, 1f)</c>.</summary>
    public static readonly Color MediumSeaGreen = new(0.2352941f, 0.7019608f, 0.4431373f, 1f);
    /// <summary>The Unity <c>MediumSlateBlue</c> preset; <c>RGBA(0.482353f, 0.4078432f, 0.9333334f, 1f)</c>.</summary>
    public static readonly Color MediumSlateBlue = new(0.482353f, 0.4078432f, 0.9333334f, 1f);
    /// <summary>The Unity <c>MediumSpringGreen</c> preset; <c>RGBA(0f, 0.9803922f, 0.6039216f, 1f)</c>.</summary>
    public static readonly Color MediumSpringGreen = new(0f, 0.9803922f, 0.6039216f, 1f);
    /// <summary>The Unity <c>MediumTurquoise</c> preset; <c>RGBA(0.282353f, 0.8196079f, 0.8000001f, 1f)</c>.</summary>
    public static readonly Color MediumTurquoise = new(0.282353f, 0.8196079f, 0.8000001f, 1f);
    /// <summary>The Unity <c>MediumVioletRed</c> preset; <c>RGBA(0.7803922f, 0.08235294f, 0.5215687f, 1f)</c>.</summary>
    public static readonly Color MediumVioletRed = new(0.7803922f, 0.08235294f, 0.5215687f, 1f);
    /// <summary>The Unity <c>MidnightBlue</c> preset; <c>RGBA(0.09803922f, 0.09803922f, 0.4392157f, 1f)</c>.</summary>
    public static readonly Color MidnightBlue = new(0.09803922f, 0.09803922f, 0.4392157f, 1f);
    /// <summary>The Unity <c>MintCream</c> preset; <c>RGBA(0.9607844f, 1f, 0.9803922f, 1f)</c>.</summary>
    public static readonly Color MintCream = new(0.9607844f, 1f, 0.9803922f, 1f);
    /// <summary>The Unity <c>MistyRose</c> preset; <c>RGBA(1f, 0.8941177f, 0.882353f, 1f)</c>.</summary>
    public static readonly Color MistyRose = new(1f, 0.8941177f, 0.882353f, 1f);
    /// <summary>The Unity <c>Moccasin</c> preset; <c>RGBA(1f, 0.8941177f, 0.7098039f, 1f)</c>.</summary>
    public static readonly Color Moccasin = new(1f, 0.8941177f, 0.7098039f, 1f);
    /// <summary>The Unity <c>NavajoWhite</c> preset; <c>RGBA(1f, 0.8705883f, 0.6784314f, 1f)</c>.</summary>
    public static readonly Color NavajoWhite = new(1f, 0.8705883f, 0.6784314f, 1f);
    /// <summary>The Unity <c>NavyBlue</c> preset; <c>RGBA(0f, 0f, 0.5019608f, 1f)</c>.</summary>
    public static readonly Color NavyBlue = new(0f, 0f, 0.5019608f, 1f);
    /// <summary>The Unity <c>OldLace</c> preset; <c>RGBA(0.9921569f, 0.9607844f, 0.9019608f, 1f)</c>.</summary>
    public static readonly Color OldLace = new(0.9921569f, 0.9607844f, 0.9019608f, 1f);
    /// <summary>The Unity <c>Olive</c> preset; <c>RGBA(0.5019608f, 0.5019608f, 0f, 1f)</c>.</summary>
    public static readonly Color Olive = new(0.5019608f, 0.5019608f, 0f, 1f);
    /// <summary>The Unity <c>OliveDrab</c> preset; <c>RGBA(0.4196079f, 0.5568628f, 0.1372549f, 1f)</c>.</summary>
    public static readonly Color OliveDrab = new(0.4196079f, 0.5568628f, 0.1372549f, 1f);
    /// <summary>The Unity <c>Orange</c> preset; <c>RGBA(1f, 0.6470588f, 0f, 1f)</c>.</summary>
    public static readonly Color Orange = new(1f, 0.6470588f, 0f, 1f);
    /// <summary>The Unity <c>OrangeRed</c> preset; <c>RGBA(1f, 0.2705882f, 0f, 1f)</c>.</summary>
    public static readonly Color OrangeRed = new(1f, 0.2705882f, 0f, 1f);
    /// <summary>The Unity <c>Orchid</c> preset; <c>RGBA(0.854902f, 0.4392157f, 0.8392158f, 1f)</c>.</summary>
    public static readonly Color Orchid = new(0.854902f, 0.4392157f, 0.8392158f, 1f);
    /// <summary>The Unity <c>PaleGoldenRod</c> preset; <c>RGBA(0.9333334f, 0.909804f, 0.6666667f, 1f)</c>.</summary>
    public static readonly Color PaleGoldenRod = new(0.9333334f, 0.909804f, 0.6666667f, 1f);
    /// <summary>The Unity <c>PaleGreen</c> preset; <c>RGBA(0.5960785f, 0.9843138f, 0.5960785f, 1f)</c>.</summary>
    public static readonly Color PaleGreen = new(0.5960785f, 0.9843138f, 0.5960785f, 1f);
    /// <summary>The Unity <c>PaleTurquoise</c> preset; <c>RGBA(0.6862745f, 0.9333334f, 0.9333334f, 1f)</c>.</summary>
    public static readonly Color PaleTurquoise = new(0.6862745f, 0.9333334f, 0.9333334f, 1f);
    /// <summary>The Unity <c>PaleVioletRed</c> preset; <c>RGBA(0.8588236f, 0.4392157f, 0.5764706f, 1f)</c>.</summary>
    public static readonly Color PaleVioletRed = new(0.8588236f, 0.4392157f, 0.5764706f, 1f);
    /// <summary>The Unity <c>PapayaWhip</c> preset; <c>RGBA(1f, 0.937255f, 0.8352942f, 1f)</c>.</summary>
    public static readonly Color PapayaWhip = new(1f, 0.937255f, 0.8352942f, 1f);
    /// <summary>The Unity <c>PeachPuff</c> preset; <c>RGBA(1f, 0.854902f, 0.7254902f, 1f)</c>.</summary>
    public static readonly Color PeachPuff = new(1f, 0.854902f, 0.7254902f, 1f);
    /// <summary>The Unity <c>Peru</c> preset; <c>RGBA(0.8039216f, 0.5215687f, 0.2470588f, 1f)</c>.</summary>
    public static readonly Color Peru = new(0.8039216f, 0.5215687f, 0.2470588f, 1f);
    /// <summary>The Unity <c>Pink</c> preset; <c>RGBA(1f, 0.7529413f, 0.7960785f, 1f)</c>.</summary>
    public static readonly Color Pink = new(1f, 0.7529413f, 0.7960785f, 1f);
    /// <summary>The Unity <c>Plum</c> preset; <c>RGBA(0.8666667f, 0.627451f, 0.8666667f, 1f)</c>.</summary>
    public static readonly Color Plum = new(0.8666667f, 0.627451f, 0.8666667f, 1f);
    /// <summary>The Unity <c>PowderBlue</c> preset; <c>RGBA(0.6901961f, 0.8784314f, 0.9019608f, 1f)</c>.</summary>
    public static readonly Color PowderBlue = new(0.6901961f, 0.8784314f, 0.9019608f, 1f);
    /// <summary>The Unity <c>Purple</c> preset; <c>RGBA(0.627451f, 0.1254902f, 0.9411765f, 1f)</c>.</summary>
    public static readonly Color Purple = new(0.627451f, 0.1254902f, 0.9411765f, 1f);
    /// <summary>The Unity <c>RebeccaPurple</c> preset; <c>RGBA(0.4f, 0.2f, 0.6f, 1f)</c>.</summary>
    public static readonly Color RebeccaPurple = new(0.4f, 0.2f, 0.6f, 1f);
    /// <summary>The Unity <c>Red</c> preset; <c>RGBA(1f, 0f, 0f, 1f)</c>.</summary>
    public static readonly Color Red = new(1f, 0f, 0f, 1f);
    /// <summary>The Unity <c>RosyBrown</c> preset; <c>RGBA(0.7372549f, 0.5607843f, 0.5607843f, 1f)</c>.</summary>
    public static readonly Color RosyBrown = new(0.7372549f, 0.5607843f, 0.5607843f, 1f);
    /// <summary>The Unity <c>RoyalBlue</c> preset; <c>RGBA(0.254902f, 0.4117647f, 0.882353f, 1f)</c>.</summary>
    public static readonly Color RoyalBlue = new(0.254902f, 0.4117647f, 0.882353f, 1f);
    /// <summary>The Unity <c>SaddleBrown</c> preset; <c>RGBA(0.5450981f, 0.2705882f, 0.07450981f, 1f)</c>.</summary>
    public static readonly Color SaddleBrown = new(0.5450981f, 0.2705882f, 0.07450981f, 1f);
    /// <summary>The Unity <c>Salmon</c> preset; <c>RGBA(0.9803922f, 0.5019608f, 0.4470589f, 1f)</c>.</summary>
    public static readonly Color Salmon = new(0.9803922f, 0.5019608f, 0.4470589f, 1f);
    /// <summary>The Unity <c>SandyBrown</c> preset; <c>RGBA(0.9568628f, 0.6431373f, 0.3764706f, 1f)</c>.</summary>
    public static readonly Color SandyBrown = new(0.9568628f, 0.6431373f, 0.3764706f, 1f);
    /// <summary>The Unity <c>SeaGreen</c> preset; <c>RGBA(0.1803922f, 0.5450981f, 0.3411765f, 1f)</c>.</summary>
    public static readonly Color SeaGreen = new(0.1803922f, 0.5450981f, 0.3411765f, 1f);
    /// <summary>The Unity <c>Seashell</c> preset; <c>RGBA(1f, 0.9607844f, 0.9333334f, 1f)</c>.</summary>
    public static readonly Color Seashell = new(1f, 0.9607844f, 0.9333334f, 1f);
    /// <summary>The Unity <c>Sienna</c> preset; <c>RGBA(0.627451f, 0.3215686f, 0.1764706f, 1f)</c>.</summary>
    public static readonly Color Sienna = new(0.627451f, 0.3215686f, 0.1764706f, 1f);
    /// <summary>The Unity <c>Silver</c> preset; <c>RGBA(0.7529413f, 0.7529413f, 0.7529413f, 1f)</c>.</summary>
    public static readonly Color Silver = new(0.7529413f, 0.7529413f, 0.7529413f, 1f);
    /// <summary>The Unity <c>SkyBlue</c> preset; <c>RGBA(0.5294118f, 0.8078432f, 0.9215687f, 1f)</c>.</summary>
    public static readonly Color SkyBlue = new(0.5294118f, 0.8078432f, 0.9215687f, 1f);
    /// <summary>The Unity <c>SlateBlue</c> preset; <c>RGBA(0.4156863f, 0.3529412f, 0.8039216f, 1f)</c>.</summary>
    public static readonly Color SlateBlue = new(0.4156863f, 0.3529412f, 0.8039216f, 1f);
    /// <summary>The Unity <c>SlateGray</c> preset; <c>RGBA(0.4392157f, 0.5019608f, 0.5647059f, 1f)</c>.</summary>
    public static readonly Color SlateGray = new(0.4392157f, 0.5019608f, 0.5647059f, 1f);
    /// <summary>The Unity <c>Snow</c> preset; <c>RGBA(1f, 0.9803922f, 0.9803922f, 1f)</c>.</summary>
    public static readonly Color Snow = new(1f, 0.9803922f, 0.9803922f, 1f);
    /// <summary>The Unity <c>SoftRed</c> preset; <c>RGBA(0.8627452f, 0.1921569f, 0.1960784f, 1f)</c>.</summary>
    public static readonly Color SoftRed = new(0.8627452f, 0.1921569f, 0.1960784f, 1f);
    /// <summary>The Unity <c>SoftBlue</c> preset; <c>RGBA(0.1882353f, 0.682353f, 0.7490196f, 1f)</c>.</summary>
    public static readonly Color SoftBlue = new(0.1882353f, 0.682353f, 0.7490196f, 1f);
    /// <summary>The Unity <c>SoftGreen</c> preset; <c>RGBA(0.5490196f, 0.7882354f, 0.1411765f, 1f)</c>.</summary>
    public static readonly Color SoftGreen = new(0.5490196f, 0.7882354f, 0.1411765f, 1f);
    /// <summary>The Unity <c>SoftYellow</c> preset; <c>RGBA(1f, 0.9333334f, 0.5490196f, 1f)</c>.</summary>
    public static readonly Color SoftYellow = new(1f, 0.9333334f, 0.5490196f, 1f);
    /// <summary>The Unity <c>SpringGreen</c> preset; <c>RGBA(0f, 1f, 0.4980392f, 1f)</c>.</summary>
    public static readonly Color SpringGreen = new(0f, 1f, 0.4980392f, 1f);
    /// <summary>The Unity <c>SteelBlue</c> preset; <c>RGBA(0.2745098f, 0.509804f, 0.7058824f, 1f)</c>.</summary>
    public static readonly Color SteelBlue = new(0.2745098f, 0.509804f, 0.7058824f, 1f);
    /// <summary>The Unity <c>Tan</c> preset; <c>RGBA(0.8235295f, 0.7058824f, 0.5490196f, 1f)</c>.</summary>
    public static readonly Color Tan = new(0.8235295f, 0.7058824f, 0.5490196f, 1f);
    /// <summary>The Unity <c>Teal</c> preset; <c>RGBA(0f, 0.5019608f, 0.5019608f, 1f)</c>.</summary>
    public static readonly Color Teal = new(0f, 0.5019608f, 0.5019608f, 1f);
    /// <summary>The Unity <c>Thistle</c> preset; <c>RGBA(0.8470589f, 0.7490196f, 0.8470589f, 1f)</c>.</summary>
    public static readonly Color Thistle = new(0.8470589f, 0.7490196f, 0.8470589f, 1f);
    /// <summary>The Unity <c>Tomato</c> preset; <c>RGBA(1f, 0.3882353f, 0.2784314f, 1f)</c>.</summary>
    public static readonly Color Tomato = new(1f, 0.3882353f, 0.2784314f, 1f);
    /// <summary>The Unity <c>Turquoise</c> preset; <c>RGBA(0.2509804f, 0.8784314f, 0.8156863f, 1f)</c>.</summary>
    public static readonly Color Turquoise = new(0.2509804f, 0.8784314f, 0.8156863f, 1f);
    /// <summary>The Unity <c>Violet</c> preset; <c>RGBA(0.9333334f, 0.509804f, 0.9333334f, 1f)</c>.</summary>
    public static readonly Color Violet = new(0.9333334f, 0.509804f, 0.9333334f, 1f);
    /// <summary>The Unity <c>VioletRed</c> preset; <c>RGBA(0.8156863f, 0.1254902f, 0.5647059f, 1f)</c>.</summary>
    public static readonly Color VioletRed = new(0.8156863f, 0.1254902f, 0.5647059f, 1f);
    /// <summary>The Unity <c>Wheat</c> preset; <c>RGBA(0.9607844f, 0.8705883f, 0.7019608f, 1f)</c>.</summary>
    public static readonly Color Wheat = new(0.9607844f, 0.8705883f, 0.7019608f, 1f);
    /// <summary>The Unity <c>White</c> preset; <c>RGBA(1f, 1f, 1f, 1f)</c>.</summary>
    public static readonly Color White = new(1f, 1f, 1f, 1f);
    /// <summary>The Unity <c>WhiteSmoke</c> preset; <c>RGBA(0.9607844f, 0.9607844f, 0.9607844f, 1f)</c>.</summary>
    public static readonly Color WhiteSmoke = new(0.9607844f, 0.9607844f, 0.9607844f, 1f);
    /// <summary>The Unity <c>Yellow</c> preset; <c>RGBA(1f, 235f / 255f, 4f / 255f, 1f)</c>.</summary>
    public static readonly Color Yellow = new(1f, 235f / 255f, 4f / 255f, 1f);
    /// <summary>The Unity <c>YellowGreen</c> preset; <c>RGBA(0.6039216f, 0.8039216f, 0.1960784f, 1f)</c>.</summary>
    public static readonly Color YellowGreen = new(0.6039216f, 0.8039216f, 0.1960784f, 1f);
    /// <summary>The Unity <c>YellowNice</c> preset; <c>RGBA(1f, 235f / 255f, 4f / 255f, 1f)</c>.</summary>
    public static readonly Color YellowNice = new(1f, 235f / 255f, 4f / 255f, 1f);
    /// <summary>The mid-gray Unity preset; <c>RGBA(0.5, 0.5, 0.5, 1)</c>.</summary>
    public static readonly Color Gray = new(0.5f, 0.5f, 0.5f, 1f);

    /// <summary>The mid-gray Unity preset, an alias of <see cref="Gray"/>. It keeps Unity's British spelling.</summary>
    public static readonly Color Grey = Gray;

    /// <summary>Fully transparent black; an alias of <see cref="Clear"/>.</summary>
    public static readonly Color Transparent = new(0f, 0f, 0f, 0f);
}
