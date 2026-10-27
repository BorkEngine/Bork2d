using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Project;
using Xunit;

namespace Bork2d.Tests.Bake.Hosting;

/// <summary>
/// Shares one built fixture project and one built bake host across the host tests, so the slow
/// <c>dotnet build</c> steps run once per test collection. Binary fixtures are generated into a
/// temporary directory and never committed.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BakeHostFixtureCollection : ICollectionFixture<TempBakeFixture>
{
    /// <summary>The collection name the host tests join.</summary>
    public const string Name = "Bake host fixture";
}

/// <summary>
/// A temporary <c>Project.Common</c>-like library plus a Q-04 data library, an asset tree, a
/// generated host, and the loaded manifest.
/// </summary>
/// <remarks>
/// The library sources are written as strings into the OS temporary directory and built with the
/// pinned SDK. The fixture covers a public mapped class, a readonly record struct root, an
/// internal mapped type with <c>InternalsVisibleTo("Bork2d.BakeHost")</c>, a mapped type in a
/// referenced library (Q-04), <c>Bork2d.Api.Assets.SpriteAsset</c>, and the failure shapes the type
/// resolver must reject (missing, ambiguous, assembly-qualified, nested, generic, abstract, static,
/// enum, non-shapeable, and internal without a friend grant).
/// </remarks>
public sealed class TempBakeFixture : IDisposable
{
    /// <summary>The descriptor file name.</summary>
    internal const string DescriptorFileName = "bork2d.project.toml";

    private const string ProjectCommonTypes = """
        using System.Runtime.CompilerServices;
        using System.Text.Json.Serialization;
        using Bork2d.Api.Assets;
        using Bork2d.Api.Rendering;
        using PolyType;

        [assembly: InternalsVisibleTo("Bork2d.BakeHost")]

        namespace BakeFixture.Common
        {
            [GenerateShape]
            public sealed partial class GameConfig
            {
                public required string Name { get; set; }

                public int Health { get; set; }

                public Stats Stats { get; set; }

                public Color Tint { get; set; }

                public AssetRef<TextureDescriptor> Texture { get; set; }

                public Game.Data.Weapon? StarterWeapon { get; set; }

                [JsonPropertyName("max_players")]
                public int MaxPlayers { get; set; }

                [JsonIgnore]
                public string? DebugLabel { get; set; }

                [JsonRequired]
                public string? Must { get; set; }
            }

            [GenerateShape]
            public readonly partial record struct Stats(int Attack, int Defense);

            [GenerateShape]
            public readonly partial record struct StatsRoot(int Attack, int Defense);

            [GenerateShape]
            internal sealed partial class HiddenConfig
            {
                public int Value { get; set; }
            }

            public sealed class NoShape
            {
                public int Value { get; set; }
            }

            public abstract class AbstractRoot
            {
            }

            public static class StaticRoot
            {
            }

            public sealed class GenericRoot<T>
            {
                public T? Value { get; set; }
            }

            public static class Outer
            {
                public sealed class Inner
                {
                }
            }

            public enum Mood
            {
                Calm = 0,
                Angry = 1,
            }
        }

        namespace Game.Data
        {
            [GenerateShape]
            public sealed partial class DupType
            {
                public int Value { get; set; }
            }
        }
        """;

    private const string GameDataTypes = """
        using PolyType;

        namespace Game.Data
        {
            [GenerateShape]
            public sealed partial class Weapon
            {
                public required string Name { get; set; }

                public int Damage { get; set; }
            }

            [GenerateShape]
            internal sealed partial class HiddenWeapon
            {
                public int Damage { get; set; }
            }

            [GenerateShape]
            public sealed partial class DupType
            {
                public int Value { get; set; }
            }
        }
        """;

    private const string Descriptor = """
        schema = 1
        name = "BakeFixture"

        [assets]
        root = "Assets"

        [bake]
        host = "host"
        """;

