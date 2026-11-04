using Bork2d.Bake.Baking;
using Bork2d.Bake.Incremental;
using Bork2d.Bake.Textures;
using Bork2d.Tests.Bake.Baking;
using Xunit;

namespace Bork2d.Tests.Bake.Incremental;

/// <summary>
/// The configuration hash's tool markers: the texture writer/encoder/mip marker is part of the hash,
/// so a tool upgrade invalidates cached texture payloads.
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
}
