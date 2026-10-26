using Bork2d.Api;
using Bork2d.Assets;
using Xunit;

namespace Bork2d.Tests;

public sealed class AssetDescriptorValidationTests
{
    [Theory]
    [InlineData("nearest", "clamp")]
    [InlineData("nearest", "repeat")]
    [InlineData("linear", "clamp")]
    [InlineData("linear", "repeat")]
    public async Task ValidSamplerCombinationsLoad(string filter, string wrap)
    {
        TextureDescriptor descriptor = SpriteTestSupport.Descriptor();
        descriptor.Filter = filter;
        descriptor.Wrap = wrap;

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = SpriteTestSupport.WriteSpritePack(directory, descriptor);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        TextureDescriptor loaded = await host.Service.LoadAsset<TextureDescriptor>(
            SpriteTestSupport.TexturePath);

        Assert.Equal(filter, loaded.Filter);
        Assert.Equal(wrap, loaded.Wrap);
        Assert.Equal(0, host.Service.TextureResourceCount);
    }

    [Fact]
    public async Task EveryLoadReturnsAFreshDescriptorInstance()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = SpriteTestSupport.WriteSpritePack(directory);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        TextureDescriptor first = await host.Service.LoadAsset<TextureDescriptor>(
            SpriteTestSupport.TexturePath);
        TextureDescriptor second = await host.Service.LoadAsset<TextureDescriptor>(
            SpriteTestSupport.TexturePath);

