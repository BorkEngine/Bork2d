namespace Bork2d.Render.Sprites;

/// <summary>
/// One contiguous group of submitted instances that share one resolved shader entry inside a camera
/// (shader draw run).
/// </summary>
/// <remarks>
/// Runs never reorder sprites: a new run starts only where two adjacent visible sprites resolve to
/// different entries. <see cref="FirstInstance"/> is the absolute frame-buffer record index, so a
/// run's draw is <c>vkCmdDraw(4, InstanceCount, 0, FirstInstance)</c>. Every run also owns one
/// 144-byte dynamic global-uniform block; <see cref="GlobalBlockOffset"/> is that block's byte
/// offset, already aligned by the frame's block stride.
/// </remarks>
/// <param name="FirstInstance">The first instance record for the run in the frame buffer.</param>
/// <param name="InstanceCount">The number of records the run draws; always at least one.</param>
/// <param name="ShaderEntryId">The resolved shader entry identity that groups the run.</param>
/// <param name="GlobalBlockOffset">The run's global-uniform dynamic byte offset.</param>
internal readonly record struct ShaderRun(
    int FirstInstance,
    int InstanceCount,
    int ShaderEntryId,
    uint GlobalBlockOffset);
