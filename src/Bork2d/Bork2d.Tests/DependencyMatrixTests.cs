using System.Reflection;
using Xunit;

namespace Bork2d.Tests;

public sealed class DependencyMatrixTests
{
    private const string LoggingAbstractions = "Microsoft.Extensions.Logging.Abstractions";
    private const string MessagePack = "Nerdbank.MessagePack";

    private static readonly string[] _forbiddenApiReferencePrefixes =
    [
        "Silk.NET",
        "Vulkan",
        "Avalonia",
        "ZLogger",
    ];

    [Fact]
    public void ApiReferencesOnlyTheAllowedContractSurface()
    {
        var assembly = typeof(Bork2d.Api.IAssets).Assembly;

        Assert.Empty(Bork2dReferences(assembly));

        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            Assert.False(
                IsForbiddenApiReference(reference.Name),
                $"Bork2d.Api must not reference '{reference.Name}'.");
            Assert.True(
                IsFrameworkReference(reference.Name) || reference.Name == LoggingAbstractions,
                $"Bork2d.Api may only reference the BCL and {LoggingAbstractions}, not '{reference.Name}'.");
        }
    }

    [Fact]
    public void CommonReferencesOnlyApi()
    {
        AssertReferenceMatrix(typeof(Bork2d.Common.Transform).Assembly, "Bork2d.Api");
    }

    [Fact]
    public void CommonReferencesTheGeneratedShapeSerializationPackage()
    {
        var references = typeof(Bork2d.Common.Transform).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.Contains(MessagePack, references);
    }

    [Fact]
    public void CommonExposesNoPublicImplementationTypes()
    {
        Assert.Empty(typeof(Bork2d.Common.Transform).Assembly.GetExportedTypes());
    }

    [Fact]
    public void AssetsReferencesOnlyApiAndCommon()
    {
        AssertReferenceMatrix(
            typeof(Bork2d.Assets.PackBuilder).Assembly,
            "Bork2d.Api",
            "Bork2d.Common");
    }

    [Fact]
    public void AssetsExposesOnlyThePackToolingSurface()
    {
        var exported = typeof(Bork2d.Assets.PackBuilder).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Bork2d.Assets.PackBuilder",
                "Bork2d.Assets.PackCompressionMode",
            },
            exported);
    }

    [Fact]
    public void RenderReferencesOnlyApiCommonAndAssets()
    {
        AssertReferenceMatrix(
            typeof(Bork2d.Render.AssemblyReferenceAnchor).Assembly,
            "Bork2d.Api",
            "Bork2d.Common",
            "Bork2d.Assets");
    }

    [Fact]
    public void RuntimeReferencesOnlyTheImplementationLayers()
    {
        AssertReferenceMatrix(
            typeof(Bork2d.Runtime.AssemblyReferenceAnchor).Assembly,
            "Bork2d.Api",
            "Bork2d.Common",
            "Bork2d.Assets",
            "Bork2d.Render");
    }

    [Fact]
    public void RuntimeExposesOnlyTheBuilderAndApp()
    {
        // The SDL adapter is internal to the composition root: adding Silk.NET.SDL must not leak a
        // public type, and the public surface stays the M1 builder/app pair.
        var exported = typeof(Bork2d.Runtime.Bork2dApp).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Bork2d.Runtime.Bork2dApp",
                "Bork2d.Runtime.Bork2dAppBuilder`1",
            },
            exported);
    }

    [Fact]
    public void TestsMayReferenceEveryProject()
    {
        AssertReferenceMatrix(
            typeof(DependencyMatrixTests).Assembly,
            "Bork2d.Api",
            "Bork2d.Common",
            "Bork2d.Assets",
            "Bork2d.Render",
            "Bork2d.Runtime");
    }

    [Fact]
    public void AotSmokeReferencesOnlyTheRuntimeSurfaceItUses()
    {
        // Tests references Bork2d.AotSmoke as a project so its assembly is copied next to the
        // test assembly. No test type uses it, so it is loaded by name here.
        var assembly = Assembly.Load("Bork2d.AotSmoke");

        AssertReferenceMatrix(assembly, "Bork2d.Api", "Bork2d.Common", "Bork2d.Runtime");
    }

    private static void AssertReferenceMatrix(Assembly assembly, params string[] expectedReferences)
    {
        var expected = expectedReferences
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, Bork2dReferences(assembly));
    }

    private static string[] Bork2dReferences(Assembly assembly)
    {
        return assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Bork2d.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsForbiddenApiReference(string? name)
    {
        return name is not null
            && _forbiddenApiReferencePrefixes.Any(
                prefix => name.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool IsFrameworkReference(string? name)
    {
        return name is null
            || name.StartsWith("System", StringComparison.Ordinal)
            || name is "mscorlib" or "netstandard";
    }
}
