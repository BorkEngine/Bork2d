using Bork2d.Engine.Assets;
using Bork2d.Engine.Scene;
using Bork2d.Runtime;
using Pong.Common;

namespace Pong.Desktop;

internal static class Program
{
    private const string WindowTitle = "Pong";
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;
    private const double MaxDeltaTime = 0.25;
    private const string PackFileName = "assets.pck";

    internal static int Main(string[] args)
    {
        string packPath = Path.Combine(AppContext.BaseDirectory, PackFileName);

        try
        {
            var game = new PongGame();
            using var app = new Bork2dAppBuilder<GameLayer>()
                .WithGame(game)
                .WithTitle(WindowTitle)
                .WithSize(WindowWidth, WindowHeight)
                .WithResizable(true)
                .WithMaxDeltaTime(MaxDeltaTime)
                .WithAssetsPack(packPath)
                .Build();
            app.Run();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Pong windowed run failed: {exception.Message}");
            return 1;
        }
    }
}
