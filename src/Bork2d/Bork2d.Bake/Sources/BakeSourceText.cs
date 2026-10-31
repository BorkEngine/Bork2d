using System.Text;
using Bork2d.Bake.Diagnostics;

namespace Bork2d.Bake.Sources;

/// <summary>
/// Reads a source file as strict UTF-8, stripping an optional BOM.
/// </summary>
/// <remarks>
/// Invalid bytes are a <see cref="BakeDiagnosticCodes.SourceNotUtf8"/> diagnostic instead of
/// replacement characters, and read failures are a
/// <see cref="BakeDiagnosticCodes.SourceReadFailed"/> diagnostic, so a broken source never crashes a
/// scan or a bake. Discovery (metadata scanning) and binding share this path so both report the same
/// message for the same file.
/// </remarks>
internal static class BakeSourceText
{
    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>Reads a source as strict UTF-8 text.</summary>
    /// <param name="fullPath">The absolute source path.</param>
    /// <param name="displayPath">The relative path used in the diagnostic message.</param>
    /// <param name="diagnosticPath">The diagnostic path (project-relative or absolute).</param>
    /// <param name="assetPath">The canonical asset path, when known.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <param name="text">The decoded text when successful.</param>
    /// <returns><see langword="true"/> when the file was read and decoded.</returns>
    internal static bool TryRead(
        string fullPath,
        string displayPath,
        string diagnosticPath,
        string? assetPath,
        BakeDiagnostics diagnostics,
        out string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);
        ArgumentNullException.ThrowIfNull(diagnosticPath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        text = string.Empty;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SourceReadFailed,
                $"Failed to read '{displayPath}': {exception.Message}",
                diagnosticPath,
                assetPath: assetPath);
            return false;
        }

        ReadOnlySpan<byte> content = bytes;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            content = content[3..];
        }

        try
        {
            text = _strictUtf8.GetString(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SourceNotUtf8,
                $"'{displayPath}' is not valid UTF-8.",
                diagnosticPath,
                assetPath: assetPath);
            return false;
        }
    }
}
