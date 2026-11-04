using Silk.NET.Vulkan;

namespace Bork2d.Render.Shaders;

/// <summary>
/// The Vulkan module-and-pipeline seam behind the shader catalog.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ShaderStore"/> owns entry identity, validation, asynchronous loading, fallback, and
/// retirement; this seam owns only the Vulkan objects. The production implementation is
/// <c>TwoDimensionalPipeline</c>, which owns both built-in vertex modules, caches one fragment module
/// per shader content hash, and one graphics pipeline per <c>(kind, content hash, output format)</c>,
/// so identical content shares its module while a sprite and a line pipeline stay distinct.
/// Deterministic tests substitute a recording fake, so the store's loading and retirement rules run
/// without a GPU.
/// </para>
/// <para>
/// All members run on the render thread. A failed module or pipeline creation throws; the store
/// converts that failure into the requesting kind's default-shader fallback and never crashes the
/// frame.
/// </para>
/// </remarks>
internal interface IShaderModuleFactory
{
    /// <summary>Creates a fragment shader module from validated SPIR-V bytes.</summary>
    /// <param name="kind">The vertex-stage kind the module will be paired with, for labels.</param>
    /// <param name="spirV">The validated fragment module bytes.</param>
    /// <param name="name">The asset path used in errors and debug labels.</param>
    /// <returns>The created module; the catalog owns it.</returns>
    ShaderModule CreateModule(ShaderPipelineKind kind, byte[] spirV, string name);

    /// <summary>Destroys one fragment shader module created by <see cref="CreateModule"/>.</summary>
    /// <param name="kind">The vertex-stage kind the module was created for, for labels.</param>
    /// <param name="module">The module to destroy; a zero handle is ignored.</param>
    void DestroyModule(ShaderPipelineKind kind, ShaderModule module);

    /// <summary>
    /// Gets the graphics pipeline for a kind, content hash, and output format, creating it on first
    /// use.
    /// </summary>
    /// <param name="kind">The vertex-stage kind the pipeline combines with the fragment module.</param>
    /// <param name="colorFormat">The target's sRGB output color format.</param>
    /// <param name="contentHash">The shader content hash that owns the pipeline.</param>
    /// <param name="module">The module the new pipeline is created from.</param>
    /// <returns>The cached pipeline for that kind, content, and format.</returns>
    Pipeline GetOrCreatePipeline(
        ShaderPipelineKind kind,
        Format colorFormat,
        ulong contentHash,
        ShaderModule module);

    /// <summary>Looks up a cached pipeline without creating one.</summary>
    /// <param name="kind">The vertex-stage kind the pipeline combines with the fragment module.</param>
    /// <param name="contentHash">The shader content hash that owns the pipeline.</param>
    /// <param name="colorFormat">The target's sRGB output color format.</param>
    /// <param name="pipeline">The cached pipeline when one exists.</param>
    /// <returns><see langword="true"/> when the pipeline exists.</returns>
    bool TryGetPipeline(
        ShaderPipelineKind kind,
        ulong contentHash,
        Format colorFormat,
        out Pipeline pipeline);

    /// <summary>Destroys every pipeline for one kind and content hash (a retired shader's pipelines).</summary>
    /// <remarks>
    /// The caller owns the frame-safety rule: the catalog retires content only after every frame that
    /// could reference it has completed.
    /// </remarks>
    /// <param name="kind">The vertex-stage kind whose pipeline is destroyed.</param>
    /// <param name="contentHash">The shader content hash whose pipeline is destroyed.</param>
    void DestroyPipelines(ShaderPipelineKind kind, ulong contentHash);
}
