using System.Reflection;
using System.Runtime.CompilerServices;
using Bork2d.Engine;
using Bork2d.Engine.Rendering;
using Xunit;

namespace Bork2d.Tests.Architecture;

/// <summary>
/// Pins the M8 public surface for render stats: <see cref="RenderStats"/> exactly as the approved
/// contract declares it, and <c>IEngine&lt;TLayer&gt;.RenderStats</c> as the only added engine
/// member (M8 §6.9/§6.10).
/// </summary>
public sealed class RenderStatsPublicSurfaceTests
{
    [Fact]
    public void RenderStatsIsAPublicReadOnlyRecordStructInTheRenderingNamespace()
    {
        Type type = typeof(RenderStats);

        Assert.True(type.IsPublic);
        Assert.True(type.IsValueType);
        Assert.Equal("Bork2d.Engine.Rendering", type.Namespace);
        Assert.True(
            type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false),
            "RenderStats must be a readonly record struct.");
    }

    [Fact]
    public void RenderStatsDeclaresExactlyTheDocumentedFieldsInOrder()
    {
        string[] expected =
        [
            "Int64 FrameSequence",
            "Int32 FramebufferWidth",
            "Int32 FramebufferHeight",
            "Int32 DrawCalls",
            "Int32 Instances",
            "Int32 Cameras",
            "Int32 NonEmptyCameras",
            "Int32 TexturePages",
            "Int32 TexturePageCapacity",
            "Int64 SkippedSprites",
            "Int64 ValidationErrors",
            "Int64 ValidationWarnings",
        ];

        // The positional record constructor carries the approved field order.
        ConstructorInfo constructor = Assert.Single(typeof(RenderStats).GetConstructors());
        string[] parameters = constructor
            .GetParameters()
            .Select(parameter => $"{parameter.ParameterType.Name} {parameter.Name}")
            .ToArray();
        Assert.Equal(expected, parameters);

        // The public properties are exactly those fields, all read-only (init-only at most, which is
        // the positional-record accessor; there is no public setter).
        PropertyInfo[] properties = typeof(RenderStats)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(
            expected.OrderBy(signature => signature, StringComparer.Ordinal),
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
    public void EngineDeclaresExactlyTheDocumentedMembers()
    {
        Type engine = typeof(IEngine<>);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        string[] properties = engine
            .GetProperties(flags)
            .Select(property => $"{property.PropertyType.Name} {property.Name}")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();
        string[] methods = engine
            .GetMethods(flags)
            .Where(method => !method.IsSpecialName)
            .Select(Describe)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "IAssets Assets",
                "IInput Input",
                "ILoggerFactory LoggerFactory",
                "ISceneRoot Root",
                "ITime Time",
                "IWindow Window",
                "RenderStats RenderStats",
            },
            properties);
        Assert.Equal(
            new[]
            {
                "CreateCamera(ITransform)",
                "CreateSpriteRender(ITransform)",
                "CreateTransform(ITransform)",
                "Exit()",
            },
            methods);
    }

    [Fact]
    public void RenderStatsIsTheOnlyEngineMemberThatMentionsTheSnapshot()
    {
        var members = typeof(IEngine<>)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Where(member => member.Name.Contains("RenderStats", StringComparison.Ordinal))
            .Select(member => member.Name)
            .ToArray();

        Assert.Equal(["RenderStats"], members);
        Assert.Equal(
            "Bork2d.Engine.Rendering.RenderStats",
            typeof(IEngine<>).GetProperty("RenderStats")!.PropertyType.FullName);
    }

    private static string Describe(MethodInfo method)
    {
        string parameters = string.Join(
            ',',
            method.GetParameters().Select(parameter => parameter.ParameterType.Name));
        return $"{method.Name}({parameters})";
    }
}
