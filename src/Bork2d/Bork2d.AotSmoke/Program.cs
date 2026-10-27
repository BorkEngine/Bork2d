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
/// The default (no arguments) run proves the M1 scene path and the M4 baked-pack path: the imported
/// <c>build/Bork2d.Bake.targets</c> bake the prepared <c>Bork2d.BakeFixture</c> asset tree into
/// <c>AppContext.BaseDirectory/assets.pck</c>, which the run mounts with
/// <see cref="Bork2d.Runtime.Bork2dAppBuilder{TLayer}.WithAssetsPack(string)"/>. It never loads SDL.
/// </para>
/// <para>
/// <c>--windowed</c> runs the M2 SDL demo on the real windowed host with <c>WithoutAssets()</c>;
/// <c>--ticks N</c> additionally turns it into the automated acceptance run that exits with code 0
/// after exactly <c>N</c> ticks. Invalid or unknown arguments print the usage and return 2. The app
/// uses only the public <c>Bork2d.Api</c>/<c>Bork2d.Runtime</c> surface, so it stays
/// NativeAOT-clean.
/// </para>
/// </remarks>
internal static class Program
{
    private const string WindowTitle = "Bork2d M2 Demo";
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;

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
        writer.WriteLine("  (no arguments)  run the headless one-tick NativeAOT smoke on the baked fixture pack");
        writer.WriteLine("  --windowed      run the interactive SDL windowed demo (Escape or close exits)");
        writer.WriteLine("  --ticks N       with --windowed: exit after exactly N completed ticks (N >= 1)");
    }

    /// <summary>
    /// Runs the M1 scene checks and the M4 baked-pack proof against the packs next to the executable.
    /// </summary>
    /// <returns>0 when every assertion passes.</returns>
    private static int RunHeadless()
    {
        string packPath = Path.Combine(AppContext.BaseDirectory, AssetSmoke.PackFileName);
        string dlcPackPath = Path.Combine(AppContext.BaseDirectory, AssetSmoke.DlcPackFileName);
        SmokeChecks.Require(
            File.Exists(packPath),
            $"the baked main pack must exist at '{packPath}'; the build's bake targets produce it "
                + "next to the app and the publish copies it next to the published executable.");
        SmokeChecks.Require(
            File.Exists(dlcPackPath),
            $"the baked DLC pack must exist at '{dlcPackPath}'; the build's bake targets produce it "
                + "next to the app and the publish copies it next to the published executable.");

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
            $"Browse must report {AssetSmoke.EntryCount} main-pack entries before the overlay; got "
                + $"{game.BrowsedEntryCount}.");
        SmokeChecks.Require(
            game.MergedBrowsedEntryCount == AssetSmoke.MergedEntryCount,
            $"Browse must report {AssetSmoke.MergedEntryCount} entries after the DLC overlay; got "
                + $"{game.MergedBrowsedEntryCount}.");
        SmokeChecks.Require(
            game.RawByteCount == AssetSmoke.TexturePayload.Length,
            "LoadData must return the baked raw texture payload.");
        SmokeChecks.Require(
            game.LoadedGameName == AssetSmoke.GameName,
            "LoadAsset<GameConfig> must return the baked class-root custom data.");
        SmokeChecks.Require(
            game.LoadedDlcGameName == AssetSmoke.DlcGameName,
            "LoadAsset<GameConfig> must return the DLC pack's custom data.");
        SmokeChecks.Require(
            game.LoadedDlcTexturePath == AssetSmoke.TexturePath,
            "the DLC configuration's texture reference must resolve to the main pack.");
        SmokeChecks.Require(
            game.LoadedStatsRoot == AssetSmoke.GameStatsRoot,
            "LoadAsset<StatsRoot>(AssetRef<StatsRoot>) must return the baked struct root.");
        SmokeChecks.Require(
            game.LoadedSpriteTexturePath == AssetSmoke.TexturePath,
            "LoadAsset<SpriteAsset> must return the baked sprite with its texture reference.");
        SmokeChecks.Require(
            game.LoadedTexturePayloadPath == AssetSmoke.TexturePayloadPath,
            "LoadAsset<TextureDescriptor> must return the baked descriptor with its raw payload reference.");

        VerifyMessagePackRoundTrip();
        VerifyRootIsCleared(game);

        Console.WriteLine(
            $"Bork2d NativeAOT smoke passed: Setup/Tick/Teardown = 1/1/1, root cleared, "
            + $"default logging, Nerdbank.MessagePack generated shape, baked fixture packs at "
            + $"'{packPath}' and '{dlcPackPath}', mainEntries={game.BrowsedEntryCount}, "
            + $"mergedEntries={game.MergedBrowsedEntryCount} ({AssetSmoke.GamePath}, "
            + $"{AssetSmoke.StatsRootPath}, {AssetSmoke.SpritePath}, {AssetSmoke.TexturePath}, "
            + $"{AssetSmoke.TexturePayloadPath}, {AssetSmoke.DlcGamePath}), "
            + $"game={game.LoadedGameName}, dlc={game.LoadedDlcGameName}, "
            + $"dlcTexture={game.LoadedDlcTexturePath}, statsRoot={game.LoadedStatsRoot}, "
            + $"spriteScale={AssetSmoke.SpriteScale}, spriteTexture={game.LoadedSpriteTexturePath}, "
            + $"texturePayload={game.LoadedTexturePayloadPath}, rawBytes={game.RawByteCount}.");
        return 0;
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
            Name = AssetSmoke.SmokeName,
            Value = AssetSmoke.SmokeValue,
            Scale = AssetSmoke.SmokeScale,
            Tint = AssetSmoke.SmokeTint,
        };

        var roundTripped = MessagePackSmokeSerializer.RoundTrip(original);

        SmokeChecks.Require(
            roundTripped.Name == original.Name,
            "the MessagePack generated shape must round-trip the name.");
        SmokeChecks.Require(
            roundTripped.Value == AssetSmoke.SmokeValue,
            "the MessagePack generated shape must round-trip the value.");
        SmokeChecks.Require(
            roundTripped.Scale == AssetSmoke.SmokeScale,
            "the MessagePack generated shape must round-trip the scale.");
        SmokeChecks.Require(
            roundTripped.Tint == original.Tint,
            "the MessagePack generated shape must round-trip the Color surrogate.");

        Console.WriteLine($"MessagePack generated-shape round trip: name={roundTripped.Name}, "
            + $"value={roundTripped.Value}, scale={roundTripped.Scale}, tint={roundTripped.Tint}");
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
