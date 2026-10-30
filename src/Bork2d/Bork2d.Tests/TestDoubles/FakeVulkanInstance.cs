using Bork2d.Render.Vulkan;
using Silk.NET.Vulkan;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A fake <see cref="IVulkanInstance"/> that records the debug-label and surface calls without
/// loading <c>vulkan-1.dll</c>.
/// </summary>
internal sealed class FakeVulkanInstance : IVulkanInstance
{
    private int _destroyCount;

    /// <inheritdoc/>
    public nint Handle { get; init; } = 1;

    /// <inheritdoc/>
    public bool ValidationEnabled { get; init; }

    /// <inheritdoc/>
    public bool DebugUtilsEnabled { get; init; }

    /// <inheritdoc/>
    public IReadOnlyList<string> EnabledLayers { get; init; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> EnabledExtensions { get; init; } = [];

    /// <inheritdoc/>
    public int ValidationErrorCount { get; init; }

    /// <inheritdoc/>
    public int ValidationWarningCount { get; init; }

    /// <inheritdoc/>
    public Exception? FatalException { get; init; }

    /// <summary>Gets the recorded object names.</summary>
    internal List<(ObjectType ObjectType, nint Handle, string Name)> ObjectNames { get; } = [];

    /// <summary>Gets the recorded command-buffer label transitions.</summary>
    internal List<(nint CommandBuffer, string? Label)> CommandBufferLabels { get; } = [];

    /// <summary>Gets the destroyed surface handles.</summary>
    internal List<nint> DestroyedSurfaces { get; } = [];

    /// <summary>Gets the pre-encoded labels opened on command buffers.</summary>
    internal List<(nint CommandBuffer, nint Label)> CachedCommandBufferLabels { get; } = [];

    /// <summary>Gets the number of debug labels allocated.</summary>
    internal int CreatedDebugLabelCount { get; private set; }

    /// <summary>Gets the number of debug labels destroyed.</summary>
    internal int DestroyedDebugLabelCount { get; private set; }

    /// <summary>Gets the number of destroy calls.</summary>
    internal int DestroyCount => Volatile.Read(ref _destroyCount);

    /// <inheritdoc/>
    public void Destroy() => Interlocked.Exchange(ref _destroyCount, 1);

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <inheritdoc/>
    public void DestroySurface(nint surface)
    {
        if (surface != 0)
        {
            DestroyedSurfaces.Add(surface);
        }
    }

    /// <inheritdoc/>
    public void SetObjectName(nint device, ObjectType objectType, nint objectHandle, string name)
        => ObjectNames.Add((objectType, objectHandle, name));

    /// <inheritdoc/>
    public void BeginCommandBufferLabel(nint device, nint commandBuffer, string label)
        => CommandBufferLabels.Add((commandBuffer, label));

    /// <inheritdoc/>
    public void EndCommandBufferLabel(nint device, nint commandBuffer)
        => CommandBufferLabels.Add((commandBuffer, null));

    /// <inheritdoc/>
    public nint CreateDebugLabel(string label)
    {
        if (!DebugUtilsEnabled || string.IsNullOrEmpty(label))
        {
            return 0;
        }

        CreatedDebugLabelCount++;
        return 0x7000 + CreatedDebugLabelCount;
    }

    /// <inheritdoc/>
    public void DestroyDebugLabel(nint label)
    {
        if (label == 0)
        {
            return;
        }

        DestroyedDebugLabelCount++;
    }

    /// <inheritdoc/>
    public void BeginCachedCommandBufferLabel(nint device, nint commandBuffer, nint label)
        => CachedCommandBufferLabels.Add((commandBuffer, label));
}
