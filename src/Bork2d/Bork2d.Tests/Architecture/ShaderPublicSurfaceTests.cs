using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;
using PolyType;
using Xunit;

namespace Bork2d.Tests.Architecture;

/// <summary>
/// Pins the custom-shader public contract: the new asset types and declarations, the engine's
/// layout version, and the shader members added to <see cref="ISpriteRender{TLayer}"/>.
/// </summary>
public sealed class ShaderPublicSurfaceTests
{
    [Fact]
    public void ShaderAssetIsAPublicSealedShapeableClassWithTheDocumentedFields()
    {
        Type type = typeof(ShaderAsset);

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
                "DataRef SpirV",
                "ShaderTextureSlot[] TextureSlots",
                "ShaderUniform[] GlobalUniforms",
                "ShaderUniform[] InstanceUniforms",
                "String EntryPoint",
                "UInt32 LayoutVersion",
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
    public void ShaderUniformTypeDeclaresTheFiveApprovedValues()
    {
        Assert.Equal(
            new[] { "Float", "Vector2", "Vector3", "Vector4", "Color" },
            Enum.GetNames<ShaderUniformType>());
        Assert.Equal(
            new[] { 0, 1, 2, 3, 4 },
            Enum.GetValues<ShaderUniformType>().Select(value => (int)value));
    }

    [Fact]
    public void ShaderUniformIsAReadOnlyRecordStructWithTheApprovedFields()
    {
        Type type = typeof(ShaderUniform);

        Assert.True(type.IsPublic);
        Assert.True(type.IsValueType);
        Assert.Equal("Bork2d.Engine.Assets", type.Namespace);
        Assert.True(
            type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false),
            "ShaderUniform must be a readonly record struct.");

        ConstructorInfo constructor = Assert.Single(type.GetConstructors());
        ParameterInfo[] parameters = constructor.GetParameters();
        Assert.Equal(
            new[]
            {
                "String Name",
                "ShaderUniformType Type",
                "Int32 Offset",
                "Vector4 Default",
            },
            parameters.Select(parameter => $"{parameter.ParameterType.Name} {parameter.Name}"));

        PropertyInfo[] properties = type.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(
            new[] { "Int32 Offset", "ShaderUniformType Type", "String Name", "Vector4 Default" },
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
    public void ShaderTextureSlotIsAReadOnlyRecordStructWithTheApprovedField()
    {
        Type type = typeof(ShaderTextureSlot);

        Assert.True(type.IsPublic);
        Assert.True(type.IsValueType);
        Assert.Equal("Bork2d.Engine.Assets", type.Namespace);
        Assert.True(
            type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false),
            "ShaderTextureSlot must be a readonly record struct.");

        ConstructorInfo constructor = Assert.Single(type.GetConstructors());
        ParameterInfo parameter = Assert.Single(constructor.GetParameters());
        Assert.Equal(typeof(string), parameter.ParameterType);
        Assert.Equal("Name", parameter.Name);

        PropertyInfo property = Assert.Single(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal("String Name", $"{property.PropertyType.Name} {property.Name}");
    }

    [Fact]
    public void SpriteRenderDeclaresTheShaderContractMembers()
    {
        Type sprite = typeof(ISpriteRender<>);

        PropertyInfo shader = Assert.IsAssignableFrom<PropertyInfo>(
            sprite.GetProperty(nameof(ISpriteRender<GameLayer>.Shader)));
        Assert.Equal(typeof(ShaderAsset), shader.PropertyType);
        Assert.True(shader.CanRead);
        Assert.True(shader.CanWrite);

        PropertyInfo textures = Assert.IsAssignableFrom<PropertyInfo>(
            sprite.GetProperty(nameof(ISpriteRender<GameLayer>.AdditionalTextures)));
        Assert.Equal(typeof(AssetRef<TextureDescriptor>[]), textures.PropertyType);
        Assert.True(textures.CanRead);
        Assert.True(textures.CanWrite);

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;
        string[] uniformMethods = sprite
            .GetMethods(flags)
            .Where(method => !method.IsSpecialName)
            .Where(method => method.Name is nameof(ISpriteRender<GameLayer>.SetUniform)
                or nameof(ISpriteRender<GameLayer>.ResetUniforms))
            .Select(Describe)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

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
            uniformMethods);
    }

    [Fact]
    public void CurrentShaderLayoutVersionIsInternalAndThree()
    {
        Assert.False(typeof(ShaderLayout).IsPublic);
        Assert.Equal(3u, ShaderLayout.CurrentVersion);
    }

    private static string Describe(MethodInfo method)
    {
        string parameters = string.Join(
            ',',
            method.GetParameters().Select(parameter => parameter.ParameterType.Name));
        return $"{method.Name}({parameters})";
    }
}
