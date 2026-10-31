using System.IO.Compression;
using System.Text;

namespace Bork2d.Tests.Bake.Textures;

/// <summary>
/// Generates tiny in-code texture sources so no binary fixture files are committed.
/// </summary>
/// <remarks>
/// PNG sources are encoded here (signature, IHDR, optional PLTE/tRNS, zlib IDAT, IEND) so the tests
/// control colors, alpha, palette, grayscale, and bit depth exactly. BMP and TGA are written with
/// their documented headers; the JPG sample is a 2×2 solid-red image encoded once and embedded as
/// base64 in this file (allowed by the M4 test plan for the real decoder path).
/// </remarks>
internal static class TextureImageFactory
{
    private const byte PngColorTypeGrayscale = 0;
    private const byte PngColorTypeRgb = 2;
    private const byte PngColorTypePalette = 3;
    private const byte PngColorTypeRgba = 6;

    private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly byte[] _bmp24Pixels =
    [
        0, 0, 255, 0, 255, 0,
        255, 0, 0, 255, 255, 255,
    ];

    /// <summary>
    /// Creates a 2×2 PNG with straight-alpha RGBA pixels: red opaque, green at alpha 128, blue at
    /// alpha 64, and transparent white.
    /// </summary>
    /// <returns>The PNG bytes.</returns>
    internal static byte[] RgbaPng()
        => CreatePng(
            2,
            2,
            PngColorTypeRgba,
            8,
            [
                255, 0, 0, 255,
                0, 255, 0, 128,
                0, 0, 255, 64,
                255, 255, 255, 0,
            ],
            palette: null,
            paletteAlpha: null);

    /// <summary>
    /// Creates a 2×2 RGB PNG with the pixels (10,20,30), (40,50,60), (70,80,90), (100,110,120).
    /// </summary>
    /// <returns>The PNG bytes.</returns>
    internal static byte[] RgbPng()
        => CreatePng(
            2,
            2,
            PngColorTypeRgb,
            8,
            [
                10, 20, 30,
                40, 50, 60,
                70, 80, 90,
                100, 110, 120,
            ],
            palette: null,
            paletteAlpha: null);

    /// <summary>
    /// Creates a 2×2 8-bit grayscale PNG with the values 0, 85, 170, and 255.
    /// </summary>
    /// <returns>The PNG bytes.</returns>
    internal static byte[] GrayscalePng()
        => CreatePng(
            2,
            2,
            PngColorTypeGrayscale,
            8,
            [0, 85, 170, 255],
            palette: null,
            paletteAlpha: null);

    /// <summary>
    /// Creates a 2×2 8-bit palette PNG with red/green/blue/white entries, palette alpha 255/128/64/0,
    /// and indices 0/1/2/3.
    /// </summary>
    /// <returns>The PNG bytes.</returns>
    internal static byte[] PalettePng()
        => CreatePng(
            2,
            2,
            PngColorTypePalette,
            8,
            [0, 1, 2, 3],
            palette:
            [
                255, 0, 0,
                0, 255, 0,
                0, 0, 255,
                255, 255, 255,
            ],
            paletteAlpha: [255, 128, 64, 0]);

    /// <summary>
    /// Creates a PNG header only: signature plus IHDR, with no pixel data. Header-level rejections
    /// run before decode, so the image data is deliberately absent.
    /// </summary>
    /// <param name="width">The declared width.</param>
    /// <param name="height">The declared height.</param>
    /// <param name="bitDepth">The declared bit depth.</param>
    /// <param name="colorType">The declared PNG color type.</param>
    /// <returns>The truncated PNG bytes.</returns>
    internal static byte[] HeaderOnlyPng(
        uint width,
        uint height,
        byte bitDepth = 8,
        byte colorType = PngColorTypeRgb)
    {
        using var output = new MemoryStream();
        output.Write(_pngSignature);
        WriteChunk(output, "IHDR"u8, CreateIhdr(width, height, bitDepth, colorType));
        return output.ToArray();
    }

