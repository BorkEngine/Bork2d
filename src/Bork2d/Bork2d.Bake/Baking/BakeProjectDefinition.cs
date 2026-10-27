using Bork2d.Api.Assets;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Processors;
using Bork2d.Bake.Textures;
using PolyType;

namespace Bork2d.Bake.Baking;

/// <summary>
/// The mutable builder-side configuration model. <see cref="BakeApplicationBuilder"/> fills it and
/// <see cref="BakeProjectFactory"/> validates it into an immutable <see cref="BakeProject"/>.
/// </summary>
/// <remarks>
/// <para>
/// The definition records the order of packs and processors, the explicit data-type registrations,
/// the texture defaults, and any semantic configuration issue found while the builder is being
/// configured (for example a duplicate <c>RegisterAsset&lt;T&gt;()</c>). The builder never throws for
/// a semantic error; the issues are surfaced by the command handler as <c>BK1xxx</c> diagnostics with
/// exit code <c>2</c>.
/// </para>
/// <para>
/// Programmer errors (a null or empty method argument) are thrown immediately by the public builder;
/// this model assumes non-empty path strings.
/// </para>
/// </remarks>
internal sealed class BakeProjectDefinition
{
    private readonly List<BakePackDefinition> _packs = [];
    private readonly Dictionary<string, BakeAssetTypeRegistration> _assetTypes = new(StringComparer.Ordinal);
    private readonly List<IBakeAssetProcessor> _processors = [];
    private readonly List<BakeDiagnostic> _issues = [];

    /// <summary>Gets or sets the configured project root, or <see langword="null"/> when unset.</summary>
    internal string? ProjectRoot { get; private set; }

    /// <summary>Gets the configured packs in builder order.</summary>
    internal IReadOnlyList<BakePackDefinition> Packs => _packs;

    /// <summary>Gets the explicit data-type registrations keyed by full type name.</summary>
    internal IReadOnlyDictionary<string, BakeAssetTypeRegistration> AssetTypes => _assetTypes;

    /// <summary>Gets the registered processors in registration order.</summary>
    internal IReadOnlyList<IBakeAssetProcessor> Processors => _processors;

    /// <summary>Gets a value indicating whether <c>UseDefaultProcessors()</c> was called.</summary>
    internal bool UseDefaultProcessors { get; private set; }

    /// <summary>Gets the default sampler filter for textures without a sidecar.</summary>
    internal BakeTextureFilter TextureFilter { get; private set; } = BakeTextureFilter.Linear;

    /// <summary>Gets the default sampler wrap mode for textures without a sidecar.</summary>
    internal BakeTextureWrap TextureWrap { get; private set; } = BakeTextureWrap.Clamp;

    /// <summary>Gets the semantic configuration issues found while building the definition.</summary>
    internal IReadOnlyList<BakeDiagnostic> Issues => _issues;

    /// <summary>Sets the base for relative pack roots and diagnostic display paths.</summary>
    /// <param name="path">The project root path.</param>
    internal void SetProjectRoot(string path) => ProjectRoot = path;

    /// <summary>Adds one ordered pack; the first added pack is the main pack.</summary>
    /// <param name="packPath">The pack output path relative to the output root.</param>
    /// <param name="root">The asset root relative to the project root.</param>
    internal void AddPack(string packPath, string root)
        => _packs.Add(new BakePackDefinition(packPath, root));

    /// <summary>Registers one explicit data type.</summary>
    /// <typeparam name="T">The shapeable data type.</typeparam>
    internal void AddAsset<T>()
        where T : IShapeable<T>
    {
        BakeAssetTypeRegistration registration = BakeAssetTypeRegistration.Create<T>();
        if (_assetTypes.TryAdd(registration.TypeName, registration))
        {
            return;
        }

        AddIssue(
            BakeDiagnosticCodes.AssetTypeDuplicate,
            $"The data type '{registration.TypeName}' is registered more than once; call "
                + "RegisterAsset<T>() exactly once per type.");
    }

    /// <summary>Registers <c>Bork2d.Api.Assets.SpriteAsset</c>; idempotent.</summary>
    internal void UseDefaultAssets()
    {
        string spriteTypeName = typeof(SpriteAsset).FullName
            ?? throw new InvalidOperationException("SpriteAsset has no full name.");

        if (!_assetTypes.ContainsKey(spriteTypeName))
        {
            AddAsset<SpriteAsset>();
        }
    }

    /// <summary>Records that the default processor registration was requested; idempotent.</summary>
    internal void RequestDefaultProcessors() => UseDefaultProcessors = true;

    /// <summary>Adds one registered processor.</summary>
    /// <param name="processor">The internal processor adapter.</param>
    internal void AddProcessor(IBakeAssetProcessor processor) => _processors.Add(processor);

    /// <summary>Sets the texture defaults used when a texture sidecar is missing.</summary>
    /// <param name="filter">The default sampler filter.</param>
    /// <param name="wrap">The default sampler wrap mode.</param>
    internal void WithTextureDefaults(BakeTextureFilter filter, BakeTextureWrap wrap)
    {
        TextureFilter = filter;
        TextureWrap = wrap;
    }

    /// <summary>Records one semantic configuration issue.</summary>
    /// <param name="code">The stable <c>BK1xxx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    internal void AddIssue(string code, string message)
        => _issues.Add(new BakeDiagnostic(
            BakeSeverity.Error,
            code,
            message,
            FilePath: null,
            Line: null,
            Column: null,
            AssetPath: null,
            MemberPath: null));
}
