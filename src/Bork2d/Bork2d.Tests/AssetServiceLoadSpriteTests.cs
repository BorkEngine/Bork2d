using Bork2d.Api;
using Bork2d.Assets;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bork2d.Tests;

public sealed class AssetServiceLoadSpriteTests
{
    private const string SecondSpritePath = "sprites/idle";

    [Fact]
    public void MissingSpriteReturnsEmptyLogsAndCachesNoHandle()
    {
        using var host = new AssetServiceHost();
        host.Start();

        Sprite handle = host.Service.LoadSprite("sprites/missing");

        Assert.Equal(Sprite.Empty, handle);
        Assert.Equal(0, host.Service.SpriteHandleCount);
        Assert.Single(
            host.LogsAt(LogLevel.Information),
            log => log.Message.Contains("sprites/missing", StringComparison.Ordinal));
    }

    [Fact]
    public void RepeatedMissesLogEachTimeAndReturnEmpty()
    {
        using var host = new AssetServiceHost();
        host.Start();

        Assert.Equal(Sprite.Empty, host.Service.LoadSprite("sprites/missing"));
        Assert.Equal(Sprite.Empty, host.Service.LoadSprite("sprites/missing"));

        Assert.Equal(
            2,
            host.LogsAt(LogLevel.Information).Count(
                log => log.Message.Contains("sprites/missing", StringComparison.Ordinal)));
        Assert.Equal(0, host.Service.SpriteHandleCount);
    }

    [Fact]
    public async Task PresentSpriteReturnsAHandleImmediatelyAndLoadsAsynchronously()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = SpriteTestSupport.WriteSpritePack(directory);

        using var host = new AssetServiceHost(packPath);
        using var entered = new ManualResetEventSlim(initialState: false);
        using var gate = new ManualResetEventSlim(initialState: false);
        host.Service.RequestProcessingHook = path =>
        {
            if (path == SpriteTestSupport.SpritePath)
            {
                entered.Set();
                gate.Wait(TimeSpan.FromSeconds(10));
            }
        };
        host.Start();

        Sprite handle = host.Service.LoadSprite(SpriteTestSupport.SpritePath);

        Assert.NotEqual(Sprite.Empty, handle);
        Assert.True(handle.Id > 0);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.False(host.Service.TryGetSpriteResource(handle, out _));

        gate.Set();
        await WaitForSpriteAsync(host, handle);

        Assert.True(host.Service.TryGetSpriteResource(handle, out SpriteResource? resource));
        Assert.True(resource.IsValid);
        Assert.NotNull(resource.Asset);
        Assert.Equal(SpriteTopology.Quad, resource.Asset.Topology);
        Assert.NotNull(resource.Texture);
        Assert.Equal(2u, resource.Texture.Descriptor!.Width);
        Assert.Equal(SpriteTestSupport.PayloadPath, resource.Texture.Payload!.Path);
        Assert.Equal(16, resource.Texture.Payload.Size);

