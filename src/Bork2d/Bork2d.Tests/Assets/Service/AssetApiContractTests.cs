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
        MethodInfo method = typeof(IAssets).GetMethod(nameof(IAssets.LoadAsset))
            ?? throw new InvalidOperationException("IAssets.LoadAsset was not found.");
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
    public void IAssetsDeclaresTheSixDocumentedMembers()
    {
        string[] members = typeof(IAssets)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Browse",
                "Exists",
                "LoadAsset",
                "LoadData",
                "LoadPack",
                "LoadSprite",
            },
            members);
    }
}
