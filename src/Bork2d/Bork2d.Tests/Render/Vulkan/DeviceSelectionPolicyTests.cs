using Bork2d.Render.Vulkan;
using Silk.NET.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Deterministic tests for <see cref="VulkanDevicePolicy"/>; no Vulkan call or GPU is involved.
/// </summary>
public sealed class DeviceSelectionPolicyTests
{
    [Fact]
    public void EvaluateAcceptsAWindowedDeviceWithEveryRequirement()
    {
        DeviceEvaluation evaluation = VulkanDevicePolicy.Evaluate(
            Candidate(),
            requirePresent: true);

        Assert.True(evaluation.Accepted, evaluation.RejectionReason);
        Assert.Null(evaluation.RejectionReason);
        Assert.NotNull(evaluation.QueueFamily);
        Assert.Equal(0u, evaluation.QueueFamily.Value.Index);
    }

    [Fact]
    public void EvaluateAcceptsAnOffscreenDeviceWithoutSwapchainOrPresentSupport()
    {
        DeviceCandidate candidate = Candidate(present: false, swapchain: false);

        DeviceEvaluation evaluation = VulkanDevicePolicy.Evaluate(
            candidate,
            requirePresent: false);

        Assert.True(evaluation.Accepted, evaluation.RejectionReason);
    }

