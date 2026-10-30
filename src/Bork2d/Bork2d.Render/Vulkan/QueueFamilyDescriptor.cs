namespace Bork2d.Render.Vulkan;

/// <summary>
/// A plain description of one physical-device queue family, used by the pure selection policy.
/// </summary>
/// <param name="Index">The queue family index reported by Vulkan.</param>
/// <param name="QueueCount">The number of queues in the family.</param>
/// <param name="Graphics">Whether the family supports graphics commands.</param>
/// <param name="Compute">Whether the family supports compute commands.</param>
/// <param name="Transfer">Whether the family supports transfer commands.</param>
/// <param name="Present">Whether the family can present to the surface (windowed only).</param>
internal readonly record struct QueueFamilyDescriptor(
    uint Index,
    uint QueueCount,
    bool Graphics,
    bool Compute,
    bool Transfer,
    bool Present);
