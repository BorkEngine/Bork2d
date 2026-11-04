namespace Bork2d.Render.Vulkan;

/// <summary>
/// The device feature set the renderer requires (D-M6-08): dynamic rendering, synchronization 2,
/// <c>shaderDemoteToHelperInvocation</c>, the six descriptor-indexing features, and
/// <c>textureCompressionBC</c> for KTX2 block-compressed textures.
/// </summary>
/// <remarks>
/// The same shape carries a device's advertised features, the requirement set, and the enabled
/// features, so the pure selection policy and the production selector agree by construction.
/// <c>textureCompressionBC</c> is a desktop Vulkan 1.3 requirement: Bork2d has no RGBA fallback and
/// no runtime transcoder, so a device without it cannot render the baked BC textures and must fail
/// startup instead of silently drawing the placeholder. <c>shaderDemoteToHelperInvocation</c> is
/// required for fragment shaders that use <c>discard</c>/demote, for example the Pong court's
/// dashed center line: such a bake emits SPIR-V declaring that capability, and a device (or a
/// validation-enabled instance) without the feature rejects the shader module.
/// </remarks>
internal readonly record struct VulkanFeatureSet(
    bool DynamicRendering,
    bool Synchronization2,
    bool ShaderDemoteToHelperInvocation,
    bool RuntimeDescriptorArray,
    bool ShaderSampledImageArrayNonUniformIndexing,
    bool DescriptorBindingPartiallyBound,
    bool DescriptorBindingSampledImageUpdateAfterBind,
    bool DescriptorBindingUpdateUnusedWhilePending,
    bool DescriptorBindingVariableDescriptorCount,
    bool TextureCompressionBC)
{
    /// <summary>Gets the complete feature set the renderer requires and enables.</summary>
    internal static VulkanFeatureSet Required { get; } = new(
        DynamicRendering: true,
        Synchronization2: true,
        ShaderDemoteToHelperInvocation: true,
        RuntimeDescriptorArray: true,
        ShaderSampledImageArrayNonUniformIndexing: true,
        DescriptorBindingPartiallyBound: true,
        DescriptorBindingSampledImageUpdateAfterBind: true,
        DescriptorBindingUpdateUnusedWhilePending: true,
        DescriptorBindingVariableDescriptorCount: true,
        TextureCompressionBC: true);

    /// <summary>
    /// Gets the names of the features enabled here but missing from <paramref name="required"/>.
    /// </summary>
    /// <param name="required">The requirement set to check against.</param>
    /// <returns>The missing feature names in specification order; empty when all are present.</returns>
    internal IReadOnlyList<string> MissingFeatures(VulkanFeatureSet required)
    {
        var missing = new List<string>(capacity: 10);
        if (required.DynamicRendering && !DynamicRendering)
        {
            missing.Add("dynamicRendering");
        }

        if (required.Synchronization2 && !Synchronization2)
        {
            missing.Add("synchronization2");
        }

        if (required.ShaderDemoteToHelperInvocation && !ShaderDemoteToHelperInvocation)
        {
            missing.Add("shaderDemoteToHelperInvocation");
        }

        if (required.RuntimeDescriptorArray && !RuntimeDescriptorArray)
        {
            missing.Add("runtimeDescriptorArray");
        }

        if (required.ShaderSampledImageArrayNonUniformIndexing
            && !ShaderSampledImageArrayNonUniformIndexing)
        {
            missing.Add("shaderSampledImageArrayNonUniformIndexing");
        }

        if (required.DescriptorBindingPartiallyBound && !DescriptorBindingPartiallyBound)
        {
            missing.Add("descriptorBindingPartiallyBound");
        }

        if (required.DescriptorBindingSampledImageUpdateAfterBind
            && !DescriptorBindingSampledImageUpdateAfterBind)
        {
            missing.Add("descriptorBindingSampledImageUpdateAfterBind");
        }

        if (required.DescriptorBindingUpdateUnusedWhilePending
            && !DescriptorBindingUpdateUnusedWhilePending)
        {
            missing.Add("descriptorBindingUpdateUnusedWhilePending");
        }

        if (required.DescriptorBindingVariableDescriptorCount
            && !DescriptorBindingVariableDescriptorCount)
        {
            missing.Add("descriptorBindingVariableDescriptorCount");
        }

        if (required.TextureCompressionBC && !TextureCompressionBC)
        {
            missing.Add("textureCompressionBC");
        }

        return missing;
    }
}
