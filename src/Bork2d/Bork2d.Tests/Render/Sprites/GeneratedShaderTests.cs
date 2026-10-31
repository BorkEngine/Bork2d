using System.Buffers.Binary;
using Bork2d.Render.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Sprites;

/// <summary>
/// Proves the build-time GLSL compiler's generated C# is linked into <c>Bork2d.Render</c> without a
/// runtime shader file or compiler (M8 §6.7/§8.1).
/// </summary>
public sealed class GeneratedShaderTests
{
    [Fact]
    public void GeneratedModulesAreSpirV()
    {
        AssertSpirV(SpriteShaderSpirV.Vertex, "vertex");
        AssertSpirV(SpriteShaderSpirV.Fragment, "fragment");
        Assert.NotEqual(SpriteShaderSpirV.Vertex.Length, SpriteShaderSpirV.Fragment.Length);
    }

    private static void AssertSpirV(byte[] module, string stage)
    {
        Assert.True(module.Length >= 20, $"The {stage} module is too short.");
        Assert.Equal(0, module.Length % 4);
        Assert.Equal(0x07230203u, BinaryPrimitives.ReadUInt32LittleEndian(module));
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(module.AsSpan(4));
        Assert.Equal(1u, version >> 16);
        Assert.True((version & 0xFF00u) >= 0x0300u, $"The {stage} module targets SPIR-V below 1.3.");
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(module.AsSpan(8)) > 0);
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(module.AsSpan(12)) > 0);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(module.AsSpan(16)));
    }
}
