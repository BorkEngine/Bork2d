namespace Bork2d.Bake.Textures;

/// <summary>
/// Reads a texture source header before decoding so unsupported bit depths, impossible dimensions,
/// and derivable truncation fail deterministically without allocating pixels.
/// </summary>
/// <remarks>
/// <para>
/// PNG is identified by its signature and IHDR chunk, BMP by <c>BM</c> plus a known DIB header, and
/// TGA by a plausible 18-byte header (TGA has no magic). JPG and unknown formats are left to the
/// decoder for dimensions: a decoder failure is reported by <see cref="TextureSourceReader"/>.
/// </para>
/// <para>
/// The 16-bit policy is the approved Q-12 default: explicit 16-bit PNGs are rejected, while 8-bit
/// and lower PNG sources and decoder-normalized BMP/TGA/JPG sources are accepted.
/// </para>
/// <para>
/// Truncation is detected only where the header makes the byte layout derivable: uncompressed
/// BMP (BI_RGB, core or info header) and uncompressed TGA (image types 1, 2, 3). RLE, bitfield, and
/// JPEG-compressed BMP as well as RLE TGA are left to the decoder because their compressed payload
/// length cannot be derived from the header. A JPEG is truncated only when it contains no
/// end-of-image (<c>FF D9</c>) marker after the image data: trailing padding, appended metadata, or
/// concatenated data after the marker is allowed, so the marker is also searched backwards through
/// the file when it is not the final two bytes. No size policy cap is applied; this is corruption
/// detection (D-M3-06 is unaffected).
/// </para>
/// </remarks>
internal static class TextureSourceProbe
{
    private const int PngHeaderLength = 33;
    private const int PngWidthOffset = 16;
    private const int PngHeightOffset = 20;
    private const int PngBitDepthOffset = 24;
    private const int PngBitDepth16 = 16;
    private const int BmpPixelOffset = 10;
    private const int BmpWidthOffset = 18;
    private const int BmpHeightOffset = 22;
    private const int BmpCoreHeightOffset = 20;
    private const int BmpDibHeaderSizeOffset = 14;
    private const int BmpCoreBitsPerPixelOffset = 24;
    private const int BmpBitsPerPixelOffset = 28;
    private const int BmpCompressionOffset = 30;
    private const int BmpCoreDibHeaderSize = 12;
    private const int BmpInfoDibHeaderSize = 40;
    private const int TgaColorMapLengthOffset = 5;
    private const int TgaColorMapEntrySizeOffset = 7;
    private const int TgaWidthOffset = 12;
    private const int TgaHeightOffset = 14;
    private const int TgaBitsPerPixelOffset = 16;

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

    /// <summary>
    /// Finds a truncation problem for source formats whose byte layout is derivable from the header.
    /// </summary>
    /// <param name="stream">A seekable stream over the source file, used only for the JPEG fallback scan.</param>
    /// <param name="header">The first bytes of the source file.</param>
    /// <param name="trailer">The last (up to two) bytes of the source file.</param>
    /// <param name="fileLength">The source file length in bytes.</param>
    /// <param name="detail">The truncation detail when a problem was found, otherwise empty.</param>
    /// <returns><see langword="true"/> when the file is shorter than its header requires.</returns>
    internal static bool TryGetTruncationProblem(
        Stream stream,
        ReadOnlySpan<byte> header,
        ReadOnlySpan<byte> trailer,
        long fileLength,
        out string detail)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (TryGetBmpTruncation(header, fileLength, out detail)
            || TryGetTgaTruncation(header, fileLength, out detail))
        {
            return true;
        }

        return TryGetJpegTruncation(stream, header, trailer, fileLength, out detail);
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
            // BITMAPCOREHEADER: width and height are 16-bit fields at 18 and 20; offset 22 is the
            // planes field, not the height.
            width = ReadUInt16LittleEndian(header, BmpWidthOffset);
            height = ReadUInt16LittleEndian(header, BmpCoreHeightOffset);
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