    [Fact]
    public void EvaluateRejectsApiVersionBelow13AndNamesTheReportedVersion()
    {
        DeviceCandidate candidate = Candidate(apiVersion: VulkanVersion.Make(1, 2, 3));

        DeviceEvaluation evaluation = VulkanDevicePolicy.Evaluate(candidate, requirePresent: false);

        Assert.False(evaluation.Accepted);
        Assert.Contains("1.2.3", evaluation.RejectionReason, StringComparison.Ordinal);
        Assert.Contains("1.3.0", evaluation.RejectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateRejectsADeviceWithoutAGraphicsQueueFamily()
    {
        DeviceCandidate candidate = Candidate(graphics: false);

        DeviceEvaluation evaluation = VulkanDevicePolicy.Evaluate(candidate, requirePresent: false);

        Assert.False(evaluation.Accepted);
        Assert.Contains("graphics queue family", evaluation.RejectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateRejectsAWindowedDeviceWhoseGraphicsFamilyCannotPresent()
    {
        DeviceCandidate candidate = Candidate(present: false);

        DeviceEvaluation windowed = VulkanDevicePolicy.Evaluate(candidate, requirePresent: true);
        DeviceEvaluation offscreen = VulkanDevicePolicy.Evaluate(candidate, requirePresent: false);

        Assert.False(windowed.Accepted);
        Assert.Contains("presentation", windowed.RejectionReason, StringComparison.Ordinal);
        Assert.True(offscreen.Accepted, offscreen.RejectionReason);
    }

    [Fact]
    public void EvaluateRejectsAWindowedDeviceWithoutTheSwapchainExtension()
    {
        DeviceCandidate candidate = Candidate(swapchain: false);

        DeviceEvaluation windowed = VulkanDevicePolicy.Evaluate(candidate, requirePresent: true);
        DeviceEvaluation offscreen = VulkanDevicePolicy.Evaluate(candidate, requirePresent: false);

        Assert.False(windowed.Accepted);
        Assert.Contains(
            VulkanNames.SwapchainExtension,
            windowed.RejectionReason,
            StringComparison.Ordinal);
        Assert.True(offscreen.Accepted, offscreen.RejectionReason);
    }

    [Fact]
    public void EvaluateRejectsAQueueFamilyWithNoQueues()
    {
        DeviceCandidate candidate = Candidate() with
        {
            QueueFamilies = [new QueueFamilyDescriptor(0, 0, Graphics: true, false, false, true)],
        };

        DeviceEvaluation evaluation = VulkanDevicePolicy.Evaluate(candidate, requirePresent: false);

        Assert.False(evaluation.Accepted);
        Assert.Contains("graphics queue family", evaluation.RejectionReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dynamicRendering")]
    [InlineData("synchronization2")]
    [InlineData("runtimeDescriptorArray")]
    [InlineData("shaderSampledImageArrayNonUniformIndexing")]
    [InlineData("descriptorBindingPartiallyBound")]
    [InlineData("descriptorBindingSampledImageUpdateAfterBind")]
    [InlineData("descriptorBindingUpdateUnusedWhilePending")]
    [InlineData("descriptorBindingVariableDescriptorCount")]
    public void EvaluateRejectsEachMissingRequiredFeature(string featureName)
    {
        VulkanFeatureSet features = new(
            DynamicRendering: featureName != "dynamicRendering",
            Synchronization2: featureName != "synchronization2",
            RuntimeDescriptorArray: featureName != "runtimeDescriptorArray",
            ShaderSampledImageArrayNonUniformIndexing:
                featureName != "shaderSampledImageArrayNonUniformIndexing",
            DescriptorBindingPartiallyBound: featureName != "descriptorBindingPartiallyBound",
            DescriptorBindingSampledImageUpdateAfterBind:
                featureName != "descriptorBindingSampledImageUpdateAfterBind",
            DescriptorBindingUpdateUnusedWhilePending:
                featureName != "descriptorBindingUpdateUnusedWhilePending",
            DescriptorBindingVariableDescriptorCount:
                featureName != "descriptorBindingVariableDescriptorCount");

        DeviceEvaluation evaluation = VulkanDevicePolicy.Evaluate(
            Candidate(features: features),
            requirePresent: false);

        Assert.False(evaluation.Accepted);
        Assert.Contains(featureName, evaluation.RejectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateNameEveryMissingRequirementInOneMessage()
    {
        DeviceCandidate candidate = Candidate(
            apiVersion: VulkanVersion.Make(1, 2, 0),
            present: false,
            swapchain: false,
            features: VulkanFeatureSet.Required with { DynamicRendering = false });

        DeviceEvaluation evaluation = VulkanDevicePolicy.Evaluate(candidate, requirePresent: true);

        Assert.False(evaluation.Accepted);
        Assert.Contains("1.2.0", evaluation.RejectionReason, StringComparison.Ordinal);
        Assert.Contains(VulkanNames.SwapchainExtension, evaluation.RejectionReason, StringComparison.Ordinal);
        Assert.Contains("dynamicRendering", evaluation.RejectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectPreferredPrefersDiscreteThenIntegratedThenOther()
    {
        DeviceEvaluation integrated = VulkanDevicePolicy.Evaluate(
            Candidate(type: PhysicalDeviceType.IntegratedGpu, name: "integrated"),
            requirePresent: false);
        DeviceEvaluation discrete = VulkanDevicePolicy.Evaluate(
            Candidate(type: PhysicalDeviceType.DiscreteGpu, name: "discrete"),
            requirePresent: false);
        DeviceEvaluation virtualGpu = VulkanDevicePolicy.Evaluate(
            Candidate(type: PhysicalDeviceType.VirtualGpu, name: "virtual"),
            requirePresent: false);

        Assert.Equal(1, VulkanDevicePolicy.SelectPreferred([integrated, discrete, virtualGpu]));
    }

    [Fact]
    public void SelectPreferredFallsBackToIntegratedWhenNoDiscreteDeviceIsAccepted()
    {
        DeviceEvaluation rejectedDiscrete = VulkanDevicePolicy.Evaluate(
            Candidate(type: PhysicalDeviceType.DiscreteGpu, graphics: false),
            requirePresent: false);
        DeviceEvaluation integrated = VulkanDevicePolicy.Evaluate(
            Candidate(type: PhysicalDeviceType.IntegratedGpu),
            requirePresent: false);

        Assert.Equal(1, VulkanDevicePolicy.SelectPreferred([rejectedDiscrete, integrated]));
    }

    [Fact]
    public void SelectPreferredUsesEnumerationOrderWithinAClass()
    {
        DeviceEvaluation first = VulkanDevicePolicy.Evaluate(
            Candidate(name: "first"),
            requirePresent: false);
        DeviceEvaluation second = VulkanDevicePolicy.Evaluate(
            Candidate(name: "second"),
            requirePresent: false);

        Assert.Equal(0, VulkanDevicePolicy.SelectPreferred([first, second]));
    }

    [Fact]
    public void SelectPreferredReturnsMinusOneWhenNothingIsAccepted()
    {
        DeviceEvaluation rejected = VulkanDevicePolicy.Evaluate(
            Candidate(graphics: false),
            requirePresent: false);

        Assert.Equal(-1, VulkanDevicePolicy.SelectPreferred([rejected]));
    }

    [Fact]
    public void PreferenceRankOrdersDiscreteIntegratedAndOtherTypes()
    {
        Assert.Equal(0, VulkanDevicePolicy.PreferenceRank(PhysicalDeviceType.DiscreteGpu));
        Assert.Equal(1, VulkanDevicePolicy.PreferenceRank(PhysicalDeviceType.IntegratedGpu));
        Assert.Equal(2, VulkanDevicePolicy.PreferenceRank(PhysicalDeviceType.VirtualGpu));
        Assert.Equal(2, VulkanDevicePolicy.PreferenceRank(PhysicalDeviceType.Cpu));
        Assert.Equal(2, VulkanDevicePolicy.PreferenceRank(PhysicalDeviceType.Other));
    }

    private static DeviceCandidate Candidate(
        PhysicalDeviceType type = PhysicalDeviceType.DiscreteGpu,
        uint? apiVersion = null,
        bool graphics = true,
        bool present = true,
        bool swapchain = true,
        VulkanFeatureSet? features = null,
        string name = "Test device")
    {
        string[] extensions = swapchain ? [VulkanNames.SwapchainExtension] : [];
        return new DeviceCandidate(
            name,
            type,
            apiVersion ?? VulkanVersion.ApiVersion13,
            0x01020304,
            [new QueueFamilyDescriptor(0, 1, graphics, Compute: false, Transfer: false, present)],
            extensions,
            features ?? VulkanFeatureSet.Required);
    }
}
