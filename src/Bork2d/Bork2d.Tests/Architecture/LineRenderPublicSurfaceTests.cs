using System.Reflection;
using Bork2d.Engine.Scene;
using Xunit;

namespace Bork2d.Tests.Architecture;

/// <summary>
/// Pins the public <c>ILineRender&lt;TLayer&gt;</c> surface: exactly the line-specific members,
/// deriving from the shared <see cref="IRenderable{TLayer}"/> contract.
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
        Assert.True(InheritsGenericInterface(type, typeof(IRenderable<>)));
        Assert.True(typeof(IBoundedTransform).IsAssignableFrom(type));
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
                "IReadOnlyList`1 Points",
                "Single Width",
            },
            properties);
        Assert.Empty(methods);
    }

    [Fact]
    public void PointsAreReadWrite()
    {
        PropertyInfo points = Assert.IsAssignableFrom<PropertyInfo>(
            typeof(ILineRender<>).GetProperty("Points"));
        Assert.True(points.CanRead);
        Assert.True(points.CanWrite);
        Assert.Equal(typeof(IReadOnlyList<>), points.PropertyType.GetGenericTypeDefinition());
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
