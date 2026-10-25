using Bork2d.Common;
using Bork2d.Runtime;

namespace Bork2d.AotSmoke;

/// <summary>
/// The NativeAOT smoke app: default logging plus a one-tick headless app with a parented
/// transform/camera/sprite, followed by a generated-shape MessagePack round trip.
/// </summary>
/// <remarks>
/// The app runs no window, GPU work, pack loading, or real loop. It exists to prove that the
/// runtime projects publish and run under NativeAOT with zero AOT/trim warnings while the default
/// logging provider and the Nerdbank.MessagePack generated shape execute.
/// </remarks>
internal static class Program
{
    private const int Value = 42;
    private const float Scale = 1.5f;

    internal static int Main()
    {
        var game = new SmokeGame();

        using (var app = new Bork2dAppBuilder<SmokeLayer>()
            .WithGame(game)
            .WithHeadless()
            .Build())
        {
            app.Run();

            SmokeChecks.Require(game.SetupCount == 1, "Setup must run exactly once.");
            SmokeChecks.Require(game.TickCount == 1, "Tick must run exactly once.");
            SmokeChecks.Require(game.TeardownCount == 1, "Teardown must run exactly once.");
            var transform = game.ParentTransform;
            SmokeChecks.Require(
                transform is not null && transform.Parent is null,
                "the game must detach the root's children during Teardown.");
        }

        VerifyMessagePackRoundTrip();
        VerifyRootIsCleared(game);

        Console.WriteLine(
            "Bork2d NativeAOT smoke passed: Setup/Tick/Teardown = 1/1/1, root cleared, "
            + "default logging, Nerdbank.MessagePack generated shape.");
        return 0;
    }

    private static void VerifyMessagePackRoundTrip()
    {
        var original = new MessagePackSmokeData
        {
            Name = "bork2d-aot-smoke",
            Value = Value,
            Scale = Scale,
        };

        var roundTripped = MessagePackSmokeSerializer.RoundTrip(original);

        SmokeChecks.Require(
            roundTripped.Name == original.Name,
            "the MessagePack generated shape must round-trip the name.");
        SmokeChecks.Require(
            roundTripped.Value == Value,
            "the MessagePack generated shape must round-trip the value.");
        SmokeChecks.Require(
            roundTripped.Scale == Scale,
            "the MessagePack generated shape must round-trip the scale.");

        Console.WriteLine($"MessagePack generated-shape round trip: name={roundTripped.Name}, "
            + $"value={roundTripped.Value}, scale={roundTripped.Scale}");
    }

    private static void VerifyRootIsCleared(SmokeGame game)
    {
        var transform = game.ParentTransform;
        SmokeChecks.Require(
            transform is not null && transform.Parent is null,
            "app disposal must leave the root's children detached.");
        SmokeChecks.Require(
            game.Engine is not null,
            "the game must have captured the engine.");
    }
}
