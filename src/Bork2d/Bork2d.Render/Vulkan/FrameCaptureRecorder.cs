using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// Records the optional image-to-buffer copy for <c>IEngine.CaptureFrameAsync</c> (M8 §6.9).
/// </summary>
/// <remarks>
/// The copy runs after the last camera pass and before present/discard. The image transitions
/// <c>COLOR_ATTACHMENT_OPTIMAL → TRANSFER_SRC_OPTIMAL</c>, is copied into the frame's pooled
/// host-visible staging buffer, and then transitions to <c>PRESENT_SRC_KHR</c> (windowed) or back to
/// <c>COLOR_ATTACHMENT_OPTIMAL</c> (offscreen). The copy includes every camera clear and draw and
/// excludes OS/window chrome. When no capture is requested, none of this is recorded.
/// </remarks>
internal static unsafe class FrameCaptureRecorder
{
    /// <summary>Records the transition, copy, and final transition for one frame capture.</summary>
    /// <param name="vk">The Vulkan API object.</param>
    /// <param name="commandBuffer">The frame command buffer, already begun.</param>
    /// <param name="image">The composited target image in <c>COLOR_ATTACHMENT_OPTIMAL</c>.</param>
    /// <param name="staging">The frame's host-visible staging buffer.</param>
    /// <param name="width">The captured extent width in pixels.</param>
    /// <param name="height">The captured extent height in pixels.</param>
    /// <param name="toPresentSrc">
    /// <see langword="true"/> for windowed (finish in <c>PRESENT_SRC_KHR</c>);
    /// <see langword="false"/> for offscreen (restore <c>COLOR_ATTACHMENT_OPTIMAL</c>).
    /// </param>
    internal static void RecordCopy(
        Vk vk,
        CommandBuffer commandBuffer,
        Image image,
        Buffer staging,
        int width,
        int height,
        bool toPresentSrc)
    {
        RecordBarrier(
            vk,
            commandBuffer,
            image,
            oldLayout: ImageLayout.ColorAttachmentOptimal,
            newLayout: ImageLayout.TransferSrcOptimal,
            srcStage: PipelineStageFlags2.ColorAttachmentOutputBit,
            srcAccess: AccessFlags2.ColorAttachmentWriteBit,
            dstStage: PipelineStageFlags2.TransferBit,
            dstAccess: AccessFlags2.TransferReadBit);

        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            ImageOffset = new Offset3D { X = 0, Y = 0, Z = 0 },
            ImageExtent = new Extent3D
            {
                Width = (uint)width,
                Height = (uint)height,
                Depth = 1,
            },
        };
        vk.CmdCopyImageToBuffer(
            commandBuffer,
            image,
            ImageLayout.TransferSrcOptimal,
            staging,
            1,
            &region);

        RecordBarrier(
            vk,
            commandBuffer,
            image,
            oldLayout: ImageLayout.TransferSrcOptimal,
            newLayout: toPresentSrc
                ? ImageLayout.PresentSrcKhr
                : ImageLayout.ColorAttachmentOptimal,
            srcStage: PipelineStageFlags2.TransferBit,
            srcAccess: AccessFlags2.TransferReadBit,
            dstStage: toPresentSrc
                ? PipelineStageFlags2.None
                : PipelineStageFlags2.ColorAttachmentOutputBit,
            dstAccess: toPresentSrc
                ? AccessFlags2.None
                : AccessFlags2.ColorAttachmentWriteBit);
    }

    /// <summary>Records the windowed <c>COLOR_ATTACHMENT_OPTIMAL → PRESENT_SRC_KHR</c> transition.</summary>
    /// <param name="vk">The Vulkan API object.</param>
    /// <param name="commandBuffer">The frame command buffer, already begun.</param>
    /// <param name="image">The composited target image.</param>
    internal static void RecordPresentTransition(
        Vk vk,
        CommandBuffer commandBuffer,
        Image image)
        => RecordBarrier(
            vk,
            commandBuffer,
            image,
            oldLayout: ImageLayout.ColorAttachmentOptimal,
            newLayout: ImageLayout.PresentSrcKhr,
            srcStage: PipelineStageFlags2.ColorAttachmentOutputBit,
            srcAccess: AccessFlags2.ColorAttachmentWriteBit,
            dstStage: PipelineStageFlags2.None,
            dstAccess: AccessFlags2.None);

    private static void RecordBarrier(
        Vk vk,
        CommandBuffer commandBuffer,
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        PipelineStageFlags2 srcStage,
        AccessFlags2 srcAccess,
        PipelineStageFlags2 dstStage,
        AccessFlags2 dstAccess)
    {
        var barrier = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = srcStage,
            SrcAccessMask = srcAccess,
            DstStageMask = dstStage,
            DstAccessMask = dstAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
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