    private static bool TryGetBmpTruncation(
        ReadOnlySpan<byte> header,
        long fileLength,
        out string detail)
    {
        detail = string.Empty;

        if (header.Length < 26 || header[0] != (byte)'B' || header[1] != (byte)'M')
        {
            return false;
        }

        int pixelOffset = ReadInt32LittleEndian(header, BmpPixelOffset);
        int dibHeaderSize = ReadInt32LittleEndian(header, BmpDibHeaderSizeOffset);

        int width;
        int height;
        int bitsPerPixel;
        int compression;
        if (dibHeaderSize == BmpCoreDibHeaderSize)
        {
            width = ReadUInt16LittleEndian(header, BmpWidthOffset);
            height = ReadUInt16LittleEndian(header, BmpCoreHeightOffset);
            bitsPerPixel = ReadUInt16LittleEndian(header, BmpCoreBitsPerPixelOffset);
            compression = 0;
        }
        else if (dibHeaderSize >= BmpInfoDibHeaderSize && header.Length >= 34)
        {
            width = ReadInt32LittleEndian(header, BmpWidthOffset);
            height = ReadInt32LittleEndian(header, BmpHeightOffset);
            bitsPerPixel = ReadUInt16LittleEndian(header, BmpBitsPerPixelOffset);
            compression = ReadInt32LittleEndian(header, BmpCompressionOffset);
        }
        else
        {
            return false;
        }

        // Only uncompressed BI_RGB has a derivable length; RLE/bitfield/embedded formats are left
        // to the decoder. Malformed fields are left to the decoder or the dimension probe.
        if (compression != 0
            || bitsPerPixel is not (1 or 4 or 8 or 16 or 24 or 32)
            || width <= 0
            || height == 0
            || pixelOffset < 0)
        {
            return false;
        }

        long rowBytes = (((long)width * bitsPerPixel + 31) / 32) * 4;
        long rowCount = height < 0 ? -(long)height : height;
        if (rowBytes != 0 && rowCount > (long.MaxValue - pixelOffset) / rowBytes)
        {
            // The dimension probe owns impossible sizes; do not risk an overflow here.
            return false;
        }

        long expectedLength = pixelOffset + (rowBytes * rowCount);
        if (fileLength >= expectedLength)
        {
            return false;
        }

        detail = $"the BMP header declares {expectedLength} bytes but the file has {fileLength}";
        return true;
    }

    private static bool TryGetTgaTruncation(
        ReadOnlySpan<byte> header,
        long fileLength,
        out string detail)
    {
        detail = string.Empty;

        if (header.Length < 18)
        {
            return false;
        }

        int imageType = header[2];
        int colorMapType = header[1];

        // Only uncompressed true-color, color-mapped, and grayscale images have a derivable
        // length; RLE image types (9/10/11) are left to the decoder.
        if (imageType is not (1 or 2 or 3))
        {
            return false;
        }

        if (imageType == 1 && colorMapType != 1)
        {
            return false;
        }

        int idLength = header[0];
        int colorMapLength = ReadUInt16LittleEndian(header, TgaColorMapLengthOffset);
        int colorMapEntrySize = header[TgaColorMapEntrySizeOffset];
        int width = ReadUInt16LittleEndian(header, TgaWidthOffset);
        int height = ReadUInt16LittleEndian(header, TgaHeightOffset);
        int bitsPerPixel = header[TgaBitsPerPixelOffset];

        if (width <= 0 || height <= 0 || bitsPerPixel is not (8 or 15 or 16 or 24 or 32))
        {
            return false;
        }

        long colorMapBytes = colorMapType == 1
            ? (long)colorMapLength * ((colorMapEntrySize + 7) / 8)
            : 0;
        long expectedLength = 18
            + idLength
            + colorMapBytes
            + ((long)width * height * ((bitsPerPixel + 7) / 8));
        if (fileLength >= expectedLength)
        {
            return false;
        }

        detail = $"the TGA header declares {expectedLength} bytes but the file has {fileLength}";
        return true;
    }

    private static bool TryGetJpegTruncation(
        Stream stream,
        ReadOnlySpan<byte> header,
        ReadOnlySpan<byte> trailer,
        long fileLength,
        out string detail)
    {
        detail = string.Empty;

        if (!IsJpeg(header))
        {
            return false;
        }

        // A valid JPEG may carry trailing bytes (padding, appended metadata, concatenated data)
        // after its end-of-image marker, so only a stream with no EOI marker at all is truncated.
        if (IsEndOfImage(trailer) || ContainsEndOfImage(stream, fileLength))
        {
            return false;
        }

        detail = "the JPEG stream has no end-of-image (FF D9) marker after the image data";
        return true;
    }

    private static bool IsJpeg(ReadOnlySpan<byte> header)
        => header.Length >= 3
            && header[0] == 0xFF
            && header[1] == 0xD8
            && header[2] == 0xFF;

    private static bool IsEndOfImage(ReadOnlySpan<byte> trailer)
        => trailer.Length >= 2 && trailer[0] == 0xFF && trailer[1] == 0xD9;

    private static bool ContainsEndOfImage(Stream stream, long fileLength)
    {
        const int chunkSize = 16 * 1024;
        byte[] buffer = new byte[chunkSize];

        long end = fileLength;
        while (end > 0)
        {
            long start = Math.Max(0, end - chunkSize);
            int count = (int)(end - start);
            stream.Position = start;
            int read = stream.ReadAtLeast(
                buffer.AsSpan(0, count),
                count,
                throwOnEndOfStream: false);

            for (int i = read - 1; i >= 1; i--)
            {
                if (buffer[i - 1] == 0xFF && buffer[i] == 0xD9)
                {
                    return true;
                }
            }

            if (start == 0)
            {
                break;
            }

            // Overlap one byte so a marker split across two chunks is still found.
            long nextEnd = start + 1;
            if (nextEnd >= end)
            {
                break;
            }

            end = nextEnd;
        }

        return false;
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