    /// <summary>
    /// Creates a 2×2 24-bit bottom-up BMP: top-left red, top-right green, bottom-left blue, and
    /// bottom-right white.
    /// </summary>
    /// <returns>The BMP bytes.</returns>
    internal static byte[] Bmp24()
    {
        const int width = 2;
        const int height = 2;
        int rowBytes = width * 3;
        int paddedRowBytes = (rowBytes + 3) / 4 * 4;
        int pixelBytes = paddedRowBytes * height;
        int fileSize = 54 + pixelBytes;

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(pixelBytes);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        // BMP rows are stored bottom-up for a positive height.
        for (int y = height - 1; y >= 0; y--)
        {
            writer.Write(_bmp24Pixels, y * rowBytes, rowBytes);
            for (int padding = rowBytes; padding < paddedRowBytes; padding++)
            {
                writer.Write((byte)0);
            }
        }

        writer.Flush();
        return output.ToArray();
    }

    /// <summary>
    /// Creates a BMP header only (no pixel data) with the declared dimensions.
    /// </summary>
    /// <param name="width">The declared width.</param>
    /// <param name="height">The declared height.</param>
    /// <returns>The truncated BMP bytes.</returns>
    internal static byte[] HeaderOnlyBmp(int width, int height)
    {
        var bmp = new byte[54];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteInt32LittleEndian(bmp.AsSpan(14, 4), 40);
        WriteInt32LittleEndian(bmp.AsSpan(18, 4), width);
        WriteInt32LittleEndian(bmp.AsSpan(22, 4), height);
        return bmp;
    }

    /// <summary>
    /// Creates a BITMAPCOREHEADER (DIB size 12) BMP header only: the 14-byte file header plus the
    /// 12-byte core header, with no pixel data or color table.
    /// </summary>
    /// <param name="width">The declared 16-bit width.</param>
    /// <param name="height">The declared 16-bit height.</param>
    /// <returns>The 26-byte truncated BMP bytes.</returns>
    internal static byte[] HeaderOnlyBmpCore(ushort width, ushort height)
    {
        var bmp = new byte[26];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteInt32LittleEndian(bmp.AsSpan(2, 4), 30);
        WriteInt32LittleEndian(bmp.AsSpan(10, 4), 26);
        WriteInt32LittleEndian(bmp.AsSpan(14, 4), 12);
        bmp[18] = (byte)(width & 0xFF);
        bmp[19] = (byte)(width >> 8);
        bmp[20] = (byte)(height & 0xFF);
        bmp[21] = (byte)(height >> 8);
        bmp[22] = 1;
        bmp[24] = 24;
        return bmp;
    }

    /// <summary>
    /// Creates a complete 24-bit BITMAPCOREHEADER BMP with the declared dimensions and black
    /// pixels.
    /// </summary>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The BMP bytes.</returns>
    internal static byte[] CoreBmp(ushort width = 1, ushort height = 1)
    {
        const int fileHeaderSize = 14;
        const int coreHeaderSize = 12;
        const int pixelOffset = fileHeaderSize + coreHeaderSize;
        int rowBytes = width * 3;
        int paddedRowBytes = (rowBytes + 3) / 4 * 4;
        int pixelBytes = paddedRowBytes * height;

        var bmp = new byte[pixelOffset + pixelBytes];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteInt32LittleEndian(bmp.AsSpan(2, 4), bmp.Length);
        WriteInt32LittleEndian(bmp.AsSpan(10, 4), pixelOffset);
        WriteInt32LittleEndian(bmp.AsSpan(14, 4), coreHeaderSize);
        bmp[18] = (byte)(width & 0xFF);
        bmp[19] = (byte)(width >> 8);
        bmp[20] = (byte)(height & 0xFF);
        bmp[21] = (byte)(height >> 8);
        bmp[22] = 1;
        bmp[24] = 24;
        return bmp;
    }

    /// <summary>
    /// Creates a TGA header only (no pixel data) with the declared dimensions.
    /// </summary>
    /// <param name="width">The declared width.</param>
    /// <param name="height">The declared height.</param>
    /// <returns>The truncated TGA bytes.</returns>
    internal static byte[] HeaderOnlyTga(ushort width, ushort height)
    {
        var tga = new byte[18];
        tga[2] = 2;
        tga[12] = (byte)(width & 0xFF);
        tga[13] = (byte)(width >> 8);
        tga[14] = (byte)(height & 0xFF);
        tga[15] = (byte)(height >> 8);
        tga[16] = 24;
        return tga;
    }

