using System.Numerics;
using Bork2d.Api;
using Bork2d.Api.Assets;
using Bork2d.Api.Scene;
using Bork2d.BakeFixture;
using Bork2d.Common.Scene;
using Microsoft.Extensions.Logging;

namespace Bork2d.AotSmoke.Smoke;

/// <summary>
/// The one-tick game used by the default AOT smoke app: it builds a small parented scene in
/// <c>Setup</c>, verifies it and the baked fixture pack in the single <c>Tick</c>, and detaches the
/// root in <c>Teardown</c>.
/// </summary>
/// <remarks>
/// <para>
/// The game deliberately uses only the public engine surface and the default logging path. It
/// creates no window, GPU resource, or loop. <c>Setup</c> starts the five loads from the baked
/// packet the app mounted before <c>Setup</c>: the <see cref="GameConfig"/> class root, the
/// <see cref="StatsRoot"/> struct root through an <see cref="AssetRef{TAsset}"/>, the
/// <see cref="SpriteAsset"/>, its <see cref="TextureDescriptor"/>, and the raw RGBA8 payload.
/// <c>Tick</c> browses/exists and awaits all five tasks with a bounded timeout before asserting
/// their exact values.
/// </para>
/// <para>
/// The asset checks use only the public <see cref="IAssets"/> surface, so the AOT smoke proves the
/// shape-only <c>LoadAsset&lt;T&gt;</c> path, including the D-M4-11 struct-root reference overload,
/// without touching internal asset types.
/// </para>
/// </remarks>
internal sealed class SmokeGame : IGame<SmokeLayer>
{
    private const string Category = "Bork2d.AotSmoke";
    private static readonly TimeSpan _loadTimeout = TimeSpan.FromSeconds(10);

    private static readonly Vector2 _expectedPosition = new(4f, -2f);
    private static readonly Vector2 _expectedScale = new(2f, 3f);

    private readonly bool _loadAssets;
    private Task<GameConfig>? _gameTask;
    private Task<StatsRoot>? _statsRootTask;
    private Task<SpriteAsset>? _spriteTask;
    private Task<TextureDescriptor>? _textureTask;
    private Task<byte[]>? _rawTask;

    /// <summary>Initializes a new instance of the <see cref="SmokeGame"/> class.</summary>
    /// <param name="loadAssets">
    /// Whether the run also loads the baked fixture pack's custom data, sprite, descriptor, and raw
    /// payload.
    /// </param>
    internal SmokeGame(bool loadAssets)
    {
        _loadAssets = loadAssets;
    }

    /// <summary>Gets the number of <c>Setup</c> calls.</summary>
    public int SetupCount { get; private set; }

    /// <summary>Gets the number of <c>Tick</c> calls.</summary>
    public int TickCount { get; private set; }

    /// <summary>Gets the number of <c>Teardown</c> calls.</summary>
    public int TeardownCount { get; private set; }

    /// <summary>Gets the engine captured during <c>Setup</c>.</summary>
    public IEngine<SmokeLayer>? Engine { get; private set; }

    /// <summary>Gets the transform created under the root.</summary>
    public ITransform? ParentTransform { get; private set; }

    /// <summary>Gets the camera created under the transform.</summary>
    public ICamera<SmokeLayer>? Camera { get; private set; }

    /// <summary>Gets the sprite created under the transform.</summary>
    public ISpriteRender<SmokeLayer>? SpriteRender { get; private set; }

    /// <summary>Gets the number of visible pack entries observed in <c>Tick</c>.</summary>
    public int BrowsedEntryCount { get; private set; }

    /// <summary>Gets the number of bytes loaded by the raw texture payload request.</summary>
    public int RawByteCount { get; private set; }

    /// <summary>Gets the baked <see cref="GameConfig"/> name loaded in <c>Tick</c>.</summary>
    public string? LoadedGameName { get; private set; }

