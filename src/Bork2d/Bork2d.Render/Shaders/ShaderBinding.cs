namespace Bork2d.Render.Shaders;

/// <summary>
/// The draw-time state of one captured shader binding before a sprite is turned into an instance.
/// </summary>
internal enum ShaderBindingState
{
    /// <summary>
    /// The custom shader is still loading or its module/pipeline has not been created yet; the
    /// sprite is skipped for the frame.
    /// </summary>
    Pending,

    /// <summary>The shader is ready; the sprite draws with the entry named by the binding.</summary>
    Ready,

    /// <summary>
    /// The custom shader failed validation, loading, module creation, or pipeline creation, or its
    /// declarations conflict with the engine-wide global names. The sprite draws with the default
    /// entry instead.
    /// </summary>
    Failed,
}

/// <summary>
/// The renderer's answer to "which shader entry does this captured sprite use this frame?".
/// </summary>
/// <remarks>
/// A <see cref="ShaderBindingState.Pending"/> binding skips the sprite; a
/// <see cref="ShaderBindingState.Failed"/> binding draws it with the default entry. The planner never
/// sees a Vulkan handle: it groups runs by <see cref="EntryId"/> and resolves the pipeline through
/// the production shader store when the run is recorded.
/// </remarks>
/// <param name="EntryId">
/// The shader entry identity used for run grouping. The default entry is always id <c>0</c>.
/// </param>
/// <param name="State">Whether the sprite waits, draws the entry, or falls back to the default.</param>
internal readonly record struct ShaderBinding(int EntryId, ShaderBindingState State);
