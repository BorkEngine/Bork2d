namespace Bork2d.Tests.TestSupport;

/// <summary>
/// Locates the repository root from the test output directory.
/// </summary>
internal static class RepositoryPaths
{
    /// <summary>Gets the repository root directory.</summary>
    internal static string Root { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "build", "Bork2d.Assets.targets")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "The test could not locate the repository root (build/Bork2d.Assets.targets).");
    }
}
