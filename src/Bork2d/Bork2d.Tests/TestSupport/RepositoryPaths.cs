namespace Bork2d.Tests.TestSupport;

/// <summary>
/// Locates repository artifacts (the fixture assets executable, the build targets, and the build
/// configuration) from the test output directory.
/// </summary>
internal static class RepositoryPaths
{
    /// <summary>Gets the repository root directory.</summary>
    internal static string Root { get; } = FindRepositoryRoot();

    /// <summary>Gets the test build configuration (<c>Debug</c> or <c>Release</c>).</summary>
    internal static string Configuration { get; } = FindConfiguration();

    /// <summary>Gets the fixture assets executable project directory.</summary>
    internal static string FixtureAssetsProjectDirectory
        => Path.Combine(Root, "src", "Bork2d", "Bork2d.BakeFixture.Assets");

    /// <summary>Gets the built fixture assets executable assembly path.</summary>
    internal static string FixtureAssetsExecutable
        => Path.Combine(
            FixtureAssetsProjectDirectory,
            "bin",
            Configuration,
            "net11.0",
            "Bork2d.BakeFixture.Assets.dll");

    /// <summary>Gets the assets build target file.</summary>
    internal static string AssetsTargets
        => Path.Combine(Root, "build", "Bork2d.Assets.targets");

    /// <summary>Gets the assets copy target file.</summary>
    internal static string AssetsCopyTargets
        => Path.Combine(Root, "build", "Bork2d.Assets.Copy.targets");

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

    private static string FindConfiguration()
    {
        string output = Path.GetFullPath(AppContext.BaseDirectory);
        foreach (string part in output.Split(Path.DirectorySeparatorChar))
        {
            if (string.Equals(part, "Release", StringComparison.OrdinalIgnoreCase))
            {
                return "Release";
            }
        }

        return "Debug";
    }
}
