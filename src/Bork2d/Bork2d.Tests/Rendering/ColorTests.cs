using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Bork2d.Engine.Rendering;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Deterministic tests for the immutable <see cref="Color"/> value type: its <see cref="Vector4"/>
/// storage, constructors and indexer, exact equality, arithmetic, interpolation, HSV conversion,
/// formatting, presets, and deconstruction.
/// </summary>
public sealed class ColorTests
{
    [Fact]
    public void ColorIsExactlyOneVector4()
    {
        Assert.True(typeof(Color).IsValueType);
        Assert.Equal(16, Unsafe.SizeOf<Color>());
        Assert.Equal(Unsafe.SizeOf<Vector4>(), Unsafe.SizeOf<Color>());
    }

    [Fact]
    public void FourArgumentConstructorStoresEachComponent()
    {
        Color color = new(0.1f, 0.2f, 0.3f, 0.4f);

        Assert.Equal(0.1f, color.R);
        Assert.Equal(0.2f, color.G);
        Assert.Equal(0.3f, color.B);
        Assert.Equal(0.4f, color.A);
    }

    [Fact]
    public void ThreeArgumentConstructorDefaultsAlphaToOne()
    {
        Color color = new(0.25f, 0.5f, 0.75f);

        Assert.Equal(0.25f, color.R);
        Assert.Equal(0.5f, color.G);
        Assert.Equal(0.75f, color.B);
        Assert.Equal(1f, color.A);
    }

    [Fact]
    public void DefaultColorIsTransparentBlack()
    {
        Color color = default;

        Assert.Equal(0f, color.R);
        Assert.Equal(0f, color.G);
        Assert.Equal(0f, color.B);
        Assert.Equal(0f, color.A);
        Assert.Equal(Color.Transparent, color);
        Assert.Equal(Color.Clear, color);
    }

