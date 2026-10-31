using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Pong.Common;

namespace Pong.Tests.TestSupport;

/// <summary>
/// A headless probe game that loads the full baked Pong asset chain, records it for assertions, and
/// requests <see cref="IEngine{TLayer}.Exit"/> after exactly one tick so the K1 loop terminates.
/// </summary>
internal sealed class BakedAssetProbeGame : IGame<GameLayer>
{
    private IEngine<GameLayer>? _engine;

    /// <summary>Gets the number of <c>Setup</c> calls.</summary>
    internal int SetupCount { get; private set; }

    /// <summary>Gets the number of <c>Tick</c> calls.</summary>
    internal int TickCount { get; private set; }

    /// <summary>Gets the number of <c>Teardown</c> calls.</summary>
    internal int TeardownCount { get; private set; }

    /// <summary>Gets the number of visible pack entries seen during <c>Setup</c>.</summary>
    internal int PackEntryCount { get; private set; }

    /// <summary>Gets the loaded settings.</summary>
    internal GameSettings Settings { get; private set; }

    /// <summary>Gets the loaded paddle sprite, or <see langword="null"/> before <c>Setup</c>.</summary>
    internal SpriteAsset? PaddleSprite { get; private set; }

    /// <summary>Gets the loaded ball sprite, or <see langword="null"/> before <c>Setup</c>.</summary>
    internal SpriteAsset? BallSprite { get; private set; }

    /// <summary>Gets the loaded paddle texture descriptor, or <see langword="null"/> before <c>Setup</c>.</summary>
    internal TextureDescriptor? PaddleTexture { get; private set; }

    /// <summary>Gets the loaded ball texture descriptor, or <see langword="null"/> before <c>Setup</c>.</summary>
    internal TextureDescriptor? BallTexture { get; private set; }

    /// <summary>Gets the byte length of the paddle's raw RGBA8 payload.</summary>
    internal int PaddlePayloadLength { get; private set; }

    /// <summary>Gets the ball's raw RGBA8 payload, or <see langword="null"/> before <c>Setup</c>.</summary>
    internal byte[]? BallPayload { get; private set; }

    /// <inheritdoc/>
    public void Setup(IEngine<GameLayer> engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        SetupCount++;
        PackEntryCount = engine.Assets.Browse().Count;
        Settings = engine.Assets.LoadAsset<GameSettings>("game_settings").GetAwaiter().GetResult();
        PaddleSprite = engine.Assets.LoadAsset(Settings.Paddle).GetAwaiter().GetResult();
        BallSprite = engine.Assets.LoadAsset(Settings.Ball).GetAwaiter().GetResult();
        PaddleTexture = engine.Assets.LoadAsset(PaddleSprite.Texture).GetAwaiter().GetResult();
        BallTexture = engine.Assets.LoadAsset(BallSprite.Texture).GetAwaiter().GetResult();
        PaddlePayloadLength = engine.Assets
            .LoadData(PaddleTexture.Payload)
            .GetAwaiter()
            .GetResult()
            .Length;
        BallPayload = engine.Assets.LoadData(BallTexture.Payload).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public void Tick()
    {
        TickCount++;
        _engine?.Exit();
    }

    /// <inheritdoc/>
    public void Teardown() => TeardownCount++;
}