    /// <summary>Gets the baked <see cref="StatsRoot"/> loaded in <c>Tick</c>.</summary>
    public StatsRoot LoadedStatsRoot { get; private set; }

    /// <summary>Gets the sprite's texture path loaded in <c>Tick</c>.</summary>
    public string? LoadedSpriteTexturePath { get; private set; }

    /// <summary>Gets the texture descriptor's payload path loaded in <c>Tick</c>.</summary>
    public string? LoadedTexturePayloadPath { get; private set; }

    /// <inheritdoc />
    public void Setup(IEngine<SmokeLayer> engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        SetupCount++;
        Engine = engine;

        engine.LoggerFactory.CreateLogger(Category)
            .LogInformation("AOT smoke setup: creating a parented transform, camera, and sprite");

        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = _expectedPosition;
        parent.LocalRotation = 450f;
        parent.LocalScale = _expectedScale;
        ParentTransform = parent;

        var camera = engine.CreateCamera(parent);
        camera.ZIndex = 7;
        camera.CullingMask = SmokeLayer.World;
        Camera = camera;

        var sprite = engine.CreateSpriteRender(parent);
        sprite.Sprite = null;
        sprite.Layer = SmokeLayer.World;
        sprite.ZIndex = 3;
        SpriteRender = sprite;

        if (_loadAssets)
        {
            // The baked pack was mounted by Run() before Setup, so the loader thread is live here.
            _gameTask = engine.Assets.LoadAsset<GameConfig>(AssetSmoke.GamePath);
            _statsRootTask = engine.Assets.LoadAsset(
                new AssetRef<StatsRoot>(AssetSmoke.StatsRootPath));
            _spriteTask = engine.Assets.LoadAsset<SpriteAsset>(AssetSmoke.SpritePath);
            _textureTask = engine.Assets.LoadAsset<TextureDescriptor>(AssetSmoke.TexturePath);
            _rawTask = engine.Assets.LoadData(AssetSmoke.TexturePayloadPath);
        }
    }

    /// <inheritdoc />
    public void Tick()
    {
        TickCount++;

        var engine = Engine
            ?? throw new InvalidOperationException("AOT smoke check failed: Setup did not capture the engine.");
        var parent = ParentTransform
            ?? throw new InvalidOperationException("AOT smoke check failed: Setup created no transform.");
        var camera = Camera
            ?? throw new InvalidOperationException("AOT smoke check failed: Setup created no camera.");
        var sprite = SpriteRender
            ?? throw new InvalidOperationException("AOT smoke check failed: Setup created no sprite.");

        SmokeChecks.RequireSame(engine.Root, parent.Parent, "the transform must be parented to the root.");
        SmokeChecks.RequireSame(parent, camera.Parent, "the camera must be parented to the transform.");
        SmokeChecks.RequireSame(parent, sprite.Parent, "the sprite must be parented to the transform.");

        SmokeChecks.Require(
            parent.LocalPosition == _expectedPosition,
            "the transform position must round-trip through the scene graph.");
        SmokeChecks.Require(
            parent.LocalScale == _expectedScale,
            "the transform scale must round-trip through the scene graph.");
        SmokeChecks.Require(
            parent.LocalRotation == 90f,
            "the 450-degree rotation must be normalized to 90 degrees.");
        SmokeChecks.Require(camera.ZIndex == 7, "the camera ZIndex must round-trip.");
        SmokeChecks.Require(camera.CullingMask == SmokeLayer.World, "the camera culling mask must round-trip.");
        SmokeChecks.Require(sprite.Layer == SmokeLayer.World, "the sprite layer must round-trip.");
        SmokeChecks.Require(sprite.ZIndex == 3, "the sprite ZIndex must round-trip.");

        if (_loadAssets)
        {
            VerifyAssets(engine);
        }

        engine.LoggerFactory.CreateLogger(Category)
            .LogInformation("AOT smoke tick: scene graph verified");
    }

