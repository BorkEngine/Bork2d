using System.Reflection;
using Bork2d.Api;
using Bork2d.Assets;
using Xunit;

namespace Bork2d.Tests;

public sealed class AssetExceptionContractTests
{
    [Fact]
    public void AssetErrorKindHasTheDocumentedValuesInOrder()
    {
        Assert.Equal(
            new[] { "NotFound", "RawAsset", "Decode", "Io", "InvalidData" },
            Enum.GetNames<AssetErrorKind>());

        Assert.Equal(
            new[] { 0, 1, 2, 3, 4 },
            Enum.GetValues<AssetErrorKind>().Select(value => (int)value));
    }

    [Fact]
    public void ConstructorsStoreKindPathMessageAndInnerException()
    {
        var inner = new InvalidOperationException("inner");

        var simple = new AssetException(AssetErrorKind.NotFound, "data/config", "not found");
        Assert.Equal(AssetErrorKind.NotFound, simple.Kind);
        Assert.Equal("data/config", simple.AssetPath);
        Assert.Equal("not found", simple.Message);
        Assert.Null(simple.InnerException);

        var wrapped = new AssetException(AssetErrorKind.Decode, null, "decode failed", inner);
        Assert.Equal(AssetErrorKind.Decode, wrapped.Kind);
        Assert.Null(wrapped.AssetPath);
        Assert.Equal("decode failed", wrapped.Message);
        Assert.Same(inner, wrapped.InnerException);
    }

    [Fact]
    public void AssetExceptionDerivesFromBork2dException()
    {
        Assert.True(typeof(Bork2dException).IsAssignableFrom(typeof(AssetException)));

        var exception = new AssetException(AssetErrorKind.Io, "assets.pck", "read failed");
        Assert.IsAssignableFrom<Bork2dException>(exception);
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void AssetExceptionIsSealedWithExactlyTheTwoDocumentedConstructors()
    {
        Type type = typeof(AssetException);

        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.False(type.IsDefined(typeof(SerializableAttribute), inherit: false));

        string[][] signatures = type.GetConstructors()
            .Select(constructor => constructor.GetParameters()
                .Select(parameter => parameter.ParameterType.Name)
                .ToArray())
            .OrderBy(parameters => parameters.Length)
            .ToArray();

        Assert.Equal(2, signatures.Length);
        Assert.Equal(["AssetErrorKind", "String", "String"], signatures[0]);
        Assert.Equal(["AssetErrorKind", "String", "String", "Exception"], signatures[1]);
    }

    [Fact]
    public void AssetExceptionDeclaresOnlyKindAndAssetPath()
    {
        PropertyInfo[] properties = typeof(AssetException)
            .GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "String AssetPath",
                "AssetErrorKind Kind",
            },
            properties.Select(property => $"{property.PropertyType.Name} {property.Name}"));
        Assert.All(properties, property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void TheAssetServiceStaysInternalAndUnExported()
    {
        var assembly = typeof(PackBuilder).Assembly;

        Assert.False(typeof(AssetService).IsPublic);
        Assert.DoesNotContain(typeof(AssetService), assembly.GetExportedTypes());
        Assert.DoesNotContain(typeof(AssetFileSystem), assembly.GetExportedTypes());
        Assert.DoesNotContain(typeof(AssetRequest), assembly.GetExportedTypes());
    }

    [Fact]
    public void ApiExposesTheErrorTypePair()
    {
        string[] exported = typeof(AssetException).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.Contains("Bork2d.Api.AssetException", exported);
        Assert.Contains("Bork2d.Api.AssetErrorKind", exported);
    }
}
