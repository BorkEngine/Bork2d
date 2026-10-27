using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Sources;
using Bork2d.Tests.Bake.Baking;
using Xunit;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// Processor-aware discovery: non-TOML processor sources, exact required sidecars, ordinary TOML next
/// to a sidecar-less processor, orphan sidecars, invalid base paths, and the WP2 legacy image-sidecar
/// bridge.
/// </summary>
public sealed class BakeProcessorDiscoveryTests
{
    [Fact]
    public void ProcessorSourceIsClassifiedWithItsBasePathAndMatchedExtension()
    {
        using var project = TestBakeProject.Create(
            "processor-source",
            processors: [new TestProcessor("test-stats", "stats")]);
        project.WriteAssetBytes("data/archer.stats", [1, 2, 3]);

        var diagnostics = new BakeDiagnostics();
        BakeDiscoveredSources discovered = BakeSourceDiscovery.Discover(project.Project, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.Empty(discovered.DataSources);
        BakeProcessorSource source = Assert.Single(discovered.ProcessorSources);
        Assert.Equal("data/archer.stats", source.RelativePath);
        Assert.Equal("data/archer", source.BaseAssetPath);
        Assert.Equal("stats", source.MatchedExtension);
        Assert.Equal("test-stats", source.Processor.Name);
        Assert.Equal(0, source.Pack.Index);
        Assert.Null(source.SidecarPath);
    }

    [Fact]
    public void CompoundSuffixRemovesTheWholeMatchedExtension()
    {
        using var project = TestBakeProject.Create(
            "processor-compound",
            processors:
            [
                new TestProcessor("gzip", "gz"),
                new TestProcessor("tarball", "tar.gz"),
            ]);
        project.WriteAssetBytes("art/Archive.TAR.GZ", [1]);

        var diagnostics = new BakeDiagnostics();
        BakeDiscoveredSources discovered = BakeSourceDiscovery.Discover(project.Project, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        BakeProcessorSource source = Assert.Single(discovered.ProcessorSources);
        Assert.Equal("art/Archive.TAR.GZ", source.RelativePath);
        Assert.Equal("art/archive", source.BaseAssetPath);
        Assert.Equal("tar.gz", source.MatchedExtension);
        Assert.Equal("tarball", source.Processor.Name);
    }

    [Fact]
    public void NonTomlFileWithNoRegisteredSuffixIsIgnored()
    {
        using var project = TestBakeProject.Create(
            "processor-unregistered",
            processors: [new TestProcessor("test-stats", "stats")]);
        project.WriteAssetBytes("art/player.png", [1]);

        var diagnostics = new BakeDiagnostics();
        BakeDiscoveredSources discovered = BakeSourceDiscovery.Discover(project.Project, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.Empty(discovered.ProcessorSources);
        Assert.Empty(discovered.DataSources);
    }

    [Fact]
    public void ExactSidecarIsAttachedWithoutReadingItsContent()
    {
        using var project = TestBakeProject.Create(
            "processor-sidecar",
            processors: [RequiredUnitProcessor()]);
        project.WriteAssetBytes("data/archer.unit", [1]);
        project.WriteAsset(
            "data/archer.unit.toml",
            """
            [bork2d]
            type = "Bork2d.BakeFixture.StatsRoot"
            """);

        var diagnostics = new BakeDiagnostics();
        BakeDiscoveredSources discovered = BakeSourceDiscovery.Discover(project.Project, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.Empty(discovered.DataSources);
        BakeProcessorSource source = Assert.Single(discovered.ProcessorSources);
        Assert.Equal("data/archer.unit.toml", source.SidecarRelativePath);
        Assert.Equal(project.FullPath("Assets/data/archer.unit.toml"), source.SidecarPath);
    }

    [Fact]
    public void SidecarAssociationIsCaseInsensitive()
    {
        using var project = TestBakeProject.Create(
            "processor-sidecar-case",
            processors: [RequiredUnitProcessor()]);
        project.WriteAssetBytes("data/Archer.UNIT", [1]);
        project.WriteAsset("data/ARCHER.unit.TOML", "value = 3\n");

        var diagnostics = new BakeDiagnostics();
        BakeDiscoveredSources discovered = BakeSourceDiscovery.Discover(project.Project, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        BakeProcessorSource source = Assert.Single(discovered.ProcessorSources);
        Assert.Equal("data/Archer.UNIT", source.RelativePath);
        Assert.Equal("data/ARCHER.unit.TOML", source.SidecarRelativePath);
    }

    [Fact]
    public void SidecarForANoneProcessorIsOrdinaryTomlAndMissingType()
    {
        using var project = TestBakeProject.Create(
            "processor-none-sidecar",
            processors: [new TestProcessor("test-stats", "stats")]);
        project.WriteAssetBytes("data/archer.stats", [1]);
        project.WriteAsset("data/archer.stats.toml", "value = 1\n");

        var diagnostics = new BakeDiagnostics();
        BakeDiscoveredSources discovered = BakeSourceDiscovery.Discover(project.Project, diagnostics);

        Assert.Single(discovered.ProcessorSources);
        Assert.Empty(discovered.DataSources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SourceTypeMissing, diagnostic.Code);
        Assert.Contains("data/archer.stats.toml", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OrphanRequiredSidecarIsBK2014()
    {
        using var project = TestBakeProject.Create(
            "processor-orphan",
            processors: [RequiredUnitProcessor()]);
        project.WriteAsset("data/ghost.unit.toml", "value = 1\n");

        var diagnostics = new BakeDiagnostics();
        BakeDiscoveredSources discovered = BakeSourceDiscovery.Discover(project.Project, diagnostics);

        Assert.Empty(discovered.ProcessorSources);
        Assert.Empty(discovered.DataSources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SidecarOrphaned, diagnostic.Code);
        Assert.Contains("ghost.unit.toml", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("test-unit", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidProcessorBasePathIsBK7002WithoutAnOrphanSidecar()
    {
        using var project = TestBakeProject.Create(
            "processor-invalid-base",
            processors: [RequiredUnitProcessor()]);
        project.WriteAssetBytes("data/bad name.unit", [1]);
        project.WriteAsset("data/bad name.unit.toml", "value = 1\n");

        var diagnostics = new BakeDiagnostics();
        BakeDiscoveredSources discovered = BakeSourceDiscovery.Discover(project.Project, diagnostics);

        Assert.Empty(discovered.ProcessorSources);
        Assert.Empty(discovered.DataSources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorInputInvalid, diagnostic.Code);
        Assert.Contains("bad name.unit", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("test-unit", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyImageSidecarIsStillLeftToTheTexturePipeline()
    {
        using var project = TestBakeProject.Create(
            "processor-legacy-bridge",
            processors: [new TestProcessor("test-stats", "stats")]);
        project.WriteAssetBytes("art/player.png", [1, 2, 3]);
        project.WriteAsset("art/player.png.toml", "filter = \"nearest\"\n");

        var diagnostics = new BakeDiagnostics();
        BakeDiscoveredSources discovered = BakeSourceDiscovery.Discover(project.Project, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.Empty(discovered.DataSources);
        Assert.Empty(discovered.ProcessorSources);
    }

    [Fact]
    public void TypedLegacyImageSidecarRemainsADataSource()
    {
        using var project = TestBakeProject.Create(
            "processor-legacy-typed",
            processors: [new TestProcessor("test-stats", "stats")]);
        project.WriteAssetBytes("art/player.png", [1, 2, 3]);
        project.WriteAsset(
            "art/player.png.toml",
            """
            [bork2d]
            type = "Bork2d.BakeFixture.StatsRoot"
            """);

        var diagnostics = new BakeDiagnostics();
        BakeDiscoveredSources discovered = BakeSourceDiscovery.Discover(project.Project, diagnostics);

        Assert.Empty(discovered.DataSources);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SourceInvalidAssetPath, diagnostic.Code);
    }

    private static TestSidecarProcessor RequiredUnitProcessor()
        => new("test-unit", "unit", _ => new TestSidecar { Value = 1 });
}
