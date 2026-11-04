using System.Reflection;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Scene;
using Xunit;

namespace Bork2d.Tests.Architecture;

/// <summary>
/// Pins the shared renderable contracts: <see cref="IBoundedTransform"/> owns the bounds, and
/// <see cref="IRenderable{TLayer}"/> owns the 2D draw state shared by sprite and line renderables.
/// </summary>
public sealed class RenderablePublicSurfaceTests
{
    [Fact]
    public void BoundedTransformInheritsTransformAndDeclaresOnlyBounds()
    {
        Type type = typeof(IBoundedTransform);

        Assert.True(type.IsInterface);
        Assert.True(type.IsPublic);
        Assert.Equal("Bork2d.Engine.Scene", type.Namespace);
        Assert.Contains(typeof(ITransform), type.GetInterfaces());

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;
        PropertyInfo property = Assert.Single(type.GetProperties(flags));
        Assert.Equal(nameof(IBoundedTransform.Bounds), property.Name);
        Assert.Equal(typeof(Bounds), property.PropertyType);
        Assert.True(property.CanRead);
        Assert.False(property.CanWrite);
        Assert.DoesNotContain(type.GetMethods(flags), method => !method.IsSpecialName);
    }

    [Fact]
    public void RenderableInheritsBoundedTransformAndDeclaresTheSharedDrawState()
    {
        Type type = typeof(IRenderable<>);

        Assert.True(type.IsInterface);
        Assert.True(type.IsPublic);
        Assert.Equal("Bork2d.Engine.Scene", type.Namespace);
        Assert.True(typeof(IBoundedTransform).IsAssignableFrom(type));

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;
        string[] properties = type
            .GetProperties(flags)
            .Select(property => $"{property.PropertyType.Name} {property.Name}")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();
        string[] methods = type
            .GetMethods(flags)
            .Where(method => !method.IsSpecialName)
            .Select(Describe)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Color Color",
                "Int32 ZIndex",
                "ShaderAsset Shader",
                "TLayer Layer",
            },
            properties);
        Assert.Equal(
            new[]
            {
                "ResetUniforms()",
                "SetUniform(String,Color)",
                "SetUniform(String,Single)",
                "SetUniform(String,Vector2)",
                "SetUniform(String,Vector3)",
                "SetUniform(String,Vector4)",
            },
            methods);
        Assert.Null(type.GetProperty("Tint"));
    }

    [Fact]
    public void SpriteRenderDeclaresOnlyTheSpriteSpecificMembers()
    {
        Type type = typeof(ISpriteRender<>);

        Assert.True(type.IsInterface);
        Assert.True(type.IsPublic);
        Assert.Equal("Bork2d.Engine.Scene", type.Namespace);
        Assert.True(InheritsGenericInterface(type, typeof(IRenderable<>)));

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
                "AssetRef`1[] AdditionalTextures",
                "Boolean FlipX",
                "Boolean FlipY",
                "SpriteAsset Sprite",
            },
            properties);
        Assert.DoesNotContain(type.GetMethods(flags), method => !method.IsSpecialName);
        Assert.Null(type.GetProperty("Tint"));
        Assert.Null(type.GetProperty("Shader"));
        Assert.Null(type.GetProperty("Color"));
        Assert.Null(type.GetProperty("Layer"));
        Assert.Null(type.GetProperty("ZIndex"));
    }

    private static bool InheritsGenericInterface(Type type, Type definition)
        => type.GetInterfaces().Any(candidate => candidate.IsGenericType
            && candidate.GetGenericTypeDefinition() == definition);

    private static string Describe(MethodInfo method)
    {
        string parameters = string.Join(
            ',',
            method.GetParameters().Select(parameter => parameter.ParameterType.Name));
        return $"{method.Name}({parameters})";
    }
}
