using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Project;
using Bork2d.Bake.Textures;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Bake.Textures;

public sealed class TexturePipelineTests
{
    private static readonly byte[] _rgbaPayload =
    [
        255, 0, 0, 255,
        0, 255, 0, 128,
        0, 0, 255, 64,
        255, 255, 255, 0,
    ];

    private static readonly byte[] _checkerboardPayload =
    [
        255, 0, 0, 255,
        0, 255, 0, 255,
        0, 0, 255, 255,
        255, 255, 255, 255,
    ];

    [Fact]
    public void RgbaPngDecodesToExactStraightAlphaPayloadAndDescriptor()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Diagnostics);
        BakedTexture texture = Assert.Single(result.Textures);
        Assert.Equal("textures/player", texture.AssetPath);
        Assert.Equal("textures/raw/player", texture.PayloadPath);
        Assert.Equal("textures/raw/player", texture.Descriptor.Payload.Path);
        Assert.Equal(2u, texture.Descriptor.Width);
        Assert.Equal(2u, texture.Descriptor.Height);
        Assert.Equal("rgba8", texture.Descriptor.Format);
        Assert.Equal("linear", texture.Descriptor.Filter);
        Assert.Equal("clamp", texture.Descriptor.Wrap);
        Assert.Equal(16, texture.Payload.Length);
        Assert.Equal(_rgbaPayload, texture.Payload);
        Assert.Equal(project.FullPath("textures/player.png"), texture.SourcePath);
    }

    [Fact]
    public void RgbPngSuppliesOpaqueAlpha()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/rgb.png", TextureImageFactory.RgbPng());

        BakedTexture texture = Assert.Single(TexturePipeline.Import(project.Descriptor).Textures);

        Assert.Equal(
            new byte[]
            {
                10, 20, 30, 255,
                40, 50, 60, 255,
                70, 80, 90, 255,
                100, 110, 120, 255,
            },
            texture.Payload);
        Assert.Equal(2u, texture.Descriptor.Width);
        Assert.Equal(2u, texture.Descriptor.Height);
    }

    [Fact]
    public void GrayscalePngIsExpandedToRgba()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/gray.png", TextureImageFactory.GrayscalePng());

        BakedTexture texture = Assert.Single(TexturePipeline.Import(project.Descriptor).Textures);

        Assert.Equal(
            new byte[]
            {
                0, 0, 0, 255,
                85, 85, 85, 255,
                170, 170, 170, 255,
                255, 255, 255, 255,
            },
            texture.Payload);
    }

    [Fact]
    public void PalettePngIsExpandedToRgba()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/palette.png", TextureImageFactory.PalettePng());

        BakedTexture texture = Assert.Single(TexturePipeline.Import(project.Descriptor).Textures);

        Assert.Equal(_rgbaPayload, texture.Payload);
    }

    [Fact]
    public void BmpDecodesTopLeftOrdered()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/tile.bmp", TextureImageFactory.Bmp24());

        BakedTexture texture = Assert.Single(TexturePipeline.Import(project.Descriptor).Textures);

        Assert.Equal(_checkerboardPayload, texture.Payload);
        Assert.Equal("textures/tile", texture.AssetPath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TgaDecodesTopLeftOrderedForBothOriginFlags(bool topLeftOrigin)
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/tile.tga", TextureImageFactory.Tga24(topLeftOrigin));

        BakedTexture texture = Assert.Single(TexturePipeline.Import(project.Descriptor).Textures);

        Assert.Equal(_checkerboardPayload, texture.Payload);
    }

    [Fact]
    public void JpegDecodesThroughTheRealDecoderWithOpaqueAlpha()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/photo.jpg", TextureImageFactory.TinyJpeg());

        BakedTexture texture = Assert.Single(TexturePipeline.Import(project.Descriptor).Textures);

        byte[] expected =
        [
            220, 30, 30, 255,
            220, 30, 30, 255,
            220, 30, 30, 255,
            220, 30, 30, 255,
        ];
        Assert.Equal(expected, texture.Payload);
        Assert.Equal(2u, texture.Descriptor.Width);
        Assert.Equal(2u, texture.Descriptor.Height);
    }

    [Fact]
    public void UppercaseAndMixedCaseExtensionsAreDiscovered()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/Shout.PNG", TextureImageFactory.RgbaPng());
        project.WriteImage("textures/Photo.JpEg", TextureImageFactory.TinyJpeg());
        project.WriteImage("textures/Tile.BMP", TextureImageFactory.Bmp24());
        project.WriteImage("textures/Panel.TGA", TextureImageFactory.Tga24(topLeftOrigin: true));

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            new[]
            {
                "textures/panel",
                "textures/photo",
                "textures/shout",
                "textures/tile",
            },
            result.Textures.Select(texture => texture.AssetPath));
    }

    [Fact]
    public void DescriptorDefaultsComeFromTheProjectDescriptor()
    {
        using var project = TextureTestProject.Create(
            BakeTextureFilter.Nearest,
            BakeTextureWrap.Repeat);
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());

        BakedTexture texture = Assert.Single(TexturePipeline.Import(project.Descriptor).Textures);

        Assert.Equal("nearest", texture.Descriptor.Filter);
        Assert.Equal("repeat", texture.Descriptor.Wrap);
    }

    [Fact]
    public void NestedSourceMapsToCanonicalDescriptorAndRawPaths()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/ui/button.png", TextureImageFactory.RgbaPng());

        BakedTexture texture = Assert.Single(TexturePipeline.Import(project.Descriptor).Textures);

        Assert.Equal("textures/ui/button", texture.AssetPath);
        Assert.Equal("textures/raw/ui/button", texture.PayloadPath);
        Assert.Equal("textures/raw/ui/button", texture.Descriptor.Payload.Path);
    }

    [Fact]
    public void MissingTexturesDirectoryIsAnEmptyResult()
    {
        using var project = TextureTestProject.Create();

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Textures);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void NonAssetFilesUnderTexturesAreIgnored()
    {
        using var project = TextureTestProject.Create();
        project.WriteText("textures/readme.txt", "not an asset");
        project.WriteImage("textures/layers.psd", [1, 2, 3, 4]);

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Textures);
    }

    [Fact]
    public void CorruptSourceFailsWithASourceDiagnosticAndNoTexture()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/broken.png", [1, 2, 3, 4]);

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(BakeSeverity.Error, error.Severity);
        Assert.Equal(TextureDiagnosticCodes.TextureDecode, error.Code);
        Assert.Equal("Assets/textures/broken.png", error.FilePath);
        Assert.Contains("broken.png", error.Message, StringComparison.Ordinal);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void TruncatedSourceFailsWithoutAPartialDescriptor()
    {
        using var project = TextureTestProject.Create();
        byte[] complete = TextureImageFactory.RgbaPng();
        project.WriteImage("textures/truncated.png", complete[..(complete.Length / 2)]);

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureDecode, error.Code);
        Assert.Equal("Assets/textures/truncated.png", error.FilePath);
    }

    [Fact]
    public void UnsupportedContentFailsWithASourceDiagnostic()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/text.png", "this is definitely not an image"u8.ToArray());

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureDecode, error.Code);
    }

    [Fact]
    public void SixteenBitPngIsRejectedBeforeDecoding()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage(
            "textures/deep.png",
            TextureImageFactory.HeaderOnlyPng(1, 1, bitDepth: 16, colorType: 2));

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureBitDepth, error.Code);
        Assert.Equal("Assets/textures/deep.png", error.FilePath);
    }

    [Theory]
    [InlineData(0u, 1u)]
    [InlineData(1u, 0u)]
    [InlineData(100000u, 100000u)]
    public void UnsupportedPngHeaderDimensionsAreRejected(uint width, uint height)
    {
        using var project = TextureTestProject.Create();
        project.WriteImage(
            "textures/odd.png",
            TextureImageFactory.HeaderOnlyPng(width, height));

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, error.Code);
    }

    [Fact]
    public void UnsupportedBmpHeaderDimensionsAreRejected()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/odd.bmp", TextureImageFactory.HeaderOnlyBmp(100000, 100000));

        BakeDiagnostic error = Assert.Single(TexturePipeline.Import(project.Descriptor).Diagnostics);

        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, error.Code);
    }

    [Fact]
    public void ZeroBmpHeaderDimensionIsRejected()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/odd.bmp", TextureImageFactory.HeaderOnlyBmp(0, 2));

        BakeDiagnostic error = Assert.Single(TexturePipeline.Import(project.Descriptor).Diagnostics);

        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, error.Code);
    }

    [Fact]
    public void BitmapCoreHeaderBmpDecodesWithTheStbDecoder()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/core.bmp", TextureImageFactory.CoreBmp());

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Diagnostics);
        BakedTexture texture = Assert.Single(result.Textures);
        Assert.Equal(1u, texture.Descriptor.Width);
        Assert.Equal(1u, texture.Descriptor.Height);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, texture.Payload);
    }

    [Fact]
    public void BitmapCoreHeaderReadsTheHeightFromTheCoreHeader()
    {
        // 65535x1 is a valid core-header BMP. Reading the height from offset 22 (the planes field)
        // yields 0x00180001, which the probe would reject as oversized before this 196 KB image
        // could be decoded.
        using var project = TextureTestProject.Create();
        project.WriteImage(
            "textures/wide.bmp",
            TextureImageFactory.CoreBmp(65535, 1));

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Diagnostics);
        BakedTexture texture = Assert.Single(result.Textures);
        Assert.Equal(65535u, texture.Descriptor.Width);
        Assert.Equal(1u, texture.Descriptor.Height);
        Assert.Equal(65535 * 4, texture.Payload.Length);
    }

    [Fact]
    public void ZeroBitmapCoreHeaderHeightIsRejected()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage(
            "textures/odd.bmp",
            TextureImageFactory.HeaderOnlyBmpCore(1, 0));

        BakeDiagnostic error = Assert.Single(TexturePipeline.Import(project.Descriptor).Diagnostics);

        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, error.Code);
    }

    [Fact]
    public void OversizedTgaHeaderDimensionsAreRejected()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/odd.tga", TextureImageFactory.HeaderOnlyTga(65535, 65535));

        BakeDiagnostic error = Assert.Single(TexturePipeline.Import(project.Descriptor).Diagnostics);

        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, error.Code);
    }

    [Fact]
    public void ZeroTgaHeaderDimensionIsRejected()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/odd.tga", TextureImageFactory.HeaderOnlyTga(0, 2));

        BakeDiagnostic error = Assert.Single(TexturePipeline.Import(project.Descriptor).Diagnostics);

        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, error.Code);
    }

    [Fact]
    public void DecoderSeamZeroDimensionsAreRejected()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/seam.png", [9, 9, 9]);

        TextureBakeResult result = TexturePipeline.Import(
            project.Descriptor,
            FixedImage(0, 0, []));

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, error.Code);
    }

    [Fact]
    public void DecoderSeamOversizedDimensionsAreRejectedBeforeAllocation()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/seam.png", [9, 9, 9]);

        TextureBakeResult result = TexturePipeline.Import(
            project.Descriptor,
            FixedImage(100000, 100000, []));

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureDimensions, error.Code);
    }

    [Fact]
    public void DecoderSeamPayloadLengthMismatchIsRejected()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/seam.png", [9, 9, 9]);

        TextureBakeResult result = TexturePipeline.Import(
            project.Descriptor,
            FixedImage(2, 2, [1, 2, 3]));

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TexturePayloadSize, error.Code);
    }

    [Fact]
    public void DecoderSeamOutOfMemoryIsACleanDiagnostic()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/seam.png", [9, 9, 9]);
        var decoder = new StubTextureDecoder(_ => throw new OutOfMemoryException());

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor, decoder);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureOutOfMemory, error.Code);
        Assert.Equal("Assets/textures/seam.png", error.FilePath);
    }

    [Fact]
    public void DecoderSeamSuppliesPixelsForAJpegSource()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/scan.jpg", [9, 9, 9]);

        BakedTexture texture = Assert.Single(
            TexturePipeline.Import(project.Descriptor, FixedImage(1, 1, [1, 2, 3, 4])).Textures);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, texture.Payload);
        Assert.Equal("textures/scan", texture.AssetPath);
        Assert.Equal(1u, texture.Descriptor.Width);
        Assert.Equal(1u, texture.Descriptor.Height);
    }

    [Fact]
    public void CanonicalCollisionAfterLowercasingAndExtensionStrippingIsAnError()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/Player.png", TextureImageFactory.RgbaPng());
        project.WriteImage("textures/player.jpg", TextureImageFactory.TinyJpeg());

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TexturePathCollision, error.Code);
        Assert.Equal("textures/player", error.AssetPath);
        Assert.Contains("Player.png", error.Message, StringComparison.Ordinal);
        Assert.Contains("player.jpg", error.Message, StringComparison.Ordinal);
        Assert.Equal("Assets/textures/player.jpg", error.FilePath);
    }

    [Fact]
    public void DuplicateCanonicalPathFromDifferentExtensionsIsAnError()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/tile.png", TextureImageFactory.RgbaPng());
        project.WriteImage("textures/tile.jpeg", TextureImageFactory.TinyJpeg());

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        Assert.Equal(
            TextureDiagnosticCodes.TexturePathCollision,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void SourceUnderTheReservedRawPathIsAnError()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/player.png", TextureImageFactory.RgbaPng());
        project.WriteImage("textures/raw/player.png", TextureImageFactory.RgbaPng());

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        BakedTexture texture = Assert.Single(result.Textures);
        Assert.Equal("textures/player", texture.AssetPath);

        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureReservedPath, error.Code);
        Assert.Equal("Assets/textures/raw/player.png", error.FilePath);
    }

    [Fact]
    public void SourceNameThatIsNotCanonicalSafeIsAnError()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/bad name.png", TextureImageFactory.RgbaPng());

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Textures);
        BakeDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(TextureDiagnosticCodes.TextureSourcePath, error.Code);
        Assert.Equal("Assets/textures/bad name.png", error.FilePath);
    }

    [Fact]
    public void TexturesAreOrderedByCanonicalAssetPath()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/zeta.png", TextureImageFactory.RgbaPng());
        project.WriteImage("textures/alpha.png", TextureImageFactory.RgbaPng());
        project.WriteImage("textures/middle.png", TextureImageFactory.RgbaPng());

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Equal(
            new[] { "textures/alpha", "textures/middle", "textures/zeta" },
            result.Textures.Select(texture => texture.AssetPath));
    }

    [Fact]
    public void DiagnosticsAreSortedByFile()
    {
        using var project = TextureTestProject.Create();
        project.WriteImage("textures/z bad.png", TextureImageFactory.RgbaPng());
        project.WriteImage("textures/a bad.png", TextureImageFactory.RgbaPng());

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Equal(
            new[]
            {
                "Assets/textures/a bad.png",
                "Assets/textures/z bad.png",
            },
            result.Diagnostics.Select(diagnostic => diagnostic.FilePath));
    }

    [SymlinkFact]
    public void ReparsePointFilesAreSkipped()
    {
        using var project = TextureTestProject.Create();
        string source = project.WriteImage("textures/real.png", TextureImageFactory.RgbaPng());
        string link = project.FullPath("textures/link.png");
        File.CreateSymbolicLink(link, source);

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Diagnostics);
        BakedTexture texture = Assert.Single(result.Textures);
        Assert.Equal("textures/real", texture.AssetPath);
    }

    [SymlinkFact]
    public void ReparsePointDirectoriesAreSkipped()
    {
        using var project = TextureTestProject.Create();
        string outside = project.CreateDirectory("elsewhere");
        File.WriteAllBytes(Path.Combine(outside, "other.png"), TextureImageFactory.RgbaPng());
        string link = project.FullPath("textures/linked");
        Directory.CreateSymbolicLink(link, outside);

        TextureBakeResult result = TexturePipeline.Import(project.Descriptor);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Textures);
    }

    private static ITextureDecoder FixedImage(int width, int height, byte[] pixels)
        => new StubTextureDecoder(_ => new DecodedImage(width, height, pixels));

    private sealed class StubTextureDecoder(Func<Stream, DecodedImage> decode) : ITextureDecoder
    {
        DecodedImage ITextureDecoder.Decode(Stream stream) => decode(stream);
    }
}