        Assert.Contains(
            host.LogsAt(LogLevel.Debug),
            log => log.Message.Contains("Asset loaded", StringComparison.Ordinal)
                && log.Message.Contains(SpriteTestSupport.SpritePath, StringComparison.Ordinal)
                && log.Message.Contains("sprite", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandlesAreIdempotentWhilePendingAndAfterSuccess()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = SpriteTestSupport.WriteSpritePack(directory);

        using var host = new AssetServiceHost(packPath);
        using var entered = new ManualResetEventSlim(initialState: false);
        using var gate = new ManualResetEventSlim(initialState: false);
        host.Service.RequestProcessingHook = path =>
        {
            if (path == SpriteTestSupport.SpritePath)
            {
                entered.Set();
                gate.Wait(TimeSpan.FromSeconds(10));
            }
        };
        host.Start();

        Sprite handle = host.Service.LoadSprite(SpriteTestSupport.SpritePath);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        Assert.Equal(handle, host.Service.LoadSprite(SpriteTestSupport.SpritePath));

        gate.Set();
        await WaitForSpriteAsync(host, handle);

        Assert.Equal(handle, host.Service.LoadSprite(SpriteTestSupport.SpritePath));
        Assert.Equal(1, host.Service.SpriteHandleCount);
    }

    [Fact]
    public async Task RepeatedCallAfterSuccessDoesNotEnqueueAnotherLoad()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = SpriteTestSupport.WriteSpritePack(directory);

        using var host = new AssetServiceHost(packPath);
        var processed = new List<string>();
        host.Service.RequestProcessingHook = path =>
        {
            lock (processed)
            {
                processed.Add(path);
            }
        };
        host.Start();

        Sprite handle = host.Service.LoadSprite(SpriteTestSupport.SpritePath);
        await WaitForSpriteAsync(host, handle);
        int processedCount = CountProcessed(processed);

        Assert.Equal(handle, host.Service.LoadSprite(SpriteTestSupport.SpritePath));
        Assert.Equal(processedCount, CountProcessed(processed));
        Assert.Equal(1, processedCount);
    }

    [Fact]
    public async Task FailedSpriteKeepsTheSameHandleAndIsNotRetried()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Scale = 0f;

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = SpriteTestSupport.WriteSpritePack(directory, sprite: sprite);

        using var host = new AssetServiceHost(packPath);
        var processed = new List<string>();
        host.Service.RequestProcessingHook = path =>
        {
            lock (processed)
            {
                processed.Add(path);
            }
        };
        host.Start();

        Sprite handle = host.Service.LoadSprite(SpriteTestSupport.SpritePath);
        await WaitForSpriteAsync(host, handle);

        Assert.True(host.Service.TryGetSpriteResource(handle, out SpriteResource? resource));
        Assert.False(resource.IsValid);
        Assert.Contains("scale", resource.Failure, StringComparison.OrdinalIgnoreCase);
        Assert.Single(host.LogsAt(LogLevel.Error));

        int processedCount = CountProcessed(processed);
        Assert.Equal(handle, host.Service.LoadSprite(SpriteTestSupport.SpritePath));
        Assert.Equal(handle, host.Service.LoadSprite(SpriteTestSupport.SpritePath));
        Assert.Equal(processedCount, CountProcessed(processed));
        Assert.Equal(1, host.Service.SpriteResourceCount);

