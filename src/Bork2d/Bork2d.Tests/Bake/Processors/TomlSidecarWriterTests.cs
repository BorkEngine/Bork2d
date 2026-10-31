using Bork2d.Bake.Toml;
using PolyType;
using Xunit;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// The canonical sidecar writer: shape-order keys, camelCase and attribute naming, ignored members,
/// skipped nulls, enum member names, deterministic LF output, and unsupported-member rejection.
/// </summary>
public sealed class TomlSidecarWriterTests
{
    [Fact]
    public void WritesScalarSettingsInShapeOrderWithCamelCaseAndEnumNames()
    {
        var sidecar = new TestSidecar
        {
            Value = 7,
            Mode = TestSidecarMode.Slow,
            Label = null,
            Ignored = "not written",
        };

        Assert.True(
            TomlSidecarWriter.TryWrite(ShapeOf<TestSidecar>(), sidecar, out string text, out string? reason),
            reason);

        Assert.Equal("value = 7\nmode = \"slow\"\n", text);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public void HonorsJsonPropertyNameAndSkipsNullOptionals()
    {
        var sidecar = new TestSidecar
        {
            Value = 1,
            Mode = TestSidecarMode.Fast,
            Label = "hello",
        };

        Assert.True(
            TomlSidecarWriter.TryWrite(ShapeOf<TestSidecar>(), sidecar, out string text, out _));

        Assert.Equal("value = 1\nmode = \"fast\"\ndisplay = \"hello\"\n", text);
    }

    [Fact]
    public void OutputIsDeterministic()
    {
        var sidecar = new TestSidecar { Value = 3, Mode = TestSidecarMode.Fast };

        Assert.True(TomlSidecarWriter.TryWrite(ShapeOf<TestSidecar>(), sidecar, out string first, out _));
        Assert.True(TomlSidecarWriter.TryWrite(ShapeOf<TestSidecar>(), sidecar, out string second, out _));
        Assert.Equal(first, second);
    }

    [Fact]
    public void KeysThatAreNotBareTomlKeysAreQuoted()
    {
        var sidecar = new QuotedKeySidecar { Text = "hello" };

        Assert.True(
            TomlSidecarWriter.TryWrite(
                ShapeOf<QuotedKeySidecar>(),
                sidecar,
                out string text,
                out string? reason),
            reason);

        Assert.Equal("\"display name\" = \"hello\"\n", text);
    }

    [Fact]
    public void WritesNonNullNullableScalarAndEnumMembers()
    {
        var sidecar = new NullableSidecar { Count = 7, Mode = TestSidecarMode.Slow };

        Assert.True(
            TomlSidecarWriter.TryWrite(
                ShapeOf<NullableSidecar>(),
                sidecar,
                out string text,
                out string? reason),
            reason);

        Assert.Equal("count = 7\nmode = \"slow\"\n", text);
    }

    [Fact]
    public void SkipsAbsentNullableMembers()
    {
        var sidecar = new NullableSidecar { Count = 3 };

        Assert.True(
            TomlSidecarWriter.TryWrite(
                ShapeOf<NullableSidecar>(),
                sidecar,
                out string text,
                out string? reason),
            reason);

        Assert.Equal("count = 3\n", text);
    }

    [Fact]
    public void UnsupportedMemberTypesFail()
    {
        var sidecar = new UnsupportedSidecar { Values = [1, 2] };

        Assert.False(
            TomlSidecarWriter.TryWrite(ShapeOf<UnsupportedSidecar>(), sidecar, out string text, out string? reason));
        Assert.Equal(string.Empty, text);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    private static ITypeShape ShapeOf<T>()
        where T : IShapeable<T>
        => T.GetTypeShape();
}
