namespace Bork2d.Render.Shaders;

/// <summary>
/// The engine vertex-stage kind a fragment shader is combined with.
/// </summary>
/// <remarks>
/// The fragment interface is shared between the two kinds (shader ABI v3), but the vertex module,
/// the vertex input state, and the input-assembly topology differ. A shader catalog entry resolves
/// its pipeline per <see cref="ShaderPipelineKind"/>, so the same fragment content used by a sprite
/// and a line caches two graphics pipelines while sharing the fragment module content.
/// </remarks>
internal enum ShaderPipelineKind
{
    /// <summary>The sprite vertex stage: no vertex buffer, four generated quad corners per instance.</summary>
    Sprite,

    /// <summary>The line vertex stage: one 112-byte vertex record per triangle vertex.</summary>
    Line,
}
