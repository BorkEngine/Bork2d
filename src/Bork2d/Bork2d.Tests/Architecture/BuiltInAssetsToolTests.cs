using System.Reflection;
using System.Xml.Linq;
using Bork2d.Engine.Assets;
using Bork2d.Render;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Architecture;

/// <summary>
/// Proves the built-in pack generator is build-only and never a runtime dependency, and that the
/// embedded pack is produced by the ordinary bake tooling instead of a runtime shader compiler.
/// </summary>
public sealed class BuiltInAssetsToolTests
{
    [Fact]
    public void GeneratorIsOutsideTheRuntimeSolution()
    {
        string solution = File.ReadAllText(
            Path.Combine(RepositoryPaths.Root, "src", "Bork2d", "Bork2d.slnx"));

        Assert.DoesNotContain("Bork2d.BuiltInAssets", solution, StringComparison.Ordinal);
    }

    [Fact]
    public void NoRuntimeAssemblyReferencesTheGeneratorTheBakeOrTheShaderCompiler()
    {
        Assembly[] runtimeAssemblies =
        [
            typeof(IAssets).Assembly,
            typeof(AssemblyReferenceAnchor).Assembly,
            typeof(Bork2d.Runtime.Bork2dApp).Assembly,
        ];

        foreach (Assembly assembly in runtimeAssemblies)
        {
            string[] references = assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name ?? string.Empty)
                .ToArray();

            Assert.DoesNotContain("Bork2d.BuiltInAssets", references);
            Assert.DoesNotContain("Bork2d.Bake", references);
            Assert.DoesNotContain("Silk.NET.Shaderc", references);
            Assert.DoesNotContain("Silk.NET.Shaderc.Native", references);
        }
    }

    [Fact]
    public void GeneratorProjectIsABuildOnlyExecutable()
    {
        string projectPath = Path.Combine(
            RepositoryPaths.Root,
            "build",
            "Bork2d.BuiltInAssets",
            "Bork2d.BuiltInAssets.csproj");
        XDocument project = XDocument.Load(projectPath);

        Dictionary<string, string> properties = project
            .Descendants("PropertyGroup")
            .SelectMany(group => group.Elements())
            .ToDictionary(
                property => property.Name.LocalName,
                property => property.Value.Trim(),
                StringComparer.Ordinal);

        Assert.Equal("Exe", properties["OutputType"]);
        Assert.Equal("false", properties["IsAotCompatible"]);
        Assert.Equal("false", properties["PublishAot"]);
        Assert.Equal("false", properties["IsPackable"]);
        Assert.Equal("true", properties["AllowUnsafeBlocks"]);

        string[] references = project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")!.Value.Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "../../src/Bork2d/Bork2d.Bake/Bork2d.Bake.csproj",
                "../../src/Bork2d/Bork2d.Engine/Bork2d.Engine.csproj",
            },
            references);
    }

    [Fact]
    public void RuntimeProjectImportsTheGeneratorTargetsAndHasNoShaderCompilerPackage()
    {
        string projectPath = Path.Combine(
            RepositoryPaths.Root,
            "src",
            "Bork2d",
            "Bork2d.Runtime",
            "Bork2d.Runtime.csproj");
        XDocument project = XDocument.Load(projectPath);

        Assert.Contains(
            project.Descendants("Import"),
            element => element.Attribute("Project")?.Value.Contains(
                "Bork2d.BuiltInAssets.targets",
                StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            project.Descendants("PackageReference"),
            element => element.Attribute("Include")?.Value.StartsWith(
                "Silk.NET.Shaderc",
                StringComparison.Ordinal) == true);
    }
}
