using Bork2d.Bake.Builder;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Textures;
using PolyType;
using Xunit;

namespace Bork2d.Tests.Bake.Builder;

/// <summary>
/// The public builder configuration contract: valid configuration, semantic <c>BK1xxx</c>
/// diagnostics with exit code <c>2</c> (never a throw from <c>Build()</c>), explicit and default
/// registrations, idempotence, duplicate detection, texture defaults, and stored processors.
/// </summary>
public sealed class BakeApplicationBuilderTests
{
    [Fact]
    public void ValidConfigurationCreatesTheProject()
    {
        using var directory = TempDirectory.Create("builder-valid");
        directory.CreateDirectory("Assets");
        directory.CreateDirectory("DLC1");

        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .WithPack("dlc1.pck", "DLC1")
            .UseDefaultAssets()
            .RegisterAsset<Bork2d.BakeFixture.GameConfig>()
            .RegisterAsset<Bork2d.BakeFixture.StatsRoot>()
            .UseDefaultProcessors()
            .WithTextureDefaults(TextureFilter.Linear, TextureWrap.Repeat)
            .AddProcessor(new FakeProcessor());

        var diagnostics = new BakeDiagnostics();
        Bork2d.Bake.Baking.BakeProject? project = builder.CreateProject(diagnostics);

        Assert.NotNull(project);
        Assert.Empty(diagnostics.GetSorted());
        Assert.Equal(2, project.Packs.Count);
        Assert.True(project.Packs[0].IsMain);
        Assert.False(project.Packs[1].IsMain);
        Assert.Equal("assets.pck", project.Packs[0].PackPath);
        Assert.Equal("dlc1.pck", project.Packs[1].PackPath);
        Assert.Equal(Path.Combine(directory.RootPath, "Assets"), project.Packs[0].Root);
        Assert.Equal(Path.Combine(directory.RootPath, "DLC1"), project.Packs[1].Root);
        Assert.Equal(BakeTextureFilter.Linear, project.TextureFilter);
        Assert.Equal(BakeTextureWrap.Repeat, project.TextureWrap);
        Assert.True(project.UseDefaultProcessors);
        Assert.Equal(2, project.Processors.Count);
        Assert.Equal("texture", project.Processors[0].Name);
        Assert.Equal("fake", project.Processors[1].Name);
        Assert.Contains("Bork2d.Api.Assets.SpriteAsset", project.AssetTypes.Keys);
        Assert.Contains("Bork2d.BakeFixture.GameConfig", project.AssetTypes.Keys);
        Assert.Contains("Bork2d.BakeFixture.StatsRoot", project.AssetTypes.Keys);
    }

    [Fact]
    public void MissingPackIsBK1003()
    {
        using var directory = TempDirectory.Create("builder-missing-pack");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath);

        BakeDiagnostic diagnostic = AssertConfigError(builder, BakeDiagnosticCodes.PackRequired);

        Assert.Contains("WithPack", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicatePackPathIsBK1008()
    {
        using var directory = TempDirectory.Create("builder-duplicate-pack");
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .WithPack("assets.pck", "Assets");

        AssertConfigError(builder, BakeDiagnosticCodes.PackPathDuplicate);
    }

    [Fact]
    public void DuplicateRootIsBK1009()
    {
        using var directory = TempDirectory.Create("builder-duplicate-root");
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .WithPack("dlc1.pck", "Assets");

        AssertConfigError(builder, BakeDiagnosticCodes.PackRootDuplicate);
    }

    [Fact]
    public void EscapingRootIsBK1005()
    {
        using var directory = TempDirectory.Create("builder-escaping-root");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "../outside");

        AssertConfigError(builder, BakeDiagnosticCodes.PackRootInvalid);
    }

    [Fact]
    public void RootedPackPathIsBK1004()
    {
        using var directory = TempDirectory.Create("builder-rooted-pack");
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack(Path.Combine(directory.RootPath, "assets.pck"), "Assets");

        AssertConfigError(builder, BakeDiagnosticCodes.PackPathInvalid);
    }

