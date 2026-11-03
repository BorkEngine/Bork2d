using System.Numerics;
using System.Runtime.CompilerServices;

namespace Bork2d.Engine.Assets;

/// <summary>
/// The immutable render-side copy of a <see cref="ShaderAsset"/>'s declarations.
/// </summary>
/// <remarks>
/// <para>
/// A shader asset is a mutable authoring and loading DTO. Assigning it to a sprite renderable freezes
/// these declarations once, so mutating the asset afterwards — including replacing or editing its
/// declaration arrays — cannot change a captured snapshot or a planned frame. The copy holds the
/// SPIR-V reference, the layout version, the entry point, cloned declaration arrays, an ordinal
/// name-to-declaration lookup per block, and the precomputed four-vector default instance-uniform
/// block.
/// </para>
/// <para>
/// Freezing is idempotent per asset instance and allocation-free after the first call: the
/// <see cref="ConditionalWeakTable{TKey, TValue}"/> cache keeps one frozen copy per live asset
/// without a serialized or public cache member, and the weak key lets an unreferenced asset and its
/// copy be collected together. Freezing is thread-safe: concurrent callers for the same asset
/// observe the same published instance.
/// </para>
/// <para>
/// Malformed declarations are tolerated here: null arrays become empty, null names and duplicate
/// names are skipped by the lookup and the default block (the first declaration wins), and invalid
/// offsets or unknown types contribute no lanes. The renderer later rejects or falls back from such
/// metadata; the freeze path never throws for it.
/// </para>
/// </remarks>
internal sealed class ShaderRenderData
{
    private static readonly ConditionalWeakTable<ShaderAsset, ShaderRenderData> _frozen = new();

    private readonly Dictionary<string, ShaderUniform> _instanceUniformLookup;
    private readonly Dictionary<string, ShaderUniform> _globalUniformLookup;
    private readonly Vector4[] _defaultInstanceUniformBlock;

    private ShaderRenderData(ShaderAsset asset)
    {
        SpirV = asset.SpirV;
        LayoutVersion = asset.LayoutVersion;
        EntryPoint = asset.EntryPoint ?? string.Empty;
        InstanceUniforms = CloneUniforms(asset.InstanceUniforms);
        GlobalUniforms = CloneUniforms(asset.GlobalUniforms);
        TextureSlots = asset.TextureSlots is { } slots
            ? (ShaderTextureSlot[])slots.Clone()
            : [];

        _instanceUniformLookup = BuildLookup(InstanceUniforms);
        _globalUniformLookup = BuildLookup(GlobalUniforms);
        _defaultInstanceUniformBlock = BuildDefaultInstanceUniformBlock(_instanceUniformLookup);
    }

    /// <summary>Gets the referenced SPIR-V module, or an absent reference for no module.</summary>
    internal DataRef SpirV { get; }

    /// <summary>Gets the shader layout schema version the declarations were built for.</summary>
    internal uint LayoutVersion { get; }

    /// <summary>Gets the fragment entry point name; empty when the asset had none.</summary>
    internal string EntryPoint { get; }

    /// <summary>Gets the frozen per-instance uniform declarations.</summary>
    internal ShaderUniform[] InstanceUniforms { get; }

    /// <summary>Gets the frozen engine-wide uniform declarations.</summary>
    internal ShaderUniform[] GlobalUniforms { get; }

    /// <summary>Gets the frozen additional texture-slot declarations.</summary>
    internal ShaderTextureSlot[] TextureSlots { get; }

    /// <summary>
    /// Gets the precomputed default instance-uniform block: four <see cref="Vector4"/> lanes holding
    /// the declared defaults at their float-lane offsets, with every undeclared lane zero.
    /// </summary>
    internal ReadOnlySpan<Vector4> DefaultInstanceUniformBlock => _defaultInstanceUniformBlock;

    /// <summary>
    /// Returns the frozen copy for <paramref name="asset"/>, creating it on first use.
    /// </summary>
    /// <param name="asset">The mutable asset to freeze.</param>
    /// <returns>The single frozen copy published for this asset instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="asset"/> is <see langword="null"/>.</exception>
    internal static ShaderRenderData Freeze(ShaderAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return _frozen.GetValue(asset, static key => new ShaderRenderData(key));
    }

    /// <summary>
    /// Returns the frozen copy for <paramref name="asset"/>, or <see langword="null"/> when the
    /// asset is <see langword="null"/>.
    /// </summary>
    /// <param name="asset">The mutable asset to freeze, or <see langword="null"/>.</param>
    /// <returns>The frozen copy, or <see langword="null"/>.</returns>
    internal static ShaderRenderData? FreezeOrNull(ShaderAsset? asset)
        => asset is null ? null : Freeze(asset);

    /// <summary>
    /// Looks up a declared per-instance uniform by its exact, ordinal name.
    /// </summary>
    /// <param name="name">The shader-declared name.</param>
    /// <param name="uniform">The declaration when found; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the declaration exists; otherwise <see langword="false"/>.</returns>
    internal bool TryGetInstanceUniform(string? name, out ShaderUniform uniform)
    {
        if (name is null)
        {
            uniform = default;
            return false;
        }

        return _instanceUniformLookup.TryGetValue(name, out uniform);
    }

    /// <summary>
    /// Looks up a declared engine-wide uniform by its exact, ordinal name.
    /// </summary>
    /// <param name="name">The shader-declared name.</param>
    /// <param name="uniform">The declaration when found; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the declaration exists; otherwise <see langword="false"/>.</returns>
    internal bool TryGetGlobalUniform(string? name, out ShaderUniform uniform)
    {
        if (name is null)
        {
            uniform = default;
            return false;
        }

        return _globalUniformLookup.TryGetValue(name, out uniform);
    }

    private static ShaderUniform[] CloneUniforms(ShaderUniform[]? uniforms)
        => uniforms is { } source ? (ShaderUniform[])source.Clone() : [];

    private static Dictionary<string, ShaderUniform> BuildLookup(ShaderUniform[] uniforms)
    {
        var lookup = new Dictionary<string, ShaderUniform>(
            uniforms.Length,
            StringComparer.Ordinal);
        foreach (ShaderUniform uniform in uniforms)
        {
            // An ordinal lookup cannot store a null key, and a duplicate declaration is malformed
            // metadata: keep the first declaration so freezing stays total instead of throwing.
            if (uniform.Name is not null)
            {
                _ = lookup.TryAdd(uniform.Name, uniform);
            }
        }

        return lookup;
    }

    private static Vector4[] BuildDefaultInstanceUniformBlock(
        Dictionary<string, ShaderUniform> lookup)
    {
        var block = new Vector4[ShaderLayout.InstanceBlockVectorCount];
        foreach (ShaderUniform uniform in lookup.Values)
        {
            ShaderLayout.WriteUniform(block, in uniform, uniform.Default);
        }

        return block;
    }
}
