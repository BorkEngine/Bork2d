using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Bork2d.Bake;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Assets.Pack;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Architecture;

public sealed class DependencyMatrixTests
{
    private const string EngineAssembly = "Bork2d.Engine";
    private const string LoggingAbstractions = "Microsoft.Extensions.Logging.Abstractions";
    private const string MessagePack = "Nerdbank.MessagePack";
    private const string PolyType = "PolyType";
    private const string Hashing = "System.IO.Hashing";
    private const string Tomlyn = "Tomlyn";
    private const string StbImageSharp = "StbImageSharp";
    private const string MetadataLoadContext = "System.Reflection.MetadataLoadContext";

    private static readonly string[] _forbiddenEngineReferencePrefixes =
    [
        "Silk.NET",
        "Vulkan",
        "Avalonia",
        "ZLogger",
        "Tomlyn",
        "StbImageSharp",
    ];

    private static readonly HashSet<string> _allowedBakeEngineNamespaces =
    [
        "Bork2d.Engine",
        "Bork2d.Engine.Rendering",
        "Bork2d.Engine.Assets",
        "Bork2d.Engine.Assets.Pack",
        "Bork2d.Engine.Assets.Vfs",
    ];

    [Fact]
    public void EngineReferencesOnlyTheAllowedContractAndImplementationSurface()
    {
        var assembly = typeof(IAssets).Assembly;

        Assert.Empty(Bork2dReferences(assembly));

        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            Assert.False(
                IsForbiddenEngineReference(reference.Name),
                $"{EngineAssembly} must not reference '{reference.Name}'.");
            Assert.True(
                IsFrameworkReference(reference.Name)
                    || reference.Name is LoggingAbstractions or PolyType or MessagePack or Hashing,
                $"{EngineAssembly} may only reference the BCL, {LoggingAbstractions}, {PolyType}, "
                    + $"{MessagePack}, and {Hashing}, not '{reference.Name}'.");
        }
    }

    [Fact]
    public void EngineReferencesTheGeneratedShapeAndHashingPackages()
    {
        var references = typeof(IAssets).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.Contains(PolyType, references);
        Assert.Contains(MessagePack, references);
        Assert.Contains(Hashing, references);
    }

    [Fact]
    public void EngineExposesOnlyTheDocumentedAreaNamespaces()
    {
        var namespaces = typeof(Bork2d.Engine.IEngine<>).Assembly
            .GetExportedTypes()
            .Select(type => type.Namespace ?? string.Empty)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Bork2d.Engine",
                "Bork2d.Engine.Assets",
                "Bork2d.Engine.Assets.Pack",
                "Bork2d.Engine.Input",
                "Bork2d.Engine.Platform",
                "Bork2d.Engine.Rendering",
                "Bork2d.Engine.Scene",
            },
            namespaces);
    }

    [Fact]
    public void EngineExposesOnlyThePackToolingFromTheAssetImplementation()
    {
        var exported = typeof(IAssets).Assembly
            .GetExportedTypes()
            .Where(type => string.Equals(
                type.Namespace,
                "Bork2d.Engine.Assets.Pack",
                StringComparison.Ordinal))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Bork2d.Engine.Assets.Pack.PackBuilder",
                "Bork2d.Engine.Assets.Pack.PackCompressionMode",
            },
            exported);
    }

    [Fact]
    public void EngineImplementationStaysInternal()
    {
        AssertInternal(typeof(Bork2d.Engine.Scene.Transform));
        AssertInternal(typeof(Bork2d.Engine.Scene.SceneRoot));
        AssertInternal(typeof(Bork2d.Engine.Scene.Camera<>));
        AssertInternal(typeof(Bork2d.Engine.Scene.SpriteRender<>));
        AssertInternal(typeof(Bork2d.Engine.Scene.GameEngine<>));
        AssertInternal(typeof(Bork2d.Engine.Assets.Loading.AssetService));
        AssertInternal(typeof(Bork2d.Engine.Assets.Vfs.AssetFileSystem));
        AssertInternal(typeof(Bork2d.Engine.Assets.Vfs.ResolvedAsset));
        AssertInternal(typeof(Bork2d.Engine.Assets.Pack.PackArchive));
        AssertInternal(typeof(Bork2d.Engine.Assets.Pack.PackHeader));
        AssertInternal(typeof(Bork2d.Engine.Rendering.FrameSnapshot));
        AssertInternal(typeof(Bork2d.Engine.Rendering.CameraSnapshot));
        AssertInternal(typeof(Bork2d.Engine.Rendering.SpriteSnapshot));
        AssertInternal(typeof(Bork2d.Engine.Rendering.SnapshotCapture<>));
        AssertInternal(typeof(Bork2d.Engine.Rendering.CameraProjection));
        AssertInternal(typeof(Bork2d.Engine.Rendering.CameraProjectionResult));
    }

    [Fact]
    public void EnginePublicSignaturesExposeNoImplementationPackages()
    {
        Assembly assembly = typeof(IAssets).Assembly;

        foreach (Type exported in assembly.GetExportedTypes())
        {
            foreach (Type signatureType in PublicSignatureTypes(exported).SelectMany(Flatten))
            {
                Type type = signatureType.IsByRef ? signatureType.GetElementType()! : signatureType;
                if (type.IsGenericParameter || type.Assembly == assembly)
                {
                    continue;
                }

                string? name = type.Assembly.GetName().Name;
                Assert.False(
                    name is MessagePack or Hashing or Tomlyn or StbImageSharp,
                    $"'{exported.FullName}' exposes the implementation type '{type.FullName}'.");
            }
        }
    }

    [Fact]
    public void RenderReferencesOnlyEngine()
    {
        AssertReferenceMatrix(
            typeof(Bork2d.Render.AssemblyReferenceAnchor).Assembly,
            EngineAssembly);
    }

    [Fact]
    public void RenderReferencesTheVulkanBindingsAndNoWindowTools()
    {
        var references = typeof(Bork2d.Render.AssemblyReferenceAnchor).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.Contains("Silk.NET.Vulkan", references);
        Assert.Contains("Silk.NET.Vulkan.Extensions.KHR", references);
        Assert.Contains("Silk.NET.Vulkan.Extensions.EXT", references);
        Assert.DoesNotContain("Silk.NET.SDL", references);
        Assert.DoesNotContain("Bork2d.Runtime", references);
        Assert.DoesNotContain("Bork2d.Bake", references);
    }

    [Fact]
    public void RenderExposesNoPublicTypes()
    {
        var exported = typeof(Bork2d.Render.AssemblyReferenceAnchor).Assembly
            .GetExportedTypes();

        Assert.Empty(exported);
    }

    [Fact]
    public void RenderProjectPinsTheVulkanPackagesAndEnablesUnsafeAndAotAnalysis()
    {
        string projectPath = Path.Combine(
            RepositoryPaths.Root,
            "src",
            "Bork2d",
            "Bork2d.Render",
            "Bork2d.Render.csproj");
        XDocument project = XDocument.Load(projectPath);

        Dictionary<string, string> packages = project
            .Descendants("PackageReference")
            .ToDictionary(
                reference => reference.Attribute("Include")!.Value,
                reference => reference.Attribute("Version")!.Value,
                StringComparer.Ordinal);

        Assert.Equal("2.23.0", packages["Silk.NET.Vulkan"]);
        Assert.Equal("2.23.0", packages["Silk.NET.Vulkan.Extensions.KHR"]);
        Assert.Equal("2.23.0", packages["Silk.NET.Vulkan.Extensions.EXT"]);

        Dictionary<string, string> properties = project
            .Descendants("PropertyGroup")
            .SelectMany(group => group.Elements())
            .ToDictionary(
                property => property.Name.LocalName,
                property => property.Value.Trim(),
                StringComparer.Ordinal);

        Assert.Equal("true", properties["AllowUnsafeBlocks"]);
        Assert.Equal("true", properties["IsAotCompatible"]);
        Assert.Equal("true", properties["EnableTrimAnalyzer"]);
        Assert.Equal("true", properties["EnableAotAnalyzer"]);
    }

    [Fact]
    public void RuntimeReferencesOnlyTheImplementationLayers()
    {
        Assembly runtimeAssembly = typeof(Bork2d.Runtime.AssemblyReferenceAnchor).Assembly;

        AssertReferenceMatrix(
            runtimeAssembly,
            EngineAssembly,
            "Bork2d.Render");

        string[] references = runtimeAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("Silk.NET.Vulkan", references);
        Assert.DoesNotContain("Silk.NET.Vulkan.Extensions.KHR", references);
        Assert.DoesNotContain("Silk.NET.Vulkan.Extensions.EXT", references);
    }

    [Fact]
    public void RuntimeExposesOnlyTheBuilderAndApp()
    {
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
    public void BakeReferencesOnlyEngine()
    {
        AssertReferenceMatrix(
            typeof(BakeApplicationBuilder).Assembly,
            EngineAssembly);
    }

    [Fact]
    public void BakeReferencesTheToolingPackagesAndNoRuntimeImplementation()
    {
        var references = typeof(BakeApplicationBuilder).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.Contains(Tomlyn, references);
        Assert.Contains(Hashing, references);
        Assert.DoesNotContain(MetadataLoadContext, references);
        Assert.DoesNotContain("Bork2d.Runtime", references);
        Assert.DoesNotContain("Bork2d.Render", references);
    }

    [Fact]
    public void BakeExposesTheDocumentedBuilderSurface()
    {
        var exported = typeof(BakeApplicationBuilder).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Bork2d.Bake.AssetProcessorContext",
                "Bork2d.Bake.AssetProcessor`1",
                "Bork2d.Bake.BakeApplication",
                "Bork2d.Bake.BakeApplicationBuilder",
                "Bork2d.Bake.IAssetOutputSink",
                "Bork2d.Bake.IAssetProcessor",
                "Bork2d.Bake.IAssetProcessorDiagnostics",
                "Bork2d.Bake.SidecarPolicy",
            },
            exported);
    }

    [Fact]
    public void BakePublicSignaturesExposeNoImplementationTypes()
    {
        Assembly assembly = typeof(BakeApplicationBuilder).Assembly;

        foreach (Type exported in assembly.GetExportedTypes())
        {
            foreach (Type signatureType in PublicSignatureTypes(exported).SelectMany(Flatten))
            {
                Type type = signatureType.IsByRef ? signatureType.GetElementType()! : signatureType;
                if (type.IsGenericParameter)
                {
                    continue;
                }

                if (type.Assembly != assembly)
                {
                    Assert.NotEqual(typeof(PackBuilder), type);
                    Assert.NotEqual(Tomlyn, type.Assembly.GetName().Name);
                    Assert.NotEqual(StbImageSharp, type.Assembly.GetName().Name);
                    continue;
                }

                string ns = type.Namespace ?? string.Empty;
                Assert.True(
                    ns is "Bork2d.Bake",
                    $"'{exported.FullName}' exposes the internal type '{type.FullName}'.");
            }
        }
    }

    [Fact]
    public void BakeImplementationStaysInternal()
    {
        AssertInternal(typeof(Bork2d.Bake.Baking.BakePipeline));
        AssertInternal(typeof(Bork2d.Bake.Processors.IBakeAssetProcessor));
        AssertInternal(typeof(Bork2d.Bake.Toml.TomlShapeBinder));
        AssertInternal(typeof(Bork2d.Bake.Textures.StbTextureDecoder));
    }

    [Fact]
    public void BakeReferencesOnlyTheAllowedEngineNamespaces()
    {
        // Q-20 is no longer assembly-enforced because the engine contract and implementation share
        // one assembly. The metadata guard proves the tooling only touches the public contract and
        // the two internal tooling seams (the pack writer and the canonical path grammar).
        Assembly assembly = typeof(BakeApplicationBuilder).Assembly;
        using var stream = File.OpenRead(assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();

        int references = 0;
        foreach (TypeReferenceHandle handle in reader.TypeReferences)
        {
            TypeReference reference = reader.GetTypeReference(handle);
            if (reference.ResolutionScope.Kind != HandleKind.AssemblyReference)
            {
                continue;
            }

            AssemblyReference scope = reader.GetAssemblyReference(
                (AssemblyReferenceHandle)reference.ResolutionScope);
            if (!string.Equals(reader.GetString(scope.Name), EngineAssembly, StringComparison.Ordinal))
            {
                continue;
            }

            references++;
            string ns = reader.GetString(reference.Namespace);
            Assert.True(
                _allowedBakeEngineNamespaces.Contains(ns),
                $"{EngineAssembly} tooling must not reference the engine namespace '{ns}'.");
        }

        Assert.True(references > 0, "Bake must reference the engine contract.");
    }

    [Fact]
    public void RuntimeAssembliesDoNotReferenceTheBakeTooling()
    {
        Assembly[] runtimeAssemblies =
        [
            typeof(IAssets).Assembly,
            typeof(Bork2d.Render.AssemblyReferenceAnchor).Assembly,
            typeof(Bork2d.Runtime.Bork2dApp).Assembly,
        ];

        foreach (Assembly assembly in runtimeAssemblies)
        {
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                string name = reference.Name ?? string.Empty;
                Assert.False(
                    name.StartsWith("Bork2d.Bake", StringComparison.Ordinal),
                    $"{assembly.GetName().Name} must not reference '{name}'.");
                Assert.NotEqual(Tomlyn, name);
                Assert.NotEqual(StbImageSharp, name);
            }
        }
    }

    [Fact]
    public void TestsMayReferenceEveryEngineProject()
    {
        AssertReferenceMatrix(
            typeof(DependencyMatrixTests).Assembly,
            EngineAssembly,
            "Bork2d.Bake",
            "Bork2d.Render",
            "Bork2d.Runtime");
    }

    [Fact]
    public void EngineSolutionContainsOnlyTheFiveProjects()
    {
        string solutionPath = Path.Combine(
            RepositoryPaths.Root,
            "src",
            "Bork2d",
            "Bork2d.slnx");
        string text = File.ReadAllText(solutionPath);
        string[] projects = Regex.Matches(text, "Project Path=\"(?<path>[^\"]+)\"")
            .Select(match => match.Groups["path"].Value)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Bork2d.Bake/Bork2d.Bake.csproj",
                "Bork2d.Engine/Bork2d.Engine.csproj",
                "Bork2d.Render/Bork2d.Render.csproj",
                "Bork2d.Runtime/Bork2d.Runtime.csproj",
                "Bork2d.Tests/Bork2d.Tests.csproj",
            },
            projects);
    }

    private static void AssertInternal(Type type)
    {
        Assert.False(type.IsPublic);
        Assert.DoesNotContain(type, type.Assembly.GetExportedTypes());
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

    private static IEnumerable<Type> PublicSignatureTypes(Type exported)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (MethodInfo method in exported.GetMethods(flags))
        {
            yield return method.ReturnType;
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (PropertyInfo property in exported.GetProperties(flags))
        {
            yield return property.PropertyType;
        }

        foreach (FieldInfo field in exported.GetFields(flags))
        {
            yield return field.FieldType;
        }
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.IsArray)
        {
            foreach (Type element in Flatten(type.GetElementType()!))
            {
                yield return element;
            }
        }

        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
            {
                foreach (Type nested in Flatten(argument))
                {
                    yield return nested;
                }
            }
        }
    }

    private static bool IsForbiddenEngineReference(string? name)
    {
        return name is not null
            && _forbiddenEngineReferencePrefixes.Any(
                prefix => name.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool IsFrameworkReference(string? name)
    {
        return name is null
            || name.StartsWith("System", StringComparison.Ordinal)
            || name is "mscorlib" or "netstandard";
    }
}
