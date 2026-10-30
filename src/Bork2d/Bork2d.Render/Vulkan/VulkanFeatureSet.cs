namespace Bork2d.Render.Vulkan;

/// <summary>
/// The device feature set M6 requires (D-M6-08): dynamic rendering, synchronization 2, and the
/// six descriptor-indexing features.
/// </summary>
/// <remarks>
/// The same shape carries a device's advertised features, the requirement set, and the enabled
/// features, so the pure selection policy and the production selector agree by construction.
/// </remarks>
internal readonly record struct VulkanFeatureSet(
    bool DynamicRendering,
    bool Synchronization2,
    bool RuntimeDescriptorArray,
    bool ShaderSampledImageArrayNonUniformIndexing,
    bool DescriptorBindingPartiallyBound,
    bool DescriptorBindingSampledImageUpdateAfterBind,
    bool DescriptorBindingUpdateUnusedWhilePending,
    bool DescriptorBindingVariableDescriptorCount)
{
    /// <summary>Gets the complete feature set M6 requires and enables.</summary>
    internal static VulkanFeatureSet Required { get; } = new(
        DynamicRendering: true,
        Synchronization2: true,
        RuntimeDescriptorArray: true,
        ShaderSampledImageArrayNonUniformIndexing: true,
        DescriptorBindingPartiallyBound: true,
        DescriptorBindingSampledImageUpdateAfterBind: true,
        DescriptorBindingUpdateUnusedWhilePending: true,
        DescriptorBindingVariableDescriptorCount: true);

    /// <summary>
    /// Gets the names of the features enabled here but missing from <paramref name="required"/>.
    /// </summary>
    /// <param name="required">The requirement set to check against.</param>
    /// <returns>The missing feature names in specification order; empty when all are present.</returns>
    internal IReadOnlyList<string> MissingFeatures(VulkanFeatureSet required)
    {
        var missing = new List<string>(capacity: 8);
        if (required.DynamicRendering && !DynamicRendering)
        {
            missing.Add("dynamicRendering");
        }

        if (required.Synchronization2 && !Synchronization2)
        {
            missing.Add("synchronization2");
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

        return missing;
    }
}
