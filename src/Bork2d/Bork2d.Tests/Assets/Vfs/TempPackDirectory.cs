using Bork2d.Engine.Assets.Pack;

namespace Bork2d.Tests.Assets.Vfs;

/// <summary>
/// A unique temporary directory for pack files. No binary fixture is committed and every test
/// disposes the directory after the file system that holds its packs.
/// </summary>
internal sealed class TempPackDirectory : IDisposable
{
    private readonly bool _relativeToCurrentDirectory;
    private bool _disposed;

    private TempPackDirectory(string rootPath, bool relativeToCurrentDirectory)
    {
        RootPath = rootPath;
        _relativeToCurrentDirectory = relativeToCurrentDirectory;
        Directory.CreateDirectory(rootPath);
    }

    internal string RootPath { get; }

    /// <summary>Creates a directory under the system temp path and uses absolute pack paths.</summary>
    internal static TempPackDirectory CreateUnderTemp()
        => new(
            Path.Combine(Path.GetTempPath(), $"bork2d-vfs-{Guid.NewGuid():N}"),
            relativeToCurrentDirectory: false);

    /// <summary>
    /// Creates a directory under the process current directory so a genuinely relative pack path can
    /// be computed without changing the current directory.
    /// </summary>
    internal static TempPackDirectory CreateUnderCurrentDirectory()
        => new(
            Path.Combine(Environment.CurrentDirectory, $"bork2d-vfs-{Guid.NewGuid():N}"),
            relativeToCurrentDirectory: true);

    internal string FullPath(string fileName) => Path.Combine(RootPath, fileName);

    internal string SuppliedPath(string fileName)
        => _relativeToCurrentDirectory
            ? Path.GetRelativePath(Environment.CurrentDirectory, FullPath(fileName))
            : FullPath(fileName);

    internal string WritePack(
        string fileName,
        Action<PackBuilder> configure,
        PackCompressionMode mode = PackCompressionMode.Debug)
    {
        var builder = new PackBuilder(mode);
        configure(builder);
        string fullPath = FullPath(fileName);
        builder.Write(fullPath);
        return fullPath;
    }

    internal string WriteBytes(string fileName, byte[] bytes)
    {
        string fullPath = FullPath(fileName);
        File.WriteAllBytes(fullPath, bytes);
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
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
