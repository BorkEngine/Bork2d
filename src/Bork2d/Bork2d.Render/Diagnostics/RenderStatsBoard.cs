using Bork2d.Engine.Rendering;

namespace Bork2d.Render.Diagnostics;

/// <summary>
/// The render-thread-owned board that holds the last completed frame's <see cref="RenderStats"/>
/// and serves the thread-safe read the host installs as the engine's stats source (M8 §6.9).
/// </summary>
/// <remarks>
/// The renderer publishes once per completed frame; readers get a consistent value under a short
/// lock, never a torn struct. Reads never touch Vulkan and never block on the GPU.
/// </remarks>
internal sealed class RenderStatsBoard
{
    private readonly Lock _gate = new();
    private RenderStats _stats;

    /// <summary>Gets the last published snapshot, or <see langword="default"/> before the first frame.</summary>
    internal RenderStats Current
    {
        get
        {
            lock (_gate)
            {
                return _stats;
            }
        }
    }

    /// <summary>Publishes the completed frame's snapshot.</summary>
    /// <param name="stats">The immutable frame stats.</param>
    internal void Publish(in RenderStats stats)
    {
        lock (_gate)
        {
            _stats = stats;
        }
    }
}
