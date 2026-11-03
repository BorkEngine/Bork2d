using Bork2d.Engine.Assets;

namespace Bork2d.Render.Shaders;

/// <summary>
/// One usable fragment-shader entry in the renderer's catalog: the frozen metadata the planner needs
/// and the identity that groups contiguous sprites into a <see cref="Sprites.ShaderRun"/>.
/// </summary>
/// <remarks>
/// A custom entry is keyed by its SPIR-V <see cref="DataRef"/> path because the captured sprite
/// already carries the frozen <see cref="ShaderRenderData"/>; the renderer never reloads the
/// <see cref="ShaderAsset"/> per draw. The first frozen metadata seen for a path supplies the entry's
/// declarations, so the path is the shader identity and its declaration set is fixed for that
/// catalog. The default entry always has id <c>0</c>.
/// </remarks>
internal sealed class ShaderEntry
{
    /// <summary>Initializes a new instance of the <see cref="ShaderEntry"/> class.</summary>
    /// <param name="id">The entry identity used for run grouping.</param>
    /// <param name="path">The asset path used in diagnostics (the SPIR-V path for a custom shader).</param>
    /// <param name="metadata">The frozen declarations the planner reads.</param>
    /// <param name="isDefault">Whether this is the built-in <c>shader/sprite</c> entry.</param>
    internal ShaderEntry(int id, string path, ShaderRenderData metadata, bool isDefault)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(metadata);
        Id = id;
        Path = path;
        Metadata = metadata;
        IsDefault = isDefault;
    }

    /// <summary>Gets the entry identity used for run grouping.</summary>
    internal int Id { get; }

    /// <summary>Gets the asset path used in diagnostics.</summary>
    internal string Path { get; }

    /// <summary>Gets the frozen shader declarations.</summary>
    internal ShaderRenderData Metadata { get; }

    /// <summary>Gets a value indicating whether this is the built-in <c>shader/sprite</c> entry.</summary>
    internal bool IsDefault { get; }
}
