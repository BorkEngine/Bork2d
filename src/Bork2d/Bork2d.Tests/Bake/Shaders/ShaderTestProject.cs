using Bork2d.Bake;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Engine.Assets;

namespace Bork2d.Tests.Bake.Shaders;

/// <summary>
/// A temporary project with an <c>Assets</c> root and the default processors, plus a separate output
/// root, for shader bakes through the real builder and pack pipeline.
/// </summary>
/// <remarks>
/// The project is produced through the public <see cref="BakeApplicationBuilder"/> exactly as a game's
/// assets executable would configure it, so tests exercise the real default-processor ordering,
/// required-sidecar creation, shader compilation, and pack pipeline. Paths passed to the write
/// helpers are relative to the asset root (<c>materials/lit.frag</c>).
/// </remarks>
internal sealed class ShaderTestProject : IDisposable
{
    private const string AssetsDirectoryName = "Assets";

    private readonly TempBakeDirectory _directory;
    private readonly TempBakeDirectory _output;

    private ShaderTestProject(
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

    /// <summary>Gets the absolute path of the pack a <see cref="Bake"/> writes.</summary>
    internal string PackPath => _output.FullPath("assets.pck");

    /// <summary>Creates a temporary project configured with the default processors.</summary>
    /// <returns>The test project.</returns>
    internal static ShaderTestProject Create()
    {
        TempBakeDirectory directory = TempBakeDirectory.Create();
        directory.CreateDirectory(AssetsDirectoryName);
        TempBakeDirectory output = TempBakeDirectory.Create();

        var diagnostics = new BakeDiagnostics();
        BakeProject? project = new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", AssetsDirectoryName)
            .UseDefaultProcessors()
            .WithTextureDefaults(TextureFilter.Linear, TextureWrap.Clamp)
            .CreateProject(diagnostics);

        if (project is null || diagnostics.HasErrors)
        {
            throw new InvalidOperationException(
                "The shader test project is invalid:" + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        diagnostics.GetSorted().Select(diagnostic => diagnostic.Format())));
        }

        return new ShaderTestProject(directory, output, project);
    }

    /// <summary>Runs the real bake pipeline into the separate output root.</summary>
    /// <param name="check">Whether to run a check-only bake (no pack and no source writes).</param>
    /// <returns>The bake result, including every diagnostic.</returns>
    internal BakeResult Bake(bool check = false)
        => BakePipeline.Run(
            Project,
            new BakeOptions(Path.GetFullPath(_output.RootPath), BakeMode.Debug, check, force: false),
            new BakeDiagnostics());

    /// <summary>Writes a text file (normally a shader source or sidecar) under the asset root.</summary>
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

    /// <summary>Writes raw bytes under the asset root.</summary>
    /// <param name="relativePath">The asset-root-relative path.</param>
    /// <param name="bytes">The file contents.</param>
    /// <returns>The absolute file path.</returns>
    internal string WriteBytes(string relativePath, byte[] bytes)
    {
        string fullPath = FullPath(relativePath);
        CreateParentDirectory(fullPath);
        File.WriteAllBytes(fullPath, bytes);
        return fullPath;
    }

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
