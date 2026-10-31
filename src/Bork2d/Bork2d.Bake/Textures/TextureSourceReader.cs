namespace Bork2d.Bake.Textures;

/// <summary>
/// The single texture decode/probe implementation: reads one source file, applies the header and
/// truncation probes, decodes to straight-alpha RGBA8, and validates dimensions and payload size.
/// </summary>
/// <remarks>
/// <para>
/// The reader is used by the built-in texture processor and directly by tests that exercise the decode
/// matrix. It reports every failure through the supplied error callback with a stable
/// <c>BK4xxx</c> code: read failure (<c>BK4001</c>), decode failure (<c>BK4003</c>), 16-bit PNG
/// (<c>BK4004</c>), unsupported dimensions (<c>BK4005</c>), out-of-memory (<c>BK4013</c>), payload-size
/// mismatch (<c>BK4014</c>), and truncation (<c>BK4015</c>). A failed decode never produces a partial
/// payload.
/// </para>
/// <para>
/// Decoding is StbImageSharp to straight alpha: no premultiplication, gamma, ICC, flipping, or
/// resampling. The display path is used only in messages; the caller supplies the diagnostic file
/// location.
/// </para>
/// </remarks>
internal static class TextureSourceReader
{
    private const int HeaderByteCount = 64;

    /// <summary>
    /// Decodes one texture source with the shared StbImageSharp decoder.
    /// </summary>
    /// <param name="sourcePath">The absolute source path.</param>
    /// <param name="displayPath">The path used in diagnostic messages (normally the source's relative path).</param>
    /// <param name="error">The callback that receives a <c>BK4xxx</c> code and message.</param>
    /// <param name="payload">The RGBA8 payload when decoding succeeded, otherwise an empty array.</param>
    /// <param name="width">The decoded width when decoding succeeded, otherwise zero.</param>
    /// <param name="height">The decoded height when decoding succeeded, otherwise zero.</param>
    /// <returns><see langword="true"/> when the source decoded and validated successfully.</returns>
    internal static bool TryDecode(
        string sourcePath,
        string displayPath,
        Action<string, string> error,
        out byte[] payload,
        out int width,
        out int height)
        => TryDecode(
            sourcePath,
            displayPath,
            StbTextureDecoder.Instance,
            error,
            out payload,
            out width,
            out height);

    /// <summary>
    /// Decodes one texture source with an explicit decoder seam.
    /// </summary>
    /// <remarks>
    /// The seam exists only so tests can exercise the probe/dimension/payload validation paths with a
    /// controlled decoder; production always uses the shared StbImageSharp instance.
    /// </remarks>
    /// <param name="sourcePath">The absolute source path.</param>
    /// <param name="displayPath">The path used in diagnostic messages.</param>
    /// <param name="decoder">The decoder implementation.</param>
    /// <param name="error">The callback that receives a <c>BK4xxx</c> code and message.</param>
    /// <param name="payload">The RGBA8 payload when decoding succeeded, otherwise an empty array.</param>
    /// <param name="width">The decoded width when decoding succeeded, otherwise zero.</param>
    /// <param name="height">The decoded height when decoding succeeded, otherwise zero.</param>
    /// <returns><see langword="true"/> when the source decoded and validated successfully.</returns>
    internal static bool TryDecode(
        string sourcePath,
        string displayPath,
        ITextureDecoder decoder,
        Action<string, string> error,
        out byte[] payload,
        out int width,
        out int height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(error);

        payload = [];
        width = 0;
        height = 0;

        FileStream stream;
        try
        {
            stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            AddReadFailure(error, displayPath, exception);
            return false;
        }

        using (stream)
        {
            try
            {
                byte[] header = new byte[HeaderByteCount];
                int headerLength = stream.ReadAtLeast(
                    header,
                    header.Length,
                    throwOnEndOfStream: false);

                if (TextureSourceProbe.TryGetHeaderProblem(
                        header.AsSpan(0, headerLength),
                        out string code,
                        out long probeWidth,
                        out long probeHeight))
                {
                    error(
                        code,
                        BuildHeaderProblemMessage(displayPath, code, probeWidth, probeHeight));
                    return false;
                }

                long fileLength = stream.Length;
                Span<byte> trailer = stackalloc byte[2];
                int trailerLength = ReadTrailer(stream, fileLength, trailer);

                if (TextureSourceProbe.TryGetTruncationProblem(
                        stream,
                        header.AsSpan(0, headerLength),
                        trailer[..trailerLength],
                        fileLength,
                        out string truncationDetail))
                {
                    error(
                        TextureDiagnosticCodes.TextureTruncated,
                        $"Texture source '{displayPath}' is truncated: {truncationDetail}.");
                    return false;
                }

                stream.Position = 0;
                DecodedImage image = decoder.Decode(stream);

                if (!TextureSourceProbe.IsSupportedSize(image.Width, image.Height))
                {
                    error(
                        TextureDiagnosticCodes.TextureDimensions,
                        $"Texture source '{displayPath}' decoded to unsupported dimensions "
                            + $"{image.Width}x{image.Height}; width and height must be positive and "
                            + "the RGBA8 payload must fit in one array.");
                    return false;
                }

                long expectedLength = (long)image.Width * image.Height * 4;
                if (image.Pixels.Length != expectedLength)
                {
                    error(
                        TextureDiagnosticCodes.TexturePayloadSize,
                        $"Texture source '{displayPath}' decoded to {image.Pixels.Length} "
                            + $"RGBA8 bytes; expected {expectedLength} (width × height × 4).");
                    return false;
                }

                width = image.Width;
                height = image.Height;
                payload = image.Pixels;
                return true;
            }
            catch (OutOfMemoryException)
            {
                error(
                    TextureDiagnosticCodes.TextureOutOfMemory,
                    $"Texture source '{displayPath}' could not be decoded: the process ran out "
                        + "of memory.");
                return false;
            }
            catch (Exception exception) when (IsReadFailure(exception))
            {
                AddReadFailure(error, displayPath, exception);
                return false;
            }
            catch (Exception exception)
            {
                // The third-party decoder may surface malformed input as any exception type; the
                // bake must turn every such failure into a source diagnostic, never a crash.
                error(
                    TextureDiagnosticCodes.TextureDecode,
                    $"Texture source '{displayPath}' could not be decoded: {exception.Message}");
                return false;
            }
        }
    }

    private static string BuildHeaderProblemMessage(
        string displayPath,
        string code,
        long width,
        long height)
        => code == TextureDiagnosticCodes.TextureBitDepth
            ? $"Texture source '{displayPath}' is a 16-bit PNG; only PNG sources with a bit depth of "
                + "8 or lower are supported."
            : $"Texture source '{displayPath}' declares unsupported dimensions {width}x{height}; "
                + "width and height must be positive and the RGBA8 payload must fit in one array.";

    private static void AddReadFailure(
        Action<string, string> error,
        string displayPath,
        Exception exception)
        => error(
            TextureDiagnosticCodes.TextureSourceRead,
            $"Failed to read texture source '{displayPath}': {exception.Message}");

    private static int ReadTrailer(Stream stream, long fileLength, Span<byte> trailer)
    {
        if (fileLength < trailer.Length)
        {
            return 0;
        }

        stream.Position = fileLength - trailer.Length;
        return stream.ReadAtLeast(trailer, trailer.Length, throwOnEndOfStream: false);
    }

    private static bool IsReadFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException;
}
