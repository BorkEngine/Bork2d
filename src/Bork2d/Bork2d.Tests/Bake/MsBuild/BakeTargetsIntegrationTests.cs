using Bork2d.Bake.Incremental;
using Bork2d.Tests.Bake.Baking;
using Bork2d.Tests.Bake.Textures;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Bake.MsBuild;

/// <summary>
/// The assets MSBuild integration: a temporary assets executable imports
/// <c>build/Bork2d.Assets.targets</c> and bakes its own packs on Build; a temporary desktop app takes
/// a build-only reference and imports <c>build/Bork2d.Assets.Copy.targets</c> to copy the packs to its
/// output and publish directories. No descriptor, no generated host, and no pack-path list exist.
/// </summary>
public sealed class BakeTargetsIntegrationTests
{
    [Fact]
    public void BuildBakesAssetsCopiesToTheAppAndPublishAndCleanRemovesThem()
    {
        using var fixture = TemporaryAssetsSolution.Create();

        TempProcessRunner.Result assetsBuild = fixture.BuildAssets();

        Assert.True(assetsBuild.ExitCode == 0, assetsBuild.Output);
        Assert.Contains("baked '", assetsBuild.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(fixture.AssetsPackPath));
        Assert.True(File.Exists(fixture.DlcPackPath));
        Assert.Equal(
            _expectedEntries,
            BakedPackAssertions.Shapes(fixture.AssetsPackPath).Select(shape => shape.Path));
        Assert.Equal(
            new[] { BakeProjectFixture.DlcGamePath },
            BakedPackAssertions.Shapes(fixture.DlcPackPath).Select(shape => shape.Path));
        Assert.Equal(
            BakeProjectFixture.ExpectedTexturePayload,
            ReadRawPayload(fixture.AssetsPackPath, BakeProjectFixture.TexturePayloadPath));
        Assert.False(Directory.Exists(Path.Combine(fixture.AssetsProjectDirectory, "obj", "bork2d", "bakehost")));
        string manifestPath = BakeManifestPaths.ManifestPath(fixture.AssetsProjectDirectory);
        Assert.True(File.Exists(manifestPath));
        Assert.Contains("\"schema\": 3", File.ReadAllText(manifestPath), StringComparison.Ordinal);

        // A second build with no changed input/output/config is a no-op: the executable's schema-3
        // manifest skips the bake and the packs stay byte-identical.
        byte[] firstAssetsBytes = File.ReadAllBytes(fixture.AssetsPackPath);
        TempProcessRunner.Result secondAssetsBuild = fixture.BuildAssets();
        Assert.True(secondAssetsBuild.ExitCode == 0, secondAssetsBuild.Output);
        Assert.Contains("up to date:", secondAssetsBuild.Output, StringComparison.Ordinal);
        Assert.Contains("no inputs changed.", secondAssetsBuild.Output, StringComparison.Ordinal);
        Assert.Equal(firstAssetsBytes, File.ReadAllBytes(fixture.AssetsPackPath));

        TempProcessRunner.Result appBuild = fixture.BuildApp();

        Assert.True(appBuild.ExitCode == 0, appBuild.Output);
        Assert.True(File.Exists(fixture.AppAssetsPackPath), appBuild.Output);
        Assert.True(File.Exists(fixture.AppDlcPackPath), appBuild.Output);
        Assert.Equal(File.ReadAllBytes(fixture.AssetsPackPath), File.ReadAllBytes(fixture.AppAssetsPackPath));
        Assert.Equal(File.ReadAllBytes(fixture.DlcPackPath), File.ReadAllBytes(fixture.AppDlcPackPath));
        AssertNoToolingAssemblies(Path.GetDirectoryName(fixture.AppAssetsPackPath)!, includeAssetsExecutable: false);

        TempProcessRunner.Result publish = fixture.PublishApp();

        Assert.True(publish.ExitCode == 0, publish.Output);
        string publishedPack = Path.Combine(fixture.PublishDirectory, "assets.pck");
        string publishedDlc = Path.Combine(fixture.PublishDirectory, "dlc1.pck");
        Assert.True(File.Exists(publishedPack), publish.Output);
        Assert.True(File.Exists(publishedDlc), publish.Output);
        Assert.Equal(File.ReadAllBytes(fixture.AssetsPackPath), File.ReadAllBytes(publishedPack));
        AssertNoToolingAssemblies(fixture.PublishDirectory, includeAssetsExecutable: false);

        TempProcessRunner.Result clean = fixture.CleanAssets();

        Assert.True(clean.ExitCode == 0, clean.Output);
        Assert.False(File.Exists(fixture.AssetsPackPath));
        Assert.False(File.Exists(fixture.DlcPackPath));
        Assert.False(Directory.Exists(BakeManifestPaths.Directory(fixture.AssetsProjectDirectory)));
    }

