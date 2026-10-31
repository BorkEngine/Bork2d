using Silk.NET.Vulkan;

namespace Bork2d.Render.Textures;

/// <summary>
/// The renderer-owned seam that writes bindless page descriptors into every per-frame descriptor
/// set (M8 §6.5).
/// </summary>
/// <remarks>
/// The page subsystem owns images and samplers; the renderer owns the per-frame descriptor sets.
/// Both renderers implement this interface over their frame array, so a page becoming ready and a
/// capacity growth are written into both frames in flight.
/// </remarks>
internal interface IBindlessPageWriter
{
    /// <summary>
    /// Recreates every per-frame descriptor pool/set with <paramref name="capacity"/> entries.
    /// </summary>
    /// <remarks>
    /// The implementation waits the device idle first (growth happens between frames with all
    /// in-flight frames idle), then recreates the sets empty. The caller rewrites every resident
    /// page descriptor afterwards.
    /// </remarks>
    /// <param name="capacity">The new variable descriptor count, including the placeholder.</param>
    void EnsureCapacity(int capacity);

    /// <summary>Writes one page's combined image sampler into every per-frame bindless set.</summary>
    /// <param name="pageIndex">The bindless array element.</param>
    /// <param name="imageView">The page's image view, in <c>SHADER_READ_ONLY_OPTIMAL</c>.</param>
    /// <param name="sampler">The cached sampler for the page's filter/wrap pair.</param>
    void WritePage(int pageIndex, ImageView imageView, Sampler sampler);
}
