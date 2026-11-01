using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The target resources one recorded frame offers to every registered render pass.
/// </summary>
/// <remarks>
/// <para>
/// A pass reads only the members it needs: the sprite pass records against the attachment
/// view/image, the frame descriptor set, the target format, the command buffer, and the Vulkan API.
/// A future pass may use only some of them, and no member exposes another pass's plan or the
/// aggregate statistics, so recording cannot mutate planning.
/// </para>
/// <para>
/// The context is a value with no ownership: the target driver owns every handle and keeps it alive
/// for the whole recording. It is created once per recorded frame on the render thread.
/// </para>
/// </remarks>
/// <param name="Vk">The Vulkan API object.</param>
/// <param name="CommandBuffer">The frame command buffer, already begun.</param>
/// <param name="AttachmentView">The target image view in <c>COLOR_ATTACHMENT_OPTIMAL</c>.</param>
/// <param name="AttachmentImage">The target image, for the per-pass color-attachment barrier.</param>
/// <param name="DescriptorSet">The frame's bindless descriptor set.</param>
/// <param name="TargetFormat">The target's sRGB output color format.</param>
internal readonly record struct RenderPassRecordContext(
    Vk Vk,
    CommandBuffer CommandBuffer,
    ImageView AttachmentView,
    Image AttachmentImage,
    DescriptorSet DescriptorSet,
    Format TargetFormat);