    [Fact]
    public void InvalidSourceFailsTheAssetsBuildWithACanonicalDiagnosticAndKeepsThePack()
    {
        using var fixture = TemporaryAssetsSolution.Create();
        TempProcessRunner.Result first = fixture.BuildAssets();
        Assert.True(first.ExitCode == 0, first.Output);
        byte[] packBytes = File.ReadAllBytes(fixture.AssetsPackPath);

        File.WriteAllText(fixture.GameSourcePath, "name = \n");

        TempProcessRunner.Result failed = fixture.BuildAssets();

        Assert.True(failed.ExitCode != 0, failed.Output);
        Assert.Contains("error BK2001:", failed.Output, StringComparison.Ordinal);
        Assert.Contains("Assets/data/game.toml(", failed.Output, StringComparison.Ordinal);
        Assert.Equal(packBytes, File.ReadAllBytes(fixture.AssetsPackPath));

        File.WriteAllText(
            fixture.GameSourcePath,
            BakeProjectFixture.GameToml);
        TempProcessRunner.Result recovered = fixture.BuildAssets();
        Assert.True(recovered.ExitCode == 0, recovered.Output);
        Assert.Equal(packBytes, File.ReadAllBytes(fixture.AssetsPackPath));

        File.WriteAllText(
            fixture.GameSourcePath,
            BakeProjectFixture.GameToml.Replace("health = 120", "health = 130", StringComparison.Ordinal));
        TempProcessRunner.Result changed = fixture.BuildAssets();
        Assert.True(changed.ExitCode == 0, changed.Output);
        Assert.NotEqual(packBytes, File.ReadAllBytes(fixture.AssetsPackPath));
    }

    private static readonly string[] _expectedEntries =
    [
        BakeProjectFixture.TexturePath,
        BakeProjectFixture.TexturePayloadPath,
        BakeProjectFixture.GamePath,
        BakeProjectFixture.StatsPath,
        BakeProjectFixture.SpritePath,
    ];

    private static byte[] ReadRawPayload(string packPath, string assetPath)
        => BakedPackAssertions
            .Payloads(packPath)
            .Zip(BakedPackAssertions.Shapes(packPath))
            .Single(pair => pair.Second.Path == assetPath)
            .First;

    private static void AssertNoToolingAssemblies(string directory, bool includeAssetsExecutable)
    {
        foreach (string file in Directory.GetFiles(directory, "*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            Assert.False(
                name is "Bork2d.Bake" or "Bork2d.Bake.Builder" or "Bork2d.BakeHost" or "Bork2d.Bake.Cli",
                $"'{file}' must not be copied into an app output.");
            if (includeAssetsExecutable)
            {
                Assert.NotEqual("TempAssets", name);
            }

            Assert.NotEqual("Tomlyn", name);
            Assert.NotEqual("StbImageSharp", name);
        }
    }

    /// <summary>
    /// A temporary assets executable plus a desktop app that consumes its packs through the copy
    /// target. All sources and the texture are test-written; no binary fixture is committed.
    /// </summary>
    private sealed class TemporaryAssetsSolution : IDisposable
    {
        private const string AssetsProjectName = "TempAssets";
        private const string AppProjectName = "TempApp";

