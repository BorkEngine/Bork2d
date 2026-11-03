using Bork2d.Engine.Assets;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Shaders;

/// <summary>
/// The seam the sprite frame planner uses to resolve a captured sprite's shader to a drawable entry.
/// </summary>
/// <remarks>
/// <para>
/// The production implementation is <see cref="ShaderStore"/>, which owns asynchronous loads, the
/// content-hash module/pipeline caches, validation, fallback, and catalog retirement. Deterministic
/// tests substitute a fake so pending/ready/failed bindings, run grouping, extra texture resolution,
/// and global blocks are verified without Vulkan.
/// </para>
/// <para>
/// Every member runs on the render thread. <see cref="Resolve"/> never blocks on an asset load and
/// never creates a Vulkan object in a test double; the output format is supplied so the production
/// store can lazily create that format's pipeline.
/// </para>
/// </remarks>
internal interface IShaderFrameSource
{
    /// <summary>Gets the entry id of the built-in sprite shader, used for every fallback.</summary>
    int DefaultEntryId { get; }

    /// <summary>
    /// Resolves one captured sprite's frozen shader metadata for the frame being planned.
    /// </summary>
    /// <remarks>
    /// A <see langword="null"/> <paramref name="shader"/> selects the default entry. A custom entry
    /// is keyed by its SPIR-V path and starts at most one asynchronous load on first sighting in the
    /// current catalog. The call is allocation-free for an entry that is already known.
    /// </remarks>
    /// <param name="shader">The frozen custom shader metadata, or <see langword="null"/> for the default.</param>
    /// <param name="outputFormat">The target's sRGB output color format for this frame.</param>
    /// <returns>The binding to draw, wait for, or fall back from.</returns>
    ShaderBinding Resolve(ShaderRenderData? shader, Format outputFormat);

    /// <summary>Gets the entry a ready binding names, or the default entry.</summary>
    /// <param name="entryId">The entry id from a ready binding or <see cref="DefaultEntryId"/>.</param>
    /// <returns>The entry whose frozen metadata the planner reads.</returns>
    ShaderEntry GetEntry(int entryId);
}
