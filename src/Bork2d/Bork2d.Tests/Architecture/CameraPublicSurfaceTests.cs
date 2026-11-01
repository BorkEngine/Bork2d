using System.Reflection;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Xunit;

namespace Bork2d.Tests.Architecture;

/// <summary>
/// Pins the public <c>ICamera&lt;TLayer&gt;</c> surface: M8 adds exactly the three approved helper
/// members and nothing else, and the camera and sprite interfaces derive from the shared
/// <see cref="IRenderable"/> contract.
/// </summary>
public sealed class CameraPublicSurfaceTests
{
    [Fact]
    public void CameraDeclaresExactlyTheDocumentedMembers()
    {
        Type camera = typeof(ICamera<>);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        string[] properties = camera
            .GetProperties(flags)
            .Select(property => $"{property.PropertyType.Name} {property.Name}")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();
        string[] methods = camera
            .GetMethods(flags)
            .Where(method => !method.IsSpecialName)
            .Select(Describe)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "ClearKind Clear",
                "Color ClearColor",
                "Int32 ZIndex",
                "Single Size",
                "TLayer CullingMask",
                "Vector2 WorldSize",
                "Vector4 Viewport",
            },
            properties);
        Assert.Equal(
            new[]
            {
                "ScreenToWorld(Vector2)",
                "WorldToScreen(Vector2)",
            },
            methods);
    }

    [Fact]
    public void RenderablesShareThePublicBoundsContract()
    {
        Assert.True(typeof(IRenderable).IsAssignableFrom(typeof(ICamera<>)));
        Assert.True(typeof(IRenderable).IsAssignableFrom(typeof(ISpriteRender<>)));
        Assert.True(typeof(ITransform).IsAssignableFrom(typeof(IRenderable)));

        PropertyInfo? bounds = typeof(IRenderable).GetProperty(nameof(IRenderable.Bounds));
        Assert.NotNull(bounds);
        Assert.Equal(typeof(Bounds), bounds.PropertyType);
        Assert.False(bounds.CanWrite);
    }

    private static string Describe(MethodInfo method)
    {
        string parameters = string.Join(
            ',',
            method.GetParameters().Select(parameter => parameter.ParameterType.Name));
        return $"{method.Name}({parameters})";
    }
}
