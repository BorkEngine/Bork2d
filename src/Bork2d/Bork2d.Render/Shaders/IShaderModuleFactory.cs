using Silk.NET.Vulkan;

namespace Bork2d.Render.Shaders;

/// <summary>
/// The Vulkan module-and-pipeline seam behind the shader catalog.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ShaderStore"/> owns entry identity, validation, asynchronous loading, fallback, and
/// retirement; this seam owns only the Vulkan objects. The production implementation is
/// <c>SpritePipeline</c>, which caches one module per shader content hash and one graphics pipeline
/// per <c>(content hash, output format)</c>, so identical content shares both. Deterministic tests
/// substitute a recording fake, so the store's loading and retirement rules run without a GPU.
/// </para>
/// <para>
/// All members run on the render thread. A failed module or pipeline creation throws; the store
/// converts that failure into the default-shader fallback and never crashes the frame.
/// </para>
/// </remarks>
internal interface IShaderModuleFactory
{
    /// <summary>Creates a fragment shader module from validated SPIR-V bytes.</summary>
    /// <param name="spirV">The validated fragment module bytes.</param>
    /// <param name="name">The asset path used in errors and debug labels.</param>
    /// <returns>The created module; the catalog owns it.</returns>
    ShaderModule CreateModule(byte[] spirV, string name);

    /// <summary>Destroys one fragment shader module created by <see cref="CreateModule"/>.</summary>
    /// <param name="module">The module to destroy; a zero handle is ignored.</param>
    void DestroyModule(ShaderModule module);

    /// <summary>
    /// Gets the graphics pipeline for a content hash and output format, creating it on first use.
    /// </summary>
    /// <param name="colorFormat">The target's sRGB output color format.</param>
    /// <param name="contentHash">The shader content hash that owns the pipeline.</param>
    /// <param name="module">The module the new pipeline is created from.</param>
    /// <returns>The cached pipeline for that content and format.</returns>
    Pipeline GetOrCreatePipeline(Format colorFormat, ulong contentHash, ShaderModule module);

    /// <summary>Looks up a cached pipeline without creating one.</summary>
    /// <param name="contentHash">The shader content hash that owns the pipeline.</param>
    /// <param name="colorFormat">The target's sRGB output color format.</param>
    /// <param name="pipeline">The cached pipeline when one exists.</param>
    /// <returns><see langword="true"/> when the pipeline exists.</returns>
    bool TryGetPipeline(ulong contentHash, Format colorFormat, out Pipeline pipeline);

    /// <summary>Destroys every pipeline for one content hash (a retired shader's pipelines).</summary>
    /// <remarks>
    /// The caller owns the frame-safety rule: the catalog retires content only after every frame that
    /// could reference it has completed.
    /// </remarks>
    /// <param name="contentHash">The shader content hash whose pipelines are destroyed.</param>
    void DestroyPipelines(ulong contentHash);
}
