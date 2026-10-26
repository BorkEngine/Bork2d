using Bork2d.Api;
using PolyType;

namespace Bork2d.Assets;

/// <summary>
/// Builds a v1 asset pack for tooling and tests.
/// </summary>
/// <remarks>
/// <para>
/// This is the only public tooling surface in <c>Bork2d.Assets</c>; game code references only
/// <c>Bork2d.Api</c> and <c>Bork2d.Runtime</c>. The builder stores payloads in memory, is not
/// thread-safe, and never consumes itself: <see cref="Build"/> and <see cref="Write(string)"/> may
/// be called repeatedly, and entries may be added after a build.
/// </para>
/// <para>
/// Entry paths use the canonical asset-path rules. Entries are sorted by canonical path when
/// written. There is no byte-for-byte determinism guarantee: Zstd output may vary across runs and
/// runtime versions.
/// </para>
/// </remarks>
public sealed class PackBuilder
{
    private readonly PackCompressionMode _mode;
    private readonly Dictionary<string, PackSourceEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="PackBuilder"/> class.
    /// </summary>
    /// <param name="mode">The payload compression policy.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a known mode.</exception>
    public PackBuilder(PackCompressionMode mode)
    {
        if (mode is not PackCompressionMode.Debug and not PackCompressionMode.Release)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown pack compression mode.");
        }

        _mode = mode;
    }

    /// <summary>
    /// Adds an opaque payload.
    /// </summary>
    /// <param name="path">The asset path; it is normalized to canonical form and copied.</param>
    /// <param name="payload">The payload bytes, which are copied into the builder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not a valid asset path.</exception>
    /// <exception cref="Bork2dException">The canonical path is already present.</exception>
    public void AddRaw(string path, ReadOnlySpan<byte> payload)
    {
        string canonicalPath = AssetPath.Normalize(path);
        ThrowIfDuplicate(canonicalPath);
        _entries.Add(canonicalPath, new PackSourceEntry(canonicalPath, isRaw: true, payload.ToArray()));
    }

    /// <summary>
    /// Serializes a value with its source-generated MessagePack shape and adds it as an asset.
    /// </summary>
    /// <typeparam name="T">The shapeable asset type.</typeparam>
    /// <param name="path">The asset path; it is normalized to canonical form.</param>
    /// <param name="value">The value to serialize.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not a valid asset path.</exception>
    /// <exception cref="Bork2dException">
    /// The canonical path is already present, or serialization failed.
    /// </exception>
    public void AddMessagePack<T>(string path, T value)
        where T : IShapeable<T>
    {
        ArgumentNullException.ThrowIfNull(value);

        string canonicalPath = AssetPath.Normalize(path);
        ThrowIfDuplicate(canonicalPath);

        byte[] payload;
        try
        {
            payload = PackSerialization.SerializeAsset(value);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new Bork2dException(
                $"Failed to serialize MessagePack asset '{canonicalPath}': {exception.Message}",
                exception);
        }

        _entries.Add(canonicalPath, new PackSourceEntry(canonicalPath, isRaw: false, payload));
    }

    /// <summary>
    /// Builds the complete pack in one array.
    /// </summary>
    /// <returns>The pack bytes.</returns>
    /// <exception cref="Bork2dException">The pack exceeds <see cref="Array.MaxLength"/>.</exception>
    public byte[] Build()
    {
        PackLayout layout = CreateLayout();
        return layout.ToArray();
    }

    /// <summary>
    /// Writes the complete pack to an overwritten file.
    /// </summary>
    /// <param name="path">
    /// The filesystem path; relative paths resolve against the process current directory.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <exception cref="Bork2dException">The pack could not be written.</exception>
    public void Write(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The pack path must not be empty or whitespace.", nameof(path));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            throw new ArgumentException(
                $"Invalid pack path '{path}': {exception.Message}",
                nameof(path),
                exception);
        }

        PackLayout layout = CreateLayout();
        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            layout.WriteTo(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            throw new Bork2dException(
                $"Failed to write asset pack '{fullPath}': {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Streams the complete pack to a caller-supplied stream and leaves the stream open.
    /// </summary>
    /// <param name="destination">The writable destination stream.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    /// <exception cref="Bork2dException">The pack could not be written.</exception>
    public void Write(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The pack destination must be writable.", nameof(destination));
        }

        PackLayout layout = CreateLayout();
        try
        {
            layout.WriteTo(destination);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            throw new Bork2dException(
                $"Failed to write the asset pack: {exception.Message}",
                exception);
        }
    }

    private PackLayout CreateLayout()
    {
        var source = new List<PackSourceEntry>(_entries.Count);
        source.AddRange(_entries.Values);

        try
        {
            return PackLayout.Create(source, _mode);
        }
        catch (OverflowException exception)
        {
            throw new Bork2dException(
                "The pack size exceeds the v1 field widths or the supported file length.",
                exception);
        }
    }

    private void ThrowIfDuplicate(string canonicalPath)
    {
        if (_entries.ContainsKey(canonicalPath))
        {
            throw new Bork2dException($"Duplicate asset path '{canonicalPath}'.");
        }
    }
}
