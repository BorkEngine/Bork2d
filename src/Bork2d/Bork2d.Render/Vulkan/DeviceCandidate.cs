using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// A plain description of one enumerated physical device, used by the pure selection policy and
/// by the production selector's one-shot candidate logging.
/// </summary>
/// <param name="Name">The device name reported by Vulkan.</param>
/// <param name="Type">The reported device type.</param>
/// <param name="ApiVersion">The packed Vulkan API version the device supports.</param>
/// <param name="DriverVersion">The packed driver version.</param>
/// <param name="QueueFamilies">The queue families; present support is filled in windowed mode.</param>
/// <param name="Extensions">The device extensions the device advertises.</param>
/// <param name="Features">The features M6 cares about as advertised by the device.</param>
internal readonly record struct DeviceCandidate(
    string Name,
    PhysicalDeviceType Type,
    uint ApiVersion,
    uint DriverVersion,
    IReadOnlyList<QueueFamilyDescriptor> QueueFamilies,
    IReadOnlyList<string> Extensions,
    VulkanFeatureSet Features);
