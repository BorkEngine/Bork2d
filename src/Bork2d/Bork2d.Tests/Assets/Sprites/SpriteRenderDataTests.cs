using System.Numerics;
using Bork2d.Engine.Assets;
using Xunit;

namespace Bork2d.Tests.Assets.Sprites;

/// <summary>
/// Deterministic tests for the frozen sprite render data: one immutable copy per live asset,
/// idempotent and thread-safe freezing, and cloned arrays.
/// </summary>
public sealed class SpriteRenderDataTests
{
    [Fact]
    public void FreezingTheSameAssetReturnsTheSameInstance()
    {
        SpriteAsset asset = CreateAsset();

        SpriteRenderData first = SpriteRenderData.Freeze(asset);
        SpriteRenderData second = SpriteRenderData.Freeze(asset);

        Assert.Same(first, second);
        Assert.Equal("textures/player", first.Texture.Path);
        Assert.Equal(new Vector2(2f, 3f), first.Scale);
        Assert.Equal(new Vector2(0.25f, 0.75f), first.Pivot);
        Assert.Equal(SpriteTopology.Triangles, first.Topology);
        Assert.Equal(asset.Uvs, first.Uvs);
        Assert.Equal(asset.Indices, first.Indices);
    }

    [Fact]
    public async Task FreezingOnTwoThreadsProducesOneFrozenInstance()
    {
        SpriteAsset asset = CreateAsset();
        using var gate = new ManualResetEventSlim(initialState: false);

        Task<SpriteRenderData> first = Task.Run(() =>
        {
            gate.Wait();
            return SpriteRenderData.Freeze(asset);
        });
        Task<SpriteRenderData> second = Task.Run(() =>
        {
            gate.Wait();
            return SpriteRenderData.Freeze(asset);
        });
        gate.Set();

        SpriteRenderData[] frozen = await Task.WhenAll(first, second)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(frozen[0], frozen[1]);
        Assert.Same(SpriteRenderData.Freeze(asset), frozen[0]);

        asset.Scale = new Vector2(1f, 1f);
        Assert.Equal(new Vector2(2f, 3f), frozen[0].Scale);
    }

    [Fact]
    public void FrozenArraysAreClonesOfTheAssetArrays()
    {
        SpriteAsset asset = CreateAsset();
        SpriteRenderData frozen = SpriteRenderData.Freeze(asset);

        asset.Uvs![0] = new Vector2(-1f, -1f);
        asset.Indices![0] = 7;

        Assert.Equal(new Vector2(0.1f, 0.2f), frozen.Uvs![0]);
        Assert.Equal((ushort)0, frozen.Indices![0]);
    }

    [Fact]
    public void FreezeOrNullPassesThroughNull()
    {
        SpriteAsset asset = CreateAsset();

        Assert.Null(SpriteRenderData.FreezeOrNull(null));
        Assert.Same(SpriteRenderData.Freeze(asset), SpriteRenderData.FreezeOrNull(asset));
    }

    private static SpriteAsset CreateAsset() => new()
    {
        Texture = "textures/player",
        Uvs =
        [
            new Vector2(0.1f, 0.2f),
            new Vector2(0.9f, 0.2f),
            new Vector2(0.9f, 0.8f),
            new Vector2(0.1f, 0.8f),
        ],
        Scale = new Vector2(2f, 3f),
        Pivot = new Vector2(0.25f, 0.75f),
        Topology = SpriteTopology.Triangles,
        Indices = [0, 1, 2],
    };
}