    /// <inheritdoc />
    public void Teardown()
    {
        TeardownCount++;

        var engine = Engine
            ?? throw new InvalidOperationException("AOT smoke check failed: Setup did not capture the engine.");
        var parent = ParentTransform
            ?? throw new InvalidOperationException("AOT smoke check failed: Setup created no transform.");

        engine.Root.DetachAll();

        SmokeChecks.Require(parent.Parent is null, "Teardown must detach the root's direct children.");
        SmokeChecks.RequireSame(
            parent,
            Camera?.Parent,
            "detaching the root must keep the camera attached to the detached transform.");
        SmokeChecks.RequireSame(
            parent,
            SpriteRender?.Parent,
            "detaching the root must keep the sprite attached to the detached transform.");

        engine.LoggerFactory.CreateLogger(Category)
            .LogInformation("AOT smoke teardown: root cleared");
    }

    private void VerifyAssets(IEngine<SmokeLayer> engine)
    {
        IReadOnlyList<AssetInfo> browse = engine.Assets.Browse();
        BrowsedEntryCount = browse.Count;

        SmokeChecks.Require(
            browse.Count == AssetSmoke.EntryCount,
            $"Browse must return exactly {AssetSmoke.EntryCount} entries; got {browse.Count}.");

        string[] expectedPaths =
        [
            AssetSmoke.GamePath,
            AssetSmoke.StatsRootPath,
            AssetSmoke.SpritePath,
            AssetSmoke.TexturePath,
            AssetSmoke.TexturePayloadPath,
        ];

        foreach (string path in expectedPaths)
        {
            SmokeChecks.Require(
                browse.Any(info => info.Path == path),
                $"Browse must contain the baked asset '{path}'.");
            SmokeChecks.Require(
                engine.Assets.Exists(path),
                $"Exists must find the baked asset '{path}'.");
        }

        SmokeChecks.Require(
            !engine.Assets.Exists("data/missing"),
            "Exists must be false for a path that is not in the pack.");

        GameConfig game = _gameTask!
            .WaitAsync(_loadTimeout)
            .GetAwaiter()
            .GetResult();
        StatsRoot statsRoot = _statsRootTask!
            .WaitAsync(_loadTimeout)
            .GetAwaiter()
            .GetResult();
        SpriteAsset sprite = _spriteTask!
            .WaitAsync(_loadTimeout)
            .GetAwaiter()
            .GetResult();
        TextureDescriptor texture = _textureTask!
            .WaitAsync(_loadTimeout)
            .GetAwaiter()
            .GetResult();
        byte[] raw = _rawTask!
            .WaitAsync(_loadTimeout)
            .GetAwaiter()
            .GetResult();

        VerifyGameConfig(game);
        VerifyStatsRoot(statsRoot);
        VerifySprite(sprite);
        VerifyTexture(texture, raw);

        LoadedGameName = game.Name;
        LoadedStatsRoot = statsRoot;
        LoadedSpriteTexturePath = sprite.Texture.Path;
        LoadedTexturePayloadPath = texture.Payload.Path;
        RawByteCount = raw.Length;
    }

    private static void VerifyGameConfig(GameConfig game)
    {
        SmokeChecks.Require(
            game.Name == AssetSmoke.GameName,
            "LoadAsset<GameConfig> must round-trip the required name.");
        SmokeChecks.Require(
            game.Health == AssetSmoke.GameHealth,
            "LoadAsset<GameConfig> must round-trip the health integer.");
        SmokeChecks.Require(
            game.MaxPlayers == AssetSmoke.GameMaxPlayers,
            "LoadAsset<GameConfig> must honor the [JsonPropertyName(\"max_players\")] mapping.");
        SmokeChecks.Require(
            game.Must == AssetSmoke.GameMust,
            "LoadAsset<GameConfig> must round-trip the [JsonRequired] member.");
        SmokeChecks.Require(
            game.Stats == AssetSmoke.GameStats,
            "LoadAsset<GameConfig> must round-trip the nested readonly record struct.");
        SmokeChecks.Require(
            game.Tint == AssetSmoke.GameTint,
            "LoadAsset<GameConfig> must round-trip the Color marshaler surrogate.");
        SmokeChecks.Require(
            game.Texture.Path == AssetSmoke.TexturePath,
            "LoadAsset<GameConfig> must round-trip the AssetRef<TextureDescriptor>.");
    }

