using Bork2d.Api;
using Bork2d.Assets.Pack;
using Bork2d.Bake.Diagnostics;

namespace Bork2d.Bake.Baking;

/// <summary>
/// Writes a composed pack atomically: a unique temporary file in the destination directory is fully
/// written first, then replaces the target in one operation.
/// </summary>
/// <remarks>
/// <para>
/// A failed bake must leave the previous pack unchanged (M4 §10.1). The temporary file is created in
/// the destination directory so the final <see cref="File.Replace(string, string, string?)"/> or
/// <see cref="File.Move(string, string)"/> stays on one volume and is atomic; it is deleted on any
/// failure, including a <c>PackBuilder</c> write failure.
/// </para>
/// <para>
/// <c>PackBuilder</c> remains the only pack writer: this helper only chooses the path and performs the
/// replacement.
/// </para>
/// </remarks>
internal static class AtomicPackWriter
{
    /// <summary>Writes the pack to a temporary file and atomically replaces the target.</summary>
    /// <param name="packPath">The absolute pack target.</param>
    /// <param name="builder">The composed pack builder.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns><see langword="true"/> when the target now holds the new pack.</returns>
    internal static bool Write(
        string packPath,
        PackBuilder builder,
        BakeDiagnostics diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packPath);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(diagnostics);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(packPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.PackWriteFailed,
                $"Invalid pack path '{packPath}': {exception.Message}",
                packPath);
            return false;
        }

        string directory = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory;
        string tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(directory);
            builder.Write(tempPath);

            if (File.Exists(fullPath))
            {
                File.Replace(
                    tempPath,
                    fullPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException or Bork2dException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.PackWriteFailed,
                $"Failed to write the asset pack '{fullPath}': {exception.Message}",
                fullPath);
            return false;
        }
        finally
        {
            // The target is only replaced after a successful temporary write; anything left over is
            // always removed so a failed bake leaves no stray file.
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
