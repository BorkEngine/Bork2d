using Bork2d.Engine.Assets;
using Bork2d.Render.Vulkan;
using Silk.NET.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Deterministic tests for the pure sampler state used by the sprite pipeline: linear mip filtering,
/// the full mip chain's LOD range, and disabled anisotropy. The real-GPU mip test is the behavior
/// proof of the same contract.
/// </summary>
public sealed class SpritePipelineSamplerTests
{
    [Theory]
    [InlineData(TextureFilter.Nearest)]
    [InlineData(TextureFilter.Linear)]
    public void MipFilteringIsLinearOverTheFullChain(TextureFilter filter)
    {
        SamplerCreateInfo info = SpritePipeline.CreateSamplerInfo(filter, TextureWrap.Clamp);

        Assert.Equal(SamplerMipmapMode.Linear, info.MipmapMode);
        Assert.Equal(0f, info.MinLod);
        Assert.Equal(1000f, info.MaxLod);
        Assert.False(info.AnisotropyEnable);
        Assert.Equal(1f, info.MaxAnisotropy);
    }

    [Theory]
    [InlineData(TextureFilter.Nearest, Filter.Nearest, Filter.Nearest)]
    [InlineData(TextureFilter.Linear, Filter.Linear, Filter.Linear)]
    public void FilterSelectsMagnificationAndMinification(
        TextureFilter filter,
        Filter expectedMag,
        Filter expectedMin)
    {
        SamplerCreateInfo info = SpritePipeline.CreateSamplerInfo(filter, TextureWrap.Clamp);

        Assert.Equal(expectedMag, info.MagFilter);
        Assert.Equal(expectedMin, info.MinFilter);
    }

    [Theory]
    [InlineData(TextureWrap.Clamp, SamplerAddressMode.ClampToEdge)]
    [InlineData(TextureWrap.Repeat, SamplerAddressMode.Repeat)]
    public void WrapSelectsEveryAddressMode(TextureWrap wrap, SamplerAddressMode expected)
    {
        SamplerCreateInfo info = SpritePipeline.CreateSamplerInfo(TextureFilter.Linear, wrap);

        Assert.Equal(expected, info.AddressModeU);
        Assert.Equal(expected, info.AddressModeV);
        Assert.Equal(expected, info.AddressModeW);
    }
}
