using Bork2d.Bake;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Textures;
using Bork2d.Tests.Bake.Textures;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// A temporary project directory plus the immutable <see cref="BakeProject"/> that the internal
/// pipeline and its focused tests consume.
/// </summary>
/// <remarks>
/// The configuration is produced through the same internal definition/factory path the public builder
/// uses. Tests that exercise the public builder itself use <c>BakeApplicationBuilder</c> directly.
/// </remarks>
internal sealed class TestBakeProject : IDisposable
{
    private readonly TempBakeDirectory _directory;

    private TestBakeProject(TempBakeDirectory directory, BakeProject project)
    {
        _directory = directory;
        Project = project;
    }

    /// <summary>Gets the temporary project root.</summary>
    internal string RootPath => _directory.RootPath;

    /// <summary>Gets the validated project configuration.</summary>
    internal BakeProject Project { get; }

    /// <summary>Creates a one-pack project with an <c>Assets</c> root.</summary>
    /// <param name="name">A folder-name fragment that identifies the test.</param>
    /// <param name="registerFixtureTypes">Whether to register the fixture data types.</param>
    /// <param name="processors">The custom processors to register, in builder order.</param>
    /// <returns>The temporary project.</returns>
    internal static TestBakeProject Create(
        string name,
        bool registerFixtureTypes = true,
        IReadOnlyList<IAssetProcessor>? processors = null)
        => Create(name, [("assets.pck", "Assets")], registerFixtureTypes, processors);

    /// <summary>Creates a project with the supplied ordered pack roots.</summary>
    /// <param name="name">A folder-name fragment that identifies the test.</param>
    /// <param name="packs">The ordered <c>(pack path, root)</c> entries.</param>
    /// <param name="registerFixtureTypes">Whether to register the fixture data types.</param>
    /// <param name="processors">The custom processors to register, in builder order.</param>
    /// <returns>The temporary project.</returns>
    internal static TestBakeProject Create(
        string name,
        IReadOnlyList<(string Pack, string Root)> packs,
        bool registerFixtureTypes = true,
        IReadOnlyList<IAssetProcessor>? processors = null)
    {
        var directory = TempBakeDirectory.Create();
        foreach ((_, string root) in packs)
        {
            directory.CreateDirectory(root);
        }

        BakeProject project = BakeProjectFixture.CreateProject(
            directory.RootPath,
            packs,
            registerFixtureTypes,
            processors);

        return new TestBakeProject(directory, project);
    }

    /// <summary>Resolves a path relative to the project root.</summary>
    /// <param name="relativePath">The project-relative path.</param>
    /// <returns>The absolute path.</returns>
    internal string FullPath(string relativePath) => _directory.FullPath(relativePath);

    /// <summary>Writes a text file under the main pack's <c>Assets</c> root.</summary>
    /// <param name="relativePath">The asset-root-relative path.</param>
    /// <param name="content">The text contents.</param>
    internal void WriteAsset(string relativePath, string content)
        => WriteAssetUnder("Assets", relativePath, content);

    /// <summary>Writes a binary file under the main pack's <c>Assets</c> root.</summary>
    /// <param name="relativePath">The asset-root-relative path.</param>
    /// <param name="bytes">The file contents.</param>
    internal void WriteAssetBytes(string relativePath, byte[] bytes)
        => WriteAssetBytesUnder("Assets", relativePath, bytes);

    /// <summary>Writes a text file under a pack root.</summary>
    /// <param name="root">The pack root name.</param>
    /// <param name="relativePath">The root-relative path.</param>
    /// <param name="content">The text contents.</param>
    internal void WriteAssetUnder(string root, string relativePath, string content)
    {
        string fullPath = Path.Combine(
            RootPath,
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    /// <summary>Writes a binary file under a pack root.</summary>
    /// <param name="root">The pack root name.</param>
    /// <param name="relativePath">The root-relative path.</param>
    /// <param name="bytes">The file contents.</param>
    internal void WriteAssetBytesUnder(string root, string relativePath, byte[] bytes)
    {
        string fullPath = Path.Combine(
            RootPath,
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }

    /// <summary>Writes the standard fixture data sources and texture sidecar.</summary>
    internal void WriteStandardAssets()
    {
        WriteAsset("data/game.toml", BakeProjectFixture.GameToml);
        WriteAsset("roots/stats.toml", BakeProjectFixture.StatsToml);
        WriteAsset("sprites/player.toml", BakeProjectFixture.SpriteToml);
        WriteAsset("art/player.png.toml", BakeProjectFixture.TextureSidecarToml);
    }

    /// <summary>Writes the test-generated fixture texture under <c>Assets/art</c>.</summary>
    internal void WriteTexture()
        => WriteAssetBytes("art/player.png", TextureImageFactory.RgbaPng());

    /// <inheritdoc/>
    public void Dispose() => _directory.Dispose();
}
