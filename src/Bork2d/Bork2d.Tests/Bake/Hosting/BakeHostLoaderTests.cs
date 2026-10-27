using System.Reflection;
using System.Runtime.Loader;
using Bork2d.Api.Assets;
using Bork2d.Assets.Pack;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Project;
using Bork2d.Bake.Sources;
using Xunit;

namespace Bork2d.Tests.Bake.Hosting;

/// <summary>
/// The non-collectible host load, the fixed assembly name, shared type identity, the load-time shape
/// validation, and the missing/stale host diagnostics.
/// </summary>
[Collection(BakeHostFixtureCollection.Name)]
public sealed class BakeHostLoaderTests
{
    private readonly TempBakeFixture _fixture;

    /// <summary>Initializes a new instance of the <see cref="BakeHostLoaderTests"/> class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public BakeHostLoaderTests(TempBakeFixture fixture) => _fixture = fixture;

    [Fact]
    public void FixtureHostLoadsInTheFixedNonCollectibleContext()
    {
        BakeHostHandle handle = _fixture.Handle;

        Assert.NotNull(handle.LoadContext);
        Assert.Equal(BakeHostIdentity.AssemblyName, handle.LoadContext.Name);
        Assert.False(handle.LoadContext.IsCollectible);
        Assert.Equal(BakeHostIdentity.AssemblyName, handle.HostAssembly.GetName().Name);
        Assert.Equal(
            BakeHostIdentity.AssemblyName,
            handle.Manifest.GetType().Assembly.GetName().Name);
    }

    [Fact]
    public void ManifestImplementsTheSharedContractAndSharesEngineIdentities()
    {
        BakeHostHandle handle = _fixture.Handle;

        Assert.True(typeof(IBakeManifest).IsAssignableFrom(handle.Manifest.GetType()));

        Assembly assets = handle.LoadContext.LoadFromAssemblyName(new AssemblyName("Bork2d.Assets"));
        Assembly api = handle.LoadContext.LoadFromAssemblyName(new AssemblyName("Bork2d.Api"));

        Assert.Same(typeof(PackBuilder).Assembly, assets);
        Assert.Same(typeof(IAssets).Assembly, api);
        Assert.Same(typeof(PackBuilder).Assembly, AssemblyLoadContext.Default.Assemblies
            .Single(assembly => assembly.GetName().Name == "Bork2d.Assets"));
    }

    [Fact]
    public void HostLoadShapeValidationPassesForTheFixtureTypes()
    {
        var diagnostics = new BakeDiagnostics();

        _fixture.Manifest.ValidateShapes(diagnostics);

        Assert.Empty(diagnostics.GetSorted());
    }

    [Fact]
    public void MissingHostIsAHostAssemblyDiagnostic()
    {
        var diagnostics = new BakeDiagnostics();

        BakeHostHandle? handle = BakeHostLoader.TryLoad(
            Path.Combine(Path.GetTempPath(), $"missing-host-{Guid.NewGuid():N}.dll"),
            diagnostics);

        Assert.Null(handle);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.HostAssemblyMissing, diagnostic.Code);
    }

    [Fact]
    public void StaleHostWithMissingMappingsIsReported()
    {
        string directory = CreateDescriptorProject(
            """
            schema = 1

            [assets]
            root = "Assets"
            """,
            "data/config.toml",
            """
            name = "campaign"
            health = 1
            must = "m"

            [bork2d]
            type = "BakeFixture.Common.GameConfig"
            """);
        var diagnostics = new BakeDiagnostics();
        ProjectDescriptor descriptor = ProjectDescriptorParser.Parse(
            Path.Combine(directory, TempBakeFixture.DescriptorFileName),
            diagnostics)!;
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        bool valid = BakeHostManifestValidator.Validate(
            descriptor,
            sources,
            _fixture.Manifest,
            diagnostics);

        Assert.False(valid);
        BakeDiagnostic[] stale = [.. diagnostics
            .GetSorted()
            .Where(diagnostic => diagnostic.Code == BakeDiagnosticCodes.HostStale)];
        Assert.NotEmpty(stale);
        Assert.Contains(
            stale,
            diagnostic => diagnostic.Message.Contains("StatsRoot", StringComparison.Ordinal));
        Assert.Contains(
            stale,
            diagnostic => diagnostic.Message.Contains("SpriteAsset", StringComparison.Ordinal));
    }

    [Fact]
    public void StaleHostWithAnUndeclaredRegistrationIsReported()
    {
        string directory = CreateDescriptorProject(
            """
            schema = 1

            [assets]
            root = "Assets"
            """,
            "data/config.toml",
            """
            value = 1

            [bork2d]
            type = "BakeFixture.Common.MissingFromHost"
            """);
        var diagnostics = new BakeDiagnostics();
        ProjectDescriptor descriptor = ProjectDescriptorParser.Parse(
            Path.Combine(directory, TempBakeFixture.DescriptorFileName),
            diagnostics)!;
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);

        bool valid = BakeHostManifestValidator.Validate(
            descriptor,
            sources,
            _fixture.Manifest,
            diagnostics);

        Assert.False(valid);
        Assert.Contains(
            diagnostics.GetSorted(),
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.HostStale
                && diagnostic.Message.Contains("MissingFromHost", StringComparison.Ordinal));
    }

    [Fact]
    public void MatchingDescriptorValidatesAgainstTheHostManifest()
    {
        var diagnostics = new BakeDiagnostics();
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(
            _fixture.DescriptorDescriptor,
            diagnostics);

        bool valid = BakeHostManifestValidator.Validate(
            _fixture.DescriptorDescriptor,
            sources,
            _fixture.Manifest,
            diagnostics);

        Assert.True(valid);
        Assert.Empty(diagnostics.GetSorted());
    }

    private static string CreateDescriptorProject(
        string descriptor,
        string relativeSourcePath,
        string source)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"bork2d-bake-stale-{Guid.NewGuid():N}");
        Directory.CreateDirectory(
            Path.GetDirectoryName(
                Path.Combine(directory, "Assets", relativeSourcePath))!);
        File.WriteAllText(Path.Combine(directory, TempBakeFixture.DescriptorFileName), descriptor);
        File.WriteAllText(Path.Combine(directory, "Assets", relativeSourcePath), source);
        return directory;
    }
}