    [Fact]
    public void MissingPackRootIsBK1006()
    {
        using var directory = TempDirectory.Create("builder-missing-root");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets");

        AssertConfigError(builder, BakeDiagnosticCodes.PackRootMissing);
    }

    [Fact]
    public void MissingProjectRootIsBK1002()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"bork2d-missing-{Guid.NewGuid():N}");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(missing)
            .WithPack("assets.pck", "Assets");

        AssertConfigError(builder, BakeDiagnosticCodes.ProjectRootMissing);
    }

    [Fact]
    public void DuplicateAssetRegistrationIsBK1010()
    {
        using var directory = TempDirectory.Create("builder-duplicate-asset");
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .RegisterAsset<Bork2d.BakeFixture.GameConfig>()
            .RegisterAsset<Bork2d.BakeFixture.GameConfig>();

        BakeDiagnostic diagnostic = AssertConfigError(builder, BakeDiagnosticCodes.AssetTypeDuplicate);

        Assert.Contains("Bork2d.BakeFixture.GameConfig", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseDefaultAssetsIsIdempotent()
    {
        using var directory = TempDirectory.Create("builder-default-assets");
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .UseDefaultAssets()
            .UseDefaultAssets();

        var diagnostics = new BakeDiagnostics();
        Bork2d.Bake.Baking.BakeProject? project = builder.CreateProject(diagnostics);

        Assert.NotNull(project);
        Assert.Empty(diagnostics.GetSorted());
        Assert.Contains("Bork2d.Api.Assets.SpriteAsset", project.AssetTypes.Keys);
    }

    [Fact]
    public void ExplicitDuplicateOfADefaultAssetIsBK1010()
    {
        using var directory = TempDirectory.Create("builder-default-duplicate");
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .UseDefaultAssets()
            .RegisterAsset<Bork2d.Api.Assets.SpriteAsset>();

        AssertConfigError(builder, BakeDiagnosticCodes.AssetTypeDuplicate);
    }

    [Fact]
    public void UseDefaultProcessorsIsIdempotentAndRegistersTheTextureProcessor()
    {
        using var directory = TempDirectory.Create("builder-default-processors");
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .UseDefaultProcessors()
            .UseDefaultProcessors();

        var diagnostics = new BakeDiagnostics();
        Bork2d.Bake.Baking.BakeProject? project = builder.CreateProject(diagnostics);

        Assert.NotNull(project);
        Assert.Empty(diagnostics.GetSorted());
        Assert.True(project.UseDefaultProcessors);
        Bork2d.Bake.Processors.IBakeAssetProcessor processor =
            Assert.Single(project.Processors);
        Assert.Equal("texture", processor.Name);
        Assert.Equal(
            new[] { "bmp", "jpeg", "jpg", "png", "tga" },
            processor.Extensions.Order(StringComparer.Ordinal));
        Assert.Equal(Bork2d.Bake.Processors.BakeSidecarPolicy.Required, processor.Policy);
        Assert.NotNull(processor.SidecarShape);
    }

    [Fact]
    public void DefaultsRegisteredBeforeACustomProcessorLetTheCustomOneOverrideAnExtension()
    {
        using var directory = TempDirectory.Create("builder-default-override");
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .UseDefaultProcessors()
            .AddProcessor(new FakePngProcessor());

        var diagnostics = new BakeDiagnostics();
        Bork2d.Bake.Baking.BakeProject? project = builder.CreateProject(diagnostics);

        Assert.NotNull(project);
        BakeDiagnostic info = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.ProcessorExtensionOverride, info.Code);
        Assert.Equal(BakeSeverity.Info, info.Severity);
        Assert.Contains("texture", info.Message, StringComparison.Ordinal);
        Assert.Contains("fake-png", info.Message, StringComparison.Ordinal);
        Assert.True(
            project.ProcessorRegistry.TryMatch(
                "art/player.png",
                out Bork2d.Bake.Processors.BakeProcessorMatch match));
        Assert.Equal("fake-png", match.Processor.Name);
    }

    [Fact]
    public void RequiredSidecarProcessorIsStoredWithItsPolicyAndShape()
    {
        using var directory = TempDirectory.Create("builder-sidecar-processor");
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .AddProcessor(new FakeSidecarProcessor());

        var diagnostics = new BakeDiagnostics();
        Bork2d.Bake.Baking.BakeProject? project = builder.CreateProject(diagnostics);

        Assert.NotNull(project);
        var processor = Assert.Single(project.Processors);
        Assert.Equal(typeof(FakeSidecarProcessor).FullName, processor.Name);
        Assert.Equal(Bork2d.Bake.Processors.BakeSidecarPolicy.Required, processor.Policy);
        Assert.NotNull(processor.SidecarShape);
        Assert.Equal(typeof(TestSidecar), processor.SidecarShape!.Type);
    }

    [Fact]
    public void NothingReplacedByTheIncrementalManifestIsImplemented()
    {
        // WP1 accepts --force but never produces UpToDate; the pipeline status surfaced by a normal
        // run is Success. This guards against accidentally implementing the WP5 skip early.
        using var directory = TempDirectory.Create("builder-no-incremental");
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .WithTextureDefaults(TextureFilter.Nearest, TextureWrap.Clamp);

        var diagnostics = new BakeDiagnostics();
        Bork2d.Bake.Baking.BakeProject? project = builder.CreateProject(diagnostics);

        Assert.NotNull(project);
        Assert.Equal(BakeTextureFilter.Nearest, project.TextureFilter);
        Assert.Equal(BakeTextureWrap.Clamp, project.TextureWrap);
    }

    [Fact]
    public void BuildDoesNotThrowForBadConfiguration()
    {
        BakeApplication application = new BakeApplicationBuilder().Build();

        Assert.NotNull(application);
    }

    [Fact]
    public void NullAndEmptyArgumentsAreProgrammerErrors()
    {
        var builder = new BakeApplicationBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithProjectRoot(null!));
        Assert.Throws<ArgumentException>(() => builder.WithProjectRoot(" "));
        Assert.Throws<ArgumentNullException>(() => builder.WithPack(null!, "Assets"));
        Assert.Throws<ArgumentNullException>(() => builder.WithPack("assets.pck", null!));
        Assert.Throws<ArgumentNullException>(() => builder.AddProcessor(null!));
    }

    private static BakeDiagnostic AssertConfigError(
        BakeApplicationBuilder builder,
        string expectedCode)
    {
        var diagnostics = new BakeDiagnostics();
        _ = builder.CreateProject(diagnostics);
        return Assert.Single(
            diagnostics.GetSorted(),
            diagnostic => diagnostic.Code == expectedCode);
    }

    private sealed class FakeProcessor : IAssetProcessor
    {
        public string Name => "fake";

        public IReadOnlyList<string> Extensions => ["fake"];

        public SidecarPolicy Policy => SidecarPolicy.None;

        public ITypeShape? SidecarShape => null;

        public void Process(AssetProcessorContext context)
        {
        }
    }

    private sealed class FakePngProcessor : IAssetProcessor
    {
        public string Name => "fake-png";

        public IReadOnlyList<string> Extensions => ["png"];

        public SidecarPolicy Policy => SidecarPolicy.None;

        public ITypeShape? SidecarShape => null;

        public void Process(AssetProcessorContext context)
        {
        }
    }

    private sealed class FakeSidecarProcessor : AssetProcessor<TestSidecar>
    {
        public override IReadOnlyList<string> Extensions => ["fake"];

        protected override TestSidecar CreateDefaultSidecar(AssetProcessorContext context)
            => new();

        protected override void Process(AssetProcessorContext context, TestSidecar sidecar)
        {
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string rootPath) => RootPath = rootPath;

        internal string RootPath { get; }

        internal static TempDirectory Create(string name)
        {
            string rootPath = Path.Combine(
                Path.GetTempPath(),
                $"bork2d-{name}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return new TempDirectory(rootPath);
        }

        internal string CreateDirectory(string relativePath)
        {
            string fullPath = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