    private static readonly (string RelativePath, string Content)[] _assets =
    [
        ("data/config.toml", """
            name = "campaign"
            health = 100
            max_players = 4
            must = "present"
            tint = [1.0, 0.5, 0.25, 1.0]
            texture = "textures/player"

            [stats]
            attack = 1
            defense = 2

            [bork2d]
            type = "BakeFixture.Common.GameConfig"
            """),
        ("roots/stats.toml", """
            attack = 7
            defense = 9

            [bork2d]
            type = "BakeFixture.Common.StatsRoot"
            """),
        ("hidden/secret.toml", """
            value = 5

            [bork2d]
            type = "BakeFixture.Common.HiddenConfig"
            """),
        ("weapons/sword.toml", """
            name = "sword"
            damage = 12

            [bork2d]
            type = "Game.Data.Weapon"
            """),
        ("sprites/player.toml", """
            texture = "textures/player"
            uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]
            scale = 16.0
            pivot = [0.5, 0.5]
            topology = "triangles"
            indices = [0, 1, 2, 0, 2, 3]

            [bork2d]
            type = "Bork2d.Api.Assets.SpriteAsset"
            """),
        ("textures/player.png.toml", """
            filter = "nearest"
            """),
    ];

    private readonly string _rootPath;

    /// <summary>Builds the fixture libraries, generates the host, builds it, and loads it.</summary>
    public TempBakeFixture()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"bork2d-bake-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);

        string apiPath = typeof(Bork2d.Api.Assets.IAssets).Assembly.Location;
        WriteFile("Game.Data/Game.Data.csproj", LibraryProject("Game.Data", apiPath));
        WriteFile("Game.Data/Types.cs", GameDataTypes);
        WriteFile("Project.Common/Project.Common.csproj", CommonProject(apiPath));
        WriteFile("Project.Common/Types.cs", ProjectCommonTypes);
        WriteFile(DescriptorFileName, Descriptor);

        foreach ((string relativePath, string content) in _assets)
        {
            WriteFile(Path.Combine("Assets", relativePath), content);
        }

        BuildProject("Project.Common/Project.Common.csproj");

        var diagnostics = new BakeDiagnostics();
        var options = new HostGenerationOptions(
            DescriptorPath,
            ProjectCommonAssemblyPath,
            HostDirectory,
            "Debug",
            force: true);
        HostGenerationResult? generation = HostGenerator.Generate(options, diagnostics);
        if (generation is null)
        {
            throw new InvalidOperationException(
                "The fixture bake host could not be generated:" + Environment.NewLine
                    + RenderDiagnostics(diagnostics));
        }

