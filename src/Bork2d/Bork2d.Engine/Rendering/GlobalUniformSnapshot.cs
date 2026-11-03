using System.Numerics;
using Bork2d.Engine.Assets;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// One value-only user global shader uniform captured into a frame snapshot.
/// </summary>
/// <param name="Name">
/// The engine-wide uniform name, matched ordinally. The string is a snapshot reference owned by the
/// engine and is released when the snapshot is reset.
/// </param>
/// <param name="Type">The validated uniform type the engine assigned to the name.</param>
/// <param name="Value">The captured four-component value; components beyond the type are ignored.</param>
/// <remarks>
/// A global uniform is user state, not a renderable producer slot. It is captured after the scene
/// walk from the engine-wide value map and cleared when the snapshot is reused, so no snapshot
/// retains a stale name or value.
/// </remarks>
internal readonly record struct GlobalUniformSnapshot(
    string Name,
    ShaderUniformType Type,
    Vector4 Value);
