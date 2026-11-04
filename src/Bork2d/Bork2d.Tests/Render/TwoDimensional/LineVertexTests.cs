using System.Numerics;
using System.Runtime.InteropServices;
using Bork2d.Render.TwoDimensional;
using Xunit;

namespace Bork2d.Tests.Render.TwoDimensional;

/// <summary>
/// Deterministic tests for the 112-byte line vertex record and its mapped-memory writer: the stride,
/// the seven field offsets, the 28-float layout, and the destination guard.
/// </summary>
public sealed class LineVertexTests
{
    [Fact]
    public void LineVertexIsExactly28FloatsAnd112Bytes()
    {
        Assert.Equal(112, LineVertex.SizeInBytes);
        Assert.Equal(28, LineVertex.FloatCount);
        Assert.Equal(112, Marshal.SizeOf<LineVertex>());
        Assert.Equal(LineVertex.SizeInBytes, LineVertex.FloatCount * sizeof(float));
    }

    [Fact]
    public void LineVertexFieldOffsetsMatchTheShaderAttributeLocations()
    {
        Assert.Equal(0, LineVertex.PositionOffset);
        Assert.Equal(8, LineVertex.LineUvOffset);
        Assert.Equal(16, LineVertex.ColorOffset);
        Assert.Equal(32, LineVertex.MiscOffset);
        Assert.Equal(48, LineVertex.Params0Offset);
        Assert.Equal(64, LineVertex.Params1Offset);
        Assert.Equal(80, LineVertex.Params2Offset);
        Assert.Equal(96, LineVertex.Params3Offset);
    }

    [Fact]
    public void WriterWritesPositionLineUvColorMiscAndParamsInOrder()
    {
        var vertex = new LineVertex
        {
            Position = new Vector2(1f, 2f),
            LineUv = new Vector2(3f, 4f),
            Color = new Vector4(5f, 6f, 7f, 8f),
            Misc = new Vector4(9f, 10f, 11f, 12f),
            Params0 = new Vector4(13f, 14f, 15f, 16f),
            Params1 = new Vector4(17f, 18f, 19f, 20f),
            Params2 = new Vector4(21f, 22f, 23f, 24f),
            Params3 = new Vector4(25f, 26f, 27f, 28f),
        };
        var destination = new byte[LineVertex.SizeInBytes];

        LineVertexWriter.Write(destination, in vertex);

        float[] floats = MemoryMarshal.Cast<byte, float>(destination).ToArray();
        Assert.Equal(Enumerable.Range(1, 28).Select(value => (float)value), floats);
    }

    [Fact]
    public void WriterRejectsAShortDestination()
    {
        var vertex = default(LineVertex);
        var destination = new byte[LineVertex.SizeInBytes - 1];

        Assert.Throws<ArgumentOutOfRangeException>(
            () => LineVertexWriter.Write(destination, in vertex));
    }
}