        Assert.True(host.Service.TryGetSpriteResource(handle, out SpriteResource? stillFailed));
        Assert.Same(resource, stillFailed);
        Assert.Single(host.LogsAt(LogLevel.Error));
    }

    [Fact]
    public async Task RawSpriteEntryIsNonRenderableAndLogsAnError()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddMessagePack(
                    SpriteTestSupport.TexturePath,
                    SpriteTestSupport.Descriptor());
                builder.AddRaw(
                    SpriteTestSupport.PayloadPath,
                    SpriteTestSupport.Payload(SpriteTestSupport.Descriptor()));
                builder.AddRaw(SpriteTestSupport.SpritePath, [1, 2, 3]);
            });

        using var host = new AssetServiceHost(packPath);
        host.Start();

        Sprite handle = host.Service.LoadSprite(SpriteTestSupport.SpritePath);
        await WaitForSpriteAsync(host, handle);

        Assert.True(host.Service.TryGetSpriteResource(handle, out SpriteResource? resource));
        Assert.False(resource.IsValid);
        Assert.Contains(
            host.LogsAt(LogLevel.Error),
            log => log.Message.Contains(SpriteTestSupport.SpritePath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingTextureDescriptorIsNonRenderable()
    {
        await AssertSpriteFailureAsync(includeDescriptor: false);
    }

    [Fact]
    public async Task MissingTexturePayloadIsNonRenderable()
    {
        await AssertSpriteFailureAsync(includePayload: false);
    }

    [Fact]
    public async Task PayloadSizeMismatchIsNonRenderable()
    {
        await AssertSpriteFailureAsync(payload: new byte[15]);
    }

    [Fact]
    public async Task PayloadIsNotReadDuringSpriteLoad()
    {
        // A release-style compressed payload that is corrupt but whose declared Size matches
        // width × height × 4. Descriptor validation is metadata-only, so the sprite becomes ready.
        TextureDescriptor descriptor = SpriteTestSupport.Descriptor();
        byte[] spriteBytes = PackSerialization.SerializeAsset(SpriteTestSupport.Quad());
        byte[] descriptorBytes = PackSerialization.SerializeAsset(descriptor);
        byte[] corruptCompressedPayload = [0xDE, 0xAD, 0xBE, 0xEF];

        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry(
                    SpriteTestSupport.SpritePath,
                    isRaw: false,
                    offset: dataStart,
                    length: (uint)spriteBytes.Length,
                    size: (uint)spriteBytes.Length),
                PackTestSupport.Entry(
                    SpriteTestSupport.TexturePath,
                    isRaw: false,
                    offset: dataStart + (ulong)spriteBytes.Length,
                    length: (uint)descriptorBytes.Length,
                    size: (uint)descriptorBytes.Length),
                PackTestSupport.Entry(
                    SpriteTestSupport.PayloadPath,
                    isRaw: true,
                    isCompressed: true,
                    offset: dataStart + (ulong)spriteBytes.Length + (ulong)descriptorBytes.Length,
                    length: (uint)corruptCompressedPayload.Length,
                    size: 16),
            ],
            spriteBytes,
            descriptorBytes,
            corruptCompressedPayload);

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WriteBytes("corrupt-payload.pck", pack);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        Sprite handle = host.Service.LoadSprite(SpriteTestSupport.SpritePath);
        await WaitForSpriteAsync(host, handle);

        Assert.True(host.Service.TryGetSpriteResource(handle, out SpriteResource? resource));
        Assert.True(resource.IsValid);
        Assert.True(resource.Texture!.Payload!.IsCompressed);
        Assert.Equal(16, resource.Texture.Payload.Size);

        // The payload was never decoded during the sprite load; reading it explicitly still fails.
        Assert.Empty(host.LogsAt(LogLevel.Error));
        var readException = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadData(SpriteTestSupport.PayloadPath));
        Assert.True(readException.Kind is AssetErrorKind.Decode or AssetErrorKind.Io);
    }

    [Fact]
    public async Task SpritesSharingATextureShareOneDescriptorResource()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddMessagePack(
                    SpriteTestSupport.TexturePath,
                    SpriteTestSupport.Descriptor());
                builder.AddRaw(
                    SpriteTestSupport.PayloadPath,
                    SpriteTestSupport.Payload(SpriteTestSupport.Descriptor()));
                builder.AddMessagePack(
                    SpriteTestSupport.SpritePath,
                    SpriteTestSupport.Quad());
                builder.AddMessagePack(
                    SecondSpritePath,
                    SpriteTestSupport.Quad());
            });

        using var host = new AssetServiceHost(packPath);
        host.Start();

        Sprite first = host.Service.LoadSprite(SpriteTestSupport.SpritePath);
        Sprite second = host.Service.LoadSprite(SecondSpritePath);
        await Task.WhenAll(
            WaitForSpriteAsync(host, first),
            WaitForSpriteAsync(host, second));

        Assert.True(host.Service.TryGetSpriteResource(first, out SpriteResource? firstResource));
        Assert.True(host.Service.TryGetSpriteResource(second, out SpriteResource? secondResource));
        Assert.Same(firstResource.Texture, secondResource.Texture);
        Assert.Equal(1, host.Service.TextureResourceCount);
        Assert.Equal(2, host.Service.SpriteResourceCount);
        Assert.Equal(2, host.Service.SpriteHandleCount);

        Assert.Contains(
            host.LogsAt(LogLevel.Debug),
            log => log.Message.Contains("Texture descriptor cache hit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedDescriptorIsCachedAndSharedByReferencingSprites()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder =>
            {
                SpriteAsset first = SpriteTestSupport.Quad();
                first.Texture = "textures/missing";
                SpriteAsset second = SpriteTestSupport.Quad();
                second.Texture = "textures/missing";

                builder.AddMessagePack(SpriteTestSupport.SpritePath, first);
                builder.AddMessagePack(SecondSpritePath, second);
            });

        using var host = new AssetServiceHost(packPath);
        host.Start();

        Sprite first = host.Service.LoadSprite(SpriteTestSupport.SpritePath);
        Sprite second = host.Service.LoadSprite(SecondSpritePath);
        await Task.WhenAll(
            WaitForSpriteAsync(host, first),
            WaitForSpriteAsync(host, second));

        Assert.True(host.Service.TryGetTextureResource("textures/missing", out TextureResource? texture));
        Assert.False(texture.IsValid);
        Assert.Equal(1, host.Service.TextureResourceCount);

        Assert.True(host.Service.TryGetSpriteResource(first, out SpriteResource? firstResource));
        Assert.True(host.Service.TryGetSpriteResource(second, out SpriteResource? secondResource));
        Assert.False(firstResource.IsValid);
        Assert.False(secondResource.IsValid);
        Assert.Contains("textures/missing", firstResource.Failure, StringComparison.Ordinal);
        Assert.Equal(2, host.LogsAt(LogLevel.Error).Length);
    }

    [Fact]
    public async Task NonQuadSpritesProduceValidResources()
    {
        foreach (SpriteAsset sprite in new[]
        {
            SpriteTestSupport.TriangleStrip(),
            SpriteTestSupport.Triangles(),
        })
        {
            using var directory = TempPackDirectory.CreateUnderTemp();
            string packPath = SpriteTestSupport.WriteSpritePack(directory, sprite: sprite);

            using var host = new AssetServiceHost(packPath);
            host.Start();

            Sprite handle = host.Service.LoadSprite(SpriteTestSupport.SpritePath);
            await WaitForSpriteAsync(host, handle);

            Assert.True(host.Service.TryGetSpriteResource(handle, out SpriteResource? resource));
            Assert.True(resource.IsValid);
            Assert.Equal(sprite.Topology, resource.Asset!.Topology);
            Assert.Equal(sprite.Indices, resource.Asset.Indices);
        }
    }

    [Fact]
    public async Task EnqueuedSpriteResolvesAgainstTheEnqueueTimeSnapshot()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        TextureDescriptor baseDescriptor = SpriteTestSupport.Descriptor();
        baseDescriptor.Width = 1;
        baseDescriptor.Height = 1;
        string basePath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddMessagePack(SpriteTestSupport.TexturePath, baseDescriptor);
                builder.AddRaw(
                    SpriteTestSupport.PayloadPath,
                    SpriteTestSupport.Payload(baseDescriptor));
                builder.AddMessagePack(
                    SpriteTestSupport.SpritePath,
                    SpriteTestSupport.Quad());
            });

        TextureDescriptor overlayDescriptor = SpriteTestSupport.Descriptor();
        overlayDescriptor.Width = 2;
        overlayDescriptor.Height = 2;
        TextureDescriptor overlayTexture = SpriteTestSupport.Descriptor();
        overlayTexture.Width = 3;
        overlayTexture.Height = 3;
        overlayTexture.Payload = "textures/raw/overlay";

        SpriteAsset overlaySprite = SpriteTestSupport.Quad();
        overlaySprite.Texture = "textures/overlay";

        string overlayPath = directory.WritePack(
            "overlay.pck",
            builder =>
            {
                // Same path as the base descriptor, shadowed by the overlay.
                builder.AddMessagePack(SpriteTestSupport.TexturePath, overlayDescriptor);
                builder.AddRaw(
                    SpriteTestSupport.PayloadPath,
                    SpriteTestSupport.Payload(overlayDescriptor));
                builder.AddMessagePack("textures/overlay", overlayTexture);
                builder.AddRaw("textures/raw/overlay", SpriteTestSupport.Payload(overlayTexture));
                builder.AddMessagePack("sprites/overlay", overlaySprite);
            });

        using var host = new AssetServiceHost(basePath);
        using var entered = new ManualResetEventSlim(initialState: false);
        using var gate = new ManualResetEventSlim(initialState: false);
        host.Service.RequestProcessingHook = path =>
        {
            if (path == SpriteTestSupport.SpritePath)
            {
                entered.Set();
                gate.Wait(TimeSpan.FromSeconds(10));
            }
        };
        host.Start();

        Sprite handle = host.Service.LoadSprite(SpriteTestSupport.SpritePath);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        host.Service.LoadPack(overlayPath);
        gate.Set();
        await WaitForSpriteAsync(host, handle);

        Assert.True(host.Service.TryGetSpriteResource(handle, out SpriteResource? resource));
        Assert.True(resource.IsValid);
        Assert.Equal(1u, resource.Texture!.Descriptor!.Width);
        Assert.Equal(4, resource.Texture.Payload!.Size);

        // LoadAsset is an independent read and sees the later snapshot.
        TextureDescriptor current = await host.Service.LoadAsset<TextureDescriptor>(
            SpriteTestSupport.TexturePath);
        Assert.Equal(2u, current.Width);

        // A sprite path that was not cached before the mount sees the overlay's descriptor too.
        Sprite overlayHandle = host.Service.LoadSprite("sprites/overlay");
        await WaitForSpriteAsync(host, overlayHandle);
        Assert.True(
            host.Service.TryGetSpriteResource(overlayHandle, out SpriteResource? overlayResource));
        Assert.True(overlayResource.IsValid);
        Assert.Equal(3u, overlayResource.Texture!.Descriptor!.Width);
        Assert.Equal(36, overlayResource.Texture.Payload!.Size);
    }

    [Fact]
    public async Task LoadAssetOfAnInternalSpriteIsIndependentOfTheResourceCache()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = SpriteTestSupport.WriteSpritePack(directory);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        Sprite handle = host.Service.LoadSprite(SpriteTestSupport.SpritePath);
        await WaitForSpriteAsync(host, handle);
        Assert.True(host.Service.TryGetSpriteResource(handle, out SpriteResource? resource));

        SpriteAsset loaded = await host.Service.LoadAsset<SpriteAsset>(
            SpriteTestSupport.SpritePath);

        Assert.NotSame(resource.Asset, loaded);
        Assert.Equal(SpriteTestSupport.TexturePath, loaded.Texture);
    }

    private static int CountProcessed(List<string> processed)
    {
        lock (processed)
        {
            return processed.Count;
        }
    }

    private static async Task WaitForSpriteAsync(AssetServiceHost host, Sprite handle)
        => await host.Service.GetSpriteCompletion(handle).WaitAsync(TimeSpan.FromSeconds(10));

    private static async Task AssertSpriteFailureAsync(
        bool includeDescriptor = true,
        bool includePayload = true,
        byte[]? payload = null)
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = SpriteTestSupport.WriteSpritePack(
            directory,
            includeDescriptor: includeDescriptor,
            includePayload: includePayload,
            payload: payload);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        Sprite handle = host.Service.LoadSprite(SpriteTestSupport.SpritePath);
        await WaitForSpriteAsync(host, handle);

        Assert.True(host.Service.TryGetSpriteResource(handle, out SpriteResource? resource));
        Assert.False(resource.IsValid);
        Assert.Contains(
            host.LogsAt(LogLevel.Error),
            log => log.Message.Contains(SpriteTestSupport.SpritePath, StringComparison.Ordinal));
    }
}