        Assert.NotSame(first, second);
        Assert.Equal(SpriteTestSupport.PayloadPath, first.Payload);
        Assert.Equal(2u, first.Width);
        Assert.Equal(2u, first.Height);
        Assert.Equal("rgba8", first.Format);
    }

    [Fact]
    public async Task ZeroWidthIsInvalidData()
    {
        TextureDescriptor descriptor = SpriteTestSupport.Descriptor();
        descriptor.Width = 0;

        AssetException exception = await AssertInvalidAsync(descriptor);

        Assert.Contains("width", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ZeroHeightIsInvalidData()
    {
        TextureDescriptor descriptor = SpriteTestSupport.Descriptor();
        descriptor.Height = 0;

        AssetException exception = await AssertInvalidAsync(descriptor);

        Assert.Contains("height", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingPayloadIsInvalidData()
    {
        AssetException exception = await AssertInvalidAsync(
            SpriteTestSupport.Descriptor(),
            includePayload: false);

        Assert.Contains(SpriteTestSupport.PayloadPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonRawPayloadIsInvalidData()
    {
        AssetException exception = await AssertInvalidAsync(
            SpriteTestSupport.Descriptor(),
            payloadAsMessagePack: true);

        Assert.Contains("raw", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PayloadSizeMismatchIsInvalidData()
    {
        AssetException exception = await AssertInvalidAsync(
            SpriteTestSupport.Descriptor(),
            payload: new byte[15]);

        Assert.Contains("16", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ktx2FormatIsInvalidData()
    {
        TextureDescriptor descriptor = SpriteTestSupport.Descriptor();
        descriptor.Format = "ktx2";

        AssetException exception = await AssertInvalidAsync(descriptor);

        Assert.Contains("rgba8", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NullFormatIsInvalidData()
    {
        TextureDescriptor descriptor = SpriteTestSupport.Descriptor();
        descriptor.Format = null;

        await AssertInvalidAsync(descriptor);
    }

    [Fact]
    public async Task InvalidFilterIsInvalidData()
    {
        TextureDescriptor descriptor = SpriteTestSupport.Descriptor();
        descriptor.Filter = "cubic";

        await AssertInvalidAsync(descriptor);
    }

    [Fact]
    public async Task InvalidWrapIsInvalidData()
    {
        TextureDescriptor descriptor = SpriteTestSupport.Descriptor();
        descriptor.Wrap = "mirror";

        await AssertInvalidAsync(descriptor);
    }

    [Fact]
    public async Task NonCanonicalPayloadPathIsInvalidData()
    {
        TextureDescriptor descriptor = SpriteTestSupport.Descriptor();
        descriptor.Payload = "textures/raw/Player";

        await AssertInvalidAsync(descriptor);
    }

    [Fact]
    public async Task NullPayloadPathIsInvalidData()
    {
        TextureDescriptor descriptor = SpriteTestSupport.Descriptor();
        descriptor.Payload = null;

        await AssertInvalidAsync(descriptor, includePayload: false);
    }

    [Fact]
    public async Task PixelCountOverflowIsRejectedWithoutOverflowing()
    {
        TextureDescriptor descriptor = SpriteTestSupport.Descriptor();
        descriptor.Width = uint.MaxValue;
        descriptor.Height = uint.MaxValue;

        AssetException exception = await AssertInvalidAsync(
            descriptor,
            payload: []);

        Assert.Contains("overflow", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DescriptorValidationDoesNotDecompressThePayload()
    {
        TextureDescriptor descriptor = SpriteTestSupport.Descriptor();
        byte[] descriptorBytes = PackSerialization.SerializeAsset(descriptor);
        byte[] corruptCompressedPayload = [0xDE, 0xAD, 0xBE, 0xEF];

        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry(
                    SpriteTestSupport.TexturePath,
                    isRaw: false,
                    offset: dataStart,
                    length: (uint)descriptorBytes.Length,
                    size: (uint)descriptorBytes.Length),
                PackTestSupport.Entry(
                    SpriteTestSupport.PayloadPath,
                    isRaw: true,
                    isCompressed: true,
                    offset: dataStart + (ulong)descriptorBytes.Length,
                    length: (uint)corruptCompressedPayload.Length,
                    size: 16),
            ],
            descriptorBytes,
            corruptCompressedPayload);

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WriteBytes("descriptor.pck", pack);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        TextureDescriptor loaded = await host.Service.LoadAsset<TextureDescriptor>(
            SpriteTestSupport.TexturePath);
        Assert.Equal(2u, loaded.Width);

        // The descriptor validated from metadata only; actually reading the payload still fails.
        var readException = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadData(SpriteTestSupport.PayloadPath));
        Assert.Equal(AssetErrorKind.Decode, readException.Kind);
    }

    [Fact]
    public void DescriptorUsesPropertyNameKeys()
    {
        byte[] payload = PackSerialization.SerializeAsset(SpriteTestSupport.Descriptor());

        Assert.True(payload.AsSpan().IndexOf("Payload"u8) >= 0);
        Assert.True(payload.AsSpan().IndexOf("Width"u8) >= 0);
        Assert.True(payload.AsSpan().IndexOf("Format"u8) >= 0);
        Assert.True(payload.AsSpan().IndexOf("Filter"u8) >= 0);
        Assert.True(payload.AsSpan().IndexOf("Wrap"u8) >= 0);
    }

    [Fact]
    public async Task MissingTopLevelDescriptorFaultsWithNotFound()
    {
        using var host = new AssetServiceHost();
        host.Start();

        var exception = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadAsset<TextureDescriptor>("textures/missing"));

        Assert.Equal(AssetErrorKind.NotFound, exception.Kind);
    }

    [Fact]
    public async Task RawTopLevelDescriptorFaultsWithRawAsset()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw(SpriteTestSupport.TexturePath, [1, 2, 3]));

        using var host = new AssetServiceHost(packPath);
        host.Start();

        var exception = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadAsset<TextureDescriptor>(SpriteTestSupport.TexturePath));

        Assert.Equal(AssetErrorKind.RawAsset, exception.Kind);
    }

    private static async Task<AssetException> AssertInvalidAsync(
        TextureDescriptor descriptor,
        bool includePayload = true,
        byte[]? payload = null,
        bool payloadAsMessagePack = false)
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddMessagePack(SpriteTestSupport.TexturePath, descriptor);
                if (!includePayload)
                {
                    return;
                }

                if (payloadAsMessagePack)
                {
                    builder.AddMessagePack(
                        SpriteTestSupport.PayloadPath,
                        new PackTestData { Name = "payload", Value = 1 });
                    return;
                }

                builder.AddRaw(
                    SpriteTestSupport.PayloadPath,
                    payload ?? SpriteTestSupport.Payload(descriptor));
            });

        using var host = new AssetServiceHost(packPath);
        host.Start();

        AssetException exception = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadAsset<TextureDescriptor>(SpriteTestSupport.TexturePath));

        Assert.Equal(AssetErrorKind.InvalidData, exception.Kind);
        Assert.Equal(SpriteTestSupport.TexturePath, exception.AssetPath);
        return exception;
    }
}
