namespace Bork2d.Assets;

/// <summary>
/// One payload queued for a pack build.
/// </summary>
internal sealed class PackSourceEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PackSourceEntry"/> class.
    /// </summary>
    /// <param name="path">The canonical asset path.</param>
    /// <param name="isRaw">Whether the payload is opaque raw bytes.</param>
    /// <param name="payload">The uncompressed payload bytes.</param>
    internal PackSourceEntry(string path, bool isRaw, byte[] payload)
    {
        Path = path;
        IsRaw = isRaw;
        Payload = payload;
    }

    /// <summary>Gets the canonical asset path.</summary>
    internal string Path { get; }

    /// <summary>Gets a value indicating whether the payload is raw bytes.</summary>
    internal bool IsRaw { get; }

    /// <summary>Gets the uncompressed payload bytes.</summary>
    internal byte[] Payload { get; }
}
