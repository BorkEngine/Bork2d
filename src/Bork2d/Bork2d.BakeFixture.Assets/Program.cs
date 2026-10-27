using Bork2d.Api.Assets;
using Bork2d.Bake.Builder;
using Bork2d.BakeFixture.Assets.Processors;

return await new BakeApplicationBuilder()
    .WithProjectRoot(".")
    .WithPack("assets.pck", "Assets")
    .WithPack("dlc1.pck", "DLC1")
    .UseDefaultAssets()
    .RegisterAsset<Bork2d.BakeFixture.GameConfig>()
    .RegisterAsset<Bork2d.BakeFixture.StatsRoot>()
    .UseDefaultProcessors()
    .WithTextureDefaults(TextureFilter.Linear, TextureWrap.Clamp)
    .AddProcessor(new PpmProcessor(TextureFilter.Linear, TextureWrap.Clamp))
    .AddProcessor(new UnitProcessor())
    .Build()
    .RunAsync(args);
