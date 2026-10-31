using Bork2d.Engine.Assets;

namespace Bork2d.Render.Textures;

/// <summary>
/// One texture page record: the canonical descriptor path, its lifecycle state, the assigned
/// bindless index, and the in-flight descriptor/payload loads (M8 §6.5).
/// </summary>
/// <remarks>
/// The record is created on first sighting of a path and lives for the run. A successful load
/// assigns an index; a failed path keeps its record (<see cref="TexturePageState.Failed"/>) so a
/// later descriptor for the same path reuses the failure instead of retrying.
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
}
