using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Processors;
using Bork2d.Bake.Sources;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Assets.Pack;
using Bork2d.Tests.Assets.Service;
using Bork2d.Tests.Bake.Baking;
using Xunit;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// Pack composition and the atomic pack writer: deterministic composition through an explicit typed
/// registration, destination-directory creation, replacement of an existing pack, and the guarantee
/// that a failure leaves the previous pack byte-for-byte intact with no temporary file behind.
/// </summary>
public sealed class PackCompositionTests
{
    private const string TexturePath = "textures/player";
    private const string PayloadPath = "textures/player-raw";

    [Fact]
    public async Task ComposesCustomDataAndTextureEntriesIntoOnePack()
    {
        using var project = TestBakeProject.Create("composition");
        using var directory = TempBakeDirectory.Create();
        var diagnostics = new BakeDiagnostics();
        var game = new Bork2d.BakeFixture.GameConfig
        {
            Name = "campaign",
            Health = 7,
            Stats = new Bork2d.BakeFixture.Stats(1, 2),
        };
        byte[] payload = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();

        BakedAssetIndex index = BakedAssetIndex.Build(
            [Custom("data/game", game)],
            [
                TextureDescriptorOutput(),
                RawPayloadOutput(payload),
            ],
            diagnostics);
        Assert.Empty(diagnostics.GetSorted());

        PackBuilder? builder = PackComposer.Compose(
            index,
            project.Project,
            PackCompressionMode.Debug,
            diagnostics);
        Assert.NotNull(builder);
        Assert.Empty(diagnostics.GetSorted());

        string packPath = directory.FullPath("assets.pck");
        Assert.True(AtomicPackWriter.Write(packPath, builder, diagnostics));
        Assert.Empty(diagnostics.GetSorted());

        using PackArchive archive = PackArchive.Open(packPath);
        Assert.Equal(
            new[] { "data/game", TexturePath, PayloadPath },
            archive.Entries.Select(entry => entry.Path));

        using var host = new AssetServiceHost(packPath);
        host.Start();

        Bork2d.BakeFixture.GameConfig loaded =
            await host.Service.LoadAsset<Bork2d.BakeFixture.GameConfig>("data/game");
        Assert.Equal("campaign", loaded.Name);
        Assert.Equal(7, loaded.Health);
        Assert.Equal(new Bork2d.BakeFixture.Stats(1, 2), loaded.Stats);

        TextureDescriptor descriptor = await host.Service.LoadAsset<TextureDescriptor>(TexturePath);
        Assert.Equal(PayloadPath, descriptor.Payload.Path);
        Assert.Equal(2u, descriptor.Width);
        Assert.Equal(2u, descriptor.Height);
        Assert.Equal(TextureFormat.Rgba8, descriptor.Format);
        Assert.Equal(payload, await host.Service.LoadData(PayloadPath));
    }

    [Fact]
    public async Task AtomicWriterReplacesAnExistingPackAndLeavesNoTemporaryFile()
    {
        using var directory = TempBakeDirectory.Create();
        string packPath = directory.FullPath("nested/dir/assets.pck");
        var diagnostics = new BakeDiagnostics();

        var first = new PackBuilder(PackCompressionMode.Debug);
        first.AddRaw("data/value", new byte[] { 1, 2, 3 });
        Assert.True(AtomicPackWriter.Write(packPath, first, diagnostics));

        var second = new PackBuilder(PackCompressionMode.Debug);
        second.AddRaw("data/value", new byte[] { 9, 8, 7, 6 });
        Assert.True(AtomicPackWriter.Write(packPath, second, diagnostics));
        Assert.Empty(diagnostics.GetSorted());

        using var host = new AssetServiceHost(packPath);
        host.Start();
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, await host.Service.LoadData("data/value"));

        string destinationDirectory = Path.GetDirectoryName(packPath)!;
        Assert.Empty(Directory.GetFiles(destinationDirectory, "*.tmp"));
    }

    [Fact]
    public void FailedWriteLeavesThePreviousPackUnchangedAndRemovesTheTemporaryFile()
    {
        using var directory = TempBakeDirectory.Create();
        string packPath = directory.FullPath("assets.pck");
        var diagnostics = new BakeDiagnostics();

        var first = new PackBuilder(PackCompressionMode.Debug);
        first.AddRaw("data/value", new byte[] { 4, 5, 6 });
        Assert.True(AtomicPackWriter.Write(packPath, first, diagnostics));
        byte[] before = File.ReadAllBytes(packPath);

        var invalid = new PackBuilder(PackCompressionMode.Debug);
        invalid.AddMessagePack(
            "data/game",
            new Bork2d.BakeFixture.GameConfig
            {
                Name = "campaign",
                Texture = "data/missing",
            });

        Assert.False(AtomicPackWriter.Write(packPath, invalid, diagnostics));

        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.PackWriteFailed, diagnostic.Code);
        Assert.Equal(before, File.ReadAllBytes(packPath));
        Assert.Empty(Directory.GetFiles(directory.RootPath, "*.tmp"));
    }

    [Fact]
    public void ComposerReportsAnInternalBK5001WhenThePackWriterRejectsAnEntry()
    {
        using var project = TestBakeProject.Create("composer-reject");
        using var directory = TempBakeDirectory.Create();
        var diagnostics = new BakeDiagnostics();

        var value = new Bork2d.BakeFixture.GameConfig { Name = "campaign" };

        // A non-canonical output path is rejected by PackBuilder when the typed writer runs; the
        // composer converts the writer exception into an internal baker diagnostic.
        BakedAssetIndex index = BakedAssetIndex.Build(
            [Custom("Bad Path", value)],
            [],
            diagnostics);
        Assert.Empty(diagnostics.GetSorted());

        PackBuilder? builder = PackComposer.Compose(
            index,
            project.Project,
            PackCompressionMode.Debug,
            diagnostics);

        Assert.Null(builder);
        BakeDiagnostic diagnostic = Assert.Single(diagnostics.GetSorted());
        Assert.Equal(BakeDiagnosticCodes.PackComposeFailed, diagnostic.Code);
        Assert.Equal("Bad Path", diagnostic.AssetPath);
        Assert.Contains("Internal bake error", diagnostic.Message, StringComparison.Ordinal);
    }

    private static BoundAsset Custom(string assetPath, object value)
        => new(
            assetPath,
            typeof(Bork2d.BakeFixture.GameConfig).FullName!,
            value,
            $"Assets/{assetPath}.toml",
            $"{assetPath}.toml",
            []);

    private static BakeProcessorOutput TextureDescriptorOutput()
    {
        var descriptor = new TextureDescriptor
        {
            Payload = PayloadPath,
            Width = 2,
            Height = 2,
            Format = TextureFormat.Rgba8,
            Filter = TextureFilter.Linear,
            Wrap = TextureWrap.Clamp,
        };

        return BakeProcessorOutput.ForAsset(
            TexturePath,
            typeof(TextureDescriptor).FullName!,
            (builder, path) => builder.AddMessagePack(path, descriptor),
            [],
            "Assets/textures/player.png",
            "texture");
    }

    private static BakeProcessorOutput RawPayloadOutput(byte[] payload)
        => BakeProcessorOutput.ForData(
            PayloadPath,
            payload,
            "Assets/textures/player.png",
            "texture");
}
