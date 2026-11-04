using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// Records the final fullscreen present pass: it samples the linear scene image and writes the
/// sRGB-encoded color into the UNORM swapchain or offscreen target.
/// </summary>
/// <remarks>
/// <para>
/// One fullscreen triangle covers the whole target with no vertex buffer, no push constant, and no
/// blending, so every target pixel receives exactly one encoded sample and the pass is a real draw.
/// The render area is the full target extent; the attachment uses <c>loadOp=DONT_CARE</c> because the
/// triangle overwrites every pixel and <c>storeOp=STORE</c> so the target keeps the encoded result.
/// </para>
/// <para>
/// A <c>COLOR_ATTACHMENT_OPTIMAL → SHADER_READ_ONLY_OPTIMAL</c> barrier runs first, so the sampling
/// observes every 2D pass's stored output. It is a synchronization dependency, not a pass.
/// Render-thread-only; recording allocates nothing.
/// </para>
/// </remarks>
internal static unsafe class PresentPassRecorder
{
    /// <summary>The vertex count of the fullscreen triangle.</summary>
    internal const uint FullscreenVertexCount = 3;

    /// <summary>Records the present barrier, rendering pass, and fullscreen draw.</summary>
    /// <param name="vk">The Vulkan API object.</param>
    /// <param name="commandBuffer">The frame command buffer, already begun.</param>
    /// <param name="pipeline">The present pipeline owner (layout and cached pipeline).</param>
    /// <param name="sceneDescriptorSet">The frame's present descriptor set for the scene view.</param>
    /// <param name="sceneImage">The linear scene image in <c>COLOR_ATTACHMENT_OPTIMAL</c>.</param>
    /// <param name="targetView">The final UNORM target view in <c>COLOR_ATTACHMENT_OPTIMAL</c>.</param>
    /// <param name="targetFormat">The final UNORM target color format.</param>
    /// <param name="extent">The full final target extent in pixels.</param>
    internal static void Record(
        Vk vk,
        CommandBuffer commandBuffer,
        PresentPipeline pipeline,
        DescriptorSet sceneDescriptorSet,
        Image sceneImage,
        ImageView targetView,
        Format targetFormat,
        Extent2D extent)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        RecordSceneReadBarrier(vk, commandBuffer, sceneImage);

        var area = new Rect2D
        {
            Offset = new Offset2D
            {
                X = 0,
                Y = 0,
            },
            Extent = extent,
        };
        var attachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = targetView,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = area,
            LayerCount = 1,
            ViewMask = 0,
            ColorAttachmentCount = 1,
            PColorAttachments = &attachment,
            PDepthAttachment = null,
            PStencilAttachment = null,
        };
        vk.CmdBeginRendering(commandBuffer, &rendering);

        vk.CmdBindPipeline(
            commandBuffer,
            PipelineBindPoint.Graphics,
            pipeline.GetPipeline(targetFormat));

        // The pass owns its single descriptor set; there is no dynamic offset.
        DescriptorSet set = sceneDescriptorSet;
        vk.CmdBindDescriptorSets(
            commandBuffer,
            PipelineBindPoint.Graphics,
            pipeline.PipelineLayout,
            0,
            1,
            &set,
            0,
            null);

        var viewport = new Viewport
        {
            X = 0f,
            Y = 0f,
            Width = extent.Width,
            Height = extent.Height,
            MinDepth = 0f,
            MaxDepth = 1f,
        };
        vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);
        Rect2D scissor = area;
        vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);
        vk.CmdDraw(commandBuffer, FullscreenVertexCount, 1, 0, 0);

        vk.CmdEndRendering(commandBuffer);
    }

    private static void RecordSceneReadBarrier(
        Vk vk,
        CommandBuffer commandBuffer,
        Image sceneImage)
    {
        var barrier = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags2.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags2.FragmentShaderBit,
            DstAccessMask = AccessFlags2.ShaderSampledReadBit,
            OldLayout = ImageLayout.ColorAttachmentOptimal,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = sceneImage,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };
        var dependency = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier,
        };
        vk.CmdPipelineBarrier2(commandBuffer, &dependency);
    }
}
