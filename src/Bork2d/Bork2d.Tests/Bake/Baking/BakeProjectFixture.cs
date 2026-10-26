using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Project;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// Shares one temporary project, one generated host, and one loaded manifest across the bake and
/// load tests, so the slow <c>dotnet build</c> step runs once per collection.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BakeProjectFixtureCollection : ICollectionFixture<BakeProjectFixture>
{
    /// <summary>The collection name the bake/load tests join.</summary>
    public const string Name = "Bake project fixture";
}

/// <summary>
/// A complete temporary game project whose <c>Project.Common</c> is the real
/// <c>Bork2d.BakeFixture.dll</c>: a class root with a <c>Color</c>, a nested struct, and a texture
/// reference; a <c>StatsRoot</c> struct root; a <c>SpriteAsset</c>; and a generated PNG with an
/// import sidecar. The generated host is built with the pinned SDK and loaded through the production
/// loader. No binary fixture is committed.
/// </summary>
public sealed class BakeProjectFixture : IDisposable
{
    /// <summary>The descriptor file name.</summary>
    internal const string DescriptorFileName = "bork2d.project.toml";

    /// <summary>The canonical class-root asset path.</summary>
    internal const string GamePath = "data/game";

    /// <summary>The canonical struct-root asset path.</summary>
    internal const string StatsPath = "roots/stats";

    /// <summary>The canonical sprite asset path.</summary>
    internal const string SpritePath = "sprites/player";

    /// <summary>The canonical texture descriptor path.</summary>
    internal const string TexturePath = "textures/player";

    /// <summary>The canonical raw texture payload path.</summary>
    internal const string TexturePayloadPath = "textures/raw/player";

    /// <summary>The exact RGBA8 payload the generated PNG decodes to.</summary>
    internal static readonly byte[] ExpectedTexturePayload =
    [
        255, 0, 0, 255,
        0, 255, 0, 128,
        0, 0, 255, 64,
        255, 255, 255, 0,
    ];

    private const string Descriptor = """
        schema = 1
        name = "BakeFixture"

        [assets]
        root = "Assets"

        [bake]
        host = "host"
        pack = "out/assets.pck"

        [textures]
        filter = "linear"
        wrap = "clamp"

        [[types]]
        prefix = "data"
        type = "Bork2d.BakeFixture.GameConfig"

        [[types]]
        prefix = "roots"
        type = "Bork2d.BakeFixture.StatsRoot"

        [[types]]
        prefix = "sprites"
        type = "Bork2d.Api.Assets.SpriteAsset"
        """;

    private const string GameConfigToml = """
        name = "campaign"
        health = 120
        max_players = 4
        must = "present"
        tint = [1.0, 0.5, 0.25, 1.0]
        texture = "textures/player"

        [stats]
        attack = 3
        defense = 7
        """;

    private const string StatsToml = """
        attack = 11
        defense = 13
        """;

    private const string SpriteToml = """
        texture = "textures/player"
        uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]
        scale = 16.0
        pivot = [0.5, 0.5]
        topology = "triangles"
        indices = [0, 1, 2, 0, 2, 3]
        """;

    private readonly string _rootPath;

    /// <summary>Builds the fixture project and host, then loads the host.</summary>
    public BakeProjectFixture()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"bork2d-bake-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);

        WriteFile(DescriptorFileName, Descriptor);
        WriteFile("Assets/data/game.toml", GameConfigToml);
        WriteFile("Assets/roots/stats.toml", StatsToml);
        WriteFile("Assets/sprites/player.toml", SpriteToml);
        WriteFile(
            "Assets/textures/player.png.toml",
            """
            filter = "nearest"
            wrap = "repeat"
            """);
        WriteBytes("Assets/textures/player.png", TextureImageFactory.RgbaPng());

        var generationDiagnostics = new BakeDiagnostics();
        var options = new HostGenerationOptions(
            DescriptorPath,
            CommonAssemblyPath,
            HostDirectory,
            "Debug",
            force: true);
        HostGenerationResult? generation = HostGenerator.Generate(options, generationDiagnostics)
            ?? throw new InvalidOperationException(
                "The bake fixture host could not be generated:" + Environment.NewLine
                    + RenderDiagnostics(generationDiagnostics));

