namespace Bork2d.Render.Vulkan;

/// <summary>
/// The per-frame capture state machine: a request may only be completed with GPU-written bytes when
/// the image-to-buffer copy was recorded <em>and</em> the command buffer was submitted.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Recorded"/> is set while the frame command buffer is being recorded;
/// <see cref="Submitted"/> is set only after the queue submission succeeds. A frame that recorded a
/// copy but failed before submission (a fatal during recording or a failed submit) must fault its
/// attached requests instead of copying staging memory the GPU never wrote.
/// </para>
/// <para>
/// The struct is pure, so the fatal/stop edge is covered deterministically while the renderer uses
/// the same transitions around its Vulkan calls.
/// </para>
/// </remarks>
internal struct FrameCaptureState
{
    /// <summary>Gets a value indicating whether the image-to-buffer copy was recorded.</summary>
    internal bool Recorded { get; private set; }

    /// <summary>Gets a value indicating whether the command buffer was successfully submitted.</summary>
    internal bool Submitted { get; private set; }

    /// <summary>Gets a value indicating whether the staging bytes are GPU-written and safe to copy.</summary>
    internal readonly bool CanComplete => Recorded && Submitted;

    /// <summary>
    /// Gets a value indicating whether a copy was recorded but its frame was never submitted.
    /// </summary>
    internal readonly bool RecordedButNotSubmitted => Recorded && !Submitted;

    /// <summary>Starts a new frame's capture recording, clearing any previous state.</summary>
    internal void Begin()
    {
        Recorded = false;
        Submitted = false;
    }

    /// <summary>Records that the image-to-buffer copy was recorded into the command buffer.</summary>
    internal void MarkRecorded() => Recorded = true;

    /// <summary>Records that the command buffer was submitted successfully.</summary>
    internal void MarkSubmitted() => Submitted = true;

    /// <summary>Clears the state after the attached requests completed or faulted.</summary>
    internal void Reset()
    {
        Recorded = false;
        Submitted = false;
    }
}
