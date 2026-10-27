using Bork2d.Tests.Bake.Baking;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.MsBuild;

/// <summary>
/// The MSBuild integration (§16.8, Q-10/Q-11): a temporary app project imports
/// <c>build/Bork2d.Bake.targets</c>, references <c>Bork2d.BakeFixture</c>, builds with
/// <c>dotnet build</c>, and produces the pack at the documented location. A second build skips
/// without touching the pack, an invalid source fails the build with canonical <c>BKxxxx</c>
/// diagnostics and keeps the previous pack, publish copies the pack, and clean removes every
/// generated artifact. The project sources and texture are written by the test; no binary fixture is
/// committed.
/// </summary>
public sealed class BakeTargetsIntegrationTests
{
    private const string Descriptor = """
        schema = 1

        [[assets]]
        root = "Assets"
        pack = "assets.pck"

        [[assets]]
        root = "DLC1"
        pack = "dlc1.pck"

        [bake]
        host = "obj/bork2d/bakehost"

        [textures]
        filter = "linear"
        wrap = "clamp"
        """;

    private const string GameToml = """
        name = "campaign"
        health = 120
        max_players = 4
        must = "present"
        tint = [1.0, 0.5, 0.25, 1.0]
        texture = "art/player"

        [stats]
        attack = 3
        defense = 7

        [bork2d]
        type = "Bork2d.BakeFixture.GameConfig"
        """;

    private const string StatsToml = """
        attack = 11
        defense = 13

        [bork2d]
        type = "Bork2d.BakeFixture.StatsRoot"
        """;

    private const string SpriteToml = """
        texture = "art/player"
        uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]
        scale = 16.0
        pivot = [0.5, 0.5]
        topology = "triangles"
        indices = [0, 1, 2, 0, 2, 3]

        [bork2d]
        type = "Bork2d.Api.Assets.SpriteAsset"
        """;

    private const string DlcToml = """
        name = "dlc"
        health = 10
        must = "overlay"
        texture = "art/player"

        [bork2d]
        type = "Bork2d.BakeFixture.GameConfig"
        """;

    private const string SidecarToml = """
        filter = "nearest"
        wrap = "repeat"
        """;

    private static readonly string[] _expectedEntries =
    [
        BakeProjectFixture.TexturePath,
        BakeProjectFixture.TexturePayloadPath,
        BakeProjectFixture.GamePath,
        BakeProjectFixture.StatsPath,
        BakeProjectFixture.SpritePath,
    ];

    [Fact]
    public void BuildBakesOncePublishesThePackAndCleanRemovesEverything()
    {
        using var app = TemporaryBakeApp.Create();

        TempProcessRunner.Result build = app.Build();

        Assert.True(build.ExitCode == 0, build.Output);
        Assert.Contains("baked '", build.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(app.PackPath));
        Assert.True(File.Exists(app.DlcPackPath));
        Assert.True(File.Exists(app.HostProjectPath));
        Assert.True(File.Exists(app.HostManifestPath));
        Assert.True(File.Exists(app.IncrementalManifestPath));
        Assert.Equal(_expectedEntries, BakedPackAssertions.Shapes(app.PackPath).Select(shape => shape.Path));
        Assert.Equal(
            new[] { BakeProjectFixture.DlcGamePath },
            BakedPackAssertions.Shapes(app.DlcPackPath).Select(shape => shape.Path));
        Assert.Equal(
            BakeProjectFixture.ExpectedTexturePayload,
            ReadRawPayload(app.PackPath, BakeProjectFixture.TexturePayloadPath));
        AssertNoToolingAssemblies(Path.GetDirectoryName(app.PackPath)!);

        byte[] packBytes = File.ReadAllBytes(app.PackPath);
        DateTime packWriteTime = File.GetLastWriteTimeUtc(app.PackPath);
        byte[] manifestBytes = File.ReadAllBytes(app.IncrementalManifestPath);

        TempProcessRunner.Result second = app.Build();

        Assert.True(second.ExitCode == 0, second.Output);
        Assert.Contains("up to date", second.Output, StringComparison.Ordinal);
        Assert.Equal(packBytes, File.ReadAllBytes(app.PackPath));
        Assert.Equal(packWriteTime, File.GetLastWriteTimeUtc(app.PackPath));
        Assert.Equal(manifestBytes, File.ReadAllBytes(app.IncrementalManifestPath));

        TempProcessRunner.Result publish = app.Publish();

        Assert.True(publish.ExitCode == 0, publish.Output);
        string publishedPack = Path.Combine(app.PublishDirectory, "assets.pck");
        string publishedDlcPack = Path.Combine(app.PublishDirectory, "dlc1.pck");
        Assert.True(File.Exists(publishedPack), publish.Output);
        Assert.True(File.Exists(publishedDlcPack), publish.Output);
        Assert.Equal(packBytes, File.ReadAllBytes(publishedPack));
        Assert.Equal(File.ReadAllBytes(app.DlcPackPath), File.ReadAllBytes(publishedDlcPack));
        AssertNoToolingAssemblies(app.PublishDirectory);

        TempProcessRunner.Result clean = app.Clean();

        Assert.True(clean.ExitCode == 0, clean.Output);
        Assert.False(File.Exists(app.PackPath));
        Assert.False(File.Exists(app.DlcPackPath));
        Assert.False(Directory.Exists(app.HostDirectory));
        Assert.False(Directory.Exists(app.IncrementalDirectory));
    }