    [Fact]
    public void IndexerReadsComponentsInRgbaOrder()
    {
        Color color = new(0.1f, 0.2f, 0.3f, 0.4f);

        Assert.Equal(color.R, color[0]);
        Assert.Equal(color.G, color[1]);
        Assert.Equal(color.B, color[2]);
        Assert.Equal(color.A, color[3]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void IndexerRejectsOutOfRangeIndexes(int index)
    {
        IndexOutOfRangeException exception =
            Assert.Throws<IndexOutOfRangeException>(() => Color.White[index]);

        Assert.Contains("0, 1, 2, or 3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImplicitVector4ConversionRoundTrips()
    {
        Color color = new(0.1f, 0.2f, 0.3f, 0.4f);

        Vector4 vector = color;
        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 0.4f), vector);

        Color roundTripped = vector;
        Assert.Equal(color, roundTripped);
    }

    [Fact]
    public void ArithmeticOperatorsWorkComponentWise()
    {
        Color left = new(0.5f, 0.25f, 1f, 0.75f);
        Color right = new(0.25f, 0.5f, 0f, 0.25f);

        Assert.Equal(new Color(0.75f, 0.75f, 1f, 1f), left + right);
        Assert.Equal(new Color(0.25f, -0.25f, 1f, 0.5f), left - right);
        Assert.Equal(new Color(0.125f, 0.125f, 0f, 0.1875f), left * right);
        Assert.Equal(new Color(1f, 0.5f, 2f, 1.5f), left * 2f);
        Assert.Equal(new Color(1f, 0.5f, 2f, 1.5f), 2f * left);
        Assert.Equal(new Color(0.25f, 0.125f, 0.5f, 0.375f), left / 2f);
    }

    [Fact]
    public void EqualityComparesComponentsExactly()
    {
        Color color = new(0.1f, 0.2f, 0.3f, 0.4f);
        Color same = new(0.1f, 0.2f, 0.3f, 0.4f);
        Color slightlyDifferent = new(0.1000001f, 0.2f, 0.3f, 0.4f);
        Color differentComponent = new(0.1f, 0.2f, 0.3f, 0.5f);

        Assert.Equal(color, same);
        Assert.True(color == same);
        Assert.False(color != same);
        Assert.Equal(color.GetHashCode(), same.GetHashCode());
        Assert.Contains(same, new HashSet<Color> { color });

        Assert.NotEqual(color, slightlyDifferent);
        Assert.True(color != slightlyDifferent);
        Assert.False(color == slightlyDifferent);

        Assert.NotEqual(color, differentComponent);
        Assert.True(color != differentComponent);
    }

    [Fact]
    public void ObjectEqualityOnlyMatchesColors()
    {
        Color color = new(0.1f, 0.2f, 0.3f, 0.4f);

        Assert.True(color.Equals((object)new Color(0.1f, 0.2f, 0.3f, 0.4f)));
        Assert.False(color.Equals((object?)null));
        Assert.False(color.Equals("red"));
    }

    [Fact]
    public void LerpClampsTheInterpolationRatio()
    {
        Color a = new(0f, 0.25f, 0.5f, 0f);
        Color b = new(1f, 0.75f, 1f, 1f);

        Assert.Equal(a, Color.Lerp(a, b, -0.5f));
        Assert.Equal(b, Color.Lerp(a, b, 1.5f));
        Assert.Equal(new Color(0.5f, 0.5f, 0.75f, 0.5f), Color.Lerp(a, b, 0.5f));
    }

    [Fact]
    public void LerpUnclampedDoesNotClampTheInterpolationRatio()
    {
        Color a = new(0f, 0.25f, 0.5f, 0f);
        Color b = new(1f, 0.75f, 1f, 1f);

        Assert.Equal(new Color(-1f, -0.25f, 0f, -1f), Color.LerpUnclamped(a, b, -1f));
        Assert.Equal(new Color(2f, 1.25f, 1.5f, 2f), Color.LerpUnclamped(a, b, 2f));
        Assert.Equal(a, Color.LerpUnclamped(a, b, 0f));
        Assert.Equal(b, Color.LerpUnclamped(a, b, 1f));
    }

    [Fact]
    public void GrayscaleUsesUnityLuminance()
    {
        Assert.Equal(0.299f, Color.Red.Grayscale);
        Assert.Equal(0.587f, Color.Green.Grayscale);
        Assert.Equal(0.114f, Color.Blue.Grayscale);
        Assert.Equal(0.5f, Color.Gray5.Grayscale, 6);
    }

    [Fact]
    public void MaxColorComponentIsTheLargestRgbComponent()
    {
        Assert.Equal(0.9f, new Color(0.1f, 0.9f, 0.4f, 1f).MaxColorComponent);
        Assert.Equal(1f, Color.White.MaxColorComponent);
        Assert.Equal(0f, Color.Clear.MaxColorComponent);
    }

    [Fact]
    public void HSVToRGBProducesPrimaryColors()
    {
        Assert.Equal(Color.Red, Color.HSVToRGB(0f, 1f, 1f));

        Color green = Color.HSVToRGB(1f / 3f, 1f, 1f);
        Assert.Equal(1f, green.G);
        Assert.Equal(1f, green.A);
        Assert.Equal(0f, green.R, 5);
        Assert.Equal(0f, green.B, 5);

        Color blue = Color.HSVToRGB(2f / 3f, 1f, 1f);
        Assert.Equal(1f, blue.B);
        Assert.Equal(1f, blue.A);
        Assert.Equal(0f, blue.R, 5);
        Assert.Equal(0f, blue.G, 5);
    }

    [Fact]
    public void HSVToRGBHandlesZeroSaturationAndZeroValue()
    {
        Assert.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), Color.HSVToRGB(0f, 0f, 0.5f));
        Assert.Equal(new Color(0f, 0f, 0f, 1f), Color.HSVToRGB(0.5f, 1f, 0f));
    }

    [Fact]
    public void HSVToRGBClampsOnlyWhenHdrIsDisabled()
    {
        Assert.Equal(new Color(2f, 0f, 0f, 1f), Color.HSVToRGB(0f, 1f, 2f));
        Assert.Equal(new Color(1f, 0f, 0f, 1f), Color.HSVToRGB(0f, 1f, 2f, hdr: false));
    }

    [Fact]
    public void HSVToRGBMatchesUnityForOutOfRangeHues()
    {
        Assert.Equal(new Color(0f, 0f, 0f, 1f), Color.HSVToRGB(2f, 1f, 1f));

        Color negative = Color.HSVToRGB(-0.1f, 1f, 1f);
        Assert.Equal(1f, negative.R);
        Assert.Equal(0.4f, negative.G, 5);
        Assert.Equal(0f, negative.B, 5);
    }