        private readonly string _rootDirectory;

        private TemporaryAssetsSolution(string rootDirectory) => _rootDirectory = rootDirectory;

        internal string AssetsProjectDirectory => Path.Combine(_rootDirectory, "assets");

        internal string AppProjectDirectory => Path.Combine(_rootDirectory, "app");

        internal string PublishDirectory => Path.Combine(_rootDirectory, "publish");

        internal string AssetsPackPath => Path.Combine(
            AssetsProjectDirectory,
            "bin",
            "Debug",
            "net11.0",
            "assets.pck");

        internal string DlcPackPath => Path.Combine(
            AssetsProjectDirectory,
            "bin",
            "Debug",
            "net11.0",
            "dlc1.pck");

        internal string AppAssetsPackPath => Path.Combine(
            AppProjectDirectory,
            "bin",
            "Debug",
            "net11.0",
            "assets.pck");

        internal string AppDlcPackPath => Path.Combine(
            AppProjectDirectory,
            "bin",
            "Debug",
            "net11.0",
            "dlc1.pck");

        internal string GameSourcePath => Path.Combine(
            AssetsProjectDirectory,
            "Assets",
            "data",
            "game.toml");

        internal static TemporaryAssetsSolution Create()
        {
            string repositoryRoot = RepositoryPaths.Root;
            string root = Path.Combine(
                Path.GetTempPath(),
                $"bork2d-assets-msbuild-{Guid.NewGuid():N}");
            var solution = new TemporaryAssetsSolution(root);

            string builderProject = Path.Combine(
                repositoryRoot,
                "src",
                "Bork2d",
                "Bork2d.Bake.Builder",
                "Bork2d.Bake.Builder.csproj");
            string fixtureProject = Path.Combine(
                repositoryRoot,
                "src",
                "Bork2d",
                "Bork2d.BakeFixture",
                "Bork2d.BakeFixture.csproj");

            Directory.CreateDirectory(solution.AssetsProjectDirectory);
            Directory.CreateDirectory(solution.AppProjectDirectory);

            File.WriteAllText(
                Path.Combine(solution.AssetsProjectDirectory, $"{AssetsProjectName}.csproj"),
                AssetsProject(repositoryRoot, builderProject, fixtureProject));
            File.WriteAllText(
                Path.Combine(solution.AssetsProjectDirectory, "Program.cs"),
                AssetsProgram());
            File.WriteAllText(
                Path.Combine(solution.AppProjectDirectory, $"{AppProjectName}.csproj"),
                AppProject(repositoryRoot, fixtureProject, solution.AssetsProjectDirectory));
            File.WriteAllText(
                Path.Combine(solution.AppProjectDirectory, "Program.cs"),
                "return 0;\n");

            solution.WriteAsset("data/game.toml", BakeProjectFixture.GameToml);
            solution.WriteAsset("roots/stats.toml", BakeProjectFixture.StatsToml);
            solution.WriteAsset("sprites/player.toml", BakeProjectFixture.SpriteToml);
            solution.WriteAsset("art/player.png.toml", BakeProjectFixture.TextureSidecarToml);
            solution.WriteAssetBytes("art/player.png", TextureImageFactory.RgbaPng());
            solution.WriteDlcAsset(
                "data/dlc.toml",
                """
                name = "dlc"
                health = 10
                must = "overlay"
                texture = "art/player"

                [bork2d]
                type = "Bork2d.BakeFixture.GameConfig"
                """);

            return solution;
        }

        internal TempProcessRunner.Result BuildAssets()
            => RunAssets("build", $"{AssetsProjectName}.csproj", "-c", "Debug", "-nologo");

        internal TempProcessRunner.Result CleanAssets()
            => RunAssets("clean", $"{AssetsProjectName}.csproj", "-c", "Debug", "-nologo");

        internal TempProcessRunner.Result BuildApp()
            => RunApp("build", $"{AppProjectName}.csproj", "-c", "Debug", "-nologo");

