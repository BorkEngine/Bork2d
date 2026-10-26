using Nerdbank.MessagePack;

namespace Bork2d.Common.Serialization;

/// <summary>
/// Round-trips <see cref="MessagePackSmokeData"/> through Nerdbank.MessagePack using only the
/// source-generated shape.
/// </summary>
/// <remarks>
/// <para>
/// The serializer is created with its default constructor and no reflection resolver: the
/// generated shape on <see cref="MessagePackSmokeData"/> is the only serialization metadata
/// involved. The instance is immutable and thread-safe, so it is shared.
/// </para>
/// <para>
/// This helper is the M1 policy smoke, not an asset loader. Pack assets and their generated
/// shapes are M3/M4.
/// </para>
/// </remarks>
internal static class MessagePackSmokeSerializer
{
    private static readonly MessagePackSerializer _serializer = new();

    /// <summary>
    /// Serializes the smoke document to MessagePack bytes.
    /// </summary>
    /// <param name="value">The document to serialize.</param>
    /// <returns>The MessagePack payload.</returns>
    internal static byte[] Serialize(MessagePackSmokeData value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return _serializer.Serialize(value);
    }

    /// <summary>
    /// Deserializes a MessagePack smoke document.
    /// </summary>
    /// <param name="payload">The MessagePack payload.</param>
    /// <returns>The deserialized document.</returns>
    /// <exception cref="InvalidOperationException">The payload deserialized to <see langword="null"/>.</exception>
    internal static MessagePackSmokeData Deserialize(ReadOnlyMemory<byte> payload)
        => _serializer.Deserialize<MessagePackSmokeData>(payload)
            ?? throw new InvalidOperationException("The MessagePack smoke payload deserialized to null.");

    /// <summary>
    /// Serializes and deserializes the smoke document.
    /// </summary>
    /// <param name="value">The document to round-trip.</param>
    /// <returns>The deserialized document.</returns>
    internal static MessagePackSmokeData RoundTrip(MessagePackSmokeData value)
        => Deserialize(Serialize(value));
}
