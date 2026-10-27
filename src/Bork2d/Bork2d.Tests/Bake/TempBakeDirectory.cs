namespace Bork2d.Tests.Bake;

/// <summary>
/// A unique temporary directory for bake-tooling tests. No fixture file is committed.
/// </summary>
internal sealed class TempBakeDirectory : IDisposable
{
    private bool _disposed;

    private TempBakeDirectory(string rootPath)
    {
        RootPath = rootPath;
        Directory.CreateDirectory(rootPath);
    }

    internal string RootPath { get; }

    internal static TempBakeDirectory Create()
        => new(Path.Combine(Path.GetTempPath(), $"bork2d-bake-{Guid.NewGuid():N}"));

    /// <summary>
    /// Creates a directory under the process current directory so a genuinely relative CLI path can
    /// be exercised without changing the process current directory.
    /// </summary>
    internal static TempBakeDirectory CreateUnderCurrentDirectory()
        => new(Path.Combine(Environment.CurrentDirectory, $"bork2d-bake-{Guid.NewGuid():N}"));

    internal string FullPath(string relativePath)
        => Path.GetFullPath(Path.Combine(RootPath, relativePath));

    internal string CreateDirectory(string relativePath)
    {
        string fullPath = FullPath(relativePath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    internal string WriteFile(string relativePath, string content)
    {
        string fullPath = FullPath(relativePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Directory.Exists(RootPath))
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch (IOException)
            {
                // A failed test can leave a pack handle open; the OS temp cleaner owns the rest.
            }
        }
    }
}
