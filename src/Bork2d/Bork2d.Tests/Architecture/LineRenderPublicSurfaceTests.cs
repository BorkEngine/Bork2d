using System.Reflection;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Scene;
using Xunit;

namespace Bork2d.Tests.Architecture;

/// <summary>
/// Pins the public <c>ILineRender&lt;TLayer&gt;</c> surface: exactly the approved members, deriving
/// from the shared <see cref="IRenderable"/> contract.
/// </summary>
public sealed class LineRenderPublicSurfaceTests
{
    [Fact]
    public void LineRenderIsAPublicInterfaceInTheSceneNamespace()
    {
        Type type = typeof(ILineRender<>);

        Assert.True(type.IsInterface);
        Assert.True(type.IsPublic);
        Assert.Equal("Bork2d.Engine.Scene", type.Namespace);
        Assert.True(typeof(IRenderable).IsAssignableFrom(type));
    }

    [Fact]
    public void LineRenderDeclaresExactlyTheDocumentedMembers()
    {
        Type line = typeof(ILineRender<>);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        string[] properties = line
            .GetProperties(flags)
            .Select(property => $"{property.PropertyType.Name} {property.Name}")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();
        string[] methods = line
            .GetMethods(flags)
            .Where(method => !method.IsSpecialName)
            .Select(Describe)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Boolean AntiAlias",
                "Boolean Closed",
                "Color Color",
                "IReadOnlyList`1 Points",
                "Int32 ZIndex",
                "ShaderAsset Shader",
                "Single Width",
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
    }

    [Fact]
    public void PointsAreReadWriteAndShaderIsNullable()
    {
        PropertyInfo points = Assert.IsAssignableFrom<PropertyInfo>(
            typeof(ILineRender<>).GetProperty("Points"));
        Assert.True(points.CanRead);
        Assert.True(points.CanWrite);
        Assert.Equal(typeof(IReadOnlyList<>), points.PropertyType.GetGenericTypeDefinition());

        PropertyInfo shader = Assert.IsAssignableFrom<PropertyInfo>(
            typeof(ILineRender<>).GetProperty("Shader"));
        Assert.Equal(typeof(ShaderAsset), shader.PropertyType);
        Assert.True(shader.CanWrite);
    }

    private static string Describe(MethodInfo method)
    {
        string parameters = string.Join(
            ',',
            method.GetParameters().Select(parameter => parameter.ParameterType.Name));
        return $"{method.Name}({parameters})";
    }
}
