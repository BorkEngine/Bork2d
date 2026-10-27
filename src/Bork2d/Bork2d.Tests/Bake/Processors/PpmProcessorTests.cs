using Bork2d.Api.Assets;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Builder;
using Bork2d.Bake.Diagnostics;
using Bork2d.BakeFixture.Assets.Processors;
using Bork2d.Tests.Assets.Service;
using Bork2d.Tests.Bake.Baking;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// The PPM acceptance processor through the public builder and the real bake/load path: P6 parsing,
/// exact RGBA8 bytes, descriptor and payload paths, default-sidecar creation, sidecar binding and
/// override, invalid input, discovery, and path collisions.
/// </summary>
public sealed class PpmProcessorTests
{
    private const string TextureType = "Bork2d.Api.Assets.TextureDescriptor";

    private static readonly byte[] _defaultHeader = "P6\n2 2\n255\n"u8.ToArray();

    private static readonly byte[] _redGreenBlueWhite =
    [
        255, 0, 0,
        0, 255, 0,
        0, 0, 255,
        255, 255, 255,
    ];

    private static readonly byte[] _expectedRgba8 =
    [
        255, 0, 0, 255,
        0, 255, 0, 255,
        0, 0, 255, 255,
        255, 255, 255, 255,
    ];

    [Fact]
    public async Task ValidP6EmitsADescriptorAndTheExactRgbaPayload()
    {
        using var project = CreatePpmProject("ppm-valid");
        project.WriteAssetBytes("art/checker.ppm", Ppm(_defaultHeader, _redGreenBlueWhite));
        project.WriteAsset("art/checker.ppm.toml", "filter = \"nearest\"\nwrap = \"repeat\"\n");
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, result.AssetCount);
        Assert.Equal(1, result.TextureCount);
        Assert.Equal(
            new[] { "art/checker", "art/checker-raw" },
            result.Entries.Select(entry => entry.Path));

        BakedAssetEntry descriptorEntry = Assert.Single(
            result.Entries,
            entry => entry.Path == "art/checker");
        Assert.Equal(BakedAssetEntryKind.ProcessorAsset, descriptorEntry.Kind);
        Assert.Equal(TextureType, descriptorEntry.TypeName);
        BakedAssetEntry rawEntry = Assert.Single(
            result.Entries,
            entry => entry.Path == "art/checker-raw");
        Assert.Equal(BakedAssetEntryKind.ProcessorData, rawEntry.Kind);
        Assert.True(rawEntry.IsRaw);

