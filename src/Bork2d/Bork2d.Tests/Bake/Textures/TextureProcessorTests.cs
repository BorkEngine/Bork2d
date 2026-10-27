using Bork2d.Api.Assets;
using Bork2d.Assets.Pack;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Builder;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Processors;
using Bork2d.Tests.Assets.Service;
using Bork2d.Tests.Bake.Baking;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Bake.Textures;

/// <summary>
/// The built-in texture processor through the real builder and pack pipeline: descriptor and raw
/// payload paths, default-sidecar creation from <c>WithTextureDefaults</c>, sidecar binding and
/// <c>BK2xxx</c> diagnostics, collision, discovery, and the pack summary counts.
/// </summary>
public sealed class TextureProcessorTests
{
    private const string TextureType = "Bork2d.Api.Assets.TextureDescriptor";

    [Fact]
    public async Task DescriptorAndRawPayloadAreEmittedAtTheCanonicalPaths()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("art/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("art/player.png.toml", BakeProjectFixture.TextureSidecarToml);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, result.AssetCount);
        Assert.Equal(1, result.TextureCount);
        Assert.Equal(1, result.ReferenceCount);
        Assert.Equal(
            new[] { "art/player", "art/player-raw" },
            result.Entries.Select(entry => entry.Path));
        BakedAssetEntry descriptorEntry = Assert.Single(
            result.Entries,
            entry => entry.Path == "art/player");
        Assert.Equal(BakedAssetEntryKind.ProcessorAsset, descriptorEntry.Kind);
        Assert.Equal(TextureType, descriptorEntry.TypeName);
        BakedAssetEntry rawEntry = Assert.Single(
            result.Entries,
            entry => entry.Path == "art/player-raw");
        Assert.Equal(BakedAssetEntryKind.ProcessorData, rawEntry.Kind);
        Assert.True(rawEntry.IsRaw);

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();

        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("art/player");
        Assert.Equal("art/player-raw", descriptor.Payload.Path);
        Assert.Equal(2u, descriptor.Width);
        Assert.Equal(2u, descriptor.Height);
        Assert.Equal(TextureFormat.Rgba8, descriptor.Format);
        Assert.Equal(TextureFilter.Nearest, descriptor.Filter);
        Assert.Equal(TextureWrap.Repeat, descriptor.Wrap);
        Assert.Equal(
            BakeProjectFixture.ExpectedTexturePayload,
            await host.Service.LoadData("art/player-raw"));
    }

    [Fact]
    public async Task MissingSidecarIsCreatedCanonicallyWithTheBuilderDefaults()
    {
        using var project = TextureTestProject.Create(TextureFilter.Nearest, TextureWrap.Repeat);
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == BakeSeverity.Error);
        BakeDiagnostic info = Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.Equal(BakeSeverity.Info, info.Severity);
        Assert.Equal("Assets/textures/player.png.toml", info.FilePath);

