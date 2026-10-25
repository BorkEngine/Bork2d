using System.Numerics;
using Bork2d.Api;
using Microsoft.Extensions.Logging;

namespace Bork2d.AotSmoke;

/// <summary>
/// The one-tick game used by the AOT smoke app: it builds a small parented scene in
/// <c>Setup</c>, verifies it in the single <c>Tick</c>, and detaches the root in <c>Teardown</c>.
/// </summary>
/// <remarks>
/// The game deliberately uses only the public engine surface and the default logging path. It
/// creates no window, GPU resource, or pack, and it runs no loop.
/// </remarks>
internal sealed class SmokeGame : IGame<SmokeLayer>
{
    private const string Category = "Bork2d.AotSmoke";

    private static readonly Vector2 _expectedPosition = new(4f, -2f);
    private static readonly Vector2 _expectedScale = new(2f, 3f);

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
        sprite.Sprite = Sprite.Empty;
        sprite.Layer = SmokeLayer.World;
        sprite.ZIndex = 3;
        SpriteRender = sprite;
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
}
