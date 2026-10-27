using Bork2d.Api.Assets;
using Bork2d.Api.Rendering;
using Bork2d.Assets.Pack;
using Bork2d.Tests.Assets.Service;
using Bork2d.Tests.Assets.Vfs;
using Xunit;

namespace Bork2d.Tests.Assets.Pack;

/// <summary>
/// D-M4-12: <see cref="Color"/> serializes through its surrogate marshaler, packs through
/// <see cref="PackBuilder"/>, and a null surrogate is a decode fault rather than a
/// <see cref="NullReferenceException"/>.
/// </summary>
public sealed class ColorMarshalerTests
{
    [Fact]
    public async Task ColorMemberRoundTripsThroughTheMarshaler()
    {
        var color = new Color(R: 1f, G: 0.5f, B: 0.25f, A: 1f);
        var value = new ColorPackData { Tint = color };

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder => builder.AddMessagePack("data/color", value));

        using var host = new AssetServiceHost(packPath);
        host.Start();

        ColorPackData loaded = await host.Service.LoadAsset<ColorPackData>("data/color");
        Assert.Equal(color, loaded.Tint);
    }

    [Fact]
    public void SerializedColorUsesTheFourFloatSurrogate()
    {
        var value = new ColorPackData { Tint = new Color(1f, 0.5f, 0.25f, 1f) };

        byte[] payload = PackSerialization.SerializeAsset(value);

        // The map value is a four-element array of float32 values: 0x94 then 0xCA.
        ReadOnlySpan<byte> marker = [0x94, 0xCA];
        Assert.True(
            payload.AsSpan().IndexOf(marker) >= 0,
            "Color must serialize as a float[4] surrogate array, not an object map.");
    }

    [Fact]
    public void NullSurrogateFaultsDecodeInsteadOfNullReference()
    {
        var exception = Assert.Throws<AssetException>(
            () => new ColorMarshaler().Unmarshal(null));

        Assert.Equal(AssetErrorKind.Decode, exception.Kind);
        Assert.Null(exception.AssetPath);
    }

    [Fact]
    public void NonFourElementSurrogateFaultsDecode()
    {
        var exception = Assert.Throws<AssetException>(
            () => new ColorMarshaler().Unmarshal([1f, 2f, 3f]));

        Assert.Equal(AssetErrorKind.Decode, exception.Kind);
    }

    [Fact]
    public async Task NilColorMemberFaultsDecode()
    {
        // {"Tint": nil}: map(1), key "Tint" (fixstr 4), nil.
        byte[] payload = [0x81, 0xA4, (byte)'T', (byte)'i', (byte)'n', (byte)'t', 0xC0];
        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry(
                    "data/color",
                    isRaw: false,
                    isCompressed: false,
                    offset: dataStart,
                    length: (uint)payload.Length,
                    size: (uint)payload.Length),
            ],
            payload);

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WriteBytes("color.pck", pack);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        var exception = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadAsset<ColorPackData>("data/color"));

        Assert.Equal(AssetErrorKind.Decode, exception.Kind);
        Assert.Equal("data/color", exception.AssetPath);
    }
}
