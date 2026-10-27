using Bork2d.Bake.Baking;
using Bork2d.Bake.Builder;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Baking;

/// <summary>
/// The shared bake-project fixture: canonical paths, standard source TOML, and helpers that build the
/// internal <see cref="BakeProject"/> or the public <see cref="BakeApplication"/> for the standard
/// fixture configuration.
/// </summary>
public static class BakeProjectFixture
{
    /// <summary>The canonical class-root asset path.</summary>
    internal const string GamePath = "data/game";

    /// <summary>The canonical struct-root asset path.</summary>
    internal const string StatsPath = "roots/stats";

    /// <summary>The canonical sprite asset path.</summary>
    internal const string SpritePath = "sprites/player";

    /// <summary>The canonical texture descriptor path (arbitrary <c>art/</c> location).</summary>
    internal const string TexturePath = "art/player";

    /// <summary>The canonical raw texture payload path.</summary>
    internal const string TexturePayloadPath = "art/player-raw";

    /// <summary>The canonical path of the DLC custom data (the DLC pack's only entry).</summary>
    internal const string DlcGamePath = "data/dlc";

    /// <summary>The exact RGBA8 payload the test-generated PNG decodes to.</summary>
    internal static readonly byte[] ExpectedTexturePayload =
    [
        255, 0, 0, 255,
        0, 255, 0, 128,
        0, 0, 255, 64,
        255, 255, 255, 0,
    ];

    /// <summary>The standard class-root source.</summary>
    internal const string GameToml = """
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

    /// <summary>The standard struct-root source.</summary>
    internal const string StatsToml = """
        attack = 11
        defense = 13

        [bork2d]
        type = "Bork2d.BakeFixture.StatsRoot"
        """;

    /// <summary>The standard sprite source.</summary>
    internal const string SpriteToml = """
        texture = "art/player"
        uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]
        scale = 16.0
        pivot = [0.5, 0.5]
        topology = "triangles"
        indices = [0, 1, 2, 0, 2, 3]

        [bork2d]
        type = "Bork2d.Api.Assets.SpriteAsset"
        """;

    /// <summary>The standard texture sidecar.</summary>
    internal const string TextureSidecarToml = """
        filter = "nearest"
        wrap = "repeat"
        """;

    /// <summary>Builds the internal project configuration for a temporary project.</summary>
    /// <param name="projectDirectory">The project root.</param>
    /// <param name="packs">The ordered <c>(pack path, root)</c> entries.</param>
    /// <param name="registerFixtureTypes">Whether to register the fixture data types.</param>
    /// <returns>The validated project.</returns>
    internal static BakeProject CreateProject(
        string projectDirectory,
        IReadOnlyList<(string Pack, string Root)> packs,
        bool registerFixtureTypes = true)
    {
        var definition = new BakeProjectDefinition();
        definition.SetProjectRoot(projectDirectory);
        foreach ((string pack, string root) in packs)
        {
            definition.AddPack(pack, root);
        }

        definition.UseDefaultAssets();
        if (registerFixtureTypes)
        {
            definition.AddAsset<Bork2d.BakeFixture.GameConfig>();
            definition.AddAsset<Bork2d.BakeFixture.StatsRoot>();
        }

        definition.RequestDefaultProcessors();
        definition.WithTextureDefaults(BakeTextureFilter.Linear, BakeTextureWrap.Clamp);

        var diagnostics = new BakeDiagnostics();
        if (!BakeProjectFactory.TryCreate(definition, null, diagnostics, out BakeProject? project))
        {
            throw new InvalidOperationException(
                "The test bake project is invalid:" + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        diagnostics.GetSorted().Select(diagnostic => diagnostic.Format())));
        }

        return project;
    }

    /// <summary>Builds the public application for the standard fixture configuration.</summary>
    /// <param name="projectDirectory">The project root.</param>
    /// <param name="packs">The ordered <c>(pack path, root)</c> entries.</param>
    /// <param name="registerFixtureTypes">Whether to register the fixture data types.</param>
    /// <returns>The configured application.</returns>
    internal static BakeApplication CreateApplication(
        string projectDirectory,
        IReadOnlyList<(string Pack, string Root)> packs,
        bool registerFixtureTypes = true)
    {
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(projectDirectory);

        foreach ((string pack, string root) in packs)
        {
            builder.WithPack(pack, root);
        }

        builder.UseDefaultAssets();
        if (registerFixtureTypes)
        {
            builder
                .RegisterAsset<Bork2d.BakeFixture.GameConfig>()
                .RegisterAsset<Bork2d.BakeFixture.StatsRoot>();
        }

        return builder
            .UseDefaultProcessors()
            .WithTextureDefaults(TextureFilter.Linear, TextureWrap.Clamp)
            .Build();
    }

}