    private static void VerifyStatsRoot(StatsRoot statsRoot)
    {
        SmokeChecks.Require(
            statsRoot == AssetSmoke.GameStatsRoot,
            "LoadAsset<StatsRoot>(AssetRef<StatsRoot>) must round-trip the baked struct root.");
    }

    private static void VerifySprite(SpriteAsset sprite)
    {
        SmokeChecks.Require(
            sprite.Texture.Path == AssetSmoke.TexturePath,
            "LoadAsset<SpriteAsset> must round-trip the texture reference.");

        Vector2[] uvs = sprite.Uvs
            ?? throw new InvalidOperationException(
                "AOT smoke check failed: the baked sprite UVs must not be null.");
        SmokeChecks.Require(
            uvs.Length == AssetSmoke.SpriteUvs.Length,
            $"the baked sprite must carry {AssetSmoke.SpriteUvs.Length} UVs; got {uvs.Length}.");
        for (int index = 0; index < uvs.Length; index++)
        {
            SmokeChecks.Require(
                uvs[index] == AssetSmoke.SpriteUvs[index],
                $"the baked sprite UV {index} must round-trip.");
        }

        SmokeChecks.Require(
            sprite.Scale == AssetSmoke.SpriteScale,
            "LoadAsset<SpriteAsset> must round-trip the pixels-per-unit scale.");
        SmokeChecks.Require(
            sprite.Pivot == AssetSmoke.SpritePivot,
            "LoadAsset<SpriteAsset> must round-trip the normalized pivot.");
        SmokeChecks.Require(
            sprite.Topology == AssetSmoke.SpriteAssetTopology,
            "LoadAsset<SpriteAsset> must round-trip the topology enum.");

        ushort[] indices = sprite.Indices
            ?? throw new InvalidOperationException(
                "AOT smoke check failed: the baked sprite indices must not be null.");
        SmokeChecks.Require(
            indices.AsSpan().SequenceEqual(AssetSmoke.SpriteIndices),
            "LoadAsset<SpriteAsset> must round-trip the baked index buffer.");
    }

    private static void VerifyTexture(TextureDescriptor texture, byte[] raw)
    {
        SmokeChecks.Require(
            texture.Payload.Path == AssetSmoke.TexturePayloadPath,
            "LoadAsset<TextureDescriptor> must round-trip the DataRef payload path.");
        SmokeChecks.Require(
            texture.Width == AssetSmoke.TextureWidth && texture.Height == AssetSmoke.TextureHeight,
            "LoadAsset<TextureDescriptor> must round-trip the 2x2 dimensions.");
        SmokeChecks.Require(
            texture.Format == AssetSmoke.TextureFormat,
            "LoadAsset<TextureDescriptor> must round-trip the rgba8 format.");
        SmokeChecks.Require(
            texture.Filter == AssetSmoke.TextureFilter,
            "LoadAsset<TextureDescriptor> must round-trip the nearest filter.");
        SmokeChecks.Require(
            texture.Wrap == AssetSmoke.TextureWrap,
            "LoadAsset<TextureDescriptor> must round-trip the repeat wrap.");

        int expectedLength = checked((int)(AssetSmoke.TextureWidth * AssetSmoke.TextureHeight * 4));
        SmokeChecks.Require(
            raw.Length == expectedLength,
            $"the baked raw payload must be {expectedLength} bytes; got {raw.Length}.");
        SmokeChecks.Require(
            raw.AsSpan().SequenceEqual(AssetSmoke.TexturePayload),
            "LoadData must return the exact baked RGBA8 payload bytes.");
    }
}
