using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Project;
using Bork2d.Bake.Sources;
using Xunit;

namespace Bork2d.Tests.Bake.Sources;

/// <summary>
/// The source discovery contract: mapped <c>.toml</c> only, texture sidecars ignored, longest prefix
/// on a segment boundary, ordinal order, and Q-13 unmapped-source errors.
/// </summary>
public sealed class BakeSourceDiscoveryTests
{
    [Fact]
    public void MappedTomlSourcesAreDiscoveredAndOtherFilesAreIgnored()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(
            directory,
            """
            [[types]]
            prefix = "data"
            type = "BakeFixture.Common.GameConfig"
            """);
        directory.WriteFile("Assets/data/a.toml", "value = 1\n");
        directory.WriteFile("Assets/data/readme.md", "not a source\n");
        directory.WriteFile("Assets/textures/player.png.toml", "filter = \"nearest\"\n");
        directory.WriteFile("Assets/other/b.toml", "value = 2\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        BakeSource source = Assert.Single(sources);
        Assert.Equal("data/a", source.AssetPath);
        Assert.Equal("data/a.toml", source.RelativePath);
        Assert.Equal("data", source.Prefix);
        Assert.Equal("BakeFixture.Common.GameConfig", source.TypeName);

        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SourceUnmapped, diagnostic.Code);
        Assert.Contains("other/b.toml", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SourcesAreSortedOrdinalByCanonicalAssetPath()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(
            directory,
            """
            [[types]]
            prefix = "data"
            type = "BakeFixture.Common.GameConfig"
            """);
        directory.WriteFile("Assets/data/zeta.toml", "value = 1\n");
        directory.WriteFile("Assets/data/alpha.toml", "value = 1\n");
        directory.WriteFile("Assets/data/beta.toml", "value = 1\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Equal(
            new[] { "data/alpha", "data/beta", "data/zeta" },
            sources.Select(source => source.AssetPath));
        Assert.Empty(diagnostics.GetSorted());
    }

    [Fact]
    public void LongestPrefixOnASegmentBoundaryWins()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(
            directory,
            """
            [[types]]
            prefix = "data"
            type = "Type.General"

            [[types]]
            prefix = "data/special"
            type = "Type.Special"
            """);
        directory.WriteFile("Assets/data/special/x.toml", "value = 1\n");
        directory.WriteFile("Assets/data/other.toml", "value = 1\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.Equal(2, sources.Count);
        Assert.Equal("data/other", sources[0].AssetPath);
        Assert.Equal("Type.General", sources[0].TypeName);
        Assert.Equal("data/special/x", sources[1].AssetPath);
        Assert.Equal("Type.Special", sources[1].TypeName);
        Assert.Equal("data/special", sources[1].Prefix);
    }

    [Fact]
    public void PrefixNeverMatchesASiblingDirectory()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(
            directory,
            """
            [[types]]
            prefix = "data"
            type = "Type.General"
            """);
        directory.WriteFile("Assets/database/x.toml", "value = 1\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        Assert.Empty(sources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SourceUnmapped, diagnostic.Code);
    }

    [Fact]
    public void APrefixMayMatchTheAssetPathExactly()
    {
        using var directory = TempBakeDirectory.Create();
        ProjectDescriptor descriptor = Parse(
            directory,
            """
            [[types]]
            prefix = "config"
            type = "Type.Config"
            """);
        directory.WriteFile("Assets/config.toml", "value = 1\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        BakeSource source = Assert.Single(sources);
        Assert.Equal("config", source.AssetPath);
        Assert.Equal("config", source.Prefix);
        Assert.Empty(diagnostics.GetSorted());
    }


    private static ProjectDescriptor Parse(TempBakeDirectory directory, string typeMappings)
    {
        directory.CreateDirectory("Assets");
        string descriptorPath = directory.WriteFile(
            "bork2d.project.toml",
            $"""
            schema = 1

            [assets]
            root = "Assets"

            {typeMappings}
            """);

        var diagnostics = new BakeDiagnostics();
        ProjectDescriptor? descriptor = ProjectDescriptorParser.Parse(descriptorPath, diagnostics);
        Assert.NotNull(descriptor);
        Assert.Empty(diagnostics.GetSorted());
        return descriptor;
    }
}
