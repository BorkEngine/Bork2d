using System.Numerics;
using Bork2d.Api;
using Bork2d.Api.Assets;
using Bork2d.Api.Scene;
using Bork2d.Common.Scene;
using Bork2d.Common.Serialization;
using Microsoft.Extensions.Logging;

namespace Bork2d.AotSmoke.Smoke;

/// <summary>
/// The one-tick game used by the AOT smoke app: it builds a small parented scene in
/// <c>Setup</c>, verifies it in the single <c>Tick</c>, and detaches the root in <c>Teardown</c>.
/// </summary>
/// <remarks>
/// <para>
/// The game deliberately uses only the public engine surface and the default logging path. It
/// creates no window, GPU resource, or loop. With <c>loadAssets</c> it also exercises the M3 asset
/// pipeline: <c>Setup</c> starts a raw and a generated-shape MessagePack load, and <c>Tick</c>
/// browses/exists and awaits both tasks with a bounded timeout before asserting their values.
/// </para>
/// <para>
/// The asset checks use only the public <see cref="IAssets"/> surface, so the AOT smoke proves the
/// constrained <c>LoadAsset&lt;T&gt;</c> shape path without touching internal asset types.
/// </para>
/// </remarks>
internal sealed class SmokeGame : IGame<SmokeLayer>
{
    private const string Category = "Bork2d.AotSmoke";
    private static readonly TimeSpan _loadTimeout = TimeSpan.FromSeconds(10);

    private static readonly Vector2 _expectedPosition = new(4f, -2f);
    private static readonly Vector2 _expectedScale = new(2f, 3f);

    private readonly bool _loadAssets;
    private Task<byte[]>? _rawTask;
    private Task<MessagePackSmokeData>? _dataTask;
    private Task<SpriteAsset>? _spriteTask;

    /// <summary>Initializes a new instance of the <see cref="SmokeGame"/> class.</summary>
    /// <param name="loadAssets">
    /// Whether the run also loads the temporary base pack's raw and MessagePack assets.
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

    /// <summary>Gets the number of bytes loaded by the raw asset request.</summary>
    public int RawByteCount { get; private set; }

    /// <summary>Gets the MessagePack document name loaded in <c>Tick</c>.</summary>
    public string? LoadedName { get; private set; }

    /// <summary>Gets the sprite's texture path loaded in <c>Tick</c>.</summary>
    public string? LoadedSpriteTexturePath { get; private set; }

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
            // The base pack was opened by Run() before Setup, so the loader thread is live here.
            _rawTask = engine.Assets.LoadData(AssetSmoke.RawPath);
            _dataTask = engine.Assets.LoadAsset<MessagePackSmokeData>(AssetSmoke.MessagePackPath);
            _spriteTask = engine.Assets.LoadAsset<SpriteAsset>(AssetSmoke.SpritePath);
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
        SmokeChecks.Require(
            browse.Any(info => info.Path == AssetSmoke.RawPath),
            $"Browse must contain the raw asset '{AssetSmoke.RawPath}'.");
        SmokeChecks.Require(
            browse.Any(info => info.Path == AssetSmoke.MessagePackPath),
            $"Browse must contain the MessagePack asset '{AssetSmoke.MessagePackPath}'.");
        SmokeChecks.Require(
            browse.Any(info => info.Path == AssetSmoke.SpritePath),
            $"Browse must contain the sprite asset '{AssetSmoke.SpritePath}'.");
        SmokeChecks.Require(
            engine.Assets.Exists(AssetSmoke.RawPath),
            "Exists must find the raw asset.");
        SmokeChecks.Require(
            engine.Assets.Exists(AssetSmoke.MessagePackPath),
            "Exists must find the MessagePack asset.");
        SmokeChecks.Require(
            engine.Assets.Exists(AssetSmoke.SpritePath),
            "Exists must find the sprite asset.");
        SmokeChecks.Require(
            !engine.Assets.Exists("data/missing"),
            "Exists must be false for a path that is not in the pack.");

        byte[] raw = _rawTask!
            .WaitAsync(_loadTimeout)
            .GetAwaiter()
            .GetResult();
        MessagePackSmokeData data = _dataTask!
            .WaitAsync(_loadTimeout)
            .GetAwaiter()
            .GetResult();
        SpriteAsset sprite = _spriteTask!
            .WaitAsync(_loadTimeout)
            .GetAwaiter()
            .GetResult();

        RawByteCount = raw.Length;
        SmokeChecks.Require(
            raw.AsSpan().SequenceEqual(AssetSmoke.RawPayload),
            "LoadData must return the exact raw payload bytes.");
        SmokeChecks.Require(
            data.Name == AssetSmoke.Name,
            "LoadAsset<MessagePackSmokeData> must round-trip the name.");
        SmokeChecks.Require(
            data.Value == AssetSmoke.Value,
            "LoadAsset<MessagePackSmokeData> must round-trip the value.");
        SmokeChecks.Require(
            data.Scale == AssetSmoke.Scale,
            "LoadAsset<MessagePackSmokeData> must round-trip the scale.");
        SmokeChecks.Require(
            data.Tint == AssetSmoke.Tint,
            "LoadAsset<MessagePackSmokeData> must round-trip the Color marshaler surrogate.");
        SmokeChecks.Require(
            sprite.Texture.Path == AssetSmoke.TexturePath,
            "LoadAsset<SpriteAsset> must round-trip the texture reference.");
        SmokeChecks.Require(
            sprite.Scale == 1f,
            "LoadAsset<SpriteAsset> must round-trip the scale.");
        LoadedName = data.Name;
        LoadedSpriteTexturePath = sprite.Texture.Path;
    }
}
