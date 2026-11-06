using System.Reflection;
using Bork2d.Engine.Scene;
using Xunit;

namespace Bork2d.Tests.Architecture;

/// <summary>
/// Pins the public font renderable surface: exactly the five font-specific members deriving from
/// <see cref="IRenderable{TLayer}"/>, and the <see cref="TextAlignment"/> value contract.
/// </summary>
public sealed class FontRenderPublicSurfaceTests
{
    [Fact]
    public void FontRenderIsAPublicInterfaceInTheSceneNamespace()
    {
        Type type = typeof(IFontRender<>);

        Assert.True(type.IsInterface);
        Assert.True(type.IsPublic);
        Assert.Equal("Bork2d.Engine.Scene", type.Namespace);
        Assert.True(InheritsGenericInterface(type, typeof(IRenderable<>)));
        Assert.True(typeof(IBoundedTransform).IsAssignableFrom(type));
    }

    [Fact]
    public void FontRenderDeclaresExactlyTheDocumentedMembers()
    {
        Type type = typeof(IFontRender<>);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        string[] properties = type
            .GetProperties(flags)
            .Select(property => $"{property.PropertyType.Name} {property.Name}")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "FontAsset Font",
                "Single LineSpacing",
                "Single Size",
                "String Text",
                "TextAlignment Alignment",
            },
            properties);
        Assert.Equal(5, properties.Length);
        Assert.DoesNotContain(type.GetMethods(flags), method => !method.IsSpecialName);
    }

    [Fact]
    public void TextAlignmentHasTheApprovedValuesInOrder()
    {
        Type type = typeof(TextAlignment);

        Assert.True(type.IsEnum);
        Assert.True(type.IsPublic);
        Assert.Equal("Bork2d.Engine.Scene", type.Namespace);
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(type));
        Assert.Equal(
            new[] { "Left", "Center", "Right" },
            Enum.GetNames<TextAlignment>());
        Assert.Equal(
            new[] { 0, 1, 2 },
            Enum.GetValues<TextAlignment>().Select(value => (int)value));
    }

    private static bool InheritsGenericInterface(Type type, Type definition)
        => type.GetInterfaces().Any(candidate => candidate.IsGenericType
            && candidate.GetGenericTypeDefinition() == definition);
}