    [Fact]
    public void InvalidSourceFailsTheBuildWithACanonicalDiagnosticAndKeepsThePack()
    {
        using var app = TemporaryBakeApp.Create();
        TempProcessRunner.Result first = app.Build();
        Assert.True(first.ExitCode == 0, first.Output);
        byte[] packBytes = File.ReadAllBytes(app.PackPath);
        byte[] manifestBytes = File.ReadAllBytes(app.IncrementalManifestPath);

        File.WriteAllText(app.GameSourcePath, "name = \n");

        TempProcessRunner.Result failed = app.Build();

        Assert.True(failed.ExitCode != 0, failed.Output);
        Assert.Contains("error BK2001:", failed.Output, StringComparison.Ordinal);
        Assert.Contains("Assets/data/game.toml(", failed.Output, StringComparison.Ordinal);
        Assert.Equal(packBytes, File.ReadAllBytes(app.PackPath));
        Assert.Equal(manifestBytes, File.ReadAllBytes(app.IncrementalManifestPath));

        File.WriteAllText(app.GameSourcePath, GameToml);

        TempProcessRunner.Result recovered = app.Build();

        Assert.True(recovered.ExitCode == 0, recovered.Output);
        Assert.Contains("up to date", recovered.Output, StringComparison.Ordinal);
        Assert.Equal(packBytes, File.ReadAllBytes(app.PackPath));

        File.WriteAllText(
            app.GameSourcePath,
            GameToml.Replace("health = 120", "health = 130", StringComparison.Ordinal));

        TempProcessRunner.Result changed = app.Build();

        Assert.True(changed.ExitCode == 0, changed.Output);
        Assert.Contains("baked '", changed.Output, StringComparison.Ordinal);
        Assert.NotEqual(packBytes, File.ReadAllBytes(app.PackPath));
    }

    private static byte[] ReadRawPayload(string packPath, string assetPath)
        => BakedPackAssertions
            .Payloads(packPath)
            .Zip(BakedPackAssertions.Shapes(packPath))
            .Single(pair => pair.Second.Path == assetPath)
            .First;

    private static void AssertNoToolingAssemblies(string directory)
    {
        foreach (string file in Directory.GetFiles(directory, "*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            Assert.False(
                name is "Bork2d.Bake" or "Bork2d.Bake.Cli" or "Bork2d.BakeHost",
                $"'{file}' must not be copied into the app output.");
            Assert.NotEqual("Tomlyn", name);
            Assert.NotEqual("StbImageSharp", name);
        }
    }

    /// <summary>
    /// A temporary runnable app project that imports the repository targets and references the
    /// fixture library, with a test-written descriptor, asset tree, and generated texture.
    /// </summary>
    private sealed class TemporaryBakeApp : IDisposable
    {
        private const string ProjectFileName = "BakeApp.csproj";

        private readonly string _rootDirectory;

        private TemporaryBakeApp(string rootDirectory)
        {
            _rootDirectory = rootDirectory;
        }

        internal string ProjectDirectory => Path.Combine(_rootDirectory, "app");

        internal string PublishDirectory => Path.Combine(_rootDirectory, "publish");

        internal string PackPath => Path.Combine(
            ProjectDirectory,
            "bin",
            "Debug",
            "net11.0",
            "assets.pck");

        internal string DlcPackPath => Path.Combine(
            ProjectDirectory,
            "bin",
            "Debug",
            "net11.0",
            "dlc1.pck");

        internal string HostDirectory => Path.Combine(
            ProjectDirectory,
            "obj",
            "bork2d",
            "bakehost");

