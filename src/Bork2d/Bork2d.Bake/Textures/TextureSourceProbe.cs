namespace Bork2d.Bake.Textures;

/// <summary>
/// Reads a texture source header before decoding so unsupported bit depths and impossible
/// dimensions fail deterministically without allocating pixels.
/// </summary>
/// <remarks>
/// <para>
/// PNG is identified by its signature and IHDR chunk, BMP by <c>BM</c> plus a known DIB header, and
/// TGA by a plausible 18-byte header (TGA has no magic). JPG and unknown formats are left to the
/// decoder: their dimensions are validated after decode, and a decoder failure is reported by the
/// pipeline.
/// </para>
/// <para>
/// The 16-bit policy is the approved Q-12 default: explicit 16-bit PNGs are rejected, while 8-bit
/// and lower PNG sources and decoder-normalized BMP/TGA/JPG sources are accepted.
/// </para>
/// </remarks>
internal static class TextureSourceProbe
{
    private const int PngHeaderLength = 33;
    private const int PngWidthOffset = 16;
    private const int PngHeightOffset = 20;
    private const int PngBitDepthOffset = 24;
    private const int PngBitDepth16 = 16;
    private const int BmpWidthOffset = 18;
    private const int BmpHeightOffset = 22;
    private const int BmpDibHeaderSizeOffset = 14;
    private const int BmpCoreDibHeaderSize = 12;
    private const int BmpInfoDibHeaderSize = 40;
    private const int TgaWidthOffset = 12;
    private const int TgaHeightOffset = 14;

    private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Finds a header-level problem that must be rejected before decoding.
    /// </summary>
    /// <param name="header">The first bytes of the source file (at most the header read size).</param>
    /// <param name="code">The <c>BK4xxx</c> code when a problem was found, otherwise empty.</param>
    /// <param name="width">The declared width when a problem was found, otherwise zero.</param>
    /// <param name="height">The declared height when a problem was found, otherwise zero.</param>
    /// <returns><see langword="true"/> when the source must not be decoded.</returns>
    internal static bool TryGetHeaderProblem(
        ReadOnlySpan<byte> header,
        out string code,
        out long width,
        out long height)
    {
        code = string.Empty;
        width = 0;
        height = 0;

        if (TryReadPng(header, out long pngWidth, out long pngHeight, out int bitDepth))
        {
            if (bitDepth == PngBitDepth16)
            {
                code = TextureDiagnosticCodes.TextureBitDepth;
                width = pngWidth;
                height = pngHeight;
                return true;
            }

            if (!IsSupportedSize(pngWidth, pngHeight))
            {
                code = TextureDiagnosticCodes.TextureDimensions;
                width = pngWidth;
                height = pngHeight;
                return true;
            }

            return false;
        }

        if (TryReadBmp(header, out long bmpWidth, out long bmpHeight))
        {
            if (!IsSupportedSize(bmpWidth, bmpHeight))
            {
                code = TextureDiagnosticCodes.TextureDimensions;
                width = bmpWidth;
                height = bmpHeight;
                return true;
            }

            return false;
        }

        if (TryReadTga(header, out long tgaWidth, out long tgaHeight)
            && !IsSupportedSize(tgaWidth, tgaHeight))
        {
            code = TextureDiagnosticCodes.TextureDimensions;
            width = tgaWidth;
            height = tgaHeight;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tests whether an image size fits the RGBA8 payload contract: positive, at most
    /// <see cref="int.MaxValue"/> per axis, and no larger than a single array can hold.
    /// </summary>
    /// <param name="width">The pixel width.</param>
    /// <param name="height">The pixel height.</param>
    /// <returns><see langword="true"/> when the size is supported.</returns>
    internal static bool IsSupportedSize(long width, long height)
    {
        if (width <= 0 || height <= 0 || width > int.MaxValue || height > int.MaxValue)
        {
            return false;
        }

        // Divide instead of multiplying: width*height*4 can overflow 64 bits at the int limits.
        return height <= Array.MaxLength / 4 / width;
    }

    private static bool TryReadPng(
        ReadOnlySpan<byte> header,
        out long width,
        out long height,
        out int bitDepth)
    {
        width = 0;
        height = 0;
        bitDepth = 0;

        if (header.Length < PngHeaderLength
            || !header[.._pngSignature.Length].SequenceEqual(_pngSignature)
            || !header.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        width = ReadBigEndianUInt32(header, PngWidthOffset);
        height = ReadBigEndianUInt32(header, PngHeightOffset);
        bitDepth = header[PngBitDepthOffset];
        return true;
    }

    private static bool TryReadBmp(ReadOnlySpan<byte> header, out long width, out long height)
    {
        width = 0;
        height = 0;

        if (header.Length < 26 || header[0] != (byte)'B' || header[1] != (byte)'M')
        {
            return false;
        }

        int dibHeaderSize = ReadInt32LittleEndian(header, BmpDibHeaderSizeOffset);

        if (dibHeaderSize == BmpCoreDibHeaderSize && header.Length >= 22)
        {
            width = ReadUInt16LittleEndian(header, BmpWidthOffset);
            height = ReadUInt16LittleEndian(header, BmpHeightOffset);
            return true;
        }

        if (dibHeaderSize < BmpInfoDibHeaderSize)
        {
            return false;
        }

        int signedWidth = ReadInt32LittleEndian(header, BmpWidthOffset);
        int signedHeight = ReadInt32LittleEndian(header, BmpHeightOffset);

        // A negative height is the top-down BMP convention; its magnitude is the row count.
        long heightMagnitude = signedHeight < 0 ? -(long)signedHeight : signedHeight;
        width = signedWidth;
        height = heightMagnitude;
        return true;
    }

    private static bool TryReadTga(ReadOnlySpan<byte> header, out long width, out long height)
    {
        width = 0;
        height = 0;

        if (header.Length < 18)
        {
            return false;
        }

        byte imageType = header[2];
        byte colorMapType = header[1];
        byte bitsPerPixel = header[16];
        bool isTrueColorOrGray = imageType is 1 or 2 or 3 or 9 or 10 or 11;
        bool hasPlausibleColorMap = colorMapType is 0 or 1;
        bool hasPlausibleDepth = bitsPerPixel is 8 or 15 or 16 or 24 or 32;

        if (!isTrueColorOrGray || !hasPlausibleColorMap || !hasPlausibleDepth)
        {
            return false;
        }

        width = ReadUInt16LittleEndian(header, TgaWidthOffset);
        height = ReadUInt16LittleEndian(header, TgaHeightOffset);
        return true;
    }

    private static uint ReadBigEndianUInt32(ReadOnlySpan<byte> buffer, int offset)
        => ((uint)buffer[offset] << 24)
            | ((uint)buffer[offset + 1] << 16)
            | ((uint)buffer[offset + 2] << 8)
            | buffer[offset + 3];

    private static int ReadInt32LittleEndian(ReadOnlySpan<byte> buffer, int offset)
        => buffer[offset]
            | (buffer[offset + 1] << 8)
            | (buffer[offset + 2] << 16)
            | (buffer[offset + 3] << 24);

    private static int ReadUInt16LittleEndian(ReadOnlySpan<byte> buffer, int offset)
        => buffer[offset] | (buffer[offset + 1] << 8);
}
