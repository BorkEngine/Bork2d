using System.Numerics;
using Bork2d.Engine.Assets;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// One stored engine-wide user shader uniform: its validated type and current four-component value.
/// </summary>
/// <remarks>
/// A name keeps exactly one type while it is stored. The engine stores this value under its
/// dedicated global-uniform lock and copies it into a <see cref="GlobalUniformSnapshot"/> when a
/// frame is captured.
/// </remarks>
internal readonly struct GlobalUniformValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalUniformValue"/> struct.
    /// </summary>
    /// <param name="type">The validated uniform type assigned to the name.</param>
    /// <param name="value">The current four-component value.</param>
    internal GlobalUniformValue(ShaderUniformType type, Vector4 value)
    {
        Type = type;
        Value = value;
    }

    /// <summary>Gets the validated uniform type assigned to the name.</summary>
    internal ShaderUniformType Type { get; }

    /// <summary>Gets the current four-component value.</summary>
    internal Vector4 Value { get; }
}
