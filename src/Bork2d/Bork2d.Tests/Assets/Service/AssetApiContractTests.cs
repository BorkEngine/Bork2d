using System.Reflection;
using System.Runtime.CompilerServices;
using Bork2d.Api.Assets;
using Bork2d.Tests.Assets.Pack;
using PolyType;
using Xunit;

namespace Bork2d.Tests.Assets.Service;

public sealed class AssetApiContractTests
{
    [Fact]
    public void LoadAssetRequiresAClassWithTheShapeableConstraint()
    {
        MethodInfo method = typeof(IAssets)
            .GetMethods()
            .Single(candidate =>
                candidate.Name == nameof(IAssets.LoadAsset)
                && candidate.GetParameters()[0].ParameterType == typeof(string)
                && candidate.IsGenericMethodDefinition);
        Assert.True(method.IsGenericMethodDefinition);

        Type parameter = Assert.Single(method.GetGenericArguments());
        Assert.True(
            parameter.GenericParameterAttributes.HasFlag(
                GenericParameterAttributes.ReferenceTypeConstraint),
            "LoadAsset<T> must constrain T to a reference type.");
        Assert.True(
            !parameter.GenericParameterAttributes.HasFlag(
                GenericParameterAttributes.DefaultConstructorConstraint),
            "LoadAsset<T> must not require a parameterless constructor.");

        Type constraint = Assert.Single(parameter.GetGenericParameterConstraints());
        Assert.True(constraint.IsGenericType);
        Assert.Equal(typeof(IShapeable<>), constraint.GetGenericTypeDefinition());
        Assert.Same(parameter, Assert.Single(constraint.GetGenericArguments()));
    }

    [Fact]
    public void ShapeConstraintIsSatisfiedByACompiledShape()
    {
        Type[] interfaces = typeof(PackTestData)
            .GetInterfaces()
            .Select(candidate => candidate.IsGenericType
                ? candidate.GetGenericTypeDefinition()
                : candidate)
            .ToArray();

        Assert.Contains(typeof(IShapeable<>), interfaces);
    }

    [Fact]
    public void PublicAssetTypesCarryCompiledShapes()
    {
        Assert.Contains(
            typeof(IShapeable<>),
            typeof(SpriteAsset).GetInterfaces().Select(UnwrapGeneric));
        Assert.Contains(
            typeof(IShapeable<>),
            typeof(TextureDescriptor).GetInterfaces().Select(UnwrapGeneric));
    }

    [Fact]
    public void AssetInfoIsAPublicReadOnlyRecordStructWithTheDocumentedFields()
    {
        Type type = typeof(AssetInfo);

        Assert.True(type.IsPublic);
        Assert.True(type.IsValueType);
        Assert.True(type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false));

        string[] fields = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.PropertyType.Name} {property.Name}")
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Boolean IsCompressed",
                "Boolean IsRaw",
                "Int64 Length",
                "String PackPath",
                "String Path",
                "Int64 Size",
            },
            fields);
    }

    [Fact]
    public void IAssetsDeclaresOnlyTheDocumentedLoadMembers()
    {
        string[] signatures = typeof(IAssets)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(Describe)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Browse(String)",
                "Exists(String)",
                "LoadAsset(AssetRef`1)",
                "LoadAsset(String)",
                "LoadData(DataRef)",
                "LoadData(String)",
                "LoadPack(String)",
            },
            signatures);
    }

    [Fact]
    public void SpriteHandleTypeIsGone()
    {
        Assert.DoesNotContain(
            typeof(IAssets).Assembly.GetExportedTypes(),
            type => type.FullName == "Bork2d.Api.Rendering.Sprite");
        Assert.DoesNotContain(
            typeof(IAssets).GetMethods(),
            method => method.Name == "LoadSprite");
    }

    [Fact]
    public void AssetReferenceValuesExposeTheirPaths()
    {
        var asset = new SpriteAsset
        {
            Texture = new AssetRef<TextureDescriptor>("textures/player"),
            Scale = 16f,
        };

        Assert.Equal("textures/player", asset.Texture.Path);
        Assert.True(asset.Texture.HasValue);
        Assert.False(default(AssetRef<TextureDescriptor>).HasValue);

        DataRef data = "textures/raw/player";
        Assert.Equal("textures/raw/player", data.Path);
        Assert.True(data.HasValue);
        Assert.False(default(DataRef).HasValue);
        Assert.Null(default(AssetRef<TextureDescriptor>).Path);
        Assert.Null(default(DataRef).Path);
    }

    private static Type UnwrapGeneric(Type type)
        => type.IsGenericType ? type.GetGenericTypeDefinition() : type;

    private static string Describe(MethodInfo method)
    {
        string parameters = string.Join(
            ',',
            method.GetParameters().Select(parameter =>
            {
                Type type = parameter.ParameterType;
                if (type.IsGenericType)
                {
                    return type.GetGenericTypeDefinition().Name;
                }

                return type.Name;
            }));
        return $"{method.Name}({parameters})";
    }
}
