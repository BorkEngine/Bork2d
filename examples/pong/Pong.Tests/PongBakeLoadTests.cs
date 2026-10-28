using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Runtime;
using Microsoft.Extensions.Logging;
using Pong.Common;
using Pong.Tests.TestSupport;
using Xunit;

namespace Pong.Tests;

/// <summary>
/// End-to-end bake/load coverage: copies the real asset tree, bakes it through the public builder
/// with <c>CircleProcessor</c>, and reads every value back through a public headless app.
/// </summary>
public sealed class PongBakeLoadTests
{
    private const int BallSize = 64;
    private const int ExpectedPackEntryCount = 7;
    private const int ExpectedPaddlePayloadLength = 16 * 96 * 4;
    private const int ExpectedBallPayloadLength = BallSize * BallSize * 4;

    [Fact]
    public async Task BakedPackLoadsThroughAHeadlessProbe()
    {
        using var project = PongTestProject.Create();

        int exitCode = await project.BakeAsync();

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(project.PackPath));

        var game = new BakedAssetProbeGame();
        using (var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithAssetsPack(project.PackPath)
            .WithLogging(static builder => builder.SetMinimumLevel(LogLevel.None))
            .Build())
        {
            app.Run();
        }

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.Equal(ExpectedPackEntryCount, game.PackEntryCount);

        GameSettings settings = game.Settings;
        Assert.Equal("sprites/paddle", settings.Paddle.Path);
        Assert.Equal("sprites/ball", settings.Ball.Path);
        Assert.Equal(14f, settings.PaddleSpeed);
        Assert.Equal(12f, settings.BallSpeed);
        Assert.Equal(11, settings.WinningScore);
        Assert.Equal(new Color(1f, 0.1f, 0.1f, 1f), settings.LeftPaddleTint);
        Assert.Equal(new Color(0.1f, 0.3f, 1f, 1f), settings.RightPaddleTint);

        SpriteAsset paddleSprite = Assert.IsType<SpriteAsset>(game.PaddleSprite);
        SpriteAsset ballSprite = Assert.IsType<SpriteAsset>(game.BallSprite);
        Assert.Equal("art/paddle", paddleSprite.Texture.Path);
        Assert.Equal("textures/ball", ballSprite.Texture.Path);
        Assert.Equal(64f, paddleSprite.Scale);
        Assert.Equal(64f, ballSprite.Scale);

        TextureDescriptor paddleTexture = Assert.IsType<TextureDescriptor>(game.PaddleTexture);
        TextureDescriptor ballTexture = Assert.IsType<TextureDescriptor>(game.BallTexture);
        Assert.Equal(16u, paddleTexture.Width);
        Assert.Equal(96u, paddleTexture.Height);
        Assert.Equal((uint)BallSize, ballTexture.Width);
        Assert.Equal((uint)BallSize, ballTexture.Height);
        Assert.Equal(TextureFilter.Linear, paddleTexture.Filter);
        Assert.Equal(TextureWrap.Clamp, paddleTexture.Wrap);
        Assert.Equal(TextureFilter.Linear, ballTexture.Filter);
        Assert.Equal(TextureWrap.Clamp, ballTexture.Wrap);

        Assert.Equal(ExpectedPaddlePayloadLength, game.PaddlePayloadLength);
        byte[] ballPayload = Assert.IsType<byte[]>(game.BallPayload);
        Assert.Equal(ExpectedBallPayloadLength, ballPayload.Length);
        Assert.Equal(255, ballPayload[AlphaOffset(BallSize / 2, BallSize / 2)]);
        Assert.Equal(0, ballPayload[AlphaOffset(0, 0)]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"resolution\": 64, \"size\": 65}")]
    public async Task InvalidCircleSourceFailsTheBake(string source)
    {
        using var project = PongTestProject.Create();
        project.WriteAsset("textures/ball.circle", source);

        int exitCode = await project.BakeAsync();

        Assert.NotEqual(0, exitCode);
        Assert.False(File.Exists(project.PackPath));
    }

    private static int AlphaOffset(int x, int y) => (((y * BallSize) + x) * 4) + 3;
}
