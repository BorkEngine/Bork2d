using System.Numerics;
using System.Runtime.InteropServices;
using Bork2d.Render.Sprites;
using Xunit;

namespace Bork2d.Tests.Render.Sprites;

/// <summary>
/// Deterministic tests for the std140 global uniform block shared by every custom fragment shader:
/// nine <c>vec4</c> values, 144 bytes with no padding.
/// </summary>
public sealed class GlobalUniformBlockTests
{
    [Fact]
    public void BlockHasTheApprovedNineVector144ByteLayout()
    {
        Assert.Equal(144, GlobalUniformBlock.SizeInBytes);
        Assert.Equal(9, GlobalUniformBlock.VectorCount);
        Assert.Equal(8, GlobalUniformBlock.GlobalSlotCount);
        Assert.Equal(144, Marshal.SizeOf<GlobalUniformBlock>());
        Assert.Equal(0, OffsetOf(nameof(GlobalUniformBlock.Time)));
        Assert.Equal(16, OffsetOf(nameof(GlobalUniformBlock.Global0)));
        Assert.Equal(32, OffsetOf(nameof(GlobalUniformBlock.Global1)));
        Assert.Equal(48, OffsetOf(nameof(GlobalUniformBlock.Global2)));
        Assert.Equal(64, OffsetOf(nameof(GlobalUniformBlock.Global3)));
        Assert.Equal(80, OffsetOf(nameof(GlobalUniformBlock.Global4)));
        Assert.Equal(96, OffsetOf(nameof(GlobalUniformBlock.Global5)));
        Assert.Equal(112, OffsetOf(nameof(GlobalUniformBlock.Global6)));
        Assert.Equal(128, OffsetOf(nameof(GlobalUniformBlock.Global7)));
        Assert.Equal(0, GlobalUniformBlock.TimeOffset);
        Assert.Equal(16, GlobalUniformBlock.GlobalsOffset);
    }

    [Fact]
    public void DefaultBlockHasZeroTimeAndZeroUserSlots()
    {
        var block = new GlobalUniformBlock();

        Assert.Equal(Vector4.Zero, block.Time);
        Assert.Equal(Vector4.Zero, block.Global0);
        Assert.Equal(Vector4.Zero, block.Global7);
    }

    private static int OffsetOf(string field)
        => (int)Marshal.OffsetOf<GlobalUniformBlock>(field);
}
