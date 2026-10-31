namespace Bork2d.Engine.Assets;

/// <summary>
/// Classifies an <see cref="AssetException"/>.
/// </summary>
/// <remarks>
/// Pack open/parse/mount failures are reported as <see cref="Bork2dException"/>, not as an
/// <see cref="AssetException"/>. Argument and programming errors use the BCL exceptions.
/// </remarks>
public enum AssetErrorKind
{
    /// <summary>
    /// A valid canonical asset path is not present in the merged filesystem.
    /// </summary>
    NotFound,

    /// <summary>
    /// A raw entry was requested through the MessagePack-asset load path.
    /// </summary>
    /// <remarks>
    /// Use <see cref="IAssets.LoadData(string)"/> when the payload is opaque bytes.
    /// </remarks>
    RawAsset,

    /// <summary>
    /// A MessagePack or Zstd payload could not be decoded, or the decoded asset was null.
    /// </summary>
    Decode,

    /// <summary>
    /// Reading a mounted pack or a tooling file failed.
    /// </summary>
    Io,

    /// <summary>
    /// A valid pack or payload has an invalid shape at load time.
    /// </summary>
    /// <remarks>
    /// For example, a declared size above <see cref="Array.MaxLength"/> for
    /// <see cref="IAssets.LoadData(string)"/>. Asset-field and reference validation belongs to
    /// pack creation, not the runtime loader.
    /// </remarks>
    InvalidData,
}
