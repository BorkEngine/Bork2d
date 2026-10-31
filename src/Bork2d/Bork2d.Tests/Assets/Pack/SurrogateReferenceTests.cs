using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Assets.Pack;
using Bork2d.Tests.Assets.Service;
using Bork2d.Tests.Assets.Vfs;
using Xunit;

namespace Bork2d.Tests.Assets.Pack;

/// <summary>
/// Regression coverage for the D-M4-12 surrogate visitor: a marshaler may hide an
/// <see cref="AssetRef{TAsset}"/> in its surrogate, and the collector must visit it.
/// </summary>
public sealed class SurrogateReferenceTests
{
    [Fact]
    public async Task ReferencesInsideSurrogatesAreCollectedAndRoundTrip()
    {
        var holder = new SurrogateReferencePackData
        {
            Reference = new(new AssetRef<PackTestData>("data/config")),
        };

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddMessagePack("data/config", new PackTestData { Name = "config" });
                builder.AddMessagePack("data/holder", holder);
            });

        using var host = new AssetServiceHost(packPath);
        host.Start();

        SurrogateReferencePackData loaded =
            await host.Service.LoadAsset<SurrogateReferencePackData>("data/holder");

        Assert.Equal("data/config", loaded.Reference.Target.Path);
    }

    [Fact]
    public void MissingReferenceInsideASurrogateFailsPackBuild()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddMessagePack(
            "data/holder",
            new SurrogateReferencePackData
            {
                Reference = new(new AssetRef<PackTestData>("data/missing")),
            });

        Bork2dException exception = Assert.Throws<Bork2dException>(() => builder.Build());

        Assert.Contains("data/holder", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Reference", exception.Message, StringComparison.Ordinal);
        Assert.Contains("data/missing", exception.Message, StringComparison.Ordinal);
    }
}
