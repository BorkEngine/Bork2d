using System.Text;

namespace Bork2d.Runtime.Sdl;

/// <summary>
/// Decodes the UTF-8 text carried by an <c>SDL_TEXTINPUT</c> event.
/// </summary>
/// <remarks>
/// SDL stores a null-terminated UTF-8 string in a fixed 32-byte event buffer. Decoding uses the
/// standard replacement fallback, so malformed input yields U+FFFD instead of throwing; a surrogate
/// pair becomes two UTF-16 <see cref="char"/> values.
/// </remarks>
internal static class SdlText
{
    /// <summary>The size of the SDL text event buffer (<c>SDL_TEXTINPUTEVENT_TEXT_SIZE</c>).</summary>
    internal const int BufferSize = 32;

    /// <summary>Decodes a null-terminated UTF-8 buffer.</summary>
    /// <param name="utf8">The raw buffer; content after the first null terminator is ignored.</param>
    /// <returns>The decoded text, or an empty string for an empty buffer.</returns>
    internal static string Decode(ReadOnlySpan<byte> utf8)
    {
        var terminator = utf8.IndexOf((byte)0);
        if (terminator >= 0)
        {
            utf8 = utf8[..terminator];
        }

        return utf8.IsEmpty ? string.Empty : Encoding.UTF8.GetString(utf8);
    }
}
