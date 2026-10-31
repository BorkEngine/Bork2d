using Bork2d.Render.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Deterministic tests for the per-frame capture state machine (M8 §6.9/§6.12): a request may only be
/// completed when the copy was recorded <em>and</em> the frame was submitted; a recorded-but-not-
/// submitted frame is the fatal/stop edge that must fault instead of returning uninitialized bytes.
/// </summary>
public sealed class FrameCaptureStateTests
{
    [Fact]
    public void ABeginFrameCannotComplete()
    {
        var state = new FrameCaptureState();

        state.Begin();

        Assert.False(state.Recorded);
        Assert.False(state.Submitted);
        Assert.False(state.CanComplete);
        Assert.False(state.RecordedButNotSubmitted);
    }

    [Fact]
    public void ARecordedButNotSubmittedFrameMustFaultItsRequests()
    {
        var state = new FrameCaptureState();
        state.Begin();
        state.MarkRecorded();

        Assert.True(state.Recorded);
        Assert.False(state.Submitted);
        Assert.False(state.CanComplete);
        Assert.True(state.RecordedButNotSubmitted);
    }

    [Fact]
    public void ARecordedAndSubmittedFrameCanComplete()
    {
        var state = new FrameCaptureState();
        state.Begin();
        state.MarkRecorded();
        state.MarkSubmitted();

        Assert.True(state.CanComplete);
        Assert.False(state.RecordedButNotSubmitted);
    }

    [Fact]
    public void ResetAndBeginClearTheFatalEdge()
    {
        var state = new FrameCaptureState();
        state.Begin();
        state.MarkRecorded();
        state.Reset();

        Assert.False(state.CanComplete);
        Assert.False(state.RecordedButNotSubmitted);

        state.Begin();
        state.MarkRecorded();
        state.MarkSubmitted();
        state.Begin();

        Assert.False(state.CanComplete);
        Assert.False(state.RecordedButNotSubmitted);
    }
}
