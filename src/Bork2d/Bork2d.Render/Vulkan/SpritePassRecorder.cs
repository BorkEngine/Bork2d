using System.Numerics;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Sprites;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// Records the frame's full-target opaque-black clear and the approved per-camera dynamic-rendering
/// passes with one instanced draw per contiguous shader run (M8 §6.8/§6.11, custom shaders).
/// </summary>
/// <remarks>
/// <para>
/// Every recorded frame starts with one full-target clear to opaque black, recorded after the linear
/// scene image's initial layout transition and before any camera pass. It is a normal
/// dynamic-rendering pass with a single linear <c>R8G8B8A8_UNORM</c> scene color attachment, no
/// depth/stencil, <c>loadOp=CLEAR</c>, and <c>storeOp=STORE</c>; it is not a camera and not a draw
/// call, so statistics and draw accounting are unchanged.
/// </para>
/// <para>
/// Each valid camera owns one <c>vkCmdBeginRendering</c>/<c>vkCmdEndRendering</c> pair over its
/// rounded pixel rect with a single linear scene color attachment, no depth/stencil, and
/// <c>storeOp=STORE</c>. <c>Clear.Color</c> uses <c>loadOp=CLEAR</c> with the camera's linear RGBA
/// color; <c>Clear.None</c>/<c>Clear.Depth</c> use <c>LOAD</c> and never touch depth. A camera with
/// at least one run sets its viewport/scissor once, pushes the 64-byte view-projection once, then
/// binds each run's pipeline and descriptor sets with that run's global-block dynamic offset and
/// issues <c>vkCmdDraw(4, run.InstanceCount, 0, run.FirstInstance)</c>. Consecutive runs that share
/// both the pipeline and the dynamic offset skip the redundant binds. A camera with no runs records
/// only its pass when it clears.
/// </para>
/// <para>
/// A scene color-attachment barrier runs before every camera pass so its <c>LOAD</c> observes the
/// frame clear and the earlier cameras' stored output (and, offscreen, the previous submission's
/// output). It is a synchronization dependency, not a pass: the frame clear is the only extra render
/// pass before the final present pass.
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
    /// <param name="sceneView">The linear scene image view in <c>COLOR_ATTACHMENT_OPTIMAL</c>.</param>
    /// <param name="extent">The full target extent in pixels.</param>
    internal static void RecordFrameClear(
        Vk vk,
        CommandBuffer commandBuffer,
        ImageView sceneView,
        Extent2D extent)
    {
        var attachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = sceneView,
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

    /// <summary>Records every valid camera's pass and shader runs from a frame plan.</summary>
    /// <param name="vk">The Vulkan API object.</param>
    /// <param name="commandBuffer">The frame command buffer, already begun.</param>
    /// <param name="spritePipeline">The pipeline owner (pipeline layout for push constants/binding).</param>
    /// <param name="descriptorSet">The frame's bindless descriptor set (set 0).</param>
    /// <param name="globalDescriptorSet">The frame's global uniform descriptor set (set 1).</param>
    /// <param name="sceneView">The linear scene image view in <c>COLOR_ATTACHMENT_OPTIMAL</c>.</param>
    /// <param name="sceneImage">The linear scene image (for the per-pass color-attachment barrier).</param>
    /// <param name="cameras">The plan's valid camera array.</param>
    /// <param name="cameraCount">The number of valid cameras in <paramref name="cameras"/>.</param>
    /// <param name="runs">The plan's shader runs.</param>
    /// <param name="runPipelines">
    /// The graphics pipeline for each run, parallel to <paramref name="runs"/>.
    /// </param>
    internal static void RecordPasses(
        Vk vk,
        CommandBuffer commandBuffer,
        SpritePipeline spritePipeline,
        DescriptorSet descriptorSet,
        DescriptorSet globalDescriptorSet,
        ImageView sceneView,
        Image sceneImage,
        CameraDrawPlan[] cameras,
        int cameraCount,
        ShaderRun[] runs,
        Pipeline[] runPipelines)
    {
        ArgumentNullException.ThrowIfNull(spritePipeline);
        ArgumentNullException.ThrowIfNull(cameras);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(runPipelines);

        for (var index = 0; index < cameraCount; index++)
        {
            CameraDrawPlan camera = cameras[index];
            bool hasInstances = camera.RunCount > 0;
            if (!hasInstances && camera.Clear != ClearKind.Color)
            {
                // An empty Clear.None/Depth camera records nothing at all.
                continue;
            }

            RecordColorAttachmentBarrier(vk, commandBuffer, sceneImage);

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
                ImageView = sceneView,
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
                RecordRuns(
                    vk,
                    commandBuffer,
                    spritePipeline,
                    descriptorSet,
                    globalDescriptorSet,
                    camera,
                    runs,
                    runPipelines,
                    area);
            }

            vk.CmdEndRendering(commandBuffer);
        }
    }

    private static void RecordRuns(
        Vk vk,
        CommandBuffer commandBuffer,
        SpritePipeline spritePipeline,
        DescriptorSet descriptorSet,
        DescriptorSet globalDescriptorSet,
        in CameraDrawPlan camera,
        ShaderRun[] runs,
        Pipeline[] runPipelines,
        in Rect2D area)
    {
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

        Matrix4x4 viewProjection = camera.Projection.ViewProjection;
        vk.CmdPushConstants(
            commandBuffer,
            spritePipeline.PipelineLayout,
            ShaderStageFlags.VertexBit,
            0,
            SpritePipeline.PushConstantBytes,
            &viewProjection);

        var bound = false;
        var boundPipeline = default(Pipeline);
        var boundOffset = 0u;
        int runEnd = camera.FirstRun + camera.RunCount;
        for (var runIndex = camera.FirstRun; runIndex < runEnd; runIndex++)
        {
            ShaderRun run = runs[runIndex];
            Pipeline pipeline = runPipelines[runIndex];
            bool rebind = !bound || pipeline.Handle != boundPipeline.Handle || run.GlobalBlockOffset != boundOffset;
            if (rebind)
            {
                bound = true;
                boundPipeline = pipeline;
                boundOffset = run.GlobalBlockOffset;
                RecordBindings(
                    vk,
                    commandBuffer,
                    spritePipeline,
                    descriptorSet,
                    globalDescriptorSet,
                    pipeline,
                    run.GlobalBlockOffset);
            }

            vk.CmdDraw(
                commandBuffer,
                QuadVertexCount,
                (uint)run.InstanceCount,
                0,
                (uint)run.FirstInstance);
        }
    }

    private static void RecordBindings(
        Vk vk,
        CommandBuffer commandBuffer,
        SpritePipeline spritePipeline,
        DescriptorSet descriptorSet,
        DescriptorSet globalDescriptorSet,
        Pipeline graphicsPipeline,
        uint dynamicOffset)
    {
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, graphicsPipeline);
        DescriptorSet* sets = stackalloc DescriptorSet[2];
        sets[0] = descriptorSet;
        sets[1] = globalDescriptorSet;
        uint offset = dynamicOffset;
        vk.CmdBindDescriptorSets(
            commandBuffer,
            PipelineBindPoint.Graphics,
            spritePipeline.PipelineLayout,
            0,
            2,
            sets,
            1,
            &offset);
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
