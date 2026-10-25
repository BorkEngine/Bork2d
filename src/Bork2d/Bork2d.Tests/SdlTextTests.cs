using System.Text;
using Bork2d.Runtime;
using Xunit;

namespace Bork2d.Tests;

/// <summary>
/// UTF-8 decoding for <c>SDL_TEXTINPUT</c>: null-terminated buffers, surrogate pairs, and malformed
/// input that must be replaced rather than thrown.
/// </summary>
public sealed class SdlTextTests
{
    [Fact]
    public void DecodesAsciiUpToTheNullTerminator()
    {
        var buffer = new byte[SdlText.BufferSize];
        buffer[0] = (byte)'a';
        buffer[1] = (byte)'b';
        buffer[2] = (byte)'c';

        Assert.Equal("abc", SdlText.Decode(buffer));
    }

    [Fact]
    public void IgnoresBytesAfterTheFirstNullTerminator()
    {
        var buffer = new byte[SdlText.BufferSize];
        buffer[0] = (byte)'x';
        buffer[1] = 0;
        buffer[2] = (byte)'y';

        Assert.Equal("x", SdlText.Decode(buffer));
    }

    [Fact]
    public void EmptyBufferDecodesToEmptyString()
    {
        Assert.Equal(string.Empty, SdlText.Decode(ReadOnlySpan<byte>.Empty));
        Assert.Equal(string.Empty, SdlText.Decode(new byte[SdlText.BufferSize]));
    }

    [Fact]
    public void DecodesASurrogatePairAsTwoChars()
    {
        var utf8 = Encoding.UTF8.GetBytes("😀");

        var text = SdlText.Decode(utf8);

        Assert.Equal("😀", text);
        Assert.Equal(2, text.Length);
        Assert.True(char.IsHighSurrogate(text[0]));
        Assert.True(char.IsLowSurrogate(text[1]));
    }

    [Fact]
    public void MalformedUtf8IsReplacedInsteadOfThrowing()
    {
        byte[] malformed = [(byte)'a', 0xFF, (byte)'b'];

        var text = SdlText.Decode(malformed);

        Assert.Equal("a\uFFFDb", text);
    }
}
