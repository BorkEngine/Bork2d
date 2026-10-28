using Bork2d.Bake;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Pong.Assets.Processors;
using Pong.Common;

namespace Pong.Tests.TestSupport;

/// <summary>
/// A unique temporary project root containing a copy of the real <c>Pong.Assets/Assets</c> tree and a
/// separate output root for the baked pack.
/// </summary>
/// <remarks>
/// Every instance uses its own GUID-named directory, so the bake/load tests are parallel-safe and
/// never touch the committed assets. Disposal removes the copied tree and every bake artifact.
/// </remarks>
internal sealed class PongTestProject : IDisposable
{
    private const string PackName = "assets.pck";
    private const string AssetsRoot = "Assets";

    private static readonly string _sourceAssetsPath = Path.Combine(
        FindRepositoryRoot(),
        "examples",
        "pong",
        "Pong.Assets",
        AssetsRoot);

    private readonly string _basePath;
    private bool _disposed;

    private PongTestProject(string basePath)
    {
        _basePath = basePath;
        ProjectRoot = Path.Combine(basePath, "project");
        OutputRoot = Path.Combine(basePath, "output");
        Directory.CreateDirectory(ProjectRoot);
        Directory.CreateDirectory(OutputRoot);
        CopyDirectory(_sourceAssetsPath, Path.Combine(ProjectRoot, AssetsRoot));
    }

    /// <summary>Gets the temporary project root.</summary>
    internal string ProjectRoot { get; }

    /// <summary>Gets the temporary bake output root.</summary>
    internal string OutputRoot { get; }

    /// <summary>Gets the expected path of the baked main pack.</summary>
    internal string PackPath => Path.Combine(OutputRoot, PackName);

    /// <summary>Creates a temp project root containing a fresh copy of the real asset tree.</summary>
    internal static PongTestProject Create()
        => new(Path.Combine(Path.GetTempPath(), $"pong-tests-{Guid.NewGuid():N}"));

    /// <summary>Overwrites a text file under the copied asset root.</summary>
    internal void WriteAsset(string relativePath, string content)
    {
        string fullPath = Path.Combine(
            ProjectRoot,
            AssetsRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    /// <summary>Bakes the copied assets with the same configuration as <c>Pong.Assets</c>.</summary>
    /// <returns>The process exit code of the bake command.</returns>
    internal Task<int> BakeAsync()
    {
        string[] args =
        [
            "bake",
            "--project-root",
            ProjectRoot,
            "--output-root",
            OutputRoot,
            "--mode",
            "debug",
        ];

        return new BakeApplicationBuilder()
            .WithProjectRoot(ProjectRoot)
            .WithPack(PackName, AssetsRoot)
            .UseDefaultAssets()
            .RegisterAsset<GameSettings>()
            .UseDefaultProcessors()
            .WithTextureDefaults(TextureFilter.Linear, TextureWrap.Clamp)
            .AddProcessor(new CircleProcessor())
            .Build()
            .RunAsync(args);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (Directory.Exists(_basePath))
            {
                Directory.Delete(_basePath, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed test can leave a pack handle open; the OS temp cleaner owns the rest.
        }
    }

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
            "The Pong tests could not locate the repository root (build/Bork2d.Assets.targets).");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
