using Xunit;

namespace Bork2d.Tests.TestSupport;

/// <summary>
/// Marks a test that needs symbolic-link/reparse-point creation, which Windows allows only in
/// Developer Mode or from an elevated process.
/// </summary>
/// <remarks>
/// The capability probe creates and removes a file link and a directory link under the system temp
/// directory at discovery time. When the OS/user cannot create either, the test is skipped with a
/// documented reason instead of failing.
/// </remarks>
public sealed class SymlinkFactAttribute : FactAttribute
{
    /// <summary>The documented skip reason.</summary>
    internal const string SkipReason =
        "This OS/user cannot create symbolic links (reparse points); enable Windows Developer "
        + "Mode or run elevated to cover reparse-point handling.";

    /// <summary>Initializes a new instance of the <see cref="SymlinkFactAttribute"/> class.</summary>
    public SymlinkFactAttribute()
    {
        if (!CanCreateSymbolicLinks())
        {
            Skip = SkipReason;
        }
    }

    private static bool CanCreateSymbolicLinks()
    {
        string root = Path.Combine(Path.GetTempPath(), $"bork2d-symlink-probe-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            string targetDirectory = Path.Combine(root, "target-dir");
            Directory.CreateDirectory(targetDirectory);
            string targetFile = Path.Combine(root, "target.txt");
            File.WriteAllText(targetFile, "probe");

            Directory.CreateSymbolicLink(Path.Combine(root, "link-dir"), targetDirectory);
            File.CreateSymbolicLink(Path.Combine(root, "link.txt"), targetFile);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // The OS temp cleaner owns a probe that could not be removed.
            }
        }
    }
}