        TempProcessRunner.Result hostBuild = TempProcessRunner.Run(
            "dotnet",
            ["build", generation.ProjectPath, "-c", "Debug", "-warnaserror", "-nologo"],
            _rootPath);
        if (hostBuild.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "The bake fixture host did not build:" + Environment.NewLine + hostBuild.Output);
        }

        var loadDiagnostics = new BakeDiagnostics();
        Handle = BakeHostLoader.TryLoad(HostAssemblyPath, loadDiagnostics)
            ?? throw new InvalidOperationException(
                "The bake fixture host could not be loaded:" + Environment.NewLine
                    + RenderDiagnostics(loadDiagnostics));

        var descriptorDiagnostics = new BakeDiagnostics();
        DescriptorDescriptor = ProjectDescriptorParser.Parse(DescriptorPath, descriptorDiagnostics)
            ?? throw new InvalidOperationException(
                "The bake fixture descriptor is invalid:" + Environment.NewLine
                    + RenderDiagnostics(descriptorDiagnostics));
    }

    /// <summary>Gets the fixture project root.</summary>
    internal string RootPath => _rootPath;

    /// <summary>Gets the absolute descriptor path.</summary>
    internal string DescriptorPath => Path.Combine(_rootPath, DescriptorFileName);

    /// <summary>Gets the absolute asset root.</summary>
    internal string AssetsRoot => Path.Combine(_rootPath, "Assets");

    /// <summary>Gets the generated host output directory.</summary>
    internal string HostDirectory => Path.Combine(_rootPath, "host");

    /// <summary>Gets the built generated host assembly path.</summary>
    internal string HostAssemblyPath
        => Path.Combine(HostDirectory, "bin", "Debug", "net11.0", "Bork2d.BakeHost.dll");

    /// <summary>Gets the built <c>Bork2d.BakeFixture</c> assembly the host references.</summary>
    internal string CommonAssemblyPath
        => typeof(Bork2d.BakeFixture.GameConfig).Assembly.Location;

    /// <summary>Gets the descriptor's default pack path.</summary>
    internal string DefaultPackPath => Path.Combine(_rootPath, "out", "assets.pck");

    /// <summary>Gets the loaded generated host.</summary>
    internal BakeHostHandle Handle { get; }

    /// <summary>Gets the loaded generated manifest.</summary>
    internal IBakeManifest Manifest => Handle.Manifest;

    /// <summary>Gets the parsed fixture descriptor.</summary>
    internal ProjectDescriptor DescriptorDescriptor { get; }

    /// <summary>Gets the descriptor text, for tests that need an independent project.</summary>
    internal static string DescriptorText => Descriptor;

    /// <summary>
    /// Creates an independent project directory with the fixture descriptor and an empty
    /// <c>Assets/</c> tree so a test can mutate sources without disturbing the shared fixture.
    /// </summary>
    /// <param name="name">A folder-name fragment that identifies the test.</param>
    /// <returns>The new project directory.</returns>
    internal static string CreateProject(string name)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"bork2d-bake-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "Assets"));
        File.WriteAllText(Path.Combine(directory, DescriptorFileName), Descriptor);
        return directory;
    }

    /// <summary>Writes a text file under a project copy's <c>Assets/</c> tree.</summary>
    /// <param name="projectDirectory">The project copy returned by <see cref="CreateProject"/>.</param>
    /// <param name="relativePath">The asset-root-relative path.</param>
    /// <param name="content">The text contents.</param>
    internal static void WriteAsset(
        string projectDirectory,
        string relativePath,
        string content)
    {
        string fullPath = Path.Combine(
            projectDirectory,
            "Assets",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    /// <summary>Writes a binary file under a project copy's <c>Assets/</c> tree.</summary>
    /// <param name="projectDirectory">The project copy returned by <see cref="CreateProject"/>.</param>
    /// <param name="relativePath">The asset-root-relative path.</param>
    /// <param name="bytes">The file contents.</param>
    internal static void WriteAssetBytes(
        string projectDirectory,
        string relativePath,
        byte[] bytes)
    {
        string fullPath = Path.Combine(
            projectDirectory,
            "Assets",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }

    /// <summary>Writes the generated fixture texture into a project copy.</summary>
    /// <param name="projectDirectory">The project copy returned by <see cref="CreateProject"/>.</param>
    internal static void WriteTexture(string projectDirectory)
        => WriteAssetBytes(projectDirectory, "textures/player.png", TextureImageFactory.RgbaPng());

    /// <inheritdoc/>
    public void Dispose()
    {
        // The non-collectible host load context keeps the host and fixture assembly memory-mapped on
        // Windows; the OS temp cleaner owns a directory that cannot be removed here.
        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string RenderDiagnostics(BakeDiagnostics diagnostics)
        => string.Join(
            Environment.NewLine,
            diagnostics.GetSorted().Select(diagnostic => diagnostic.Format()));

    private void WriteFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(_rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private void WriteBytes(string relativePath, byte[] bytes)
    {
        string fullPath = Path.Combine(_rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }
}
