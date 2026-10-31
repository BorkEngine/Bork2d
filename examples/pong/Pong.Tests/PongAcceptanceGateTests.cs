using Bork2d.Engine.Rendering;
using Pong.Desktop;
using Xunit;

namespace Pong.Tests;

/// <summary>
/// The M8 short-vs-long Pong acceptance decision (M8 §10/§13): the 120-tick runs assert validation
/// and content only, while the documented long run asserts every draw/statistics relationship.
/// </summary>
public sealed class PongAcceptanceGateTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(120, false)]
    [InlineData(999, false)]
    [InlineData(1000, true)]
    [InlineData(20000, true)]
    public void TheThresholdSplitsShortAndLongRuns(int completedTicks, bool expected)
    {
        Assert.Equal(expected, PongAcceptanceGate.IsLongRun(completedTicks));
    }

    [Fact]
    public void AShortRunDoesNotRequireDrawCounterRelationships()
    {
        IReadOnlyList<string> failures = PongAcceptanceGate.Validate(Stats(), completedTicks: 120);

        Assert.Empty(failures);
    }

    [Fact]
    public void AShortRunStillRequiresZeroValidation()
    {
        IReadOnlyList<string> failures = PongAcceptanceGate.Validate(
            Stats(validationErrors: 2, validationWarnings: 1),
            completedTicks: 120);

        Assert.Equal(2, failures.Count);
        Assert.Contains(failures, failure => failure.Contains("validation errors", StringComparison.Ordinal));
        Assert.Contains(
            failures,
            failure => failure.Contains("validation warnings", StringComparison.Ordinal));
    }

    [Fact]
    public void ALongRunSatisfiesTheDrawStatisticsRelationships()
    {
        IReadOnlyList<string> failures = PongAcceptanceGate.Validate(Stats(), completedTicks: 20000);

        Assert.Empty(failures);
    }

    [Fact]
    public void ALongRunRejectsEveryMissingDrawStatisticsRelationship()
    {
        IReadOnlyList<string> failures = PongAcceptanceGate.Validate(
            Stats(cameras: 0, nonEmpty: 0, draws: 0, instances: 0, pages: 1),
            completedTicks: 20000);

        Assert.Equal(5, failures.Count);
        Assert.Contains(failures, failure => failure.Contains("camera must be captured", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("camera must draw", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("at least one draw", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("three Pong sprites", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("texture pages", StringComparison.Ordinal));
    }

    [Fact]
    public void ALongRunRequiresDrawCallsToEqualNonEmptyCameras()
    {
        IReadOnlyList<string> failures = PongAcceptanceGate.Validate(
            Stats(cameras: 2, draws: 2, nonEmpty: 1),
            completedTicks: 20000);

        Assert.Single(failures);
        Assert.Contains("DrawCalls must equal NonEmptyCameras", failures[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ALongRunRequiresDrawCallsNotToExceedCameras()
    {
        IReadOnlyList<string> failures = PongAcceptanceGate.Validate(
            Stats(cameras: 0, nonEmpty: 1, draws: 1),
            completedTicks: 20000);

        Assert.Equal(2, failures.Count);
        Assert.Contains(failures, failure => failure.Contains("camera must be captured", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("never exceed Cameras", StringComparison.Ordinal));
    }

    [Fact]
    public void ALongRunStillRequiresZeroValidation()
    {
        IReadOnlyList<string> failures = PongAcceptanceGate.Validate(
            Stats(validationErrors: 1),
            completedTicks: 20000);

        Assert.Single(failures);
        Assert.Contains("validation errors", failures[0], StringComparison.Ordinal);
    }

    private static RenderStats Stats(
        int cameras = 1,
        int nonEmpty = 1,
        int draws = 1,
        int instances = 3,
        int pages = 3,
        long validationErrors = 0,
        long validationWarnings = 0)
        => new(
            FrameSequence: 1,
            FramebufferWidth: 1280,
            FramebufferHeight: 720,
            DrawCalls: draws,
            Instances: instances,
            Cameras: cameras,
            NonEmptyCameras: nonEmpty,
            TexturePages: pages,
            TexturePageCapacity: 256,
            SkippedSprites: 0,
            ValidationErrors: validationErrors,
            ValidationWarnings: validationWarnings);
}
