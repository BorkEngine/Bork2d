using Bork2d.Engine.Assets;
using Bork2d.Render.Shaders;
using Silk.NET.Vulkan;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A deterministic <see cref="IShaderFrameSource"/> for GPU-free planner tests: it maps a frozen
/// <see cref="ShaderRenderData"/> to a pending, ready, or failed binding without Vulkan or I/O.
/// </summary>
/// <remarks>
/// Both default entries always exist and are ready: <c>shader/sprite</c> is entry <c>0</c> and
/// <c>shader/line</c> is entry <c>1</c>. Tests register custom entries with <see cref="Register"/>
/// and can replace either default's metadata to model a game pack that shadows the built-in shader
/// with declarations. An unregistered custom shader resolves as pending by default, so a test must
/// opt every custom asset into a state. A failed binding resolves to the requesting kind's default
/// entry, matching the production catalog's fallback.
/// </remarks>
internal sealed class FakeShaderFrameSource : IShaderFrameSource
{
    private readonly Dictionary<ShaderRenderData, ShaderBinding> _bindings =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ShaderRenderData, int> _entryIds =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, ShaderEntry> _entries = [];
    private int _nextEntryId = ShaderStore.FirstCustomEntryId;

    /// <summary>Initializes a new instance of the <see cref="FakeShaderFrameSource"/> class.</summary>
    internal FakeShaderFrameSource()
    {
        DefaultMetadata = ShaderRenderData.Freeze(new ShaderAsset
        {
            SpirV = ShaderStore.DefaultSpirVPath,
            LayoutVersion = ShaderLayout.CurrentVersion,
            EntryPoint = "main",
        });
        LineDefaultMetadata = ShaderRenderData.Freeze(new ShaderAsset
        {
            SpirV = ShaderStore.LineSpirVPath,
            LayoutVersion = ShaderLayout.CurrentVersion,
            EntryPoint = "main",
        });
        _entries.Add(
            DefaultEntryId,
            new ShaderEntry(
                DefaultEntryId,
                ShaderStore.DefaultShaderPath,
                DefaultMetadata,
                isDefault: true));
        _entries.Add(
            LineDefaultEntryId,
            new ShaderEntry(
                LineDefaultEntryId,
                ShaderStore.LineShaderPath,
                LineDefaultMetadata,
                isDefault: true));
    }

    /// <summary>Gets the entry id of the built-in sprite default.</summary>
    internal int DefaultEntryId => ShaderStore.DefaultEntryId;

    /// <summary>Gets the entry id of the built-in line default.</summary>
    internal int LineDefaultEntryId => ShaderStore.LineDefaultEntryId;

    /// <summary>Gets the current sprite default entry metadata.</summary>
    internal ShaderRenderData DefaultMetadata { get; private set; }

    /// <summary>Gets the current line default entry metadata.</summary>
    internal ShaderRenderData LineDefaultMetadata { get; private set; }

    /// <summary>Replaces the sprite default entry's declarations (a shadowed <c>shader/sprite</c>).</summary>
    /// <param name="asset">The shader asset whose frozen declarations become the default entry's.</param>
    internal void SetDefaultMetadata(ShaderAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        DefaultMetadata = ShaderRenderData.Freeze(asset);
        _entries[DefaultEntryId] = new ShaderEntry(
            DefaultEntryId,
            ShaderStore.DefaultShaderPath,
            DefaultMetadata,
            isDefault: true);
    }

    /// <summary>Replaces the line default entry's declarations (a shadowed <c>shader/line</c>).</summary>
    /// <param name="asset">The shader asset whose frozen declarations become the default entry's.</param>
    internal void SetLineDefaultMetadata(ShaderAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        LineDefaultMetadata = ShaderRenderData.Freeze(asset);
        _entries[LineDefaultEntryId] = new ShaderEntry(
            LineDefaultEntryId,
            ShaderStore.LineShaderPath,
            LineDefaultMetadata,
            isDefault: true);
    }

    /// <summary>
    /// Freezes a custom shader asset, registers its entry, and returns the metadata a snapshot
    /// carries.
    /// </summary>
    /// <param name="asset">The custom shader asset.</param>
    /// <param name="state">The binding state to resolve for that metadata.</param>
    /// <param name="entryId">The entry id; a new id is assigned when negative.</param>
    /// <returns>The frozen metadata to assign to a snapshot's shader field.</returns>
    internal ShaderRenderData Register(
        ShaderAsset asset,
        ShaderBindingState state,
        int entryId = -1)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ShaderRenderData metadata = ShaderRenderData.Freeze(asset);
        int id = entryId >= 0 ? entryId : _nextEntryId++;
        string path = asset.SpirV.Path ?? $"test/shader-{id}";
        _entries[id] = new ShaderEntry(id, path, metadata, isDefault: false);
        _bindings[metadata] = new ShaderBinding(id, state);
        _entryIds[metadata] = id;
        return metadata;
    }

    /// <summary>Gets the registered entry id for a frozen custom shader metadata instance.</summary>
    /// <param name="metadata">The metadata returned by <see cref="Register"/>.</param>
    /// <returns>The entry id the fake resolves for that metadata.</returns>
    internal int EntryIdFor(ShaderRenderData metadata)
        => _entryIds.TryGetValue(metadata, out int entryId)
            ? entryId
            : throw new InvalidOperationException("The metadata was not registered with the fake.");

    /// <inheritdoc/>
    public int GetDefaultEntryId(ShaderPipelineKind kind)
        => kind == ShaderPipelineKind.Sprite ? DefaultEntryId : LineDefaultEntryId;

    /// <inheritdoc/>
    public ShaderBinding Resolve(
        ShaderRenderData? shader,
        ShaderPipelineKind kind,
        Format outputFormat)
    {
        int defaultEntryId = GetDefaultEntryId(kind);
        if (shader is null)
        {
            return new ShaderBinding(defaultEntryId, ShaderBindingState.Ready);
        }

        // An unregistered custom shader is modelled as still loading, the catalog's first-sighting
        // state; tests register every shader whose ready/failed behavior they exercise.
        if (!_bindings.TryGetValue(shader, out ShaderBinding binding))
        {
            return new ShaderBinding(defaultEntryId, ShaderBindingState.Pending);
        }

        return binding.State == ShaderBindingState.Failed
            ? new ShaderBinding(defaultEntryId, ShaderBindingState.Failed)
            : binding;
    }

    /// <inheritdoc/>
    public ShaderEntry GetEntry(int entryId)
        => _entries.TryGetValue(entryId, out ShaderEntry? entry)
            ? entry
            : throw new InvalidOperationException($"The fake has no shader entry {entryId}.");
}
