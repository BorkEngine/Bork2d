using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Bork2d.Engine.Assets;
using PolyType;
using Xunit;

namespace Bork2d.Tests.Architecture;

/// <summary>
/// Pins the font asset public contract: the generated-shape <see cref="FontAsset"/> DTO and the two
/// value-only records it exposes.
/// </summary>
public sealed class FontPublicSurfaceTests
{
    [Fact]
    public void FontAssetIsAPublicSealedShapeableClassWithTheDocumentedFields()
    {
        Type type = typeof(FontAsset);

        Assert.True(type.IsPublic);
        Assert.True(type.IsClass);
        Assert.True(type.IsSealed);
        Assert.Contains(
            typeof(IShapeable<>),
            type.GetInterfaces().Select(candidate => candidate.IsGenericType
                ? candidate.GetGenericTypeDefinition()
                : candidate));

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;
        PropertyInfo[] properties = type.GetProperties(flags);
        Assert.Equal(
            new[]
            {
                "AssetRef`1 Atlas",
                "FontGlyph[] Glyphs",
                "FontKerning[] Kerning",
                "Single Ascent",
                "Single Descent",
                "Single DistanceRange",
                "Single LineGap",
                "UInt32 PixelsPerEm",
            },
            properties
                .Select(property => $"{property.PropertyType.Name} {property.Name}")
                .OrderBy(signature => signature, StringComparer.Ordinal));
        Assert.All(
            properties,
            property => Assert.True(
                property.SetMethod is { IsPublic: true },
                $"{property.Name} must have a public setter."));
    }

    [Fact]
    public void NewFontAssetHasEmptyNonNullCollections()
    {
        var asset = new FontAsset();

        Assert.Empty(asset.Glyphs);
        Assert.Empty(asset.Kerning);
        Assert.False(asset.Atlas.HasValue);
        Assert.Equal(0u, asset.PixelsPerEm);
    }

    [Fact]
    public void FontAssetExposesMeasureTextWithTheApprovedSignature()
    {
        MethodInfo method = Assert.IsAssignableFrom<MethodInfo>(
            typeof(FontAsset).GetMethod(
                "MeasureText",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        Assert.True(method.IsPublic);
        Assert.Equal(typeof(Vector2), method.ReturnType);
        ParameterInfo[] parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(ReadOnlySpan<char>), parameters[0].ParameterType);
        Assert.Equal("text", parameters[0].Name);
        Assert.Equal(typeof(float), parameters[1].ParameterType);
        Assert.Equal("size", parameters[1].Name);
    }

    [Fact]
    public void FontGlyphIsAReadOnlyRecordStructWithTheApprovedFields()
    {
        Type type = typeof(FontGlyph);

        Assert.True(type.IsPublic);
        Assert.True(type.IsValueType);
        Assert.Equal("Bork2d.Engine.Assets", type.Namespace);
        Assert.True(
            type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false),
            "FontGlyph must be a readonly record struct.");

        ConstructorInfo constructor = Assert.Single(type.GetConstructors());
        ParameterInfo[] parameters = constructor.GetParameters();
        Assert.Equal(
            new[]
            {
                "Int32 Codepoint",
                "Single Advance",
                "Vector2 Bearing",
                "Vector4 PlaneBounds",
                "Vector4 AtlasBounds",
            },
            parameters.Select(parameter => $"{parameter.ParameterType.Name} {parameter.Name}"));

        PropertyInfo[] properties = type.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(
            new[]
            {
                "Int32 Codepoint",
                "Single Advance",
                "Vector2 Bearing",
                "Vector4 AtlasBounds",
                "Vector4 PlaneBounds",
            },
            properties
                .Select(property => $"{property.PropertyType.Name} {property.Name}")
                .OrderBy(signature => signature, StringComparer.Ordinal));
        Assert.All(
            properties,
            property => Assert.True(
                property.SetMethod is null
                    || property.SetMethod.ReturnParameter
                        .GetRequiredCustomModifiers()
                        .Contains(typeof(IsExternalInit)),
                $"{property.Name} must not have a public setter."));
    }

    [Fact]
    public void FontKerningIsAReadOnlyRecordStructWithTheApprovedFields()
    {
        Type type = typeof(FontKerning);

        Assert.True(type.IsPublic);
        Assert.True(type.IsValueType);
        Assert.Equal("Bork2d.Engine.Assets", type.Namespace);
        Assert.True(
            type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false),
            "FontKerning must be a readonly record struct.");

        ConstructorInfo constructor = Assert.Single(type.GetConstructors());
        Assert.Equal(
            new[]
            {
                "Int32 LeftCodepoint",
                "Int32 RightCodepoint",
                "Single Adjustment",
            },
            constructor.GetParameters()
                .Select(parameter => $"{parameter.ParameterType.Name} {parameter.Name}"));

        PropertyInfo[] properties = type.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(
            new[]
            {
                "Int32 LeftCodepoint",
                "Int32 RightCodepoint",
                "Single Adjustment",
            },
            properties
                .Select(property => $"{property.PropertyType.Name} {property.Name}")
                .OrderBy(signature => signature, StringComparer.Ordinal));
        Assert.All(
            properties,
            property => Assert.True(
                property.SetMethod is null
                    || property.SetMethod.ReturnParameter
                        .GetRequiredCustomModifiers()
                        .Contains(typeof(IsExternalInit)),
                $"{property.Name} must not have a public setter."));
    }

    [Fact]
    public void FontGlyphCarriesTheDocumentedValueShape()
    {
        var glyph = new FontGlyph(
            0x30,
            0.6f,
            new Vector2(0.1f, -0.05f),
            new Vector4(0.1f, -0.05f, 0.5f, 0.7f),
            new Vector4(0.0f, 0.0f, 0.1f, 0.2f));

        Assert.Equal(0x30, glyph.Codepoint);
        Assert.Equal(0.6f, glyph.Advance);
        Assert.Equal(new Vector2(0.1f, -0.05f), glyph.Bearing);
        Assert.Equal(new Vector4(0.1f, -0.05f, 0.5f, 0.7f), glyph.PlaneBounds);
        Assert.Equal(new Vector4(0.0f, 0.0f, 0.1f, 0.2f), glyph.AtlasBounds);
    }
}
