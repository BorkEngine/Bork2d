using Bork2d.Bake.Builder;

return await new BakeApplicationBuilder()
    .WithProjectRoot(".")
    .WithPack("assets.pck", "Assets")
    .WithPack("dlc1.pck", "DLC1")
    .UseDefaultAssets()
    .RegisterAsset<Bork2d.BakeFixture.GameConfig>()
    .RegisterAsset<Bork2d.BakeFixture.StatsRoot>()
    .UseDefaultProcessors()
    .WithTextureDefaults(TextureFilter.Linear, TextureWrap.Clamp)
    .Build()
    .RunAsync(args);
