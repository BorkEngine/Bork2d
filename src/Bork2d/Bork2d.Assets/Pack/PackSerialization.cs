using Nerdbank.MessagePack;
using PolyType;

namespace Bork2d.Assets.Pack;

/// <summary>
/// The single Nerdbank.MessagePack serializer used for pack headers and MessagePack assets.
/// </summary>
/// <remarks>
/// The serializer is immutable and thread-safe, so one instance is shared. It has no reflection
/// resolver: every type must carry a source-generated shape.
/// </remarks>
internal static class PackSerialization
{
    private static readonly MessagePackSerializer _serializer = new();

    /// <summary>
    /// Serializes the pack header with its generated shape.
    /// </summary>
    /// <param name="header">The header to serialize.</param>
    /// <returns>The MessagePack bytes.</returns>
    internal static byte[] SerializeHeader(PackHeader header) => _serializer.Serialize(header);

    /// <summary>
    /// Deserializes a pack header with its generated shape.
    /// </summary>
    /// <param name="payload">The MessagePack bytes.</param>
    /// <returns>The header, or <see langword="null"/> when the payload is MessagePack nil.</returns>
    internal static PackHeader? DeserializeHeader(ReadOnlyMemory<byte> payload)
        => _serializer.Deserialize<PackHeader>(payload);

    /// <summary>
    /// Serializes a MessagePack asset with the generated shape on <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The shapeable asset type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The MessagePack bytes.</returns>
    internal static byte[] SerializeAsset<T>(T value)
        where T : IShapeable<T>
        => _serializer.Serialize(value);

    /// <summary>
    /// Deserializes a MessagePack asset from a decompressed payload stream with the generated shape
    /// on <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// The shape is passed explicitly from <see cref="IShapeable{T}"/>; the serializer's
    /// reflection-shape provider is never consulted. A root may be a class or a struct: the loader
    /// rejects an empty or MessagePack <c>nil</c> payload before calling this method, so a value-type
    /// root is never silently defaulted. The caller owns the stream and is responsible for verifying
    /// that the declared payload size was consumed exactly.
    /// </remarks>
    /// <typeparam name="T">The shapeable asset type.</typeparam>
    /// <param name="stream">The readable payload stream.</param>
    /// <returns>The deserialized asset, or <see langword="null"/> for a MessagePack nil payload.</returns>
    internal static T? DeserializeAsset<T>(Stream stream)
        where T : IShapeable<T>
        => _serializer.Deserialize<T>(stream, T.GetTypeShape());
}
