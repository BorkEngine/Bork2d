using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Xunit;

namespace Bork2d.Tests.Bake.Hosting;

/// <summary>
/// The generated project must reference <c>Project.Common</c> under the assembly's real metadata
/// name, not the file or folder name: a common assembly built as <c>Bork2d.BakeFixture.dll</c> was
/// previously added twice under two names (<c>Project.Common</c> and its real name), which produced a
/// duplicate-reference build.
/// </summary>
public sealed class HostCommonAssemblyNameTests
{
    [Fact]
    public void GeneratedProjectUsesTheCommonAssemblysRealNameOnce()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        directory.WriteFile(
            "Assets/data/source.toml",
            """
            value = 1

            [bork2d]
            type = "Bork2d.BakeFixture.GameConfig"
            """);
        string descriptorPath = directory.WriteFile(
            "bork2d.project.toml",
            """
            schema = 1

            [assets]
            root = "Assets"
            """);

        string commonPath = typeof(Bork2d.BakeFixture.GameConfig).Assembly.Location;
        var diagnostics = new BakeDiagnostics();
        var options = new HostGenerationOptions(
            descriptorPath,
            commonPath,
            directory.FullPath("host"),
            "Debug",
            force: false);

        HostGenerationResult? result = HostGenerator.Generate(options, diagnostics);

        Assert.NotNull(result);
        Assert.Empty(diagnostics.GetSorted());
        string project = File.ReadAllText(result.ProjectPath);
        Assert.Contains(
            "<Reference Include=\"Bork2d.BakeFixture\">",
            project,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Project.Common", project, StringComparison.Ordinal);
        Assert.Single(
            result.References,
            reference => string.Equals(
                reference.AssemblyName,
                "Bork2d.BakeFixture",
                StringComparison.Ordinal));
    }
}
