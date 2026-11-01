using Bork2d.Engine.Assets;

namespace Bork2d.Render.Textures;

/// <summary>
/// One texture page record: the canonical descriptor path, its lifecycle state, the assigned
/// bindless index, and the in-flight descriptor/payload loads (M8 §6.5).
/// </summary>
/// <remarks>
/// The record is created on first sighting of a path. A successful load assigns an index; a failed
/// path keeps its record (<see cref="TexturePageState.Failed"/>) so a later descriptor for the same
/// path reuses the failure instead of retrying. A catalog change (a later overlay mount) retires the
/// whole record set: these records leave the page map and are never reused, while a later
/// <c>EnsurePage</c> creates a fresh record that reloads the path from the new catalog.
/// </remarks>
internal sealed class TexturePage
{
    /// <summary>Initializes a new instance of the <see cref="TexturePage"/> class.</summary>
    /// <param name="path">The canonical texture descriptor path.</param>
    internal TexturePage(string path) => Path = path;

    /// <summary>Gets the canonical texture descriptor path that identifies the page.</summary>
    internal string Path { get; }

    /// <summary>Gets or sets the current lifecycle state.</summary>
    internal TexturePageState State { get; set; }

    /// <summary>Gets or sets why the content was rejected, or <c>None</c>.</summary>
    internal TexturePageFailure Failure { get; set; }

    /// <summary>Gets or sets the assigned bindless index, or <c>-1</c> before one is assigned.</summary>
    internal int Index { get; set; } = -1;

    /// <summary>Gets or sets the quad-size width: the descriptor's declared width when valid.</summary>
    internal int Width { get; set; }

    /// <summary>Gets or sets the quad-size height: the descriptor's declared height when valid.</summary>
    internal int Height { get; set; }

    /// <summary>Gets or sets the in-flight descriptor load, or <see langword="null"/>.</summary>
    internal Task<TextureDescriptor>? DescriptorTask { get; set; }

    /// <summary>Gets or sets the in-flight payload load, or <see langword="null"/>.</summary>
    internal Task<byte[]>? PayloadTask { get; set; }

    /// <summary>Gets or sets the loaded descriptor, or <see langword="null"/> before it loads.</summary>
    internal TextureDescriptor? Descriptor { get; set; }

    /// <summary>
    /// Gets or sets the validated payload held while the page waits for its upload budget, or
    /// <see langword="null"/> before validation and after the upload is submitted.
    /// </summary>
    internal byte[]? Payload { get; set; }

    /// <summary>Gets or sets the validated payload size in bytes (<c>Width * Height * 4</c>).</summary>
    internal long PayloadBytes { get; set; }

    /// <summary>
    /// Gets or sets the store frame sequence in which <c>EnsurePage</c> last saw this path; the
    /// upload planner starts the most recently seen pages first.
    /// </summary>
    internal long LastSeenFrame { get; set; }

    /// <summary>Gets or sets the first-sighting order, which breaks equal-priority upload ties.</summary>
    internal long LoadSequence { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a catalog change retired this page while it was
    /// uploading. A retired upload still completes to release its GPU resources but never becomes
    /// visible or counts as ready.
    /// </summary>
    internal bool Retired { get; set; }
}
