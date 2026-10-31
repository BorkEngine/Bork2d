using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// Deterministic XXH3-128 hashing for the incremental bake manifest.
/// </summary>
/// <remarks>
/// <para>
/// Digests are the 16 raw bytes rendered as 32 uppercase hex characters with
/// <see cref="Convert.ToHexString(byte[])"/>. Multi-field hashes frame every field with an explicit
/// 8-byte little-endian length before its bytes, so concatenating different field boundaries cannot
/// produce the same digest. Callers normalize and sort paths before calling
/// <see cref="HashFields(ReadOnlyMemory{byte}[])"/>; the helper never reorders fields.
/// </para>
/// <para>
/// There is no manifest IO or skip logic here (WP5); this is the hash primitive only.
/// </para>
/// </remarks>
internal static class BakeHash
{
    private const int BufferSize = 64 * 1024;

    /// <summary>Hashes a byte span.</summary>
    /// <param name="data">The bytes to hash.</param>
    /// <returns>The 32-character uppercase hex digest.</returns>
    internal static string Hash(ReadOnlySpan<byte> data)
        => Convert.ToHexString(XxHash128.Hash(data));

    /// <summary>Hashes a UTF-8 string.</summary>
    /// <param name="value">The text to hash.</param>
    /// <returns>The 32-character uppercase hex digest.</returns>
    internal static string HashText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Hash(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>Hashes ordered fields with explicit length framing.</summary>
    /// <param name="fields">The fields, already in their deterministic order.</param>
    /// <returns>The 32-character uppercase hex digest.</returns>
    internal static string HashFields(params ReadOnlyMemory<byte>[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var hash = new XxHash128();
        Span<byte> length = stackalloc byte[sizeof(long)];
        foreach (ReadOnlyMemory<byte> field in fields)
        {
            BinaryPrimitives.WriteInt64LittleEndian(length, field.Length);
            hash.Append(length);
            hash.Append(field.Span);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>Hashes ordered UTF-8 text fields with explicit length framing.</summary>
    /// <param name="fields">The fields, already in their deterministic order.</param>
    /// <returns>The 32-character uppercase hex digest.</returns>
    internal static string HashTextFields(params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        ReadOnlyMemory<byte>[] encoded = new ReadOnlyMemory<byte>[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(fields[i]);
            encoded[i] = Encoding.UTF8.GetBytes(fields[i]);
        }

        return HashFields(encoded);
    }

    /// <summary>Hashes a file's contents without buffering the whole file.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The 32-character uppercase hex digest.</returns>
    internal static string HashFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan);
        var hash = new XxHash128();
        byte[] buffer = new byte[BufferSize];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            hash.Append(buffer.AsSpan(0, read));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
