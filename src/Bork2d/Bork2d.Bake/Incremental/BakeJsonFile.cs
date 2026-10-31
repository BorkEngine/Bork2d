using System.Text.Json;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// Reads and writes the internal incremental JSON files deterministically.
/// </summary>
/// <remarks>
/// <para>
/// The tooling writes these files itself, so the reader is intentionally narrow: only the documented
/// fields are read, extra properties are ignored for forward compatibility, and any missing or
/// mistyped field makes the file unusable (the caller re-bakes or regenerates instead of failing).
/// </para>
/// <para>
/// Writes are atomic: a unique temporary file in the destination directory is replaced over the
/// target, so a reader never observes a half-written manifest or state file. The text uses LF and
/// UTF-8 without a BOM, so two writes of the same model produce the same bytes.
/// </para>
/// </remarks>
internal static class BakeJsonFile
{
    /// <summary>Tries to read a JSON file's bytes.</summary>
    /// <param name="path">The absolute file path.</param>
    /// <returns>The bytes, or <see langword="null"/> when the file is missing or unreadable.</returns>
    internal static byte[]? TryReadAllBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            return null;
        }
    }

    /// <summary>Writes bytes atomically to a path.</summary>
    /// <param name="path">The absolute target path.</param>
    /// <param name="bytes">The bytes to write.</param>
    /// <returns><see langword="true"/> when the target now holds the bytes.</returns>
    internal static bool TryWriteAtomically(string path, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);

        string? tempPath = null;
        try
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory;
            Directory.CreateDirectory(directory);
            tempPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, fullPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            return false;
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>Creates a JSON document parse from bytes.</summary>
    /// <param name="bytes">The file bytes.</param>
    /// <returns>The document, or <see langword="null"/> when the bytes are not valid JSON.</returns>
    internal static JsonDocument? TryParse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
        }
    }

    private static bool IsIoFailure(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException;
}
