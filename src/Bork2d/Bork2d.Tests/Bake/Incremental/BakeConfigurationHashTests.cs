using Bork2d.Bake.Baking;
using Bork2d.Bake.Fonts;
using Bork2d.Bake.Incremental;
using Bork2d.Bake.Svg;
using Bork2d.Bake.Textures;
using Bork2d.Tests.Bake.Baking;
using Xunit;

namespace Bork2d.Tests.Bake.Incremental;

/// <summary>
/// The configuration hash's tool markers: the texture writer/encoder/mip marker, the SVG
/// parser/MSDF/mip marker, and the font outline/atlas marker are part of the hash, so a tool upgrade
/// invalidates cached payloads.
/// </summary>
public sealed class BakeConfigurationHashTests
{
    [Fact]
    public void TextureToolMarkerParticipatesInTheConfigurationHash()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        BakeProject project = BakeProjectFixture.CreateProject(
            directory.RootPath,
            [("assets.pck", "Assets")]);

        string actual = BakeConfigurationHash.Compute(project);
        string otherTool = BakeConfigurationHash.Compute(project, "ktx2-0/bcn-0.0.0/mips-0");

        Assert.NotEqual(actual, otherTool);
        Assert.Equal("ktx2-1/bcn-2.3.0/mips-1", TextureToolInfo.ConfigurationMarker);
    }

    [Fact]
    public void SvgToolMarkerParticipatesInTheConfigurationHash()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        BakeProject project = BakeProjectFixture.CreateProject(
            directory.RootPath,
            [("assets.pck", "Assets")]);

        string actual = BakeConfigurationHash.Compute(project);
        string otherTool = BakeConfigurationHash.Compute(
            project,
            TextureToolInfo.ConfigurationMarker,
            "svg-0/seasonmsdf-0.0.0/sdfmips-0");

        Assert.NotEqual(actual, otherTool);
        Assert.Equal("svg-1/seasonmsdf-0.2.0/sdfmips-1", SvgToolInfo.ConfigurationMarker);
    }

    [Fact]
    public void FontToolMarkerParticipatesInTheConfigurationHash()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        BakeProject project = BakeProjectFixture.CreateProject(
            directory.RootPath,
            [("assets.pck", "Assets")]);

        string actual = BakeConfigurationHash.Compute(project);
        string otherTool = BakeConfigurationHash.Compute(
            project,
            TextureToolInfo.ConfigurationMarker,
            SvgToolInfo.ConfigurationMarker,
            "font-0/stbtruetype-0.0.0/atlas-0");

        Assert.NotEqual(actual, otherTool);
        Assert.Equal("font-1/stbtruetype-1.26.13/atlas-1", FontToolInfo.ConfigurationMarker);
    }
}
