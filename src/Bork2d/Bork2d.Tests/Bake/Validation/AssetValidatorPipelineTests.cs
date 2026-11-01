using Bork2d.Bake;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Engine.Assets;
using Bork2d.Tests.Bake.Baking;
using Xunit;

namespace Bork2d.Tests.Bake.Validation;

/// <summary>
/// The semantic-validation pipeline contract: every registered validator runs after binding and before
/// processor execution, index construction, composition, or staging; an error fails at the validation
/// stage and leaves every previous pack byte-identical, while warnings and infos do not fail; a
/// <c>--check</c> run validates and writes nothing; binding failures still stop before validation.
/// </summary>
public sealed class AssetValidatorPipelineTests
{
    private const string ErrorCode = "BK8090";
    private const string WarningCode = "BK8091";
    private const string InfoCode = "BK8092";
    private const string SpriteSourcePath = "Assets/sprites/player.toml";

    [Fact]
    public void ValidatorErrorFailsAtTheValidationStageWithAssetAndSourcePaths()
    {
        using var projectDirectory = TempBakeDirectory.Create();
        projectDirectory.CreateDirectory("Assets");
        projectDirectory.WriteFile("Assets/data/main.toml", GameToml(health: -1));
        var validator = ErrorWhen(asset => asset.Health < 0);
        BakeProject project = CreateGameConfigProject(
            projectDirectory.RootPath,
            [("assets.pck", "Assets")],
            validator);
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.Equal(BakeFailureStage.Validation, result.FailureStage);
        Assert.False(result.PackWritten);
        Assert.Equal(Path.Combine(output.RootPath, "assets.pck"), result.PackPath);
        BakeDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ErrorCode, diagnostic.Code);
        Assert.Equal(BakeSeverity.Error, diagnostic.Severity);
        Assert.Equal("Assets/data/main.toml", diagnostic.FilePath);
        Assert.Equal("data/main", diagnostic.AssetPath);
        Assert.Contains("data/main", diagnostic.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(output.RootPath, "assets.pck")));
        Assert.Empty(Directory.GetFiles(output.RootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void ValidatorErrorOnTheDlcPackLeavesThePublishedSetUnchangedAndWritesNoStaging()
    {
        using var projectDirectory = TempBakeDirectory.Create();
        projectDirectory.CreateDirectory("Assets");
        projectDirectory.CreateDirectory("DLC1");
        projectDirectory.WriteFile("Assets/data/main.toml", GameToml(health: 10));
        projectDirectory.WriteFile("DLC1/data/dlc.toml", GameToml(health: 11));
        var validator = ErrorWhen(asset => asset.Health == 13);
        BakeProject project = CreateGameConfigProject(
            projectDirectory.RootPath,
            [("assets.pck", "Assets"), ("dlc1.pck", "DLC1")],
            validator);
        using var output = TempBakeDirectory.Create();

        BakeResult first = RunBake(project, output.RootPath);
        Assert.Equal(BakeStatus.Success, first.Status);
        Assert.True(first.PackWritten);

        string mainPath = Path.Combine(output.RootPath, "assets.pck");
        string dlcPath = Path.Combine(output.RootPath, "dlc1.pck");
        byte[] mainBefore = File.ReadAllBytes(mainPath);
        byte[] dlcBefore = File.ReadAllBytes(dlcPath);

        // The main pack validates and is staged; the DLC pack then fails validation. The uncommitted
        // staged main pack must be removed and no target may change.
        projectDirectory.WriteFile("DLC1/data/dlc.toml", GameToml(health: 13));
        BakeResult failed = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, failed.Status);
        Assert.Equal(BakeFailureStage.Validation, failed.FailureStage);
        Assert.False(failed.PackWritten);
        Assert.Equal(dlcPath, failed.PackPath);
        BakeDiagnostic diagnostic = Assert.Single(failed.Diagnostics);
        Assert.Equal(ErrorCode, diagnostic.Code);
        Assert.Equal("DLC1/data/dlc.toml", diagnostic.FilePath);
        Assert.Equal("data/dlc", diagnostic.AssetPath);
        Assert.Equal(mainBefore, File.ReadAllBytes(mainPath));
        Assert.Equal(dlcBefore, File.ReadAllBytes(dlcPath));
        Assert.Empty(Directory.GetFiles(output.RootPath, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(output.RootPath, "*.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public void ValidatorWarningsAndInfosAreReportedWithoutFailingThePack()
    {
        using var projectDirectory = TempBakeDirectory.Create();
        projectDirectory.CreateDirectory("Assets");
        projectDirectory.WriteFile("Assets/data/main.toml", GameToml(health: 10));
        var validator = new LambdaGameConfigValidator(
            (assetPath, asset, diagnostics) =>
            {
                diagnostics.Warning(WarningCode, $"asset '{assetPath}' needs review");
                diagnostics.Info(InfoCode, $"asset '{assetPath}' was validated");
            });
        BakeProject project = CreateGameConfigProject(
            projectDirectory.RootPath,
            [("assets.pck", "Assets")],
            validator);
        using var output = TempBakeDirectory.Create();
        string packPath = Path.Combine(output.RootPath, "assets.pck");

        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.True(File.Exists(packPath));
        BakeDiagnostic warning = Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == WarningCode);
        Assert.Equal(BakeSeverity.Warning, warning.Severity);
        BakeDiagnostic info = Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == InfoCode);
        Assert.Equal(BakeSeverity.Info, info.Severity);
        Assert.All(
            result.Diagnostics,
            diagnostic =>
            {
                Assert.Equal("Assets/data/main.toml", diagnostic.FilePath);
                Assert.Equal("data/main", diagnostic.AssetPath);
            });
    }

    [Fact]
    public void EveryValidatorForAValidAssetRunsAndProducesNoDiagnostics()
    {
        using var projectDirectory = TempBakeDirectory.Create();
        projectDirectory.CreateDirectory("Assets");
        projectDirectory.WriteFile("Assets/data/main.toml", GameToml(health: 10));
        var first = new LambdaGameConfigValidator((_, _, _) => { });
        var second = new LambdaGameConfigValidator((_, _, _) => { });
        BakeProject project = CreateGameConfigProject(
            projectDirectory.RootPath,
            [("assets.pck", "Assets")],
            first,
            second);
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.True(result.PackWritten);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(new[] { "data/main" }, first.AssetPaths);
        Assert.Equal(new[] { "data/main" }, second.AssetPaths);
    }

    [Fact]
    public void CheckRunsValidatorsAndWritesNothing()
    {
        using var projectDirectory = TempBakeDirectory.Create();
        projectDirectory.CreateDirectory("Assets");
        projectDirectory.WriteFile("Assets/data/main.toml", GameToml(health: 10));
        var validator = ErrorWhen(asset => asset.Health < 0);
        BakeProject project = CreateGameConfigProject(
            projectDirectory.RootPath,
            [("assets.pck", "Assets")],
            validator);
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output.RootPath, check: true);

        Assert.Equal(BakeStatus.Success, result.Status);
        Assert.False(result.PackWritten);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(new[] { "data/main" }, validator.AssetPaths);
        Assert.Empty(Directory.GetFiles(output.RootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void CheckReportsValidatorErrorsAndWritesNothing()
    {
        using var projectDirectory = TempBakeDirectory.Create();
        projectDirectory.CreateDirectory("Assets");
        projectDirectory.WriteFile("Assets/data/main.toml", GameToml(health: -1));
        var validator = ErrorWhen(asset => asset.Health < 0);
        BakeProject project = CreateGameConfigProject(
            projectDirectory.RootPath,
            [("assets.pck", "Assets")],
            validator);
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output.RootPath, check: true);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.Equal(BakeFailureStage.Validation, result.FailureStage);
        Assert.False(result.PackWritten);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ErrorCode);
        Assert.Empty(Directory.GetFiles(output.RootPath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void BindingFailureIsStillReportedBeforeValidation()
    {
        using var projectDirectory = TempBakeDirectory.Create();
        projectDirectory.CreateDirectory("Assets");
        projectDirectory.WriteFile("Assets/data/main.toml", "name = \n");
        var validator = new LambdaGameConfigValidator((_, _, _) => { });
        BakeProject project = CreateGameConfigProject(
            projectDirectory.RootPath,
            [("assets.pck", "Assets")],
            validator);
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.Equal(BakeFailureStage.Sources, result.FailureStage);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.TomlSyntax);
        Assert.Empty(validator.AssetPaths);
    }

    [Fact]
    public void ExplicitSpriteValidatorAndTheBuiltInValidatorBothRun()
    {
        using var projectDirectory = TempBakeDirectory.Create();
        projectDirectory.CreateDirectory("Assets");
        projectDirectory.WriteFile(
            "Assets/sprites/player.toml",
            """
            uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]
            scale = [-1.0, 1.0]
            pivot = [0.5, 0.5]
            topology = "quad"

            [bork2d]
            type = "Bork2d.Engine.Assets.SpriteAsset"
            """);
        var explicitValidator = new LambdaSpriteValidator(
            (assetPath, asset, diagnostics) =>
                diagnostics.Info(InfoCode, $"asset '{assetPath}' was checked"));
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(projectDirectory.RootPath)
            .WithPack("assets.pck", "Assets")
            .UseDefaultAssets()
            .RegisterAsset<SpriteAsset>(explicitValidator);
        BakeProject project = CreateProject(builder);
        using var output = TempBakeDirectory.Create();

        BakeResult result = RunBake(project, output.RootPath);

        Assert.Equal(BakeStatus.DiagnosticsError, result.Status);
        Assert.Equal(BakeFailureStage.Validation, result.FailureStage);
        Assert.False(result.PackWritten);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == BakeDiagnosticCodes.SpriteScaleInvalid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == InfoCode);
        Assert.All(
            result.Diagnostics,
            diagnostic =>
            {
                Assert.Equal(SpriteSourcePath, diagnostic.FilePath);
                Assert.Equal("sprites/player", diagnostic.AssetPath);
            });
    }

    private static BakeProject CreateGameConfigProject(
        string projectRoot,
        IReadOnlyList<(string Pack, string Root)> packs,
        params IAssetValidator<GameConfig>[] validators)
    {
        BakeApplicationBuilder builder = new BakeApplicationBuilder()
            .WithProjectRoot(projectRoot);

        foreach ((string pack, string root) in packs)
        {
            builder.WithPack(pack, root);
        }

        builder.UseDefaultAssets();
        foreach (IAssetValidator<GameConfig> validator in validators)
        {
            builder.RegisterAsset(validator);
        }

        return CreateProject(builder);
    }

    private static BakeProject CreateProject(BakeApplicationBuilder builder)
    {
        var diagnostics = new BakeDiagnostics();
        BakeProject? project = builder.CreateProject(diagnostics);
        if (project is null || diagnostics.HasErrors)
        {
            throw new InvalidOperationException(
                "The test project is invalid:" + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        diagnostics.GetSorted().Select(diagnostic => diagnostic.Format())));
        }

        return project;
    }

    private static BakeResult RunBake(BakeProject project, string outputRoot, bool check = false)
        => BakePipeline.Run(
            project,
            new BakeOptions(Path.GetFullPath(outputRoot), BakeMode.Debug, check, force: false),
            new BakeDiagnostics());

    private static string GameToml(int health)
        => $"""
            name = "config"
            health = {health}
            must = "present"

            [bork2d]
            type = "Bork2d.Tests.Bake.Baking.GameConfig"
            """;

    private static LambdaGameConfigValidator ErrorWhen(Func<GameConfig, bool> predicate)
        => new(
            (assetPath, asset, diagnostics) =>
            {
                if (predicate(asset))
                {
                    diagnostics.Error(
                        ErrorCode,
                        $"asset '{assetPath}' has invalid health {asset.Health}");
                }
            });

    private sealed class LambdaGameConfigValidator : IAssetValidator<GameConfig>
    {
        private readonly Action<string, GameConfig, IAssetProcessorDiagnostics> _validate;

        internal LambdaGameConfigValidator(
            Action<string, GameConfig, IAssetProcessorDiagnostics> validate)
            => _validate = validate;

        internal List<string> AssetPaths { get; } = [];

        public void Validate(
            string assetPath,
            GameConfig asset,
            IAssetProcessorDiagnostics diagnostics)
        {
            AssetPaths.Add(assetPath);
            _validate(assetPath, asset, diagnostics);
        }
    }

    private sealed class LambdaSpriteValidator : IAssetValidator<SpriteAsset>
    {
        private readonly Action<string, SpriteAsset, IAssetProcessorDiagnostics> _validate;

        internal LambdaSpriteValidator(
            Action<string, SpriteAsset, IAssetProcessorDiagnostics> validate)
            => _validate = validate;

        public void Validate(
            string assetPath,
            SpriteAsset asset,
            IAssetProcessorDiagnostics diagnostics)
            => _validate(assetPath, asset, diagnostics);
    }
}
