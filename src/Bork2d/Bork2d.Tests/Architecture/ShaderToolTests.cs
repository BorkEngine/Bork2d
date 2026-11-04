using System.Xml.Linq;
using Bork2d.Render;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Architecture;

/// <summary>
/// Proves the renderer loads its default shaders from the embedded built-in pack at runtime and that
/// the retired build-time C# shader generator, its target, and its checked-in GLSL sources are gone.
/// </summary>
public sealed class ShaderToolTests
{
    private static readonly string[] _renderShaderSources =
    [
        Path.Combine("src", "Bork2d", "Bork2d.Render", "Shaders", "sprite.vert"),
        Path.Combine("src", "Bork2d", "Bork2d.Render", "Shaders", "sprite.frag"),
    ];

    private static readonly string[] _builtInShaderSources =
    [
        Path.Combine("src", "Bork2d", "BuiltInAssets", "Assets", "shader", "vertex.vert"),
        Path.Combine("src", "Bork2d", "BuiltInAssets", "Assets", "shader", "line-vertex.vert"),
        Path.Combine("src", "Bork2d", "BuiltInAssets", "Assets", "shader", "sprite.frag"),
        Path.Combine("src", "Bork2d", "BuiltInAssets", "Assets", "shader", "sprite.frag.toml"),
        Path.Combine("src", "Bork2d", "BuiltInAssets", "Assets", "shader", "line.frag"),
        Path.Combine("src", "Bork2d", "BuiltInAssets", "Assets", "shader", "line.frag.toml"),
        Path.Combine("src", "Bork2d", "BuiltInAssets", "Assets", "present", "vertex.pvert"),
        Path.Combine("src", "Bork2d", "BuiltInAssets", "Assets", "present", "fragment.pfrag"),
    ];

    [Fact]
    public void RenderReferencesNoShaderCompilerAssemblyOrPackage()
    {
        string[] references = typeof(AssemblyReferenceAnchor).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("Silk.NET.Shaderc", references);
        Assert.DoesNotContain("Silk.NET.Shaderc.Native", references);
        Assert.DoesNotContain("Bork2d.ShaderCompiler", references);
    }

    [Fact]
    public void TheLegacyShaderCompilerProjectAndTargetAreGone()
    {
        string toolDirectory = Path.Combine(
            RepositoryPaths.Root,
            "build",
            "Bork2d.ShaderCompiler");

        // A developer checkout can keep untracked bin/obj output under the old tool directory, so
        // assert the retired sources and integration are gone rather than that the directory is.
        Assert.False(
            File.Exists(Path.Combine(toolDirectory, "Bork2d.ShaderCompiler.csproj")),
            "The retired build/Bork2d.ShaderCompiler/Bork2d.ShaderCompiler.csproj must not exist.");
        Assert.False(
            File.Exists(Path.Combine(toolDirectory, "Program.cs")),
            "The retired build/Bork2d.ShaderCompiler/Program.cs must not exist.");
        Assert.False(
            File.Exists(Path.Combine(RepositoryPaths.Root, "build", "Bork2d.ShaderCompiler.targets")),
            "The retired build/Bork2d.ShaderCompiler.targets integration must not exist.");

        if (Directory.Exists(toolDirectory))
        {
            Assert.Empty(
                Directory.EnumerateFiles(toolDirectory, "*.csproj", SearchOption.AllDirectories));
        }
    }

    [Fact]
    public void RenderProjectImportsNoShaderTargetsAndReferencesNoShaderc()
    {
        string projectPath = Path.Combine(
            RepositoryPaths.Root,
            "src",
            "Bork2d",
            "Bork2d.Render",
            "Bork2d.Render.csproj");
        XDocument project = XDocument.Load(projectPath);

        Assert.DoesNotContain(
            project.Descendants("Import"),
            element => element.Attribute("Project")?.Value.Contains(
                "Bork2d.ShaderCompiler",
                StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            project.Descendants("PackageReference"),
            element => element.Attribute("Include")?.Value.StartsWith(
                "Silk.NET.Shaderc",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void TheLegacyRenderShaderSourcesAreGone()
    {
        foreach (string relativePath in _renderShaderSources)
        {
            Assert.False(
                File.Exists(Path.Combine(RepositoryPaths.Root, relativePath)),
                $"The renderer must not carry a compiled-in shader source at '{relativePath}'.");
        }
    }

    [Fact]
    public void TheBuiltInShaderSourcesLiveUnderBuiltInAssets()
    {
        foreach (string relativePath in _builtInShaderSources)
        {
            Assert.True(
                File.Exists(Path.Combine(RepositoryPaths.Root, relativePath)),
                $"The built-in shader source '{relativePath}' must exist under BuiltInAssets.");
        }
    }

    [Fact]
    public void TheShaderCompilerIsOutsideTheRuntimeSolution()
    {
        string solution = File.ReadAllText(
            Path.Combine(RepositoryPaths.Root, "src", "Bork2d", "Bork2d.slnx"));

        Assert.DoesNotContain("Bork2d.ShaderCompiler", solution, StringComparison.Ordinal);
    }
}
