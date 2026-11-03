using System.Numerics;

namespace Bork2d.Engine.Assets;

/// <summary>
/// The engine-side shader layout contract shared by the engine, the renderer, and the bake tooling.
/// </summary>
/// <remarks>
/// <para>
/// The contract fixes the current layout version and the block sizes every custom fragment shader
/// layout is authored against. <see cref="CurrentVersion"/> is the only version this engine build
/// implements; a <see cref="ShaderAsset.LayoutVersion"/> that differs is rejected or falls back in
/// the renderer rather than being reinterpreted here.
/// </para>
/// <para>
/// Offsets in <see cref="ShaderUniform"/> are float-lane offsets. The instance block is 16 float
/// lanes, stored as four <see cref="Vector4"/> lanes; the global block is eight
/// <see cref="Vector4"/> slots. The lane helpers below read and write one component at a time and
/// silently ignore an out-of-range lane, so malformed metadata cannot throw or corrupt an adjacent
/// block.
/// </para>
/// </remarks>
internal static class ShaderLayout
{
    /// <summary>
    /// The shader layout version implemented by this engine build.
    /// </summary>
    internal const uint CurrentVersion = 1;

    /// <summary>
    /// The number of float lanes in the per-instance uniform block.
    /// </summary>
    internal const int InstanceBlockFloatCount = 16;

    /// <summary>
    /// The number of <see cref="Vector4"/> lanes in the per-instance uniform block.
    /// </summary>
    internal const int InstanceBlockVectorCount = InstanceBlockFloatCount / 4;

    /// <summary>
    /// The number of <see cref="Vector4"/> slots in the engine-wide global uniform block.
    /// </summary>
    internal const int GlobalBlockSlotCount = 8;

    /// <summary>
    /// Returns how many consecutive float lanes a declared uniform type occupies.
    /// </summary>
    /// <param name="type">The declared uniform type.</param>
    /// <returns>
    /// The lane count, or zero for an unknown numeric value. Zero makes an unknown type contribute
    /// nothing instead of throwing from the freeze path.
    /// </returns>
    internal static int ComponentCount(ShaderUniformType type) => type switch
    {
        ShaderUniformType.Float => 1,
        ShaderUniformType.Vector2 => 2,
        ShaderUniformType.Vector3 => 3,
        ShaderUniformType.Vector4 => 4,
        ShaderUniformType.Color => 4,
        _ => 0,
    };

    /// <summary>
    /// Reads one float lane from a <see cref="Vector4"/>-lane block.
    /// </summary>
    /// <param name="block">The block to read; its length times four is the lane count.</param>
    /// <param name="floatLane">The zero-based float-lane offset.</param>
    /// <param name="value">The lane value, or <c>0</c> when the lane is out of range.</param>
    /// <returns><see langword="true"/> when the lane exists; otherwise <see langword="false"/>.</returns>
    internal static bool TryReadComponent(
        ReadOnlySpan<Vector4> block,
        int floatLane,
        out float value)
    {
        if ((uint)floatLane >= (uint)(block.Length * 4))
        {
            value = 0f;
            return false;
        }

        value = GetComponent(block[floatLane >> 2], floatLane & 3);
        return true;
    }

    /// <summary>
    /// Writes one float lane in a <see cref="Vector4"/>-lane block.
    /// </summary>
    /// <param name="block">The block to write; its length times four is the lane count.</param>
    /// <param name="floatLane">The zero-based float-lane offset.</param>
    /// <param name="value">The value to store.</param>
    /// <returns><see langword="true"/> when the lane exists; otherwise <see langword="false"/>.</returns>
    internal static bool TryWriteComponent(Span<Vector4> block, int floatLane, float value)
    {
        if ((uint)floatLane >= (uint)(block.Length * 4))
        {
            return false;
        }

        ref Vector4 lane = ref block[floatLane >> 2];
        SetComponent(ref lane, floatLane & 3, value);
        return true;
    }

    /// <summary>
    /// Writes a uniform value into a <see cref="Vector4"/>-lane block at its declared float-lane
    /// offset.
    /// </summary>
    /// <remarks>
    /// Only the components the declared type covers are written. A negative offset, an offset whose
    /// value does not fit entirely in the block, and an unknown type write nothing; partially
    /// out-of-range lanes are never written.
    /// </remarks>
    /// <param name="block">The destination block.</param>
    /// <param name="uniform">The declaration that supplies the offset and type.</param>
    /// <param name="value">The value whose covered components are written.</param>
    internal static void WriteUniform(Span<Vector4> block, in ShaderUniform uniform, Vector4 value)
    {
        int count = ComponentCount(uniform.Type);
        int laneCount = block.Length * 4;
        if (count == 0 || uniform.Offset < 0 || uniform.Offset > laneCount - count)
        {
            return;
        }

        for (var component = 0; component < count; component++)
        {
            _ = TryWriteComponent(
                block,
                uniform.Offset + component,
                GetComponent(value, component));
        }
    }

    /// <summary>
    /// Reads a uniform value from a <see cref="Vector4"/>-lane block using its declared offset and
    /// type.
    /// </summary>
    /// <remarks>
    /// Components outside the block keep their zero default, so a malformed declaration yields a
    /// partially or fully zero value instead of throwing. An unknown type reads zero.
    /// </remarks>
    /// <param name="block">The source block.</param>
    /// <param name="uniform">The declaration that supplies the offset and type.</param>
    /// <returns>The value assembled from the declared float lanes.</returns>
    internal static Vector4 ReadUniform(ReadOnlySpan<Vector4> block, in ShaderUniform uniform)
    {
        int count = ComponentCount(uniform.Type);
        int laneCount = block.Length * 4;
        var value = Vector4.Zero;
        if (count == 0 || uniform.Offset < 0 || uniform.Offset > laneCount - count)
        {
            return value;
        }

        for (var component = 0; component < count; component++)
        {
            if (TryReadComponent(
                block,
                uniform.Offset + component,
                out float componentValue))
            {
                SetComponent(ref value, component, componentValue);
            }
        }

        return value;
    }

    private static float GetComponent(Vector4 value, int component) => component switch
    {
        0 => value.X,
        1 => value.Y,
        2 => value.Z,
        _ => value.W,
    };

    private static void SetComponent(ref Vector4 value, int component, float componentValue)
    {
        switch (component)
        {
            case 0:
                value.X = componentValue;
                break;
            case 1:
                value.Y = componentValue;
                break;
            case 2:
                value.Z = componentValue;
                break;
            default:
                value.W = componentValue;
                break;
        }
    }
}
