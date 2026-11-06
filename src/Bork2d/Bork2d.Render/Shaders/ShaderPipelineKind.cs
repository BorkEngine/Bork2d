namespace Bork2d.Render.Shaders;

/// <summary>
/// The engine vertex-stage kind a fragment shader is combined with.
/// </summary>
/// <remarks>
/// The fragment interface is shared between the kinds (shader ABI v3), but the vertex module, the
/// vertex input state, and the input-assembly topology differ. A shader catalog entry resolves its
/// pipeline per <see cref="ShaderPipelineKind"/>, so the same fragment content used by a sprite, a
/// line, and a font caches one graphics pipeline per kind while sharing the fragment module content.
/// <see cref="Font"/> is a pipeline-cache and run-identity kind: it uses the sprite vertex stage's
/// SPIR-V and the same per-frame sprite instance buffer, so a glyph is an ordinary instance record
/// with no dedicated glyph buffer, descriptor binding, or engine-owned vertex shader asset.
/// </remarks>
internal enum ShaderPipelineKind
{
    /// <summary>The sprite vertex stage: no vertex buffer, four generated quad corners per instance.</summary>
    Sprite,

    /// <summary>The line vertex stage: one 112-byte vertex record per triangle vertex.</summary>
    Line,

    /// <summary>
    /// The font glyph stage: the sprite vertex stage's SPIR-V and instance ABI, used as a distinct
    /// pipeline-cache kind so a shader assigned to a font and to a sprite caches two pipelines.
    /// </summary>
    Font,
}
