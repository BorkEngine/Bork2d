using System.Reflection;
using Xunit;

namespace Bork2d.Tests.Architecture;

public sealed class DependencyMatrixTests
{
    private const string LoggingAbstractions = "Microsoft.Extensions.Logging.Abstractions";
    private const string MessagePack = "Nerdbank.MessagePack";
    private const string PolyType = "PolyType";
    private const string Hashing = "System.IO.Hashing";
    private const string Tomlyn = "Tomlyn";
    private const string StbImageSharp = "StbImageSharp";

    private static readonly string[] _forbiddenApiReferencePrefixes =
    [
        "Silk.NET",
        "Vulkan",
        "Avalonia",
        "ZLogger",
        "Nerdbank.MessagePack",
        "System.IO.Hashing",
    ];

    [Fact]
    public void ApiReferencesOnlyTheAllowedContractSurface()
    {
        var assembly = typeof(Bork2d.Api.Assets.IAssets).Assembly;

        Assert.Empty(Bork2dReferences(assembly));

        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            Assert.False(
                IsForbiddenApiReference(reference.Name),
                $"Bork2d.Api must not reference '{reference.Name}'.");
            Assert.True(
                IsFrameworkReference(reference.Name)
                    || reference.Name is LoggingAbstractions or PolyType,
                $"Bork2d.Api may only reference the BCL, {LoggingAbstractions}, and {PolyType}, "
                    + $"not '{reference.Name}'.");
        }
    }

    [Fact]
    public void ApiReferencesPolyTypeOnlyForTheShapeContract()
    {
        var references = typeof(Bork2d.Api.Assets.IAssets).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.Contains(PolyType, references);
        Assert.DoesNotContain(MessagePack, references);
        Assert.DoesNotContain(Hashing, references);
    }

    [Fact]
    public void CommonReferencesOnlyApi()
    {
        AssertReferenceMatrix(typeof(Bork2d.Common.Scene.Transform).Assembly, "Bork2d.Api");
    }

    [Fact]
    public void CommonReferencesTheGeneratedShapeSerializationPackage()
    {
        var references = typeof(Bork2d.Common.Scene.Transform).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.Contains(MessagePack, references);
    }

    [Fact]
    public void CommonExposesNoPublicImplementationTypes()
    {
        Assert.Empty(typeof(Bork2d.Common.Scene.Transform).Assembly.GetExportedTypes());
    }

    [Fact]
    public void ApiExposesOnlyTheDocumentedAreaNamespaces()
    {
        var namespaces = typeof(Bork2d.Api.IEngine<>).Assembly
            .GetExportedTypes()
            .Select(type => type.Namespace ?? string.Empty)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Bork2d.Api",
                "Bork2d.Api.Assets",
                "Bork2d.Api.Input",
                "Bork2d.Api.Platform",
                "Bork2d.Api.Rendering",
                "Bork2d.Api.Scene",
            },
            namespaces);
    }

    [Fact]
    public void AssetsReferencesOnlyApiAndCommon()
    {
        AssertReferenceMatrix(
            typeof(Bork2d.Assets.Pack.PackBuilder).Assembly,
            "Bork2d.Api",
            "Bork2d.Common");
    }

    [Fact]
    public void AssetsExposesOnlyThePackToolingSurface()
    {
        var exported = typeof(Bork2d.Assets.Pack.PackBuilder).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Bork2d.Assets.Pack.PackBuilder",
                "Bork2d.Assets.Pack.PackCompressionMode",
            },
            exported);
    }

    [Fact]
    public void AssetFileSystemStaysInternal()
    {
        var assembly = typeof(Bork2d.Assets.Pack.PackBuilder).Assembly;
        var type = typeof(Bork2d.Assets.Vfs.AssetFileSystem);

        Assert.False(type.IsPublic);
        Assert.DoesNotContain(type, assembly.GetExportedTypes());
        Assert.False(typeof(Bork2d.Assets.Vfs.ResolvedAsset).IsPublic);
        Assert.DoesNotContain(typeof(Bork2d.Assets.Vfs.ResolvedAsset), assembly.GetExportedTypes());
        Assert.False(typeof(Bork2d.Assets.Loading.AssetService).IsPublic);
        Assert.DoesNotContain(typeof(Bork2d.Assets.Loading.AssetService), assembly.GetExportedTypes());
    }

    [Fact]
    public void BakeReferencesOnlyApiAndAssets()
    {
        AssertReferenceMatrix(
            typeof(Bork2d.Bake.AssemblyReferenceAnchor).Assembly,
            "Bork2d.Api",
            "Bork2d.Assets");
    }

    [Fact]
    public void BakeReferencesTheToolingPackagesAndNoRuntimeImplementation()
    {
        var references = typeof(Bork2d.Bake.AssemblyReferenceAnchor).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.Contains(Tomlyn, references);
        Assert.Contains(Hashing, references);
        Assert.DoesNotContain("Bork2d.Common", references);
        Assert.DoesNotContain("Bork2d.Runtime", references);
        Assert.DoesNotContain("Bork2d.Render", references);
    }

    [Fact]
    public void BakeExposesNoPublicTypes()
    {
        Assert.Empty(typeof(Bork2d.Bake.AssemblyReferenceAnchor).Assembly.GetExportedTypes());
    }

    [Fact]
    public void CliReferencesOnlyBake()
    {
        AssertReferenceMatrix(
            typeof(Bork2d.Bake.Cli.CliApplication).Assembly,
            "Bork2d.Bake");
    }

    [Fact]
    public void CliExposesNoPublicTypes()
    {
        Assert.Empty(typeof(Bork2d.Bake.Cli.CliApplication).Assembly.GetExportedTypes());
    }

    [Fact]
    public void RuntimeAssembliesDoNotReferenceTheBakeTooling()
    {
        Assembly[] runtimeAssemblies =
        [
            typeof(Bork2d.Api.Assets.IAssets).Assembly,
            typeof(Bork2d.Common.Scene.Transform).Assembly,
            typeof(Bork2d.Assets.Pack.PackBuilder).Assembly,
            typeof(Bork2d.Render.AssemblyReferenceAnchor).Assembly,
            typeof(Bork2d.Runtime.Bork2dApp).Assembly,
            Assembly.Load("Bork2d.AotSmoke"),
        ];

        foreach (Assembly assembly in runtimeAssemblies)
        {
            // Bork2d.BakeFixture is the game-side Project.Common-like fixture (Q-16), not tooling; the
            // AOT smoke references it deliberately. Everything else must not reference any Bake*
            // assembly, the fixture included.
            bool isAotSmoke = string.Equals(
                assembly.GetName().Name,
                "Bork2d.AotSmoke",
                StringComparison.Ordinal);

            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                string name = reference.Name ?? string.Empty;
                Assert.False(
                    name is "Bork2d.Bake" or "Bork2d.Bake.Cli" or "Bork2d.BakeHost",
                    $"{assembly.GetName().Name} must not reference '{name}'.");
                if (!isAotSmoke)
                {
                    Assert.False(
                        name.StartsWith("Bork2d.Bake", StringComparison.Ordinal),
                        $"{assembly.GetName().Name} must not reference '{name}'.");
                }

                Assert.NotEqual(Tomlyn, name);
                Assert.NotEqual(StbImageSharp, name);
            }
        }
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
    public void BakeFixtureReferencesOnlyApiAndNoTooling()
    {
        // The fixture stands in for a game's Project.Common: public data types and Bork2d.Api only.
        // It must stay usable from the AOT proof and must never pull the bake tooling into a game.
        AssertReferenceMatrix(typeof(Bork2d.BakeFixture.GameConfig).Assembly, "Bork2d.Api");

        var references = typeof(Bork2d.BakeFixture.GameConfig).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(Tomlyn, references);
        Assert.DoesNotContain(StbImageSharp, references);
        Assert.DoesNotContain("Bork2d.Bake", references);
        Assert.DoesNotContain("Bork2d.Bake.Cli", references);
    }

    [Fact]
    public void BakeFixtureExposesOnlyItsRegisteredDataTypes()
    {
        var exported = typeof(Bork2d.BakeFixture.GameConfig).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Bork2d.BakeFixture.GameConfig",
                "Bork2d.BakeFixture.Stats",
                "Bork2d.BakeFixture.StatsRoot",
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
            "Bork2d.Bake",
            "Bork2d.Bake.Cli",
            "Bork2d.BakeFixture",
            "Bork2d.Render",
            "Bork2d.Runtime");
    }

    [Fact]
    public void AotSmokeReferencesOnlyTheRuntimeSurfaceItUses()
    {
        // Tests references Bork2d.AotSmoke as a project so its assembly is copied next to the
        // test assembly. No test type uses it, so it is loaded by name here. Assets is referenced
        // for the public pack-tooling surface used by the default AOT pack proof (D-M3-04/D-M3-19),
        // and BakeFixture is the Project.Common-like fixture the M4 targets bake and the WP6 smoke
        // loads (Q-16); it is never the bake tooling itself.
        var assembly = Assembly.Load("Bork2d.AotSmoke");

        AssertReferenceMatrix(
            assembly,
            "Bork2d.Api",
            "Bork2d.Assets",
            "Bork2d.BakeFixture",
            "Bork2d.Common",
            "Bork2d.Runtime");
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
