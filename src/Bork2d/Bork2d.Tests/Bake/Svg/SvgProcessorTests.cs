using System.Numerics;
using Bork2d.Bake;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Incremental;
using Bork2d.Bake.Processors;
using Bork2d.Bake.Svg;
using Bork2d.Bake.Textures;
using Bork2d.Engine.Assets;
using Bork2d.Tests.Assets.Service;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// The built-in SVG processor through the real builder and pack pipeline: the sprite, texture
/// descriptor, and KTX2 payload paths, default-sidecar creation and overrides, compression, invalid
/// options and content diagnostics, check and incremental bakes, and the default registry match.
/// </summary>
public sealed class SvgProcessorTests
{
    private const string SpriteType = "Bork2d.Engine.Assets.SpriteAsset";

    private const string TextureType = "Bork2d.Engine.Assets.TextureDescriptor";

    private static readonly DateTime _pinnedTime = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string WideSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
          <rect x="20" y="20" width="160" height="60" />
        </svg>
        """;

    private const string DonutSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
          <path d="M 60 50 a 40 40 0 1 0 80 0 a 40 40 0 1 0 -80 0 Z M 80 50 a 20 20 0 1 1 40 0 a 20 20 0 1 1 -40 0 Z" />
        </svg>
        """;

    /// <summary>
    /// A 200x10 artboard: the default 512 resolution still yields a 512-pixel maximum dimension while
    /// keeping the default-resolution bakes small and fast.
    /// </summary>
    private const string ShortWideSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 10">
          <rect x="20" y="2" width="160" height="6" />
        </svg>
        """;

    [Fact]
    public void EndToEndPackEmitsTheSpriteTextureAndPayload()
    {
        using var project = SvgTestProject.Create();
        project.WriteText("art/wordmark.svg", WideSvg);
        project.WriteText("art/wordmark.svg.toml", Sidecar());

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.AssetCount);
        Assert.Equal(1, result.TextureCount);
        Assert.Equal(2, result.ReferenceCount);
        Assert.Equal(
            new[] { "art/wordmark", "art/wordmark-sdf", "art/wordmark-sdf-ktx2" },
            result.Entries.Select(entry => entry.Path));

        BakedAssetEntry spriteEntry = Assert.Single(
            result.Entries,
            entry => entry.Path == "art/wordmark");
        Assert.Equal(BakedAssetEntryKind.ProcessorAsset, spriteEntry.Kind);
        Assert.Equal(SpriteType, spriteEntry.TypeName);
        Assert.False(spriteEntry.IsRaw);

        BakedAssetEntry descriptorEntry = Assert.Single(
            result.Entries,
            entry => entry.Path == "art/wordmark-sdf");
        Assert.Equal(BakedAssetEntryKind.ProcessorAsset, descriptorEntry.Kind);
        Assert.Equal(TextureType, descriptorEntry.TypeName);
        Assert.False(descriptorEntry.IsRaw);

        BakedAssetEntry payloadEntry = Assert.Single(
            result.Entries,
            entry => entry.Path == "art/wordmark-sdf-ktx2");
        Assert.Equal(BakedAssetEntryKind.ProcessorData, payloadEntry.Kind);
        Assert.Null(payloadEntry.TypeName);
        Assert.True(payloadEntry.IsRaw);
    }

    [Fact]
    public async Task BakedSpriteHasFullUvsACenterPivotAndTheDocumentedLayout()
    {
        using var project = SvgTestProject.Create();
        project.WriteText("art/wordmark.svg", WideSvg);
        project.WriteText("art/wordmark.svg.toml", Sidecar());

        BakeResult result = project.Bake();
        Assert.Equal(BakeStatus.Success, result.Status);

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        SpriteAsset sprite = await host.Service.LoadAsset<SpriteAsset>("art/wordmark");

        Assert.Equal("art/wordmark-sdf", sprite.Texture.Path);
        Assert.Equal(
            new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            },
            sprite.Uvs);
        Assert.Equal(new Vector2(0.5f, 0.5f), sprite.Pivot);
        Assert.Equal(128.0 / 54.0, sprite.Scale.X, 5);
        Assert.Equal(74.0 / 54.0, sprite.Scale.Y, 5);
        Assert.Equal(SpriteTopology.Quad, sprite.Topology);
        Assert.Null(sprite.Indices);
    }

    [Fact]
    public async Task BakedTextureHasTheKtx2PayloadAndDescriptorMetadata()
    {
        using var project = SvgTestProject.Create();
        project.WriteText("art/wordmark.svg", WideSvg);
        project.WriteText("art/wordmark.svg.toml", Sidecar());

        BakeResult result = project.Bake();
        Assert.Equal(BakeStatus.Success, result.Status);

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("art/wordmark-sdf");
        Assert.Equal("art/wordmark-sdf-ktx2", descriptor.Payload.Path);
        Assert.Equal(128u, descriptor.Width);
        Assert.Equal(74u, descriptor.Height);
        Assert.Equal(TextureFormat.Rgb8, descriptor.Format);
        Assert.Equal((uint)TextureMipChain.LevelCount(128, 74), descriptor.MipLevels);
        Assert.Equal(TextureFilter.Linear, descriptor.Filter);
        Assert.Equal(TextureWrap.Clamp, descriptor.Wrap);

        byte[] payload = await host.Service.LoadData("art/wordmark-sdf-ktx2");
        Assert.Equal(Ktx2TestDocument.Identifier, payload[..12]);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(payload);
        Assert.Equal(37u, document.VkFormat);
        Assert.Equal(descriptor.Width, document.Width);
        Assert.Equal(descriptor.Height, document.Height);
        Assert.Equal(descriptor.MipLevels, document.LevelCount);
        for (int level = 0; level < document.LevelCount; level++)
        {
            int width = TextureMipChain.LevelWidth(128, level);
            int height = TextureMipChain.LevelHeight(74, level);
            Assert.Equal(width * height * 4, document.GetLevelBytes(level).Length);
        }
    }

    [Fact]
    public async Task MissingSidecarIsCreatedWithTheExplicitDefaults()
    {
        using var project = SvgTestProject.Create();
        project.WriteText("art/wordmark.svg", ShortWideSvg);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        BakeDiagnostic info = Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.Equal(BakeSeverity.Info, info.Severity);
        Assert.Equal(SvgTestProject.DisplayPath("art/wordmark.svg.toml"), info.FilePath);

        string sidecarPath = project.FullPath("art/wordmark.svg.toml");
        Assert.True(File.Exists(sidecarPath));
        Assert.Equal(
            "resolution = 512\nrange = 8\npadding = 2\ncompression = \"none\"\n",
            File.ReadAllText(sidecarPath));

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("art/wordmark-sdf");
        Assert.Equal(512u, descriptor.Width);
        Assert.Equal(45u, descriptor.Height);
        Assert.Equal(TextureFormat.Rgb8, descriptor.Format);
        SpriteAsset sprite = await host.Service.LoadAsset<SpriteAsset>("art/wordmark");
        Assert.Equal(512.0 / 24.6, sprite.Scale.X, 4);
        Assert.Equal(45.0 / 24.6, sprite.Scale.Y, 4);
    }

    [Fact]
    public async Task PartialSidecarOverridesOnlyThePresentKey()
    {
        using var project = SvgTestProject.Create();
        project.WriteText("art/wordmark.svg", WideSvg);
        project.WriteText("art/wordmark.svg.toml", "resolution = 64\n");

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("art/wordmark-sdf");
        Assert.Equal(64u, descriptor.Width);
        Assert.Equal(42u, descriptor.Height);
        Assert.Equal(TextureFormat.Rgb8, descriptor.Format);
        SpriteAsset sprite = await host.Service.LoadAsset<SpriteAsset>("art/wordmark");
        Assert.Equal(64.0 / 22.0, sprite.Scale.X, 5);
        Assert.Equal(42.0 / 22.0, sprite.Scale.Y, 5);
    }

    [Fact]
    public async Task EmptySidecarUsesAllDefaults()
    {
        using var project = SvgTestProject.Create();
        project.WriteText("art/wordmark.svg", ShortWideSvg);
        project.WriteText("art/wordmark.svg.toml", string.Empty);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(string.Empty, File.ReadAllText(project.FullPath("art/wordmark.svg.toml")));

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("art/wordmark-sdf");
        Assert.Equal(512u, descriptor.Width);
        Assert.Equal(45u, descriptor.Height);
        Assert.Equal(TextureFormat.Rgb8, descriptor.Format);
    }

    [Fact]
    public async Task Bc7CompressionSelectsTheBc7ContainerAndKeepsTheLayout()
    {
        using var project = SvgTestProject.Create();
        project.WriteText("art/wordmark.svg", DonutSvg);
        project.WriteText("art/wordmark.svg.toml", Sidecar(compression: "bc7"));

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        SpriteAsset sprite = await host.Service.LoadAsset<SpriteAsset>("art/wordmark");
        Assert.Equal(128.0 / 54.0, sprite.Scale.X, 5);
        Assert.Equal(74.0 / 54.0, sprite.Scale.Y, 5);

        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("art/wordmark-sdf");
        Assert.Equal(TextureFormat.CompRgba8, descriptor.Format);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(
            await host.Service.LoadData("art/wordmark-sdf-ktx2"));
        Assert.Equal(145u, document.VkFormat);
        Assert.Equal(descriptor.MipLevels, document.LevelCount);
        for (int level = 0; level < document.LevelCount; level++)
        {
            int width = TextureMipChain.LevelWidth(128, level);
            int height = TextureMipChain.LevelHeight(74, level);
            Assert.Equal(((width + 3) / 4) * ((height + 3) / 4) * 16, document.GetLevelBytes(level).Length);
        }
    }

    [Theory]
    [InlineData("resolution = 0\n")]
    [InlineData("range = 0\n")]
    [InlineData("padding = -1\n")]
    [InlineData("resolution = 8\nrange = 8\npadding = 2\n")]
    [InlineData("resolution = 8193\n")]
    public void InvalidNumericOptionsReportBK9204WithoutOutputs(string sidecarToml)
    {
        using var project = SvgTestProject.Create();
        project.WriteText("art/wordmark.svg", WideSvg);
        project.WriteText("art/wordmark.svg.toml", sidecarToml);

        BakeResult result = project.Bake();

        AssertFailedBake(project, result, SvgDiagnosticCodes.SdfOptionsInvalid);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorFailed);
    }

    [Fact]
    public void InvalidCompressionEnumIsBK2006AndNotBK9204()
    {
        using var project = SvgTestProject.Create();
        project.WriteText("art/wordmark.svg", WideSvg);
        project.WriteText("art/wordmark.svg.toml", "compression = \"bc1\"\n");

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.False(result.PackWritten);
        Assert.False(File.Exists(project.PackPath));
        Assert.Empty(result.Entries);
        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.BinderEnumValue, diagnostic.Code);
        Assert.Equal(BakeSeverity.Error, diagnostic.Severity);
        Assert.Equal(SvgTestProject.DisplayPath("art/wordmark.svg.toml"), diagnostic.FilePath);
        Assert.DoesNotContain(
            result.Diagnostics,
            candidate => candidate.Code == SvgDiagnosticCodes.SdfOptionsInvalid);
    }

    [Fact]
    public void MalformedXmlIsBK9201WithoutOutputs()
    {
        using var project = SvgTestProject.Create();
        project.WriteText("art/wordmark.svg", "<svg><rect></svg>");
        project.WriteText("art/wordmark.svg.toml", Sidecar(64));

        BakeResult result = project.Bake();

        AssertFailedBake(project, result, SvgDiagnosticCodes.SourceXmlInvalid);
    }

    [Fact]
    public void MalformedPathIsBK9202WithTheSourceLocation()
    {
        using var project = SvgTestProject.Create();
        project.WriteText(
            "art/wordmark.svg",
            """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
              <path d="M 0 0 L" />
            </svg>
            """);
        project.WriteText("art/wordmark.svg.toml", Sidecar(64));

        BakeResult result = project.Bake();

        AssertFailedBake(project, result, SvgDiagnosticCodes.ContentInvalid);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == SvgDiagnosticCodes.ContentInvalid);
        Assert.Contains("offset", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("line 2", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("column ", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidUtf8SourceIsBK9201WithoutOutputs()
    {
        using var project = SvgTestProject.Create();
        project.WriteBytes("art/wordmark.svg", [0x3C, 0x73, 0x76, 0x67, 0x3E, 0xFF, 0xFE]);
        project.WriteText("art/wordmark.svg.toml", Sidecar(64));

        BakeResult result = project.Bake();

        AssertFailedBake(project, result, SvgDiagnosticCodes.SourceXmlInvalid);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains("not valid UTF-8", StringComparison.Ordinal));
    }

    [Fact]
    public void UnsupportedElementIsAnInfoAndTheValidShapeStillBakes()
    {
        using var project = SvgTestProject.Create();
        project.WriteText(
            "art/wordmark.svg",
            """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
              <text x="10" y="10">ignored</text>
              <rect x="20" y="20" width="160" height="60" />
            </svg>
            """);
        project.WriteText("art/wordmark.svg.toml", Sidecar());

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        BakeDiagnostic info = Assert.Single(result.Diagnostics);
        Assert.Equal(SvgDiagnosticCodes.FeatureIgnored, info.Code);
        Assert.Equal(BakeSeverity.Info, info.Severity);
        Assert.Equal(SvgTestProject.DisplayPath("art/wordmark.svg"), info.FilePath);
        Assert.Equal(
            new[] { "art/wordmark", "art/wordmark-sdf", "art/wordmark-sdf-ktx2" },
            result.Entries.Select(entry => entry.Path));
    }

    [Fact]
    public void NoDrawableGeometryIsBK9205WithoutOutputs()
    {
        using var project = SvgTestProject.Create();
        project.WriteText(
            "art/wordmark.svg",
            """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
              <rect x="20" y="20" width="160" height="60" fill="none" />
            </svg>
            """);
        project.WriteText("art/wordmark.svg.toml", Sidecar(64));

        BakeResult result = project.Bake();

        AssertFailedBake(project, result, SvgDiagnosticCodes.NoGeometry);
    }

    [Fact]
    public void ShapeCoveringTheWholeTextureIsBK9206WithoutOutputs()
    {
        using var project = SvgTestProject.Create();
        project.WriteText(
            "art/wordmark.svg",
            """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
              <rect x="-100" y="-100" width="400" height="300" />
            </svg>
            """);
        project.WriteText("art/wordmark.svg.toml", Sidecar(64));

        BakeResult result = project.Bake();

        AssertFailedBake(project, result, SvgDiagnosticCodes.SdfBakeFailed);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == SvgDiagnosticCodes.SdfBakeFailed);
        Assert.Contains("inside at every texel", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckBakeCreatesNoSidecarAndWritesNoPack()
    {
        using var project = SvgTestProject.Create();
        project.WriteText("art/wordmark.svg", ShortWideSvg);

        BakeResult result = project.Bake(check: true);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.False(result.PackWritten);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.False(File.Exists(project.FullPath("art/wordmark.svg.toml")));
        Assert.False(File.Exists(project.PackPath));
        Assert.Equal(
            new[] { "art/wordmark", "art/wordmark-sdf", "art/wordmark-sdf-ktx2" },
            result.Entries.Select(entry => entry.Path));
    }

    [Fact]
    public void SecondIncrementalBakeIsUpToDateAndTracksTheCreatedSidecar()
    {
        using var project = SvgTestProject.Create();
        project.WriteText("art/wordmark.svg", ShortWideSvg);
        string manifestPath = BakeManifestPaths.ManifestPath(project.RootPath);

        BakeResult first = project.BakeIncremental();

        Assert.Equal(BakeStatus.Success, first.Status);
        Assert.True(File.Exists(project.FullPath("art/wordmark.svg.toml")));
        BakeIncrementalManifest? manifest = BakeManifestStore.TryRead(manifestPath);
        Assert.NotNull(manifest);
        Assert.Contains(
            manifest!.Inputs,
            input => input.Path == SvgTestProject.DisplayPath("art/wordmark.svg.toml"));
        Assert.Equal(BakeConfigurationHash.Compute(project.Project), manifest.ConfigurationHash);
        Assert.Equal("svg-1/seasonmsdf-0.2.0/sdfmips-1", SvgToolInfo.ConfigurationMarker);

        byte[] packBytes = File.ReadAllBytes(project.PackPath);
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        File.SetLastWriteTimeUtc(project.PackPath, _pinnedTime);
        File.SetLastWriteTimeUtc(manifestPath, _pinnedTime);

        BakeResult second = project.BakeIncremental();

        Assert.Equal(BakeStatus.UpToDate, second.Status);
        Assert.False(second.PackWritten);
        Assert.Contains(
            second.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.IncrementalSkipped);
        Assert.Equal(packBytes, File.ReadAllBytes(project.PackPath));
        Assert.Equal(manifestBytes, File.ReadAllBytes(manifestPath));
        Assert.Equal(_pinnedTime, File.GetLastWriteTimeUtc(project.PackPath));
        Assert.Equal(_pinnedTime, File.GetLastWriteTimeUtc(manifestPath));
    }

    [Fact]
    public void OutputPathsAreBaseDerivedAndReferencesValidate()
    {
        using var project = SvgTestProject.Create();
        project.WriteText("art/icons/logo.svg", WideSvg);
        project.WriteText("art/icons/logo.svg.toml", Sidecar(64, range: 4, padding: 1));

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Equal(2, result.ReferenceCount);
        Assert.Equal(
            new[]
            {
                "art/icons/logo",
                "art/icons/logo-sdf",
                "art/icons/logo-sdf-ktx2",
            },
            result.Entries.Select(entry => entry.Path));
    }

    [Fact]
    public void DefaultRegistryMatchesSvgCaseInsensitivelyWithARequiredSidecar()
    {
        using var project = SvgTestProject.Create();

        Assert.True(
            project.Project.ProcessorRegistry.TryMatch(
                "art/Wordmark.SVG",
                out BakeProcessorMatch match));
        Assert.Equal("svg", match.Processor.Name);
        Assert.Equal(["svg"], match.Processor.Extensions);
        Assert.Equal(SidecarPolicy.Required, match.Processor.Policy);
        Assert.True(project.Project.ProcessorRegistry.TryMatch("art/wordmark.svg", out _));
    }

    private static string Sidecar(
        int resolution = 128,
        int range = 8,
        int padding = 2,
        string compression = "none")
        => $"resolution = {resolution}\nrange = {range}\npadding = {padding}\n"
            + $"compression = \"{compression}\"\n";

    private static void AssertFailedBake(
        SvgTestProject project,
        BakeResult result,
        string expectedCode)
    {
        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.False(result.PackWritten);
        Assert.False(File.Exists(project.PackPath));
        Assert.Empty(result.Entries);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == expectedCode
                && diagnostic.Severity == BakeSeverity.Error);
    }
}
