using System.Numerics;
using Bork2d.Api.Assets;
using Bork2d.Api.Rendering;
using Bork2d.BakeFixture;

namespace Bork2d.AotSmoke.Smoke;

/// <summary>
/// Holds the canonical paths and the exact expected values of the baked fixture pack that the
/// default NativeAOT smoke run loads.
/// </summary>
/// <remarks>
/// The pack is produced by the imported <c>build/Bork2d.Bake.targets</c> from the committed
/// <c>Bork2d.BakeFixture</c> asset tree and contains exactly five entries: <c>data/game</c>, the
/// <c>roots/stats</c> struct root, <c>sprites/player</c>, <c>textures/player</c>, and the raw
/// <c>textures/raw/player</c> payload. The smoke opens it with
/// <see cref="Path.Combine(string, string)"/> on <see cref="AppContext.BaseDirectory"/>, so the
/// published executable reads the pack next to itself instead of building a temporary pack or
/// depending on the process current directory. No binary fixture is committed.
/// </remarks>
internal static class AssetSmoke
{
    /// <summary>The baked pack file name next to the app and in the publish directory.</summary>
    internal const string PackFileName = "assets.pck";

    /// <summary>The canonical path of the baked class-root custom data.</summary>
    internal const string GamePath = "data/game";

    /// <summary>The canonical path of the baked struct-root custom data.</summary>
    internal const string StatsRootPath = "roots/stats";

    /// <summary>The canonical path of the baked sprite asset.</summary>
    internal const string SpritePath = "sprites/player";

    /// <summary>The canonical path of the baked texture descriptor.</summary>
    internal const string TexturePath = "textures/player";

    /// <summary>The canonical path of the baked raw texture payload.</summary>
    internal const string TexturePayloadPath = "textures/raw/player";

    /// <summary>The exact entry count of the baked fixture pack.</summary>
    internal const int EntryCount = 5;

    /// <summary>The expected <c>name</c> of the baked <see cref="GameConfig"/>.</summary>
    internal const string GameName = "campaign";

    /// <summary>The expected <c>health</c> of the baked <see cref="GameConfig"/>.</summary>
    internal const int GameHealth = 120;

    /// <summary>The expected <c>max_players</c> of the baked <see cref="GameConfig"/>.</summary>
    internal const int GameMaxPlayers = 4;

    /// <summary>The expected <c>must</c> value of the baked <see cref="GameConfig"/>.</summary>
    internal const string GameMust = "present";

    /// <summary>The expected nested struct of the baked <see cref="GameConfig"/>.</summary>
    internal static Stats GameStats => new(Attack: 3, Defense: 7);

    /// <summary>The expected linear tint of the baked <see cref="GameConfig"/>.</summary>
    internal static Color GameTint => new(R: 1f, G: 0.5f, B: 0.25f, A: 1f);

    /// <summary>The expected <c>attack</c> of the baked <see cref="StatsRoot"/>.</summary>
    internal const int StatsRootAttack = 11;

    /// <summary>The expected <c>defense</c> of the baked <see cref="StatsRoot"/>.</summary>
    internal const int StatsRootDefense = 13;

    /// <summary>The expected value of the baked <see cref="StatsRoot"/>.</summary>
    internal static StatsRoot GameStatsRoot => new(Attack: StatsRootAttack, Defense: StatsRootDefense);

    /// <summary>The expected four top-left-origin UVs of the baked <see cref="SpriteAsset"/>.</summary>
    internal static Vector2[] SpriteUvs =>
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    /// <summary>The expected pixels-per-world-unit scale of the baked <see cref="SpriteAsset"/>.</summary>
    internal const float SpriteScale = 16f;

    /// <summary>The expected normalized pivot of the baked <see cref="SpriteAsset"/>.</summary>
    internal static Vector2 SpritePivot => new(0.5f, 0.5f);

    /// <summary>The expected topology of the baked <see cref="SpriteAsset"/>.</summary>
    internal const SpriteTopology SpriteAssetTopology = SpriteTopology.Triangles;

    /// <summary>The expected index buffer of the baked <see cref="SpriteAsset"/>.</summary>
    internal static ushort[] SpriteIndices => [0, 1, 2, 0, 2, 3];

    /// <summary>The expected pixel width of the baked <see cref="TextureDescriptor"/>.</summary>
    internal const uint TextureWidth = 2;

    /// <summary>The expected pixel height of the baked <see cref="TextureDescriptor"/>.</summary>
    internal const uint TextureHeight = 2;

    /// <summary>The expected pixel format of the baked <see cref="TextureDescriptor"/>.</summary>
    internal const string TextureFormat = "rgba8";

    /// <summary>The expected sampler filter of the baked <see cref="TextureDescriptor"/>.</summary>
    internal const string TextureFilter = "nearest";

    /// <summary>The expected sampler wrap mode of the baked <see cref="TextureDescriptor"/>.</summary>
    internal const string TextureWrap = "repeat";

    /// <summary>
    /// The exact 2x2 RGBA8 payload the baked <c>textures/raw/player</c> entry stores: red opaque,
    /// green half transparent, blue quarter transparent, white fully transparent.
    /// </summary>
    internal static ReadOnlySpan<byte> TexturePayload =>
    [
        255, 0, 0, 255,
        0, 255, 0, 128,
        0, 0, 255, 64,
        255, 255, 255, 0,
    ];

    /// <summary>The name used by the in-memory generated-shape round trip (not the baked pack).</summary>
    internal const string SmokeName = "bork2d-aot-smoke";

    /// <summary>The integer used by the in-memory generated-shape round trip.</summary>
    internal const int SmokeValue = 42;

    /// <summary>The scale used by the in-memory generated-shape round trip.</summary>
    internal const float SmokeScale = 1.5f;

    /// <summary>The linear tint used by the in-memory generated-shape round trip.</summary>
    internal static Color SmokeTint => new(R: 1f, G: 0.5f, B: 0.25f, A: 1f);
}