        internal string HostProjectPath => Path.Combine(HostDirectory, "BakeHost.csproj");

        internal string HostManifestPath => Path.Combine(HostDirectory, "BakeManifest.g.cs");

        internal string IncrementalDirectory => Path.Combine(
            ProjectDirectory,
            "obj",
            "bork2d",
            "bake");

        internal string IncrementalManifestPath => Path.Combine(
            IncrementalDirectory,
            "manifest.json");

        internal string GameSourcePath => Path.Combine(
            ProjectDirectory,
            "Assets",
            "data",
            "game.toml");

        internal string DlcSourcePath => Path.Combine(
            ProjectDirectory,
            "DLC1",
            "data",
            "dlc.toml");

        internal static TemporaryBakeApp Create()
        {
            string repositoryRoot = FindRepositoryRoot();
            string root = Path.Combine(
                Path.GetTempPath(),
                $"bork2d-bake-msbuild-{Guid.NewGuid():N}");
            var app = new TemporaryBakeApp(root);

            Directory.CreateDirectory(app.ProjectDirectory);
            File.WriteAllText(
                Path.Combine(app.ProjectDirectory, ProjectFileName),
                Project(repositoryRoot));
            File.WriteAllText(
                Path.Combine(app.ProjectDirectory, "Program.cs"),
                """
                internal static class Program
                {
                    internal static int Main() => 0;
                }
                """);
            File.WriteAllText(Path.Combine(app.ProjectDirectory, "bork2d.project.toml"), Descriptor);
            app.WriteAsset("data/game.toml", GameToml);
            app.WriteAsset("roots/stats.toml", StatsToml);
            app.WriteAsset("sprites/player.toml", SpriteToml);
            app.WriteAsset("art/player.png.toml", SidecarToml);
            app.WriteAssetBytes("art/player.png", TextureImageFactory.RgbaPng());
            app.WriteDlcAsset("data/dlc.toml", DlcToml);

            return app;
        }

        internal TempProcessRunner.Result Build()
            => Run("build", ProjectFileName, "-c", "Debug", "-nologo");

        internal TempProcessRunner.Result Clean()
            => Run("clean", ProjectFileName, "-c", "Debug", "-nologo");

        internal TempProcessRunner.Result Publish()
            => Run(
                "publish",
                ProjectFileName,
                "-c",
                "Debug",
                "-o",
                PublishDirectory,
                "-nologo");

        /// <inheritdoc/>
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_rootDirectory))
                {
                    Directory.Delete(_rootDirectory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static string Project(string repositoryRoot)
        {
            string fixtureProject = Path.Combine(
                repositoryRoot,
                "src",
                "Bork2d",
                "Bork2d.BakeFixture",
                "Bork2d.BakeFixture.csproj");
            string targets = Path.Combine(repositoryRoot, "build", "Bork2d.Bake.targets");

            return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup>
                        <OutputType>Exe</OutputType>
                        <TargetFramework>net11.0</TargetFramework>
                        <Nullable>enable</Nullable>
                        <ImplicitUsings>enable</ImplicitUsings>
                        <AssemblyName>BakeApp</AssemblyName>
                        <Bork2dProjectDescriptor>$(MSBuildProjectDirectory)\bork2d.project.toml</Bork2dProjectDescriptor>
                        <Bork2dBakeCommonAssemblyName>Bork2d.BakeFixture</Bork2dBakeCommonAssemblyName>
                        <Bork2dBakePackPaths>assets.pck;dlc1.pck</Bork2dBakePackPaths>
                    </PropertyGroup>
                    <ItemGroup>
                        <ProjectReference Include="{fixtureProject}" />
                    </ItemGroup>
                    <Import Project="{targets}" />
                </Project>
                """;
        }

        private TempProcessRunner.Result Run(params string[] arguments)
            => TempProcessRunner.Run("dotnet", arguments, ProjectDirectory, timeoutMilliseconds: 600_000);

        private void WriteAsset(string relativePath, string content)
        {
            string fullPath = Path.Combine(
                ProjectDirectory,
                "Assets",
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        private void WriteDlcAsset(string relativePath, string content)
        {
            string fullPath = Path.Combine(
                ProjectDirectory,
                "DLC1",
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        private void WriteAssetBytes(string relativePath, byte[] bytes)
        {
            string fullPath = Path.Combine(
                ProjectDirectory,
                "Assets",
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, bytes);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "build", "Bork2d.Bake.targets")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                "The test could not locate the repository root (build/Bork2d.Bake.targets).");
        }
    }
}
