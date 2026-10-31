using Bork2d.Bake;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Processors;
using Bork2d.Bake.Sources;
using Bork2d.Tests.Bake.Baking;
using Xunit;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// The required-sidecar lifecycle: deterministic creation only when missing, no overwrite, canonical
/// TOML, binder diagnostics, unsupported references, and sidecar creation failures.
/// </summary>
public sealed class BakeSidecarTests
{
    [Fact]
    public void MissingSidecarIsCreatedOnceAsCanonicalTomlWithAnInfo()
    {
        var processor = new TestSidecarProcessor(
            "test-unit",
            "unit",
            _ => new TestSidecar { Value = 7, Mode = TestSidecarMode.Slow });
        using var project = TestBakeProject.Create("sidecar-create", processors: [processor]);
        project.WriteAssetBytes("data/archer.unit", [1]);

        var diagnostics = new BakeDiagnostics();
        _ = Run(project, diagnostics);

        Assert.DoesNotContain(
            diagnostics.GetSorted(),
            diagnostic => diagnostic.Severity == BakeSeverity.Error);
        BakeDiagnostic info = Assert.Single(
            diagnostics.GetSorted(),
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.Equal(BakeSeverity.Info, info.Severity);
        Assert.Equal("Assets/data/archer.unit.toml", info.FilePath);
        Assert.Equal(1, processor.CreateDefaultSidecarCount);
        Assert.Equal(1, processor.ProcessCount);

        string sidecarPath = project.FullPath("Assets/data/archer.unit.toml");
        Assert.True(File.Exists(sidecarPath));
        string text = File.ReadAllText(sidecarPath);
        Assert.Equal("value = 7\nmode = \"slow\"\n", text);

        byte[] bytes = File.ReadAllBytes(sidecarPath);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public void CheckModeUsesTheDefaultSidecarInMemoryWithoutWritingIt()
    {
        TestSidecar? bound = null;
        var processor = new TestSidecarProcessor(
            "test-unit",
            "unit",
            _ => new TestSidecar { Value = 7, Mode = TestSidecarMode.Slow },
            (_, sidecar) => bound = sidecar);
        using var project = TestBakeProject.Create("sidecar-check", processors: [processor]);
        project.WriteAssetBytes("data/archer.unit", [1]);
        string sidecarPath = project.FullPath("Assets/data/archer.unit.toml");

        var checkDiagnostics = new BakeDiagnostics();
        _ = Run(project, checkDiagnostics, allowSourceWrites: false);

        Assert.Empty(checkDiagnostics.GetSorted());
        Assert.False(File.Exists(sidecarPath));
        Assert.DoesNotContain(
            checkDiagnostics.GetSorted(),
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.Equal(1, processor.CreateDefaultSidecarCount);
        Assert.Equal(1, processor.ProcessCount);
        Assert.NotNull(bound);
        Assert.Equal(7, bound!.Value);
        Assert.Equal(TestSidecarMode.Slow, bound.Mode);

        var bakeDiagnostics = new BakeDiagnostics();
        _ = Run(project, bakeDiagnostics, allowSourceWrites: true);

        Assert.True(File.Exists(sidecarPath));
        Assert.Single(
            bakeDiagnostics.GetSorted(),
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.Equal(2, processor.CreateDefaultSidecarCount);
        Assert.Equal(2, processor.ProcessCount);
    }

    [Fact]
    public void CheckModeStillValidatesTheDefaultSidecarSerialization()
    {
        using var project = TestBakeProject.Create(
            "sidecar-check-unsupported",
            processors: [new UnsupportedSidecarProcessor()]);
        project.WriteAssetBytes("data/thing.unsupported", [1]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(
            project,
            diagnostics,
            allowSourceWrites: false);

        Assert.Empty(outputs);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorSidecarCreateFailed, diagnostic.Code);
        Assert.False(File.Exists(project.FullPath("Assets/data/thing.unsupported.toml")));
    }

    [Fact]
    public void ValidateThroughBakeApplicationLeavesNoSidecar()
    {
        var processor = new TestSidecarProcessor(
            "test-unit",
            "unit",
            _ => new TestSidecar { Value = 7 });
        using var project = TestBakeProject.Create("sidecar-validate");
        project.WriteAssetBytes("data/archer.unit", [1]);
        BakeApplication application = BakeProjectFixture.CreateApplication(
            project.RootPath,
            [("assets.pck", "Assets")],
            processors: [processor]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = application.Run(["validate"], output, error, "test-assets");

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(project.FullPath("Assets/data/archer.unit.toml")));
        Assert.DoesNotContain("BK7009", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, processor.CreateDefaultSidecarCount);
        Assert.Equal(1, processor.ProcessCount);
    }

    [Fact]
    public void ExistingSidecarIsBoundWithoutCallingCreateDefaultSidecar()
    {
        TestSidecar? bound = null;
        var processor = new TestSidecarProcessor(
            "test-unit",
            "unit",
            _ => new TestSidecar { Value = 7 },
            (_, sidecar) => bound = sidecar);
        using var project = TestBakeProject.Create("sidecar-existing", processors: [processor]);
        project.WriteAssetBytes("data/archer.unit", [1]);
        project.WriteAsset("data/archer.unit.toml", "value = 3\nmode = \"fast\"\n");

        var diagnostics = new BakeDiagnostics();
        _ = Run(project, diagnostics);

        Assert.Empty(diagnostics.GetSorted());
        Assert.Equal(0, processor.CreateDefaultSidecarCount);
        Assert.Equal(1, processor.ProcessCount);
        Assert.NotNull(bound);
        Assert.Equal(3, bound!.Value);
        Assert.Equal(TestSidecarMode.Fast, bound.Mode);
        Assert.Null(bound.Label);
    }

    [Fact]
    public void AnExistingSidecarIsNeverOverwritten()
    {
        var processor = new TestSidecarProcessor(
            "test-unit",
            "unit",
            _ => new TestSidecar { Value = 7 });
        using var project = TestBakeProject.Create("sidecar-no-overwrite", processors: [processor]);
        project.WriteAssetBytes("data/archer.unit", [1]);
        project.WriteAsset("data/archer.unit.toml", "value = 3\n");
        string sidecarPath = project.FullPath("Assets/data/archer.unit.toml");
        byte[] before = File.ReadAllBytes(sidecarPath);

        _ = Run(project, new BakeDiagnostics());

        Assert.Equal(before, File.ReadAllBytes(sidecarPath));
        Assert.Equal(0, processor.CreateDefaultSidecarCount);
    }

    [Fact]
    public void SecondRunReusesTheCreatedSidecarAndDoesNotCreateAgain()
    {
        var processor = new TestSidecarProcessor(
            "test-unit",
            "unit",
            _ => new TestSidecar { Value = 7 });
        using var project = TestBakeProject.Create("sidecar-created-once", processors: [processor]);
        project.WriteAssetBytes("data/archer.unit", [1]);

        _ = Run(project, new BakeDiagnostics());
        _ = Run(project, new BakeDiagnostics());

        Assert.Equal(1, processor.CreateDefaultSidecarCount);
        Assert.Equal(2, processor.ProcessCount);
    }

    [Theory]
    [InlineData("bogus = 1\n", BakeDiagnosticCodes.BinderUnknownKey)]
    [InlineData("value = \"x\"\n", BakeDiagnosticCodes.BinderTypeMismatch)]
    [InlineData("mode = \"turbo\"\n", BakeDiagnosticCodes.BinderEnumValue)]
    [InlineData("value =\n", BakeDiagnosticCodes.TomlSyntax)]
    public void SidecarBindingErrorsUseTheBk2Range(string toml, string expectedCode)
    {
        var processor = new TestSidecarProcessor(
            "test-unit",
            "unit",
            _ => new TestSidecar { Value = 7 });
        using var project = TestBakeProject.Create("sidecar-binding-error", processors: [processor]);
        project.WriteAssetBytes("data/archer.unit", [1]);
        project.WriteAsset("data/archer.unit.toml", toml);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Empty(outputs);
        Assert.Equal(0, processor.ProcessCount);
        Assert.Equal(
            expectedCode,
            Assert.Single(diagnostics.GetSorted()).Code);
    }

    [Fact]
    public void SidecarBork2dTableIsAnUnknownKey()
    {
        var processor = new TestSidecarProcessor(
            "test-unit",
            "unit",
            _ => new TestSidecar { Value = 7 });
        using var project = TestBakeProject.Create("sidecar-bork2d", processors: [processor]);
        project.WriteAssetBytes("data/archer.unit", [1]);
        project.WriteAsset(
            "data/archer.unit.toml",
            """
            [bork2d]
            type = "Bork2d.Tests.Bake.Baking.StatsRoot"
            """);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Empty(outputs);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.BinderUnknownKey, diagnostic.Code);
        Assert.Equal("bork2d", diagnostic.MemberPath);
        Assert.Equal("Assets/data/archer.unit.toml", diagnostic.FilePath);
    }

    [Fact]
    public void SidecarReferencesAreUnsupported()
    {
        using var project = TestBakeProject.Create(
            "sidecar-reference",
            processors: [new ReferenceSidecarProcessor()]);
        project.WriteAssetBytes("data/thing.ref", [1]);
        project.WriteAsset("data/thing.ref.toml", "texture = \"art/player\"\n");

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Empty(outputs);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SidecarReferenceUnsupported, diagnostic.Code);
        Assert.Equal("texture", diagnostic.MemberPath);
    }

    [Fact]
    public void ASidecarShapeDeclaringAReferenceIsUnsupportedEvenWhenOmitted()
    {
        using var project = TestBakeProject.Create(
            "sidecar-reference-omitted",
            processors: [new ReferenceSidecarProcessor()]);
        project.WriteAssetBytes("data/thing.ref", [1]);
        project.WriteAsset("data/thing.ref.toml", string.Empty);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Empty(outputs);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.SidecarReferenceUnsupported, diagnostic.Code);
        Assert.Equal("texture", diagnostic.MemberPath);
    }

    [Fact]
    public void CreateDefaultSidecarReturningNullIsBK7006()
    {
        var processor = new TestSidecarProcessor(
            "test-unit",
            "unit",
            _ => null!);
        using var project = TestBakeProject.Create("sidecar-null", processors: [processor]);
        project.WriteAssetBytes("data/archer.unit", [1]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Empty(outputs);
        Assert.Equal(0, processor.ProcessCount);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorSidecarCreateFailed, diagnostic.Code);
        Assert.False(File.Exists(project.FullPath("Assets/data/archer.unit.toml")));
    }

    [Fact]
    public void CreateDefaultSidecarThrowingIsBK7006()
    {
        var processor = new TestSidecarProcessor(
            "test-unit",
            "unit",
            _ => throw new InvalidOperationException("cannot default"));
        using var project = TestBakeProject.Create("sidecar-throws", processors: [processor]);
        project.WriteAssetBytes("data/archer.unit", [1]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Empty(outputs);
        Assert.Equal(0, processor.ProcessCount);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorSidecarCreateFailed, diagnostic.Code);
        Assert.Contains("test-unit", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedDefaultSidecarMemberIsBK7006()
    {
        using var project = TestBakeProject.Create(
            "sidecar-unsupported",
            processors: [new UnsupportedSidecarProcessor()]);
        project.WriteAssetBytes("data/thing.unsupported", [1]);

        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeProcessorOutput> outputs = Run(project, diagnostics);

        Assert.Empty(outputs);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorSidecarCreateFailed, diagnostic.Code);
        Assert.Contains("test-unsupported-sidecar", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(project.FullPath("Assets/data/thing.unsupported.toml")));
    }

    private static IReadOnlyList<BakeProcessorOutput> Run(
        TestBakeProject project,
        BakeDiagnostics diagnostics,
        bool allowSourceWrites = true)
    {
        BakeDiscoveredSources discovered = BakeSourceDiscovery.Discover(project.Project, diagnostics);
        return BakeProcessorRunner.Run(
            project.Project,
            discovered.ProcessorSources,
            diagnostics,
            allowSourceWrites);
    }
}