        string sidecarPath = project.FullPath("textures/player.png.toml");
        Assert.True(File.Exists(sidecarPath));
        Assert.Equal(
            "filter = \"nearest\"\nwrap = \"repeat\"\n",
            File.ReadAllText(sidecarPath));

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("textures/player");
        Assert.Equal(TextureFilter.Nearest, descriptor.Filter);
        Assert.Equal(TextureWrap.Repeat, descriptor.Wrap);
    }

    [Fact]
    public async Task ExistingSidecarIsNotOverwritten()
    {
        using var project = TextureTestProject.Create(TextureFilter.Nearest, TextureWrap.Repeat);
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        const string sidecar = "filter = \"linear\"\nwrap = \"clamp\"\n";
        project.WriteText("textures/player.png.toml", sidecar);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.Equal(sidecar, File.ReadAllText(project.FullPath("textures/player.png.toml")));

        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("textures/player");
        Assert.Equal(TextureFilter.Linear, descriptor.Filter);
        Assert.Equal(TextureWrap.Clamp, descriptor.Wrap);
    }

    [Fact]
    public async Task PartialSidecarOverridesOnlyTheSpecifiedSetting()
    {
        // The absent wrap key keeps the builder default (repeat); the present filter key overrides it.
        using var project = TextureTestProject.Create(TextureFilter.Nearest, TextureWrap.Repeat);
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/player.png.toml", "filter = \"linear\"\n");

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("textures/player");
        Assert.Equal(TextureFilter.Linear, descriptor.Filter);
        Assert.Equal(TextureWrap.Repeat, descriptor.Wrap);
    }

    [Fact]
    public async Task EmptySidecarUsesTheBuilderDefaults()
    {
        // An existing sidecar with no keys overrides nothing, so both values come from the builder.
        using var project = TextureTestProject.Create(TextureFilter.Nearest, TextureWrap.Repeat);
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/player.png.toml", string.Empty);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("textures/player");
        Assert.Equal(TextureFilter.Nearest, descriptor.Filter);
        Assert.Equal(TextureWrap.Repeat, descriptor.Wrap);
    }

    [Fact]
    public async Task SidecarSettingsApplyToTheMatchingSourceOnly()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteImage("textures/enemy.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/player.png.toml", "filter = \"nearest\"\nwrap = \"repeat\"\n");

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        TextureDescriptor player = await host.Service.LoadAsset<TextureDescriptor>("textures/player");
        TextureDescriptor enemy = await host.Service.LoadAsset<TextureDescriptor>("textures/enemy");
        Assert.Equal(TextureFilter.Nearest, player.Filter);
        Assert.Equal(TextureWrap.Repeat, player.Wrap);
        Assert.Equal(TextureFilter.Linear, enemy.Filter);
        Assert.Equal(TextureWrap.Clamp, enemy.Wrap);
    }

    [Fact]
    public async Task SidecarAssociationIgnoresStemAndExtensionCasing()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/Player.png", TextureImageFactory.RgbaPng());
        project.WriteText(
            "textures/PLAYER.PNG.toml",
            "filter = \"nearest\"\nwrap = \"repeat\"\n");

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Empty(result.Diagnostics);
        using var host = new AssetServiceHost(project.PackPath);
        host.Start();
        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>("textures/player");
        Assert.Equal(TextureFilter.Nearest, descriptor.Filter);
        Assert.Equal(TextureWrap.Repeat, descriptor.Wrap);
    }

    [Fact]
    public async Task SidecarWithADifferentExtensionIsOrphaned()
    {
        // Filename decides classification: <source>.toml must match the source file name exactly
        // (case-insensitively). A sidecar for a different accepted extension is not associated.
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/tile.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/tile.jpeg.toml", "filter = \"nearest\"\n");

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.SidecarOrphaned, diagnostic.Code);
        Assert.Equal("Assets/textures/tile.jpeg.toml", diagnostic.FilePath);
        Assert.False(File.Exists(project.PackPath));
    }

    [Fact]
    public void OrphanSidecarIsBK2014()
    {
        using var project = TextureTestProject.Create();
        project.WriteText("textures/ghost.png.toml", "filter = \"nearest\"\n");

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.SidecarOrphaned, diagnostic.Code);
        Assert.Equal("Assets/textures/ghost.png.toml", diagnostic.FilePath);
        Assert.Contains("texture", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownSidecarKeyIsBK2002WithTheKeySpan()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/player.png.toml", "format = \"rgba8\"\n");

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.BinderUnknownKey, diagnostic.Code);
        Assert.Equal("Assets/textures/player.png.toml", diagnostic.FilePath);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
        Assert.Contains("format", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("filter = \"cubic\"", 10)]
    [InlineData("wrap = \"mirror\"", 8)]
    public void UnknownSidecarValueIsBK2006WithTheValueSpan(string toml, int column)
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/player.png.toml", toml);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.BinderEnumValue, diagnostic.Code);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(column, diagnostic.Column);
    }

    [Fact]
    public void WrongSidecarValueTypeIsBK2006WithTheValueSpan()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/player.png.toml", "filter = 1\n");

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.BinderEnumValue, diagnostic.Code);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(10, diagnostic.Column);
    }

    [Fact]
    public void SidecarSyntaxErrorIsBK2001()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/player.png.toml", "filter = \n");

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.TomlSyntax, diagnostic.Code);
        Assert.Equal("Assets/textures/player.png.toml", diagnostic.FilePath);
        Assert.NotNull(diagnostic.Line);
    }

    [Fact]
    public void DuplicateSidecarKeyIsBK2001OnTheSecondKey()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText(
            "textures/player.png.toml",
            """
            filter = "nearest"
            filter = "linear"
            """);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.TomlSyntax, diagnostic.Code);
        Assert.Equal(2, diagnostic.Line);
    }

    [Fact]
    public void TypedBork2dTableInASidecarIsAnUnknownKey()
    {
        // Filename decides classification, so a [bork2d] table inside <source>.toml is an unknown
        // sidecar key, not a data declaration.
        using var project = TextureTestProject.Create();
        project.WriteImage("art/player.png", TextureImageFactory.RgbaPng());
        project.WriteText(
            "art/player.png.toml",
            """
            [bork2d]
            type = "Bork2d.BakeFixture.StatsRoot"
            """);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.BinderUnknownKey, diagnostic.Code);
        Assert.Equal("Assets/art/player.png.toml", diagnostic.FilePath);
        Assert.Equal(1, diagnostic.Line);
        Assert.False(File.Exists(project.PackPath));
    }

    [Fact]
    public void InvalidSourcePathIsBK7002WithoutAnOrphanSidecar()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/bad name.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/bad name.png.toml", "filter = \"nearest\"\n");

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.ProcessorInputInvalid, diagnostic.Code);
        Assert.Equal("Assets/textures/bad name.png", diagnostic.FilePath);
        Assert.Contains("texture", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CollisionAcrossAcceptedExtensionsIsBK3005()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/Player.png", TextureImageFactory.RgbaPng());
        project.WriteImage("textures/player.jpg", TextureImageFactory.TinyJpeg());

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.False(File.Exists(project.PackPath));
        // Both the descriptor and the raw payload paths collide, so both are reported.
        BakeDiagnostic[] collisions =
        [
            .. result.Diagnostics.Where(
                diagnostic => diagnostic.Code == BakeDiagnosticCodes.AssetPathCollision),
        ];
        Assert.Equal(2, collisions.Length);
        Assert.Contains(collisions, diagnostic => diagnostic.AssetPath == "textures/player");
        Assert.Contains(collisions, diagnostic => diagnostic.AssetPath == "textures/player-raw");
    }

    [Fact]
    public void RawPayloadNamedSourceCollidesWithTheOtherSourcesPayload()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("art/player.png", TextureImageFactory.RgbaPng());
        project.WriteImage("art/player-raw.png", TextureImageFactory.RgbaPng());

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        BakeDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == BakeDiagnosticCodes.AssetPathCollision);
        Assert.Equal("art/player-raw", diagnostic.AssetPath);
    }

    [Fact]
    public void MixedCaseExtensionsAreDiscoveredInCanonicalPathOrder()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/Shout.PNG", TextureImageFactory.RgbaPng());
        project.WriteImage("textures/Photo.JpEg", TextureImageFactory.TinyJpeg());
        project.WriteImage("textures/Tile.BMP", TextureImageFactory.Bmp24());
        project.WriteImage("textures/Panel.TGA", TextureImageFactory.Tga24(topLeftOrigin: true));

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Equal(4, result.TextureCount);
        Assert.Equal(
            new[]
            {
                "textures/panel",
                "textures/photo",
                "textures/shout",
                "textures/tile",
            },
            result.Entries
                .Where(entry => entry.TypeName == TextureType)
                .Select(entry => entry.Path));
    }

    [Fact]
    public void NonAssetFilesAnywhereUnderTheRootAreIgnored()
    {
        using var project = TextureTestProject.Create();
        project.WriteText("textures/readme.txt", "not an asset");
        project.WriteImage("textures/layers.psd", [1, 2, 3, 4]);
        project.WriteText("art/readme.txt", "not an asset");

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void EmptyAssetsRootBakesAnEmptyPack()
    {
        using var project = TextureTestProject.Create();

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Entries);

        using PackArchive archive = PackArchive.Open(project.PackPath);
        Assert.Empty(archive.Entries);
    }

    [Fact]
    public void UnregisteredImageFilesAreIgnoredWithoutDefaultProcessors()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        directory.WriteFile("Assets/art/player.png", "not used");
        File.WriteAllBytes(
            directory.FullPath("Assets/art/player.png"),
            TextureImageFactory.RgbaPng());
        using var output = TempBakeDirectory.Create();

        var diagnostics = new BakeDiagnostics();
        BakeProject? project = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .CreateProject(diagnostics);
        Assert.NotNull(project);
        Assert.Empty(diagnostics.GetSorted());

        BakeResult result = BakePipeline.Run(
            project,
            new BakeOptions(output.RootPath, BakeMode.Debug, check: false, force: false),
            new BakeDiagnostics());

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Entries);
        Assert.Equal(0, result.TextureCount);
    }

    [Fact]
    public void CheckBakeCreatesNoSidecarAndWritesNoPack()
    {
        using var project = TextureTestProject.Create(TextureFilter.Nearest, TextureWrap.Repeat);
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());

        BakeResult result = project.Bake(check: true);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.False(result.PackWritten);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.False(File.Exists(project.FullPath("textures/player.png.toml")));
        Assert.False(File.Exists(project.PackPath));
        Assert.Equal(1, result.TextureCount);
    }

    [Fact]
    public void TextureDiagnosticsAreSortedByFile()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/z bad.png", TextureImageFactory.RgbaPng());
        project.WriteImage("textures/a bad.png", TextureImageFactory.RgbaPng());

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.Equal(
            new[]
            {
                "Assets/textures/a bad.png",
                "Assets/textures/z bad.png",
            },
            result.Diagnostics.Select(diagnostic => diagnostic.FilePath));
    }

    [SymlinkFact]
    public void ReparsePointFilesAreSkipped()
    {
        using var project = TextureTestProject.Create();
        string source = project.WriteImage("textures/real.png", TextureImageFactory.RgbaPng());
        string link = project.FullPath("textures/link.png");
        File.CreateSymbolicLink(link, source);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == BakeSeverity.Error);
        BakeDiagnostic created = Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.ProcessorSidecarCreated);
        Assert.Equal("Assets/textures/real.png.toml", created.FilePath);
        Assert.Equal(["textures/real", "textures/real-raw"], result.Entries.Select(entry => entry.Path));
    }

    [SymlinkFact]
    public void ReparsePointDirectoriesAreSkipped()
    {
        using var project = TextureTestProject.Create();
        // The target must be outside the asset root: discovery scans every folder, so a real
        // directory under Assets would be baked independently of the link being skipped.
        string outside = project.ProjectPath("elsewhere");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "other.png"), TextureImageFactory.RgbaPng());

        project.CreateDirectory("textures");
        string link = project.FullPath("textures/linked");
        Directory.CreateSymbolicLink(link, outside);

        BakeResult result = project.Bake();

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void UseDefaultProcessorsRegistersTheBuiltInTextureProcessor()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        var diagnostics = new BakeDiagnostics();

        BakeProject? project = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .UseDefaultProcessors()
            .UseDefaultProcessors()
            .CreateProject(diagnostics);

        Assert.NotNull(project);
        Assert.Empty(diagnostics.GetSorted());
        Assert.True(project.UseDefaultProcessors);
        IBakeAssetProcessor processor = Assert.Single(project.Processors);
        Assert.Equal("texture", processor.Name);
        Assert.Equal(["bmp", "jpeg", "jpg", "png", "tga"], processor.Extensions.Order(StringComparer.Ordinal));
        Assert.Equal(BakeSidecarPolicy.Required, processor.Policy);
        Assert.NotNull(processor.SidecarShape);
    }

    [Fact]
    public void WithTextureDefaultsAfterUseDefaultProcessorsStillApplies()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        File.WriteAllBytes(
            directory.FullPath("Assets/player.png"),
            TextureImageFactory.RgbaPng());
        using var output = TempBakeDirectory.Create();

        var diagnostics = new BakeDiagnostics();
        BakeProject? project = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets")
            .UseDefaultProcessors()
            .WithTextureDefaults(TextureFilter.Nearest, TextureWrap.Repeat)
            .CreateProject(diagnostics);
        Assert.NotNull(project);
        Assert.Empty(diagnostics.GetSorted());

        BakeResult result = BakePipeline.Run(
            project,
            new BakeOptions(output.RootPath, BakeMode.Debug, check: false, force: false),
            new BakeDiagnostics());

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.Equal(
            "filter = \"nearest\"\nwrap = \"repeat\"\n",
            File.ReadAllText(directory.FullPath("Assets/player.png.toml")));
    }
}