    /// <summary>
    /// Creates a 2×2 24-bit uncompressed TGA with the same pixel layout as <see cref="Bmp24"/>
    /// (top-left red, top-right green, bottom-left blue, bottom-right white).
    /// </summary>
    /// <param name="topLeftOrigin">Whether the TGA origin descriptor declares a top-left origin.</param>
    /// <returns>The TGA bytes.</returns>
    internal static byte[] Tga24(bool topLeftOrigin)
    {
        const int width = 2;
        const int height = 2;

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);

        var header = new byte[18];
        header[2] = 2;
        header[12] = (byte)(width & 0xFF);
        header[13] = (byte)(width >> 8);
        header[14] = (byte)(height & 0xFF);
        header[15] = (byte)(height >> 8);
        header[16] = 24;
        header[17] = topLeftOrigin ? (byte)0x20 : (byte)0x00;
        writer.Write(header);

        if (topLeftOrigin)
        {
            writer.Write(_bmp24Pixels);
        }
        else
        {
            for (int y = height - 1; y >= 0; y--)
            {
                writer.Write(_bmp24Pixels, y * width * 3, width * 3);
            }
        }

        writer.Flush();
        return output.ToArray();
    }

    /// <summary>
    /// Creates a valid 2×2 RLE-compressed (image type 10) TGA with a single red run. The compressed
    /// payload length cannot be derived from the header, so the pipeline must not reject it as
    /// truncated.
    /// </summary>
    /// <returns>The TGA bytes.</returns>
    internal static byte[] RleTga()
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);

        var header = new byte[18];
        header[2] = 10;
        header[12] = 2;
        header[14] = 2;
        header[16] = 24;
        header[17] = 0x20;
        writer.Write(header);

        writer.Write((byte)0x83);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)255);
        writer.Flush();
        return output.ToArray();
    }

    /// <summary>
    /// Returns a 2×2 solid red (220,30,30) JPEG encoded once and embedded as base64.
    /// </summary>
    /// <returns>The JPEG bytes.</returns>
    internal static byte[] TinyJpeg() => Convert.FromBase64String(TinyJpegBase64);

    private static byte[] CreateIhdr(uint width, uint height, byte bitDepth, byte colorType)
    {
        var ihdr = new byte[13];
        WriteUInt32BigEndian(ihdr.AsSpan(0, 4), width);
        WriteUInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = bitDepth;
        ihdr[9] = colorType;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        return ihdr;
    }

    private static byte[] CreatePng(
        int width,
        int height,
        byte colorType,
        byte bitDepth,
        byte[] scanlines,
        byte[]? palette,
        byte[]? paletteAlpha)
    {
        int rowBytes = scanlines.Length / height;

        using var output = new MemoryStream();
        output.Write(_pngSignature);
        WriteChunk(output, "IHDR"u8, CreateIhdr((uint)width, (uint)height, bitDepth, colorType));

        if (palette is not null)
        {
            WriteChunk(output, "PLTE"u8, palette);
        }

        if (paletteAlpha is not null)
        {
            WriteChunk(output, "tRNS"u8, paletteAlpha);
        }

        using var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(scanlines, y * rowBytes, rowBytes);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw.ToArray());
        }

        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        WriteUInt32BigEndian(stream, (uint)data.Length);
        stream.Write(type);
        stream.Write(data);

        uint crc = UpdateCrc(0xFFFFFFFFu, type);
        crc = UpdateCrc(crc, data);
        WriteUInt32BigEndian(stream, ~crc);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc;
    }

    private static void WriteUInt32BigEndian(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32BigEndian(Span<byte> buffer, uint value)
    {
        buffer[0] = (byte)(value >> 24);
        buffer[1] = (byte)(value >> 16);
        buffer[2] = (byte)(value >> 8);
        buffer[3] = (byte)value;
    }

    private static void WriteInt32LittleEndian(Span<byte> buffer, int value)
    {
        buffer[0] = (byte)value;
        buffer[1] = (byte)(value >> 8);
        buffer[2] = (byte)(value >> 16);
        buffer[3] = (byte)(value >> 24);
    }

    // A 2x2 solid red JPEG generated with the Windows GDI+ encoder; JPEG has no alpha, so every
    // decoded pixel is (220,30,30,255). Embedded because committing binary fixtures is forbidden.
    private const string TinyJpegBase64 =
        """
        /9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsK
        CwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQU
        FBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAACAAIDASIA
        AhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQA
        AAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3
        ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWm
        p6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEA
        AwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSEx
        BhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElK
        U1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3
        uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDxKiii
        vyo/v0//2Q==
        """;
}
