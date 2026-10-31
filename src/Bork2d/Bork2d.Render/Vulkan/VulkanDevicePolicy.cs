using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The pure physical-device acceptance and preference policy.
/// </summary>
/// <remarks>
/// The policy never calls Vulkan: it operates on <see cref="DeviceCandidate"/> descriptions so it
/// is deterministic and testable without a GPU. The production selector fills the descriptions
/// once, logs each evaluation, and creates the logical device for the accepted candidate.
/// </remarks>
internal static class VulkanDevicePolicy
{
    /// <summary>
    /// Evaluates one candidate against the M6 requirements.
    /// </summary>
    /// <param name="candidate">The candidate description.</param>
    /// <param name="requirePresent">
    /// Whether the candidate must present to the surface on its graphics queue family (windowed).
    /// Offscreen passes <see langword="false"/>.
    /// </param>
    /// <returns>The acceptance result and the selected queue family when accepted.</returns>
    internal static DeviceEvaluation Evaluate(DeviceCandidate candidate, bool requirePresent)
    {
        var reasons = new List<string>(capacity: 4);

        if (candidate.ApiVersion < VulkanVersion.ApiVersion13)
        {
            reasons.Add(
                $"Vulkan {VulkanVersion.Format(candidate.ApiVersion)} is below the required 1.3.0");
        }

        if (requirePresent && !ContainsExtension(candidate.Extensions, VulkanNames.SwapchainExtension))
        {
            reasons.Add($"missing required device extension {VulkanNames.SwapchainExtension}");
        }

        QueueFamilyDescriptor? queueFamily = SelectQueueFamily(
            candidate.QueueFamilies,
            requirePresent,
            reasons);

        IReadOnlyList<string> missingFeatures = candidate.Features.MissingFeatures(
            VulkanFeatureSet.Required);
        if (missingFeatures.Count > 0)
        {
            reasons.Add($"missing required device features: {string.Join(", ", missingFeatures)}");
        }

        return reasons.Count == 0 && queueFamily is not null
            ? new DeviceEvaluation(candidate, Accepted: true, RejectionReason: null, queueFamily)
            : new DeviceEvaluation(candidate, Accepted: false, string.Join("; ", reasons), queueFamily);
    }

    /// <summary>
    /// Selects the accepted candidate with the best preference class (discrete, then integrated,
    /// then any other), preserving enumeration order within a class.
    /// </summary>
    /// <param name="evaluations">The evaluated candidates in enumeration order.</param>
    /// <returns>The selected index, or <c>-1</c> when no candidate was accepted.</returns>
    internal static int SelectPreferred(IReadOnlyList<DeviceEvaluation> evaluations)
    {
        var selected = -1;
        var selectedRank = int.MaxValue;

        for (var index = 0; index < evaluations.Count; index++)
        {
            DeviceEvaluation evaluation = evaluations[index];
            if (!evaluation.Accepted)
            {
                continue;
            }

            var rank = PreferenceRank(evaluation.Candidate.Type);
            if (rank < selectedRank)
            {
                selected = index;
                selectedRank = rank;
            }
        }

        return selected;
    }

    /// <summary>Gets the M6 preference rank for a device type; lower is preferred.</summary>
    /// <param name="type">The reported physical-device type.</param>
    /// <returns><c>0</c> for discrete, <c>1</c> for integrated, <c>2</c> for anything else.</returns>
    internal static int PreferenceRank(PhysicalDeviceType type)
        => type switch
        {
            PhysicalDeviceType.DiscreteGpu => 0,
            PhysicalDeviceType.IntegratedGpu => 1,
            _ => 2,
        };

    private static QueueFamilyDescriptor? SelectQueueFamily(
        IReadOnlyList<QueueFamilyDescriptor>? families,
        bool requirePresent,
        List<string> reasons)
    {
        if (families is null || families.Count == 0)
        {
            reasons.Add("no queue families");
            return null;
        }

        var hasGraphicsFamily = false;
        for (var index = 0; index < families.Count; index++)
        {
            QueueFamilyDescriptor family = families[index];
            if (!family.Graphics || family.QueueCount == 0)
            {
                continue;
            }

            hasGraphicsFamily = true;
            if (!requirePresent || family.Present)
            {
                return family;
            }
        }

        reasons.Add(
            hasGraphicsFamily
                ? "no graphics queue family supports presentation"
                : "no graphics queue family");
        return null;
    }

    private static bool ContainsExtension(IReadOnlyList<string>? extensions, string name)
    {
        if (extensions is null)
        {
            return false;
        }

        for (var index = 0; index < extensions.Count; index++)
        {
            if (string.Equals(extensions[index], name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
