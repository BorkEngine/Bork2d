using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The target resources one recorded frame offers to every registered render pass.
/// </summary>
/// <remarks>
/// <para>
/// A pass reads only the members it needs: the 2D pass records into the linear
/// <paramref name="SceneView"/>/<paramref name="SceneImage"/> with the frame descriptor sets and the
/// scene format; the present pass samples the scene image and records into the final
/// <paramref name="TargetView"/>/<paramref name="TargetImage"/> with the per-frame present descriptor
/// set and the target format. No member exposes another pass's plan or the aggregate statistics, so
/// recording cannot mutate planning.
/// </para>
/// <para>
/// The context is a value with no ownership: the target driver owns every handle and keeps it alive
/// for the whole recording. It is created once per recorded frame on the render thread.
/// </para>
/// </remarks>
/// <param name="Vk">The Vulkan API object.</param>
/// <param name="CommandBuffer">The frame command buffer, already begun.</param>
/// <param name="SceneView">The linear scene image view in <c>COLOR_ATTACHMENT_OPTIMAL</c>.</param>
/// <param name="SceneImage">The linear scene image, for the 2D pass's color-attachment barrier.</param>
/// <param name="TargetView">The final UNORM target view in <c>COLOR_ATTACHMENT_OPTIMAL</c>.</param>
/// <param name="TargetImage">The final UNORM target image.</param>
/// <param name="TargetExtent">The full final target extent in pixels.</param>
/// <param name="DescriptorSet">The frame's bindless descriptor set (set 0).</param>
/// <param name="GlobalDescriptorSet">The frame's global uniform descriptor set (set 1).</param>
/// <param name="PresentDescriptorSet">
/// The frame's present descriptor set: the linear scene image view bound to the present pipeline's
/// combined image sampler.
/// </param>
/// <param name="SceneFormat">The linear scene color format the 2D pipelines were created for.</param>
/// <param name="TargetFormat">The final UNORM target color format.</param>
internal readonly record struct RenderPassRecordContext(
    Vk Vk,
    CommandBuffer CommandBuffer,
    ImageView SceneView,
    Image SceneImage,
    ImageView TargetView,
    Image TargetImage,
    Extent2D TargetExtent,
    DescriptorSet DescriptorSet,
    DescriptorSet GlobalDescriptorSet,
    DescriptorSet PresentDescriptorSet,
    Format SceneFormat,
    Format TargetFormat);
