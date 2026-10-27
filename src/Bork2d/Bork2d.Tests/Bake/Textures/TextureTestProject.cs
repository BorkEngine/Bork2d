using Bork2d.Bake.Baking;
using Bork2d.Bake.Textures;

namespace Bork2d.Tests.Bake.Textures;

/// <summary>
/// A temporary project with an <c>Assets</c> root and a validated project configuration for texture
/// tests.
/// </summary>
/// <remarks>
/// Paths passed to the write helpers are relative to the asset root (<c>textures/player.png</c>), and
/// <see cref="DisplayPath"/> returns the project-relative form diagnostics use
/// (<c>Assets/textures/player.png</c>).
/// </remarks>
internal sealed class TextureTestProject : IDisposable
{
    private const string AssetsDirectoryName = "Assets";

    private readonly TempBakeDirectory _directory;

    private TextureTestProject(TempBakeDirectory directory, BakeProject project)
    {
        _directory = directory;
        Project = project;
    }

    /// <summary>Gets the validated project configuration.</summary>
    internal BakeProject Project { get; }

    /// <summary>Gets the project root (the temp directory).</summary>
    internal string RootPath => _directory.RootPath;

    /// <summary>Creates a temporary project with the given texture defaults.</summary>
    /// <param name="filter">The default filter.</param>
    /// <param name="wrap">The default wrap mode.</param>
    /// <returns>The test project.</returns>
    internal static TextureTestProject Create(
        BakeTextureFilter filter = BakeTextureFilter.Linear,
        BakeTextureWrap wrap = BakeTextureWrap.Clamp)
    {
        TempBakeDirectory directory = TempBakeDirectory.Create();
        string assetsRoot = directory.CreateDirectory(AssetsDirectoryName);
        var pack = new BakePack(
            index: 0,
            assetsRoot,
            relativeRoot: AssetsDirectoryName,
            packPath: "assets.pck");
        var project = new BakeProject(
            directory.RootPath,
            [pack],
            filter,
            wrap,
            new Dictionary<string, BakeAssetTypeRegistration>(StringComparer.Ordinal),
            [],
            useDefaultProcessors: false);

        return new TextureTestProject(directory, project);
    }

    /// <summary>Writes a texture source or sidecar byte array under the asset root.</summary>
    /// <param name="relativePath">The asset-root-relative path.</param>
    /// <param name="bytes">The file contents.</param>
    /// <returns>The absolute file path.</returns>
    internal string WriteImage(string relativePath, byte[] bytes)
    {
        string fullPath = FullPath(relativePath);
        CreateParentDirectory(fullPath);
        File.WriteAllBytes(fullPath, bytes);
        return fullPath;
    }

    /// <summary>Writes a text file (normally a sidecar) under the asset root.</summary>
    /// <param name="relativePath">The asset-root-relative path.</param>
    /// <param name="text">The file contents.</param>
    /// <returns>The absolute file path.</returns>
    internal string WriteText(string relativePath, string text)
    {
        string fullPath = FullPath(relativePath);
        CreateParentDirectory(fullPath);
        File.WriteAllText(fullPath, text);
        return fullPath;
    }

    /// <summary>Writes a text file under the project root (not the asset root).</summary>
    /// <param name="projectRelativePath">The project-relative path.</param>
    /// <param name="text">The file contents.</param>
    /// <returns>The absolute file path.</returns>
    internal string WriteProjectText(string projectRelativePath, string text)
    {
        string fullPath = _directory.FullPath(projectRelativePath);
        CreateParentDirectory(fullPath);
        File.WriteAllText(fullPath, text);
        return fullPath;
    }

    /// <summary>Creates a directory under the asset root.</summary>
    /// <param name="relativePath">The asset-root-relative directory path.</param>
    /// <returns>The absolute directory path.</returns>
    internal string CreateDirectory(string relativePath)
        => _directory.CreateDirectory(ToAssetsPath(relativePath));

    /// <summary>Resolves a path relative to the project root (not the asset root).</summary>
    /// <param name="projectRelativePath">The project-relative path.</param>
    /// <returns>The absolute path.</returns>
    internal string ProjectPath(string projectRelativePath) => _directory.FullPath(projectRelativePath);

    /// <summary>Resolves an asset-root-relative path to an absolute path.</summary>
    /// <param name="relativePath">The asset-root-relative path.</param>
    /// <returns>The absolute path.</returns>
    internal string FullPath(string relativePath)
        => _directory.FullPath(ToAssetsPath(relativePath));

    /// <summary>Returns the project-relative display form of an asset-root-relative path.</summary>
    /// <param name="relativePath">The asset-root-relative path.</param>
    /// <returns>The diagnostics display path.</returns>
    internal static string DisplayPath(string relativePath)
        => AssetsDirectoryName + "/" + relativePath.Replace('\\', '/');

    /// <inheritdoc/>
    public void Dispose() => _directory.Dispose();

    private static string ToAssetsPath(string relativePath)
        => Path.Combine(AssetsDirectoryName, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void CreateParentDirectory(string fullPath)
    {
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
