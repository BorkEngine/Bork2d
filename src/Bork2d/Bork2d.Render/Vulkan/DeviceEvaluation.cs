namespace Bork2d.Render.Vulkan;

/// <summary>
/// The pure policy result for one <see cref="DeviceCandidate"/>.
/// </summary>
/// <param name="Candidate">The evaluated candidate.</param>
/// <param name="Accepted">Whether the candidate satisfies every M6 requirement.</param>
/// <param name="RejectionReason">
/// The first rejection reason, or <see langword="null"/> when accepted. Multiple reasons are
/// joined so the startup failure names every missing requirement.
/// </param>
/// <param name="QueueFamily">
/// The selected graphics (and, windowed, present) queue family when accepted; otherwise
/// <see langword="null"/>.
/// </param>
internal readonly record struct DeviceEvaluation(
    DeviceCandidate Candidate,
    bool Accepted,
    string? RejectionReason,
    QueueFamilyDescriptor? QueueFamily);
