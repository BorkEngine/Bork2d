using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using PolyType;

namespace Pong.Common;

/// <summary>
/// The baked game settings: the two sprite references, the movement speeds, the winning score, and
/// the per-player paddle tints.
/// </summary>
/// <param name="Paddle">The paddle sprite reference baked from <c>game_settings.toml</c>.</param>
/// <param name="Ball">The ball sprite reference baked from <c>game_settings.toml</c>.</param>
/// <param name="PaddleSpeed">The paddle movement speed in world units per second.</param>
/// <param name="BallSpeed">The ball speed in world units per second.</param>
/// <param name="WinningScore">The score that ends the match.</param>
/// <param name="LeftPaddleTint">The linear tint of the left paddle.</param>
/// <param name="RightPaddleTint">The linear tint of the right paddle.</param>
/// <remarks>
/// The type is a positional record struct with a compiled <c>[GenerateShape]</c>, so the bake
/// tooling binds <c>Assets/game_settings.toml</c> by constructor parameter name and the runtime
/// loads the packed asset with <c>LoadAsset&lt;GameSettings&gt;</c>; there is no reflection or
/// runtime TOML.
/// </remarks>
[GenerateShape]
public readonly partial record struct GameSettings(
    AssetRef<SpriteAsset> Paddle,
    AssetRef<SpriteAsset> Ball,
    float PaddleSpeed,
    float BallSpeed,
    int WinningScore,
    Color LeftPaddleTint,
    Color RightPaddleTint);
