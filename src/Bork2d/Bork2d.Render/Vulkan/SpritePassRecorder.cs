using System.Numerics;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Sprites;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// Records the frame's full-target opaque-black clear and the approved per-camera dynamic-rendering
/// passes with one instanced draw per non-empty camera (M8 §6.8/§6.11).
/// </summary>
/// <remarks>
/// <para>
/// Every recorded frame starts with one full-target clear to opaque black, recorded after the
/// target's initial layout transition and before any camera pass. It is a normal dynamic-rendering
/// pass with a single sRGB color attachment, no depth/stencil, <c>loadOp=CLEAR</c>, and
/// <c>storeOp=STORE</c>; it is not a camera and not a draw call, so statistics and draw accounting
/// are unchanged.
/// </para>
/// <para>
/// Each valid camera owns one <c>vkCmdBeginRendering</c>/<c>vkCmdEndRendering</c> pair over its
/// rounded pixel rect with a single sRGB color attachment, no depth/stencil, and
/// <c>storeOp=STORE</c>. <c>Clear.Color</c> uses <c>loadOp=CLEAR</c> with the camera's linear RGBA
/// color; <c>Clear.None</c>/<c>Clear.Depth</c> use <c>LOAD</c> and never touch depth. A camera with
/// at least one instance binds the pipeline/descriptor set, pushes the 64-byte view-projection, sets
/// its viewport/scissor, and issues exactly one <c>vkCmdDraw(4, count, 0, firstInstance)</c>; an
/// empty camera records only the pass when it clears.
/// </para>
/// <para>
/// A color-attachment barrier runs before every camera pass so its <c>LOAD</c> observes the frame
/// clear and the earlier cameras' stored output (and, offscreen, the previous submission's output).
/// It is a synchronization dependency, not a pass: the frame clear is the only extra render pass.
/// </para>
/// </remarks>
internal static unsafe class SpritePassRecorder
{
    /// <summary>The vertex count of the shader-generated quad.</summary>
    internal const uint QuadVertexCount = 4;

