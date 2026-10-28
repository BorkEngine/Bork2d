using Bork2d.Bake;
using Bork2d.Engine.Assets;
using Pong.Assets.Processors;
using Pong.Common;

return await new BakeApplicationBuilder()
    .WithProjectRoot(".")
    .WithPack("assets.pck", "Assets")
    .UseDefaultAssets()
    .RegisterAsset<GameSettings>()
    .UseDefaultProcessors()
    .WithTextureDefaults(TextureFilter.Linear, TextureWrap.Clamp)
    .AddProcessor(new CircleProcessor())
    .Build()
    .RunAsync(args);
