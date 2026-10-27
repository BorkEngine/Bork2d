using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Project;
using Bork2d.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Textures;

public sealed class TextureSidecarTests
{
    [Fact]
    public void ParsedProjectDescriptorDefaultsFlowIntoTextureDefaults()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteProjectText(
            "bork2d.project.toml",
            """
            schema = 1

            [[assets]]
            root = "Assets"
            pack = "assets.pck"
            """);

        var diagnostics = new BakeDiagnostics();
        ProjectDescriptor? descriptor = ProjectDescriptorParser.Parse(
            project.ProjectPath("bork2d.project.toml"),
            diagnostics);

        Assert.NotNull(descriptor);
        Assert.False(diagnostics.HasErrors);
        BakedTexture texture = Assert.Single(TexturePipeline.Import(descriptor!).Textures);
        Assert.Equal("linear", texture.Descriptor.Filter);
        Assert.Equal("clamp", texture.Descriptor.Wrap);
    }

    [Fact]
    public void MissingSidecarAppliesDescriptorDefaults()
    {
        using var project = TextureTestProject.Create(
            BakeTextureFilter.Nearest,
            BakeTextureWrap.Repeat);
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());

        BakedTexture texture = Assert.Single(TexturePipeline.Import(project.Descriptor).Textures);

        Assert.Equal("nearest", texture.Descriptor.Filter);
        Assert.Equal("repeat", texture.Descriptor.Wrap);
        Assert.Null(texture.Settings.SidecarPath);
        Assert.Null(texture.Settings.FilterSpan);
        Assert.Null(texture.Settings.WrapSpan);
    }

    [Fact]
    public void SidecarOverridesBothSettingsAndKeepsValueSpans()
    {
        using var project = TextureTestProject.Create(
            BakeTextureFilter.Linear,
            BakeTextureWrap.Clamp);
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText(
            "textures/player.png.toml",
            """
            filter = "nearest"
            wrap = "repeat"
            """);

        BakedTexture texture = Assert.Single(TexturePipeline.Import(project.Descriptor).Textures);

        Assert.Equal("nearest", texture.Descriptor.Filter);
        Assert.Equal("repeat", texture.Descriptor.Wrap);
        Assert.Equal(project.FullPath("textures/player.png.toml"), texture.Settings.SidecarPath);
        Assert.Equal(
            new TextureSourceSpan("Assets/textures/player.png.toml", 1, 10),
            texture.Settings.FilterSpan);
        Assert.Equal(
            new TextureSourceSpan("Assets/textures/player.png.toml", 2, 8),
            texture.Settings.WrapSpan);
    }

    [Fact]
    public void PartialSidecarKeepsTheOtherDescriptorDefault()
    {
        using var project = TextureTestProject.Create(
            BakeTextureFilter.Linear,
            BakeTextureWrap.Clamp);
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/player.png.toml", "filter = \"nearest\"\n");

        BakedTexture texture = Assert.Single(TexturePipeline.Import(project.Descriptor).Textures);

        Assert.Equal("nearest", texture.Descriptor.Filter);
        Assert.Equal("clamp", texture.Descriptor.Wrap);
        Assert.NotNull(texture.Settings.FilterSpan);
        Assert.Null(texture.Settings.WrapSpan);
    }

    [Fact]
    public void EmptySidecarUsesDescriptorDefaults()
    {
        using var project = TextureTestProject.Create(
            BakeTextureFilter.Linear,
            BakeTextureWrap.Repeat);
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/player.png.toml", string.Empty);

        BakedTexture texture = Assert.Single(TexturePipeline.Import(project.Descriptor).Textures);

        Assert.Equal("linear", texture.Descriptor.Filter);
        Assert.Equal("repeat", texture.Descriptor.Wrap);
        Assert.Equal(project.FullPath("textures/player.png.toml"), texture.Settings.SidecarPath);
        Assert.Null(texture.Settings.FilterSpan);
        Assert.Null(texture.Settings.WrapSpan);
    }

    [Fact]
    public void SidecarSettingsApplyToTheMatchingSourceOnly()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteImage("textures/enemy.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/player.png.toml", "filter = \"nearest\"\nwrap = \"repeat\"\n");

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Textures.Count);
        BakedTexture player = Assert.Single(
            result.Textures,
            texture => texture.AssetPath == "textures/player");
        BakedTexture enemy = Assert.Single(
            result.Textures,
            texture => texture.AssetPath == "textures/enemy");
        Assert.Equal("linear", enemy.Descriptor.Filter);
        Assert.Equal("clamp", enemy.Descriptor.Wrap);
        Assert.Equal("nearest", player.Descriptor.Filter);
        Assert.Equal("repeat", player.Descriptor.Wrap);
    }

    [Fact]
    public void SidecarAssociationIgnoresStemAndExtensionCasing()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/Player.png", TextureImageFactory.RgbaPng());
        project.WriteText(
            "textures/PLAYER.PNG.toml",
            "filter = \"nearest\"\nwrap = \"repeat\"\n");

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Diagnostics);
        BakedTexture texture = Assert.Single(result.Textures);
        Assert.Equal("textures/player", texture.AssetPath);
        Assert.Equal("nearest", texture.Descriptor.Filter);
        Assert.Equal("repeat", texture.Descriptor.Wrap);
        Assert.Equal(
            project.FullPath("textures/PLAYER.PNG.toml"),
            texture.Settings.SidecarPath);
        Assert.Equal(
            new TextureSourceSpan("Assets/textures/PLAYER.PNG.toml", 1, 10),
            texture.Settings.FilterSpan);
    }

    [Fact]
    public void SidecarExtensionMayDifferWhenTheCanonicalPathMatches()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/tile.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/tile.jpeg.toml", "filter = \"nearest\"\n");

        BakedTexture texture = Assert.Single(TexturePipeline.Import(project.Descriptor).Textures);

        Assert.Equal("nearest", texture.Descriptor.Filter);
        Assert.Equal("textures/tile", texture.AssetPath);
    }

    [Fact]
    public void SidecarWhoseCanonicalCandidateHasNoSourceIsAnError()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/GHOST.PNG.toml", "filter = \"nearest\"\n");

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        BakedTexture texture = Assert.Single(result.Textures);
        Assert.Equal("textures/player", texture.AssetPath);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureSidecarWithoutSource, error.Code);
        Assert.Equal("Assets/textures/GHOST.PNG.toml", error.FilePath);
    }

    [Fact]
    public void UnknownSidecarKeyIsAnErrorWithTheKeySpan()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/player.png.toml", "format = \"rgba8\"\n");

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureSidecarUnknownKey, error.Code);
        Assert.Equal("Assets/textures/player.png.toml", error.FilePath);
        Assert.Equal(1, error.Line);
        Assert.Equal(1, error.Column);
        Assert.Contains("format", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("filter = \"cubic\"", 10)]
    [InlineData("wrap = \"mirror\"", 8)]
    public void UnknownSidecarValueIsAnErrorWithTheValueSpan(string toml, int column)
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/player.png.toml", toml);

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureSidecarValue, error.Code);
        Assert.Equal(1, error.Line);
        Assert.Equal(column, error.Column);
    }

    [Fact]
    public void WrongSidecarValueTypeIsAnError()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/player.png.toml", "filter = 1\n");

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureSidecarValue, error.Code);
        Assert.Equal(1, error.Line);
        Assert.Equal(10, error.Column);
    }

    [Fact]
    public void SidecarSyntaxErrorIsATomlDiagnostic()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/player.png.toml", "filter = \n");

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.TomlSyntax, error.Code);
        Assert.Equal("Assets/textures/player.png.toml", error.FilePath);
        Assert.NotNull(error.Line);
    }

    [Fact]
    public void DuplicateSidecarKeyIsATomlDiagnostic()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteText(
            "textures/player.png.toml",
            """
            filter = "nearest"
            filter = "linear"
            """);

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeDiagnosticCodes.TomlSyntax, error.Code);
        Assert.Equal(2, error.Line);
    }

    [Fact]
    public void SidecarWithoutSourceIsAnError()
    {
        using var project = TextureTestProject.Create();
        project.WriteText(
            "textures/ghost.png.toml",
            "filter = \"nearest\"\n");

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureSidecarWithoutSource, error.Code);
        Assert.Equal("Assets/textures/ghost.png.toml", error.FilePath);
    }

    [Fact]
    public void SidecarNextToAnArbitraryArtSourceApplies()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("art/player.png", TextureImageFactory.RgbaPng());
        project.WriteText("art/player.png.toml", "filter = \"nearest\"\n");

        BakedTexture texture = Assert.Single(TexturePipeline.Import(project.Descriptor).Textures);

        Assert.Equal("art/player", texture.AssetPath);
        Assert.Equal("nearest", texture.Descriptor.Filter);
        Assert.Equal(
            project.FullPath("art/player.png.toml"),
            texture.Settings.SidecarPath);
    }

    [Fact]
    public void SidecarForAnInvalidSourceIsNotAlsoReportedAsOrphaned()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/bad name.png", TextureImageFactory.RgbaPng());
        project.WriteText("textures/bad name.png.toml", "filter = \"nearest\"\n");

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureSourcePath, error.Code);
        Assert.Equal("Assets/textures/bad name.png", error.FilePath);
    }
}
