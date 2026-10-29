using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Runtime;
using Microsoft.Extensions.Logging;
using Pong.Common;
using Pong.Tests.TestSupport;
using Xunit;

namespace Pong.Tests;

/// <summary>
/// The full <see cref="PongGame"/> headless run on a freshly baked pack: lifecycle counts, the loaded
/// asset chain, the shared paddle sprite, tints, layers, and teardown detachment.
/// </summary>
public sealed class PongGameTests
{
    [Fact]
    public async Task HeadlessGameRunsTheFullLifecycleOnTheBakedPack()
    {
        using var project = PongTestProject.Create();
        Assert.Equal(0, await project.BakeAsync());

        var game = new PongGame(interactive: false);
        using (var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithAssetsPack(project.PackPath)
            .WithLogging(static builder => builder.SetMinimumLevel(LogLevel.None))
            .Build())
        {
            app.Run();

            Assert.Equal(1, game.SetupCount);
            Assert.Equal(1, game.TickCount);
            Assert.Equal(1, game.TeardownCount);
            Assert.Equal(7, game.PackEntryCount);

            SpriteAsset paddleSprite = Assert.IsType<SpriteAsset>(game.PaddleSprite);
            SpriteAsset ballSprite = Assert.IsType<SpriteAsset>(game.BallSprite);
            TextureDescriptor paddleTexture = Assert.IsType<TextureDescriptor>(game.PaddleTexture);
            TextureDescriptor ballTexture = Assert.IsType<TextureDescriptor>(game.BallTexture);

            Assert.Equal(16u, paddleTexture.Width);
            Assert.Equal(96u, paddleTexture.Height);
            Assert.Equal(64u, ballTexture.Width);
            Assert.Equal(64u, ballTexture.Height);
            Assert.Equal(16 * 96 * 4, game.PaddlePixelCount);
            Assert.Equal(64 * 64 * 4, game.BallPixelCount);

            Assert.NotNull(game.LeftPaddle);
            Assert.NotNull(game.RightPaddle);
            Assert.NotNull(game.Ball);
            Assert.Same(paddleSprite, game.LeftPaddle!.Sprite);
            Assert.Same(paddleSprite, game.RightPaddle!.Sprite);
            Assert.Same(ballSprite, game.Ball!.Sprite);
            Assert.Equal(game.Settings.LeftPaddleTint, game.LeftPaddle.Tint);
            Assert.Equal(game.Settings.RightPaddleTint, game.RightPaddle.Tint);
            Assert.Equal(Color.White, game.Ball.Tint);
            Assert.Equal(GameLayer.Paddles, game.LeftPaddle.Layer);
            Assert.Equal(GameLayer.Paddles, game.RightPaddle.Layer);
            Assert.Equal(GameLayer.Ball, game.Ball.Layer);
        }

        Assert.Null(game.World!.Parent);
    }
}
