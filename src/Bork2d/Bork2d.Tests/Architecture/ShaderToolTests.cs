using System.Xml.Linq;
using Bork2d.Render;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Architecture;

/// <summary>
/// Proves the shader compiler is build-only and never a runtime dependency (M8 §6.7/§13.9).
/// </summary>
public sealed class ShaderToolTests
{
    [Fact]
    public void RenderReferencesNoShaderCompilerAssembly()
    {
        string[] references = typeof(AssemblyReferenceAnchor).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("Silk.NET.Shaderc", references);
        Assert.DoesNotContain("Bork2d.ShaderCompiler", references);
    }

    [Fact]
    public void ShaderCompilerPinsTheApprovedPackages()
    {
        string projectPath = Path.Combine(
            RepositoryPaths.Root,
            "build",
            "Bork2d.ShaderCompiler",
            "Bork2d.ShaderCompiler.csproj");
        Dictionary<string, string> packages = XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .ToDictionary(
                reference => reference.Attribute("Include")!.Value,
                reference => reference.Attribute("Version")!.Value,
                StringComparer.Ordinal);

        Assert.Equal("2.23.0", packages["Silk.NET.Shaderc"]);
        Assert.Equal("2.23.0", packages["Silk.NET.Shaderc.Native"]);
    }

    [Fact]
    public void ShaderCompilerIsOutsideTheRuntimeSolution()
    {
        string solution = File.ReadAllText(
            Path.Combine(RepositoryPaths.Root, "src", "Bork2d", "Bork2d.slnx"));

        Assert.DoesNotContain("Bork2d.ShaderCompiler", solution, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderProjectImportsTheShaderTargetsAndHasNoShaderPackage()
    {
        string projectPath = Path.Combine(
            RepositoryPaths.Root,
            "src",
            "Bork2d",
            "Bork2d.Render",
            "Bork2d.Render.csproj");
        XDocument project = XDocument.Load(projectPath);

        Assert.Contains(
            project.Descendants("Import"),
            element => element.Attribute("Project")?.Value.Contains(
                "Bork2d.ShaderCompiler.targets",
                StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            project.Descendants("PackageReference"),
            element => element.Attribute("Include")?.Value.StartsWith(
                "Silk.NET.Shaderc",
                StringComparison.Ordinal) == true);
    }
}