        using var host = new AssetServiceHost(output.FullPath("assets.pck"));
        host.Start();

        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("art/checker");
        Assert.Equal("art/checker-raw", descriptor.Payload.Path);
        Assert.Equal(2u, descriptor.Width);
        Assert.Equal(2u, descriptor.Height);
        Assert.Equal("rgba8", descriptor.Format);
        Assert.Equal("nearest", descriptor.Filter);
        Assert.Equal("repeat", descriptor.Wrap);
        Assert.Equal(_expectedRgba8, await host.Service.LoadData("art/checker-raw"));
        Assert.True(host.Service.Exists("art/checker"));
        Assert.True(host.Service.Exists("art/checker-raw"));
    }

    [Fact]
    public void HeaderCommentsAndWhitespaceAreAccepted()
    {
        using var project = CreatePpmProject("ppm-comments");
        byte[] header = "P6\n# generated fixture\n2 2\n# maximum color value\n255\n"u8.ToArray();
        project.WriteAssetBytes("art/checker.ppm", Ppm(header, _redGreenBlueWhite));
        project.WriteAsset("art/checker.ppm.toml", "filter = \"linear\"\nwrap = \"clamp\"\n");
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(_expectedRgba8, ReadPayload(output, "art/checker-raw"));
    }

    [Fact]
    public async Task MissingSidecarIsCreatedFromTheConfiguredDefaults()
    {
        using var project = TestBakeProject.Create(
            "ppm-default-sidecar",
            processors: [new PpmProcessor(TextureFilter.Nearest, TextureWrap.Repeat)]);
        project.WriteAssetBytes("art/checker.ppm", Ppm(_defaultHeader, _redGreenBlueWhite));
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output);

        Assert.Equal(BakeStatus.Success, result.Status);
        BakeDiagnostic info = Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.Equal(BakeSeverity.Info, info.Severity);
        Assert.Equal("Assets/art/checker.ppm.toml", info.FilePath);
        Assert.Equal(
            "filter = \"nearest\"\nwrap = \"repeat\"\n",
            File.ReadAllText(project.FullPath("Assets/art/checker.ppm.toml")));

        using var host = new AssetServiceHost(output.FullPath("assets.pck"));
        host.Start();
        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("art/checker");
        Assert.Equal("nearest", descriptor.Filter);
        Assert.Equal("repeat", descriptor.Wrap);
    }

    [Fact]
    public async Task PartialSidecarOverridesOnlyTheSpecifiedSetting()
    {
        // The absent filter key keeps the configured nearest default; wrap overrides repeat with clamp.
        using var project = TestBakeProject.Create(
            "ppm-partial-sidecar",
            processors: [new PpmProcessor(TextureFilter.Nearest, TextureWrap.Repeat)]);
        project.WriteAssetBytes("art/checker.ppm", Ppm(_defaultHeader, _redGreenBlueWhite));
        project.WriteAsset("art/checker.ppm.toml", "wrap = \"clamp\"\n");
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output);

        Assert.Equal(BakeStatus.Success, result.Status);
        using var host = new AssetServiceHost(output.FullPath("assets.pck"));
        host.Start();
        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("art/checker");
        Assert.Equal("nearest", descriptor.Filter);
        Assert.Equal("clamp", descriptor.Wrap);
    }

    [Theory]
    [MemberData(nameof(InvalidPpmInputs))]
    public void InvalidInputIsBK7xxxAndProducesNoOutputs(string caseName, byte[] source)
    {
        Assert.False(string.IsNullOrEmpty(caseName));
        using var project = CreatePpmProject("ppm-invalid");
        project.WriteAssetBytes("art/broken.ppm", source);
        project.WriteAsset("art/broken.ppm.toml", "filter = \"linear\"\nwrap = \"clamp\"\n");
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.False(result.PackWritten);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Severity == BakeSeverity.Error);
        Assert.StartsWith("BK7", diagnostic.Code, StringComparison.Ordinal);
        Assert.Equal("Assets/art/broken.ppm", diagnostic.FilePath);
        Assert.Contains("ppm", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("broken.ppm", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(output.FullPath("assets.pck")));
    }

    [Fact]
    public void UnknownSidecarKeyIsBK2002()
    {
        using var project = CreatePpmProject("ppm-sidecar-key");
        project.WriteAssetBytes("art/checker.ppm", Ppm(_defaultHeader, _redGreenBlueWhite));
        project.WriteAsset("art/checker.ppm.toml", "format = \"rgba8\"\n");
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.BinderUnknownKey, diagnostic.Code);
        Assert.Equal("Assets/art/checker.ppm.toml", diagnostic.FilePath);
        Assert.Contains("format", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnregisteredPpmFilesAreIgnoredWithoutTheProcessor()
    {
        using var project = TestBakeProject.Create("ppm-unregistered");
        project.WriteAssetBytes("art/checker.ppm", Ppm(_defaultHeader, _redGreenBlueWhite));
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Entries);
        Assert.Equal(0, result.TextureCount);
    }

    [Fact]
    public void CollisionWithTheBuiltInTextureIsBK3005()
    {
        using var project = CreatePpmProject("ppm-collision");
        project.WriteAssetBytes("art/checker.ppm", Ppm(_defaultHeader, _redGreenBlueWhite));
        project.WriteAsset("art/checker.ppm.toml", "filter = \"nearest\"\nwrap = \"repeat\"\n");
        project.WriteAssetBytes("art/checker.png", TextureImageFactory.RgbaPng());
        project.WriteAsset("art/checker.png.toml", BakeProjectFixture.TextureSidecarToml);
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.False(result.PackWritten);
        BakeDiagnostic[] collisions =
        [
            .. result.Diagnostics.Where(
                diagnostic => diagnostic.Code == BakeDiagnosticCodes.AssetPathCollision),
        ];
        Assert.Equal(2, collisions.Length);
        Assert.Contains(collisions, diagnostic => diagnostic.AssetPath == "art/checker");
        Assert.Contains(collisions, diagnostic => diagnostic.AssetPath == "art/checker-raw");
    }

    /// <summary>Gets the invalid PPM sources used by the theory.</summary>
    public static TheoryData<string, byte[]> InvalidPpmInputs => new()
    {
        { "wrong magic", Ppm("P5\n2 2\n255\n"u8.ToArray(), _redGreenBlueWhite) },
        { "wrong maximum", Ppm("P6\n2 2\n254\n"u8.ToArray(), _redGreenBlueWhite) },
        { "zero width", Ppm("P6\n0 2\n255\n"u8.ToArray(), _redGreenBlueWhite) },
        { "zero height", Ppm("P6\n2 0\n255\n"u8.ToArray(), _redGreenBlueWhite) },
        { "negative-looking width", Ppm("P6\n-2 2\n255\n"u8.ToArray(), _redGreenBlueWhite) },
        { "truncated payload", Ppm(_defaultHeader, _redGreenBlueWhite[..^1]) },
        { "trailing payload byte", Ppm(_defaultHeader, [.. _redGreenBlueWhite, 7]) },
        { "missing maximum separator", "P6\n2 2\n255"u8.ToArray() },
        { "comment after maximum", Ppm("P6\n2 2\n255 # no\n"u8.ToArray(), _redGreenBlueWhite) },
    };

    private static TestBakeProject CreatePpmProject(string name)
        => TestBakeProject.Create(
            name,
            processors: [new PpmProcessor(TextureFilter.Linear, TextureWrap.Clamp)]);

    private static BakeResult RunBake(TestBakeProject project, TempBakeDirectory output)
        => BakePipeline.Run(
            project.Project,
            new BakeOptions(output.RootPath, BakeMode.Debug, check: false, force: false),
            new BakeDiagnostics());

    private static byte[] ReadPayload(TempBakeDirectory output, string assetPath)
        => BakedPackAssertions
            .Payloads(output.FullPath("assets.pck"))
            .Zip(BakedPackAssertions.Shapes(output.FullPath("assets.pck")))
            .Single(pair => pair.Second.Path == assetPath)
            .First;

    private static byte[] Ppm(byte[] header, byte[] payload)
    {
        byte[] source = new byte[header.Length + payload.Length];
        header.CopyTo(source, 0);
        payload.CopyTo(source, header.Length);
        return source;
    }
}