        internal TempProcessRunner.Result PublishApp()
            => RunApp(
                "publish",
                $"{AppProjectName}.csproj",
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

        private static string AssetsProject(
            string repositoryRoot,
            string builderProject,
            string fixtureProject)
        {
            string targets = Path.Combine(repositoryRoot, "build", "Bork2d.Assets.targets");
            return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup>
                        <OutputType>Exe</OutputType>
                        <TargetFramework>net11.0</TargetFramework>
                        <Nullable>enable</Nullable>
                        <ImplicitUsings>enable</ImplicitUsings>
                        <AssemblyName>{AssetsProjectName}</AssemblyName>
                    </PropertyGroup>
                    <ItemGroup>
                        <ProjectReference Include="{builderProject}" />
                        <ProjectReference Include="{fixtureProject}" />
                    </ItemGroup>
                    <Import Project="{targets}" />
                </Project>
                """;
        }

        private static string AssetsProgram() => """
            using Bork2d.Bake.Builder;

            return await new BakeApplicationBuilder()
                .WithProjectRoot(".")
                .WithPack("assets.pck", "Assets")
                .WithPack("dlc1.pck", "DLC1")
                .UseDefaultAssets()
                .RegisterAsset<Bork2d.BakeFixture.GameConfig>()
                .RegisterAsset<Bork2d.BakeFixture.StatsRoot>()
                .UseDefaultProcessors()
                .WithTextureDefaults(TextureFilter.Linear, TextureWrap.Clamp)
                .Build()
                .RunAsync(args);
            """;

        private static string AppProject(
            string repositoryRoot,
            string fixtureProject,
            string assetsProjectDirectory)
        {
            string copyTargets = Path.Combine(repositoryRoot, "build", "Bork2d.Assets.Copy.targets");
            string assetsProjectFile = Path.Combine(
                assetsProjectDirectory,
                $"{AssetsProjectName}.csproj");
            string assetsExecutable = Path.Combine(
                assetsProjectDirectory,
                "bin",
                "$(Configuration)",
                "net11.0",
                $"{AssetsProjectName}.dll");
            return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup>
                        <OutputType>Exe</OutputType>
                        <TargetFramework>net11.0</TargetFramework>
                        <Nullable>enable</Nullable>
                        <ImplicitUsings>enable</ImplicitUsings>
                        <AssemblyName>{AppProjectName}</AssemblyName>
                        <Bork2dAssetsProjectDirectory>{assetsProjectDirectory}</Bork2dAssetsProjectDirectory>
                        <Bork2dAssetsExecutable>{assetsExecutable}</Bork2dAssetsExecutable>
                    </PropertyGroup>
                    <ItemGroup>
                        <ProjectReference Include="{fixtureProject}" />
                        <ProjectReference Include="{assetsProjectFile}"
                                          ReferenceOutputAssembly="false" />
                    </ItemGroup>
                    <Import Project="{copyTargets}" />
                </Project>
                """;
        }

        private TempProcessRunner.Result RunAssets(params string[] arguments)
            => TempProcessRunner.Run(
                "dotnet",
                arguments,
                AssetsProjectDirectory,
                timeoutMilliseconds: 600_000);

        private TempProcessRunner.Result RunApp(params string[] arguments)
            => TempProcessRunner.Run(
                "dotnet",
                arguments,
                AppProjectDirectory,
                timeoutMilliseconds: 600_000);

        private void WriteAsset(string relativePath, string content)
        {
            string fullPath = Path.Combine(
                AssetsProjectDirectory,
                "Assets",
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        private void WriteDlcAsset(string relativePath, string content)
        {
            string fullPath = Path.Combine(
                AssetsProjectDirectory,
                "DLC1",
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        private void WriteAssetBytes(string relativePath, byte[] bytes)
        {
            string fullPath = Path.Combine(
                AssetsProjectDirectory,
                "Assets",
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, bytes);
        }
    }
}