    /// <summary>
    /// Records the full-target opaque-black clear that defines every pixel at the start of a frame.
    /// </summary>
    /// <param name="vk">The Vulkan API object.</param>
    /// <param name="commandBuffer">The frame command buffer, already begun.</param>
    /// <param name="attachmentView">The target image view in <c>COLOR_ATTACHMENT_OPTIMAL</c>.</param>
    /// <param name="extent">The full target extent in pixels.</param>
    internal static void RecordFrameClear(
        Vk vk,
        CommandBuffer commandBuffer,
        ImageView attachmentView,
        Extent2D extent)
    {
        var attachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = attachmentView,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = new ClearValue
            {
                Color = new ClearColorValue
                {
                    Float32_0 = 0f,
                    Float32_1 = 0f,
                    Float32_2 = 0f,
                    Float32_3 = 1f,
                },
            },
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D
            {
                Offset = new Offset2D
                {
                    X = 0,
                    Y = 0,
                },
                Extent = extent,
            },
            LayerCount = 1,
            ViewMask = 0,
            ColorAttachmentCount = 1,
            PColorAttachments = &attachment,
            PDepthAttachment = null,
            PStencilAttachment = null,
        };
        vk.CmdBeginRendering(commandBuffer, &rendering);
        vk.CmdEndRendering(commandBuffer);
    }

    /// <summary>Records every valid camera's pass and draw from a frame plan.</summary>
    /// <param name="vk">The Vulkan API object.</param>
    /// <param name="commandBuffer">The frame command buffer, already begun.</param>
    /// <param name="spritePipeline">The pipeline owner (pipeline layout for push constants/binding).</param>
    /// <param name="graphicsPipeline">The pipeline for the target's color format.</param>
    /// <param name="descriptorSet">The frame's bindless descriptor set.</param>
    /// <param name="attachmentView">The target image view in <c>COLOR_ATTACHMENT_OPTIMAL</c>.</param>
    /// <param name="attachmentImage">The target image (for the per-pass color-attachment barrier).</param>
    /// <param name="cameras">The plan's valid camera array.</param>
    /// <param name="cameraCount">The number of valid cameras in <paramref name="cameras"/>.</param>
    internal static void RecordPasses(
        Vk vk,
        CommandBuffer commandBuffer,
        SpritePipeline spritePipeline,
        Pipeline graphicsPipeline,
        DescriptorSet descriptorSet,
        ImageView attachmentView,
        Image attachmentImage,
        CameraDrawPlan[] cameras,
        int cameraCount)
    {
        ArgumentNullException.ThrowIfNull(spritePipeline);
        ArgumentNullException.ThrowIfNull(cameras);

        for (var index = 0; index < cameraCount; index++)
        {
            CameraDrawPlan camera = cameras[index];
            bool hasInstances = camera.InstanceCount > 0;
            if (!hasInstances && camera.Clear != ClearKind.Color)
            {
                // An empty Clear.None/Depth camera records nothing at all.
                continue;
            }

            RecordColorAttachmentBarrier(vk, commandBuffer, attachmentImage);

            var area = new Rect2D
            {
                Offset = new Offset2D
                {
                    X = camera.Projection.Left,
                    Y = camera.Projection.Top,
                },
                Extent = new Extent2D
                {
                    Width = (uint)camera.Projection.Width,
                    Height = (uint)camera.Projection.Height,
                },
            };
            var attachment = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = attachmentView,
                ImageLayout = ImageLayout.ColorAttachmentOptimal,
                LoadOp = camera.Clear == ClearKind.Color
                    ? AttachmentLoadOp.Clear
                    : AttachmentLoadOp.Load,
                StoreOp = AttachmentStoreOp.Store,
                ClearValue = camera.Clear == ClearKind.Color
                    ? new ClearValue
                    {
                        Color = new ClearColorValue
                        {
                            Float32_0 = camera.ClearColor.R,
                            Float32_1 = camera.ClearColor.G,
                            Float32_2 = camera.ClearColor.B,
                            Float32_3 = camera.ClearColor.A,
                        },
                    }
                    : default,
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

            if (hasInstances)
            {
                RecordDraw(
                    vk,
                    commandBuffer,
                    spritePipeline,
                    graphicsPipeline,
                    descriptorSet,
                    camera,
                    area);
            }

            vk.CmdEndRendering(commandBuffer);
        }
    }

    private static void RecordDraw(
        Vk vk,
        CommandBuffer commandBuffer,
        SpritePipeline spritePipeline,
        Pipeline graphicsPipeline,
        DescriptorSet descriptorSet,
        in CameraDrawPlan camera,
        in Rect2D area)
    {
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, graphicsPipeline);
        DescriptorSet set = descriptorSet;
        vk.CmdBindDescriptorSets(
            commandBuffer,
            PipelineBindPoint.Graphics,
            spritePipeline.PipelineLayout,
            0,
            1,
            &set,
            0,
            null);

        Matrix4x4 viewProjection = camera.Projection.ViewProjection;
        vk.CmdPushConstants(
            commandBuffer,
            spritePipeline.PipelineLayout,
            ShaderStageFlags.VertexBit,
            0,
            SpritePipeline.PushConstantBytes,
            &viewProjection);

        var viewport = new Viewport
        {
            X = area.Offset.X,
            Y = area.Offset.Y,
            Width = area.Extent.Width,
            Height = area.Extent.Height,
            MinDepth = 0f,
            MaxDepth = 1f,
        };
        vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);
        Rect2D scissor = area;
        vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);

        vk.CmdDraw(
            commandBuffer,
            QuadVertexCount,
            (uint)camera.InstanceCount,
            0,
            (uint)camera.FirstInstance);
    }

    private static void RecordColorAttachmentBarrier(
        Vk vk,
        CommandBuffer commandBuffer,
        Image image)
    {
        var barrier = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags2.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags2.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags2.ColorAttachmentReadBit
                | AccessFlags2.ColorAttachmentWriteBit,
            OldLayout = ImageLayout.ColorAttachmentOptimal,
            NewLayout = ImageLayout.ColorAttachmentOptimal,
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
