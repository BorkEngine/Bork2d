using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Pong.Common;

namespace Pong.Tests.TestSupport;

/// <summary>
/// Builds the settings the simulation tests and the title tests need without loading a pack.
/// </summary>
internal static class PongTestSettings
{
    internal static GameSettings Create(
        float paddleSpeed = 0f,
        float ballSpeed = 2f,
        int winningScore = 11)
        => new GameSettings(
            new AssetRef<SpriteAsset>("sprites/paddle"),
            new AssetRef<SpriteAsset>("sprites/ball"),
            paddleSpeed,
            ballSpeed,
            winningScore,
            new Color(1f, 0.1f, 0.1f, 1f),
            new Color(0.1f, 0.3f, 1f, 1f));
}
