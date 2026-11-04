namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// One contiguous group of planned records that share one renderable kind and one resolved shader
/// entry inside a camera (draw run).
/// </summary>
/// <remarks>
/// <para>
/// Runs never reorder records: a new run starts only where two adjacent visible records differ in
/// <see cref="Kind"/> or resolved shader entry, so sprite and line draws interleave in draw order and
/// a sprite run and a line run never merge even when they resolve the same shader entry.
/// </para>
/// <para>
/// The meaning of <see cref="First"/> and <see cref="Count"/> depends on <see cref="Kind"/>. A sprite
/// run draws <c>vkCmdDraw(4, Count, 0, First)</c>, where <see cref="First"/> is the absolute
/// frame-buffer instance index and <see cref="Count"/> is the instance count. A line run draws
/// <c>vkCmdDrawIndexed(Count, 1, First, 0, 0)</c>, where <see cref="First"/> is the absolute first
/// index in the frame's line index buffer and <see cref="Count"/> is its index count (three per
/// triangle). A line run's indices are absolute vertex indices, so no vertex base is needed.
/// </para>
/// <para>
/// Every run owns one 144-byte dynamic global-uniform block;
/// <see cref="GlobalBlockOffset"/> is that block's byte offset, already aligned by the frame's block
/// stride.
/// </para>
/// </remarks>
/// <param name="Kind">The renderable kind the run draws.</param>
/// <param name="ShaderEntryId">The resolved shader entry identity that groups the run.</param>
/// <param name="First">
/// The first instance record for a sprite run, or the absolute first index for a line run.
/// </param>
/// <param name="Count">
/// The instance count for a sprite run, or the index count for a line run; always at least one.
/// </param>
/// <param name="GlobalBlockOffset">The run's global-uniform dynamic byte offset.</param>
internal readonly record struct DrawRun(
    DrawKind Kind,
    int ShaderEntryId,
    int First,
    int Count,
    uint GlobalBlockOffset);
