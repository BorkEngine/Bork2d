using System.Numerics;
using Bork2d.Api.Assets;
using Bork2d.Api.Rendering;
using Bork2d.BakeFixture;

namespace Bork2d.AotSmoke.Smoke;

/// <summary>
/// Holds the canonical paths and the exact expected values of the baked fixture packs that the
/// default NativeAOT smoke run loads.
/// </summary>
/// <remarks>
/// The packs are produced by building <c>Bork2d.BakeFixture.Assets</c>, which owns the committed
/// <c>Assets/</c>/<c>DLC1/</c> source trees and imports the bake targets; the AOT smoke imports the copy
/// target and mounts the copied packs. The main pack <c>assets.pck</c> contains exactly eight entries:
/// <c>data/game</c>, the <c>roots/stats</c> struct root, <c>sprites/player</c>, <c>art/player</c>, the
/// raw <c>art/player-raw</c> payload, the PPM acceptance processor's <c>art/checker</c> descriptor and
/// <c>art/checker-raw</c> payload, and the Unit acceptance processor's <c>data/archer</c>. The DLC pack
/// <c>dlc1.pck</c> contains exactly one entry, <c>data/dlc</c>, whose texture reference resolves to the
/// main pack's <c>art/player</c> (an allowed DLC -> main reference). The smoke mounts the main pack as
/// the base and <c>LoadPack("dlc1.pck")</c> as an overlay, both next to the published executable. The
/// fixture's <c>Assets/art/player.png</c>, <c>Assets/art/checker.ppm</c>, and
/// <c>Assets/data/archer.unit</c> are committed fixture <em>source assets</em>; test packs and test
/// images stay generated in temp directories (no committed test/pack binary fixture).
/// </remarks>
internal static class AssetSmoke
{
    /// <summary>The baked main pack file name next to the app and in the publish directory.</summary>
    internal const string PackFileName = "assets.pck";

    /// <summary>The baked DLC/overlay pack file name next to the app and in the publish directory.</summary>
    internal const string DlcPackFileName = "dlc1.pck";

    /// <summary>The canonical path of the baked class-root custom data.</summary>
    internal const string GamePath = "data/game";

    /// <summary>The canonical path of the baked struct-root custom data.</summary>
    internal const string StatsRootPath = "roots/stats";

    /// <summary>The canonical path of the baked sprite asset.</summary>
    internal const string SpritePath = "sprites/player";

    /// <summary>The canonical path of the baked texture descriptor.</summary>
    internal const string TexturePath = "art/player";

    /// <summary>The canonical path of the baked raw texture payload.</summary>
    internal const string TexturePayloadPath = "art/player-raw";

    /// <summary>The canonical path of the PPM acceptance processor's baked texture descriptor.</summary>
    internal const string PpmTexturePath = "art/checker";

    /// <summary>The canonical path of the PPM acceptance processor's baked raw payload.</summary>
    internal const string PpmPayloadPath = "art/checker-raw";

    /// <summary>The canonical path of the Unit acceptance processor's baked custom data.</summary>
    internal const string UnitPath = "data/archer";

    /// <summary>The canonical path of the DLC pack's custom data.</summary>
    internal const string DlcGamePath = "data/dlc";

    /// <summary>The exact entry count of the baked main pack.</summary>
    internal const int EntryCount = 8;

    /// <summary>The exact entry count of the baked DLC pack.</summary>
    internal const int DlcEntryCount = 1;

    /// <summary>The exact visible entry count after the DLC overlay is mounted.</summary>
    internal const int MergedEntryCount = EntryCount + DlcEntryCount;

    /// <summary>The expected <c>name</c> of the baked <see cref="GameConfig"/>.</summary>
    internal const string GameName = "campaign";

    /// <summary>The expected <c>name</c> of the baked DLC <see cref="GameConfig"/>.</summary>
    internal const string DlcGameName = "dlc";

    /// <summary>The expected <c>must</c> value of the baked DLC <see cref="GameConfig"/>.</summary>
    internal const string DlcGameMust = "overlay";

    /// <summary>The expected <c>health</c> of the baked DLC <see cref="GameConfig"/>.</summary>
    internal const int DlcGameHealth = 10;

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
    internal const TextureFormat ExpectedTextureFormat = TextureFormat.Rgba8;

    /// <summary>The expected sampler filter of the baked <see cref="TextureDescriptor"/>.</summary>
    internal const TextureFilter ExpectedTextureFilter = TextureFilter.Nearest;

    /// <summary>The expected sampler wrap mode of the baked <see cref="TextureDescriptor"/>.</summary>
    internal const TextureWrap ExpectedTextureWrap = TextureWrap.Repeat;

    /// <summary>
    /// The exact 2x2 RGBA8 payload the baked <c>art/player-raw</c> entry stores: red opaque,
    /// green half transparent, blue quarter transparent, white fully transparent.
    /// </summary>
    internal static ReadOnlySpan<byte> TexturePayload =>
    [
        255, 0, 0, 255,
        0, 255, 0, 128,
        0, 0, 255, 64,
        255, 255, 255, 0,
    ];

    /// <summary>The expected pixel width of the baked PPM <see cref="TextureDescriptor"/>.</summary>
    internal const uint PpmWidth = 2;

    /// <summary>The expected pixel height of the baked PPM <see cref="TextureDescriptor"/>.</summary>
    internal const uint PpmHeight = 2;

    /// <summary>The expected pixel format of the baked PPM <see cref="TextureDescriptor"/>.</summary>
    internal const TextureFormat ExpectedPpmFormat = TextureFormat.Rgba8;

    /// <summary>The expected sampler filter of the baked PPM descriptor (sidecar override).</summary>
    internal const TextureFilter ExpectedPpmFilter = TextureFilter.Nearest;

    /// <summary>The expected sampler wrap mode of the baked PPM descriptor (sidecar override).</summary>
    internal const TextureWrap ExpectedPpmWrap = TextureWrap.Repeat;

    /// <summary>
    /// The exact 2x2 RGBA8 payload the baked <c>art/checker-raw</c> entry stores: opaque red, green,
    /// blue, and white PPM pixels converted with alpha 255.
    /// </summary>
    internal static ReadOnlySpan<byte> PpmPayload =>
    [
        255, 0, 0, 255,
        0, 255, 0, 255,
        0, 0, 255, 255,
        255, 255, 255, 255,
    ];

    /// <summary>The expected name of the baked <see cref="Unit"/>.</summary>
    internal const string UnitName = "archer";

    /// <summary>The expected health of the baked <see cref="Unit"/>.</summary>
    internal const int UnitHealth = 50;

    /// <summary>The expected attack of the baked <see cref="Unit"/>.</summary>
    internal const int UnitAttack = 20;

    /// <summary>The expected value of the baked <see cref="Unit"/>.</summary>
    internal static Unit GameUnit => new(UnitName, UnitHealth, UnitAttack);

    /// <summary>The name used by the in-memory generated-shape round trip (not the baked pack).</summary>
    internal const string SmokeName = "bork2d-aot-smoke";

    /// <summary>The integer used by the in-memory generated-shape round trip.</summary>
    internal const int SmokeValue = 42;

    /// <summary>The scale used by the in-memory generated-shape round trip.</summary>
    internal const float SmokeScale = 1.5f;

    /// <summary>The linear tint used by the in-memory generated-shape round trip.</summary>
    internal static Color SmokeTint => new(R: 1f, G: 0.5f, B: 0.25f, A: 1f);
}
