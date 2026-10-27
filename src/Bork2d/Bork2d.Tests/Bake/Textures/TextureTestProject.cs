using Bork2d.Api.Assets;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Builder;
using Bork2d.Bake.Diagnostics;

namespace Bork2d.Tests.Bake.Textures;

/// <summary>
/// A temporary project with an <c>Assets</c> root, the default-processor configuration, and a
/// separate output root for pack-level texture bakes.
/// </summary>
/// <remarks>
/// <para>
/// The project is produced through the public <see cref="BakeApplicationBuilder"/> exactly as a game's
/// assets executable would configure it, so tests exercise the same builder ordering, default texture
/// processor, sidecar creation, and pack pipeline as production. <see cref="Bake"/> runs the real
/// pipeline and writes <c>assets.pck</c> under <see cref="OutputRoot"/>; the decode-only tests call
/// <c>TextureSourceReader</c> directly instead.
/// </para>
/// <para>
/// Paths passed to the write helpers are relative to the asset root (<c>textures/player.png</c>), and
/// <see cref="DisplayPath"/> returns the project-relative form diagnostics use
/// (<c>Assets/textures/player.png</c>).
/// </para>
/// </remarks>
internal sealed class TextureTestProject : IDisposable
{
    private const string AssetsDirectoryName = "Assets";

    private readonly TempBakeDirectory _directory;
    private readonly TempBakeDirectory _output;

    private TextureTestProject(
        TempBakeDirectory directory,
        TempBakeDirectory output,
        BakeProject project)
    {
        _directory = directory;
        _output = output;
        Project = project;
    }

    /// <summary>Gets the validated project configuration.</summary>
    internal BakeProject Project { get; }

    /// <summary>Gets the project root (the temp directory).</summary>
    internal string RootPath => _directory.RootPath;

    /// <summary>Gets the pack output root.</summary>
    internal string OutputRoot => _output.RootPath;

    /// <summary>Gets the absolute path of the pack a <see cref="Bake"/> writes.</summary>
    internal string PackPath => _output.FullPath("assets.pck");

    /// <summary>Creates a temporary project with the given texture defaults.</summary>
    /// <param name="filter">The default filter for a texture without a sidecar.</param>
    /// <param name="wrap">The default wrap mode for a texture without a sidecar.</param>
    /// <returns>The test project.</returns>
    internal static TextureTestProject Create(
        TextureFilter filter = TextureFilter.Linear,
        TextureWrap wrap = TextureWrap.Clamp)
    {
        TempBakeDirectory directory = TempBakeDirectory.Create();
        directory.CreateDirectory(AssetsDirectoryName);
        TempBakeDirectory output = TempBakeDirectory.Create();

        var diagnostics = new BakeDiagnostics();
        BakeProject? project = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", AssetsDirectoryName)
            .UseDefaultProcessors()
            .WithTextureDefaults(filter, wrap)
            .CreateProject(diagnostics);

        if (project is null || diagnostics.HasErrors)
        {
            throw new InvalidOperationException(
                "The texture test project is invalid:" + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        diagnostics.GetSorted().Select(diagnostic => diagnostic.Format())));
        }

        return new TextureTestProject(directory, output, project);
    }

    /// <summary>Runs the real bake pipeline into <see cref="OutputRoot"/>.</summary>
    /// <param name="check">Whether to run a check-only bake (no pack and no source writes).</param>
    /// <returns>The bake result, including every diagnostic.</returns>
    internal BakeResult Bake(bool check = false)
        => BakePipeline.Run(
            Project,
            new BakeOptions(Path.GetFullPath(OutputRoot), BakeMode.Debug, check, force: false),
            new BakeDiagnostics());

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
    public void Dispose()
    {
        _directory.Dispose();
        _output.Dispose();
    }

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
