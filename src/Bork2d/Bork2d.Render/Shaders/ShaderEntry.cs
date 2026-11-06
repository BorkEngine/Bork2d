using Bork2d.Engine.Assets;

namespace Bork2d.Render.Shaders;

/// <summary>
/// One usable fragment-shader entry in the renderer's catalog: the frozen metadata the planner needs
/// and the identity that groups contiguous 2D records into a <c>TwoDimensional.DrawRun</c>.
/// </summary>
/// <remarks>
/// A custom entry is keyed by its SPIR-V <see cref="DataRef"/> path because the captured renderable
/// already carries the frozen <see cref="ShaderRenderData"/>; the renderer never reloads the
/// <see cref="ShaderAsset"/> per draw. The first frozen metadata seen for a path supplies the entry's
/// declarations, so the path is the shader identity and its declaration set is fixed for that
/// catalog. Each kind's built-in default entry has a fixed id: <c>shader/sprite</c> is <c>0</c>,
/// <c>shader/line</c> is <c>1</c>, and the font SDF default <c>shader/sdf</c> is <c>2</c>; custom
/// entries start at <c>3</c>.
/// </remarks>
internal sealed class ShaderEntry
{
    /// <summary>Initializes a new instance of the <see cref="ShaderEntry"/> class.</summary>
    /// <param name="id">The entry identity used for run grouping.</param>
    /// <param name="path">The asset path used in diagnostics (the SPIR-V path for a custom shader).</param>
    /// <param name="metadata">The frozen declarations the planner reads.</param>
    /// <param name="isDefault">Whether this is a built-in default entry.</param>
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

    /// <summary>Gets a value indicating whether this is a built-in default entry.</summary>
    internal bool IsDefault { get; }
}
