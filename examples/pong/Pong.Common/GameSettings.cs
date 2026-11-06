using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using PolyType;

namespace Pong.Common;

/// <summary>
/// The baked game settings: the three sprite references, the ball's and court's custom fragment
/// shaders, the HUD font, the movement speeds, the winning score, and the per-player paddle tints.
/// </summary>
/// <param name="Paddle">The paddle sprite reference baked from <c>game_settings.toml</c>.</param>
/// <param name="Ball">The ball sprite reference baked from <c>game_settings.toml</c>.</param>
/// <param name="PaddleSpeed">The paddle movement speed in world units per second.</param>
/// <param name="BallSpeed">The ball speed in world units per second.</param>
/// <param name="WinningScore">The score that ends the match.</param>
/// <param name="LeftPaddleTint">The linear tint of the left paddle.</param>
/// <param name="RightPaddleTint">The linear tint of the right paddle.</param>
/// <param name="BallShader">The custom fragment shader reference baked for the ball.</param>
/// <param name="CourtShader">The custom fragment shader reference baked for the court.</param>
/// <param name="Wordmark">
/// The sprite reference baked from <c>art/wordmark.svg</c>; the game draws it with the built-in
/// <c>shader/sdf</c> fragment shader.
/// </param>
/// <param name="ScoreFont">
/// The HUD font reference baked from <c>fonts/score.ttf</c>; it feeds all HUD text — the two scores,
/// the match state, and the control prompt — drawn with the built-in <c>shader/sdf</c> fragment
/// shader. The field keeps its original name and TOML key.
/// </param>
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
    float PaddleSpeedMultiplier,
    float BallSpeed,
    int WinningScore,
    Color LeftPaddleTint,
    Color RightPaddleTint,
    AssetRef<ShaderAsset> BallShader,
    AssetRef<ShaderAsset> CourtShader,
    AssetRef<SpriteAsset> Wordmark,
    AssetRef<FontAsset> ScoreFont);
