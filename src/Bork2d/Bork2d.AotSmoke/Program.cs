using Bork2d.AotSmoke.Demo;
using Bork2d.AotSmoke.Smoke;
using Bork2d.Common.Serialization;
using Bork2d.Runtime;

namespace Bork2d.AotSmoke;

/// <summary>
/// The NativeAOT smoke app: the default one-tick headless smoke or the opt-in windowed demo.
/// </summary>
/// <remarks>
/// <para>
/// The default (no arguments) run proves the M1 scene path and the M3 asset path: it builds a small
/// temporary pack with the public <see cref="Bork2d.Assets.Pack.PackBuilder"/>, runs a one-tick headless
/// app against it with default logging, creates a parented transform/camera/sprite, loads a raw and
/// a generated-shape MessagePack asset, browses/exists, checks root cleanup, and deletes the pack
/// after the app has disposed. It never loads SDL.
/// </para>
/// <para>
/// <c>--windowed</c> runs the M2 SDL demo on the real windowed host with
/// <c>WithoutAssets()</c>; <c>--ticks N</c> additionally turns it into the automated acceptance run
/// that exits with code 0 after exactly <c>N</c> ticks. Invalid or unknown arguments print the usage
/// and return 2. The app uses only the public <c>Bork2d.Api</c>/<c>Bork2d.Runtime</c> surface plus
/// the tooling-only <c>PackBuilder</c>, so it stays NativeAOT-clean.
/// </para>
/// </remarks>
internal static class Program
{
    private const string WindowTitle = "Bork2d M2 Demo";
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;
    private const int Value = 42;
    private const float Scale = 1.5f;

    internal static int Main(string[] args)
    {
        if (!DemoArguments.TryParse(args, out var arguments, out var error))
        {
            Console.Error.WriteLine($"Bork2d.AotSmoke: {error}");
            WriteUsage(Console.Error);
            return 2;
        }

        return arguments.Windowed ? RunWindowed(arguments) : RunHeadless();
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: Bork2d.AotSmoke [--windowed [--ticks N]]");
        writer.WriteLine("  (no arguments)  run the headless one-tick NativeAOT smoke with a temporary pack");
        writer.WriteLine("  --windowed      run the interactive SDL windowed demo (Escape or close exits)");
        writer.WriteLine("  --ticks N       with --windowed: exit after exactly N completed ticks (N >= 1)");
    }

    /// <summary>
    /// Runs the M1 scene checks and the M3 pack/load/browse proof against a temporary pack.
    /// </summary>
    /// <returns>0 when every assertion passes.</returns>
    private static int RunHeadless()
    {
        string packPath = AssetSmoke.CreatePack();
        try
        {
            var game = new SmokeGame(loadAssets: true);

            using (var app = new Bork2dAppBuilder<SmokeLayer>()
                .WithGame(game)
                .WithHeadless()
                .WithAssetsPack(packPath)
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

            SmokeChecks.Require(
                game.BrowsedEntryCount == AssetSmoke.EntryCount,
                $"Browse must report {AssetSmoke.EntryCount} pack entries; got "
                    + $"{game.BrowsedEntryCount}.");
            SmokeChecks.Require(
                game.RawByteCount == AssetSmoke.RawPayload.Length,
                "LoadData must return the raw asset payload.");
            SmokeChecks.Require(
                game.LoadedName == AssetSmoke.Name,
                "LoadAsset<MessagePackSmokeData> must return the generated-shape asset.");
            SmokeChecks.Require(
                game.LoadedSpriteTexturePath == AssetSmoke.TexturePath,
                "LoadAsset<SpriteAsset> must return the public asset shape with its texture reference.");

            VerifyMessagePackRoundTrip();
            VerifyRootIsCleared(game);

            Console.WriteLine(
                $"Bork2d NativeAOT smoke passed: Setup/Tick/Teardown = 1/1/1, root cleared, "
                + $"default logging, Nerdbank.MessagePack generated shape, public sprite asset, "
                + $"pack entries={game.BrowsedEntryCount} ({AssetSmoke.RawPath}, "
                + $"{AssetSmoke.MessagePackPath}, {AssetSmoke.SpritePath}), "
                + $"rawBytes={game.RawByteCount}, messagePackName={game.LoadedName}, "
                + $"spriteTexture={game.LoadedSpriteTexturePath}.");
            return 0;
        }
        finally
        {
            if (File.Exists(packPath))
            {
                File.Delete(packPath);
            }
        }
    }

    /// <summary>
    /// Runs the M2 windowed demo; with a tick limit it also asserts the exact acceptance counts.
    /// </summary>
    /// <param name="arguments">The parsed arguments. <see cref="DemoArguments.Windowed"/> is true.</param>
    /// <returns>0 when the run and its assertions pass.</returns>
    private static int RunWindowed(DemoArguments arguments)
    {
        var game = new WindowedDemoGame(arguments.TickLimit);

        using (var app = new Bork2dAppBuilder<SmokeLayer>()
            .WithGame(game)
            .WithTitle(WindowTitle)
            .WithSize(WindowWidth, WindowHeight)
            .WithResizable(true)
            .WithMaxDeltaTime(0.25)
            .WithoutAssets()
            .Build())
        {
            app.Run();

            SmokeChecks.Require(game.SetupCount == 1, "the windowed demo must run Setup exactly once.");
            SmokeChecks.Require(
                game.TeardownCount == 1,
                "the windowed demo must run Teardown exactly once.");
        }

        if (arguments.TickLimit is { } expectedTicks)
        {
            SmokeChecks.Require(
                game.TickCount == expectedTicks,
                $"--ticks {expectedTicks} must run exactly {expectedTicks} completed ticks; ran "
                    + $"{game.TickCount}.");
        }

        SmokeChecks.Require(
            game.Player is not null && game.Player.Parent is null,
            "disposal must detach the demo's root children.");

        Console.WriteLine(
            $"Bork2d NativeAOT windowed demo passed: Setup/Tick/Teardown = 1/{game.TickCount}/1, "
            + $"finalPosition={game.FinalPosition}, tickLimit={arguments.TickLimit?.ToString() ?? "none"}, "
            + "window destroyed, default logging, exit 0.");
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
