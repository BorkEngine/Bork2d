using Bork2d.Engine.Assets;
using Xunit;

namespace Bork2d.Tests.Assets.Sprites;

/// <summary>
/// Pins the public <see cref="ShaderAsset"/> defaults and the internal layout-version contract.
/// </summary>
public sealed class ShaderAssetTests
{
    [Fact]
    public void NewAssetHasTheDocumentedDefaults()
    {
        var asset = new ShaderAsset();

        Assert.False(asset.SpirV.HasValue);
        Assert.Equal(0u, asset.LayoutVersion);
        Assert.Equal("main", asset.EntryPoint);
        Assert.NotNull(asset.InstanceUniforms);
        Assert.Empty(asset.InstanceUniforms);
        Assert.NotNull(asset.GlobalUniforms);
        Assert.Empty(asset.GlobalUniforms);
        Assert.NotNull(asset.TextureSlots);
        Assert.Empty(asset.TextureSlots);
    }

    [Fact]
    public void CurrentLayoutVersionIsThree()
    {
        Assert.Equal(3u, ShaderLayout.CurrentVersion);
        Assert.Equal(16, ShaderLayout.InstanceBlockFloatCount);
        Assert.Equal(4, ShaderLayout.InstanceBlockVectorCount);
        Assert.Equal(8, ShaderLayout.GlobalBlockSlotCount);
    }
}
