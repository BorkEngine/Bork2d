using Bork2d.Engine.Assets;

namespace Bork2d.Render.Textures;

/// <summary>
/// The Vulkan work behind the texture-page state machine (M8 §6.5): capacity growth, uploads, and
/// bindless descriptor writes.
/// </summary>
/// <remarks>
/// <para>
/// The logical state machine (<see cref="TexturePageStore"/>) owns page identity, content
/// validation, and capacity policy; this seam owns images, memory, staging, upload fences, and the
/// descriptor writes. The render thread calls every member; no member blocks on a page load, and
/// only <see cref="WaitForUpload"/> (the startup placeholder) may wait on a fence.
/// </para>
/// <para>
/// The seam is an interface so deterministic tests can drive the state machine without a GPU. The
/// production implementation is <c>VulkanTexturePages</c>.
/// </para>
/// </remarks>
internal interface ITexturePageGpu
{
    /// <summary>
    /// Makes the per-frame bindless sets hold <paramref name="capacity"/> entries and rewrites every
    /// resident page descriptor.
    /// </summary>
    /// <remarks>
    /// Growth must not replace a descriptor set while a frame using it is in flight, so the
    /// implementation retires every in-flight frame fence before recreating the descriptor
    /// pools/sets. It never calls <c>vkDeviceWaitIdle</c> for growth. Calling it with the current
    /// capacity is a no-op.
    /// </remarks>
    /// <param name="capacity">The new total capacity, including the placeholder.</param>
    void EnsureCapacity(int capacity);

    /// <summary>
    /// Creates the page image/view, copies the payload through staging, and submits the
    /// <c>TRANSFER_DST → SHADER_READ_ONLY_OPTIMAL</c> upload on the graphics queue with a fence.
    /// </summary>
    /// <param name="pageIndex">
    /// The assigned bindless page index: <c>0</c> for the startup placeholder and the next real
    /// index for content pages.
    /// </param>
    /// <param name="descriptor">The validated descriptor; its dimensions and sampler state apply.</param>
    /// <param name="payload">The validated RGBA8 bytes, exactly <c>Width * Height * 4</c> long.</param>
    void Upload(int pageIndex, TextureDescriptor descriptor, byte[] payload);

    /// <summary>Returns whether a submitted upload's fence has signaled, without waiting.</summary>
    /// <param name="pageIndex">The page index passed to <see cref="Upload"/>.</param>
    /// <returns><see langword="true"/> when the image is in the shader-read layout.</returns>
    bool IsUploadComplete(int pageIndex);

    /// <summary>
    /// Finalizes a completed upload: releases its staging/command resources, adds the page to the
    /// resident set, and writes its descriptor into every per-frame bindless set.
    /// </summary>
    /// <param name="pageIndex">The page index passed to <see cref="Upload"/>.</param>
    void CompleteUpload(int pageIndex);

    /// <summary>
    /// Waits for a submitted upload's fence and completes it (the startup placeholder only).
    /// </summary>
    /// <param name="pageIndex">The page index passed to <see cref="Upload"/>.</param>
    void WaitForUpload(int pageIndex);

    /// <summary>
    /// Destroys one page's view, image, and image-memory range so the slot and its bytes can be
    /// reused.
    /// </summary>
    /// <remarks>
    /// The caller owns the frame-safety rule: it retires a page only after
    /// <see cref="TexturePageStore"/>'s reclaim safety age proved no in-flight frame can sample it,
    /// and never the placeholder. The implementation rewrites the slot to the placeholder before
    /// destroying the view, returns the image-memory range to the image pool for reuse, and drops
    /// the page from the resident set. Retiring an index that is not resident is a no-op.
    /// </remarks>
    /// <param name="pageIndex">The bindless page index to reclaim; never the placeholder.</param>
    void RetirePage(int pageIndex);

    /// <summary>Gets the number of resident page images (including the placeholder).</summary>
    int ResidentPageCount { get; }

    /// <summary>Gets the number of uploads that have been submitted and not yet completed.</summary>
    int PendingUploadCount { get; }

    /// <summary>Destroys every owned Vulkan object; idempotent and never throws.</summary>
    void Destroy();
}