    [Fact]
    public void RGBToHSVProducesKnownValues()
    {
        Color.RGBToHSV(Color.Red, out float h, out float s, out float v);
        Assert.Equal(0f, h);
        Assert.Equal(1f, s);
        Assert.Equal(1f, v);

        Color.RGBToHSV(Color.Green, out h, out s, out v);
        Assert.Equal(1f / 3f, h);
        Assert.Equal(1f, s);
        Assert.Equal(1f, v);

        Color.RGBToHSV(Color.Blue, out h, out s, out v);
        Assert.Equal(2f / 3f, h);
        Assert.Equal(1f, s);
        Assert.Equal(1f, v);

        Color.RGBToHSV(new Color(0.5f, 0.5f, 0.5f, 1f), out h, out s, out v);
        Assert.Equal(0f, h);
        Assert.Equal(0f, s);
        Assert.Equal(0.5f, v);
    }

    [Fact]
    public void RGBToHSVRoundTripsThroughHSVToRGB()
    {
        Color original = new(0.2f, 0.6f, 0.4f, 1f);

        Color.RGBToHSV(original, out float h, out float s, out float v);
        Color roundTripped = Color.HSVToRGB(h, s, v);

        Assert.Equal(original.R, roundTripped.R, 5);
        Assert.Equal(original.G, roundTripped.G, 5);
        Assert.Equal(original.B, roundTripped.B, 5);
        Assert.Equal(1f, roundTripped.A);
    }

    [Fact]
    public void ToStringUsesRgbaAndInvariantF3ByDefault()
    {
        Color color = new(0.1f, 0.2f, 0.3f, 0.4f);

        Assert.Equal("RGBA(0.100, 0.200, 0.300, 0.400)", color.ToString());
        Assert.Equal(
            "RGBA(0.100, 0.200, 0.300, 0.400)",
            ((IFormattable)color).ToString(null, null));
    }

    [Fact]
    public void ToStringHonorsAFormatAndProvider()
    {
        Color color = new(0.1f, 0.2f, 0.3f, 0.4f);

        Assert.Equal("RGBA(0.1, 0.2, 0.3, 0.4)", color.ToString("F1"));
        Assert.Equal(
            "RGBA(0,100, 0,200, 0,300, 0,400)",
            color.ToString("F3", CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal(
            "RGBA(0.100, 0.200, 0.300, 0.400)",
            ((IFormattable)color).ToString("F3", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void YellowUsesTheUnityValue()
    {
        Assert.Equal(new Color(1f, 235f / 255f, 4f / 255f, 1f), Color.Yellow);
        Assert.NotEqual(1f, Color.Yellow.G);
        Assert.Equal(1f, Color.Yellow.A);
    }

    [Fact]
    public void PresetsMatchUnityValues()
    {
        Assert.Equal(new Color(0.9411765f, 0.9725491f, 1f, 1f), Color.AliceBlue);
        Assert.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), Color.Gray5);
        Assert.Equal(new Color(0.1f, 0.1f, 0.1f, 1f), Color.Gray1);
        Assert.Equal(new Color(0.9f, 0.9f, 0.9f, 1f), Color.Gray9);
        Assert.Equal(new Color(0.1882353f, 0.682353f, 0.7490196f, 1f), Color.SoftBlue);
        Assert.Equal(new Color(0.9333334f, 0.8666667f, 0.509804f, 1f), Color.LightGoldenRod);
        Assert.Equal(new Color(1f, 235f / 255f, 4f / 255f, 1f), Color.YellowNice);
        Assert.Equal(new Color(0.8627452f, 0.1921569f, 0.1960784f, 1f), Color.SoftRed);
    }

    [Fact]
    public void GrayGreyClearAndTransparentAreAliases()
    {
        Assert.Equal(Color.Gray5, Color.Gray);
        Assert.Equal(Color.Gray, Color.Grey);
        Assert.Equal(Color.Transparent, Color.Clear);
        Assert.Equal(default(Color), Color.Clear);
        Assert.Equal(new Color(0f, 0f, 0f, 0f), Color.Transparent);
    }

    [Fact]
    public void EveryPresetIsOpaqueExceptClearAndTransparent()
    {
        FieldInfo[] presets = typeof(Color)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(Color))
            .ToArray();

        Assert.Equal(158, presets.Length);
        foreach (FieldInfo preset in presets)
        {
            var color = (Color)preset.GetValue(null)!;
            float expectedAlpha = preset.Name is nameof(Color.Clear) or nameof(Color.Transparent)
                ? 0f
                : 1f;
            Assert.Equal(expectedAlpha, color.A);
        }
    }

    [Fact]
    public void DeconstructSplitsComponents()
    {
        var (r, g, b, a) = new Color(1f, 2f, 3f, 4f);

        Assert.Equal(1f, r);
        Assert.Equal(2f, g);
        Assert.Equal(3f, b);
        Assert.Equal(4f, a);
    }
}
