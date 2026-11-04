using Bork2d.Render.Textures;
using Xunit;

namespace Bork2d.Tests.Render.Textures;

/// <summary>
/// The placeholder page's asset identity: it is built-in pack content at fixed canonical paths, its
/// expected built-in resolution is 8x8, and it is bindless index 0. The actual descriptor and KTX2
/// payload are asserted by the built-in pack tests.
/// </summary>
public sealed class TexturePlaceholderTests
{
    [Fact]
    public void PlaceholderIsBindlessIndexZero()
    {
        Assert.Equal(0, TexturePageCapacity.PlaceholderIndex);
    }

    [Fact]
    public void PlaceholderIsBuiltInPackContentAtCanonicalPaths()
    {
        Assert.Equal("textures/placeholder", TexturePlaceholder.DescriptorPath);
        Assert.Equal("textures/placeholder-ktx2", TexturePlaceholder.PayloadPath);
        Assert.Equal(8, TexturePlaceholder.Size);
    }
}
