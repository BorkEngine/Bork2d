using Bork2d.Bake.Diagnostics;
using Bork2d.Engine;
using Bork2d.Engine.Assets.Pack;

namespace Bork2d.Bake.Baking;

/// <summary>
/// Stages every validated pack in a unique temporary file next to its target and publishes the whole
/// set only after every target has been staged.
/// </summary>
/// <remarks>
/// <para>
/// Each pack is composed into a <see cref="PackBuilder"/>, which is the only pack writer. Staging
/// writes the complete pack to a unique <c>*.tmp</c> file in the destination directory and then
/// releases the builder, so staged bytes live on disk and the pipeline never keeps more than one
/// composed pack in memory. The transaction stores staging paths only.
/// </para>
/// <para>
/// <see cref="Commit"/> replaces every target in configuration order: an existing target is moved to a
/// unique <c>*.bak</c> backup, then the staged file is moved onto the target. If a replacement fails,
/// every already-replaced target is restored from its backup and the failure is reported with
/// <see cref="BakeDiagnosticCodes.PackWriteFailed"/> at the failing path, so a normal write failure
/// leaves every target byte-identical.
/// </para>
/// <para>
/// A process crash between a target-to-backup move and the matching staged-to-target move cannot be
/// rolled back by the baker. In that window the backup holds the previous pack and the staged file
/// holds the new one; those files are the only recovery source and can be left behind after a crash.
/// <see cref="Dispose"/> removes staging files that remain after a failed stage or commit; a backup is
/// deleted after a successful replacement or moved back during rollback, and a backup whose restore
/// failed is intentionally kept because it is the only remaining copy of the previous pack.
/// </para>
/// </remarks>
internal sealed class PackPublishTransaction : IDisposable
{
    private readonly List<StagedPack> _staged = [];
    private readonly List<ReplacedPack> _replaced = [];
    private bool _disposed;

    /// <summary>Gets the number of packs staged so far and not yet committed.</summary>
    internal int StagedPackCount => _staged.Count;

    /// <summary>Writes one complete pack to a unique staging file beside its target.</summary>
    /// <param name="packPath">The absolute pack target.</param>
    /// <param name="builder">The composed pack builder; it is released when this method returns.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns><see langword="true"/> when the pack is fully staged.</returns>
    internal bool Stage(
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
        string stagingPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(directory);
            builder.Write(stagingPath);
        }
        catch (Exception exception) when (IsWriteFailure(exception))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.PackWriteFailed,
                $"Failed to write the asset pack '{fullPath}': {exception.Message}",
                fullPath);
            DeleteFile(stagingPath);
            return false;
        }

        _staged.Add(new StagedPack(fullPath, stagingPath));
        return true;
    }

    /// <summary>Publishes every staged pack, replacing each target through its own backup.</summary>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <param name="failedPackIndex">
    /// The zero-based staged order of the pack that failed, or <c>-1</c> when every target was
    /// replaced. The staged order is the pipeline's pack order.
    /// </param>
    /// <returns><see langword="true"/> when every target now holds its new pack.</returns>
    internal bool Commit(BakeDiagnostics diagnostics, out int failedPackIndex)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        failedPackIndex = -1;

        for (int index = 0; index < _staged.Count; index++)
        {
            StagedPack staged = _staged[index];
            try
            {
                if (File.Exists(staged.TargetPath))
                {
                    string backupPath = BackupPathFor(staged.TargetPath);
                    File.Move(staged.TargetPath, backupPath);
                    _replaced.Add(new ReplacedPack(staged.TargetPath, backupPath));
                }

                File.Move(staged.StagingPath, staged.TargetPath);
            }
            catch (Exception exception) when (IsWriteFailure(exception))
            {
                RollBack();
                failedPackIndex = index;
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.PackWriteFailed,
                    $"Failed to write the asset pack '{staged.TargetPath}': {exception.Message}",
                    staged.TargetPath);
                return false;
            }
        }

        foreach (ReplacedPack replaced in _replaced)
        {
            DeleteFile(replaced.BackupPath);
        }

        _replaced.Clear();
        _staged.Clear();
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (StagedPack staged in _staged)
        {
            DeleteFile(staged.StagingPath);
        }

        _staged.Clear();
        _replaced.Clear();
    }

    private void RollBack()
    {
        for (int index = _replaced.Count - 1; index >= 0; index--)
        {
            ReplacedPack replaced = _replaced[index];
            try
            {
                // The new pack may already be in place when a later target fails; drop it first so
                // the previous bytes can be restored.
                if (File.Exists(replaced.TargetPath))
                {
                    File.Delete(replaced.TargetPath);
                }

                if (File.Exists(replaced.BackupPath))
                {
                    File.Move(replaced.BackupPath, replaced.TargetPath);
                }
            }
            catch (Exception exception) when (IsWriteFailure(exception))
            {
                // Keep the backup on disk: it is the only remaining copy of the previous pack.
                continue;
            }

            _replaced.RemoveAt(index);
        }
    }

    private static string BackupPathFor(string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory;
        return Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.bak");
    }

    private static bool IsWriteFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or Bork2dException;

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed cleanup must not hide the primary result; the OS temp cleaner owns the rest.
        }
    }

    private sealed record StagedPack(string TargetPath, string StagingPath);

    private sealed record ReplacedPack(string TargetPath, string BackupPath);
}