        TempProcessRunner.Result hostBuild = TempProcessRunner.Run(
            "dotnet",
            ["build", generation.ProjectPath, "-c", "Debug", "-warnaserror", "-nologo"],
            _rootPath);
        if (hostBuild.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "The fixture bake host did not build:" + Environment.NewLine + hostBuild.Output);
        }

        var loadDiagnostics = new BakeDiagnostics();
        Handle = BakeHostLoader.TryLoad(HostAssemblyPath, loadDiagnostics)
            ?? throw new InvalidOperationException(
                "The fixture bake host could not be loaded:" + Environment.NewLine
                    + RenderDiagnostics(loadDiagnostics));

        var descriptorDiagnostics = new BakeDiagnostics();
        DescriptorDescriptor = ProjectDescriptorParser.Parse(DescriptorPath, descriptorDiagnostics)
            ?? throw new InvalidOperationException(
                "The fixture descriptor is invalid:" + Environment.NewLine
                    + RenderDiagnostics(descriptorDiagnostics));
    }

    /// <summary>Gets the fixture root directory.</summary>
    internal string RootPath => _rootPath;

    /// <summary>Gets the descriptor path.</summary>
    internal string DescriptorPath => Path.Combine(_rootPath, DescriptorFileName);

    /// <summary>Gets the asset root.</summary>
    internal string AssetsRoot => Path.Combine(_rootPath, "Assets");

    /// <summary>Gets the built <c>Project.Common</c> assembly path.</summary>
    internal string ProjectCommonAssemblyPath
        => Path.Combine(_rootPath, "Project.Common", "bin", "Debug", "net11.0", "Project.Common.dll");

    /// <summary>Gets the generated host directory.</summary>
    internal string HostDirectory => Path.Combine(_rootPath, "host");

    /// <summary>Gets the generated host project path.</summary>
    internal string HostProjectPath => Path.Combine(HostDirectory, "BakeHost.csproj");

    /// <summary>Gets the generated manifest path.</summary>
    internal string HostManifestPath => Path.Combine(HostDirectory, "BakeManifest.g.cs");

    /// <summary>Gets the generated host assembly path.</summary>
    internal string HostAssemblyPath
        => Path.Combine(HostDirectory, "bin", "Debug", "net11.0", "Bork2d.BakeHost.dll");

    /// <summary>Gets the loaded host handle.</summary>
    internal BakeHostHandle Handle { get; }

    /// <summary>Gets the loaded manifest.</summary>
    internal IBakeManifest Manifest => Handle.Manifest;

    /// <summary>Gets the parsed fixture descriptor.</summary>
    internal ProjectDescriptor DescriptorDescriptor { get; }

    /// <summary>Gets the descriptor text used by the fixture, for copied projects.</summary>
    internal static string DescriptorText => Descriptor;

    /// <summary>
    /// Creates an independent project directory containing every fixture source, so the shared
    /// fixture host's type manifest matches the discovered declarations.
    /// </summary>
    /// <param name="name">A folder-name fragment that identifies the test.</param>
    /// <returns>The new project directory.</returns>
    internal string CreateStandardProject(string name)
        => CreateProjectCopy(name, _assets);

    /// <summary>
    /// Creates an independent project directory with the fixture mappings so a CLI test can mutate
    /// its sources without disturbing the shared fixture.
    /// </summary>
    /// <param name="name">A folder-name fragment that identifies the test.</param>
    /// <param name="assets">The asset files to write, relative to <c>Assets/</c>.</param>
    /// <returns>The new project directory.</returns>
    internal string CreateProjectCopy(
        string name,
        params (string RelativePath, string Content)[] assets)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"bork2d-bake-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "Assets"));
        File.WriteAllText(Path.Combine(directory, DescriptorFileName), Descriptor);

        foreach ((string relativePath, string content) in assets)
        {
            string fullPath = Path.Combine(directory, "Assets", relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        return directory;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // The non-collectible host load context keeps the host and Project.Common memory-mapped on
        // Windows. The OS temp cleaner owns a directory that cannot be removed here.
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

    private static string LibraryProject(string assemblyName, string apiPath) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <AssemblyName>{assemblyName}</AssemblyName>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
            </PropertyGroup>
            <ItemGroup>
                <PackageReference Include="PolyType" Version="1.4.1" />
                <Reference Include="Bork2d.Api">
                    <HintPath>{apiPath}</HintPath>
                    <Private>true</Private>
                </Reference>
            </ItemGroup>
        </Project>
        """;

    private static string CommonProject(string apiPath) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <AssemblyName>Project.Common</AssemblyName>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
            </PropertyGroup>
            <ItemGroup>
                <PackageReference Include="PolyType" Version="1.4.1" />
                <Reference Include="Bork2d.Api">
                    <HintPath>{apiPath}</HintPath>
                    <Private>true</Private>
                </Reference>
            </ItemGroup>
            <ItemGroup>
                <ProjectReference Include="..\Game.Data\Game.Data.csproj" />
            </ItemGroup>
        </Project>
        """;

    private void BuildProject(string relativeProjectPath)
    {
        TempProcessRunner.Result result = TempProcessRunner.Run(
            "dotnet",
            ["build", relativeProjectPath, "-c", "Debug", "-nologo"],
            _rootPath);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The fixture project '{relativeProjectPath}' did not build:"
                    + Environment.NewLine + result.Output);
        }
    }

    private void WriteFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(_rootPath, relativePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }
}
