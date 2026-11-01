using Bork2d.Bake;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Engine.Assets;
using Bork2d.Tests.Bake.Baking;
using Xunit;

namespace Bork2d.Tests.Bake.Validation;

/// <summary>
/// The public validator-registration contract: <c>RegisterAsset&lt;T&gt;(validator)</c> registers the
/// type without a duplicate diagnostic and appends validators in registration order,
/// <c>RegisterAsset&lt;T&gt;()</c> keeps its duplicate diagnostic, and <c>UseDefaultAssets()</c>
/// attaches the built-in sprite validator exactly once.
/// </summary>
public sealed class AssetValidatorRegistrationTests
{
    private const string GameConfigTypeName = "Bork2d.Tests.Bake.Baking.GameConfig";
    private const string SpriteTypeName = "Bork2d.Engine.Assets.SpriteAsset";

    [Fact]
    public void RegisterAssetWithValidatorRegistersTheTypeWithoutADuplicateDiagnostic()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = CreateBuilder(directory)
            .RegisterAsset<GameConfig>(new FirstGameConfigValidator());

        (BakeProject project, IReadOnlyList<BakeDiagnostic> diagnostics) = CreateProject(builder);

        Assert.Empty(diagnostics);
        Assert.Contains(GameConfigTypeName, project.AssetTypes.Keys);
        Assert.Equal(
            new[] { typeof(FirstGameConfigValidator) },
            ValidatorTypes(project, GameConfigTypeName));
    }

    [Fact]
    public void RegisterAssetWithValidatorAppendsToAnExistingTypeInRegistrationOrder()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = CreateBuilder(directory)
            .RegisterAsset<GameConfig>()
            .RegisterAsset<GameConfig>(new ThirdGameConfigValidator())
            .RegisterAsset<GameConfig>(new FirstGameConfigValidator())
            .RegisterAsset<GameConfig>(new SecondGameConfigValidator());

        (BakeProject project, IReadOnlyList<BakeDiagnostic> diagnostics) = CreateProject(builder);

        Assert.Empty(diagnostics);
        Assert.Equal(
            new[]
            {
                typeof(ThirdGameConfigValidator),
                typeof(FirstGameConfigValidator),
                typeof(SecondGameConfigValidator),
            },
            ValidatorTypes(project, GameConfigTypeName));
    }

    [Fact]
    public void PlainDuplicateRegistrationStillReportsBK1010AfterAValidatorRegistration()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = CreateBuilder(directory)
            .RegisterAsset<GameConfig>(new FirstGameConfigValidator())
            .RegisterAsset<GameConfig>();

        (BakeProject project, IReadOnlyList<BakeDiagnostic> diagnostics) = CreateProject(builder);

        BakeDiagnostic diagnostic = Assert.Single(
            diagnostics,
            candidate => candidate.Code == BakeDiagnosticCodes.AssetTypeDuplicate);
        Assert.Equal(BakeSeverity.Error, diagnostic.Severity);
        Assert.Contains(GameConfigTypeName, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(
            new[] { typeof(FirstGameConfigValidator) },
            ValidatorTypes(project, GameConfigTypeName));
    }

    [Fact]
    public void NullValidatorIsAProgrammerError()
    {
        var builder = new BakeApplicationBuilder();

        Assert.Throws<ArgumentNullException>(
            () => builder.RegisterAsset<GameConfig>(null!));
        Assert.Throws<ArgumentNullException>(
            () => builder.RegisterAsset<SpriteAsset>(null!));
    }

    [Fact]
    public void UseDefaultAssetsAttachesTheBuiltInSpriteValidatorOnce()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = CreateBuilder(directory)
            .UseDefaultAssets()
            .UseDefaultAssets();

        (BakeProject project, IReadOnlyList<BakeDiagnostic> diagnostics) = CreateProject(builder);

        Assert.Empty(diagnostics);
        Assert.Equal(
            new[] { typeof(SpriteAssetValidator) },
            ValidatorTypes(project, SpriteTypeName));
    }

    [Fact]
    public void UseDefaultAssetsAppendsTheBuiltInValidatorToAnExplicitSpriteRegistration()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = CreateBuilder(directory)
            .RegisterAsset<SpriteAsset>()
            .UseDefaultAssets();

        (BakeProject project, IReadOnlyList<BakeDiagnostic> diagnostics) = CreateProject(builder);

        Assert.Empty(diagnostics);
        Assert.Equal(
            new[] { typeof(SpriteAssetValidator) },
            ValidatorTypes(project, SpriteTypeName));
    }

    [Fact]
    public void ExplicitSpriteValidatorAndTheBuiltInValidatorKeepTheirRegistrationOrder()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = CreateBuilder(directory)
            .RegisterAsset<SpriteAsset>(new ReferenceSpriteValidator())
            .UseDefaultAssets()
            .RegisterAsset<SpriteAsset>(new LateSpriteValidator());

        (BakeProject project, IReadOnlyList<BakeDiagnostic> diagnostics) = CreateProject(builder);

        Assert.Empty(diagnostics);
        Assert.Equal(
            new[]
            {
                typeof(ReferenceSpriteValidator),
                typeof(SpriteAssetValidator),
                typeof(LateSpriteValidator),
            },
            ValidatorTypes(project, SpriteTypeName));
    }

    [Fact]
    public void ExplicitValidatorRegisteredAfterUseDefaultAssetsIsAppended()
    {
        using var directory = TempBakeDirectory.Create();
        directory.CreateDirectory("Assets");
        BakeApplicationBuilder builder = CreateBuilder(directory)
            .UseDefaultAssets()
            .RegisterAsset<SpriteAsset>(new ReferenceSpriteValidator());

        (BakeProject project, IReadOnlyList<BakeDiagnostic> diagnostics) = CreateProject(builder);

        Assert.Empty(diagnostics);
        Assert.Equal(
            new[] { typeof(SpriteAssetValidator), typeof(ReferenceSpriteValidator) },
            ValidatorTypes(project, SpriteTypeName));
    }

    private static BakeApplicationBuilder CreateBuilder(TempBakeDirectory directory)
        => new BakeApplicationBuilder()
            .WithProjectRoot(directory.RootPath)
            .WithPack("assets.pck", "Assets");

    private static (BakeProject Project, IReadOnlyList<BakeDiagnostic> Diagnostics) CreateProject(
        BakeApplicationBuilder builder)
    {
        var diagnostics = new BakeDiagnostics();
        BakeProject? project = builder.CreateProject(diagnostics);
        if (project is null)
        {
            throw new InvalidOperationException(
                "The test project could not be created:" + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        diagnostics.GetSorted().Select(diagnostic => diagnostic.Format())));
        }

        return (project, diagnostics.GetSorted());
    }

    private static Type[] ValidatorTypes(BakeProject project, string typeName)
        => [.. project.AssetTypes[typeName].Validators.Select(validator => validator.ValidatorType)];

    private sealed class FirstGameConfigValidator : IAssetValidator<GameConfig>
    {
        public void Validate(
            string assetPath,
            GameConfig asset,
            IAssetProcessorDiagnostics diagnostics)
        {
        }
    }

    private sealed class SecondGameConfigValidator : IAssetValidator<GameConfig>
    {
        public void Validate(
            string assetPath,
            GameConfig asset,
            IAssetProcessorDiagnostics diagnostics)
        {
        }
    }

    private sealed class ThirdGameConfigValidator : IAssetValidator<GameConfig>
    {
        public void Validate(
            string assetPath,
            GameConfig asset,
            IAssetProcessorDiagnostics diagnostics)
        {
        }
    }

    private sealed class ReferenceSpriteValidator : IAssetValidator<SpriteAsset>
    {
        public void Validate(
            string assetPath,
            SpriteAsset asset,
            IAssetProcessorDiagnostics diagnostics)
        {
        }
    }

    private sealed class LateSpriteValidator : IAssetValidator<SpriteAsset>
    {
        public void Validate(
            string assetPath,
            SpriteAsset asset,
            IAssetProcessorDiagnostics diagnostics)
        {
        }
    }
}
